#pragma warning disable CS1591

using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.SmartTags;
using OpenSorSe.Core.Platform;
using OpenSorSe.Executor;
using OpenSorSe.Executor.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Workflows;

/// <summary>
/// Reuses Sorting Recipes to create bounded actual-file previews from durable indexed identities.
/// Preview is read-only; only an unchanged explicitly approved preview reaches <see cref="IChangePlanFactory"/>.
/// </summary>
public sealed class ReviewedOrganizationService : IReviewedOrganizationService
{
    private const string ManualProfileId = "manual:reviewed-organization";
    private const string ManualProfileName = "Reviewed organization";
    private readonly IReviewedOrganizationEvidenceSource _indexing;
    private readonly IWorkflowTemplateEngine _templates;
    private readonly IChangePlanFactory _changePlans;
    private readonly IPathSemantics _paths;

    public ReviewedOrganizationService(
        IReviewedOrganizationEvidenceSource indexing,
        IWorkflowTemplateEngine templates,
        IChangePlanFactory changePlans,
        IPathSemantics? paths = null)
    {
        _indexing = indexing ?? throw new ArgumentNullException(nameof(indexing));
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _changePlans = changePlans ?? throw new ArgumentNullException(nameof(changePlans));
        _paths = paths ?? PlatformServices.CurrentPathSemantics;
    }

    public async Task<OrganizationProposalSet> PreviewAsync(
        OrganizationPreviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Recipe);
        ArgumentNullException.ThrowIfNull(request.SelectedFileIds);
        var selectedIds = request.SelectedFileIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (selectedIds.Length == 0)
        {
            throw new ArgumentException("Select at least one indexed file.", nameof(request));
        }

