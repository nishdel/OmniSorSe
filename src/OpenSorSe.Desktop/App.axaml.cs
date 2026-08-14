using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.Explorer;
using OpenSorSe.Application.ChangePlans;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Application.Media;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.SmartTags;
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
using OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

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
    private CancellationTokenSource? _graphStartupCancellation;
    private Task? _graphStartupTask;

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
            desktop.Exit += OnDesktopExit;
            Exception? startupFailure = null;
            if (!LifecycleOperationGuard.TryExecute(
                    "desktop-startup",
                    () => ConfigureDesktopLifetime(desktop),
                    (_, exception) =>
                    {
                        startupFailure = exception;
                        RecordLifecycleFailure("Desktop startup", exception);
                    }))
            {
                ReleaseFailedStartupServices();
                desktop.MainWindow = CreateStartupFailureWindow(startupFailure!);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureDesktopLifetime(IClassicDesktopStyleApplicationLifetime desktop)
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
        _serviceProvider.GetRequiredService<SqliteDeepIndexStore>()
            .InitializeAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        _serviceProvider.GetRequiredService<ISmartTagService>()
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
        StartKnowledgeGraphInBackground(_serviceProvider);
    }

    internal static ServiceProvider CreateServiceProvider() =>
        CreateServiceProviderForPaths(new ApplicationPathProvider());

    internal static ServiceProvider CreateServiceProviderForPaths(IApplicationPathProvider applicationPaths)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);
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
        services.AddSingleton<IMetadataExtractor, PlainTextMetadataExtractor>();
        services.AddSingleton<IMetadataExtractor, PdfMetadataExtractor>();
        services.AddSingleton<IMetadataExtractor, OpenXmlMetadataExtractor>();
        services.AddSingleton<IMetadataExtractor, ImageMetadataExtractor>();
        services.AddSingleton<IMetadataExtractionPipeline, MetadataExtractionPipeline>();
        services.AddSingleton<IPdfPageRasterizer, PdfPageRasterizer>();
        services.AddSingleton<ITesseractProcessRunner, TesseractProcessRunner>();
        services.AddSingleton<IOcrEngine, TesseractCliOcrEngine>();
        services.AddSingleton<IOcrService, OcrService>();
        services.AddSingleton<IMediaProcessRunner, ExternalMediaProcessRunner>();
        services.AddSingleton<IMediaMetadataProvider, ImageMediaMetadataProvider>();
        services.AddSingleton<IMediaMetadataProvider, FfprobeMediaMetadataProvider>();
        services.AddSingleton<IMediaTranscriptionProvider>(serviceProvider =>
            new WhisperCppTranscriptionProvider(
                serviceProvider.GetRequiredService<OpenSorSe.Core.Configuration.IConfigurationService>(),
                serviceProvider.GetRequiredService<IExternalToolLocator>(),
                serviceProvider.GetRequiredService<IMediaProcessRunner>(),
                Path.Combine(paths.CacheDirectory, "media-temporary", "transcription")));
        services.AddSingleton<IMediaVisualDescriptionProvider, UnavailableMediaVisualDescriptionProvider>();
        services.AddSingleton<IVideoFrameSampler>(serviceProvider =>
            new FfmpegVideoFrameSampler(
                serviceProvider.GetRequiredService<OpenSorSe.Core.Configuration.IConfigurationService>(),
                serviceProvider.GetRequiredService<IExternalToolLocator>(),
                serviceProvider.GetRequiredService<IMediaProcessRunner>(),
                Path.Combine(paths.CacheDirectory, "media-temporary")));
        services.AddSingleton<IMediaIntelligenceService, MediaIntelligenceService>();
        services.AddSingleton<IMediaThumbnailProvider>(serviceProvider =>
            new SkiaMediaThumbnailProvider(
                serviceProvider.GetRequiredService<OpenSorSe.Core.Configuration.IConfigurationService>(),
                Path.Combine(paths.CacheDirectory, "media-thumbnails")));
        services.AddSingleton<IContentStore>(serviceProvider =>
        {
            return new JsonContentStore(
                Path.Combine(paths.CacheDirectory, "content-index.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<IContentIndexingService, ContentIndexingService>();
        services.AddSingleton<IEmbeddingProvider, FeatureHashingEmbeddingProvider>();
        services.AddSingleton<IContentIntelligenceProvider, DeterministicContentIntelligenceProvider>();
        services.AddSingleton(_ => SmartTagTaxonomy.LoadBuiltIn());
        services.AddSingleton<ISmartTagClassifier, DeterministicSmartTagClassifier>();
        services.AddSingleton<SqliteDeepIndexStore>(serviceProvider =>
        {
            return new SqliteDeepIndexStore(
                Path.Combine(paths.DataDirectory, "index", "deep-index.db"),
                serviceProvider.GetRequiredService<IPathSemantics>());
        });
        services.AddSingleton<IDeepIndexStore>(serviceProvider =>
            serviceProvider.GetRequiredService<SqliteDeepIndexStore>());
        services.AddSingleton<IIndexPrivacyStore>(serviceProvider =>
            serviceProvider.GetRequiredService<SqliteDeepIndexStore>());
        services.AddSingleton<IRelationshipStore>(serviceProvider =>
            serviceProvider.GetRequiredService<SqliteDeepIndexStore>());
        services.AddSingleton<ISmartTagStore>(serviceProvider =>
            serviceProvider.GetRequiredService<SqliteDeepIndexStore>());
        services.AddSingleton<IRelationshipEngine, DeterministicRelationshipEngine>();
        services.AddSingleton<RelationshipService>();
        services.AddSingleton<IRelationshipService>(serviceProvider =>
            serviceProvider.GetRequiredService<RelationshipService>());
        services.AddSingleton<IRelationshipSearchSource>(serviceProvider =>
            serviceProvider.GetRequiredService<RelationshipService>());
        services.AddSingleton<IIndexFileDiscovery, PhysicalIndexFileDiscovery>();
        services.AddSingleton<IBackgroundResourceMonitor, PortableBackgroundResourceMonitor>();
        services.AddSingleton<IIndexingStageProcessor, DefaultIndexingStageProcessor>();
        services.AddSingleton<BackgroundIndexingService>();
        services.AddSingleton<IBackgroundIndexingService>(serviceProvider =>
            serviceProvider.GetRequiredService<BackgroundIndexingService>());
        services.AddSingleton<IIndexPrivacyService>(serviceProvider =>
            serviceProvider.GetRequiredService<BackgroundIndexingService>());
        services.AddSingleton<ISmartTagService, SmartTagService>();
        services.AddSingleton<IProgressiveSearchSource>(serviceProvider =>
            serviceProvider.GetRequiredService<IBackgroundIndexingService>());
        services.AddSingleton<IProgressiveSearchDocumentLookup>(serviceProvider =>
            serviceProvider.GetRequiredService<IBackgroundIndexingService>());
        services.AddSingleton<SqliteGraphProjectionSource>(serviceProvider =>
            new SqliteGraphProjectionSource(
                Path.Combine(paths.DataDirectory, "index", "deep-index.db"),
                serviceProvider.GetRequiredService<IPathSemantics>()));
        services.AddSingleton<IGraphProjectionSource>(serviceProvider =>
            serviceProvider.GetRequiredService<SqliteGraphProjectionSource>());
        services.AddSingleton<SqliteGraphStorageLifecycle>(_ =>
            new SqliteGraphStorageLifecycle(Path.Combine(paths.DataDirectory, "index")));
        services.AddSingleton<IGraphStorageLifecycle>(serviceProvider =>
            serviceProvider.GetRequiredService<SqliteGraphStorageLifecycle>());
        services.AddSingleton<IGraphDerivedStoreRecoveryProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<SqliteGraphStorageLifecycle>());
        services.AddSingleton<SqliteGraphStore>(serviceProvider =>
            new SqliteGraphStore(
                serviceProvider.GetRequiredService<SqliteGraphStorageLifecycle>().GraphDatabasePath));
        services.AddSingleton<IGraphStore>(serviceProvider =>
            serviceProvider.GetRequiredService<SqliteGraphStore>());
        services.AddSingleton<SqliteGraphDecisionStore>(serviceProvider =>
            new SqliteGraphDecisionStore(
                serviceProvider.GetRequiredService<SqliteGraphStorageLifecycle>().DecisionDatabasePath));
        services.AddSingleton<IGraphDecisionStore>(serviceProvider =>
            serviceProvider.GetRequiredService<SqliteGraphDecisionStore>());
        services.AddSingleton<IGraphIdentityResolver, ConservativeGraphIdentityResolver>();
        services.AddSingleton<IGraphStateValidator, GraphStateValidator>();
        services.AddSingleton<IGraphProjectionBuilder, DeterministicGraphProjectionBuilder>();
        services.AddSingleton<IGraphDecisionProjectionBuilder, DeterministicGraphDecisionProjectionBuilder>();
        services.AddSingleton<IGraphResourceAdmissionPolicy, GraphResourceAdmissionPolicy>();
        services.AddSingleton<GraphProjectionCoordinator>();
        services.AddSingleton<IGraphProjectionCoordinator>(serviceProvider =>
            serviceProvider.GetRequiredService<GraphProjectionCoordinator>());
        services.AddSingleton<GraphBackgroundRuntime>();
        services.AddSingleton<IGraphBackgroundRuntime>(serviceProvider =>
            serviceProvider.GetRequiredService<GraphBackgroundRuntime>());
        services.AddSingleton<IGraphReconciliationSignal>(serviceProvider =>
            serviceProvider.GetRequiredService<GraphBackgroundRuntime>());
        services.AddSingleton<IGraphQueryService, GraphQueryService>();
        services.AddSingleton<IGraphSearchSource, GraphSearchSource>();
        services.AddSingleton<IGraphPrivacyService, GraphPrivacyService>();
        services.AddSingleton<IGraphDecisionRecoveryService, GraphDecisionRecoveryService>();
        services.AddSingleton<IGraphDerivedStoreRecoveryService, GraphDerivedStoreRecoveryService>();
        services.AddSingleton<IGraphRepairService, GraphRepairService>();
        services.AddSingleton<IGraphDiagnosticsService, GraphDiagnosticsService>();
        services.AddSingleton<IGraphLegacyAuthorityBridge, RelationshipGraphAuthorityBridge>();
        services.AddSingleton<IGraphDecisionService, GraphDecisionService>();
        services.AddSingleton<ISemanticIndexStore>(serviceProvider =>
        {
            return new JsonSemanticIndexStore(
                Path.Combine(paths.CacheDirectory, "semantic-index.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<ISemanticIndexer, SemanticIndexer>();
        services.AddSingleton<ISearchQueryInterpreter, DeterministicSearchQueryInterpreter>();
        services.AddSingleton<ISearchSnippetFactory, SearchSnippetFactory>();
        services.AddSingleton<ISearchRanker, HybridSearchRanker>();
        services.AddSingleton<ISemanticSearchService, SemanticSearchService>();
        services.AddSingleton<IExplorerDataSource, ExplorerDataSource>();
        services.AddSingleton<IExplorerCompanionPresence, UnavailableExplorerCompanionPresence>();
        services.AddSingleton<IExplorerProtocolHost, NamedPipeExplorerProtocolHost>();
        services.AddSingleton<IExplorerCompanionLocator, ExplorerCompanionLocator>();
        services.AddSingleton<IExplorerCompanionLaunchService, ExplorerCompanionLaunchService>();
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
        services.AddSingleton<IAiSearchAssistant, AiSearchAssistant>();
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
        services.AddSingleton<KnowledgeGraphViewModel>();
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
        var graphStopped = false;
        _ = LifecycleOperationGuard.TryExecute(
            "knowledge-graph-shutdown",
            () => graphStopped = StopKnowledgeGraphSafely(),
            RecordLifecycleFailure);
        _ = LifecycleOperationGuard.TryExecute(
            "diagnostics-clear",
            () => _serviceProvider?.GetService<IDiagnosticsCollector>()?.ClearAll(),
            RecordLifecycleFailure);
        _ = LifecycleOperationGuard.TryExecute(
            "application-host-shutdown",
            () => _applicationHost?.ShutdownAsync().GetAwaiter().GetResult(),
            RecordLifecycleFailure);
        if (graphStopped)
        {
            _ = LifecycleOperationGuard.TryExecute(
                "service-provider-disposal",
                () => _serviceProvider?.DisposeAsync().AsTask().GetAwaiter().GetResult(),
                RecordLifecycleFailure);
        }
        else
        {
            TryGetLifecycleLogger()?.LogWarning(
                "Knowledge Graph initialization did not acknowledge bounded shutdown; process teardown will release remaining handles and durable recovery will fence unfinished work on the next startup.");
        }

        _serviceProvider = null;
        _applicationHost = null;
    }

    private void StartKnowledgeGraphInBackground(ServiceProvider serviceProvider)
    {
        _graphStartupCancellation = new CancellationTokenSource();
        _graphStartupTask = Task.Run(
            () => StartKnowledgeGraphSafelyAsync(serviceProvider, _graphStartupCancellation.Token),
            CancellationToken.None);
    }

    private bool StopKnowledgeGraphSafely()
    {
        _graphStartupCancellation?.Cancel();
        try
        {
            _graphStartupTask?
                .WaitAsync(GraphLimits.ShutdownGracePeriod)
                .GetAwaiter()
                .GetResult();
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (Exception exception)
        {
            RecordLifecycleFailure("Knowledge Graph initialization shutdown", exception);
            return false;
        }

        _graphStartupCancellation?.Dispose();
        _graphStartupCancellation = null;
        _graphStartupTask = null;

        if (_serviceProvider?.GetService<IGraphBackgroundRuntime>() is not { } graphRuntime)
        {
            return true;
        }

        using var shutdown = new CancellationTokenSource(GraphLimits.ShutdownGracePeriod);
        try
        {
            graphRuntime.StopAsync(GraphLimits.ShutdownGracePeriod, shutdown.Token)
                .GetAwaiter()
                .GetResult();
            return true;
        }
        catch (Exception exception)
        {
            RecordLifecycleFailure("Knowledge Graph runtime shutdown", exception);
            return false;
        }
    }

    private void ReleaseFailedStartupServices()
    {
        _ = LifecycleOperationGuard.TryExecute(
            "failed-startup-host-shutdown",
            () => _applicationHost?.ShutdownAsync().GetAwaiter().GetResult(),
            RecordLifecycleFailure);
        _ = LifecycleOperationGuard.TryExecute(
            "failed-startup-service-disposal",
            () => _serviceProvider?.DisposeAsync().AsTask().GetAwaiter().GetResult(),
            RecordLifecycleFailure);
        _applicationHost = null;
        _serviceProvider = null;
    }

    private static Window CreateStartupFailureWindow(Exception exception)
    {
        var category = exception.GetType().Name;
        var window = new Window
        {
            Title = "OmniSorSe could not start",
            Width = 560,
            Height = 300,
            MinWidth = 440,
            MinHeight = 240,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        var closeButton = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(closeButton, "Close OmniSorSe startup error");
        closeButton.Click += (_, _) => window.Close();
        window.Content = new StackPanel
        {
            Margin = new Thickness(28),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = "OmniSorSe could not complete startup.",
                    FontSize = 24,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = "Your original files were not changed. Restart the application. If the problem continues, include the local OmniSorSe logs when reporting the issue after reviewing them for private information.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = $"Failure category: {category}",
                    TextWrapping = TextWrapping.Wrap,
                },
                closeButton,
            },
        };
        AutomationProperties.SetName(window, "OmniSorSe startup error");
        return window;
    }

    private void RecordLifecycleFailure(string operation, Exception exception)
    {
        try
        {
            TryGetLifecycleLogger()?.LogError(
                exception,
                "{LifecycleOperation} failed safely in category {FailureCategory}.",
                operation,
                exception.GetType().Name);
        }
        catch (Exception loggingException)
        {
            System.Diagnostics.Trace.TraceError(
                "OmniSorSe lifecycle failure in {0} ({1}); local logging also failed ({2}).",
                operation,
                exception.GetType().Name,
                loggingException.GetType().Name);
        }
    }

    private ILogger? TryGetLifecycleLogger()
    {
        try
        {
            return _serviceProvider?
                .GetService<OpenSorSe.Core.Logging.ILoggingService>()?
                .CreateLogger(nameof(App));
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError(
                "OmniSorSe lifecycle logger was unavailable ({0}).",
                exception.GetType().Name);
            return null;
        }
    }

    private static async Task StartKnowledgeGraphSafelyAsync(
        ServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            await serviceProvider.GetRequiredService<IGraphBackgroundRuntime>()
                .StartAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown cancelled optional graph initialization before it became active.
        }
        catch (Exception exception)
        {
            try
            {
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>()
                    .CreateLogger(nameof(App))
                    .LogError(
                        "Optional Knowledge Graph startup failed safely in category {FailureCategory}; existing Search and indexing remain available.",
                        exception.GetType().Name);
            }
            catch (Exception loggingException)
            {
                System.Diagnostics.Trace.TraceError(
                    "Optional Knowledge Graph startup failed ({0}); logging also failed ({1}).",
                    exception.GetType().Name,
                    loggingException.GetType().Name);
            }
        }
    }
}
