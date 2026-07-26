using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenSorSe.Core.Logging;

/// <summary>
/// Provides a centrally configured debug logging factory for application modules.
/// </summary>
public sealed class LoggingService : ILoggingService
{
    private readonly object _syncRoot = new();
    private readonly DiagnosticEventBuffer _eventBuffer = new();
    private readonly LoggingStatisticsCounter _statistics = new();
    private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
    private bool _isDisposed;

    /// <inheritdoc />
    public void Initialize(LogLevel minimumLevel)
    {
        Initialize(LoggingOptions.Default with { MinimumLevel = minimumLevel });
    }

    /// <inheritdoc />
    public void Initialize(LoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        lock (_syncRoot)
        {
            ThrowIfDisposed();
            var previousFactory = _loggerFactory;
            var localFileProvider = new LocalFileLoggerProvider(options, _statistics, _eventBuffer);
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(options.MinimumLevel);
                builder.AddProvider(localFileProvider);
            });

            if (previousFactory is not NullLoggerFactory)
            {
                previousFactory.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);

        lock (_syncRoot)
        {
            ThrowIfDisposed();
            return _loggerFactory.CreateLogger(categoryName);
        }
    }

    /// <inheritdoc />
    public LoggingStatistics GetStatistics()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            return _statistics.Snapshot();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DiagnosticEvent> GetRecentEvents()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            return _eventBuffer.SnapshotNewestFirst();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _loggerFactory.Dispose();
            _isDisposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
