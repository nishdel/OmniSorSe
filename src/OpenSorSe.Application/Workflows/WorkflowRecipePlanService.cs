#pragma warning disable CS1591

using OpenSorSe.Application.Models;
using OpenSorSe.Executor;
using OpenSorSe.Executor.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Workflows;

public sealed class WorkflowRecipePlanService : IWorkflowRecipePlanService
{
    private readonly IWorkflowTemplateEngine _templateEngine;
    private readonly IChangePlanFactory _changePlanFactory;

    public WorkflowRecipePlanService(
        IWorkflowTemplateEngine templateEngine,
        IChangePlanFactory changePlanFactory)
    {
        _templateEngine = templateEngine ?? throw new ArgumentNullException(nameof(templateEngine));
        _changePlanFactory = changePlanFactory ?? throw new ArgumentNullException(nameof(changePlanFactory));
    }

    public async Task<WorkflowRecipePlanResult> CreatePlanAsync(
        ResolvedWorkflowConfiguration configuration,
        string organizationRoot,
        string sourceScanId,
        IReadOnlyList<ResultFile> files,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceScanId);
        ArgumentNullException.ThrowIfNull(files);
        if (!configuration.ChangePlans.GenerateChangePlans ||
            configuration.Recipes.Count == 0 ||
            files.Count == 0)
        {
            return new WorkflowRecipePlanResult(null, [], []);
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(organizationRoot));
        var occupied = files.Select(file => Path.GetFullPath(file.FullPath))
            .ToHashSet(ChangePlanFactory.PathComparer);
        var generated = new HashSet<string>(ChangePlanFactory.PathComparer);
        var evaluations = new List<RecipeEvaluationResult>();
        var proposals = new List<ChangeActionProposal>();
        var warnings = new List<string>();
        foreach (var file in files.OrderBy(file => file.FullPath, ChangePlanFactory.PathComparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matching = configuration.Recipes
                .Where(recipe => Applies(recipe, file))
                .OrderByDescending(recipe => recipe.Priority)
                .ThenBy(recipe => recipe.Id, StringComparer.Ordinal)
                .ToArray();
            if (matching.Length == 0)
            {
                continue;
            }

            var selected = matching[0];
            if (matching.Length > 1)
            {
                warnings.Add(
                    $"{Path.GetFileName(file.FullPath)} matched {matching.Length} recipes; \"{selected.Name}\" won by explicit priority and stable ID order.");
            }

            var values = BuildValues(file);
            var destinationsForFile = new HashSet<string>(
                occupied.Where(path => !ChangePlanFactory.PathComparer.Equals(path, file.FullPath))
                    .Concat(generated),
                ChangePlanFactory.PathComparer);
            var evaluation = _templateEngine.Evaluate(
                selected,
                new RecipeEvaluationContext(root, file.FullPath, values, destinationsForFile));
            evaluations.Add(evaluation);
            if (!evaluation.IsValid ||
                evaluation.ProposedDestinationPath is null ||
                evaluation.ProposedFileName is null)
            {
                warnings.AddRange(evaluation.Conflicts.Select(conflict =>
                    $"{Path.GetFileName(file.FullPath)}: {conflict}"));
                if (configuration.UncertaintyPolicy == WorkflowUncertaintyPolicy.Skip)
                {
                    continue;
                }

                warnings.Add(
                    $"{Path.GetFileName(file.FullPath)} was skipped because its recipe output requires reviewable unresolved values.");
                continue;
            }

            if (ChangePlanFactory.PathComparer.Equals(file.FullPath, evaluation.ProposedDestinationPath))
            {
                continue;
            }

            var sameDirectory = ChangePlanFactory.PathComparer.Equals(
                Path.GetDirectoryName(file.FullPath),
                Path.GetDirectoryName(evaluation.ProposedDestinationPath));
            var actionType = sameDirectory
                ? ChangeActionType.RenameFile
                : ChangeActionType.MoveFile;
            if (actionType == ChangeActionType.RenameFile &&
                !configuration.ChangePlans.PermitRenameProposals ||
                actionType == ChangeActionType.MoveFile &&
                !configuration.ChangePlans.PermitMoveProposals)
            {
                warnings.Add(
                    $"{Path.GetFileName(file.FullPath)} was skipped because the profile does not permit this proposal type.");
                continue;
            }

            var destinationDirectory = Path.GetDirectoryName(evaluation.ProposedDestinationPath);
            if (!configuration.ChangePlans.PermitDirectoryProposals &&
                !string.IsNullOrWhiteSpace(destinationDirectory) &&
                !Directory.Exists(destinationDirectory))
            {
                warnings.Add(
                    $"{Path.GetFileName(file.FullPath)} was skipped because its destination directory does not exist and the profile does not permit directory proposals.");
                continue;
            }

            var evidence = values
                .Where(pair => evaluation.ValuesUsed.ContainsKey(pair.Key))
                .Select(pair => pair.Value.EvidenceSource)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var provenance = new ChangeWorkflowProvenance(
                configuration.Profile.Id,
                configuration.Profile.Name,
                configuration.Profile.Revision,
                selected.Id,
                selected.Name,
                selected.Revision,
                evaluation.ValuesUsed,
                Array.AsReadOnly(evidence),
                evaluation.RequiresAiDerivedValues,
                evaluation.Warnings,
                evaluation.MissingValues);
            var reason =
                $"Workflow profile \"{configuration.Profile.Name}\" r{configuration.Profile.Revision}; " +
                $"recipe \"{selected.Name}\" r{selected.Revision}; " +
                $"{(evaluation.RequiresAiDerivedValues ? "AI-assisted reviewed values" : "deterministic values")}.";
            proposals.Add(new ChangeActionProposal(
                actionType,
                file.FullPath,
                evaluation.ProposedDestinationPath,
                evaluation.RequiresAiDerivedValues
                    ? ChangeSuggestionSource.Ai
                    : ChangeSuggestionSource.DeterministicRule,
                reason,
                proposals.Count + 1,
                file.Id,
                file.SizeInBytes,
                file.LastWriteTimeUtc)
            {
                WorkflowProvenance = provenance,
            });
            generated.Add(evaluation.ProposedDestinationPath);
            if (proposals.Count >= ChangePlanSchema.MaximumActions)
            {
                warnings.Add("The workflow proposal reached the Change Plan action limit; remaining items were skipped.");
                break;
            }
        }

        if (proposals.Count == 0)
        {
            return new WorkflowRecipePlanResult(
                null,
                Array.AsReadOnly(evaluations.ToArray()),
                Array.AsReadOnly(warnings.Distinct(StringComparer.Ordinal).ToArray()));
        }

        var plan = await _changePlanFactory.CreateAsync(
            new ChangePlanCreationRequest(
                root,
                sourceScanId,
                Array.AsReadOnly(proposals.ToArray()),
                Array.AsReadOnly(warnings.Distinct(StringComparer.Ordinal).ToArray())),
            cancellationToken).ConfigureAwait(false);
        return new WorkflowRecipePlanResult(
            plan,
            Array.AsReadOnly(evaluations.ToArray()),
            Array.AsReadOnly(warnings.Distinct(StringComparer.Ordinal).ToArray()));
    }

