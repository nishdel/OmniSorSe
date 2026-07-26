using Microsoft.Extensions.Logging;

namespace OpenSorSe.Core.Logging;

/// <summary>
/// Configures centralized logging and creates categorized loggers for application modules.
/// </summary>
public interface ILoggingService : IDisposable
{
    /// <summary>
    /// Initializes logging with the supplied minimum severity.
    /// </summary>
    /// <param name="minimumLevel">The lowest severity to write to log outputs.</param>
    void Initialize(LogLevel minimumLevel);

    /// <summary>
    /// Initializes logging with explicit local-output options.
    /// </summary>
    /// <param name="options">The validated minimum level and local sink options.</param>
    void Initialize(LoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Initialize(options.MinimumLevel);
    }

    /// <summary>
    /// Creates a logger for the supplied category.
    /// </summary>
    /// <param name="categoryName">The subsystem or type name used to categorize entries.</param>
    /// <returns>A categorized logger.</returns>
    ILogger CreateLogger(string categoryName);

    /// <summary>
    /// Gets process-lifetime logging counters without retaining log entry payloads.
    /// </summary>
    /// <returns>A snapshot of accepted entries and file-sink failures.</returns>
    LoggingStatistics GetStatistics() => LoggingStatistics.Empty;

    /// <summary>
    /// Gets a newest-first snapshot of bounded process-session diagnostic events.
    /// </summary>
    /// <returns>Immutable events safe for ordinary diagnostic inspection.</returns>
    IReadOnlyList<DiagnosticEvent> GetRecentEvents() => Array.Empty<DiagnosticEvent>();
}
