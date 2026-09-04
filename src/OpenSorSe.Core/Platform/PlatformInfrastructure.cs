using System.Runtime.InteropServices;
using System.Text;

namespace OpenSorSe.Core.Platform;

/// <summary>Implements non-mutating filesystem capability inspection for the current process.</summary>
public sealed class FileSystemCapabilities : IFileSystemCapabilities
{
    private readonly IFileIdentityProvider _identityProvider;
    private readonly IPathSemantics _pathSemantics;

    /// <summary>Creates a current-platform filesystem capability inspector.</summary>
    public FileSystemCapabilities(
        IFileIdentityProvider identityProvider,
        IPathSemantics pathSemantics)
    {
        _identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
        _pathSemantics = pathSemantics ?? throw new ArgumentNullException(nameof(pathSemantics));
    }

    /// <inheritdoc />
    public FileLinkInspection InspectLink(string path)
    {
        try
        {
            var fileExists = File.Exists(path);
            var directoryExists = Directory.Exists(path);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(path);
            }
            catch (FileNotFoundException)
            {
                return new FileLinkInspection(false, null, null, "The path does not exist.");
            }
            catch (DirectoryNotFoundException)
            {
                return new FileLinkInspection(false, null, null, "The path does not exist.");
            }

            FileSystemInfo information = directoryExists
                ? new DirectoryInfo(path)
                : new FileInfo(path);
            information.Refresh();
            var isLink = attributes.HasFlag(FileAttributes.ReparsePoint) ||
                         information.LinkTarget is not null;
            if (!isLink)
            {
                return new FileLinkInspection(
                    false,
                    null,
                    null,
                    fileExists || directoryExists
                        ? "The path is not a symbolic link or reparse point."
                        : "The path does not exist.");
            }

            string? resolved = null;
            try
            {
                resolved = information.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // The caller still receives a positive link result and must fail closed.
            }

            return new FileLinkInspection(
                true,
                information.LinkTarget,
                resolved,
                resolved is null
                    ? "The link target could not be resolved safely."
                    : "The link target was resolved for inspection only.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new FileLinkInspection(
                true,
                null,
                null,
                "Link state could not be verified; the path must be treated as unsafe.");
        }
    }

    /// <inheritdoc />
    public bool CanWriteDirectory(string path, out string explanation)
    {
        try
        {
            var normalized = _pathSemantics.NormalizeAbsolutePath(path);
            var information = new DirectoryInfo(normalized);
            if (!information.Exists)
            {
                explanation = "The destination directory does not exist.";
                return false;
            }

            if (InspectLink(normalized).IsLink)
            {
                explanation = "The destination directory is a symbolic link or reparse point.";
                return false;
            }

            if (_pathSemantics.Platform is not
                (HostPlatformKind.Windows or HostPlatformKind.Linux))
            {
                explanation =
                    "Writable-access validation is unavailable on this unverified platform.";
                return false;
            }

            if (_pathSemantics.Platform == HostPlatformKind.Linux &&
                OperatingSystem.IsLinux())
            {
                var mode = File.GetUnixFileMode(normalized);
                var writable = mode.HasFlag(UnixFileMode.UserWrite) ||
                               mode.HasFlag(UnixFileMode.GroupWrite) ||
                               mode.HasFlag(UnixFileMode.OtherWrite);
                var traversable = mode.HasFlag(UnixFileMode.UserExecute) ||
                                  mode.HasFlag(UnixFileMode.GroupExecute) ||
                                  mode.HasFlag(UnixFileMode.OtherExecute);
                if (!writable || !traversable)
                {
                    explanation = "Unix mode bits do not expose both write and directory-traversal permission.";
                    return false;
                }

                explanation = "Unix mode bits permit writing and traversal; ACL and mount policy can still reject an operation.";
                return true;
            }

            explanation = "The directory exists and is not a link; the immediate operation remains the authoritative permission check.";
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or PlatformNotSupportedException)
        {
            explanation = "Writable access could not be verified without modifying the directory.";
            return false;
        }
    }

    /// <inheritdoc />
    public long? GetAvailableFreeSpace(string path)
    {
        try
        {
            var normalized = _pathSemantics.NormalizeAbsolutePath(path);
            var drive = DriveInfo.GetDrives()
                .Where(candidate => candidate.IsReady)
                .Where(candidate =>
                    normalized.StartsWith(
                        Path.TrimEndingDirectorySeparator(candidate.RootDirectory.FullName) +
                        Path.DirectorySeparatorChar,
                        _pathSemantics.Comparison) ||
                    _pathSemantics.Comparer.Equals(
                        normalized,
                        Path.TrimEndingDirectorySeparator(candidate.RootDirectory.FullName)))
                .OrderByDescending(candidate => candidate.RootDirectory.FullName.Length)
                .FirstOrDefault();
            return drive?.AvailableFreeSpace;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool AreOnSameFileSystem(
        string firstPath,
        string secondPath,
        out string explanation)
    {
        var first = _identityProvider.GetFileSystemId(firstPath);
        var second = _identityProvider.GetFileSystemId(secondPath);
        if (first is not null &&
            second is not null &&
            (first.StartsWith("linux-device:", StringComparison.Ordinal) &&
             second.StartsWith("linux-device:", StringComparison.Ordinal) ||
             first.StartsWith("windows-volume:", StringComparison.Ordinal) &&
             second.StartsWith("windows-volume:", StringComparison.Ordinal)))
        {
            var same = string.Equals(first, second, StringComparison.Ordinal);
            explanation = same
                ? "Both paths report the same filesystem identity."
                : "The paths report different filesystem identities; a move is not treated as atomic.";
            return same;
        }

        if (_pathSemantics.Platform == HostPlatformKind.Windows)
        {
            var firstRoot = Path.GetPathRoot(_pathSemantics.NormalizeAbsolutePath(firstPath));
            var secondRoot = Path.GetPathRoot(_pathSemantics.NormalizeAbsolutePath(secondPath));
            var sameRoot = firstRoot is not null &&
                           secondRoot is not null &&
                           _pathSemantics.Comparer.Equals(firstRoot, secondRoot);
            explanation = sameRoot
                ? "Both paths use the same Windows drive root; volume identity was unavailable."
                : "The paths do not use the same Windows drive root.";
            return sameRoot;
        }

        explanation = "Filesystem identity could not be established, so cross-filesystem safety is unverified.";
        return false;
    }
}

/// <summary>Locates configured or PATH-resolved executables without running a shell.</summary>
public sealed class ExternalToolLocator : IExternalToolLocator
{
    private readonly HostPlatformKind _platform;
    private readonly Func<string, string?> _environmentVariableReader;

    /// <summary>Creates a locator for the current environment.</summary>
    public ExternalToolLocator()
        : this(PlatformServices.CurrentPlatform, Environment.GetEnvironmentVariable)
    {
    }

    /// <summary>Creates a deterministic locator for tests and explicit composition.</summary>
    public ExternalToolLocator(
        HostPlatformKind platform,
        Func<string, string?> environmentVariableReader)
    {
        _platform = platform;
        _environmentVariableReader = environmentVariableReader ??
                                     throw new ArgumentNullException(nameof(environmentVariableReader));
    }

    /// <inheritdoc />
    public ExternalToolLocation Locate(string commandName, string? configuredPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        if (commandName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            return new ExternalToolLocation(false, null, "The tool command name must not contain a path separator.");
        }

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (!Path.IsPathFullyQualified(configuredPath))
            {
                return new ExternalToolLocation(false, null, "The configured executable path must be absolute.");
            }

            return Inspect(Path.GetFullPath(configuredPath), configured: true);
        }

        var pathValue = _environmentVariableReader("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return new ExternalToolLocation(false, null, "PATH is empty; configure an absolute executable path.");
        }

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Path.IsPathFullyQualified(directory))
            {
                continue;
            }

            foreach (var candidateName in CandidateNames(commandName))
            {
                var result = Inspect(Path.Combine(directory, candidateName), configured: false);
                if (result.IsAvailable)
                {
                    return result;
                }
            }
        }

        return new ExternalToolLocation(
            false,
            null,
            $"No executable named '{commandName}' was found on PATH.");
    }

    private ExternalToolLocation Inspect(string path, bool configured)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new ExternalToolLocation(
                    false,
                    null,
                    configured
                        ? "The configured executable does not exist."
                        : "The PATH candidate does not exist.");
            }

            var inspectedPath = path;
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            {
                var resolved = new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true);
                if (resolved is null || !resolved.Exists)
                {
                    return new ExternalToolLocation(
                        false,
                        null,
                        "The executable link target could not be resolved safely.");
                }

                inspectedPath = resolved.FullName;
            }

            if (_platform == HostPlatformKind.Linux &&
                OperatingSystem.IsLinux())
            {
                var mode = File.GetUnixFileMode(inspectedPath);
                if (!mode.HasFlag(UnixFileMode.UserExecute) &&
                    !mode.HasFlag(UnixFileMode.GroupExecute) &&
                    !mode.HasFlag(UnixFileMode.OtherExecute))
                {
                    return new ExternalToolLocation(false, null, "The executable file does not have an execute bit.");
                }
            }

            return new ExternalToolLocation(
                true,
                Path.GetFullPath(inspectedPath),
                inspectedPath == path
                    ? "A usable executable was found."
                    : "A usable executable was found through a resolved link.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or PlatformNotSupportedException)
        {
            return new ExternalToolLocation(false, null, "The executable candidate could not be inspected.");
        }
    }

    private IEnumerable<string> CandidateNames(string commandName)
    {
        yield return commandName;
        if (_platform == HostPlatformKind.Windows &&
            string.IsNullOrEmpty(Path.GetExtension(commandName)))
        {
            var extensions = (_environmentVariableReader("PATHEXT") ?? ".EXE;.COM")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var extension in extensions.Where(value => value.StartsWith('.')))
            {
                yield return commandName + extension.ToLowerInvariant();
                yield return commandName + extension.ToUpperInvariant();
            }
        }
    }
}

