using System.Text.Json;

namespace OpenSorSe.Application.AI;

/// <summary>Defines deterministic limits for small-model AI prompts.</summary>
public static class AiPromptLimits
{
    /// <summary>Maximum file records included in a folder-structure prompt.</summary>
    public const int MaximumFolderStructureFiles = 12;

    /// <summary>Maximum nearby filename stems included in a rename prompt.</summary>
    public const int MaximumSiblingFileNames = 8;

    /// <summary>Maximum existing folder values considered while building allowed names.</summary>
    public const int MaximumExistingFolderNames = 12;

    /// <summary>Maximum preference values considered per supported preference category.</summary>
    public const int MaximumPreferenceValues = 6;

    /// <summary>Maximum safe folder-name choices sent to the model.</summary>
    public const int MaximumAllowedFolderNames = 16;

    /// <summary>Maximum UTF-16 characters from a prior response supplied to repair.</summary>
    public const int MaximumRepairResponseCharacters = 32_768;

    /// <summary>Maximum PDF pages included in one explicit document-text request.</summary>
    public const int MaximumDocumentTextPages = 12;

    /// <summary>Maximum extracted characters included in one explicit document-text request.</summary>
    public const int MaximumDocumentTextCharacters = 16_384;
}

/// <summary>Contains one deterministic provider prompt and exact included identities.</summary>
public sealed record AiPromptPackage(
    string TaskId,
    string Prompt,
    IReadOnlyList<string> IncludedSourceIds,
    bool WasInputBounded)
{
    /// <summary>Gets the concrete task kind.</summary>
    public AiSuggestionKind Kind { get; init; }

    /// <summary>Gets the exact short system prompt paired with the user prompt.</summary>
    public string SystemPrompt { get; init; } = string.Empty;

    /// <summary>Gets the reviewable prompt-template version.</summary>
    public string PromptVersion { get; init; } = string.Empty;

    /// <summary>Gets request-local identities mapped to known application identities.</summary>
    public IReadOnlyList<AiPromptSourceMapping> SourceMappings { get; init; } = [];

    /// <summary>Gets folder components the folder response is allowed to declare.</summary>
    public IReadOnlyList<string> AllowedFolderNames { get; init; } = [];

    /// <summary>Gets the total eligible source count before deterministic bounding.</summary>
    public int TotalInputCount { get; init; } = IncludedSourceIds.Count;

    /// <summary>Gets the source count serialized into the request.</summary>
    public int IncludedInputCount => SourceMappings.Count == 0 ? IncludedSourceIds.Count : SourceMappings.Count;

    /// <summary>Gets the count omitted by deterministic bounding.</summary>
    public int OmittedInputCount => Math.Max(0, TotalInputCount - IncludedInputCount);
}

/// <summary>Maps one short request-local identity back to a known result without exposing its path.</summary>
public sealed record AiPromptSourceMapping(string RequestSourceId, string KnownSourceId, string ExactFileName);

/// <summary>Builds capability-specific, bounded prompts and one optional repair prompt.</summary>
public interface IAiPromptBuilder
{
    /// <summary>Builds the file-rename prompt.</summary>
    AiPromptPackage BuildFileRenamePrompt(AiFileRenameRequest request, AiPreferenceSummary preferences);

    /// <summary>Builds the folder-structure prompt.</summary>
    AiPromptPackage BuildFolderStructurePrompt(AiFolderStructureRequest request, AiPreferenceSummary preferences);

    /// <summary>Builds a bounded extracted-document-text interpretation prompt.</summary>
    AiPromptPackage BuildDocumentInterpretationPrompt(AiDocumentTextRequest request);

    /// <summary>Builds the only permitted structured-output repair attempt.</summary>
    AiPromptPackage BuildRepairPrompt(
        AiPromptPackage original,
        string priorResponse,
        string validationError);
}

/// <summary>Builds deterministic, labelled prompts intended for approximately 2B–8B local models.</summary>
public sealed class AiPromptBuilder : IAiPromptBuilder
{
    /// <summary>Gets the versioned file-rename task identifier.</summary>
    public const string FileRenameTaskId = "file-rename-v2";

    /// <summary>Gets the versioned folder-structure task identifier.</summary>
    public const string FolderStructureTaskId = "folder-structure-v2";

