namespace OpenSorSe.Core.Platform;

/// <summary>
/// Provides bounded lexical operations for persisted or untrusted paths whose
/// separator syntax may differ from the current host operating system.
/// </summary>
public static class CrossPlatformPath
{
    /// <summary>
    /// Returns whether a path is rooted or drive-qualified under Windows or
    /// POSIX syntax, independent of the current host operating system.
    /// </summary>
    public static bool IsRootedOnAnyPlatform(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return Path.IsPathRooted(path) ||
               path[0] is '/' or '\\' ||
               path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';
    }

    /// <summary>
    /// Gets the final path segment while recognizing both slash styles.
    /// </summary>
    public static string GetFileName(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var separatorIndex = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return separatorIndex < 0 ? path : path[(separatorIndex + 1)..];
    }

    /// <summary>
    /// Gets the final path segment without its extension while recognizing
    /// both slash styles.
    /// </summary>
    public static string GetFileNameWithoutExtension(string path) =>
        Path.GetFileNameWithoutExtension(GetFileName(path));
}
