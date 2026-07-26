using OpenSorSe.Application.Models;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Displays one known member of the selected Duplicate View group.</summary>
public sealed class DuplicateFileRow : ViewModelBase
{
    private bool _isSelected;

    /// <summary>Initializes a row from immutable completed-scan metadata.</summary>
    public DuplicateFileRow(ResultFile file)
    {
        File = file ?? throw new ArgumentNullException(nameof(file));
        ParentPath = Path.GetDirectoryName(file.FullPath) ?? string.Empty;
        ShortParentPath = CreateShortParentPath(ParentPath);
    }

    /// <summary>Gets the immutable result represented by this row.</summary>
    public ResultFile File { get; }

    /// <summary>Gets the request-local source identity already known to the current snapshot.</summary>
    public string FileId => File.Id;

    /// <summary>Gets the display filename.</summary>
    public string FileName => File.DisplayFileName;

    /// <summary>Gets the absolute path captured by the completed scan.</summary>
    public string FullPath => File.FullPath;

    /// <summary>Gets the containing path captured by the completed scan.</summary>
    public string ParentPath { get; }

    /// <summary>Gets a compact containing-path label while the full path remains available as a tooltip.</summary>
    public string ShortParentPath { get; }

    /// <summary>Gets the known file-size display.</summary>
    public string SizeText => File.SizeInBytes is { } size ? ResultsFileRow.FormatSize(size) : "Size unavailable";

    /// <summary>Gets or sets whether the user explicitly selected this known member for a bounded open action.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private static string CreateShortParentPath(string parentPath)
    {
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return "Folder unavailable";
        }

        var root = Path.GetPathRoot(parentPath) ?? string.Empty;
        var segments = parentPath[root.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 2)
        {
            return parentPath;
        }

        return $"{root}…{Path.DirectorySeparatorChar}{segments[^2]}{Path.DirectorySeparatorChar}{segments[^1]}";
    }
}
