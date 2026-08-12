using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.State;

namespace OpenSorSe.Core.Lifecycle;

/// <summary>
/// Coordinates configuration, logging, and lifecycle state for the shared application foundation.
/// </summary>
public sealed class ApplicationHost : IApplicationHost
{
    private readonly IConfigurationService _configurationService;
    private readonly ILoggingService _loggingService;
    private readonly IApplicationState _applicationState;

    /// <summary>
    /// Initializes an application host.
    /// </summary>
    /// <param name="configurationService">The configuration service to initialize first.</param>
    /// <param name="loggingService">The centralized logging service.</param>
    /// <param name="applicationState">The lifecycle state store.</param>
    public ApplicationHost(
        IConfigurationService configurationService,
        ILoggingService loggingService,
        IApplicationState applicationState)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        _applicationState = applicationState ?? throw new ArgumentNullException(nameof(applicationState));
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!_applicationState.TryTransitionTo(ApplicationLifecycleState.Initializing))
        {
            throw new InvalidOperationException("The application cannot be initialized from its current lifecycle state.");
        }

        try
        {
            await _configurationService.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var loggingSettings = _configurationService.Current.Logging;
            _loggingService.Initialize(new LoggingOptions(
                loggingSettings.MinimumLevel,
                loggingSettings.FileLoggingEnabled,
                loggingSettings.LogDirectoryPath,
                loggingSettings.RetainedFileCount));
            _loggingService.CreateLogger(nameof(ApplicationHost)).LogInformation("OmniSorSe Core is initializing.");

            if (!_applicationState.TryTransitionTo(ApplicationLifecycleState.Running))
            {
                throw new InvalidOperationException("The application could not enter the running state.");
            }
        }
        catch
        {
            _applicationState.TryTransitionTo(ApplicationLifecycleState.Stopped);
            throw;
        }
    }

    /// <inheritdoc />
    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_applicationState.LifecycleState == ApplicationLifecycleState.Stopped)
        {
            return Task.CompletedTask;
        }

        if (!_applicationState.TryTransitionTo(ApplicationLifecycleState.ShuttingDown))
        {
            throw new InvalidOperationException("The application cannot be shut down from its current lifecycle state.");
        }

        _loggingService.CreateLogger(nameof(ApplicationHost)).LogInformation("OmniSorSe Core is shutting down.");
        _applicationState.TryTransitionTo(ApplicationLifecycleState.Stopped);
        _loggingService.Dispose();
        return Task.CompletedTask;
    }
}
