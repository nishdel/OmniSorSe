#pragma warning disable CS1591

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Application.Watching;

public sealed class PhysicalWatchedFileSystem : IWatchedFileSystem
{
    private readonly WatchedFolderPathPolicy _pathPolicy;
    private readonly IFileIdentityProvider _fileIdentityProvider;
    private readonly ILogger _logger;

    public PhysicalWatchedFileSystem(WatchedFolderPathPolicy pathPolicy, ILoggingService loggingService)
        : this(pathPolicy, loggingService, FileIdentityProviderFactory.CreateCurrent())
    {
    }

    public PhysicalWatchedFileSystem(
        WatchedFolderPathPolicy pathPolicy,
        ILoggingService loggingService,
        IFileIdentityProvider fileIdentityProvider)
    {
        _pathPolicy = pathPolicy ?? throw new ArgumentNullException(nameof(pathPolicy));
        _fileIdentityProvider = fileIdentityProvider ??
                                throw new ArgumentNullException(nameof(fileIdentityProvider));
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(PhysicalWatchedFileSystem));
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public Task<IReadOnlyList<WatchedFileProbe>> EnumerateAsync(
        WatchedFolderConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return Task.Run(
            () => EnumerateCore(configuration, cancellationToken),
            CancellationToken.None);
    }

    public Task<WatchedFileProbe?> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (File.Exists(path))
            {
                return Task.FromResult<WatchedFileProbe?>(ProbeFile(path));
            }

            if (Directory.Exists(path))
            {
                return Task.FromResult<WatchedFileProbe?>(ProbeDirectory(path));
            }
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            _logger.LogDebug(
                "Watched path probe was deferred. Error category: {ErrorCategory}.",
                exception.GetType().Name);
        }

        return Task.FromResult<WatchedFileProbe?>(null);
    }

    private IReadOnlyList<WatchedFileProbe> EnumerateCore(
        WatchedFolderConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var root = _pathPolicy.CanonicalizeRoot(configuration.FolderPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("The watched folder is unavailable.");
        }

        var output = new List<WatchedFileProbe>();
        var directories = new Stack<string>();
        directories.Push(root);
        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = directories.Pop();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(current);
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                _logger.LogWarning(
                    "A watched directory could not be enumerated. Error category: {ErrorCategory}.",
                    exception.GetType().Name);
                continue;
            }

            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_pathPolicy.IsWithinRoot(root, child))
                {
                    _logger.LogWarning("A traversal candidate outside the watched root was rejected.");
                    continue;
                }

                WatchedFileProbe? probe;
                try
                {
                    probe = Directory.Exists(child) ? ProbeDirectory(child) : ProbeFile(child);
                }
                catch (Exception exception) when (IsRecoverable(exception))
                {
                    _logger.LogDebug(
                        "A transient watched entry could not be probed. Error category: {ErrorCategory}.",
                        exception.GetType().Name);
                    continue;
                }

                if (_pathPolicy.ShouldIgnore(
                        configuration,
                        probe.FullPath,
                        probe.Attributes,
                        probe.IsDirectory ? null : probe.SizeInBytes))
                {
                    _logger.LogDebug("A watched entry was excluded by the canonical ignore policy.");
                    continue;
                }

                output.Add(probe);
                if (probe.IsDirectory &&
                    configuration.IncludeSubfolders &&
                    !probe.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    directories.Push(probe.FullPath);
                }
            }
        }

        var normalized = DisambiguateFallbackIdentities(output);
        return Array.AsReadOnly(normalized
            .OrderBy(item => item.FullPath, WatchedFolderPathPolicy.PathComparer)
            .ToArray());
    }

    private WatchedFileProbe ProbeFile(string path)
    {
        var canonical = Path.GetFullPath(path);
        var info = new FileInfo(canonical);
        info.Refresh();
        var attributes = File.GetAttributes(canonical);
        if (attributes.HasFlag(FileAttributes.Directory))
        {
            return ProbeDirectory(canonical);
        }

        var created = ToUtc(info.CreationTimeUtc);
        var modified = ToUtc(info.LastWriteTimeUtc);
        return new WatchedFileProbe(
            canonical,
            false,
            info.Length,
            created,
            modified,
            attributes,
            ReadStableFileId(canonical, created, info.Length));
    }

    private static WatchedFileProbe ProbeDirectory(string path)
    {
        var canonical = Path.GetFullPath(path);
        var info = new DirectoryInfo(canonical);
        info.Refresh();
        var attributes = File.GetAttributes(canonical);
        return new WatchedFileProbe(
            canonical,
            true,
            0,
            ToUtc(info.CreationTimeUtc),
            ToUtc(info.LastWriteTimeUtc),
            attributes,
            $"directory:{NormalizeIdentity(canonical)}");
    }

    private string ReadStableFileId(string path, DateTimeOffset creationTimeUtc, long length)
    {
        var identity = _fileIdentityProvider.Capture(path);
        if (identity.Identity is not null)
        {
            return identity.Identity;
        }

        return $"portable:{creationTimeUtc.UtcTicks:x16}:{length:x16}";
    }

    private static IReadOnlyList<WatchedFileProbe> DisambiguateFallbackIdentities(
        IReadOnlyList<WatchedFileProbe> values)
    {
        var collisions = values
            .Where(value => !value.IsDirectory)
            .GroupBy(value => value.StableId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (collisions.Count == 0)
        {
            return values;
        }

        return values.Select(value =>
        {
            if (!collisions.Contains(value.StableId))
            {
                return value;
            }

            var suffix = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeIdentity(value.FullPath))))
                .ToLowerInvariant()[..16];
            return value with { StableId = $"{value.StableId}:collision:{suffix}" };
        }).ToArray();
    }

    private static string NormalizeIdentity(string path) =>
        PlatformServices.CurrentPathSemantics.IsCaseSensitive ? path : path.ToLowerInvariant();

    private static DateTimeOffset ToUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static bool IsRecoverable(Exception exception) => exception is
        IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException;

}

public sealed class FileStabilityChecker : IFileStabilityChecker
{
    private readonly IWatchedFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;

    public FileStabilityChecker(IWatchedFileSystem fileSystem, TimeProvider? timeProvider = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<FileStabilityResult> WaitForStableAsync(
        string path,
        TimeSpan observationPeriod,
        int maximumAttempts,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (observationPeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(observationPeriod));
        }

        if (maximumAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }

        WatchedFileProbe? previous = null;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await _fileSystem.ProbeAsync(path, cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                return new FileStabilityResult(false, attempt, "The file is no longer available.", null);
            }

            if (current.IsDirectory)
            {
                return new FileStabilityResult(true, attempt, "The directory state is available.", current);
            }

            if (previous is not null &&
                previous.SizeInBytes == current.SizeInBytes &&
                previous.LastWriteTimeUtc == current.LastWriteTimeUtc)
            {
                try
                {
                    using var stream = new FileStream(
                        current.FullPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete,
                        1,
                        FileOptions.SequentialScan);
                    return new FileStabilityResult(true, attempt, "The file remained stable across two observations.", current);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Retry while another process completes its write or lock.
                }
            }

            previous = current;
            if (attempt < maximumAttempts)
            {
                await Task.Delay(observationPeriod, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        return new FileStabilityResult(
            false,
            maximumAttempts,
            "The file is still changing or temporarily locked and will be retried.",
            previous);
    }
}
