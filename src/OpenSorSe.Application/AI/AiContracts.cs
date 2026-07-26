using OpenSorSe.Application.Models;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.AI;

/// <summary>
/// Describes the user-visible state of the optional AI integration.
/// </summary>
public enum AiAvailabilityState
{
    /// <summary>AI requests are disabled by the global setting.</summary>
    Disabled,
    /// <summary>The requested capability is independently disabled.</summary>
    CapabilityDisabled,
    /// <summary>The supplied user context is not eligible for the capability.</summary>
    InvalidContext,
    /// <summary>The configured provider could not be reached.</summary>
    Unavailable,
    /// <summary>The provider is being contacted.</summary>
    Connecting,
    /// <summary>The provider is reachable but no model selection has been made.</summary>
    Connected,
    /// <summary>The provider is reachable but exposes no installed models.</summary>
    NoModelsAvailable,
    /// <summary>The configured model is not installed or available.</summary>
    ModelUnavailable,
    /// <summary>A usable model has been selected.</summary>
    ModelSelected,
    /// <summary>An AI request is running.</summary>
    RequestRunning,
    /// <summary>The AI returned an explicit, valid no-suggestion result.</summary>
    NoSuggestion,
    /// <summary>An AI request was cancelled.</summary>
    RequestCancelled,
    /// <summary>The provider returned data that did not pass application validation.</summary>
    ResponseInvalid,
}

/// <summary>
/// Identifies the bounded suggestion workflow requested from an optional provider.
/// </summary>
public enum AiSuggestionKind
{
    /// <summary>Tests the configured Ollama endpoint without generation.</summary>
    ConnectionTest,
    /// <summary>Discovers installed Ollama models without generation.</summary>
    ModelDiscovery,
    /// <summary>Suggests a safe replacement filename for one known file.</summary>
    FileRename,
    /// <summary>Suggests a preview-only logical hierarchy for bounded known metadata.</summary>
    FolderStructure,
    /// <summary>Interprets bounded locally extracted document text for review.</summary>
    DocumentTextInterpretation,
}

/// <summary>Identifies truthful progress stages for one explicit AI request.</summary>
public enum AiRequestStage
{
    /// <summary>Checking settings and capability gates.</summary>
    CheckingSettings,
    /// <summary>Connecting to the configured endpoint.</summary>
    Connecting,
    /// <summary>Checking that the exact selected model is installed.</summary>
    ValidatingModel,
    /// <summary>Preparing bounded filename metadata.</summary>
    PreparingMetadata,
    /// <summary>Sending the structured request.</summary>
    SendingRequest,
    /// <summary>Waiting for the model.</summary>
    WaitingForModel,
    /// <summary>Receiving the bounded provider response.</summary>
    ReceivingResponse,
    /// <summary>Validating the complete untrusted response.</summary>
    ValidatingSuggestion,
    /// <summary>A validated review-only suggestion is ready.</summary>
    SuggestionReady,
    /// <summary>The caller cancelled the request.</summary>
    RequestCancelled,
    /// <summary>The finite request timeout elapsed.</summary>
    RequestTimedOut,
    /// <summary>The request failed safely.</summary>
    RequestFailed,
}

/// <summary>Reports one typed progress transition and elapsed duration.</summary>
public sealed record AiRequestProgress(AiRequestStage Stage, string Message, TimeSpan Elapsed);

/// <summary>
/// Identifies a safe, user-visible provider failure category.
/// </summary>
public enum AiProviderFailureKind
{
    /// <summary>No failure occurred.</summary>
    None,
    /// <summary>The request configuration is incomplete or invalid.</summary>
    Configuration,
    /// <summary>The endpoint could not be reached.</summary>
    Unavailable,
    /// <summary>The configured model is unavailable.</summary>
    ModelUnavailable,
    /// <summary>The model or provider rejected the required structured behavior.</summary>
    UnsupportedResponse,
    /// <summary>The request exceeded its configured time limit.</summary>
    Timeout,
    /// <summary>The user cancelled the request.</summary>
    Cancelled,
    /// <summary>The provider returned an unsupported, empty, malformed, or oversized response.</summary>
    InvalidResponse,
    /// <summary>The provider returned another unsuccessful HTTP result.</summary>
    HttpFailure,
}