    private static bool Applies(SortingRecipe recipe, ResultFile file)
    {
        if (!recipe.IsEnabled || recipe.IsArchived)
        {
            return false;
        }

        var applicability = recipe.Applicability;
        if (applicability.IncludedFileTypes.Count > 0 &&
            !applicability.IncludedFileTypes.Contains(
                NormalizeExtension(file.NormalizedExtension),
                StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (applicability.Categories.Count > 0 &&
            (file.Category is null || !applicability.Categories.Contains(file.Category.Value)))
        {
            return false;
        }

        return (applicability.MinimumFileSizeBytes is null ||
                file.SizeInBytes >= applicability.MinimumFileSizeBytes) &&
               (applicability.MaximumFileSizeBytes is null ||
                file.SizeInBytes <= applicability.MaximumFileSizeBytes);
    }

    private static IReadOnlyDictionary<string, RecipeFieldValue> BuildValues(ResultFile file)
    {
        var values = new Dictionary<string, RecipeFieldValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["originalName"] = new(Path.GetFileNameWithoutExtension(file.DisplayFileName), "filesystem metadata"),
            ["extension"] = new(Path.GetExtension(file.DisplayFileName).TrimStart('.'), "filesystem metadata"),
        };
        if (file.LastWriteTimeUtc is DateTimeOffset modified)
        {
            var value = modified.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            values["modifiedDate"] = new(value, "filesystem metadata");
            values["date"] = new(value, "filesystem metadata");
        }

        if (file.CreationTimeUtc is DateTimeOffset created)
        {
            var createdUtc = created.ToUniversalTime();
            var value = createdUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            values["createdDate"] = new(value, "filesystem metadata");
            values["captureYear"] = new(createdUtc.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), "filesystem metadata");
            values["captureMonth"] = new(createdUtc.Month.ToString("00", System.Globalization.CultureInfo.InvariantCulture), "filesystem metadata");
        }

        if (file.Category is FileCategory category && category != FileCategory.Unknown)
        {
            values["category"] = new(category.ToString(), "deterministic classification");
            values["documentType"] = new(category.ToString(), "deterministic classification");
        }

        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, RecipeFieldValue>(values);
    }

    private static string NormalizeExtension(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : $".{value.Trim().TrimStart('.').ToLowerInvariant()}";
}
