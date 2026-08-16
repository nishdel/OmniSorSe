using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Persistence;

namespace OpenSorSe.Core.Configuration;

/// <summary>
/// Loads JSON user settings over safe defaults and applies environment overrides.
/// </summary>
public sealed class JsonConfigurationService : IConfigurationService
{
    private const string LoggingLevelEnvironmentVariable = "OPENSORSE_LOGGING__MINIMUMLEVEL";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Func<string, string?> _environmentVariableReader;
    private readonly ApplicationFileAccessCoordinator _fileAccess;
    private readonly string _settingsFilePath;

    /// <summary>
    /// Initializes a configuration service for a user settings file.
    /// </summary>
    /// <param name="settingsFilePath">The absolute path of the user settings file.</param>
    /// <param name="environmentVariableReader">An optional environment variable reader for testing.</param>
    public JsonConfigurationService(
        string settingsFilePath,
        Func<string, string?>? environmentVariableReader = null)
    {
        if (string.IsNullOrWhiteSpace(settingsFilePath) || !Path.IsPathRooted(settingsFilePath))
        {
            throw new ArgumentException("An absolute settings file path is required.", nameof(settingsFilePath));
        }

        _settingsFilePath = settingsFilePath;
        _fileAccess = new ApplicationFileAccessCoordinator(settingsFilePath);
        _environmentVariableReader = environmentVariableReader ?? Environment.GetEnvironmentVariable;
    }

    /// <inheritdoc />
    public ApplicationSettings Current { get; private set; } = new();

    /// <inheritdoc />
    public string? InitializationWarning { get; private set; }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var access = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        InitializationWarning = null;
        var settings = new ApplicationSettings();

        if (File.Exists(_settingsFilePath))
        {
            try
            {
                if (new FileInfo(_settingsFilePath).Length > ConfigurationLimits.MaximumSettingsFileBytes)
                {
                    throw new ConfigurationValidationException("The configuration file exceeds its supported size.");
                }

                await using var stream = File.OpenRead(_settingsFilePath);
                settings = await JsonSerializer.DeserializeAsync<ApplicationSettings>(
                    stream,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false) ?? new ApplicationSettings();
                settings = UpgradeLegacyDiagnostics(settings);
                settings.Validate();
            }
            catch (Exception exception) when (exception is JsonException or ConfigurationValidationException or IOException or UnauthorizedAccessException)
            {
                settings = new ApplicationSettings();
                InitializationWarning = "The existing OmniSorSe settings file could not be loaded and was preserved. Safe defaults are active; save Settings to replace the invalid owned file.";
            }
        }

        Current = ApplyEnvironmentOverrides(settings);
        Current.Validate();
    }

    /// <inheritdoc />
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        using var access = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await SaveCoreAsync(Current, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        using var access = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await SaveCoreAsync(settings, cancellationToken).ConfigureAwait(false);
        Current = settings;
        InitializationWarning = null;
    }

    private async Task SaveCoreAsync(ApplicationSettings settings, CancellationToken cancellationToken)
    {
        await AtomicJsonFile.WriteAsync(
            _settingsFilePath,
            settings,
            SerializerOptions,
            ConfigurationLimits.MaximumSettingsFileBytes,
            cancellationToken,
            static (_, _) => new ConfigurationValidationException(
                "The configuration file exceeds its supported size.")).ConfigureAwait(false);
    }

    private ApplicationSettings ApplyEnvironmentOverrides(ApplicationSettings settings)
    {
        var configuredLevel = _environmentVariableReader(LoggingLevelEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredLevel))
        {
            return settings;
        }

        if (!Enum.TryParse<LogLevel>(configuredLevel, true, out var minimumLevel))
        {
            throw new ConfigurationValidationException(
                $"{LoggingLevelEnvironmentVariable} must be a valid logging level.");
        }

        return settings.WithLogging(new LoggingSettings
        {
            MinimumLevel = minimumLevel,
            FileLoggingEnabled = settings.Logging.FileLoggingEnabled,
            LogDirectoryPath = settings.Logging.LogDirectoryPath,
            RetainedFileCount = settings.Logging.RetainedFileCount,
        });
    }

    private static ApplicationSettings UpgradeLegacyDiagnostics(ApplicationSettings settings)
    {
        var diagnostics = settings.Diagnostics ?? new DiagnosticsSettings();
        if (!settings.Ai.RequestDiagnosticsEnabled ||
            diagnostics.EnableDiagnostics ||
            diagnostics.AiDiagnostics)
        {
            if (settings.Diagnostics is not null)
            {
                return settings;
            }

            return settings.WithDiagnostics(diagnostics);
        }

        return settings.WithDiagnostics(new DiagnosticsSettings
        {
            EnableDiagnostics = true,
            AiDiagnostics = true,
            ShowUnredactedDiagnosticContent = settings.Ai.ShowUnredactedDiagnosticContent,
        });
    }
}
