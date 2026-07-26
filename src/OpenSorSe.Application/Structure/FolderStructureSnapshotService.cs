using System.Security.Cryptography;
using System.Text;

namespace OpenSorSe.Application.Structure;

/// <summary>Captures deterministic bounded filesystem metadata for one explicit root.</summary>
public sealed class FolderStructureSnapshotService : IFolderStructureSnapshotService
{
    /// <inheritdoc />
    public Task<FolderStructureSnapshot> CaptureAsync(
        string rootPath,
        CancellationToken cancellationToken) =>
        Task.Run(() => Capture(rootPath, cancellationToken), cancellationToken);

    private static FolderStructureSnapshot Capture(string rootPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Path.IsPathRooted(rootPath))
        {
            throw new ArgumentException("An absolute folder root is required.", nameof(rootPath));
        }

        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (!Directory.Exists(normalizedRoot))
        {
            throw new DirectoryNotFoundException("The selected restructuring root is unavailable.");
        }

        var nodes = new List<StructureNode>();
        var pending = new Stack<string>();
        pending.Push(normalizedRoot);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory)
                    .OrderBy(path => path, PathComparer)
                    .ToArray();
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var relativePath = NormalizeRelativePath(Path.GetRelativePath(normalizedRoot, entry));
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    var info = new DirectoryInfo(entry);
                    nodes.Add(new StructureNode(
                        relativePath,
                        true,
                        0,
                        info.LastWriteTimeUtc,
                        Fingerprint($"directory:{NormalizeForIdentity(relativePath)}")));
                    pending.Push(entry);
                }
                else
                {
                    var info = new FileInfo(entry);
                    nodes.Add(new StructureNode(
                        relativePath,
                        false,
                        info.Length,
                        info.LastWriteTimeUtc,
                        Fingerprint($"file:{info.Length}:{info.LastWriteTimeUtc.Ticks}")));
                }

                if (nodes.Count > StructureLimits.MaximumSnapshotNodes)
                {
                    throw new InvalidDataException(
                        $"A structure snapshot may contain at most {StructureLimits.MaximumSnapshotNodes} nodes.");
                }
            }
        }

        return StructureSnapshotFactory.Create(
            normalizedRoot,
            nodes,
            DateTimeOffset.UtcNow);
    }

    internal static string NormalizeRelativePath(string relativePath) =>
        relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    internal static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static string NormalizeForIdentity(string value) =>
        OperatingSystem.IsWindows() ? value.ToUpperInvariant() : value;

    internal static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

internal static class StructureSnapshotFactory
{
    internal static FolderStructureSnapshot Create(
        string rootPath,
        IEnumerable<StructureNode> nodes,
        DateTimeOffset capturedAtUtc)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var ordered = nodes
            .OrderBy(node => node.RelativePath, FolderStructureSnapshotService.PathComparer)
            .ToArray();
        var structureInput = string.Join(
            '\n',
            ordered.Select(node =>
                $"{(node.IsDirectory ? 'D' : 'F')}|{FolderStructureSnapshotService.NormalizeForIdentity(node.RelativePath)}|{node.IdentityFingerprint}"));
        return new FolderStructureSnapshot(
            normalizedRoot,
            FolderStructureSnapshotService.Fingerprint(
                $"root:{FolderStructureSnapshotService.NormalizeForIdentity(normalizedRoot)}"),
            FolderStructureSnapshotService.Fingerprint(structureInput),
            capturedAtUtc.ToUniversalTime(),
            Array.AsReadOnly(ordered));
    }
}
