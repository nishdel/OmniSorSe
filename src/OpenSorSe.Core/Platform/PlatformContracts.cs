using System.Runtime.InteropServices;

namespace OpenSorSe.Core.Platform;

/// <summary>Identifies the operating-system family understood by the platform layer.</summary>
public enum HostPlatformKind
{
    /// <summary>Microsoft Windows.</summary>
    Windows,

    /// <summary>A Linux-based operating system.</summary>
    Linux,

    /// <summary>Apple macOS, which is not a verified v1.5 target.</summary>
    MacOS,

    /// <summary>An unrecognized operating system.</summary>
    Other,
}

/// <summary>Describes how strongly one platform capability is supported.</summary>
public enum PlatformSupportState
{
    /// <summary>The capability is implemented and repeatably verified.</summary>
    Supported,

    /// <summary>The capability is implemented with explicit limitations.</summary>
    SupportedWithLimitations,

    /// <summary>An implementation exists but has not been verified on this platform.</summary>
    Unverified,

    /// <summary>The capability is disabled because its safety requirements cannot be met.</summary>
    Unavailable,
}

/// <summary>Identifies a platform-sensitive OpenSorSe capability.</summary>
public enum PlatformCapabilityKind
{
    /// <summary>Read-only folder scanning.</summary>
    Scanning,
    /// <summary>Filesystem metadata extraction.</summary>
    MetadataExtraction,
    /// <summary>Document content extraction.</summary>
    ContentExtraction,
    /// <summary>External Tesseract OCR discovery and invocation.</summary>
    Ocr,
    /// <summary>Configured Ollama-compatible HTTP invocation.</summary>
    AiProviderInvocation,
    /// <summary>Content-hash duplicate detection.</summary>
    DuplicateDetection,
    /// <summary>Approved file rename.</summary>
    FileRename,
    /// <summary>Approved same-filesystem file move.</summary>
    FileMove,
    /// <summary>Approved directory creation.</summary>
    DirectoryCreation,
    /// <summary>Durable operation-journal persistence.</summary>
    DurableJournaling,
    /// <summary>Verified compensating Undo.</summary>
    Undo,
    /// <summary>Operating-system file watching plus reconciliation.</summary>
    WatchedFolders,
    /// <summary>Native or best-effort stable file identity.</summary>
    StableFileIdentity,
    /// <summary>Symbolic-link or reparse-point inspection.</summary>
    LinkInspection,
    /// <summary>Permission and writable-access inspection.</summary>
    PermissionValidation,
    /// <summary>Managed plugin loading.</summary>
    PluginLoading,
    /// <summary>Platform-constrained native plugin dependencies.</summary>
    NativePluginDependencies,
    /// <summary>Opening a path with the desktop file manager.</summary>
    FileManagerIntegration,
    /// <summary>Platform packaging or automatic update support.</summary>
    PackagingAndUpdates,
}

/// <summary>Defines how recipe output names are validated for portability.</summary>
public enum FileNamePortabilityMode
{
    /// <summary>Validate only against the active platform.</summary>
    CurrentPlatform,

    /// <summary>Validate against the conservative Windows/Linux interchange policy.</summary>
    Portable,

    /// <summary>Validate against Windows filename constraints even on Linux.</summary>
    WindowsCompatible,
}

/// <summary>Describes one platform capability and the reason for its state.</summary>
/// <param name="Kind">Capability identifier.</param>
/// <param name="State">Current support state.</param>
/// <param name="Explanation">Human-readable limitation or assurance.</param>
public sealed record PlatformCapability(
    PlatformCapabilityKind Kind,
    PlatformSupportState State,
    string Explanation);

/// <summary>Contains application-owned configuration, data, state, cache, diagnostic, and plugin paths.</summary>
/// <param name="ConfigurationDirectory">Directory for user-editable configuration.</param>
/// <param name="DataDirectory">Directory for durable application data.</param>
/// <param name="StateDirectory">Directory for journals and execution state.</param>
/// <param name="CacheDirectory">Directory for reproducible caches.</param>
/// <param name="DiagnosticsDirectory">Directory for ordinary diagnostic logs.</param>
/// <param name="PluginDirectory">Controlled local plugin installation root.</param>
public sealed record ApplicationPathSet(
    string ConfigurationDirectory,
    string DataDirectory,
    string StateDirectory,
    string CacheDirectory,
    string DiagnosticsDirectory,
    string PluginDirectory);

/// <summary>Provides immutable capability and environment information for diagnostics and UI gating.</summary>
public interface IPlatformCapabilityProvider
{
    /// <summary>Gets the detected host platform.</summary>
    HostPlatformKind Platform { get; }

    /// <summary>Gets the operating-system description without machine-specific paths or secrets.</summary>
    string OperatingSystemDescription { get; }

    /// <summary>Gets the process architecture.</summary>
    Architecture ProcessArchitecture { get; }

    /// <summary>Gets the .NET runtime description.</summary>
    string RuntimeDescription { get; }

    /// <summary>Gets all known capabilities in deterministic order.</summary>
    IReadOnlyList<PlatformCapability> Capabilities { get; }

