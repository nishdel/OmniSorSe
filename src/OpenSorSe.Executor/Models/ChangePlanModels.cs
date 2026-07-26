#pragma warning disable CS1591

namespace OpenSorSe.Executor.Models;

/// <summary>Defines the persisted Change Plan schema used by OpenSorSe 1.1.</summary>
public static class ChangePlanSchema
{
    /// <summary>Current backwards-compatible Change Plan schema version.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Maximum actions retained in one plan.</summary>
    public const int MaximumActions = 1_000;

    /// <summary>Maximum plans retained by the local draft store.</summary>
    public const int MaximumStoredPlans = 100;

    /// <summary>Maximum persisted path length.</summary>
    public const int MaximumPathLength = 32_767;

    /// <summary>Maximum encoded Change Plan store size.</summary>
    public const long MaximumStoreFileBytes = 64L * 1024 * 1024;
}

/// <summary>Identifies the lifecycle of a reviewable Change Plan.</summary>
public enum ChangePlanStatus
{
    Draft,
    AwaitingReview,
    Approved,
    Rejected,
    Invalidated,
    ValidationFailed,
    Applying,
    Applied,
    PartiallyApplied,
    Failed,
}

/// <summary>Identifies a supported OpenSorSe 1.1 filesystem action.</summary>
public enum ChangeActionType
{
    RenameFile,
    MoveFile,
    CreateDirectory,
}

/// <summary>Identifies where a proposed change originated.</summary>
public enum ChangeSuggestionSource
{
    Ai,
    DeterministicRule,
    Metadata,
    Ocr,
    DuplicateAnalysis,
    ManualUserEdit,
    ExistingFolderStructureSuggestion,
}

/// <summary>Identifies a user's action-level review decision.</summary>
public enum ChangeApprovalState
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>Identifies the latest validation state of a proposed action.</summary>
public enum ChangeValidationState
{
    NotValidated,
    Valid,
    Warning,
    Conflict,
    Invalid,
    Stale,
}

/// <summary>Provides stable, user-safe conflict categories.</summary>
public enum ChangeConflictCategory
{
    None,
    InvalidAction,
    UnsupportedAction,
    SourceMissing,
    SourceRenamedExternally,
    SourceChanged,
    SourceHashChanged,
    SourceTypeUnsupported,
    SourceLocked,
    SourceOutsideRoot,
    DestinationInvalid,
    DestinationOutsideRoot,
    InvalidFileName,
    PathTooLong,
    DestinationOccupied,
    DestinationParentUnavailable,
    DirectoryCollidesWithFile,
    DuplicateDestination,
    ConflictingSourceActions,
    ExecutionOrderConflict,
    FolderMovedIntoItself,
    PermissionDenied,
    CaseOnlyRename,
    ScanStale,
    UndoResultMissing,
    UndoResultChanged,
    UndoOriginalOccupied,
    UndoDependencyConflict,
    UndoDirectoryNotEmpty,
    InterruptedStateAmbiguous,
    IoFailure,
    VerificationFailed,
}

/// <summary>Describes one structured validation conflict.</summary>
public sealed record ChangeConflict(
    ChangeConflictCategory Category,
    string Message,
    bool IsBlocking);

/// <summary>Captures a portable identity snapshot without platform-specific handles.</summary>
public sealed record FileIdentitySnapshot(
    string Identity,
    long SizeInBytes,
    DateTimeOffset LastWriteTimeUtc,
    DateTimeOffset CreationTimeUtc,
    string? ContentHash);

/// <summary>Captures immutable workflow and recipe provenance for one proposal.</summary>
public sealed record ChangeWorkflowProvenance(
    string ProfileId,
    string ProfileName,
    int ProfileRevision,
    string RecipeId,
    string RecipeName,
    int RecipeRevision,
    IReadOnlyDictionary<string, string> ValuesUsed,
    IReadOnlyList<string> EvidenceSources,
    bool IsAiAssisted,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> UnresolvedFields);

/// <summary>Represents one immutable proposed action inside a Change Plan.</summary>
public sealed record ProposedChangeAction(
    string ActionId,
    string PlanId,
    ChangeActionType ActionType,
    string? SourcePath,
    string DestinationPath,
    string? OriginalFileName,
    string? ProposedFileName,
    FileIdentitySnapshot? SourceIdentity,
    ChangeSuggestionSource SuggestionSource,
    string Reason,
    ChangeValidationState ValidationState,
    ChangeApprovalState ApprovalState,
    int ExecutionOrder,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ChangeConflict> Conflicts,
    bool WasUserEdited,
    string? AiModel,
    string? AiRequestCorrelationId)
{
    /// <summary>Gets the optional immutable v1.3 workflow provenance for this proposal.</summary>
    public ChangeWorkflowProvenance? WorkflowProvenance { get; init; }
}

/// <summary>Represents a persisted, reviewable and non-mutating collection of proposed changes.</summary>
public sealed record ChangePlan(
    int SchemaVersion,
    string PlanId,
    DateTimeOffset CreatedAtUtc,
    string? SourceScanId,
    string RootPath,
    ChangePlanStatus Status,
    IReadOnlyList<ProposedChangeAction> Actions,
    IReadOnlyList<string> Warnings,
    DateTimeOffset? ValidatedAtUtc,
    bool IsSourceScanStale)
{
    /// <summary>Gets the number of approved actions.</summary>
    public int ApprovedActionCount => Actions.Count(action => action.ApprovalState == ChangeApprovalState.Approved);

    /// <summary>Gets the number of rejected actions.</summary>
    public int RejectedActionCount => Actions.Count(action => action.ApprovalState == ChangeApprovalState.Rejected);

    /// <summary>Gets whether any approved action has an unresolved blocking conflict.</summary>
    public bool HasBlockingConflicts => Actions.Any(action =>
        action.ApprovalState == ChangeApprovalState.Approved &&
        action.Conflicts.Any(conflict => conflict.IsBlocking));
}

/// <summary>Describes one untrusted proposal before it is captured into a Change Plan.</summary>
public sealed record ChangeActionProposal(
    ChangeActionType ActionType,
    string? SourcePath,
    string DestinationPath,
    ChangeSuggestionSource SuggestionSource,
    string Reason,
    int ExecutionOrder,
    string? SourceFileIdentity = null,
    long? SourceSizeInBytes = null,
    DateTimeOffset? SourceLastWriteTimeUtc = null,
    string? ContentHash = null,
    string? AiModel = null,
    string? AiRequestCorrelationId = null)
{
    /// <summary>Gets the optional workflow provenance captured without executing the proposal.</summary>
    public ChangeWorkflowProvenance? WorkflowProvenance { get; init; }
}

/// <summary>Describes a request to capture proposals as a non-mutating Change Plan.</summary>
public sealed record ChangePlanCreationRequest(
    string RootPath,
    string? SourceScanId,
    IReadOnlyList<ChangeActionProposal> Actions,
    IReadOnlyList<string>? Warnings = null);

/// <summary>Contains a complete plan-validation result.</summary>
public sealed record ChangePlanValidationResult(
    ChangePlan Plan,
    bool CanApply,
    int ValidActionCount,
    int WarningActionCount,
    int InvalidActionCount,
    int ConflictActionCount,
    int StaleActionCount,
    string Summary);

/// <summary>Identifies why a plan validation is being performed.</summary>
public enum ChangePlanValidationPhase
{
    Creation,
    Review,
    PreExecution,
}
