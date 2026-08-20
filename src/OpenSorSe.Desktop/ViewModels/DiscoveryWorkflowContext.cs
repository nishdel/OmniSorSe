using OpenSorSe.Application.Semantic;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Captures the small canonical state required to move from discovery into Files and back.
/// It contains stable identifiers and bounded result order, never a second Search result model.
/// </summary>
public sealed record DiscoveryWorkflowContext(
    DiscoveryQueryState Query,
    string? SavedViewId,
    string SelectedFileId,
    bool IsUnresolvedReview,
    string? SourceId,
    IReadOnlyList<string> ResultFileIds)
{
    /// <summary>Gets a concise, content-safe description shown in Files.</summary>
    public string DisplayName => IsUnresolvedReview
        ? "Unresolved Smart Tag review"
        : SavedViewId is not null
            ? "Saved View results"
            : string.IsNullOrWhiteSpace(Query.QueryText)
                ? "Filtered discovery"
                : "Search results";
}

/// <summary>Requests that the shell open one durable Search result in the Files surface.</summary>
public sealed record DiscoveryFileOpenRequest(
    string FileId,
    DiscoveryWorkflowContext Context);

/// <summary>Identifies bounded movement through one captured discovery result sequence.</summary>
public enum DiscoveryReviewDirection
{
    /// <summary>Moves to the preceding result when available.</summary>
    Previous = -1,
    /// <summary>Moves to the following result when available.</summary>
    Next = 1,
}
