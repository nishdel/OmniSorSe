using System.Security.Cryptography;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Executor;

/// <summary>Implements conservative filesystem inspection and non-overwriting mutation.</summary>
public sealed class PhysicalFileSystemGateway : IFileSystemGateway
{
    /// <inheritdoc />
    public string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

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

        return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
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

        var identity = FormattableString.Invariant(
            $"file:{info.Length}:{lastWrite.UtcTicks}:{creation.UtcTicks}:{hash ?? "-"}");
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

        Directory.CreateDirectory(path);
    }

    /// <inheritdoc />
    public void MoveFile(string sourcePath, string destinationPath)
    {
        if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
        {
            throw new IOException("The destination already exists.");
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

