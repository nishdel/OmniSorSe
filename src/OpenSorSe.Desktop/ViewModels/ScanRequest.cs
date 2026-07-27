using OpenSorSe.Application.Workflows;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Represents an immutable user request to scan normalized folder roots.
/// </summary>
/// <param name="FolderPaths">The validated folder roots in user selection order.</param>
public sealed record ScanRequest(IReadOnlyList<string> FolderPaths)
{
    /// <summary>Gets the persistent workflow profile selected for this scan.</summary>
    public string ProfileId { get; init; } = BuiltInWorkflowIds.GeneralDocuments;

    /// <summary>Gets optional one-time constraints that do not mutate the saved profile.</summary>
    public WorkflowProfileOverride? OneTimeOverride { get; init; }
}
