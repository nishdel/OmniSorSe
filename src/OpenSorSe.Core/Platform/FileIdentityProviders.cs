using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenSorSe.Core.Platform;

/// <summary>Creates the native identity provider appropriate for the current platform.</summary>
public static class FileIdentityProviderFactory
{
    /// <summary>Creates a Windows, Linux, or explicit best-effort provider.</summary>
    public static IFileIdentityProvider CreateCurrent() => PlatformServices.CurrentPlatform switch
    {
        HostPlatformKind.Windows => new WindowsFileIdentityProvider(),
        HostPlatformKind.Linux => new LinuxFileIdentityProvider(),
        _ => new BestEffortFileIdentityProvider(),
    };
}

/// <summary>Captures Windows volume serial and file-index identity.</summary>
public sealed class WindowsFileIdentityProvider : IFileIdentityProvider
{
    /// <inheritdoc />
    public bool SupportsNativeIdentity => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public PlatformFileIdentity Capture(string path)
    {
        if (!SupportsNativeIdentity)
        {
            return Unavailable("Windows native identity is unavailable on this host.");
        }

        try
        {
            if (!File.Exists(path) ||
                File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            {
                return Unavailable("The path is not an existing regular non-link file.");
            }

            using var handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.None);
            if (!GetFileInformationByHandle(handle, out var information))
            {
                return BestEffort(path, "Windows native identity could not be read; metadata fallback is active.");
            }

            var index = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
            var fileSystem = $"windows-volume:{information.VolumeSerialNumber:x8}";
            return new PlatformFileIdentity(
                $"{fileSystem}:file:{index:x16}",
                fileSystem,
                FileIdentityStrength.Native,
                "Volume serial and file index identify this object on the current mounted volume.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return BestEffort(path, "Windows native identity was inaccessible; metadata fallback is active.");
        }
    }

    /// <inheritdoc />
    public string? GetFileSystemId(string path)
    {
        if (!SupportsNativeIdentity)
        {
            return null;
        }

        try
        {
            if (Directory.Exists(path))
            {
                var root = Path.GetPathRoot(Path.GetFullPath(path));
                return string.IsNullOrWhiteSpace(root)
                    ? null
                    : $"windows-root:{root.ToUpperInvariant()}";
            }

            using var handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.None);
            return GetFileInformationByHandle(handle, out var information)
                ? $"windows-volume:{information.VolumeSerialNumber:x8}"
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return null;
        }
    }

    private static PlatformFileIdentity BestEffort(string path, string explanation) =>
        BestEffortFileIdentityProvider.CaptureMetadata(path, explanation);

    private static PlatformFileIdentity Unavailable(string explanation) =>
        new(null, null, FileIdentityStrength.Unavailable, explanation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle fileHandle,
        out ByHandleFileInformation fileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}

/// <summary>Captures Linux device and inode identity through the native stat API.</summary>
public sealed class LinuxFileIdentityProvider : IFileIdentityProvider
{
    private const uint FileTypeMask = 0xF000;
    private const uint RegularFile = 0x8000;

    /// <inheritdoc />
    public bool SupportsNativeIdentity =>
        OperatingSystem.IsLinux() &&
        RuntimeInformation.ProcessArchitecture == Architecture.X64;

    /// <inheritdoc />
    public PlatformFileIdentity Capture(string path)
    {
        if (!SupportsNativeIdentity)
        {
            return new PlatformFileIdentity(
                null,
                null,
                FileIdentityStrength.Unavailable,
                "Linux device/inode identity is unavailable on this runtime architecture.");
        }

        try
        {
            if (!File.Exists(path) ||
                File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            {
                return new PlatformFileIdentity(
                    null,
                    null,
                    FileIdentityStrength.Unavailable,
                    "The path is not an existing regular non-link file.");
            }

            if (Stat(path, out var information) != 0 ||
                (information.Mode & FileTypeMask) != RegularFile)
            {
                return BestEffortFileIdentityProvider.CaptureMetadata(
                    path,
                    "Linux stat identity was unavailable; metadata fallback is active.");
            }

            var fileSystem = $"linux-device:{information.Device:x16}";
            return new PlatformFileIdentity(
                $"{fileSystem}:inode:{information.Inode:x16}",
                fileSystem,
                FileIdentityStrength.Native,
                "Device and inode identify this object on the current mounted filesystem; inode reuse remains possible.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return BestEffortFileIdentityProvider.CaptureMetadata(
                path,
                "Linux stat identity was inaccessible; metadata fallback is active.");
        }
    }

    /// <inheritdoc />
    public string? GetFileSystemId(string path)
    {
        if (!SupportsNativeIdentity)
        {
            return null;
        }

        try
        {
            return Stat(path, out var information) == 0
                ? $"linux-device:{information.Device:x16}"
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    [DllImport("libc", EntryPoint = "stat", SetLastError = true)]
    private static extern int Stat(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        out LinuxStat information);

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxStat
    {
        public ulong Device;
        public ulong Inode;
        public ulong LinkCount;
        public uint Mode;
        public uint UserId;
        public uint GroupId;
        public int Padding;
        public ulong RawDevice;
        public long Size;
        public long BlockSize;
        public long Blocks;
        public long AccessSeconds;
        public long AccessNanoseconds;
        public long ModificationSeconds;
        public long ModificationNanoseconds;
        public long ChangeSeconds;
        public long ChangeNanoseconds;
        public long Reserved0;
        public long Reserved1;
        public long Reserved2;
    }
}

/// <summary>Provides an explicit metadata identity when no native provider is verified.</summary>
public sealed class BestEffortFileIdentityProvider : IFileIdentityProvider
{
    /// <inheritdoc />
    public bool SupportsNativeIdentity => false;

    /// <inheritdoc />
    public PlatformFileIdentity Capture(string path) =>
        CaptureMetadata(
            path,
            "Creation time, length, and modification time are a best-effort identity and can collide.");

    /// <inheritdoc />
    public string? GetFileSystemId(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrWhiteSpace(root) ? null : $"path-root:{root}";
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    internal static PlatformFileIdentity CaptureMetadata(string path, string explanation)
    {
        try
        {
            var information = new FileInfo(path);
            if (!information.Exists ||
                information.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return new PlatformFileIdentity(
                    null,
                    null,
                    FileIdentityStrength.Unavailable,
                    "The path is not an existing regular non-link file.");
            }

            var identity = FormattableString.Invariant(
                $"metadata:{information.CreationTimeUtc.Ticks:x16}:{information.LastWriteTimeUtc.Ticks:x16}:{information.Length:x16}");
            return new PlatformFileIdentity(
                identity,
                null,
                FileIdentityStrength.BestEffort,
                explanation);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new PlatformFileIdentity(
                null,
                null,
                FileIdentityStrength.Unavailable,
                "File metadata identity could not be captured.");
        }
    }
}
