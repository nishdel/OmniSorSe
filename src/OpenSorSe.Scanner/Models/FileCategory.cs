namespace OpenSorSe.Scanner.Models;

/// <summary>
/// Describes the primary deterministic category assigned to a file.
/// </summary>
public enum FileCategory
{
    /// <summary>No deterministic category was matched.</summary>
    Unknown,
    /// <summary>A text or document file.</summary>
    Document,
    /// <summary>A spreadsheet file.</summary>
    Spreadsheet,
    /// <summary>A presentation file.</summary>
    Presentation,
    /// <summary>An image file.</summary>
    Image,
    /// <summary>An audio file.</summary>
    Audio,
    /// <summary>A video file.</summary>
    Video,
    /// <summary>An archive file.</summary>
    Archive,
    /// <summary>A source-code or markup file.</summary>
    Code,
    /// <summary>A structured data file.</summary>
    Data,
    /// <summary>An executable or installer file.</summary>
    Executable,
    /// <summary>A font file.</summary>
    Font,
}
