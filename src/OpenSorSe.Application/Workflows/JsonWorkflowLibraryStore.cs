#pragma warning disable CS1591

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application.Workflows;

public sealed class JsonWorkflowLibraryStore : IWorkflowLibraryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly IWorkflowValidator _validator;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonWorkflowLibraryStore(
        string path,
        IWorkflowValidator validator,
        ILoggingService loggingService)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("An absolute workflow-library path is required.", nameof(path));
        }

        _path = path;
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(JsonWorkflowLibraryStore));
    }

    public async Task<WorkflowLibraryLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return Empty();
            }

            if (new FileInfo(_path).Length > WorkflowLibraryLimits.MaximumLibraryBytes)
            {
                return Recover("The workflow library exceeds its supported size.");
            }

            try
            {
                await using var stream = File.OpenRead(_path);
                var envelope = await JsonSerializer.DeserializeAsync<WorkflowLibraryEnvelope>(
                    stream,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                if (envelope is null ||
                    envelope.SchemaVersion is < 1 or > WorkflowLibraryLimits.CurrentLibrarySchemaVersion ||
                    envelope.Profiles is null ||
                    envelope.Recipes is null)
                {
                    return Recover("The workflow library has an unsupported format.");
                }

                var recipes = envelope.Recipes.ToArray();
                var profiles = envelope.Profiles.ToArray();
                ValidateLibrary(profiles, recipes);
                return new WorkflowLibraryLoadResult(
                    Array.AsReadOnly(profiles.Select(Clone).ToArray()),
                    Array.AsReadOnly(recipes.Select(Clone).ToArray()),
                    null,
                    null,
                    envelope.SchemaVersion < WorkflowLibraryLimits.CurrentLibrarySchemaVersion);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "The workflow library JSON is malformed; the original was preserved.");
                return Recover("The workflow library is malformed. Built-in workflows remain available.");
            }
            catch (InvalidDataException exception)
            {
                _logger.LogWarning(exception, "The workflow library is invalid; the original was preserved.");
                return Recover($"{exception.Message} Built-in workflows remain available.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        IReadOnlyList<WorkflowProfile> profiles,
        IReadOnlyList<SortingRecipe> recipes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(recipes);
        var profileCopies = profiles.Select(Clone).ToArray();
        var recipeCopies = recipes.Select(Clone).ToArray();
        ValidateLibrary(profileCopies, recipeCopies);
        if (profileCopies.Any(profile => profile.IsBuiltIn) ||
            recipeCopies.Any(recipe => recipe.IsBuiltIn))
        {
            throw new InvalidDataException("Canonical built-ins are supplied by the application and are not persisted as user items.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidDataException("The workflow-library path has no parent directory.");
            Directory.CreateDirectory(directory);
            var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = new FileStream(
                                 temporary,
                                 new FileStreamOptions
                                 {
                                     Mode = FileMode.CreateNew,
                                     Access = FileAccess.Write,
                                     Share = FileShare.None,
                                     Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                                 }))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        new WorkflowLibraryEnvelope(
                            WorkflowLibraryLimits.CurrentLibrarySchemaVersion,
                            profileCopies,
                            recipeCopies),
                        JsonOptions,
                        cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                if (new FileInfo(temporary).Length > WorkflowLibraryLimits.MaximumLibraryBytes)
                {
                    throw new InvalidDataException("The workflow library exceeds its supported encoded size.");
                }

                File.Move(temporary, _path, true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ValidateLibrary(
        IReadOnlyList<WorkflowProfile> profiles,
        IReadOnlyList<SortingRecipe> recipes)
    {
        var allProfiles = BuiltInWorkflowLibrary.Profiles.Concat(profiles).ToArray();
        var allRecipes = BuiltInWorkflowLibrary.Recipes.Concat(recipes).ToArray();
        if (profiles.Any(profile => profile is null) ||
            recipes.Any(recipe => recipe is null) ||
            profiles.Any(profile => profile.IsBuiltIn) ||
            recipes.Any(recipe => recipe.IsBuiltIn) ||
            profiles.Count > WorkflowLibraryLimits.MaximumProfiles ||
            recipes.Count > WorkflowLibraryLimits.MaximumRecipes ||
            allProfiles.Select(profile => profile.Id).Distinct(StringComparer.Ordinal).Count() != allProfiles.Length ||
            allProfiles.Select(profile => profile.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != allProfiles.Length ||
            allRecipes.Select(recipe => recipe.Id).Distinct(StringComparer.Ordinal).Count() != allRecipes.Length ||
            allRecipes.Select(recipe => recipe.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != allRecipes.Length)
        {
            throw new InvalidDataException("The workflow library contains duplicate or excessive entries.");
        }

        foreach (var recipe in recipes)
        {
            var result = _validator.ValidateRecipe(recipe);
            if (!result.IsValid)
            {
                throw new InvalidDataException(
                    $"Sorting recipe \"{recipe?.Name ?? "unknown"}\" is invalid: {FirstBlocking(result)}");
            }
        }

        var availableRecipes = BuiltInWorkflowLibrary.Recipes.Concat(recipes).ToArray();
        foreach (var profile in profiles)
        {
            var result = _validator.ValidateProfile(profile, availableRecipes);
            if (!result.IsValid)
            {
                throw new InvalidDataException(
                    $"Workflow profile \"{profile?.Name ?? "unknown"}\" is invalid: {FirstBlocking(result)}");
            }
        }
    }

    private WorkflowLibraryLoadResult Recover(string message)
    {
        string? copyPath = null;
        try
        {
            copyPath = $"{_path}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.json";
            File.Copy(_path, copyPath, false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            copyPath = null;
            _logger.LogWarning(exception, "A diagnostic copy of the corrupt workflow library could not be created.");
        }

        _logger.LogWarning(
            "Workflow library recovery loaded built-ins only. OriginalPathPreserved={OriginalPreserved}; DiagnosticCopyCreated={CopyCreated}.",
            File.Exists(_path),
            copyPath is not null);
        return new WorkflowLibraryLoadResult(
            [],
            [],
            message,
            copyPath,
            false);
    }

    private static WorkflowLibraryLoadResult Empty() =>
        new([], [], null, null, false);

    private static string FirstBlocking(WorkflowValidationResult result) =>
        result.Issues.FirstOrDefault(issue => issue.IsBlocking)?.Message ?? "Validation failed.";

    internal static WorkflowProfile Clone(WorkflowProfile value) => value with
    {
        Files = value.Files with
        {
            IncludedFileTypes = Array.AsReadOnly(value.Files.IncludedFileTypes.ToArray()),
            ExcludedFileTypes = Array.AsReadOnly(value.Files.ExcludedFileTypes.ToArray()),
        },
        Ai = value.Ai with
        {
            SelectedFileTypes = Array.AsReadOnly(value.Ai.SelectedFileTypes.ToArray()),
        },
        SortingRecipeIds = Array.AsReadOnly(value.SortingRecipeIds.ToArray()),
    };

    internal static SortingRecipe Clone(SortingRecipe value) => value with
    {
        Applicability = value.Applicability with
        {
            IncludedFileTypes = Array.AsReadOnly(value.Applicability.IncludedFileTypes.ToArray()),
            Categories = Array.AsReadOnly(value.Applicability.Categories.ToArray()),
        },
        RequiredFields = Array.AsReadOnly(value.RequiredFields.ToArray()),
        OptionalFields = Array.AsReadOnly(value.OptionalFields.ToArray()),
        FallbackValues = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            value.FallbackValues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)),
        Rules = Array.AsReadOnly(value.Rules.ToArray()),
        PreviewExamples = Array.AsReadOnly(value.PreviewExamples.Select(example => example with
        {
            Values = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                example.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)),
        }).ToArray()),
    };

    private sealed record WorkflowLibraryEnvelope(
        int SchemaVersion,
        IReadOnlyList<WorkflowProfile>? Profiles,
        IReadOnlyList<SortingRecipe>? Recipes);
}
