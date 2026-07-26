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
using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.Structure;
using OpenSorSe.Application.Watching;
using OpenSorSe.Application.Workflows;
using OpenSorSe.AI;
using OpenSorSe.Desktop.Services;
using OpenSorSe.Core.Diagnostics;
using OpenSorSe.Executor;

namespace OpenSorSe.Desktop;

/// <summary>
/// Provides the Avalonia application entry point and desktop lifetime configuration.
/// </summary>
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
            _serviceProvider.GetRequiredService<IWorkflowLibraryService>()
                .InitializeAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            _serviceProvider.GetRequiredService<IWatchedFolderCoordinator>()
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
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenSorSe",
            "settings.json");
        var services = new ServiceCollection();
        services.AddOpenSorSeCore(new OpenSorSeCoreOptions { ConfigurationFilePath = settingsPath });
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
            var settingsFilePath = serviceProvider.GetRequiredService<OpenSorSeCoreOptions>().ConfigurationFilePath;
            var settingsDirectory = Path.GetDirectoryName(settingsFilePath)
                ?? throw new InvalidOperationException("The OpenSorSe settings path must include a directory.");
            return new JsonChangePlanStore(
                Path.Combine(settingsDirectory, "change-plans.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<IOperationJournalStore>(serviceProvider =>
        {
            var settingsFilePath = serviceProvider.GetRequiredService<OpenSorSeCoreOptions>().ConfigurationFilePath;
            var settingsDirectory = Path.GetDirectoryName(settingsFilePath)
                ?? throw new InvalidOperationException("The OpenSorSe settings path must include a directory.");
            return new JsonOperationJournalStore(
                Path.Combine(settingsDirectory, "operation-journal.json"),
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
            var settingsFilePath = serviceProvider.GetRequiredService<OpenSorSeCoreOptions>().ConfigurationFilePath;
            var settingsDirectory = Path.GetDirectoryName(settingsFilePath)
                ?? throw new InvalidOperationException("The OpenSorSe settings path must include a directory.");
            return new JsonContentStore(
                Path.Combine(settingsDirectory, "content-index.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<IContentIndexingService, ContentIndexingService>();
        services.AddSingleton<IEmbeddingProvider, FeatureHashingEmbeddingProvider>();
        services.AddSingleton<ISemanticIndexStore>(serviceProvider =>
        {
            var settingsFilePath = serviceProvider.GetRequiredService<OpenSorSeCoreOptions>().ConfigurationFilePath;
            var settingsDirectory = Path.GetDirectoryName(settingsFilePath)
                ?? throw new InvalidOperationException("The OpenSorSe settings path must include a directory.");
            return new JsonSemanticIndexStore(
                Path.Combine(settingsDirectory, "semantic-index.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<ISemanticIndexer, SemanticIndexer>();
        services.AddSingleton<ISemanticSearchService, SemanticSearchService>();
        services.AddSingleton<IFolderStructureSnapshotService, FolderStructureSnapshotService>();
        services.AddSingleton<IStructureComparisonService, StructureComparisonService>();
        services.AddSingleton<IStructureHistoryStore>(serviceProvider =>
        {
            var settingsFilePath = serviceProvider.GetRequiredService<OpenSorSeCoreOptions>().ConfigurationFilePath;
            var settingsDirectory = Path.GetDirectoryName(settingsFilePath)
                ?? throw new InvalidOperationException("The OpenSorSe settings path must include a directory.");
            return new JsonStructureHistoryStore(
                Path.Combine(settingsDirectory, "structure-history.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<IFolderRestructuringService, FolderRestructuringService>();
        services.AddSingleton<ICatalogComparisonService, CatalogComparisonService>();
        services.AddSingleton<IResultsCatalogStore>(serviceProvider =>
        {
            var settingsFilePath = serviceProvider.GetRequiredService<OpenSorSeCoreOptions>().ConfigurationFilePath;
            var settingsDirectory = Path.GetDirectoryName(settingsFilePath)
                ?? throw new InvalidOperationException("The OpenSorSe settings path must include a directory.");
            return new JsonResultsCatalogStore(Path.Combine(settingsDirectory, "catalog.json"), serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<WatchedFolderPathPolicy>();
        services.AddSingleton<IWatchedFolderConfigurationStore>(serviceProvider =>
        {
            var settingsFilePath = serviceProvider.GetRequiredService<OpenSorSeCoreOptions>().ConfigurationFilePath;
            var settingsDirectory = Path.GetDirectoryName(settingsFilePath)
                ?? throw new InvalidOperationException("The OpenSorSe settings path must include a directory.");
            return new JsonWatchedFolderConfigurationStore(
                Path.Combine(settingsDirectory, "watched-folders.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<IWatchedFolderCatalogueStore>(serviceProvider =>
        {
            var settingsFilePath = serviceProvider.GetRequiredService<OpenSorSeCoreOptions>().ConfigurationFilePath;
            var settingsDirectory = Path.GetDirectoryName(settingsFilePath)
                ?? throw new InvalidOperationException("The OpenSorSe settings path must include a directory.");
            return new JsonWatchedFolderCatalogueStore(
                Path.Combine(settingsDirectory, "watched-catalogues.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<IWatchedActivityStore>(serviceProvider =>
        {
            var settingsFilePath = serviceProvider.GetRequiredService<OpenSorSeCoreOptions>().ConfigurationFilePath;
            var settingsDirectory = Path.GetDirectoryName(settingsFilePath)
                ?? throw new InvalidOperationException("The OpenSorSe settings path must include a directory.");
            return new JsonWatchedActivityStore(
                Path.Combine(settingsDirectory, "watched-activity.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton<IWorkflowTemplateEngine, WorkflowTemplateEngine>();
        services.AddSingleton<IWorkflowValidator, WorkflowValidator>();
        services.AddSingleton<IWorkflowLibraryStore>(serviceProvider =>
        {
            var settingsFilePath = serviceProvider.GetRequiredService<OpenSorSeCoreOptions>().ConfigurationFilePath;
            var settingsDirectory = Path.GetDirectoryName(settingsFilePath)
                ?? throw new InvalidOperationException("The OpenSorSe settings path must include a directory.");
            return new JsonWorkflowLibraryStore(
                Path.Combine(settingsDirectory, "workflow-library.json"),
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
        services.AddSingleton<ISavedCatalogSearchStore>(serviceProvider =>
        {
            var settingsFilePath = serviceProvider.GetRequiredService<OpenSorSeCoreOptions>().ConfigurationFilePath;
            var settingsDirectory = Path.GetDirectoryName(settingsFilePath)
                ?? throw new InvalidOperationException("The OpenSorSe settings path must include a directory.");
            return new JsonSavedCatalogSearchStore(
                Path.Combine(settingsDirectory, "saved-catalog-searches.json"),
                serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
        });
        services.AddSingleton(new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
        services.AddSingleton<IAiSuggestionProvider, OllamaSuggestionProvider>();
        services.AddSingleton<IAiPromptBuilder, AiPromptBuilder>();
        services.AddSingleton<IAiResponseParser, AiResponseParser>();
        services.AddSingleton<IAiRequestDiagnosticsStore, AiRequestDiagnosticsStore>();
        services.AddSingleton<IAiDiagnosticsCollector, AiDiagnosticsCollector>();
        services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
        services.AddSingleton<IExternalFileLauncher, ExternalFileLauncher>();
        services.AddSingleton<AdvancedDiagnosticsWindowCoordinator>();
        services.AddSingleton<IAdvancedDiagnosticsWindowService>(serviceProvider =>
            serviceProvider.GetRequiredService<AdvancedDiagnosticsWindowCoordinator>());
        services.AddSingleton<IDecisionHistoryStore>(serviceProvider =>
        {
            var settingsFilePath = serviceProvider.GetRequiredService<OpenSorSeCoreOptions>().ConfigurationFilePath;
            var settingsDirectory = Path.GetDirectoryName(settingsFilePath)
                ?? throw new InvalidOperationException("The OpenSorSe settings path must include a directory.");
            return new JsonDecisionHistoryStore(Path.Combine(settingsDirectory, "decision-history.json"), serviceProvider.GetRequiredService<OpenSorSe.Core.Logging.ILoggingService>());
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
        _serviceProvider?.GetService<IDiagnosticsCollector>()?.ClearAll();
        _applicationHost?.ShutdownAsync().GetAwaiter().GetResult();
        _serviceProvider?.Dispose();
    }
}