    /// <summary>Gets one capability, returning an unavailable result for unknown values.</summary>
    PlatformCapability Get(PlatformCapabilityKind kind);

    /// <summary>Creates a bounded human-readable report suitable for a bug report.</summary>
    string ExportHumanReadable();
}

/// <summary>Centralizes current-platform path comparison, validation, and confinement.</summary>
public interface IPathSemantics
{
    /// <summary>Gets the represented operating-system family.</summary>
    HostPlatformKind Platform { get; }

    /// <summary>Gets whether path comparisons are case-sensitive by default.</summary>
    bool IsCaseSensitive { get; }

    /// <summary>Gets the platform path comparer.</summary>
    StringComparer Comparer { get; }

    /// <summary>Gets the platform path comparison.</summary>
    StringComparison Comparison { get; }

    /// <summary>Normalizes one fully qualified path without resolving or following links.</summary>
    string NormalizeAbsolutePath(string path);

    /// <summary>Returns whether the normalized candidate remains within the normalized root.</summary>
    bool IsWithinRoot(string root, string candidate);

    /// <summary>Returns whether two paths identify the same lexical platform path.</summary>
    bool PathsEqual(string first, string second);

    /// <summary>Returns whether two different spellings are a case-only path change.</summary>
    bool IsCaseOnlyDifference(string first, string second);

    /// <summary>Validates one filename segment under the selected portability policy.</summary>
    bool IsValidFileName(string value, FileNamePortabilityMode mode, out string? reason);
}

/// <summary>Exposes platform-appropriate application-owned storage paths.</summary>
public interface IApplicationPathProvider
{
    /// <summary>Gets all resolved owned directories.</summary>
    ApplicationPathSet Paths { get; }

    /// <summary>Gets the settings JSON path.</summary>
    string SettingsFilePath { get; }

    /// <summary>Creates only the exact application-owned directories represented by <see cref="Paths"/>.</summary>
    void EnsureOwnedDirectories();
}

/// <summary>Describes the strength of a captured file identity.</summary>
public enum FileIdentityStrength
{
    /// <summary>A platform-native filesystem/object identity was captured.</summary>
    Native,

    /// <summary>A bounded metadata fallback was captured.</summary>
    BestEffort,

    /// <summary>No safe identity was available.</summary>
    Unavailable,
}

/// <summary>Contains a platform file identity without claiming permanence across copies or migrations.</summary>
/// <param name="Identity">Opaque identity value.</param>
/// <param name="FileSystemId">Opaque volume or device identifier when available.</param>
/// <param name="Strength">Identity assurance.</param>
/// <param name="Explanation">Human-readable limitation.</param>
public sealed record PlatformFileIdentity(
    string? Identity,
    string? FileSystemId,
    FileIdentityStrength Strength,
    string Explanation);

/// <summary>Captures platform-native or explicit best-effort file identities.</summary>
public interface IFileIdentityProvider
{
    /// <summary>Gets whether a native identity implementation is available.</summary>
    bool SupportsNativeIdentity { get; }

    /// <summary>Captures identity for one existing regular file without following an unsupported link.</summary>
    PlatformFileIdentity Capture(string path);

    /// <summary>Gets an opaque filesystem/volume identity for an existing path when available.</summary>
    string? GetFileSystemId(string path);
}

/// <summary>Describes one inspected symbolic-link or reparse-point entry.</summary>
/// <param name="IsLink">Whether the entry is a managed link/reparse object.</param>
/// <param name="LinkTarget">The stored link target when available.</param>
/// <param name="ResolvedTarget">The final resolved target when safely available.</param>
/// <param name="Explanation">Human-readable inspection result.</param>
public sealed record FileLinkInspection(
    bool IsLink,
    string? LinkTarget,
    string? ResolvedTarget,
    string Explanation);

/// <summary>Provides non-mutating filesystem capability and boundary checks.</summary>
public interface IFileSystemCapabilities
{
    /// <summary>Inspects one entry for symbolic-link/reparse behavior.</summary>
    FileLinkInspection InspectLink(string path);

    /// <summary>Returns whether an existing directory is writable by the current process without elevating.</summary>
    bool CanWriteDirectory(string path, out string explanation);

    /// <summary>Returns available bytes for the filesystem containing the supplied path, if known.</summary>
    long? GetAvailableFreeSpace(string path);

    /// <summary>Returns whether two existing paths are known to reside on the same filesystem.</summary>
    bool AreOnSameFileSystem(string firstPath, string secondPath, out string explanation);
}

/// <summary>Describes discovery of one exact external executable.</summary>
/// <param name="IsAvailable">Whether a usable executable was found.</param>
/// <param name="ExecutablePath">Resolved executable path when available.</param>
/// <param name="Explanation">Human-readable discovery result.</param>
public sealed record ExternalToolLocation(
    bool IsAvailable,
    string? ExecutablePath,
    string Explanation);

/// <summary>Finds explicitly configured or PATH-resolved external tools without invoking a command shell.</summary>
public interface IExternalToolLocator
{
    /// <summary>Locates one tool by configured path or platform command name.</summary>
    ExternalToolLocation Locate(string commandName, string? configuredPath = null);
}
