using System.Text;
using System.Text.Json;
using OpenSorSe.Application.Models;

namespace OpenSorSe.Application.AI;

/// <summary>Defines fixed bounds for structured AI response validation.</summary>
public static class AiResponseLimits
{
    /// <summary>Maximum UTF-8 bytes accepted by the application parser.</summary>
    public const int MaximumStructuredResponseBytes = 256 * 1024;

    /// <summary>Maximum folders in one logical hierarchy.</summary>
    public const int MaximumFolders = 8;

    /// <summary>Maximum assignments in one logical hierarchy.</summary>
    public const int MaximumAssignments = AiPromptLimits.MaximumFolderStructureFiles;

    /// <summary>Maximum provider reason length.</summary>
    public const int MaximumReasonLength = 160;

    /// <summary>Maximum parent depth in one proposed logical hierarchy.</summary>
    public const int MaximumFolderDepth = 3;
}

/// <summary>Contains validated rename response values before provider attribution.</summary>
public sealed record AiParsedFileRename(
    string SourceFileId,
    string SuggestedStem,
    string SuggestedFileName,
    string Reason,
    double? Confidence);

/// <summary>Contains a validated logical hierarchy before provider attribution.</summary>
public sealed record AiParsedFolderStructure(
    IReadOnlyList<AiSuggestedFolder> Folders,
    IReadOnlyList<AiFolderStructurePlanItem> Items,
    string Reason);

/// <summary>Contains validated document interpretation values before provider attribution.</summary>
public sealed record AiParsedDocumentInterpretation(
    string SourceFileId,
    string? DocumentType,
    string? Title,
    IReadOnlyList<SuggestedTag> Tags,
    IReadOnlyList<string> Dates,
    string? Issuer,
    string? SuggestedFolder,
    string Reason,
    double? Confidence);

/// <summary>Contains either one fully valid response, a valid no-suggestion response, or one safe error.</summary>
public enum AiResponseFailureKind
{
    /// <summary>The response is valid.</summary>
    None,
    /// <summary>JSON or the exact schema shape may be corrected once.</summary>
    RepairableFormatOrSchema,
    /// <summary>An opaque identity was unknown or unsafe and must not be retried.</summary>
    UnsafeIdentity,
    /// <summary>A path, traversal, reserved location, or filesystem value must not be retried.</summary>
    UnsafePath,
    /// <summary>The model invented evidence or otherwise departed from the requested task.</summary>
    ModelMisuse,
    /// <summary>The response was empty or exceeded a hard bound.</summary>
    HardBound,
}

/// <summary>Contains either one fully valid response, a valid no-suggestion response, or one classified error.</summary>
public sealed record AiResponseParseResult<T>(
    T? Value,
    bool IsNoSuggestion,
    string Message,
    AiResponseFailureKind FailureKind = AiResponseFailureKind.None)
    where T : class
{
    /// <summary>Gets whether the complete structured response passed validation.</summary>
    public bool IsValid => Value is not null || IsNoSuggestion;

    /// <summary>Gets whether exactly one structured-output repair attempt may be made.</summary>
    public bool CanRepair => !IsValid &&
        FailureKind == AiResponseFailureKind.RepairableFormatOrSchema;
}

/// <summary>Parses and validates capability-specific untrusted JSON.</summary>
public interface IAiResponseParser
{
    /// <summary>Parses one file-rename response against the exact known file context.</summary>
    AiResponseParseResult<AiParsedFileRename> ParseFileRename(string response, AiFileRenameRequest request);

    /// <summary>Parses one file-rename response using request-local identity mapping.</summary>
    AiResponseParseResult<AiParsedFileRename> ParseFileRename(
        string response,
        AiFileRenameRequest request,
        IReadOnlyList<AiPromptSourceMapping> sourceMappings);

    /// <summary>Parses one folder-structure response against only the file records included in the prompt.</summary>
    AiResponseParseResult<AiParsedFolderStructure> ParseFolderStructure(string response, IReadOnlyList<ResultFile> includedFiles);

    /// <summary>Parses one folder response using request-local identity mapping.</summary>
    AiResponseParseResult<AiParsedFolderStructure> ParseFolderStructure(
        string response,
        IReadOnlyList<ResultFile> includedFiles,
        IReadOnlyList<AiPromptSourceMapping> sourceMappings);

    /// <summary>Parses one folder response against exact mappings and declared folder-name choices.</summary>
    AiResponseParseResult<AiParsedFolderStructure> ParseFolderStructure(
        string response,
        IReadOnlyList<ResultFile> includedFiles,
        IReadOnlyList<AiPromptSourceMapping> sourceMappings,
        IReadOnlyList<string> allowedFolderNames);

