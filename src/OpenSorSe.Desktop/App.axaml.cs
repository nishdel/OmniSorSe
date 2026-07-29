using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using OpenSorSe.Core.DependencyInjection;
using OpenSorSe.Core.Lifecycle;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Desktop.Views;
using OpenSorSe.Scanner;
using OpenSorSe.Rules;
using OpenSorSe.Application;
using OpenSorSe.Application.AI;
using OpenSorSe.Application.Catalog;
using OpenSorSe.Application.CatalogComparison;
using OpenSorSe.Application.CatalogSearch;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.ChangePlans;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.Structure;
using OpenSorSe.Application.Watching;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Application.Plugins;
using OpenSorSe.AI;
using OpenSorSe.Desktop.Services;
using OpenSorSe.Core.Diagnostics;
using OpenSorSe.Core.Platform;
using OpenSorSe.Executor;
using OpenSorSe.Indexing.Sqlite;

namespace OpenSorSe.Desktop;

/// <summary>
/// Provides the Avalonia application entry point and desktop lifetime configuration.
/// </summary>
/// <remarks>
/// This is the production composition root. Startup deliberately completes
/// Core initialization and interrupted-operation inspection before activating
/// plugins, then initializes workflows before Watched Folders so an exact
/// plugin/profile dependency cannot race the watcher. Feature behavior belongs
/// in Application/domain services rather than this registration method.
/// </remarks>
public partial class App : Avalonia.Application
{
    private ServiceProvider? _serviceProvider;
    private IApplicationHost? _applicationHost;

