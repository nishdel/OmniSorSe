using System.IO.Enumeration;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Application.Indexing;

/// <summary>Discovers regular files using platform identity and explicit link-boundary checks.</summary>
public sealed class PhysicalIndexFileDiscovery : IIndexFileDiscovery
{
    private static readonly HashSet<string> GeneratedDirectoryNames = new(
        [".git", ".svn", ".hg", ".vs", ".idea", "bin", "obj", "node_modules", "TestResults", ".artifacts"],
        StringComparer.OrdinalIgnoreCase);

    private readonly IFileIdentityProvider _identityProvider;
    private readonly IPathSemantics _pathSemantics;

    /// <summary>Initializes safe physical discovery.</summary>
    public PhysicalIndexFileDiscovery(
        IFileIdentityProvider identityProvider,
        IPathSemantics pathSemantics)
    {
        _identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
        _pathSemantics = pathSemantics ?? throw new ArgumentNullException(nameof(pathSemantics));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IndexingFileObservation> DiscoverAsync(
        IndexingSource source,
        DeepIndexingSettings settings,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        var root = _pathSemantics.NormalizeAbsolutePath(source.RootPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("The indexing source is not currently accessible.");
        }

        var directories = new Stack<string>();
        directories.Push(root);
        var processed = 0;
        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            IReadOnlyList<string> files;
            IReadOnlyList<string> children;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).ToArray();
                children = source.IncludeSubfolders
                    ? Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly).ToArray()
                    : [];
            }
            catch (UnauthorizedAccessException)
            {
                throw new IOException("Index discovery could not safely reconcile an inaccessible folder.");
            }
            catch (IOException exception)
            {
                throw new IOException("Index discovery was interrupted by a transient folder-access failure.", exception);
            }

            foreach (var child in children.OrderByDescending(path => path, _pathSemantics.Comparer))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ShouldExcludeDirectory(root, child, source, settings))
                {
                    continue;
                }

                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    throw new IOException("Index discovery could not inspect a folder link safely.");
                }
                catch (IOException exception)
                {
                    throw new IOException("Index discovery could not inspect a folder safely.", exception);
                }

                directories.Push(child);
            }

            foreach (var path in files.OrderBy(value => value, _pathSemantics.Comparer))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(root, path);
                if (IsExcluded(relativePath, source.Exclusions))
                {
                    continue;
                }

                FileInfo info;
                try
                {
                    info = new FileInfo(path);
                    info.Refresh();
                    if (!info.Exists || (info.Attributes & FileAttributes.Directory) != 0)
                    {
                        continue;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    throw new IOException("Index discovery could not read file metadata because access was denied.");
                }
                catch (IOException exception)
                {
                    throw new IOException("Index discovery could not read file metadata safely.", exception);
                }

                var identity = (info.Attributes & FileAttributes.ReparsePoint) != 0
                    ? new PlatformFileIdentity(null, null, FileIdentityStrength.Unavailable, "Link entries are not followed.")
                    : CaptureIdentity(path);
                var created = new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero);
                var modified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
                var metadataFingerprint = CreateMetadataFingerprint(
                    info.Length,
                    created,
                    modified,
                    info.Attributes);
                yield return new IndexingFileObservation(
                    info.FullName,
                    relativePath,
                    identity.Identity,
                    identity.FileSystemId,
                    info.Length,
                    created,
                    modified,
                    info.Attributes,
                    metadataFingerprint);

                processed++;
                if (processed % 128 == 0)
                {
                    await Task.Yield();
                }
            }
        }
    }

    /// <summary>Creates a path-independent metadata fingerprint for incremental decisions.</summary>
    public static string CreateMetadataFingerprint(
        long length,
        DateTimeOffset creationTimeUtc,
        DateTimeOffset lastWriteTimeUtc,
        FileAttributes attributes)
    {
        var value = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{length}|{creationTimeUtc.UtcTicks}|{lastWriteTimeUtc.UtcTicks}|{(long)attributes}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private PlatformFileIdentity CaptureIdentity(string path)
    {
        try
        {
            return _identityProvider.Capture(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new PlatformFileIdentity(
                null,
                null,
                FileIdentityStrength.Unavailable,
                "A stable identity was unavailable; path and content fingerprinting will be used.");
        }
    }

    private static bool ShouldExcludeDirectory(
        string root,
        string directory,
        IndexingSource source,
        DeepIndexingSettings settings)
    {
        if (settings.ExcludeGeneratedFolders &&
            GeneratedDirectoryNames.Contains(Path.GetFileName(directory)))
        {
            return true;
        }

        return IsExcluded(Path.GetRelativePath(root, directory), source.Exclusions);
    }

    private static bool IsExcluded(string relativePath, IReadOnlyList<string> exclusions)
    {
        var normalized = relativePath.Replace(Path.DirectorySeparatorChar, '/');
        return exclusions.Any(pattern =>
            FileSystemName.MatchesSimpleExpression(
                pattern.Replace(Path.DirectorySeparatorChar, '/'),
                normalized,
                ignoreCase: OperatingSystem.IsWindows()));
    }
}
