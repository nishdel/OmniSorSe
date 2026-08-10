using Microsoft.Extensions.DependencyInjection;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Application.Plugins;
using OpenSorSe.Application.Watching;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;
using OpenSorSe.Core.Lifecycle;
using OpenSorSe.Core.Platform;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Executor;

namespace OpenSorSe.Desktop;

/// <summary>
/// Exercises production composition, persistence initialization, and bounded
/// shutdown without opening a window during native package validation.
/// </summary>
/// <remarks>
/// Release scripts must pass a dedicated absolute application-data directory.
/// The probe never reads or writes the user's ordinary OpenSorSe data. It proves
/// the packaged executable and native dependencies can start and stop; it is not
/// a substitute for interactive UI validation.
/// </remarks>
internal static class PackageSmokeTest
{
    /// <summary>Runs the native-package startup/shutdown probe.</summary>
    /// <returns>Zero on complete startup and cleanup; otherwise one.</returns>
    internal static async Task<int> RunAsync(string smokeDataRoot)
    {
        ServiceProvider? serviceProvider = null;
        IApplicationHost? applicationHost = null;
        IGraphBackgroundRuntime? graphRuntime = null;
        var graphStarted = false;
        var exitCode = 0;
        try
        {
            if (string.IsNullOrWhiteSpace(smokeDataRoot) || !Path.IsPathFullyQualified(smokeDataRoot))
            {
                throw new InvalidOperationException(
                    "Package smoke tests require an isolated absolute data root.");
            }

            Directory.CreateDirectory(smokeDataRoot);
            var applicationPaths = new ApplicationPathProvider(
                PlatformServices.CurrentPlatform,
                _ => null,
                smokeDataRoot,
                smokeDataRoot);
            serviceProvider = App.CreateServiceProviderForPaths(applicationPaths);
            applicationHost = serviceProvider.GetRequiredService<IApplicationHost>();
            await applicationHost.InitializeAsync().ConfigureAwait(false);
            serviceProvider.GetRequiredService<IDiagnosticsCollector>().Configure(
                serviceProvider.GetRequiredService<IConfigurationService>().Current.Diagnostics);
            await serviceProvider.GetRequiredService<IChangePlanExecutionService>()
                .RecoverInterruptedAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await serviceProvider.GetRequiredService<IPluginManager>()
                .InitializeAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await serviceProvider.GetRequiredService<IWorkflowLibraryService>()
                .InitializeAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await serviceProvider.GetRequiredService<IWatchedFolderCoordinator>()
                .InitializeAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await serviceProvider.GetRequiredService<IBackgroundIndexingService>()
                .InitializeAsync(CancellationToken.None)
                .ConfigureAwait(false);
            _ = serviceProvider.GetRequiredService<MainViewModel>();
            graphRuntime = serviceProvider.GetRequiredService<IGraphBackgroundRuntime>();
            await graphRuntime.StartAsync(CancellationToken.None).ConfigureAwait(false);
            graphStarted = true;
        }
        catch (Exception exception)
        {
            TraceFailure("startup", exception);
            exitCode = 1;
        }
        finally
        {
            if (graphStarted && graphRuntime is not null)
            {
                try
                {
                    using var shutdown = new CancellationTokenSource(GraphLimits.ShutdownGracePeriod);
                    await graphRuntime.StopAsync(GraphLimits.ShutdownGracePeriod, shutdown.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    TraceFailure("knowledge-graph-shutdown", exception);
                    exitCode = 1;
                }
            }

            if (applicationHost is not null)
            {
                try
                {
                    await applicationHost.ShutdownAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    TraceFailure("application-host-shutdown", exception);
                    exitCode = 1;
                }
            }

            if (serviceProvider is not null)
            {
                try
                {
                    await serviceProvider.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    TraceFailure("service-provider-disposal", exception);
                    exitCode = 1;
                }
            }
        }

        return exitCode;
    }

    private static void TraceFailure(string operation, Exception exception) =>
        System.Diagnostics.Trace.TraceError(
            "OpenSorSe package smoke test failed during {0} ({1}).",
            operation,
            exception.GetType().Name);
}
