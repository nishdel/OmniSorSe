using System.Security.Cryptography;
using OpenSorSe.Core.Platform;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Executor;

/// <summary>Implements conservative filesystem inspection and non-overwriting mutation.</summary>
public sealed class PhysicalFileSystemGateway : IFileSystemGateway
{
    private readonly IPathSemantics _pathSemantics;
    private readonly IFileIdentityProvider _identityProvider;
    private readonly IFileSystemCapabilities _capabilities;

    /// <summary>Creates a gateway with the current process platform services.</summary>
    public PhysicalFileSystemGateway()
        : this(
            PlatformServices.CurrentPathSemantics,
            FileIdentityProviderFactory.CreateCurrent(),
            null)
    {
    }

    /// <summary>Creates a gateway over explicit platform services.</summary>
    public PhysicalFileSystemGateway(
        IPathSemantics pathSemantics,
        IFileIdentityProvider identityProvider,
        IFileSystemCapabilities? capabilities = null)
    {
        _pathSemantics = pathSemantics ?? throw new ArgumentNullException(nameof(pathSemantics));
        _identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
        _capabilities = capabilities ?? new FileSystemCapabilities(identityProvider, pathSemantics);
    }

    /// <inheritdoc />
    public string NormalizePath(string path) => _pathSemantics.NormalizeAbsolutePath(path);

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public bool IsDirectoryEmpty(string path) =>
        Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any();

    /// <inheritdoc />
    public bool IsReparsePoint(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        return _capabilities.InspectLink(path).IsLink;
    }

    /// <inheritdoc />
    public async Task<FileIdentitySnapshot?> CaptureFileIdentityAsync(
        string path,
        bool includeHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path))
        {
            return null;
        }

        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.Directory) ||
            attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return null;
        }

        var info = new FileInfo(path);
        var lastWrite = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
        var creation = new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero);
        string? hash = null;
        if (includeHash)
        {
            await using var stream = new FileStream(
                path,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    BufferSize = 64 * 1024,
                });
            var bytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            hash = Convert.ToHexString(bytes).ToLowerInvariant();
        }

        var platformIdentity = _identityProvider.Capture(path);
        var identity = platformIdentity.Identity ?? FormattableString.Invariant(
            $"file:{info.Length}:{lastWrite.UtcTicks}:{creation.UtcTicks}");
        identity = $"{identity}:sha256:{hash ?? "-"}";
        return new FileIdentitySnapshot(identity, info.Length, lastWrite, creation, hash);
    }

    /// <inheritdoc />
    public async Task<bool> CanOpenExclusivelyAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var stream = new FileStream(
                path,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous,
                    BufferSize = 1,
                });
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void CreateDirectory(string path)
    {
        if (File.Exists(path))
        {
            throw new IOException("A file already occupies the requested directory path.");
        }

        if (Directory.Exists(path))
        {
            return;
        }

        var parent = Path.GetDirectoryName(path)
            ?? throw new IOException("The directory path has no parent.");
        if (!_capabilities.CanWriteDirectory(parent, out var explanation))
        {
            throw new UnauthorizedAccessException($"The directory cannot be created safely. {explanation}");
        }

        Directory.CreateDirectory(path);
    }

    /// <inheritdoc />
    public void MoveFile(string sourcePath, string destinationPath)
    {
        if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
        {
            throw new IOException("The destination already exists.");
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new IOException("The destination path has no parent directory.");
        if (!_capabilities.CanWriteDirectory(destinationDirectory, out var permissionExplanation))
        {
            throw new UnauthorizedAccessException(
                $"The destination directory cannot be written safely. {permissionExplanation}");
        }

        if (!_capabilities.AreOnSameFileSystem(
                sourcePath,
                destinationDirectory,
                out var filesystemExplanation))
        {
            throw new IOException(
                $"Cross-filesystem moves are not supported by the journalled rename boundary. {filesystemExplanation}");
        }

        File.Move(sourcePath, destinationPath, overwrite: false);
    }

    /// <inheritdoc />
    public void DeleteDirectory(string path)
    {
        if (!IsDirectoryEmpty(path))
        {
            throw new IOException("The directory is not empty.");
        }

        Directory.Delete(path, recursive: false);
    }
}
