using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;
using OpenSorSe.Core.Errors;
using OpenSorSe.Core.Events;
using OpenSorSe.Core.Lifecycle;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Platform;
using OpenSorSe.Core.State;
using OpenSorSe.Core.Tasks;

namespace OpenSorSe.Core.DependencyInjection;

/// <summary>
/// Registers the shared Core infrastructure with the .NET dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the documented shared Core foundation as singleton services.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="options">The composition settings for Core infrastructure.</param>
    /// <returns>The same collection to support fluent composition.</returns>
    public static IServiceCollection AddOpenSorSeCore(
        this IServiceCollection services,
        OpenSorSeCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConfigurationFilePath);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(IApplicationHost)))
        {
            throw new InvalidOperationException("OmniSorSe Core services have already been registered.");
        }

        services.AddSingleton(options);
        services.TryAddSingleton<IPathSemantics>(PlatformServices.CurrentPathSemantics);
        services.TryAddSingleton<IApplicationPathProvider, ApplicationPathProvider>();
        services.TryAddSingleton<IFileIdentityProvider>(_ => FileIdentityProviderFactory.CreateCurrent());
        services.TryAddSingleton<IFileSystemCapabilities, FileSystemCapabilities>();
        services.TryAddSingleton<IExternalToolLocator, ExternalToolLocator>();
        services.TryAddSingleton<IPlatformCapabilityProvider, PlatformCapabilityProvider>();
        services.AddSingleton<IConfigurationService>(serviceProvider =>
            new JsonConfigurationService(
                serviceProvider.GetRequiredService<OpenSorSeCoreOptions>().ConfigurationFilePath));
        services.AddSingleton<IDiagnosticsRedactor, DiagnosticsRedactor>();
        services.AddSingleton<InMemoryDiagnosticsCollector>();
        services.AddSingleton<IDiagnosticsCollector>(serviceProvider =>
            serviceProvider.GetRequiredService<InMemoryDiagnosticsCollector>());
        services.AddSingleton<IDiagnosticsStore>(serviceProvider =>
            serviceProvider.GetRequiredService<InMemoryDiagnosticsCollector>());
        services.AddSingleton<IDiagnosticsEventSink>(serviceProvider =>
            serviceProvider.GetRequiredService<InMemoryDiagnosticsCollector>());
        services.AddSingleton<IDiagnosticsExportService, DiagnosticsExportService>();
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<IApplicationState, ApplicationState>();
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<IErrorHandler, ErrorHandler>();
        services.AddSingleton<ITaskManager, TaskManager>();
        services.AddSingleton<IApplicationHost, ApplicationHost>();
        services.AddSingleton<IServiceRegistry, ServiceRegistry>();
        return services;
    }
}
