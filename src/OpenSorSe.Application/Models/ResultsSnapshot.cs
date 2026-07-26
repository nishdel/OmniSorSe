using OpenSorSe.Application.Workflows;

namespace OpenSorSe.Application.Models;

/// <summary>
/// Contains the immutable, process-local data used to review one completed scan.
/// </summary>
public sealed record ResultsSnapshot(
    string SessionId,
    DateTimeOffset SessionStartedAtUtc,
    DateTimeOffset ProjectedAtUtc,
    IReadOnlyList<ResultFile> Files,
    IReadOnlyList<ResultDirectory> Directories,
    IReadOnlyList<ResultDuplicateGroup> DuplicateGroups,
    IReadOnlyList<ResultPlannedOperation> PlannedOperations,
    IReadOnlyList<ResultIssue> Issues,
    ResultsSnapshotStatistics Statistics,
    bool IsDuplicateDataAvailable)
{
    /// <summary>Gets the immutable workflow revision snapshot used by this historical scan.</summary>
    public WorkflowConfigurationSnapshot? Workflow { get; init; }
}
