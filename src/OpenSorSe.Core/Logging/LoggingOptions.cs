using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Core.Logging;

/// <summary>
/// Defines validated local-output options for the centralized logging service.
/// </summary>
/// <param name="MinimumLevel">The lowest severity emitted to configured log providers.</param>
/// <param name="FileLoggingEnabled">Whether the local daily file sink is enabled.</param>
/// <param name="LogDirectoryPath">The optional absolute directory for daily log files.</param>
/// <param name="RetainedFileCount">The number of daily log files to retain.</param>
public sealed record LoggingOptions(
    LogLevel MinimumLevel,
    bool FileLoggingEnabled = true,
    string? LogDirectoryPath = null,
    int RetainedFileCount = 7)
{
    /// <summary>
    /// Gets the conservative default local logging options.
    /// </summary>
    public static LoggingOptions Default { get; } = new(LogLevel.Information);

    /// <summary>
    /// Gets the resolved absolute directory for daily log files.
    /// </summary>
    public string ResolvedLogDirectoryPath => LogDirectoryPath ?? GetDefaultLogDirectoryPath();

    /// <summary>
    /// Validates the local logging contract before replacing configured providers.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when an option is invalid.</exception>
    public void Validate()
    {
        if (!Enum.IsDefined(MinimumLevel))
        {
            throw new ArgumentException("The minimum logging level is unsupported.", nameof(MinimumLevel));
        }

        if (RetainedFileCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(RetainedFileCount), "At least one log file must be retained.");
        }

        if (LogDirectoryPath is not null && string.IsNullOrWhiteSpace(LogDirectoryPath))
        {
            throw new ArgumentException("The log directory path cannot be empty when supplied.", nameof(LogDirectoryPath));
        }

        if (!Path.IsPathRooted(ResolvedLogDirectoryPath))
        {
            throw new ArgumentException("The log directory path must be absolute.", nameof(LogDirectoryPath));
        }
    }

    private static string GetDefaultLogDirectoryPath() =>
        new ApplicationPathProvider().Paths.DiagnosticsDirectory;
}
