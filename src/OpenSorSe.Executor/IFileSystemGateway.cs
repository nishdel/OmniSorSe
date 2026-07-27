#pragma warning disable CS1591

using OpenSorSe.Executor.Models;

namespace OpenSorSe.Executor;

/// <summary>Provides the only low-level user-file mutation boundary used by Change Plan execution.</summary>
public interface IFileSystemGateway
{
    string NormalizePath(string path);
    bool FileExists(string path);
    bool DirectoryExists(string path);
    bool IsDirectoryEmpty(string path);
    bool IsReparsePoint(string path);
    Task<FileIdentitySnapshot?> CaptureFileIdentityAsync(string path, bool includeHash, CancellationToken cancellationToken);
    Task<bool> CanOpenExclusivelyAsync(string path, CancellationToken cancellationToken);
    void CreateDirectory(string path);
    void MoveFile(string sourcePath, string destinationPath);
    void DeleteDirectory(string path);
}