/// <summary>Reports current platform capabilities for UI gating and diagnostics.</summary>
public sealed class PlatformCapabilityProvider : IPlatformCapabilityProvider
{
    private readonly IApplicationPathProvider _pathProvider;

    /// <summary>Creates a capability report from the active platform services.</summary>
    public PlatformCapabilityProvider(
        IApplicationPathProvider pathProvider,
        IFileIdentityProvider identityProvider,
        IExternalToolLocator toolLocator)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        ArgumentNullException.ThrowIfNull(identityProvider);
        ArgumentNullException.ThrowIfNull(toolLocator);
        Platform = PlatformServices.CurrentPlatform;
        OperatingSystemDescription = RuntimeInformation.OSDescription;
        ProcessArchitecture = RuntimeInformation.ProcessArchitecture;
        RuntimeDescription = RuntimeInformation.FrameworkDescription;
        Capabilities = BuildCapabilities(identityProvider, toolLocator);
    }

    /// <inheritdoc />
    public HostPlatformKind Platform { get; }

    /// <inheritdoc />
    public string OperatingSystemDescription { get; }

    /// <inheritdoc />
    public Architecture ProcessArchitecture { get; }

    /// <inheritdoc />
    public string RuntimeDescription { get; }

    /// <inheritdoc />
    public IReadOnlyList<PlatformCapability> Capabilities { get; }

    /// <inheritdoc />
    public PlatformCapability Get(PlatformCapabilityKind kind) =>
        Capabilities.FirstOrDefault(value => value.Kind == kind) ??
        new PlatformCapability(kind, PlatformSupportState.Unavailable, "The capability is not recognized.");

    /// <inheritdoc />
    public string ExportHumanReadable()
    {
        var paths = _pathProvider.Paths;
        var output = new StringBuilder();
        output.AppendLine("OmniSorSe platform diagnostics");
        output.AppendLine($"Platform: {Platform}");
        output.AppendLine($"Operating system: {OperatingSystemDescription}");
        output.AppendLine($"Process architecture: {ProcessArchitecture}");
        output.AppendLine($"Runtime: {RuntimeDescription}");
        output.AppendLine($"Runtime identifier: {RuntimeInformation.RuntimeIdentifier}");
        output.AppendLine($"Configuration: {paths.ConfigurationDirectory}");
        output.AppendLine($"Data: {paths.DataDirectory}");
        output.AppendLine($"State: {paths.StateDirectory}");
        output.AppendLine($"Cache: {paths.CacheDirectory}");
        output.AppendLine($"Diagnostics: {paths.DiagnosticsDirectory}");
        output.AppendLine($"Plugins: {paths.PluginDirectory}");
        output.AppendLine("Symlink policy: links are not followed across approved roots.");
        output.AppendLine();
        output.AppendLine("Capabilities:");
        foreach (var capability in Capabilities)
        {
            output.AppendLine($"- {capability.Kind}: {capability.State} — {capability.Explanation}");
        }

        return output.ToString();
    }

    private IReadOnlyList<PlatformCapability> BuildCapabilities(
        IFileIdentityProvider identityProvider,
        IExternalToolLocator toolLocator)
    {
        var verifiedDesktop = Platform == HostPlatformKind.Windows;
        var linuxPreview = Platform == HostPlatformKind.Linux;
        var mutationState = verifiedDesktop
            ? PlatformSupportState.Supported
            : linuxPreview
                ? PlatformSupportState.SupportedWithLimitations
                : PlatformSupportState.Unavailable;
        var coreState = verifiedDesktop
            ? PlatformSupportState.Supported
            : linuxPreview
                ? PlatformSupportState.SupportedWithLimitations
                : PlatformSupportState.Unverified;
        var tesseract = toolLocator.Locate("tesseract");
        var values = new[]
        {
            Capability(PlatformCapabilityKind.Scanning, coreState, "Read-only enumeration is root-confined and skips links."),
            Capability(PlatformCapabilityKind.MetadataExtraction, coreState, "Per-file access and format failures remain isolated."),
            Capability(PlatformCapabilityKind.ContentExtraction, coreState, "Managed extractors are bounded; native renderer availability is checked."),
            Capability(
                PlatformCapabilityKind.Ocr,
                Platform is not (HostPlatformKind.Windows or HostPlatformKind.Linux)
                    ? PlatformSupportState.Unverified
                    : tesseract.IsAvailable
                        ? PlatformSupportState.SupportedWithLimitations
                        : PlatformSupportState.Unavailable,
                tesseract.IsAvailable ? "An external Tesseract executable was detected." : tesseract.Explanation),
            Capability(PlatformCapabilityKind.AiProviderInvocation, coreState, "Uses only a configured bounded HTTP endpoint; no provider process is auto-started."),
            Capability(PlatformCapabilityKind.DuplicateDetection, coreState, "SHA-256 content hashing is platform-neutral."),
            Capability(PlatformCapabilityKind.FileRename, mutationState, "Requires reviewed Change Plan, current-platform validation, journal, and verification."),
            Capability(PlatformCapabilityKind.FileMove, mutationState, "Only same-filesystem non-overwriting moves are supported."),
            Capability(PlatformCapabilityKind.DirectoryCreation, mutationState, "Creation remains root-confined and journalled."),
            Capability(PlatformCapabilityKind.DurableJournaling, mutationState, "Atomic temporary-file replacement is used; universal fsync guarantees are not claimed."),
            Capability(PlatformCapabilityKind.Undo, mutationState, "Compensating operations verify resulting state and fail closed on conflicts."),
            Capability(
                PlatformCapabilityKind.WatchedFolders,
                Platform is HostPlatformKind.Windows or HostPlatformKind.Linux
                    ? PlatformSupportState.SupportedWithLimitations
                    : PlatformSupportState.Unverified,
                Platform == HostPlatformKind.Linux
                    ? "FileSystemWatcher uses inotify; descriptor limits, overflow, duplication, and event loss require reconciliation."
                    : "Operating-system events are hints; reconciliation remains authoritative."),
            Capability(
                PlatformCapabilityKind.StableFileIdentity,
                identityProvider.SupportsNativeIdentity
                    ? PlatformSupportState.SupportedWithLimitations
                    : PlatformSupportState.Unverified,
                identityProvider.SupportsNativeIdentity
                    ? "Native identity is available but is not permanent across copies, migrations, or inode/file-index reuse."
                    : "Only collision-prone metadata fallback identity is available."),
            Capability(PlatformCapabilityKind.LinkInspection, coreState, "Symbolic-link/reparse entries are detected and not traversed across roots."),
            Capability(PlatformCapabilityKind.PermissionValidation, mutationState, "Mode/attribute inspection is advisory; actual access remains authoritative and no elevation is attempted."),
            Capability(PlatformCapabilityKind.PluginLoading, coreState, "Managed plugins load in-process after manifest, integrity, dependency, and runtime checks."),
            Capability(
                PlatformCapabilityKind.NativePluginDependencies,
                Platform is HostPlatformKind.Windows or HostPlatformKind.Linux
                    ? PlatformSupportState.SupportedWithLimitations
                    : PlatformSupportState.Unavailable,
                "Native plugins must explicitly declare a matching runtime identifier."),
            Capability(
                PlatformCapabilityKind.FileManagerIntegration,
                Platform is HostPlatformKind.Windows or HostPlatformKind.Linux
                    ? PlatformSupportState.SupportedWithLimitations
                    : PlatformSupportState.Unavailable,
                "Uses an exact path through the operating-system association API; failure is non-fatal."),
            Capability(
                PlatformCapabilityKind.PackagingAndUpdates,
                Platform is HostPlatformKind.Windows or HostPlatformKind.MacOS
                    ? PlatformSupportState.SupportedWithLimitations
                    : PlatformSupportState.Unavailable,
                Platform switch
                {
                    HostPlatformKind.Windows =>
                        $"v{ApplicationVersionInfo.Display} provides a self-contained Windows x64 portable package and per-user installer. They are unsigned and no automatic updater is provided.",
                    HostPlatformKind.MacOS =>
                        $"v{ApplicationVersionInfo.Display} provides separate Intel and Apple Silicon disk images. They are publisher-unsigned and unnotarized; any ad-hoc signature does not identify a publisher, and no automatic updater is provided.",
                    HostPlatformKind.Linux =>
                        $"v{ApplicationVersionInfo.Display} supports source builds on Linux but does not publish a Linux installer or automatic updater.",
                    _ =>
                        $"v{ApplicationVersionInfo.Display} does not publish a package or automatic updater for this platform.",
                }),
        };
        return Array.AsReadOnly(values.OrderBy(value => value.Kind).ToArray());
    }

    private static PlatformCapability Capability(
        PlatformCapabilityKind kind,
        PlatformSupportState state,
        string explanation) =>
        new(kind, state, explanation);
}
