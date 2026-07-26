namespace OpenSorSe.Scanner.Models;

/// <summary>
/// Contains all entries and recoverable issues produced by one scan operation.
/// </summary>
public sealed record ScanResult(
    IReadOnlyList<FileEntry> Files,
    IReadOnlyList<DirectoryEntry> Directories,
    ScanStatistics Statistics,
    IReadOnlyList<ScanIssue> Issues,
    ScanStatus Status,
    TimeSpan Elapsed)
{
    /// <summary>Gets the process-session diagnostic identity for this scan, when collection was enabled.</summary>
    public string? DiagnosticSessionId { get; init; }
}
