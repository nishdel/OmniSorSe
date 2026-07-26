namespace OpenSorSe.Scanner.Models;

/// <summary>
/// Represents a file discovered during a scan.
/// </summary>
public sealed record FileEntry(
    string FullPath,
    FileMetadata? Metadata = null,
    FileHash? Hash = null,
    FileClassification? Classification = null,
    DuplicateClassification? Duplicate = null)
{
    /// <summary>Gets the process-session scan diagnostic that discovered this file.</summary>
    public string? ScanDiagnosticSessionId { get; init; }
}
