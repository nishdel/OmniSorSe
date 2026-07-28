#pragma warning disable CS1591

using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application.Watching;

public sealed class FileSystemWatcherEventSourceFactory : IWatchedFolderEventSourceFactory
{
    private readonly ILoggingService _loggingService;
    private readonly TimeProvider _timeProvider;

    public FileSystemWatcherEventSourceFactory(
        ILoggingService loggingService,
        TimeProvider? timeProvider = null)
    {
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IWatchedFolderEventSource Create(WatchedFolderConfiguration configuration) =>
        new FileSystemWatcherEventSource(configuration, _loggingService, _timeProvider);
}

public sealed class FileSystemWatcherEventSource : IWatchedFolderEventSource
{
    private readonly FileSystemWatcher _watcher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private bool _disposed;

    public FileSystemWatcherEventSource(
        WatchedFolderConfiguration configuration,
        ILoggingService loggingService,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ConfigurationId = configuration.Id;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(FileSystemWatcherEventSource));
        _watcher = new FileSystemWatcher(configuration.FolderPath)
        {
            IncludeSubdirectories = configuration.IncludeSubfolders,
            NotifyFilter = NotifyFilters.FileName |
                           NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite |
                           NotifyFilters.Size |
                           NotifyFilters.CreationTime |
                           NotifyFilters.Attributes,
            InternalBufferSize = 32 * 1024,
            EnableRaisingEvents = false,
        };
        _watcher.Created += OnCreated;
        _watcher.Changed += OnChanged;
        _watcher.Deleted += OnDeleted;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;
    }

    public event EventHandler<WatchedFolderHint>? HintReceived;
    public event EventHandler<Exception>? Error;
    public string ConfigurationId { get; }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _watcher.EnableRaisingEvents = true;
        _logger.LogInformation("Registered operating-system watcher for configuration {ConfigurationId}.", ConfigurationId);
    }

    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnCreated;
        _watcher.Changed -= OnChanged;
        _watcher.Deleted -= OnDeleted;
        _watcher.Renamed -= OnRenamed;
        _watcher.Error -= OnError;
        _watcher.Dispose();
        _logger.LogInformation("Disposed operating-system watcher for configuration {ConfigurationId}.", ConfigurationId);
    }

    private void OnCreated(object sender, FileSystemEventArgs eventArgs) =>
        Publish(eventArgs.FullPath, null, IsDirectory(eventArgs.FullPath)
            ? WatchedPathChangeKind.DirectoryCreated
            : WatchedPathChangeKind.FileCreated);

    private void OnChanged(object sender, FileSystemEventArgs eventArgs) =>
        Publish(eventArgs.FullPath, null, IsDirectory(eventArgs.FullPath)
            ? WatchedPathChangeKind.Unknown
            : WatchedPathChangeKind.FileModified);

    private void OnDeleted(object sender, FileSystemEventArgs eventArgs) =>
        Publish(eventArgs.FullPath, null, WatchedPathChangeKind.Unknown);

    private void OnRenamed(object sender, RenamedEventArgs eventArgs)
    {
        var isDirectory = IsDirectory(eventArgs.FullPath);
        var oldParent = Path.GetDirectoryName(eventArgs.OldFullPath);
        var newParent = Path.GetDirectoryName(eventArgs.FullPath);
        var moved = !WatchedFolderPathPolicy.PathComparer.Equals(oldParent, newParent);
        Publish(
            eventArgs.FullPath,
            eventArgs.OldFullPath,
            isDirectory
                ? moved ? WatchedPathChangeKind.DirectoryMoved : WatchedPathChangeKind.DirectoryRenamed
                : moved ? WatchedPathChangeKind.FileMoved : WatchedPathChangeKind.FileRenamed,
            isDirectory);
    }

    private void OnError(object sender, ErrorEventArgs eventArgs)
    {
        var exception = eventArgs.GetException() ?? new IOException("The operating-system watcher reported an unknown error.");
        _logger.LogWarning(
            exception,
            "Operating-system watcher error for configuration {ConfigurationId}; reconciliation is required.",
            ConfigurationId);
        foreach (var handler in Error?.GetInvocationList()
                     .Cast<EventHandler<Exception>>()
                     .ToArray() ?? [])
        {
            try
            {
                handler(this, exception);
            }
            catch (Exception observerException)
            {
                _logger.LogWarning(
                    observerException,
                    "A watcher-error observer failed for configuration {ConfigurationId}.",
                    ConfigurationId);
            }
        }
    }

    private void Publish(
        string path,
        string? oldPath,
        WatchedPathChangeKind kind,
        bool? isDirectory = null)
    {
        var hint = new WatchedFolderHint(
            ConfigurationId,
            kind,
            path,
            oldPath,
            _timeProvider.GetUtcNow(),
            isDirectory ?? IsDirectory(path));
        foreach (var handler in HintReceived?.GetInvocationList()
                     .Cast<EventHandler<WatchedFolderHint>>()
                     .ToArray() ?? [])
        {
            try
            {
                handler(this, hint);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "A watcher-hint observer failed for configuration {ConfigurationId}.",
                    ConfigurationId);
            }
        }
    }

    private static bool IsDirectory(string path)
    {
        try
        {
            return Directory.Exists(path) ||
                   File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.Directory);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