/// <summary>
/// Identifies the distinct user decisions retained for compatible local preference history.
/// </summary>
public enum AiSuggestionDecisionKind
{
    /// <summary>A filename suggestion was reviewed.</summary>
    Rename,
    /// <summary>A historical tag suggestion was reviewed.</summary>
    Tags,
    /// <summary>A historical category suggestion was reviewed.</summary>
    Category,
    /// <summary>A historical destination-folder suggestion was reviewed.</summary>
    DestinationFolder,
    /// <summary>A preview-only folder-structure plan was reviewed.</summary>
    FolderStructure,
}

/// <summary>
/// Identifies the result of a user review decision.
/// </summary>
public enum AiSuggestionDecisionOutcome
{
    /// <summary>The supplied value was accepted without an edit.</summary>
    Accepted,
    /// <summary>The supplied value was rejected.</summary>
    Rejected,
    /// <summary>The supplied value was accepted with an explicit edit.</summary>
    Edited,
}

/// <summary>Represents a model discovered from an optional AI provider.</summary>
public sealed record AiModel(string Id, string DisplayName);

/// <summary>Represents a user-safe provider connection result.</summary>
public sealed record AiConnectionResult(
    AiAvailabilityState State,
    string Message,
    IReadOnlyList<AiModel> Models)
{
    /// <summary>Gets the normalized endpoint used for the concrete check.</summary>
    public string? NormalizedEndpoint { get; init; }

    /// <summary>Gets the provider version when reported by a dedicated connection check.</summary>
    public string? ProviderVersion { get; init; }

    /// <summary>Gets the HTTP status returned by the provider when available.</summary>
    public int? HttpStatusCode { get; init; }

    /// <summary>Gets the concrete request duration.</summary>
    public TimeSpan? Elapsed { get; init; }
}

/// <summary>Describes safe metadata for one explicit file-rename request.</summary>
public sealed record AiFileRenameRequest(
    ResultFile File,
    IReadOnlyList<string> SiblingFileNames);

/// <summary>Describes a bounded in-memory collection supplied to a folder-structure workflow.</summary>
public sealed record AiFolderStructureRequest(
    IReadOnlyList<ResultFile> Files,
    IReadOnlyList<string> ExistingFolderNames);

/// <summary>Describes bounded extracted text for one explicit review-only interpretation request.</summary>
public sealed record AiDocumentTextRequest(
    string SourceFileId,
    string DisplayFileName,
    string? NativeText,
    string? OcrText,
    IReadOnlyList<OpenSorSe.Application.Content.OcrPageResult> Pages)
{
    /// <summary>Gets the related OCR or extraction diagnostic session, when known.</summary>
    public string? RelatedDiagnosticSessionId { get; init; }
}

/// <summary>Represents an application-normalized tag retained for existing deterministic and historical workflows.</summary>
public sealed record SuggestedTag(string DisplayName, string NormalizedValue);

/// <summary>Represents one validated, review-only filename proposal.</summary>
public sealed record AiFileRenameSuggestion(
    string SuggestionId,
    string SourceFileId,
    string SuggestedFileName,
    string Reason,
    double? Confidence,
    string Provider,
    string Model,
    DateTimeOffset GeneratedAtUtc);

/// <summary>Represents one validated logical folder in a preview-only hierarchy.</summary>
public sealed record AiSuggestedFolder(
    string FolderId,
    string Name,
    string? ParentFolderId,
    string LogicalPath,
    string Reason,
    double? Confidence);

/// <summary>Represents one validated known-file assignment in a preview-only hierarchy.</summary>
public sealed record AiFolderStructurePlanItem(string FileId, string FileName, string DestinationFolder);

/// <summary>Represents a validated, preview-only proposed folder structure.</summary>
public sealed record AiFolderStructurePlan(
    string PlanId,
    IReadOnlyList<AiSuggestedFolder> Folders,
    IReadOnlyList<AiFolderStructurePlanItem> Items,
    string Reason,
    string Provider,
    string Model,
    DateTimeOffset GeneratedAtUtc);

/// <summary>Represents one validated, unverified interpretation of bounded extracted document text.</summary>
public sealed record AiDocumentInterpretationSuggestion(
    string SuggestionId,
    string SourceFileId,
    string? DocumentType,
    string? Title,
    IReadOnlyList<SuggestedTag> Tags,
    IReadOnlyList<string> Dates,
    string? Issuer,
    string? SuggestedFolder,
    string Reason,
    double? Confidence,
    string Provider,
    string Model,
    DateTimeOffset GeneratedAtUtc);

