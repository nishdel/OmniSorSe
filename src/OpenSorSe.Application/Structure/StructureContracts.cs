namespace OpenSorSe.Application.Structure;

/// <summary>Defines hard safety and persistence bounds for folder restructuring.</summary>
public static class StructureLimits
{
    /// <summary>Maximum nodes retained in one source, proposal, or applied snapshot.</summary>
    public const int MaximumSnapshotNodes = 4_000;
    /// <summary>Maximum nodes projected in one diagram view.</summary>
    public const int MaximumVisibleNodes = 500;
    /// <summary>Maximum explicitly confirmed moves in one operation.</summary>
    public const int MaximumMovesPerOperation = 500;
    /// <summary>Maximum durable history records.</summary>
    public const int MaximumHistoryRecords = 250;
    /// <summary>Maximum characters in one stored path.</summary>
    public const int MaximumStoredPathLength = 2_048;
    /// <summary>Maximum characters in one user-safe history message.</summary>
    public const int MaximumMessageLength = 1_024;
    /// <summary>Maximum encoded size of the versioned history file.</summary>
    public const long MaximumHistoryFileBytes = 64L * 1024 * 1024;
}

/// <summary>Identifies the durable outcome of one restructuring operation.</summary>
public enum RestructuringStatus
{
    /// <summary>A proposal was generated but did not touch the filesystem.</summary>
    Previewed,
    /// <summary>Every confirmed move succeeded.</summary>
    Applied,
    /// <summary>At least one completed move could not be rolled back after failure.</summary>
    PartiallyApplied,
    /// <summary>The operation failed without leaving completed moves.</summary>
    Failed,
    /// <summary>The operation was cancelled without leaving completed moves.</summary>
    Cancelled,
}

/// <summary>Identifies the explicit user-approval state of an operation.</summary>
public enum RestructuringApprovalState
{
    /// <summary>No application request was made.</summary>
    NotRequested,
    /// <summary>The exact preview identity was explicitly confirmed.</summary>
    Approved,
    /// <summary>The supplied confirmation did not match the preview.</summary>
    Rejected,
}

/// <summary>Describes repeat-protection evaluation for one root.</summary>
public enum RestructuringProtectionState
{
    /// <summary>No prior record exists for the root.</summary>
    FirstRun,
    /// <summary>The root matches its last successfully applied structure.</summary>
    AlreadyOrganized,
    /// <summary>Existing applied files are unchanged and only new files were found.</summary>
    NewFilesOnly,
    /// <summary>Existing files or paths changed after the last successful apply.</summary>
    MateriallyChanged,
    /// <summary>Only preview, failed, partial, or cancelled records exist.</summary>
    PreviousIncomplete,
}

/// <summary>Classifies a node-level change between two folder snapshots.</summary>
public enum StructureChangeKind
{
    /// <summary>The node exists only after the comparison.</summary>
    Added,
    /// <summary>The node exists only before the comparison.</summary>
    Removed,
    /// <summary>A file identity moved to another parent.</summary>
    Moved,
    /// <summary>A file identity changed name under the same parent.</summary>
    Renamed,
    /// <summary>The node remains at the same relative path.</summary>
    Unchanged,
}

/// <summary>Identifies a per-file application outcome.</summary>
public enum RestructuringItemStatus
{
    /// <summary>The explicitly confirmed move succeeded.</summary>
    Succeeded,
    /// <summary>The move failed before completion.</summary>
    Failed,
    /// <summary>The move completed and was subsequently restored.</summary>
    RolledBack,
    /// <summary>The move completed but could not be restored.</summary>
    RollbackFailed,
    /// <summary>The move was not attempted.</summary>
    Skipped,
}

/// <summary>Stores one bounded relative node without file contents.</summary>
public sealed record StructureNode(
    string RelativePath,
    bool IsDirectory,
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    string IdentityFingerprint);

/// <summary>Stores a bounded, read-only folder structure at one point in time.</summary>
public sealed record FolderStructureSnapshot(
    string RootPath,
    string RootIdentity,
    string StructureFingerprint,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<StructureNode> Nodes);