    /// <summary>Parses a document interpretation against the exact request-local identity.</summary>
    AiResponseParseResult<AiParsedDocumentInterpretation> ParseDocumentInterpretation(
        string response,
        AiDocumentTextRequest request,
        IReadOnlyList<AiPromptSourceMapping> sourceMappings);
}

/// <summary>
/// Implements exact, case-sensitive response-shape and deterministic safety validation.
/// </summary>
public sealed class AiResponseParser : IAiResponseParser
{
    private const string SuggestionStatus = "suggestion";
    private const string NoSuggestionStatus = "no_suggestion";

    /// <inheritdoc />
    public AiResponseParseResult<AiParsedFileRename> ParseFileRename(string response, AiFileRenameRequest request) =>
        ParseFileRename(
            response,
            request,
            Array.AsReadOnly([new AiPromptSourceMapping(request.File.Id, request.File.Id, request.File.DisplayFileName)]));

    /// <inheritdoc />
    public AiResponseParseResult<AiParsedFileRename> ParseFileRename(
        string response,
        AiFileRenameRequest request,
        IReadOnlyList<AiPromptSourceMapping> sourceMappings)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.File);
        ArgumentNullException.ThrowIfNull(request.SiblingFileNames);
        ArgumentNullException.ThrowIfNull(sourceMappings);
        var sourceMapping = sourceMappings.Count == 1 &&
                            string.Equals(sourceMappings[0].KnownSourceId, request.File.Id, StringComparison.Ordinal)
            ? sourceMappings[0]
            : null;
        if (sourceMapping is null)
        {
            return Failure<AiParsedFileRename>(
                "The known rename identity mapping is invalid. No suggestion was used.",
                AiResponseFailureKind.UnsafeIdentity);
        }

        if (!TryOpen(response, out var document, out var error, out var openFailureKind))
        {
            return Failure<AiParsedFileRename>(error, openFailureKind);
        }

        using (document)
        {
            var root = document.RootElement;
            if (!TryReadCommon(root, AiPromptBuilder.FileRenameTaskId, out var status, out var reason, out error))
            {
                return Failure<AiParsedFileRename>(error, ClassifyCommonError(error));
            }

            if (status == NoSuggestionStatus)
            {
                if (!HasOnlyProperties(root, ["taskId", "status", "reason"], out var unknown))
                {
                    return Failure<AiParsedFileRename>(
                        "The AI no-suggestion response contained an unsupported or wrong-case property. No suggestion was used.",
                        IsMisuseProperty(unknown)
                            ? AiResponseFailureKind.ModelMisuse
                            : AiResponseFailureKind.RepairableFormatOrSchema);
                }

                return new AiResponseParseResult<AiParsedFileRename>(null, true, reason);
            }

            if (!HasOnlyProperties(
                    root,
                    ["taskId", "status", "sourceFileId", "suggestedStem", "reason", "confidence"],
                    out var unknownProperty))
            {
                return Failure<AiParsedFileRename>(
                    "The AI rename response contained an unsupported or wrong-case property. No suggestion was used.",
                    IsMisuseProperty(unknownProperty)
                        ? AiResponseFailureKind.ModelMisuse
                        : AiResponseFailureKind.RepairableFormatOrSchema);
            }

            if (!TryReadRequiredString(root, "sourceFileId", 32, out var sourceFileId, out error))
            {
                return Failure<AiParsedFileRename>(error);
            }

            if (!string.Equals(sourceFileId, sourceMapping.RequestSourceId, StringComparison.Ordinal))
            {
                return Failure<AiParsedFileRename>(
                    "The AI rename response referenced an unknown source file. No suggestion was used.",
                    AiResponseFailureKind.UnsafeIdentity);
            }

            if (!TryReadRequiredString(
                    root,
                    "suggestedStem",
                    AiSuggestionValidator.MaximumFileStemLength,
                    out var suggestedStem,
                    out error))
            {
                return Failure<AiParsedFileRename>(error);
            }

            if (!AiSuggestionValidator.TryNormalizeFileStem(
                    suggestedStem,
                    request.File.NormalizedExtension,
                    request.SiblingFileNames,
                    out var normalizedFileName,
                    out error))
            {
                return Failure<AiParsedFileRename>(
                    error,
                    LooksLikePathOrTraversal(suggestedStem)
                        ? AiResponseFailureKind.UnsafePath
                        : AiResponseFailureKind.ModelMisuse);
            }

            if (!AiSuggestionValidator.IsFileStemGrounded(
                    suggestedStem,
                    request.File.DisplayFileName,
                    request.File.ClassificationDisplay))
            {
                return Failure<AiParsedFileRename>(
                    "The AI rename response introduced words or facts that were not present in the supplied evidence. No suggestion was used.",
                    AiResponseFailureKind.ModelMisuse);
            }

            if (string.Equals(
                    NormalizeStemForComparison(suggestedStem),
                    NormalizeStemForComparison(Path.GetFileNameWithoutExtension(request.File.DisplayFileName)),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Failure<AiParsedFileRename>(
                    "The AI rename response did not propose a filename-stem change. No suggestion was used.",
                    AiResponseFailureKind.ModelMisuse);
            }

            if (!TryReadConfidence(root, out var confidence, out error))
            {
                return Failure<AiParsedFileRename>(error);
            }

            return new AiResponseParseResult<AiParsedFileRename>(
                new AiParsedFileRename(
                    sourceMapping.KnownSourceId,
                    suggestedStem,
                    normalizedFileName,
                    reason,
                    confidence),
                false,
                "A validated AI-generated rename suggestion is available for review.");
        }
    }

    /// <inheritdoc />
    public AiResponseParseResult<AiParsedDocumentInterpretation> ParseDocumentInterpretation(
        string response,
        AiDocumentTextRequest request,
        IReadOnlyList<AiPromptSourceMapping> sourceMappings)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceMappings);
        var mapping = sourceMappings.Count == 1 &&
                      string.Equals(sourceMappings[0].KnownSourceId, request.SourceFileId, StringComparison.Ordinal)
            ? sourceMappings[0]
            : null;
        if (mapping is null)
        {
            return Failure<AiParsedDocumentInterpretation>(
                "The known document identity mapping is invalid. No suggestion was used.",
                AiResponseFailureKind.UnsafeIdentity);
        }

        if (!TryOpen(response, out var document, out var error, out var openFailureKind))
        {
            return Failure<AiParsedDocumentInterpretation>(error, openFailureKind);
        }

        using (document)
        {
            var root = document.RootElement;
            if (!TryReadCommon(
                    root,
                    AiPromptBuilder.DocumentInterpretationTaskId,
                    out var status,
                    out var reason,
                    out error))
            {
                return Failure<AiParsedDocumentInterpretation>(error, ClassifyCommonError(error));
            }

            if (status == NoSuggestionStatus)
            {
                if (!HasOnlyProperties(root, ["taskId", "status", "reason"], out var unknown))
                {
                    return Failure<AiParsedDocumentInterpretation>(
                        "The AI no-suggestion response contained an unsupported or wrong-case property. No suggestion was used.",
                        IsMisuseProperty(unknown)
                            ? AiResponseFailureKind.ModelMisuse
                            : AiResponseFailureKind.RepairableFormatOrSchema);
                }

                return new AiResponseParseResult<AiParsedDocumentInterpretation>(null, true, reason);
            }

            if (!HasOnlyProperties(
                    root,
                    [
                        "taskId", "status", "sourceFileId", "documentType", "title", "tags",
                        "dates", "issuer", "suggestedFolder", "reason", "confidence",
                    ],
                    out var unknownProperty))
            {
                return Failure<AiParsedDocumentInterpretation>(
                    "The AI interpretation contained an unsupported or wrong-case property. No suggestion was used.",
                    IsMisuseProperty(unknownProperty)
                        ? AiResponseFailureKind.ModelMisuse
                        : AiResponseFailureKind.RepairableFormatOrSchema);
            }

            if (!TryReadRequiredString(root, "sourceFileId", 32, out var sourceId, out error))
            {
                return Failure<AiParsedDocumentInterpretation>(error);
            }

            if (!string.Equals(sourceId, mapping.RequestSourceId, StringComparison.Ordinal))
            {
                return Failure<AiParsedDocumentInterpretation>(
                    "The AI interpretation referenced an unknown source file. No suggestion was used.",
                    AiResponseFailureKind.UnsafeIdentity);
            }

            if (!TryReadNullableRequiredString(root, "documentType", 96, out var documentType, out error) ||
                !TryReadNullableRequiredString(root, "title", 240, out var title, out error) ||
                !TryReadNullableRequiredString(root, "issuer", 160, out var issuer, out error) ||
                !TryReadNullableRequiredString(
                    root,
                    "suggestedFolder",
                    AiSuggestionValidator.MaximumFolderComponentLength,
                    out var folder,
                    out error) ||
                !TryReadStringArray(root, "tags", 12, 64, out var rawTags, out error) ||
                !TryReadStringArray(root, "dates", 8, 10, out var dates, out error) ||
                !TryReadConfidence(root, out var confidence, out error))
            {
                return Failure<AiParsedDocumentInterpretation>(error);
            }

            if (!AiSuggestionValidator.TryNormalizeTags(rawTags, out var tags, out error))
            {
                return Failure<AiParsedDocumentInterpretation>(error);
            }

            if (dates.Any(date => !DateOnly.TryParseExact(
                    date,
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out _)))
            {
                return Failure<AiParsedDocumentInterpretation>(
                    "The AI interpretation contained an invalid date. No suggestion was used.");
            }

            string? safeFolder = null;
            if (folder is not null)
            {
                if (!AiSuggestionValidator.TryNormalizeFolderName(folder, out var normalizedFolder, out error))
                {
                    return Failure<AiParsedDocumentInterpretation>(
                        error,
                        LooksLikePathOrTraversal(folder)
                            ? AiResponseFailureKind.UnsafePath
                            : AiResponseFailureKind.ModelMisuse);
                }

                safeFolder = normalizedFolder;
            }

            if (documentType is null && title is null && issuer is null && safeFolder is null &&
                tags.Count == 0 && dates.Count == 0)
            {
                return Failure<AiParsedDocumentInterpretation>(
                    "The AI interpretation contained no reviewable values. No suggestion was used.",
                    AiResponseFailureKind.ModelMisuse);
            }

            if (!AiSuggestionValidator.AreDocumentInterpretationValuesGrounded(
                    request,
                    documentType,
                    title,
                    rawTags,
                    dates,
                    issuer,
                    safeFolder))
            {
                return Failure<AiParsedDocumentInterpretation>(
                    "The AI interpretation introduced values that were not present in the supplied filename or extracted text. No suggestion was used.",
                    AiResponseFailureKind.ModelMisuse);
            }

            return new AiResponseParseResult<AiParsedDocumentInterpretation>(
                new AiParsedDocumentInterpretation(
                    mapping.KnownSourceId,
                    documentType,
                    title,
                    tags,
                    dates,
                    issuer,
                    safeFolder,
                    reason,
                    confidence),
                false,
                "A validated unverified document interpretation is available for review.");
        }
    }

    /// <inheritdoc />
    public AiResponseParseResult<AiParsedFolderStructure> ParseFolderStructure(string response, IReadOnlyList<ResultFile> includedFiles) =>
        ParseFolderStructure(
            response,
            includedFiles,
            Array.AsReadOnly(includedFiles.Select(file => new AiPromptSourceMapping(file.Id, file.Id, file.DisplayFileName)).ToArray()));

    /// <inheritdoc />
    public AiResponseParseResult<AiParsedFolderStructure> ParseFolderStructure(
        string response,
        IReadOnlyList<ResultFile> includedFiles,
        IReadOnlyList<AiPromptSourceMapping> sourceMappings)
    {
        var allowedFolderNames = includedFiles
            .Select(file => file.ClassificationDisplay)
            .Append("Other")
            .Where(value => AiSuggestionValidator.TryNormalizeFolderName(value, out _, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return ParseFolderStructure(response, includedFiles, sourceMappings, allowedFolderNames);
    }

    /// <inheritdoc />
    public AiResponseParseResult<AiParsedFolderStructure> ParseFolderStructure(
        string response,
        IReadOnlyList<ResultFile> includedFiles,
        IReadOnlyList<AiPromptSourceMapping> sourceMappings,
        IReadOnlyList<string> allowedFolderNames)
    {
        ArgumentNullException.ThrowIfNull(includedFiles);
        ArgumentNullException.ThrowIfNull(sourceMappings);
        ArgumentNullException.ThrowIfNull(allowedFolderNames);
        if (includedFiles.Count == 0 || includedFiles.Any(file => file is null || string.IsNullOrWhiteSpace(file.Id)) ||
            includedFiles.Select(file => file.Id).Distinct(StringComparer.Ordinal).Count() != includedFiles.Count)
        {
            return Failure<AiParsedFolderStructure>(
                "The known folder-structure context is invalid. No suggestion was used.",
                AiResponseFailureKind.UnsafeIdentity);
        }

        if (sourceMappings.Count != includedFiles.Count ||
            sourceMappings.Select(mapping => mapping.RequestSourceId).Distinct(StringComparer.Ordinal).Count() != sourceMappings.Count ||
            sourceMappings.Select(mapping => mapping.KnownSourceId).Distinct(StringComparer.Ordinal).Count() != sourceMappings.Count ||
            sourceMappings.Any(mapping => !includedFiles.Any(file => string.Equals(file.Id, mapping.KnownSourceId, StringComparison.Ordinal))))
        {
            return Failure<AiParsedFolderStructure>(
                "The known folder-structure identity mapping is invalid. No suggestion was used.",
                AiResponseFailureKind.UnsafeIdentity);
        }

        if (!TryOpen(response, out var document, out var error, out var openFailureKind))
        {
            return Failure<AiParsedFolderStructure>(error, openFailureKind);
        }

        using (document)
        {
            var root = document.RootElement;
            if (!TryReadCommon(root, AiPromptBuilder.FolderStructureTaskId, out var status, out var reason, out error))
            {
                return Failure<AiParsedFolderStructure>(error, ClassifyCommonError(error));
            }

            if (status == NoSuggestionStatus)
            {
                if (!HasOnlyProperties(root, ["taskId", "status", "reason"], out var unknown))
                {
                    return Failure<AiParsedFolderStructure>(
                        "The AI no-suggestion response contained an unsupported or wrong-case property. No suggestion was used.",
                        IsMisuseProperty(unknown)
                            ? AiResponseFailureKind.ModelMisuse
                            : AiResponseFailureKind.RepairableFormatOrSchema);
                }

                return new AiResponseParseResult<AiParsedFolderStructure>(null, true, reason);
            }

            if (!HasOnlyProperties(
                    root,
                    ["taskId", "status", "folders", "assignments", "reason"],
                    out var unknownProperty))
            {
                return Failure<AiParsedFolderStructure>(
                    "The AI folder response contained an unsupported or wrong-case property. No suggestion was used.",
                    IsMisuseProperty(unknownProperty)
                        ? AiResponseFailureKind.ModelMisuse
                        : AiResponseFailureKind.RepairableFormatOrSchema);
            }

            if (!root.TryGetProperty("folders", out var foldersElement) || foldersElement.ValueKind != JsonValueKind.Array ||
                foldersElement.GetArrayLength() is 0 or > AiResponseLimits.MaximumFolders)
            {
                return Failure<AiParsedFolderStructure>(
                    "The AI folder response contains an invalid or excessive folder list. No suggestion was used.",
                    foldersElement.ValueKind == JsonValueKind.Array &&
                    foldersElement.GetArrayLength() > AiResponseLimits.MaximumFolders
                        ? AiResponseFailureKind.HardBound
                        : AiResponseFailureKind.RepairableFormatOrSchema);
            }

            var folderInputs = new Dictionary<string, FolderInput>(StringComparer.Ordinal);
            foreach (var folderElement in foldersElement.EnumerateArray())
            {
                if (folderElement.ValueKind != JsonValueKind.Object)
                {
                    return Failure<AiParsedFolderStructure>(
                        "The AI folder response contains a non-object folder record. No suggestion was used.");
                }

                if (!HasOnlyProperties(
                        folderElement,
                        ["folderId", "name", "parentFolderId"],
                        out var unknownFolderProperty))
                {
                    return Failure<AiParsedFolderStructure>(
                        "The AI folder response contained an unsupported or wrong-case folder property. No suggestion was used.",
                        IsMisuseProperty(unknownFolderProperty)
                            ? AiResponseFailureKind.ModelMisuse
                            : AiResponseFailureKind.RepairableFormatOrSchema);
                }

                var name = string.Empty;
                string? parentFolderId = null;
                if (!TryReadRequiredString(folderElement, "folderId", 10, out var folderId, out error) ||
                    !IsFolderId(folderId) ||
                    !TryReadRequiredString(
                        folderElement,
                        "name",
                        AiSuggestionValidator.MaximumFolderComponentLength,
                        out name,
                        out error) ||
                    !TryReadNullableRequiredString(folderElement, "parentFolderId", 10, out parentFolderId, out error) ||
                    parentFolderId is not null && !IsFolderId(parentFolderId))
                {
                    return Failure<AiParsedFolderStructure>(
                        string.IsNullOrWhiteSpace(error)
                            ? "The AI folder response contains an invalid folder identity. No suggestion was used."
                            : error,
                        LooksLikePathOrTraversal(name) ||
                        LooksLikePathOrTraversal(parentFolderId)
                            ? AiResponseFailureKind.UnsafePath
                            : AiResponseFailureKind.RepairableFormatOrSchema);
                }

                if (!AiSuggestionValidator.TryNormalizeFolderName(name, out var normalizedName, out error))
                {
                    return Failure<AiParsedFolderStructure>(
                        error,
                        LooksLikePathOrTraversal(name)
                            ? AiResponseFailureKind.UnsafePath
                            : AiResponseFailureKind.ModelMisuse);
                }

                if (!allowedFolderNames.Contains(normalizedName, StringComparer.OrdinalIgnoreCase))
                {
                    return Failure<AiParsedFolderStructure>(
                        "The AI folder response invented a folder name that was not declared in allowedFolderNames. No suggestion was used.",
                        AiResponseFailureKind.ModelMisuse);
                }

                if (!folderInputs.TryAdd(folderId, new FolderInput(folderId, normalizedName, parentFolderId)))
                {
                    return Failure<AiParsedFolderStructure>(
                        "The AI folder response contains duplicate folder identities. No suggestion was used.",
                        AiResponseFailureKind.UnsafeIdentity);
                }
            }

            foreach (var folder in folderInputs.Values)
            {
                if (folder.ParentFolderId is not null &&
                    !folderInputs.ContainsKey(folder.ParentFolderId))
                {
                    return Failure<AiParsedFolderStructure>(
                        "The AI folder response referenced an unknown parent folder identity. No suggestion was used.",
                        AiResponseFailureKind.UnsafeIdentity);
                }

                if (folder.ParentFolderId is not null &&
                    string.Equals(folder.FolderId, folder.ParentFolderId, StringComparison.Ordinal))
                {
                    return Failure<AiParsedFolderStructure>(
                        "The AI folder response contains a circular parent folder. No suggestion was used.",
                        AiResponseFailureKind.ModelMisuse);
                }
            }

            var paths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var folderId in folderInputs.Keys.Order(StringComparer.Ordinal))
            {
                if (!TryBuildLogicalPath(folderId, folderInputs, paths, new HashSet<string>(StringComparer.Ordinal), out _, out error))
                {
                    return Failure<AiParsedFolderStructure>(error, AiResponseFailureKind.ModelMisuse);
                }
            }

            if (paths.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Count)
            {
                return Failure<AiParsedFolderStructure>(
                    "The AI folder response contains duplicate logical folder paths. No suggestion was used.",
                    AiResponseFailureKind.ModelMisuse);
            }

            if (!root.TryGetProperty("assignments", out var assignmentsElement) ||
                assignmentsElement.ValueKind != JsonValueKind.Array ||
                assignmentsElement.GetArrayLength() is 0 or > AiResponseLimits.MaximumAssignments)
            {
                return Failure<AiParsedFolderStructure>(
                    "The AI folder response contains an invalid or excessive assignment list. No suggestion was used.",
                    assignmentsElement.ValueKind == JsonValueKind.Array &&
                    assignmentsElement.GetArrayLength() > AiResponseLimits.MaximumAssignments
                        ? AiResponseFailureKind.HardBound
                        : AiResponseFailureKind.RepairableFormatOrSchema);
            }

            if (assignmentsElement.GetArrayLength() != includedFiles.Count)
            {
                return Failure<AiParsedFolderStructure>(
                    "The AI folder response did not contain exactly one assignment for every supplied source file. No suggestion was used.",
                    AiResponseFailureKind.UnsafeIdentity);
            }

            var knownFiles = includedFiles.ToDictionary(file => file.Id, StringComparer.Ordinal);
            var sourceMap = sourceMappings.ToDictionary(mapping => mapping.RequestSourceId, StringComparer.Ordinal);
            var assignedFiles = new HashSet<string>(StringComparer.Ordinal);
            var items = new List<AiFolderStructurePlanItem>();
            foreach (var assignmentElement in assignmentsElement.EnumerateArray())
            {
                if (assignmentElement.ValueKind != JsonValueKind.Object)
                {
                    return Failure<AiParsedFolderStructure>(
                        "The AI folder response contains a non-object assignment. No suggestion was used.");
                }

                if (!HasOnlyProperties(
                        assignmentElement,
                        ["sourceFileId", "folderId"],
                        out var unknownAssignmentProperty))
                {
                    return Failure<AiParsedFolderStructure>(
                        "The AI folder response contained an unsupported or wrong-case assignment property. No suggestion was used.",
                        IsMisuseProperty(unknownAssignmentProperty)
                            ? AiResponseFailureKind.ModelMisuse
                            : AiResponseFailureKind.RepairableFormatOrSchema);
                }

                if (!TryReadRequiredString(assignmentElement, "sourceFileId", 32, out var sourceFileId, out error) ||
                    !TryReadRequiredString(assignmentElement, "folderId", 10, out var folderId, out error))
                {
                    return Failure<AiParsedFolderStructure>(error);
                }

                if (
                    !sourceMap.TryGetValue(sourceFileId, out var mapping) ||
                    !knownFiles.TryGetValue(mapping.KnownSourceId, out var file) ||
                    !paths.TryGetValue(folderId, out var logicalPath))
                {
                    return Failure<AiParsedFolderStructure>(
                        "The AI folder response referenced an unknown source file or folder. No suggestion was used.",
                        AiResponseFailureKind.UnsafeIdentity);
                }

                if (!assignedFiles.Add(sourceFileId))
                {
                    return Failure<AiParsedFolderStructure>(
                        "The AI folder response assigned the same source file more than once. No suggestion was used.",
                        AiResponseFailureKind.UnsafeIdentity);
                }

                items.Add(new AiFolderStructurePlanItem(file.Id, file.DisplayFileName, logicalPath));
            }

            if (assignedFiles.Count != sourceMappings.Count)
            {
                return Failure<AiParsedFolderStructure>(
                    "The AI folder response did not assign every supplied source file exactly once. No suggestion was used.",
                    AiResponseFailureKind.UnsafeIdentity);
            }

            var folders = folderInputs.Values
                .OrderBy(folder => paths[folder.FolderId], StringComparer.Ordinal)
                .Select(folder => new AiSuggestedFolder(
                    folder.FolderId,
                    folder.Name,
                    folder.ParentFolderId,
                    paths[folder.FolderId],
                    reason,
                    null))
                .ToArray();
            var orderedItems = items
                .OrderBy(item => item.FileId, StringComparer.Ordinal)
                .ThenBy(item => item.DestinationFolder, StringComparer.Ordinal)
                .ToArray();

            return new AiResponseParseResult<AiParsedFolderStructure>(
                new AiParsedFolderStructure(Array.AsReadOnly(folders), Array.AsReadOnly(orderedItems), reason),
                false,
                "A validated AI-generated folder-structure suggestion is available for review.");
        }
    }

    private static bool TryOpen(
        string response,
        out JsonDocument document,
        out string error,
        out AiResponseFailureKind failureKind)
    {
        document = default!;
        error = string.Empty;
        failureKind = AiResponseFailureKind.RepairableFormatOrSchema;
        if (string.IsNullOrWhiteSpace(response))
        {
            error = "The AI returned an empty structured response. No suggestion was used.";
            failureKind = AiResponseFailureKind.HardBound;
            return false;
        }

        var trimmed = response.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            error = "The AI wrapped its response in Markdown instead of returning the required JSON. No suggestion was used.";
            return false;
        }

        if (Encoding.UTF8.GetByteCount(trimmed) > AiResponseLimits.MaximumStructuredResponseBytes)
        {
            error = "The AI returned an excessively large structured response. No suggestion was used.";
            failureKind = AiResponseFailureKind.HardBound;
            return false;
        }

        try
        {
            document = JsonDocument.Parse(trimmed, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
        }
        catch (JsonException)
        {
            error = "The AI returned malformed JSON. No suggestion was used.";
            return false;
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            document = default!;
            error = "The AI response did not have the required JSON object shape. No suggestion was used.";
            return false;
        }

        failureKind = AiResponseFailureKind.None;
        return true;
    }

    private static bool TryReadCommon(
        JsonElement root,
        string expectedTaskId,
        out string status,
        out string reason,
        out string error)
    {
        status = string.Empty;
        reason = string.Empty;
        if (!TryReadRequiredString(root, "taskId", 64, out var taskId, out error))
        {
            return false;
        }

        if (!string.Equals(taskId, expectedTaskId, StringComparison.Ordinal))
        {
            error = "The AI response used an unexpected task identifier. No suggestion was used.";
            return false;
        }

        if (!TryReadRequiredString(root, "status", 32, out status, out error) ||
            status is not (SuggestionStatus or NoSuggestionStatus))
        {
            error = "The AI response used an unsupported status. No suggestion was used.";
            return false;
        }

        return TryReadRequiredString(root, "reason", AiResponseLimits.MaximumReasonLength, out reason, out error);
    }

    private static bool TryReadRequiredString(
        JsonElement element,
        string propertyName,
        int maximumLength,
        out string value,
        out string error)
    {
        value = string.Empty;
        error = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            error = $"Expected `{propertyName}` to be a non-empty string, but the property was missing. No suggestion was used.";
            return false;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            error = $"Expected `{propertyName}` to be a non-empty string, but received {property.ValueKind.ToString().ToLowerInvariant()}. No suggestion was used.";
            return false;
        }

        var candidate = property.GetString();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > maximumLength || candidate.Any(char.IsControl))
        {
            error = string.IsNullOrWhiteSpace(candidate)
                ? $"Expected `{propertyName}` to be a non-empty string, but received an empty string. No suggestion was used."
                : $"Expected `{propertyName}` to be a string no longer than {maximumLength} characters without control characters. No suggestion was used.";
            return false;
        }

        value = candidate.Trim();
        return true;
    }

    private static bool TryReadNullableRequiredString(
        JsonElement element,
        string propertyName,
        int maximumLength,
        out string? value,
        out string error)
    {
        value = null;
        error = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            error = $"The AI response is missing the required '{propertyName}' property. No suggestion was used.";
            return false;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            error = $"The AI response contains an invalid '{propertyName}' value. No suggestion was used.";
            return false;
        }

        var candidate = property.GetString();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > maximumLength || candidate.Any(char.IsControl))
        {
            error = $"The AI response contains an invalid '{propertyName}' value. No suggestion was used.";
            return false;
        }

        value = candidate.Trim();
        return true;
    }

    private static bool TryReadConfidence(JsonElement element, out double? confidence, out string error)
    {
        confidence = null;
        error = string.Empty;
        if (!element.TryGetProperty("confidence", out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetDouble(out var value) ||
            double.IsNaN(value) || double.IsInfinity(value) || value is < 0 or > 1)
        {
            error = "The AI response contains an invalid confidence value. No suggestion was used.";
            return false;
        }

        confidence = value;
        return true;
    }

    private static bool TryReadStringArray(
        JsonElement element,
        string propertyName,
        int maximumCount,
        int maximumItemLength,
        out IReadOnlyList<string> values,
        out string error)
    {
        values = [];
        error = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array ||
            property.GetArrayLength() > maximumCount)
        {
            error = $"The AI response contains an invalid '{propertyName}' array. No suggestion was used.";
            return false;
        }

        var parsed = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(item.GetString()) ||
                item.GetString()!.Length > maximumItemLength ||
                item.GetString()!.Any(char.IsControl))
            {
                error = $"The AI response contains an invalid '{propertyName}' value. No suggestion was used.";
                return false;
            }

            parsed.Add(item.GetString()!.Trim());
        }

        values = Array.AsReadOnly(parsed.Distinct(StringComparer.Ordinal).ToArray());
        return true;
    }

    private static bool TryBuildLogicalPath(
        string folderId,
        IReadOnlyDictionary<string, FolderInput> folders,
        IDictionary<string, string> completedPaths,
        ISet<string> visiting,
        out string logicalPath,
        out string error)
    {
        if (completedPaths.TryGetValue(folderId, out logicalPath!))
        {
            error = string.Empty;
            return true;
        }

        if (!visiting.Add(folderId))
        {
            logicalPath = string.Empty;
            error = "The AI folder response contains a circular folder hierarchy. No suggestion was used.";
            return false;
        }

        var folder = folders[folderId];
        if (folder.ParentFolderId is null)
        {
            logicalPath = folder.Name;
        }
        else if (!TryBuildLogicalPath(folder.ParentFolderId, folders, completedPaths, visiting, out var parentPath, out error))
        {
            logicalPath = string.Empty;
            return false;
        }
        else
        {
            logicalPath = $"{parentPath}/{folder.Name}";
        }

        if (logicalPath.Length > 512 ||
            logicalPath.Count(character => character == '/') + 1 > AiResponseLimits.MaximumFolderDepth)
        {
            visiting.Remove(folderId);
            logicalPath = string.Empty;
            error = "The AI folder response exceeds the maximum folder depth or logical-path length. No suggestion was used.";
            return false;
        }

        visiting.Remove(folderId);
        completedPaths[folderId] = logicalPath;
        error = string.Empty;
        return true;
    }

    private static AiResponseParseResult<T> Failure<T>(
        string error,
        AiResponseFailureKind failureKind = AiResponseFailureKind.RepairableFormatOrSchema)
        where T : class => new(
            null,
            false,
            string.IsNullOrWhiteSpace(error)
                ? "The AI response was invalid. No suggestion was used."
                : error,
            failureKind);

    private static bool HasOnlyProperties(
        JsonElement element,
        IReadOnlyCollection<string> allowedProperties,
        out string unknownProperty)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name) ||
                !allowedProperties.Contains(property.Name, StringComparer.Ordinal))
            {
                unknownProperty = property.Name;
                return false;
            }
        }

        unknownProperty = string.Empty;
        return true;
    }

    private static bool IsFolderId(string value) =>
        value.Length == 10 &&
        value.StartsWith("folder-", StringComparison.Ordinal) &&
        value.Skip(7).All(character => character is >= '0' and <= '9');

    private static bool IsMisuseProperty(string propertyName) =>
        propertyName.Contains("path", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("command", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("action", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("operation", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("alternative", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("fileNames", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("suggestions", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("options", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("metadata", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("issuer", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("title", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("tag", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("date", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikePathOrTraversal(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (Path.IsPathRooted(value) ||
         value.Contains("..", StringComparison.Ordinal) ||
         value.Contains('/') ||
         value.Contains('\\') ||
         value.Contains(':'));

    private static AiResponseFailureKind ClassifyCommonError(string error) =>
        error.Contains("unexpected task identifier", StringComparison.Ordinal)
            ? AiResponseFailureKind.ModelMisuse
            : AiResponseFailureKind.RepairableFormatOrSchema;

    private static string NormalizeStemForComparison(string value) =>
        string.Join(
            '-',
            value.Split(
                [' ', '_', '-', '.'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private sealed record FolderInput(
        string FolderId,
        string Name,
        string? ParentFolderId);
}
