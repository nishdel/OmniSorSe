using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Models;

/// <summary>
/// Represents one immutable, display-safe file result in a completed processing snapshot.
/// </summary>
public sealed record ResultFile(
    string Id,
    string FullPath,
    string DisplayFileName,
    string NormalizedExtension,
    long? SizeInBytes,
    DateTimeOffset? LastWriteTimeUtc,
    FileCategory? Category,
    string ClassificationDisplay,
    DuplicateStatus DuplicateStatus,
    string? DuplicateGroupId,
    bool HasPlannedOperation)
{
    /// <summary>Gets the filesystem creation timestamp when metadata extraction made it available.</summary>
    public DateTimeOffset? CreationTimeUtc { get; init; }
}