/// <summary>Describes one proposed in-root file move using relative paths only.</summary>
public sealed record RestructuringMove(string SourceRelativePath, string DestinationRelativePath);

/// <summary>Contains a deterministic, preview-only restructuring plan.</summary>
public sealed record RestructuringPlan(
    string OperationId,
    string RootPath,
    FolderStructureSnapshot SourceSnapshot,
    FolderStructureSnapshot ProposedSnapshot,
    IReadOnlyList<RestructuringMove> Moves,
    bool IsIncremental,
    bool IsExplicitOverride,
    string? PreviousRecordId,
    string AlgorithmVersion,
    string Summary);

/// <summary>Records the outcome of one planned file move.</summary>
public sealed record RestructuringItemOutcome(
    string SourceRelativePath,
    string DestinationRelativePath,
    RestructuringItemStatus Status,
    string Message);

/// <summary>Persists one preview/apply lifecycle and its source, proposed, and applied structures.</summary>
public sealed record RestructuringHistoryRecord(
    string OperationId,
    string RootIdentity,
    string RootPath,
    string RootFingerprint,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    FolderStructureSnapshot SourceSnapshot,
    FolderStructureSnapshot ProposedSnapshot,
    FolderStructureSnapshot? AppliedSnapshot,
    RestructuringApprovalState ApprovalState,
    RestructuringStatus Status,
    IReadOnlyList<RestructuringMove> IncludedFiles,
    IReadOnlyList<RestructuringItemOutcome> ItemOutcomes,
    string Summary,
    string? PreviousRecordId,
    string AlgorithmVersion,
    bool IsExplicitOverride);

/// <summary>Contains a controlled preview result.</summary>
public sealed record RestructuringPreviewResult(
    bool HasProposal,
    RestructuringProtectionState ProtectionState,
    RestructuringPlan? Plan,
    string Message);

/// <summary>Contains a controlled apply result and its durable record.</summary>
public sealed record RestructuringApplyResult(
    bool Succeeded,
    RestructuringStatus Status,
    RestructuringHistoryRecord Record,
    string Message);

/// <summary>Describes one comparison row for accessible diagram rendering.</summary>
public sealed record StructureChange(
    StructureChangeKind Kind,
    string? BeforeRelativePath,
    string? AfterRelativePath,
    bool IsDirectory,
    string IdentityFingerprint);

/// <summary>Captures bounded folder metadata without reading file contents.</summary>
public interface IFolderStructureSnapshotService
{
    /// <summary>Captures one explicit root using relative metadata-only nodes.</summary>
    Task<FolderStructureSnapshot> CaptureAsync(string rootPath, CancellationToken cancellationToken);
}

/// <summary>Persists bounded restructuring history independently from user files.</summary>
public interface IStructureHistoryStore
{
    /// <summary>Lists records newest first.</summary>
    Task<IReadOnlyList<RestructuringHistoryRecord>> ListAsync(CancellationToken cancellationToken);
    /// <summary>Adds or replaces one operation lifecycle record.</summary>
    Task UpsertAsync(RestructuringHistoryRecord record, CancellationToken cancellationToken);
    /// <summary>Clears application-owned history without changing user files.</summary>
    Task ClearAsync(CancellationToken cancellationToken);
}

/// <summary>Builds previews, evaluates repeat protection, and applies explicitly confirmed plans.</summary>
public interface IFolderRestructuringService
{
    /// <summary>Creates and stores a deterministic review-only proposal when one is available.</summary>
    Task<RestructuringPreviewResult> PreviewAsync(
        string rootPath,
        bool explicitOverride,
        CancellationToken cancellationToken);

    /// <summary>Applies an exact explicitly confirmed preview through bounded validated moves.</summary>
    Task<RestructuringApplyResult> ApplyAsync(
        RestructuringPlan plan,
        string confirmedOperationId,
        CancellationToken cancellationToken);
}

/// <summary>Compares structure snapshots without touching the filesystem.</summary>
public interface IStructureComparisonService
{
    /// <summary>Returns a bounded deterministic change projection.</summary>
    IReadOnlyList<StructureChange> Compare(
        FolderStructureSnapshot before,
        FolderStructureSnapshot after);
}
