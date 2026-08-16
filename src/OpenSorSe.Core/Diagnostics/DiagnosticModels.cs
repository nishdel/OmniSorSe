namespace OpenSorSe.Core.Diagnostics;

/// <summary>Identifies one independently configurable advanced-diagnostic feature area.</summary>
public enum DiagnosticCategory
{
    /// <summary>Optional AI provider requests and validation.</summary>
    Ai,
    /// <summary>Native text extraction, OCR decisions, rendering, and recognized text.</summary>
    OcrAndTextExtraction,
    /// <summary>Filesystem discovery and metadata-read decisions.</summary>
    Scanning,
    /// <summary>Exact and future duplicate-detection diagnostics.</summary>
    DuplicateDetection,
    /// <summary>Search, saved-query, and index diagnostics.</summary>
    SearchAndIndexing,
    /// <summary>Rule evaluation and organization-planning diagnostics.</summary>
    RulesAndOrganisation,
    /// <summary>File execution and undo diagnostics.</summary>
    FileOperations,
    /// <summary>Cross-feature timing and resource diagnostics.</summary>
    Performance,
}

/// <summary>Identifies the lifecycle state of a diagnostic session or event.</summary>
public enum DiagnosticStatus
{
    /// <summary>The operation is still running.</summary>
    Active,
    /// <summary>The operation completed successfully.</summary>
    Succeeded,
    /// <summary>The operation completed with usable partial output.</summary>
    PartiallySucceeded,
    /// <summary>The operation deliberately did not run.</summary>
    Skipped,
    /// <summary>The operation produced data that was safely rejected.</summary>
    Rejected,
    /// <summary>The caller cancelled the operation.</summary>
    Cancelled,
    /// <summary>The operation failed safely.</summary>
    Failed,
}

/// <summary>Identifies the user-visible importance of one diagnostic event.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Ordinary lifecycle or data-flow information.</summary>
    Information,
    /// <summary>A recoverable warning, bound, sample, or partial result.</summary>
    Warning,
    /// <summary>A failed stage or controlled error.</summary>
    Error,
}

/// <summary>Maps captured data into the shared diagnostics-viewer tabs.</summary>
public enum DiagnosticSection
{
    /// <summary>High-level request and result facts.</summary>
    Overview,
    /// <summary>Inputs supplied to the operation.</summary>
    Inputs,
    /// <summary>Values produced between input and final output.</summary>
    IntermediateResults,
    /// <summary>Final values supplied downstream or returned.</summary>
    Outputs,
    /// <summary>Warnings, validation failures, and controlled errors.</summary>
    WarningsAndErrors,
    /// <summary>Timing, sizes, counts, and resource facts.</summary>
    Performance,
}

/// <summary>Classifies a captured value for category-aware retention redaction.</summary>
public enum DiagnosticDataClassification
{
    /// <summary>Safe bounded metadata such as counts, statuses, and non-secret identifiers.</summary>
    Safe,
    /// <summary>A local path, file name, or folder name.</summary>
    Path,
    /// <summary>Document, OCR, prompt, response, or search content.</summary>
    Content,
    /// <summary>Potentially identifying file metadata, tags, or query terms.</summary>
    Metadata,
    /// <summary>A credential or secret that is never retained.</summary>
    Secret,
}

/// <summary>Identifies the privacy mode applied before a diagnostic session was retained.</summary>
public enum DiagnosticContentRetentionMode
{
    /// <summary>Paths, content, and metadata were redacted before retention.</summary>
    Redacted,
    /// <summary>Classified content was retained exactly, while credentials and secrets were still removed.</summary>
    UnredactedSecretsRemoved,
}

/// <summary>Contains one named, classified diagnostic value.</summary>
/// <param name="Name">The stable display name.</param>
/// <param name="Value">The captured value before the store applies bounds and redaction.</param>
/// <param name="Classification">The privacy classification used before retention.</param>
public sealed record DiagnosticField(
    string Name,
    string Value,
    DiagnosticDataClassification Classification = DiagnosticDataClassification.Safe);