    /// <summary>
    /// Loads the application's XAML resources.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Creates the initial desktop window after Avalonia has initialized the framework.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _serviceProvider = CreateServiceProvider();
            _applicationHost = _serviceProvider.GetRequiredService<IApplicationHost>();
            _applicationHost.InitializeAsync().GetAwaiter().GetResult();
            _serviceProvider.GetRequiredService<IDiagnosticsCollector>().Configure(
                _serviceProvider.GetRequiredService<OpenSorSe.Core.Configuration.IConfigurationService>()
                    .Current.Diagnostics);
            _serviceProvider.GetRequiredService<IChangePlanExecutionService>()
                .RecoverInterruptedAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            _serviceProvider.GetRequiredService<IPluginManager>()
                .InitializeAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            _serviceProvider.GetRequiredService<IWorkflowLibraryService>()
                .InitializeAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            _serviceProvider.GetRequiredService<IWatchedFolderCoordinator>()
                .InitializeAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            _serviceProvider.GetRequiredService<IBackgroundIndexingService>()
                .InitializeAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            _ = _serviceProvider.GetRequiredService<AdvancedDiagnosticsWindowCoordinator>();
            var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow(mainViewModel);
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var applicationPaths = new ApplicationPathProvider();
        applicationPaths.EnsureOwnedDirectories();
        var settingsPath = applicationPaths.SettingsFilePath;
        var paths = applicationPaths.Paths;
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationPathProvider>(applicationPaths);
        services.AddOpenSorSeCore(new OpenSorSeCoreOptions { ConfigurationFilePath = settingsPath });
        var pluginRoot = paths.PluginDirectory;
        services.AddSingleton<IFileScanner, FileScanner>();
        services.AddSingleton<IFileMetadataReader, FileMetadataReader>();
        services.AddSingleton<IFileHasher, FileHasher>();
        services.AddSingleton<IFileClassifier, FileClassifier>();
        services.AddSingleton<IDuplicateDetector, DuplicateDetector>();
        services.AddSingleton<IRuleEngine, RuleEngine>();
        services.AddSingleton<IActionPlanner, ActionPlanner>();
        services.AddSingleton<IConflictResolver, ConflictResolver>();
        services.AddSingleton<IFileSystemGateway, PhysicalFileSystemGateway>();
        services.AddSingleton<IChangePlanValidator, ChangePlanValidator>();
        services.AddSingleton<IChangePlanStore>(serviceProvider =>
        {
            return new JsonChangePlanStore(
                Path.Combine(paths.StateDirectory, "change-plans.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<IOperationJournalStore>(serviceProvider =>
        {
            return new JsonOperationJournalStore(
                Path.Combine(paths.StateDirectory, "operation-journal.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<IChangePlanFactory, ChangePlanFactory>();
        services.AddSingleton<IChangePlanExecutionService, ChangePlanExecutionService>();
        services.AddSingleton<IOperationReportExporter, OperationReportExporter>();
        services.AddSingleton<ISuggestionChangePlanFactory, SuggestionChangePlanFactory>();
        services.AddSingleton<IProcessingOrchestrator, ProcessingOrchestrator>();
        services.AddSingleton<IProcessingSessionManager, ProcessingSessionManager>();
        services.AddSingleton<IApplicationController, ApplicationController>();
        services.AddSingleton<IResultsSnapshotProjector, ResultsSnapshotProjector>();
        services.AddSingleton<IMetadataExtractor, FilesystemMetadataExtractor>();
        services.AddSingleton<IMetadataExtractor, PdfMetadataExtractor>();
        services.AddSingleton<IMetadataExtractor, OpenXmlMetadataExtractor>();
        services.AddSingleton<IMetadataExtractor, ImageMetadataExtractor>();
        services.AddSingleton<IMetadataExtractionPipeline, MetadataExtractionPipeline>();
        services.AddSingleton<IPdfPageRasterizer, PdfPageRasterizer>();
        services.AddSingleton<ITesseractProcessRunner, TesseractProcessRunner>();
        services.AddSingleton<IOcrEngine, TesseractCliOcrEngine>();
        services.AddSingleton<IOcrService, OcrService>();
        services.AddSingleton<IContentStore>(serviceProvider =>
        {
            return new JsonContentStore(
                Path.Combine(paths.CacheDirectory, "content-index.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<IContentIndexingService, ContentIndexingService>();
        services.AddSingleton<IEmbeddingProvider, FeatureHashingEmbeddingProvider>();
        services.AddSingleton<IDeepIndexStore>(serviceProvider =>
        {
            return new SqliteDeepIndexStore(
                Path.Combine(paths.DataDirectory, "index", "deep-index.db"),
                serviceProvider.GetRequiredService<IPathSemantics>());
        });
        services.AddSingleton<IIndexFileDiscovery, PhysicalIndexFileDiscovery>();
        services.AddSingleton<IBackgroundResourceMonitor, PortableBackgroundResourceMonitor>();
        services.AddSingleton<IIndexingStageProcessor, DefaultIndexingStageProcessor>();
        services.AddSingleton<IBackgroundIndexingService, BackgroundIndexingService>();
        services.AddSingleton<IProgressiveSearchSource>(serviceProvider =>
            serviceProvider.GetRequiredService<IBackgroundIndexingService>());
        services.AddSingleton<ISemanticIndexStore>(serviceProvider =>
        {
            return new JsonSemanticIndexStore(
                Path.Combine(paths.CacheDirectory, "semantic-index.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<ISemanticIndexer, SemanticIndexer>();
        services.AddSingleton<ISemanticSearchService, SemanticSearchService>();
        services.AddSingleton<IFolderStructureSnapshotService, FolderStructureSnapshotService>();
        services.AddSingleton<IStructureComparisonService, StructureComparisonService>();
        services.AddSingleton<IStructureHistoryStore>(serviceProvider =>
        {
            return new JsonStructureHistoryStore(
                Path.Combine(paths.DataDirectory, "structure-history.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<IFolderRestructuringService, FolderRestructuringService>();
        services.AddSingleton<ICatalogComparisonService, CatalogComparisonService>();
        services.AddSingleton<IResultsCatalogStore>(serviceProvider =>
        {
            return new JsonResultsCatalogStore(
                Path.Combine(paths.DataDirectory, "catalog.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<WatchedFolderPathPolicy>();
        services.AddSingleton<IWatchedFolderConfigurationStore>(serviceProvider =>
        {
            return new JsonWatchedFolderConfigurationStore(
                Path.Combine(paths.ConfigurationDirectory, "watched-folders.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<IWatchedFolderCatalogueStore>(serviceProvider =>
        {
            return new JsonWatchedFolderCatalogueStore(
                Path.Combine(paths.DataDirectory, "watched-catalogues.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<IWatchedActivityStore>(serviceProvider =>
        {
            return new JsonWatchedActivityStore(
                Path.Combine(paths.StateDirectory, "watched-activity.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<IWorkflowTemplateEngine, WorkflowTemplateEngine>();
        services.AddSingleton<IWorkflowValidator, WorkflowValidator>();
        services.AddSingleton<IWorkflowLibraryStore>(serviceProvider =>
        {
            return new JsonWorkflowLibraryStore(
                Path.Combine(paths.DataDirectory, "workflow-library.json"),
                serviceProvider.GetRequiredService<IWorkflowValidator>(),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<IWorkflowUsageInspector, WatchedWorkflowUsageInspector>();
        services.AddSingleton<IWorkflowLibraryService, WorkflowLibraryService>();
        services.AddSingleton<IWorkflowConfigurationResolver, WorkflowConfigurationResolver>();
        services.AddSingleton<IWorkflowImportExportService, WorkflowImportExportService>();
        services.AddSingleton<IWorkflowRecipePlanService, WorkflowRecipePlanService>();
        services.AddSingleton<WorkflowSortingRecipeResolver>();
        services.AddSingleton<IWatchedFolderManager, WatchedFolderManager>();
        services.AddSingleton<IWatchedFileSystem, PhysicalWatchedFileSystem>();
        services.AddSingleton<IFileStabilityChecker, FileStabilityChecker>();
        services.AddSingleton<IWatchedFolderEventSourceFactory, FileSystemWatcherEventSourceFactory>();
        services.AddSingleton<SessionWatchedSortingRecipeResolver>();
        services.AddSingleton<IWatchedSortingRecipeResolver>(serviceProvider =>
            serviceProvider.GetRequiredService<WorkflowSortingRecipeResolver>());
        services.AddSingleton<IWatchedSuggestionService, WatchedSuggestionService>();
        services.AddSingleton<IWatchedExecutionCorrelation, OperationJournalWatchedExecutionCorrelation>();
        services.AddSingleton<IWatchedFolderProcessor, WatchedFolderProcessor>();
        services.AddSingleton<IWatchedFolderCoordinator, WatchedFolderCoordinator>();
        foreach (var definition in BuiltInPluginCatalog.Definitions)
        {
            services.AddSingleton(definition);
        }
        services.AddSingleton<IPluginDiagnostics, PluginDiagnostics>();
        services.AddSingleton<IPluginManifestParser, PluginManifestParser>();
        services.AddSingleton<IPluginIntegrityService, PluginIntegrityService>();
        services.AddSingleton<IPluginStateStore>(
            new JsonPluginStateStore(Path.Combine(paths.StateDirectory, "plugins-state.json")));
        services.AddSingleton<IPluginDependencyResolver, PluginDependencyResolver>();
        services.AddSingleton<IPluginContributionRegistry, PluginContributionRegistry>();
        services.AddSingleton<IPluginContributionResolver, PluginContributionResolver>();
        services.AddSingleton<IPluginExtensionHost, PluginExtensionHost>();
        services.AddSingleton<IPluginRecipeFieldService, PluginRecipeFieldService>();
        services.AddSingleton<IPluginUsageInspector, PluginUsageInspector>();
        services.AddSingleton<IPluginDiscoveryService>(serviceProvider =>
            new PluginDiscoveryService(
                pluginRoot,
                serviceProvider.GetRequiredService<IPluginManifestParser>(),
                serviceProvider.GetRequiredService<IPluginStateStore>(),
                serviceProvider.GetRequiredService<IPluginIntegrityService>(),
                serviceProvider.GetRequiredService<IPluginDependencyResolver>(),
                serviceProvider.GetRequiredService<IPluginDiagnostics>(),
                serviceProvider.GetServices<BuiltInPluginDefinition>()));
        services.AddSingleton<IPluginPackageService>(serviceProvider =>
            new PluginPackageService(
                pluginRoot,
                serviceProvider.GetRequiredService<IPluginManifestParser>(),
                serviceProvider.GetRequiredService<IPluginIntegrityService>(),
                serviceProvider.GetRequiredService<IPluginUsageInspector>(),
                serviceProvider.GetRequiredService<IPluginDiagnostics>()));
        services.AddSingleton<IPluginRuntime>(serviceProvider =>
            new PluginRuntime(
                serviceProvider.GetRequiredService<IPluginContributionRegistry>(),
                serviceProvider.GetRequiredService<IPluginDiagnostics>(),
                serviceProvider.GetServices<BuiltInPluginDefinition>()));
        services.AddSingleton<IPluginManager>(serviceProvider =>
            new PluginManager(
                pluginRoot,
                serviceProvider.GetRequiredService<IPluginDiscoveryService>(),
                serviceProvider.GetRequiredService<IPluginStateStore>(),
                serviceProvider.GetRequiredService<IPluginIntegrityService>(),
                serviceProvider.GetRequiredService<IPluginRuntime>(),
                serviceProvider.GetRequiredService<IPluginPackageService>(),
                serviceProvider.GetRequiredService<IPluginDiagnostics>(),
                serviceProvider.GetRequiredService<IPluginUsageInspector>()));
        services.AddSingleton<ISavedCatalogSearchStore>(serviceProvider =>
        {
            return new JsonSavedCatalogSearchStore(
                Path.Combine(paths.DataDirectory, "saved-catalog-searches.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton(new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
        services.AddSingleton<IAiSuggestionProvider, OllamaSuggestionProvider>();
        services.AddSingleton<IAiPromptBuilder, AiPromptBuilder>();
        services.AddSingleton<IAiResponseParser, AiResponseParser>();
        services.AddSingleton<IAiRequestDiagnosticsStore, AiRequestDiagnosticsStore>();
        services.AddSingleton<IAiDiagnosticsCollector, AiDiagnosticsCollector>();
        services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
        services.AddSingleton<IDesktopIntegration>(_ => DesktopIntegrationFactory.Create());
        services.AddSingleton<IExternalFileLauncher, ExternalFileLauncher>();
        services.AddSingleton<AdvancedDiagnosticsWindowCoordinator>();
        services.AddSingleton<IAdvancedDiagnosticsWindowService>(serviceProvider =>
            serviceProvider.GetRequiredService<AdvancedDiagnosticsWindowCoordinator>());
        services.AddSingleton<IDecisionHistoryStore>(serviceProvider =>
        {
            return new JsonDecisionHistoryStore(
                Path.Combine(paths.DataDirectory, "decision-history.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<IAiSuggestionService, AiSuggestionService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs eventArgs)
    {
        _serviceProvider?.GetService<IWatchedFolderCoordinator>()?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _serviceProvider?.GetService<IBackgroundIndexingService>()?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _serviceProvider?.GetService<IPluginManager>()?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _serviceProvider?.GetService<IDiagnosticsCollector>()?.ClearAll();
        _applicationHost?.ShutdownAsync().GetAwaiter().GetResult();
        _serviceProvider?.Dispose();
    }
}