        if (selectedIds.Length > WorkflowLibraryLimits.MaximumOrganizationSelection)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"Organization preview supports at most {WorkflowLibraryLimits.MaximumOrganizationSelection} selected files.");
        }

        // Reviewed Organization never converts formats. Legacy recipes may retain an advanced
        // PreserveExtension setting, but this product workflow always preserves the source suffix.
        var recipe = request.Recipe with { PreserveExtension = true };
        var recipeValidation = _templates.ValidateRecipeTemplates(recipe);
        if (!recipeValidation.IsValid)
        {
            throw new ArgumentException(
                recipeValidation.Issues.First(issue => issue.IsBlocking).Message,
                nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var documents = await _indexing
            .GetDocumentsByIdsAsync(selectedIds, cancellationToken)
            .ConfigureAwait(false);
        var documentsById = documents
            .GroupBy(document => document.FileId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var sourceIds = documents
            .Select(document => document.SourceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (sourceIds.Length != 1 || documents.Any(document => string.IsNullOrWhiteSpace(document.SourceId)))
        {
            throw new InvalidOperationException(
                "The selected files must belong to one current indexed source. Cross-root organization is not supported.");
        }

        var sources = await _indexing.GetSourcesAsync(cancellationToken).ConfigureAwait(false);
        var source = sources.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, sourceIds[0], StringComparison.Ordinal));
        if (source is null)
        {
            throw new InvalidOperationException(
                "The indexed source for this selection is no longer registered. Refresh discovery before organizing files.");
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source.RootPath));
        var rows = new List<OrganizationProposalRow>(selectedIds.Length);
        var availability = ProductTokens.ToDictionary(token => token, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var fileId in selectedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!documentsById.TryGetValue(fileId, out var document))
            {
                rows.Add(CannotResolve(fileId));
                continue;
            }

            if (!_paths.IsWithinRoot(root, document.FullPath))
            {
                rows.Add(CannotResolve(
                    fileId,
                    document.FullPath,
                    "The indexed path is outside the selected source root."));
                continue;
            }

            var evidence = BuildEvidence(document, out var evidenceWarnings);
            foreach (var token in ProductTokens.Where(evidence.ContainsKey))
            {
                availability[token]++;
            }

            if (!Applies(recipe, document))
            {
                rows.Add(CannotResolve(
                    fileId,
                    document.FullPath,
                    "This recipe does not apply to the file type or category."));
                continue;
            }

            var evaluation = _templates.Evaluate(
                recipe,
                new RecipeEvaluationContext(root, document.FullPath, evidence));
            rows.Add(CreateRow(document, root, evaluation, evidence, evidenceWarnings));
        }

        MarkProposalCollisions(rows);
        var projectedFileActions = rows.Count(row => row.IsEligible);
        var projectedDirectories = CountRequiredDirectories(root, rows.Where(row => row.IsEligible));
        var warnings = new List<string>();
        if (rows.Any(row => row.Readiness == OrganizationProposalReadiness.CannotPropose))
        {
            warnings.Add("Every selected file must have a safe proposal before Review Changes. Edit the recipe or reduce the selection.");
        }

        if (projectedFileActions + projectedDirectories > ChangePlanSchema.MaximumActions)
        {
            warnings.Add(
                $"The preview requires {projectedFileActions + projectedDirectories} Change Plan actions; the safe limit is {ChangePlanSchema.MaximumActions}. Reduce the selection or destination depth.");
        }

        var coverage = ProductTokens.Select(token => new OrganizationEvidenceCoverage(
            token,
            ProductTokenDisplayNames[token],
            availability[token],
            selectedIds.Length)).ToArray();
        var sensitive = rows.SelectMany(row => row.Evidence).Any(item => item.IsSensitive);
        var fingerprint = Fingerprint(recipe, root, source.Id, selectedIds, rows);
        return new OrganizationProposalSet(
            $"organization-preview:{Guid.NewGuid():N}",
            recipe,
            root,
            source.Id,
            Array.AsReadOnly(selectedIds),
            Array.AsReadOnly(rows.ToArray()),
            Array.AsReadOnly(coverage),
            projectedFileActions,
            projectedDirectories,
            Array.AsReadOnly(warnings.ToArray()),
            sensitive,
            fingerprint);
    }

    public async Task<ChangePlan> CreateChangePlanAsync(
        OrganizationProposalSet proposal,
        string sourceContextId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceContextId);
        if (!proposal.CanCreateChangePlan)
        {
            throw new InvalidOperationException(
                "The organization preview contains unresolved, conflicting, unchanged, or over-bound rows. Refresh it before Review Changes.");
        }

        var fresh = await PreviewAsync(
            new OrganizationPreviewRequest(proposal.Recipe, proposal.SelectedFileIds),
            cancellationToken).ConfigureAwait(false);
        if (!fresh.CanCreateChangePlan ||
            !string.Equals(fresh.Fingerprint, proposal.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The organization preview is stale because files, evidence, destinations, or recipe inputs changed. Preview again before Review Changes.");
        }

        var proposals = fresh.Rows.Select((row, index) =>
        {
            var target = row.TargetPath!;
            var currentDirectory = Path.GetDirectoryName(row.CurrentPath);
            var targetDirectory = Path.GetDirectoryName(target);
            var sameDirectory = currentDirectory is not null &&
                                targetDirectory is not null &&
                                _paths.PathsEqual(currentDirectory, targetDirectory);
            var values = row.Evidence.ToDictionary(
                item => item.Token,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase);
            var sources = row.Evidence.Select(item => item.EvidenceSource)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var provenance = new ChangeWorkflowProvenance(
                ManualProfileId,
                ManualProfileName,
                1,
                fresh.Recipe.Id,
                fresh.Recipe.Name,
                fresh.Recipe.Revision,
                new ReadOnlyDictionary<string, string>(values),
                Array.AsReadOnly(sources),
                false,
                row.Warnings,
                row.MissingEvidence);
            return new ChangeActionProposal(
                sameDirectory ? ChangeActionType.RenameFile : ChangeActionType.MoveFile,
                row.CurrentPath,
                target,
                ChangeSuggestionSource.DeterministicRule,
                $"Reviewed organization recipe \"{fresh.Recipe.Name}\" r{fresh.Recipe.Revision} using trusted local evidence.",
                index + 1,
                row.FileId,
                row.SourceLength,
                row.SourceModifiedAtUtc)
            {
                WorkflowProvenance = provenance,
            };
        }).ToArray();

        return await _changePlans.CreateAsync(
            new ChangePlanCreationRequest(
                fresh.OrganizationRoot,
                sourceContextId,
                Array.AsReadOnly(proposals),
                fresh.Warnings),
            cancellationToken).ConfigureAwait(false);
    }

    private OrganizationProposalRow CreateRow(
        ProgressiveSearchDocument document,
        string root,
        RecipeEvaluationResult evaluation,
        IReadOnlyDictionary<string, RecipeFieldValue> values,
        IReadOnlyList<string> evidenceWarnings)
    {
        var conflicts = evaluation.Conflicts.ToList();
        var warnings = evidenceWarnings
            .Concat(evaluation.Warnings)
            .Concat(evaluation.SanitizationChanges)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var target = evaluation.ProposedDestinationPath;
        if (!File.Exists(document.FullPath))
        {
            conflicts.Add("The indexed source file is no longer available.");
        }

        if (target is not null && !_paths.PathsEqual(target, document.FullPath))
        {
            if (File.Exists(target) || Directory.Exists(target))
            {
                conflicts.Add("The proposed target is already occupied; overwrite is never allowed.");
            }
        }
        else if (target is not null)
        {
            conflicts.Add("The recipe leaves this file unchanged.");
        }

        var mappings = evaluation.ValuesUsed
            .Where(pair => values.ContainsKey(pair.Key))
            .Select(pair => new OrganizationEvidenceMapping(
                $"{{{pair.Key}}}",
                pair.Value,
                values[pair.Key].EvidenceSource,
                pair.Key.Equals("theme", StringComparison.OrdinalIgnoreCase) ||
                pair.Key.Equals("documentType", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.Token, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var readiness = conflicts.Count > 0 || !evaluation.IsValid
            ? OrganizationProposalReadiness.CannotPropose
            : evaluation.FallbackValues.Count > 0 || warnings.Count > 0
                ? OrganizationProposalReadiness.NeedsReview
                : OrganizationProposalReadiness.Reliable;
        var relativeDestination = target is null
            ? null
            : Path.GetRelativePath(root, Path.GetDirectoryName(target) ?? root);
        return new OrganizationProposalRow(
            document.FileId,
            document.FullPath,
            evaluation.ProposedFileName,
            relativeDestination is "." ? string.Empty : relativeDestination,
            target,
            readiness,
            Array.AsReadOnly(mappings),
            evaluation.MissingValues,
            evaluation.FallbackValues,
            Array.AsReadOnly(warnings.ToArray()),
            Array.AsReadOnly(conflicts.Distinct(StringComparer.Ordinal).ToArray()),
            document.Length,
            document.ModifiedTimeUtc);
    }

    private static IReadOnlyDictionary<string, RecipeFieldValue> BuildEvidence(
        ProgressiveSearchDocument document,
        out IReadOnlyList<string> warnings)
    {
        var values = new Dictionary<string, RecipeFieldValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["originalName"] = new(Path.GetFileNameWithoutExtension(document.FileName), "filesystem filename"),
            ["extension"] = new(Path.GetExtension(document.FileName).TrimStart('.'), "filesystem extension"),
        };
        if (document.CreationTimeUtc is { } created)
        {
            var value = created.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            values["filesystemCreatedDate"] = new(value, "filesystem created timestamp");
            values["createdDate"] = new(value, "legacy filesystem created timestamp");
        }

        if (document.ModifiedTimeUtc is { } modified)
        {
            var value = modified.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            values["filesystemModifiedDate"] = new(value, "filesystem modified timestamp");
            values["modifiedDate"] = new(value, "legacy filesystem modified timestamp");
            values["date"] = new(value, "deprecated generic date (filesystem modified timestamp)");
        }

        if (!string.IsNullOrWhiteSpace(document.FileType))
        {
            values["category"] = new(document.FileType, "deterministic file category");
        }

        var messages = new List<string>();
        AddSingularTrustedTag(document.SmartTags, SmartTagType.Theme, "theme", values, messages);
        AddSingularTrustedTag(document.SmartTags, SmartTagType.DocumentType, "documentType", values, messages);
        warnings = Array.AsReadOnly(messages.ToArray());
        return new ReadOnlyDictionary<string, RecipeFieldValue>(values);
    }

    private static void AddSingularTrustedTag(
        IReadOnlyList<FileSmartTag> tags,
        SmartTagType type,
        string field,
        Dictionary<string, RecipeFieldValue> values,
        List<string> warnings)
    {
        var eligible = tags.Where(tag =>
                tag.Definition.Type == type &&
                tag.Decision != SmartTagDecision.Rejected &&
                (tag.State == SmartTagAssignmentState.Accepted ||
                 tag.State == SmartTagAssignmentState.Automatic &&
                 tag.Origin == SmartTagOrigin.DeterministicClassifier &&
                 tag.Confidence == ContentIntelligenceConfidence.Strong))
            .OrderByDescending(tag => tag.State == SmartTagAssignmentState.Accepted)
            .ThenBy(tag => tag.Definition.CanonicalKey, StringComparer.Ordinal)
            .ToArray();
        if (eligible.Length == 1)
        {
            var tag = eligible[0];
            var source = tag.State == SmartTagAssignmentState.Accepted
                ? $"accepted {type} Smart Tag"
                : $"Strong deterministic {type} Smart Tag";
            values[field] = new RecipeFieldValue(tag.Definition.DisplayName, source);
        }
        else if (eligible.Length > 1)
        {
            warnings.Add($"Multiple eligible {type} values are available; the singular {{{field}}} token was not resolved.");
        }
    }

    private static bool Applies(SortingRecipe recipe, ProgressiveSearchDocument document)
    {
        if (!recipe.IsEnabled || recipe.IsArchived)
        {
            return false;
        }

        if (recipe.Applicability.IncludedFileTypes.Count > 0 &&
            !recipe.Applicability.IncludedFileTypes.Contains(
                NormalizeExtension(document.Extension),
                StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (recipe.Applicability.Categories.Count > 0 &&
            (!Enum.TryParse<FileCategory>(document.FileType, true, out var category) ||
             !recipe.Applicability.Categories.Contains(category)))
        {
            return false;
        }

        return (recipe.Applicability.MinimumFileSizeBytes is null ||
                document.Length >= recipe.Applicability.MinimumFileSizeBytes) &&
               (recipe.Applicability.MaximumFileSizeBytes is null ||
                document.Length <= recipe.Applicability.MaximumFileSizeBytes);
    }

    private void MarkProposalCollisions(List<OrganizationProposalRow> rows)
    {
        var collisions = rows
            .Where(row => row.TargetPath is not null && row.Readiness != OrganizationProposalReadiness.CannotPropose)
            .GroupBy(row => NormalizeCollisionPath(row.TargetPath!), _paths.Comparer)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(row => row.FileId))
            .ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < rows.Count; index++)
        {
            if (!collisions.Contains(rows[index].FileId))
            {
                continue;
            }

            var row = rows[index];
            rows[index] = row with
            {
                Readiness = OrganizationProposalReadiness.CannotPropose,
                Conflicts = Array.AsReadOnly(row.Conflicts
                    .Append("Multiple selected files produce the same normalized target.")
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()),
            };
        }
    }

    private int CountRequiredDirectories(
        string root,
        IEnumerable<OrganizationProposalRow> rows)
    {
        var directories = new HashSet<string>(_paths.Comparer);
        foreach (var row in rows)
        {
            var parent = Path.GetDirectoryName(row.TargetPath!);
            while (!string.IsNullOrWhiteSpace(parent) &&
                   _paths.IsWithinRoot(root, parent) &&
                   !_paths.PathsEqual(parent, root))
            {
                if (Directory.Exists(parent))
                {
                    break;
                }

                directories.Add(parent);
                parent = Path.GetDirectoryName(parent);
            }
        }

        return directories.Count;
    }

    private string NormalizeCollisionPath(string path) =>
        _paths.NormalizeAbsolutePath(path).Normalize(NormalizationForm.FormC);

    private static OrganizationProposalRow CannotResolve(
        string fileId,
        string currentPath = "Indexed file unavailable",
        string conflict = "The stable indexed file no longer resolves to an active document.") =>
        new(
            fileId,
            currentPath,
            null,
            null,
            null,
            OrganizationProposalReadiness.CannotPropose,
            [],
            [],
            [],
            [],
            [conflict],
            null,
            null);

    private static string Fingerprint(
        SortingRecipe recipe,
        string root,
        string sourceId,
        IReadOnlyList<string> selectedIds,
        IReadOnlyList<OrganizationProposalRow> rows)
    {
        var builder = new StringBuilder();
        builder.Append(recipe.Id).Append('\n')
            .Append(recipe.Revision).Append('\n')
            .Append(recipe.NamingTemplate).Append('\n')
            .Append(recipe.DestinationTemplate).Append('\n')
            .Append(root).Append('\n')
            .Append(sourceId).Append('\n');
        foreach (var id in selectedIds)
        {
            builder.Append(id).Append('\n');
        }

        foreach (var row in rows)
        {
            builder.Append(row.FileId).Append('|')
                .Append(row.CurrentPath).Append('|')
                .Append(row.TargetPath).Append('|')
                .Append(row.Readiness).Append('|')
                .Append(row.SourceLength).Append('|')
                .Append(row.SourceModifiedAtUtc?.ToUniversalTime().Ticks).Append('\n');
            foreach (var mapping in row.Evidence)
            {
                builder.Append(mapping.Token).Append('=').Append(mapping.Value).Append(';');
            }

            builder.Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string NormalizeExtension(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : $".{value.Trim().TrimStart('.').ToLowerInvariant()}";

    private static readonly string[] ProductTokens =
    [
        "originalName",
        "theme",
        "documentType",
        "filesystemCreatedDate",
        "filesystemModifiedDate",
        "category",
    ];

    private static readonly IReadOnlyDictionary<string, string> ProductTokenDisplayNames =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["originalName"] = "Original name",
            ["theme"] = "Theme",
            ["documentType"] = "Document Type",
            ["filesystemCreatedDate"] = "Filesystem Created Date",
            ["filesystemModifiedDate"] = "Filesystem Modified Date",
            ["category"] = "File Category",
        });
}

/// <summary>Adapts the existing durable indexing service without introducing another evidence store.</summary>
public sealed class ReviewedOrganizationEvidenceSource : IReviewedOrganizationEvidenceSource
{
    private readonly IBackgroundIndexingService _indexing;

    public ReviewedOrganizationEvidenceSource(IBackgroundIndexingService indexing) =>
        _indexing = indexing ?? throw new ArgumentNullException(nameof(indexing));

    public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsByIdsAsync(
        IReadOnlyList<string> fileIds,
        CancellationToken cancellationToken) =>
        _indexing.GetDocumentsByIdsAsync(fileIds, cancellationToken);

    public Task<IReadOnlyList<IndexingSource>> GetSourcesAsync(CancellationToken cancellationToken) =>
        _indexing.GetSourcesAsync(cancellationToken);
}