/// <summary>Contains one immutable, ordered event in a diagnostic session.</summary>
/// <param name="Sequence">The monotonically increasing process-local event sequence.</param>
/// <param name="SessionId">The owning diagnostic session.</param>
/// <param name="TimestampUtc">The UTC capture time.</param>
/// <param name="Stage">The stable feature stage.</param>
/// <param name="Status">The stage state after this event.</param>
/// <param name="Severity">The user-visible event severity.</param>
/// <param name="Section">The shared viewer section that owns the event details.</param>
/// <param name="Message">A concise bounded summary.</param>
/// <param name="Fields">The immutable bounded and redacted values.</param>
public sealed record DiagnosticEvent(
    long Sequence,
    string SessionId,
    DateTimeOffset TimestampUtc,
    string Stage,
    DiagnosticStatus Status,
    DiagnosticSeverity Severity,
    DiagnosticSection Section,
    string Message,
    IReadOnlyList<DiagnosticField> Fields);

/// <summary>Contains one immutable, bounded process-session diagnostic snapshot.</summary>
/// <param name="SessionId">The stable process-local request identity.</param>
/// <param name="Category">The feature category.</param>
/// <param name="Operation">The concrete feature operation.</param>
/// <param name="StartedAtUtc">The UTC start time.</param>
/// <param name="CompletedAtUtc">The UTC completion time, when terminal.</param>
/// <param name="Elapsed">The latest elapsed duration.</param>
/// <param name="Status">The current or final status.</param>
/// <param name="ContentRetentionMode">The privacy mode applied before this immutable snapshot was retained.</param>
/// <param name="RelatedSessionIds">Bounded parent and downstream correlation identities.</param>
/// <param name="Context">Bounded session-level inputs and identifiers.</param>
/// <param name="Events">Ordered immutable event snapshots.</param>
/// <param name="DroppedEventCount">The number of events omitted after a bound was reached.</param>
/// <param name="ApproximateRetainedBytes">The store's conservative memory estimate for this snapshot.</param>
public sealed record DiagnosticSession(
    string SessionId,
    DiagnosticCategory Category,
    string Operation,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    TimeSpan Elapsed,
    DiagnosticStatus Status,
    DiagnosticContentRetentionMode ContentRetentionMode,
    IReadOnlyList<string> RelatedSessionIds,
    IReadOnlyList<DiagnosticField> Context,
    IReadOnlyList<DiagnosticEvent> Events,
    int DroppedEventCount,
    long ApproximateRetainedBytes)
{
    /// <summary>Gets whether one or more detailed events were sampled or truncated.</summary>
    public bool WasTruncated => DroppedEventCount > 0 ||
        Operation.EndsWith(DiagnosticLimits.TruncationMarker, StringComparison.Ordinal) ||
        Context.Any(diagnosticField =>
            diagnosticField.Value.EndsWith(DiagnosticLimits.TruncationMarker, StringComparison.Ordinal)) ||
        Events.SelectMany(item => item.Fields).Any(diagnosticField =>
            diagnosticField.Value.EndsWith(DiagnosticLimits.TruncationMarker, StringComparison.Ordinal) ||
            (diagnosticField.Name.Contains("Truncated", StringComparison.OrdinalIgnoreCase) &&
             bool.TryParse(diagnosticField.Value, out var truncated) &&
             truncated)) ||
        Events.Any(item =>
            item.Stage.Contains("sampling", StringComparison.OrdinalIgnoreCase) ||
            item.Stage.Contains("bounded", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Describes one category shown in Settings and the unified viewer.</summary>
/// <param name="Category">The category identity.</param>
/// <param name="DisplayName">The concise user-facing label.</param>
/// <param name="Description">The instrumentation scope or planned placeholder description.</param>
/// <param name="IsInstrumented">Whether this release publishes detailed sessions for the category.</param>
public sealed record DiagnosticCategoryDescriptor(
    DiagnosticCategory Category,
    string DisplayName,
    string Description,
    bool IsInstrumented);

/// <summary>Publishes immutable session snapshots after a session changes.</summary>
public sealed class DiagnosticSessionChangedEventArgs : EventArgs
{
    /// <summary>Initializes one observer notification.</summary>
    /// <param name="session">The latest immutable session snapshot.</param>
    /// <param name="isNew">Whether this notification created the session.</param>
    public DiagnosticSessionChangedEventArgs(DiagnosticSession session, bool isNew)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        IsNew = isNew;
    }

    /// <summary>Gets the latest immutable session snapshot.</summary>
    public DiagnosticSession Session { get; }

    /// <summary>Gets whether the session was just created.</summary>
    public bool IsNew { get; }
}

/// <summary>Defines explicit process-memory and presentation bounds for advanced diagnostics.</summary>
public static class DiagnosticLimits
{
    /// <summary>Maximum retained sessions across all categories.</summary>
    public const int MaximumRetainedSessions = 50;

    /// <summary>Maximum retained sessions in one category.</summary>
    public const int MaximumRetainedSessionsPerCategory = 20;

    /// <summary>Maximum detailed events retained in one session.</summary>
    public const int MaximumEventsPerSession = 750;

    /// <summary>Maximum characters retained in one diagnostic value.</summary>
    public const int MaximumTextCharacters = 1_048_576;

    /// <summary>Maximum aggregate field characters retained in one context or event.</summary>
    public const int MaximumTextCharactersPerEvent = 1_048_576;

    /// <summary>Approximate maximum retained memory for one diagnostic session.</summary>
    public const long MaximumApproximateRetainedBytesPerSession = 8L * 1024 * 1024;

    /// <summary>Maximum OCR page records retained in one extraction session.</summary>
    public const int MaximumPageRecords = 100;

    /// <summary>Maximum detailed file or directory records retained in one scan session.</summary>
    public const int MaximumScanEntryRecords = 500;

    /// <summary>Maximum rendered-image previews retained in one session.</summary>
    public const int MaximumImagePreviewsPerSession = 0;

    /// <summary>Maximum retained bytes for one future image preview.</summary>
    public const int MaximumImagePreviewBytes = 128 * 1024;

    /// <summary>Approximate maximum retained process memory for all detailed sessions.</summary>
    public const long MaximumApproximateRetainedBytes = 32L * 1024 * 1024;

    /// <summary>Marker appended to a bounded value.</summary>
    public const string TruncationMarker = "\n[TRUNCATED BY DIAGNOSTIC LIMIT]";
}

/// <summary>Provides the supported category registry and truthful instrumentation state.</summary>
public static class DiagnosticCategoryRegistry
{
    private static readonly IReadOnlyList<DiagnosticCategoryDescriptor> Categories =
    [
        new(DiagnosticCategory.Ai, "AI diagnostics", "Ollama connection, discovery, prompts, transport, parsing, and validation.", true),
        new(DiagnosticCategory.OcrAndTextExtraction, "OCR and text extraction diagnostics", "Native extraction, quality decisions, OCR fallback, per-page results, and downstream text.", true),
        new(DiagnosticCategory.Scanning, "Scanning diagnostics", "Filesystem traversal, accepted entries, skip decisions, issues, progress, and counts.", true),
        new(DiagnosticCategory.DuplicateDetection, "Duplicate detection diagnostics", "Planned: detailed hash grouping and duplicate-decision events are not yet instrumented.", false),
        new(DiagnosticCategory.SearchAndIndexing, "Search and indexing diagnostics", "Durable run IDs, stage timing, queue state, retry counts, dependency waits, storage size, and privacy-safe failures.", true),
        new(DiagnosticCategory.RulesAndOrganisation, "Rules and organisation diagnostics", "Planned: detailed rule evaluation and organisation-planning events are not yet instrumented.", false),
        new(DiagnosticCategory.FileOperations, "File operation diagnostics", "Planned: detailed execution and undo events are not yet instrumented.", false),
        new(DiagnosticCategory.Performance, "Performance diagnostics", "Planned: cross-feature performance sessions are not yet instrumented; instrumented sessions already include timing fields.", false),
    ];

    /// <summary>Gets all categories in stable Settings order.</summary>
    public static IReadOnlyList<DiagnosticCategoryDescriptor> All => Categories;

    /// <summary>Gets the descriptor for one category.</summary>
    /// <param name="category">The category to resolve.</param>
    /// <returns>The matching stable descriptor.</returns>
    public static DiagnosticCategoryDescriptor Get(DiagnosticCategory category) =>
        Categories.First(item => item.Category == category);
}