/// <summary>Contains a safe result of generating one filename suggestion.</summary>
public sealed record AiFileRenameResult(
    AiAvailabilityState State,
    string Message,
    AiFileRenameSuggestion? Suggestion,
    bool WasInputBounded = false);

/// <summary>Contains a safe result of generating one preview-only folder-structure plan.</summary>
public sealed record AiFolderStructureResult(
    AiAvailabilityState State,
    string Message,
    AiFolderStructurePlan? Plan,
    bool WasInputBounded = false);

/// <summary>Contains a safe result for one review-only document-text interpretation.</summary>
public sealed record AiDocumentInterpretationResult(
    AiAvailabilityState State,
    string Message,
    AiDocumentInterpretationSuggestion? Suggestion,
    bool WasInputBounded = false);

/// <summary>Contains a safe result for a local AI review-history operation.</summary>
public sealed record AiDecisionResult(AiAvailabilityState State, string Message);

/// <summary>Records one local, inspectable user decision without storing a source path or document content.</summary>
public sealed record AiSuggestionDecision(
    AiSuggestionDecisionKind Kind,
    AiSuggestionDecisionOutcome Outcome,
    string? Extension,
    string SuggestedValue,
    string? FinalValue,
    string Provider,
    string Model,
    DateTimeOffset RecordedAtUtc);

/// <summary>Provides compact, deterministic preference signals derived from local decision history.</summary>
public sealed record AiPreferenceSummary(
    IReadOnlyList<string> PreferredTags,
    IReadOnlyList<string> PreferredFolders,
    IReadOnlyList<string> PreferredCategories,
    IReadOnlyList<string> RejectedValues);

/// <summary>Contains a provider-neutral generation request. Prompt text is never logged in full.</summary>
public sealed record AiProviderGenerationRequest(
    AiSuggestionKind Kind,
    string Endpoint,
    string Model,
    string Prompt,
    TimeSpan Timeout)
{
    /// <summary>Gets the exact system prompt sent to the provider.</summary>
    public string SystemPrompt { get; init; } =
        Kind is AiSuggestionKind.FileRename or
            AiSuggestionKind.FolderStructure or
            AiSuggestionKind.DocumentTextInterpretation
            ? AiStructuredOutputContracts.GetSystemPrompt(Kind)
            : string.Empty;

    /// <summary>Gets the exact user prompt sent to the provider.</summary>
    public string UserPrompt => Prompt;

    /// <summary>Gets the optional live diagnostic request identity.</summary>
    public string? DiagnosticRequestId { get; init; }

    /// <summary>Gets optional typed progress reporting owned by the current explicit request.</summary>
    public IProgress<AiRequestProgress>? Progress { get; init; }
}

/// <summary>Contains concrete provider transport facts for opt-in diagnostics.</summary>
public sealed record AiProviderRequestDiagnostics(
    string NormalizedEndpoint,
    int? HttpStatusCode,
    TimeSpan Elapsed,
    int ResponseCharacterCount,
    int ResponseByteCount,
    string RawResponse)
{
    /// <summary>Gets the complete raw HTTP envelope body before parsing.</summary>
    public string RawHttpResponse { get; init; } = RawResponse;

    /// <summary>Gets the assistant content extracted from the envelope.</summary>
    public string ExtractedAssistantResponse { get; init; } = string.Empty;

    /// <summary>Gets the exact serialized request JSON.</summary>
    public string RequestJson { get; init; } = string.Empty;

    /// <summary>Gets the safe response content type.</summary>
    public string ContentType { get; init; } = string.Empty;
}

/// <summary>Contains a provider-neutral generation response before application-owned validation.</summary>
public sealed record AiProviderGenerationResult(
    string? StructuredJson,
    AiProviderFailureKind FailureKind,
    string Message)
{
    /// <summary>Gets whether a structured response is available for application validation.</summary>
    public bool IsSuccess => FailureKind == AiProviderFailureKind.None && !string.IsNullOrWhiteSpace(StructuredJson);

    /// <summary>Gets bounded transport facts for opt-in session diagnostics.</summary>
    public AiProviderRequestDiagnostics? Diagnostics { get; init; }
}