    /// <summary>Gets the versioned extracted-text interpretation task identifier.</summary>
    public const string DocumentInterpretationTaskId = "document-text-interpretation-v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <inheritdoc />
    public AiPromptPackage BuildFileRenamePrompt(
        AiFileRenameRequest request,
        AiPreferenceSummary preferences)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.File);
        ArgumentNullException.ThrowIfNull(request.SiblingFileNames);
        ArgumentNullException.ThrowIfNull(preferences);

        const string requestSourceId = "item-001";
        var extension = Bound(request.File.NormalizedExtension, 32);
        var currentStem = Bound(Path.GetFileNameWithoutExtension(request.File.DisplayFileName), 120);
        var nearbyStems = BoundFileStems(
            request.SiblingFileNames,
            AiPromptLimits.MaximumSiblingFileNames);
        var rejectedStems = BoundFileStems(
            preferences.RejectedValues,
            AiPromptLimits.MaximumPreferenceValues);
        var maximumStemLength = Math.Min(
            AiSuggestionValidator.MaximumFileStemLength,
            Math.Max(1, 255 - extension.Length));
        var groundedEvidence = request.GroundedEvidence
            .Take(4)
            .Select(item => new
            {
                type = Bound(item.Type, 32),
                value = Bound(item.DisplayName, 64),
                authority = Bound(item.Authority, 32),
            })
            .ToArray();
        var prompt = new
        {
            promptVersion = AiPromptTemplates.FileRenamePromptVersion,
            taskId = FileRenameTaskId,
            task = "Suggest one grounded replacement filename stem for one known file.",
            input = new
            {
                sourceFileId = requestSourceId,
                currentStem,
                preservedExtension = extension,
                documentType = Bound(request.File.ClassificationDisplay, 64),
                nearbyNameStems = nearbyStems.Values,
                groundedClassificationEvidence = groundedEvidence,
                rejectedStems = rejectedStems.Values,
                maximumStemLength,
                separator = "-",
                dateFormat = "yyyy-MM-dd",
                invalidCharacters = "< > : \" / \\ | ? * and control characters",
                reservedNames = "CON PRN AUX NUL COM1-COM9 LPT1-LPT9",
            },
            rules = AiPromptTemplates.FileRenameRules,
            responseSchema = AiStructuredOutputContracts.GetSchema(AiSuggestionKind.FileRename),
            output = "Return exactly one JSON object. Property order: taskId, status, sourceFileId, suggestedStem, reason, confidence. For no_suggestion return only taskId, status, reason.",
        };

        return new AiPromptPackage(
            FileRenameTaskId,
            JsonSerializer.Serialize(prompt, JsonOptions),
            Array.AsReadOnly([request.File.Id]),
            nearbyStems.WasBounded || rejectedStems.WasBounded)
        {
            Kind = AiSuggestionKind.FileRename,
            PromptVersion = AiPromptTemplates.FileRenamePromptVersion,
            SystemPrompt = AiStructuredOutputContracts.GetSystemPrompt(AiSuggestionKind.FileRename),
            SourceMappings = Array.AsReadOnly([
                new AiPromptSourceMapping(requestSourceId, request.File.Id, request.File.DisplayFileName),
            ]),
            TotalInputCount = 1,
        };
    }

    /// <inheritdoc />
    public AiPromptPackage BuildFolderStructurePrompt(
        AiFolderStructureRequest request,
        AiPreferenceSummary preferences)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Files);
        ArgumentNullException.ThrowIfNull(request.ExistingFolderNames);
        ArgumentNullException.ThrowIfNull(preferences);

        var orderedFiles = request.Files
            .OrderBy(file => file.Id, StringComparer.Ordinal)
            .ThenBy(file => file.DisplayFileName, StringComparer.Ordinal)
            .Take(AiPromptLimits.MaximumFolderStructureFiles)
            .ToArray();
        var sourceMappings = orderedFiles
            .Select((file, index) =>
                new AiPromptSourceMapping($"item-{index + 1:D3}", file.Id, file.DisplayFileName))
            .ToArray();
        var allowedNames = BuildAllowedFolderNames(
            orderedFiles,
            request.ExistingFolderNames,
            preferences.PreferredFolders);
        var prompt = new
        {
            promptVersion = AiPromptTemplates.FolderStructurePromptVersion,
            taskId = FolderStructureTaskId,
            task = "Declare one small logical folder hierarchy and assign every supplied opaque file ID.",
            input = new
            {
                files = orderedFiles.Select((file, index) => new
                {
                    sourceFileId = sourceMappings[index].RequestSourceId,
                    currentStem = Bound(Path.GetFileNameWithoutExtension(file.DisplayFileName), 120),
                    extension = Bound(file.NormalizedExtension, 32),
                    documentType = Bound(file.ClassificationDisplay, 64),
                }).ToArray(),
                allowedFolderNames = allowedNames.Values,
                maximumFolders = AiResponseLimits.MaximumFolders,
                maximumDepth = AiResponseLimits.MaximumFolderDepth,
                maximumComponentLength = AiSuggestionValidator.MaximumFolderComponentLength,
                fallbackFolderName = "Other",
            },
            rules = AiPromptTemplates.FolderStructureRules,
            responseSchema = AiStructuredOutputContracts.GetSchema(AiSuggestionKind.FolderStructure),
            output = "Return exactly one JSON object. Property order: taskId, status, folders, assignments, reason. For no_suggestion return only taskId, status, reason.",
        };

        return new AiPromptPackage(
            FolderStructureTaskId,
            JsonSerializer.Serialize(prompt, JsonOptions),
            Array.AsReadOnly(orderedFiles.Select(file => file.Id).ToArray()),
            orderedFiles.Length < request.Files.Count || allowedNames.WasBounded)
        {
            Kind = AiSuggestionKind.FolderStructure,
            PromptVersion = AiPromptTemplates.FolderStructurePromptVersion,
            SystemPrompt = AiStructuredOutputContracts.GetSystemPrompt(AiSuggestionKind.FolderStructure),
            SourceMappings = Array.AsReadOnly(sourceMappings),
            AllowedFolderNames = allowedNames.Values,
            TotalInputCount = request.Files.Count,
        };
    }

    /// <inheritdoc />
    public AiPromptPackage BuildDocumentInterpretationPrompt(AiDocumentTextRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Pages);
        var pageInputs = request.Pages
            .Where(page => page is not null && !string.IsNullOrWhiteSpace(page.Text))
            .OrderBy(page => page.PageNumber)
            .Take(AiPromptLimits.MaximumDocumentTextPages)
            .Select(page => new DocumentTextInput(page.PageNumber, page.TextSource.ToString(), page.Text!))
            .ToArray();
        var fallbackInputs = pageInputs.Length == 0
            ? new[]
            {
                new DocumentTextInput(0, "NativeText", request.NativeText ?? string.Empty),
                new DocumentTextInput(0, "Ocr", request.OcrText ?? string.Empty),
            }.Where(item => !string.IsNullOrWhiteSpace(item.Text)).ToArray()
            : [];
        var inputs = pageInputs.Concat(fallbackInputs).ToArray();
        var remaining = AiPromptLimits.MaximumDocumentTextCharacters;
        var boundedInputs = new List<object>();
        var wasBounded = request.Pages.Count > AiPromptLimits.MaximumDocumentTextPages;
        foreach (var input in inputs)
        {
            if (remaining <= 0)
            {
                wasBounded = true;
                break;
            }

            var boundedText = Bound(input.Text, remaining);
            wasBounded |= boundedText.Length < input.Text.Length;
            boundedInputs.Add(new
            {
                pageNumber = input.PageNumber,
                provenance = input.Provenance,
                text = boundedText,
            });
            remaining -= boundedText.Length;
        }

        var prompt = new
        {
            promptVersion = AiPromptTemplates.DocumentInterpretationPromptVersion,
            taskId = DocumentInterpretationTaskId,
            task = "Extract bounded review-only descriptive metadata from supplied text.",
            input = new
            {
                sourceFileId = "item-001",
                fileName = Bound(request.DisplayFileName, 255),
                extractedTextPages = boundedInputs,
                wasInputBounded = wasBounded,
            },
            rules = AiPromptTemplates.DocumentInterpretationRules,
            responseSchema = AiStructuredOutputContracts.GetSchema(AiSuggestionKind.DocumentTextInterpretation),
            output = "Return exactly one JSON object matching responseSchema.",
        };
        return new AiPromptPackage(
            DocumentInterpretationTaskId,
            JsonSerializer.Serialize(prompt, JsonOptions),
            Array.AsReadOnly([request.SourceFileId]),
            wasBounded)
        {
            Kind = AiSuggestionKind.DocumentTextInterpretation,
            PromptVersion = AiPromptTemplates.DocumentInterpretationPromptVersion,
            SystemPrompt = AiStructuredOutputContracts.GetSystemPrompt(
                AiSuggestionKind.DocumentTextInterpretation),
            SourceMappings = Array.AsReadOnly([
                new AiPromptSourceMapping("item-001", request.SourceFileId, request.DisplayFileName),
            ]),
            TotalInputCount = 1,
        };
    }

    /// <inheritdoc />
    public AiPromptPackage BuildRepairPrompt(
        AiPromptPackage original,
        string priorResponse,
        string validationError)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(priorResponse);
        ArgumentException.ThrowIfNullOrWhiteSpace(validationError);
        var boundedResponse = Bound(priorResponse, AiPromptLimits.MaximumRepairResponseCharacters);
        var boundedError = Bound(validationError, 500);
        var prompt = new
        {
            promptVersion = AiPromptTemplates.RepairPromptVersion,
            taskId = original.TaskId,
            task = "Repair one prior response for the same task.",
            input = new
            {
                originalTaskId = original.TaskId,
                priorResponse = boundedResponse,
                validationError = boundedError,
            },
            rules = AiPromptTemplates.RepairRules,
            responseSchema = AiStructuredOutputContracts.GetSchema(original.Kind),
            output = "Return the corrected JSON object only.",
        };

        return new AiPromptPackage(
            original.TaskId,
            JsonSerializer.Serialize(prompt, JsonOptions),
            original.IncludedSourceIds,
            original.WasInputBounded ||
            boundedResponse.Length < priorResponse.Length ||
            boundedError.Length < validationError.Length)
        {
            Kind = original.Kind,
            PromptVersion = AiPromptTemplates.RepairPromptVersion,
            SystemPrompt = AiStructuredOutputContracts.RepairSystemPrompt,
            SourceMappings = original.SourceMappings,
            AllowedFolderNames = original.AllowedFolderNames,
            TotalInputCount = original.TotalInputCount,
        };
    }

    private static (IReadOnlyList<string> Values, bool WasBounded) BuildAllowedFolderNames(
        IReadOnlyList<OpenSorSe.Application.Models.ResultFile> files,
        IReadOnlyList<string> existingFolders,
        IReadOnlyList<string> preferredFolders)
    {
        var boundedExisting = existingFolders.Take(AiPromptLimits.MaximumExistingFolderNames).ToArray();
        var boundedPreferred = preferredFolders.Take(AiPromptLimits.MaximumPreferenceValues).ToArray();
        var rawCandidates = files.Select(file => file.ClassificationDisplay)
            .Concat(boundedExisting.SelectMany(SplitFolderComponents))
            .Concat(boundedPreferred.SelectMany(SplitFolderComponents))
            .Append("Other")
            .ToArray();
        var validCandidates = rawCandidates
            .Where(value => AiSuggestionValidator.TryNormalizeFolderName(value, out _, out _))
            .Select(value =>
            {
                AiSuggestionValidator.TryNormalizeFolderName(value, out var normalized, out _);
                return normalized;
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var candidates = validCandidates
            .Where(value => !string.Equals(value, "Other", StringComparison.OrdinalIgnoreCase))
            .Take(AiPromptLimits.MaximumAllowedFolderNames - 1)
            .Prepend("Other")
            .ToArray();
        return (
            Array.AsReadOnly(candidates),
            existingFolders.Count > boundedExisting.Length ||
            preferredFolders.Count > boundedPreferred.Length ||
            rawCandidates.Length != rawCandidates.Count(
                value => AiSuggestionValidator.TryNormalizeFolderName(value, out _, out _)) ||
            validCandidates.Length > candidates.Length);
    }

    private static IEnumerable<string> SplitFolderComponents(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static (IReadOnlyList<string> Values, bool WasBounded) BoundFileStems(
        IReadOnlyList<string> values,
        int limit)
    {
        var candidates = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFileNameWithoutExtension(Path.GetFileName(value.Trim())))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var all = candidates
            .Select(value => Bound(value, 120))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return (
            Array.AsReadOnly(all.Take(limit).ToArray()),
            all.Length > limit || candidates.Any(value => value.Length > 120));
    }

    private static string Bound(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private sealed record DocumentTextInput(int PageNumber, string Provenance, string Text);
}
