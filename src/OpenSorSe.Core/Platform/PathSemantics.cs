namespace OpenSorSe.Core.Platform;

/// <summary>Creates the small platform services used by production composition and compatibility code.</summary>
public static class PlatformServices
{
    /// <summary>Gets the detected operating-system family.</summary>
    public static HostPlatformKind CurrentPlatform { get; } = DetectPlatform();

    /// <summary>Gets the process-wide current-platform path semantics.</summary>
    public static IPathSemantics CurrentPathSemantics { get; } = CreatePathSemantics(CurrentPlatform);

    /// <summary>Creates path semantics for one explicit platform, primarily for validation and tests.</summary>
    public static IPathSemantics CreatePathSemantics(HostPlatformKind platform) => platform switch
    {
        HostPlatformKind.Windows => new WindowsPathSemantics(),
        HostPlatformKind.Linux => new LinuxPathSemantics(),
        HostPlatformKind.MacOS => new ConservativePathSemantics(HostPlatformKind.MacOS),
        _ => new ConservativePathSemantics(HostPlatformKind.Other),
    };

    private static HostPlatformKind DetectPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return HostPlatformKind.Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return HostPlatformKind.Linux;
        }

        return OperatingSystem.IsMacOS()
            ? HostPlatformKind.MacOS
            : HostPlatformKind.Other;
    }
}

/// <summary>Provides shared lexical confinement and filename-validation behavior.</summary>
public abstract class PathSemanticsBase : IPathSemantics
{
    private static readonly char[] WindowsInvalidCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    private static readonly HashSet<string> WindowsReservedNames = BuildWindowsReservedNames();

    /// <inheritdoc />
    public abstract HostPlatformKind Platform { get; }

    /// <inheritdoc />
    public abstract bool IsCaseSensitive { get; }

    /// <inheritdoc />
    public StringComparer Comparer =>
        IsCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <inheritdoc />
    public StringComparison Comparison =>
        IsCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <inheritdoc />
    public string NormalizeAbsolutePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("A fully qualified path is required.", nameof(path));
        }

        var normalized = Path.GetFullPath(path);
        var root = Path.GetPathRoot(normalized)
            ?? throw new ArgumentException("The path does not have a recognizable root.", nameof(path));
        return Comparer.Equals(normalized, root)
            ? root
            : Path.TrimEndingDirectorySeparator(normalized);
    }

    /// <inheritdoc />
    public bool IsWithinRoot(string root, string candidate)
    {
        var normalizedRoot = NormalizeAbsolutePath(root);
        var normalizedCandidate = NormalizeAbsolutePath(candidate);
        if (Comparer.Equals(normalizedRoot, normalizedCandidate))
        {
            return true;
        }

        var prefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar) ||
                     normalizedRoot.EndsWith(Path.AltDirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(prefix, Comparison);
    }

    /// <inheritdoc />
    public bool PathsEqual(string first, string second) =>
        Comparer.Equals(NormalizeAbsolutePath(first), NormalizeAbsolutePath(second));

    /// <inheritdoc />
    public bool IsCaseOnlyDifference(string first, string second)
    {
        var normalizedFirst = NormalizeAbsolutePath(first);
        var normalizedSecond = NormalizeAbsolutePath(second);
        return !IsCaseSensitive &&
               !string.Equals(normalizedFirst, normalizedSecond, StringComparison.Ordinal) &&
               string.Equals(normalizedFirst, normalizedSecond, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool IsValidFileName(
        string value,
        FileNamePortabilityMode mode,
        out string? reason)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            reason = "A filename cannot be empty or whitespace.";
            return false;
        }

        if (value is "." or "..")
        {
            reason = "Dot path segments are not filenames.";
            return false;
        }

        if (value.Length > 255)
        {
            reason = "The filename exceeds the conservative 255-character segment limit.";
            return false;
        }

        if (value.Any(character => character == '\0' || char.IsControl(character)) ||
            value.Contains('/') ||
            value.Contains('\\'))
        {
            reason = "The filename contains a control character or path separator.";
            return false;
        }

        var windowsPolicy = Platform == HostPlatformKind.Windows ||
                            mode is FileNamePortabilityMode.Portable or
                                FileNamePortabilityMode.WindowsCompatible;
        if (windowsPolicy &&
            (value.EndsWith(' ') ||
             value.EndsWith('.') ||
             value.IndexOfAny(WindowsInvalidCharacters) >= 0))
        {
            reason = "The filename is not valid under the selected Windows-compatible policy.";
            return false;
        }

        if (windowsPolicy &&
            WindowsReservedNames.Contains(value.Split('.', 2)[0]))
        {
            reason = "The filename uses a reserved Windows device name.";
            return false;
        }

        reason = null;
        return true;
    }

    private static HashSet<string> BuildWindowsReservedNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
        };
        for (var index = 1; index <= 9; index++)
        {
            names.Add($"COM{index}");
            names.Add($"LPT{index}");
        }

        return names;
    }
}

/// <summary>Implements Windows case-insensitive path and reserved-name semantics.</summary>
public sealed class WindowsPathSemantics : PathSemanticsBase
{
    /// <inheritdoc />
    public override HostPlatformKind Platform => HostPlatformKind.Windows;

    /// <inheritdoc />
    public override bool IsCaseSensitive => false;
}

/// <summary>Implements the default case-sensitive Linux path and filename semantics.</summary>
public sealed class LinuxPathSemantics : PathSemanticsBase
{
    /// <inheritdoc />
    public override HostPlatformKind Platform => HostPlatformKind.Linux;

    /// <inheritdoc />
    public override bool IsCaseSensitive => true;
}

/// <summary>Uses conservative case-sensitive behavior for unverified operating systems.</summary>
public sealed class ConservativePathSemantics : PathSemanticsBase
{
    /// <summary>Creates conservative semantics for an unverified platform.</summary>
    public ConservativePathSemantics(HostPlatformKind platform)
    {
        Platform = platform;
    }

    /// <inheritdoc />
    public override HostPlatformKind Platform { get; }

    /// <inheritdoc />
    public override bool IsCaseSensitive => true;
}
