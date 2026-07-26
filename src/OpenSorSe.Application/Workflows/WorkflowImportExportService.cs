#pragma warning disable CS1591

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSorSe.Application.Workflows;

public sealed class WorkflowImportExportService : IWorkflowImportExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IWorkflowLibraryService _library;
    private readonly IWorkflowValidator _validator;

    public WorkflowImportExportService(
        IWorkflowLibraryService library,
        IWorkflowValidator validator)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<string> ExportProfileAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var profile = await _library.GetProfileAsync(profileId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The workflow profile no longer exists.");
        var envelope = new WorkflowExportEnvelope(
            WorkflowExportContentType.WorkflowProfile,
            WorkflowLibraryLimits.CurrentExportSchemaVersion,
            "1.3.0",
            profile.Id,
            profile.Name,
            profile.Description,
            profile.SortingRecipeIds,
            JsonWorkflowLibraryStore.Clone(profile),
            null);
        _library.RecordDiagnostic(
            WorkflowDiagnosticKind.Export,
            "Workflow profile exported without provider settings, credentials, or document content.",
            profile.Id);
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    public async Task<string> ExportRecipeAsync(
        string recipeId,
        CancellationToken cancellationToken)
    {
        var recipe = await _library.GetRecipeAsync(recipeId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The sorting recipe no longer exists.");
        var envelope = new WorkflowExportEnvelope(
            WorkflowExportContentType.SortingRecipe,
            WorkflowLibraryLimits.CurrentExportSchemaVersion,
            "1.3.0",
            recipe.Id,
            recipe.Name,
            recipe.Description,
            [],
            null,
            JsonWorkflowLibraryStore.Clone(recipe));
        _library.RecordDiagnostic(
            WorkflowDiagnosticKind.Export,
            "Sorting recipe exported as declarative configuration without executable content.",
            recipe.Id);
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    public async Task<WorkflowImportResult> ImportAsync(
        string json,
        WorkflowImportConflictPolicy conflictPolicy,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Failure("The import is empty.");
        }

        if (Encoding.UTF8.GetByteCount(json) > WorkflowLibraryLimits.MaximumImportBytes)
        {
            return Failure("The import exceeds the supported size.");
        }

        WorkflowExportEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<WorkflowExportEnvelope>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return Failure("The import is malformed or excessively deep.");
        }

        if (envelope is null ||
            envelope.SchemaVersion != WorkflowLibraryLimits.CurrentExportSchemaVersion ||
            string.IsNullOrWhiteSpace(envelope.ItemId) ||
            string.IsNullOrWhiteSpace(envelope.Name) ||
            envelope.DependencyReferences is null)
        {
            return Failure("The import envelope is incomplete or uses an unsupported schema.");
        }

        return envelope.ContentType switch
        {
            WorkflowExportContentType.WorkflowProfile when
                envelope.Profile is not null &&
                EnvelopeMatchesProfile(envelope) =>
                await ImportProfileAsync(envelope.Profile, conflictPolicy, cancellationToken).ConfigureAwait(false),
            WorkflowExportContentType.SortingRecipe when
                envelope.Recipe is not null &&
                EnvelopeMatchesRecipe(envelope) =>
                await ImportRecipeAsync(envelope.Recipe, conflictPolicy, cancellationToken).ConfigureAwait(false),
            _ => Failure("The import content type does not match its configuration payload."),
        };
    }

    private static bool EnvelopeMatchesProfile(WorkflowExportEnvelope envelope) =>
        string.Equals(envelope.ItemId, envelope.Profile!.Id, StringComparison.Ordinal) &&
        string.Equals(envelope.Name, envelope.Profile.Name, StringComparison.Ordinal) &&
        envelope.Profile.SortingRecipeIds is not null &&
        envelope.DependencyReferences.ToHashSet(StringComparer.Ordinal).SetEquals(
            envelope.Profile.SortingRecipeIds);

    private static bool EnvelopeMatchesRecipe(WorkflowExportEnvelope envelope) =>
        string.Equals(envelope.ItemId, envelope.Recipe!.Id, StringComparison.Ordinal) &&
        string.Equals(envelope.Name, envelope.Recipe.Name, StringComparison.Ordinal) &&
        envelope.DependencyReferences.Count == 0;

    private async Task<WorkflowImportResult> ImportProfileAsync(
        WorkflowProfile imported,
        WorkflowImportConflictPolicy conflictPolicy,
        CancellationToken cancellationToken)
    {
        var recipes = await _library.ListRecipesAsync(true, cancellationToken).ConfigureAwait(false);
        var validation = _validator.ValidateProfile(imported, recipes);
        if (!validation.IsValid)
        {
            return Invalid(validation);
        }

        var profiles = await _library.ListProfilesAsync(true, cancellationToken).ConfigureAwait(false);
        var conflict = profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, imported.Id, StringComparison.Ordinal) ||
            string.Equals(profile.Name, imported.Name, StringComparison.OrdinalIgnoreCase));
        if (conflict is not null)
        {
            if (conflictPolicy == WorkflowImportConflictPolicy.Cancel)
            {
                return Failure("Import cancelled because a workflow profile ID or name conflicts.");
            }

            if (conflictPolicy == WorkflowImportConflictPolicy.Skip)
            {
                return Failure("The conflicting workflow profile was skipped.");
            }

            if (conflictPolicy == WorkflowImportConflictPolicy.ReplaceUserCreated)
            {
                if (conflict.IsBuiltIn)
                {
                    return Failure("Canonical built-in profiles cannot be replaced. Import as a copy instead.");
                }

                var replaced = await _library.UpdateProfileAsync(imported with
                {
                    Id = conflict.Id,
                    Name = conflict.Name,
                    IsBuiltIn = false,
                }, cancellationToken).ConfigureAwait(false);
                return Success(replaced.Id, "The existing user-created workflow profile was replaced after explicit conflict resolution.");
            }

            imported = imported with
            {
                Id = $"profile:{Guid.NewGuid():N}",
                Name = UniqueName(imported.Name, profiles.Select(profile => profile.Name)),
            };
        }

        var created = await _library.CreateProfileAsync(imported with
        {
            IsBuiltIn = false,
            Origin = new WorkflowProfileOrigin(
                WorkflowOriginKind.Imported,
                imported.Id,
                "1.3.0"),
        }, cancellationToken).ConfigureAwait(false);
        return Success(created.Id, "Workflow profile imported as a validated user-created item.");
    }

    private async Task<WorkflowImportResult> ImportRecipeAsync(
        SortingRecipe imported,
        WorkflowImportConflictPolicy conflictPolicy,
        CancellationToken cancellationToken)
    {
        var validation = _validator.ValidateRecipe(imported);
        if (!validation.IsValid)
        {
            return Invalid(validation);
        }

        var recipes = await _library.ListRecipesAsync(true, cancellationToken).ConfigureAwait(false);
        var conflict = recipes.FirstOrDefault(recipe =>
            string.Equals(recipe.Id, imported.Id, StringComparison.Ordinal) ||
            string.Equals(recipe.Name, imported.Name, StringComparison.OrdinalIgnoreCase));
        if (conflict is not null)
        {
            if (conflictPolicy == WorkflowImportConflictPolicy.Cancel)
            {
                return Failure("Import cancelled because a sorting recipe ID or name conflicts.");
            }

            if (conflictPolicy == WorkflowImportConflictPolicy.Skip)
            {
                return Failure("The conflicting sorting recipe was skipped.");
            }

            if (conflictPolicy == WorkflowImportConflictPolicy.ReplaceUserCreated)
            {
                if (conflict.IsBuiltIn)
                {
                    return Failure("Canonical built-in recipes cannot be replaced. Import as a copy instead.");
                }

                var replaced = await _library.UpdateRecipeAsync(imported with
                {
                    Id = conflict.Id,
                    Name = conflict.Name,
                    IsBuiltIn = false,
                }, cancellationToken).ConfigureAwait(false);
                return Success(replaced.Id, "The existing user-created sorting recipe was replaced after explicit conflict resolution.");
            }

            imported = imported with
            {
                Id = $"recipe:{Guid.NewGuid():N}",
                Name = UniqueName(imported.Name, recipes.Select(recipe => recipe.Name)),
            };
        }

        var created = await _library.CreateRecipeAsync(imported with
        {
            IsBuiltIn = false,
            Origin = new WorkflowProfileOrigin(
                WorkflowOriginKind.Imported,
                imported.Id,
                "1.3.0"),
        }, cancellationToken).ConfigureAwait(false);
        return Success(created.Id, "Sorting recipe imported as a validated declarative item.");
    }

    private static string UniqueName(string source, IEnumerable<string> existingNames)
    {
        var names = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var suffix = 1; suffix <= 999; suffix++)
        {
            var ending = suffix == 1 ? " (Imported copy)" : $" (Imported copy {suffix})";
            var maximumBase = WorkflowLibraryLimits.MaximumNameLength - ending.Length;
            var candidate = source[..Math.Min(source.Length, maximumBase)] + ending;
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("A unique bounded import name could not be generated.");
    }

    private WorkflowImportResult Invalid(WorkflowValidationResult validation)
    {
        var result = new WorkflowImportResult(
            false,
            string.Join(" ", validation.Issues.Where(issue => issue.IsBlocking).Select(issue => issue.Message)),
            null,
            Array.AsReadOnly(validation.Issues.Where(issue => !issue.IsBlocking).Select(issue => issue.Message).ToArray()));
        _library.RecordDiagnostic(WorkflowDiagnosticKind.Import, result.Message);
        return result;
    }

    private WorkflowImportResult Failure(string message)
    {
        _library.RecordDiagnostic(WorkflowDiagnosticKind.Import, message);
        return new WorkflowImportResult(false, message, null, []);
    }

    private WorkflowImportResult Success(string id, string message)
    {
        _library.RecordDiagnostic(WorkflowDiagnosticKind.Import, message, id);
        return new WorkflowImportResult(true, message, id, []);
    }

    private sealed record WorkflowExportEnvelope(
        WorkflowExportContentType ContentType,
        int SchemaVersion,
        string ApplicationVersion,
        string ItemId,
        string Name,
        string? Description,
        IReadOnlyList<string> DependencyReferences,
        WorkflowProfile? Profile,
        SortingRecipe? Recipe);
}