/// <summary>Defines the narrow provider boundary used by application-owned suggestion workflows.</summary>
public interface IAiSuggestionProvider
{
    /// <summary>Checks endpoint reachability without discovering or selecting models.</summary>
    Task<AiConnectionResult> CheckConnectionAsync(AiSettings settings, CancellationToken cancellationToken) =>
        GetConnectionAsync(settings, cancellationToken);

    /// <summary>Checks endpoint reachability while optionally publishing into an existing diagnostic session.</summary>
    Task<AiConnectionResult> CheckConnectionAsync(AiSettings settings, string? diagnosticRequestId, CancellationToken cancellationToken) =>
        CheckConnectionAsync(settings, cancellationToken);

    /// <summary>Checks provider availability and lists installed models.</summary>
    Task<AiConnectionResult> GetConnectionAsync(AiSettings settings, CancellationToken cancellationToken);

    /// <summary>Discovers models while optionally publishing into an existing diagnostic session.</summary>
    Task<AiConnectionResult> GetConnectionAsync(AiSettings settings, string? diagnosticRequestId, CancellationToken cancellationToken) =>
        GetConnectionAsync(settings, cancellationToken);

    /// <summary>Requests one structured provider response without parsing it into application models.</summary>
    Task<AiProviderGenerationResult> GenerateAsync(AiProviderGenerationRequest request, CancellationToken cancellationToken);
}

/// <summary>Persists local, user-review decisions independently from scan results.</summary>
public interface IDecisionHistoryStore
{
    /// <summary>Loads valid persisted decisions in chronological order.</summary>
    Task<IReadOnlyList<AiSuggestionDecision>> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Appends one validated local decision.</summary>
    Task AppendAsync(AiSuggestionDecision decision, CancellationToken cancellationToken);

    /// <summary>Removes all persisted local decision history.</summary>
    Task ClearAsync(CancellationToken cancellationToken);
}

/// <summary>Provides application-owned, validated suggestion workflows and local preference adaptation.</summary>
public interface IAiSuggestionService
{
    /// <summary>Tests the configured optional provider without generating a suggestion.</summary>
    Task<AiConnectionResult> TestConnectionAsync(ApplicationSettings settings, CancellationToken cancellationToken);

    /// <summary>Discovers models from the configured optional provider.</summary>
    Task<AiConnectionResult> DiscoverModelsAsync(ApplicationSettings settings, CancellationToken cancellationToken);

    /// <summary>Generates one safe, review-only filename suggestion.</summary>
    Task<AiFileRenameResult> GenerateFileRenameAsync(AiFileRenameRequest request, AiSettings settings, CancellationToken cancellationToken);

    /// <summary>Generates one safe rename suggestion with typed progress.</summary>
    Task<AiFileRenameResult> GenerateFileRenameAsync(
        AiFileRenameRequest request,
        AiSettings settings,
        IProgress<AiRequestProgress>? progress,
        CancellationToken cancellationToken) =>
        GenerateFileRenameAsync(request, settings, cancellationToken);

    /// <summary>Generates one safe, preview-only folder-structure plan.</summary>
    Task<AiFolderStructureResult> GenerateFolderStructureAsync(AiFolderStructureRequest request, AiSettings settings, CancellationToken cancellationToken);

    /// <summary>Generates one safe folder plan with typed progress.</summary>
    Task<AiFolderStructureResult> GenerateFolderStructureAsync(
        AiFolderStructureRequest request,
        AiSettings settings,
        IProgress<AiRequestProgress>? progress,
        CancellationToken cancellationToken) =>
        GenerateFolderStructureAsync(request, settings, cancellationToken);

    /// <summary>Interprets bounded extracted text only after the separate capability gate is enabled.</summary>
    Task<AiDocumentInterpretationResult> GenerateDocumentInterpretationAsync(
        AiDocumentTextRequest request,
        AiSettings settings,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AiDocumentInterpretationResult(
            AiAvailabilityState.CapabilityDisabled,
            "AI analysis of extracted document text is unavailable.",
            null));

    /// <summary>Records a user review decision for optional local preference adaptation.</summary>
    Task<AiDecisionResult> RecordDecisionAsync(AiSuggestionDecision decision, AiSettings settings, CancellationToken cancellationToken);

    /// <summary>Removes all local preference signals and recorded decisions.</summary>
    Task<AiDecisionResult> ResetDecisionHistoryAsync(ApplicationSettings settings, CancellationToken cancellationToken);
}
