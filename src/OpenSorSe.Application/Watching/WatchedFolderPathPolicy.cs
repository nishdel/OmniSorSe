#pragma warning disable CS1591

using System.Text;
using System.Text.RegularExpressions;

namespace OpenSorSe.Application.Watching;

public sealed class WatchedFolderPathPolicy
{
    private static readonly string[] BuiltInPatterns =
    [
        "~$*",
        "*.tmp",
        "*.temp",
        "*.part",
        "*.partial",
        "*.crdownload",
        "*.download",
        "*.opdownload",
        "*.swp",
        "*.swo",
        ".DS_Store",
        "Thumbs.db",
        "desktop.ini",
        ".~lock.*",
    ];

    private static readonly string[] InternalNames =
    [
        ".opensorse",
        "change-plans.json",
        "operation-journal.json",
        "watched-folders.json",
        "watched-catalogues.json",
        "watched-activity.json",
    ];

    public static StringComparer PathComparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static StringComparison PathComparison { get; } =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public string CanonicalizeRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("A watched folder path must be absolute.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path.Trim());
        var pathRoot = Path.GetPathRoot(fullPath)
                       ?? throw new ArgumentException("A watched folder path must include a root.", nameof(path));
        return PathComparer.Equals(fullPath, pathRoot)
            ? pathRoot
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public bool IsWithinRoot(string root, string candidate)
    {
        var canonicalRoot = CanonicalizeRoot(root);
        var canonicalCandidate = Path.GetFullPath(candidate);
        if (PathComparer.Equals(canonicalRoot, canonicalCandidate))
        {
            return true;
        }

        var prefix = canonicalRoot.EndsWith(Path.DirectorySeparatorChar) ||
                     canonicalRoot.EndsWith(Path.AltDirectorySeparatorChar)
            ? canonicalRoot
            : canonicalRoot + Path.DirectorySeparatorChar;
        return canonicalCandidate.StartsWith(prefix, PathComparison);
    }

    public bool Overlaps(string firstRoot, string secondRoot)
    {
        var first = CanonicalizeRoot(firstRoot);
        var second = CanonicalizeRoot(secondRoot);
        return IsWithinRoot(first, second) || IsWithinRoot(second, first);
    }

    public bool ShouldIgnore(
        WatchedFolderConfiguration configuration,
        string path,
        FileAttributes? attributes = null,
        long? sizeInBytes = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var canonicalRoot = CanonicalizeRoot(configuration.FolderPath);
        string canonicalPath;
        try
        {
            canonicalPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }

        if (!IsWithinRoot(canonicalRoot, canonicalPath))
        {
            return true;
        }

        if (attributes is { } actualAttributes &&
            (actualAttributes.HasFlag(FileAttributes.ReparsePoint) ||
             configuration.IgnoreHiddenFiles && actualAttributes.HasFlag(FileAttributes.Hidden)))
        {
            return true;
        }

        if (sizeInBytes > configuration.MaximumFileSizeBytes)
        {
            return true;
        }

        var relative = Path.GetRelativePath(canonicalRoot, canonicalPath);
        var fileName = Path.GetFileName(canonicalPath);
        if (InternalNames.Any(name =>
                string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase) ||
                relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => string.Equals(segment, name, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        foreach (var ignoredPath in configuration.IgnoredPaths)
        {
            if (string.IsNullOrWhiteSpace(ignoredPath))
            {
                continue;
            }

            var candidate = Path.IsPathFullyQualified(ignoredPath)
                ? Path.GetFullPath(ignoredPath)
                : Path.GetFullPath(Path.Combine(canonicalRoot, ignoredPath));
            if (PathComparer.Equals(candidate, canonicalPath) ||
                canonicalPath.StartsWith(candidate.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, PathComparison))
            {
                return true;
            }
        }

        return BuiltInPatterns.Concat(configuration.IgnorePatterns)
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Any(pattern => GlobMatches(pattern, relative) || GlobMatches(pattern, fileName));
    }

    public static bool GlobMatches(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > WatchedFolderLimits.MaximumPatternLength)
        {
            return false;
        }

        var normalizedPattern = pattern.Replace('\\', '/');
        var normalizedValue = value.Replace('\\', '/');
        var builder = new StringBuilder("^");
        for (var index = 0; index < normalizedPattern.Length; index++)
        {
            var character = normalizedPattern[index];
            if (character == '*' && index + 1 < normalizedPattern.Length && normalizedPattern[index + 1] == '*')
            {
                builder.Append(".*");
                index++;
            }
            else if (character == '*')
            {
                builder.Append("[^/]*");
            }
            else if (character == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(character.ToString()));
            }
        }

        builder.Append('$');
        return Regex.IsMatch(
            normalizedValue,
            builder.ToString(),
            OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant : RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
    }
}
