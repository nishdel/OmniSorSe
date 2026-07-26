using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSorSe.Application;
using OpenSorSe.Application.AI;
using OpenSorSe.Application.Catalog;
using OpenSorSe.Application.CatalogSearch;
using OpenSorSe.Application.Models;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Logging;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Rules.Models;
using OpenSorSe.Scanner.Models;
using DesktopScanRequest = OpenSorSe.Desktop.ViewModels.ScanRequest;

namespace OpenSorSe.Desktop.Tests;

/// <summary>
/// Verifies deterministic state transitions in the desktop application shell.
/// </summary>
public sealed class MainViewModelTests
{
    /// <summary>
    /// Verifies the shell starts on Dashboard and exposes only regular destinations by default.
    /// </summary>
    [Fact]
    public void Constructor_InitializesDashboardNavigation()
    {
        var viewModel = new MainViewModel();

        Assert.Equal(NavigationDestination.Dashboard, viewModel.SelectedDestination);
        Assert.Equal("Home", viewModel.CurrentPageTitle);
        Assert.Equal("Ready", viewModel.StatusText);
        Assert.DoesNotContain(NavigationDestination.CatalogComparison, viewModel.Destinations);
        Assert.DoesNotContain(NavigationDestination.StructureHistory, viewModel.Destinations);
        Assert.DoesNotContain(NavigationDestination.Diagnostics, viewModel.Destinations);
        Assert.DoesNotContain(NavigationDestination.History, viewModel.Destinations);
        Assert.Contains(NavigationDestination.Scan, viewModel.Destinations);
        Assert.Contains(NavigationDestination.Results, viewModel.Destinations);
        Assert.Contains(NavigationDestination.Catalog, viewModel.Destinations);
        Assert.Contains(NavigationDestination.Duplicates, viewModel.Destinations);
        Assert.DoesNotContain(NavigationDestination.CatalogSearch, viewModel.Destinations);
        Assert.DoesNotContain(NavigationDestination.SemanticSearch, viewModel.Destinations);
    }

    /// <summary>Verifies Help is regular, immediately precedes About, and contextual Back restores its origin.</summary>
    [Fact]
    public void ContextualHelp_OpensExpectedTopicAndReturnsToOrigin()
    {
        using var viewModel = new MainViewModel();
        var labels = viewModel.NavigationItems.Select(item => item.Label).ToArray();
        Assert.Equal(Array.IndexOf(labels, "About") - 1, Array.IndexOf(labels, "Help"));
        viewModel.Navigate(NavigationDestination.CatalogSearch);

        viewModel.CatalogSearch.HelpCommand.Execute(null);

        Assert.Equal(NavigationDestination.Help, viewModel.SelectedDestination);
        Assert.Equal(HelpTopicId.CatalogSearch, viewModel.Help.SelectedTopic.Id);
        Assert.Equal(NavigationDestination.CatalogSearch, viewModel.Help.PreviousDestination);
        viewModel.Help.BackCommand.Execute(null);
        Assert.Equal(NavigationDestination.CatalogSearch, viewModel.SelectedDestination);
    }

    /// <summary>Verifies the shell retains one Settings instance across navigation and Help round-trips.</summary>
    [Fact]
    public void Navigation_RetainsLongLivedSettingsInstance()
    {
        using var viewModel = new MainViewModel();
        var settings = viewModel.Settings;
        viewModel.Navigate(NavigationDestination.Settings);
        settings.HelpCommand.Execute(null);
        viewModel.Help.BackCommand.Execute(null);
        viewModel.Navigate(NavigationDestination.Dashboard);
        viewModel.Navigate(NavigationDestination.Settings);

        Assert.Same(settings, viewModel.Settings);
    }

    /// <summary>
    /// Verifies navigation updates the selected destination and presentation title without business work.
    /// </summary>
    [Fact]
    public void Navigate_UpdatesPresentationState()
    {
        var viewModel = new MainViewModel();
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, arguments) => changedProperties.Add(arguments.PropertyName);

        viewModel.Navigate(NavigationDestination.Settings);

        Assert.Equal(NavigationDestination.Settings, viewModel.SelectedDestination);
        Assert.Equal("Settings", viewModel.CurrentPageTitle);
        Assert.True(viewModel.IsSettingsSelected);
        Assert.False(viewModel.IsDashboardSelected);
        Assert.False(viewModel.IsScanSelected);
        Assert.False(viewModel.IsResultsSelected);
        Assert.False(viewModel.IsRulesSelected);
        Assert.False(viewModel.IsStructureHistorySelected);
        Assert.False(viewModel.IsDiagnosticsSelected);
        Assert.False(viewModel.IsHistorySelected);
        Assert.False(viewModel.IsAboutSelected);
        Assert.Contains(nameof(MainViewModel.SelectedDestination), changedProperties);
        Assert.Contains(nameof(MainViewModel.CurrentPageTitle), changedProperties);
        Assert.Contains(nameof(MainViewModel.IsDashboardSelected), changedProperties);
        Assert.Contains(nameof(MainViewModel.IsScanSelected), changedProperties);
        Assert.Contains(nameof(MainViewModel.IsResultsSelected), changedProperties);
        Assert.Contains(nameof(MainViewModel.IsRulesSelected), changedProperties);
        Assert.Contains(nameof(MainViewModel.IsStructureHistorySelected), changedProperties);
        Assert.Contains(nameof(MainViewModel.IsSettingsSelected), changedProperties);
        Assert.Contains(nameof(MainViewModel.IsDiagnosticsSelected), changedProperties);
        Assert.Contains(nameof(MainViewModel.IsHistorySelected), changedProperties);
        Assert.Contains(nameof(MainViewModel.IsAboutSelected), changedProperties);
    }

    /// <summary>
    /// Verifies the Scan destination exposes only the scan-root selection state.
    /// </summary>
    [Fact]
    public void Navigate_ToScan_ExposesFolderSelectionState()
    {
        var viewModel = new MainViewModel();

        viewModel.Navigate(NavigationDestination.Scan);

        Assert.True(viewModel.IsScanSelected);
        Assert.False(viewModel.IsFeaturePageSelected);
        Assert.NotNull(viewModel.FolderSelection);
    }

    /// <summary>Verifies shell navigation uses readable labels and remains synchronized with programmatic navigation.</summary>
    [Fact]
    public void NavigationItems_ExposeReadableLabelsAndTrackDestination()
    {
        using var viewModel = new MainViewModel(new TestConfigurationService(advancedEnabled: true), new TestLoggingService());

        Assert.DoesNotContain(viewModel.NavigationItems, item => item.Destination == NavigationDestination.CatalogComparison);
        Assert.Contains(viewModel.NavigationItems, item => item.Destination == NavigationDestination.StructureHistory && item.Label == "Folder plans");
        Assert.Equal(
            ["Home", "Scan", "Files", "Duplicates", "Saved scans", "Settings"],
            viewModel.PrimaryNavigationItems.Select(item => item.Label));
        Assert.DoesNotContain(viewModel.NavigationItems, item => item.Label == nameof(NavigationDestination.CatalogComparison));

        viewModel.Navigate(NavigationDestination.CatalogSearch);

        Assert.Equal(NavigationDestination.Catalog, viewModel.SelectedNavigationItem.Destination);
        Assert.Equal("Saved scans", viewModel.SelectedNavigationItem.Label);
        Assert.True(viewModel.IsSavedScansAreaSelected);
    }

    /// <summary>Verifies Structure history is an advanced hosted page with a controlled unavailable state in preview composition.</summary>
    [Fact]
    public async Task NavigateAsync_ToStructureHistory_HostsAdvancedHistoryPage()
    {
        using var viewModel = new MainViewModel(
            new TestConfigurationService(advancedEnabled: true),
            new TestLoggingService());

        await viewModel.NavigateAsync(NavigationDestination.StructureHistory);

        Assert.True(viewModel.IsStructureHistorySelected);
        Assert.False(viewModel.IsFeaturePageSelected);
        Assert.Equal("Folder plans", viewModel.CurrentPageTitle);
        Assert.Contains("unavailable", viewModel.StructureHistory.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies the v0.9 comparison destination is fully hosted rather than falling through to a placeholder.</summary>
    [Fact]
    public void Navigate_ToCatalogComparison_ExposesComparisonState()
    {
        using var viewModel = new MainViewModel(new TestConfigurationService(advancedEnabled: true), new TestLoggingService());

        viewModel.Navigate(NavigationDestination.CatalogComparison);

        Assert.True(viewModel.IsCatalogComparisonSelected);
        Assert.False(viewModel.IsFeaturePageSelected);
        Assert.Equal("Compare scans", viewModel.CurrentPageTitle);
        Assert.Equal(SavedScansSection.Compare, viewModel.SelectedSavedScansSection);
        Assert.NotNull(viewModel.CatalogComparison);
    }

    /// <summary>Verifies Semantic Search Beta is independently gated and does not require AI or advanced mode.</summary>
    [Fact]
    public void Constructor_SemanticSearchEnabled_ExposesMeaningSearchFromFiles()
    {
        using var viewModel = new MainViewModel(
            new TestConfigurationService(semanticSearchEnabled: true),
            new TestLoggingService());

        Assert.DoesNotContain(NavigationDestination.SemanticSearch, viewModel.Destinations);
        Assert.True(viewModel.Results.IsMeaningSearchEnabled);
        Assert.False(viewModel.EnableAi);
        Assert.False(viewModel.ShowAdvancedFeatures);
        viewModel.Results.OpenMeaningSearchCommand.Execute(null);
        Assert.True(viewModel.IsSemanticSearchSelected);
        Assert.Equal("Meaning Search (Beta)", viewModel.CurrentPageTitle);
        Assert.Equal(NavigationDestination.Results, viewModel.SelectedNavigationItem.Destination);
    }

    /// <summary>Verifies sidebar-style navigation awaits the destination refresh instead of abandoning background work.</summary>
    [Fact]
    public async Task NavigateAsync_ToCatalogComparison_AwaitsSelectorRefresh()
    {
        var catalogStore = new RecordingCatalogStore();
        using var viewModel = new MainViewModel(
            new TestConfigurationService(catalogEnabled: true, advancedEnabled: true),
            new TestLoggingService(),
            new RecordingController(),
            new ResultsSnapshotProjector(),
            new NoopAiSuggestionService(),
            catalogStore,
            new RecordingSavedSearchStore());

        await viewModel.NavigateAsync(NavigationDestination.CatalogComparison);

        Assert.Equal(NavigationDestination.CatalogComparison, viewModel.SelectedDestination);
        Assert.Equal(1, catalogStore.ListCallCount);
        Assert.Equal("No saved snapshots are available for comparison.", viewModel.CatalogComparison.StatusText);
    }

    /// <summary>Verifies direct or stale navigation cannot bypass hidden advanced-feature policy.</summary>
    [Fact]
    public void Navigate_HiddenAdvancedDestination_IsRejectedWithoutChangingSelection()
    {
        using var viewModel = new MainViewModel();

        viewModel.Navigate(NavigationDestination.Diagnostics);

        Assert.Equal(NavigationDestination.Dashboard, viewModel.SelectedDestination);
        Assert.Contains("hidden", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies saving advanced mode off rebuilds navigation and safely recovers a hidden selection.</summary>
    [Fact]
    public async Task SaveSettings_DisablesAdvancedMode_RecoversToDashboard()
    {
        var configuration = new TestConfigurationService(advancedEnabled: true);
        using var viewModel = new MainViewModel(configuration, new TestLoggingService());
        viewModel.Navigate(NavigationDestination.Diagnostics);
        viewModel.Settings.Draft.ShowAdvancedFeatures = false;

        await viewModel.Settings.SaveCommand.ExecuteAsync(null);

        Assert.Equal(NavigationDestination.Dashboard, viewModel.SelectedDestination);
        Assert.DoesNotContain(viewModel.NavigationItems, item => item.Destination == NavigationDestination.Diagnostics);
        Assert.Contains(viewModel.NavigationItems, item => item.Destination == NavigationDestination.Results);
    }

    /// <summary>Verifies shell switches persist, synchronize the Settings draft, and never contact the AI provider.</summary>
    [Fact]
    public async Task ShellFeatureSwitches_UpdateSettingsWithoutProviderActivity()
    {
        var configuration = new TestConfigurationService();
        var ai = new NoopAiSuggestionService();
        using var viewModel = new MainViewModel(
            configuration,
            new TestLoggingService(),
            new RecordingController(),
            new ResultsSnapshotProjector(),
            ai);

        Assert.False(viewModel.EnableAi);
        Assert.False(viewModel.ShowAdvancedFeatures);
        Assert.Equal("AI: Off", viewModel.AiShellStatusText);

        viewModel.EnableAi = true;
        viewModel.ShowAdvancedFeatures = true;
        await configuration.Saved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(
            () => configuration.Current.Ai.Enabled &&
                  configuration.Current.Features.ShowAdvancedFeatures);

        Assert.True(viewModel.Settings.Draft.AiEnabled);
        Assert.True(viewModel.Settings.Draft.ShowAdvancedFeatures);
        Assert.Equal("AI: On", viewModel.AiShellStatusText);
        Assert.Equal("Advanced: On", viewModel.AdvancedShellStatusText);
        Assert.Equal(0, ai.ProviderActivityCount);
    }

    /// <summary>Verifies the global Advanced switch safely leaves a page that becomes hidden.</summary>
    [Fact]
    public async Task ShellAdvancedSwitch_DisablingOnAdvancedPage_FallsBackToDashboard()
    {
        var configuration = new TestConfigurationService(advancedEnabled: true);
        using var viewModel = new MainViewModel(configuration, new TestLoggingService());
        viewModel.Navigate(NavigationDestination.Diagnostics);

        viewModel.ShowAdvancedFeatures = false;
        await configuration.Saved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => viewModel.SelectedDestination == NavigationDestination.Dashboard);

        Assert.DoesNotContain(NavigationDestination.Diagnostics, viewModel.Destinations);
        Assert.False(viewModel.Settings.Draft.ShowAdvancedFeatures);
    }

    /// <summary>
    /// Verifies unsupported navigation input is rejected before presentation state changes.
    /// </summary>
    [Fact]
    public void Navigate_UnsupportedDestination_ThrowsWithoutChangingState()
    {
        var viewModel = new MainViewModel();

        Assert.Throws<ArgumentOutOfRangeException>(() => viewModel.Navigate((NavigationDestination)999));

        Assert.Equal(NavigationDestination.Dashboard, viewModel.SelectedDestination);
    }

    /// <summary>
    /// Verifies dashboard quick actions request shell navigation without starting business operations.
    /// </summary>
    [Fact]
    public void DashboardQuickActions_KeepResultsUnavailableUntilAScanCompletes()
    {
        var viewModel = new MainViewModel();
        var initialStatistics = viewModel.Dashboard.Statistics;

        viewModel.Dashboard.ScanFolderCommand.Execute(null);
        Assert.Equal(NavigationDestination.Scan, viewModel.SelectedDestination);
        Assert.False(viewModel.Dashboard.ViewResultsCommand.CanExecute(null));
        viewModel.Dashboard.UpdateFromCompletedScan(new ResultsSummary(2, 1, 0, 1, 0));
        viewModel.Dashboard.ViewResultsCommand.Execute(null);
        Assert.Equal(NavigationDestination.Results, viewModel.SelectedDestination);
        viewModel.Dashboard.OpenSettingsCommand.Execute(null);

        Assert.Equal(NavigationDestination.Settings, viewModel.SelectedDestination);
        Assert.NotEqual(initialStatistics, viewModel.Dashboard.Statistics);
        Assert.Equal(new DashboardStatistics(2, 1, 1, 0), viewModel.Dashboard.Statistics);
        Assert.True(viewModel.Dashboard.HasCompletedScan);
    }

    /// <summary>
    /// Verifies the shell converts a selected-folder request into a controller request and presents read-only results.
    /// </summary>
    [Fact]
    public async Task StartProcessingAsync_CompletedRequest_PresentsDiscoveredFilesAndDirectories()
    {
        var controller = new RecordingController();
        using var viewModel = new MainViewModel(new TestConfigurationService(), new TestLoggingService(), controller);
        var request = new DesktopScanRequest(["C:\\Selected"]);

        var start = viewModel.StartProcessingAsync(request);
        var forwarded = await controller.Request.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(request.FolderPaths, forwarded.ScanRequest.RootDirectories);
        Assert.Empty(forwarded.Rules);
        Assert.True(viewModel.IsProcessing);
        Assert.True(viewModel.ScanProgress.IsActive);

        var file = new FileEntry("C:\\Selected\\document.txt");
        var directory = new DirectoryEntry("C:\\Selected");
        controller.Completion.SetResult(CompletedResult(file, directory));
        await start;

        Assert.False(viewModel.IsProcessing);
        Assert.Equal(NavigationDestination.Results, viewModel.SelectedDestination);
        Assert.NotNull(viewModel.Results.Snapshot);
        Assert.Equal([file.FullPath], viewModel.Results.Snapshot!.Files.Select(resultFile => resultFile.FullPath));
        Assert.Equal([directory.FullPath], viewModel.Results.Directories.Select(resultDirectory => resultDirectory.FullPath));
        Assert.Equal(["A test scan warning."], viewModel.Results.Warnings);
        Assert.Contains("1 file(s) and 1 folder(s)", viewModel.StatusText, StringComparison.Ordinal);
        Assert.Equal(ScanProgressStage.Completed, viewModel.ScanProgress.Stage);
        Assert.Equal(new DashboardStatistics(1, 1, 0, 1), viewModel.Dashboard.Statistics);
        Assert.True(viewModel.Dashboard.HasCompletedScan);
        Assert.True(viewModel.Dashboard.ViewResultsCommand.CanExecute(null));

        viewModel.Navigate(NavigationDestination.Dashboard);
        Assert.Equal(new DashboardStatistics(1, 1, 0, 1), viewModel.Dashboard.Statistics);
        viewModel.Dashboard.ViewResultsCommand.Execute(null);
        Assert.Equal(NavigationDestination.Results, viewModel.SelectedDestination);
        Assert.Equal([file.FullPath], viewModel.Results.Snapshot!.Files.Select(resultFile => resultFile.FullPath));
    }

    /// <summary>
    /// Verifies progress-view cancellation reaches the controller and never invokes a filesystem executor.
    /// </summary>
    [Fact]
    public async Task StartProcessingAsync_CancelRequest_ForwardsCancellationAndKeepsTheScanPage()
    {
        var controller = new RecordingController();
        using var viewModel = new MainViewModel(new TestConfigurationService(), new TestLoggingService(), controller);
        viewModel.Navigate(NavigationDestination.Scan);

        var start = viewModel.StartProcessingAsync(new DesktopScanRequest(["C:\\Selected"]));
        await controller.Request.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.ScanProgress.RequestCancellation();
        await controller.CancellationRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        controller.Completion.SetResult(new ProcessingSessionResult(
            new ProcessingSession("session:cancelled", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ProcessingSessionStatus.Cancelled, null),
            null));
        await start;

        Assert.False(viewModel.IsProcessing);
        Assert.Equal(NavigationDestination.Scan, viewModel.SelectedDestination);
        Assert.Equal(ScanProgressStage.Cancelled, viewModel.ScanProgress.Stage);
        Assert.Equal("Scan cancelled. Any partial discovery results were not processed further.", viewModel.StatusText);
    }

    /// <summary>
    /// Verifies an enabled catalog receives a completed display-safe snapshot after Results is already usable.
    /// </summary>
    [Fact]
    public async Task StartProcessingAsync_EnabledCatalog_PersistsCompletedSnapshotWithoutChangingResultsFlow()
    {
        var controller = new RecordingController();
        var catalog = new RecordingCatalogStore();
        using var viewModel = new MainViewModel(
            new TestConfigurationService(catalogEnabled: true),
            new TestLoggingService(),
            controller,
            new ResultsSnapshotProjector(),
            new NoopAiSuggestionService(),
            catalog);
        var start = viewModel.StartProcessingAsync(new DesktopScanRequest(["C:\\Selected"]));
        await controller.Request.Task.WaitAsync(TimeSpan.FromSeconds(5));
        controller.Completion.SetResult(CompletedResult(new FileEntry("C:\\Selected\\document.txt"), new DirectoryEntry("C:\\Selected")));

        await start;
        var saved = await catalog.Saved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await catalog.DependentsRefreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(NavigationDestination.Results, viewModel.SelectedDestination);
        Assert.NotNull(viewModel.Results.Snapshot);
        Assert.Equal(viewModel.Results.Snapshot!.SessionId, saved.Snapshot.SessionId);
        Assert.Equal(["C:\\Selected\\document.txt"], saved.Snapshot.Files.Select(file => file.FullPath));
        Assert.Equal(["C:\\Selected"], saved.SourceRoots);
        Assert.Contains("changed", viewModel.CatalogSearch.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Single(viewModel.CatalogComparison.Entries);
    }

    /// <summary>
    /// Verifies production composition initializes bounded named query presets without enumerating scan files.
    /// </summary>
    [Fact]
    public async Task InitializeCatalogAsync_WithSavedSearchStore_LoadsNamedQueries()
    {
        var savedSearchStore = new RecordingSavedSearchStore();
        using var viewModel = new MainViewModel(
            new TestConfigurationService(catalogEnabled: true),
            new TestLoggingService(),
            new RecordingController(),
            new ResultsSnapshotProjector(),
            new NoopAiSuggestionService(),
            new RecordingCatalogStore(),
            savedSearchStore);

        await viewModel.InitializeCatalogAsync();

        Assert.Equal(1, savedSearchStore.ListCallCount);
        var row = Assert.Single(viewModel.CatalogSearch.SavedSearches);
        Assert.Equal("Finance", row.Name);
        Assert.Equal("finance", row.QueryText);
    }

    private static ProcessingSessionResult CompletedResult(FileEntry file, DirectoryEntry directory)
    {
        var scan = new ScanResult(
            [file],
            [directory],
            new ScanStatistics(1, 1, 1),
            [new ScanIssue("C:\\Selected\\Skipped", ScanIssueKind.DirectoryUnavailable, "A test scan warning.")],
            ScanStatus.Completed,
            TimeSpan.Zero);
        var conflicts = new ConflictResolutionResult(
            [],
            new ConflictResolutionStatistics(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            []);
        var duplicates = new DuplicateDetectionResult(
            [file],
            [],
            new DuplicateDetectionStatistics(1, 1, 0, 0, 0, 0),
            []);
        var processing = new ProcessingResult(ProcessingStatus.Completed, scan, null, null, null, duplicates, null, null, conflicts);
        return new ProcessingSessionResult(
            new ProcessingSession("session:completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ProcessingSessionStatus.Completed, null),
            processing);
    }

    private sealed class RecordingController : IApplicationController
    {
        public TaskCompletionSource<ProcessingSessionResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<ProcessingRequest> Request { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> CancellationRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProcessingSessionResult> StartProcessingAsync(
            ProcessingRequest request,
            IProgress<ProcessingProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Request.TrySetResult(request);
            cancellationToken.Register(() => CancellationRequested.TrySetResult(true));
            return Completion.Task;
        }
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
        public TestConfigurationService(
            bool catalogEnabled = false,
            bool advancedEnabled = false,
            bool semanticSearchEnabled = false)
        {
            Current = new ApplicationSettings
            {
                Features = new FeatureSettings { ShowAdvancedFeatures = advancedEnabled },
                Catalog = new CatalogSettings { Enabled = catalogEnabled },
                SemanticSearch = new SemanticSearchSettings { Enabled = semanticSearchEnabled },
            };
        }

        public ApplicationSettings Current { get; private set; }

        public TaskCompletionSource<ApplicationSettings> Saved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            Saved.TrySetResult(settings);
            return Task.CompletedTask;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class TestLoggingService : ILoggingService
    {
        public void Initialize(LogLevel minimumLevel)
        {
        }

        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingCatalogStore : IResultsCatalogStore
    {
        private readonly List<CatalogEntry> _entries = [];

        public TaskCompletionSource<CatalogEntry> Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> DependentsRefreshed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ListCallCount { get; private set; }

        public Task<IReadOnlyList<CatalogEntrySummary>> ListAsync(CancellationToken cancellationToken)
        {
            ListCallCount++;
            if (Saved.Task.IsCompleted && ListCallCount >= 2)
            {
                DependentsRefreshed.TrySetResult(true);
            }

            return Task.FromResult<IReadOnlyList<CatalogEntrySummary>>(_entries.Select(entry => new CatalogEntrySummary(
                entry.Id,
                entry.SavedAtUtc,
                entry.Snapshot.Statistics.FilesDiscovered,
                entry.Snapshot.Statistics.DirectoriesDiscovered,
                entry.Snapshot.Statistics.WarningCount + entry.Snapshot.Statistics.ErrorCount,
                entry.Snapshot.Statistics.ExactDuplicateGroupCount)
            {
                DisplayName = entry.DisplayName,
                SourceRoots = entry.SourceRoots,
            }).ToArray());
        }

        public Task<CatalogEntry?> LoadAsync(string entryId, CancellationToken cancellationToken) => Task.FromResult<CatalogEntry?>(
            _entries.FirstOrDefault(entry => string.Equals(entry.Id, entryId, StringComparison.Ordinal)));

        public Task<CatalogEntrySummary> SaveAsync(CatalogEntry entry, CancellationToken cancellationToken)
        {
            _entries.RemoveAll(existing => string.Equals(existing.Id, entry.Id, StringComparison.Ordinal));
            _entries.Add(entry);
            Saved.TrySetResult(entry);
            return Task.FromResult(new CatalogEntrySummary(
                entry.Id,
                entry.SavedAtUtc,
                entry.Snapshot.Statistics.FilesDiscovered,
                entry.Snapshot.Statistics.DirectoriesDiscovered,
                entry.Snapshot.Statistics.WarningCount + entry.Snapshot.Statistics.ErrorCount,
                entry.Snapshot.Statistics.ExactDuplicateGroupCount));
        }

        public Task<bool> RemoveAsync(string entryId, CancellationToken cancellationToken)
        {
            var removed = _entries.RemoveAll(entry => string.Equals(entry.Id, entryId, StringComparison.Ordinal)) > 0;
            return Task.FromResult(removed);
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            _entries.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class NoopAiSuggestionService : IAiSuggestionService
    {
        public int ProviderActivityCount { get; private set; }

        public Task<AiConnectionResult> TestConnectionAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            ProviderActivityCount++;
            return Task.FromResult(new AiConnectionResult(AiAvailabilityState.Disabled, "Disabled", []));
        }

        public Task<AiConnectionResult> DiscoverModelsAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            ProviderActivityCount++;
            return Task.FromResult(new AiConnectionResult(AiAvailabilityState.Disabled, "Disabled", []));
        }

        public Task<AiFileRenameResult> GenerateFileRenameAsync(AiFileRenameRequest request, AiSettings settings, CancellationToken cancellationToken)
        {
            ProviderActivityCount++;
            return Task.FromResult(new AiFileRenameResult(AiAvailabilityState.Disabled, "Disabled", null));
        }

        public Task<AiFolderStructureResult> GenerateFolderStructureAsync(AiFolderStructureRequest request, AiSettings settings, CancellationToken cancellationToken)
        {
            ProviderActivityCount++;
            return Task.FromResult(new AiFolderStructureResult(AiAvailabilityState.Disabled, "Disabled", null));
        }

        public Task<AiDecisionResult> RecordDecisionAsync(AiSuggestionDecision decision, AiSettings settings, CancellationToken cancellationToken) => Task.FromResult(new AiDecisionResult(AiAvailabilityState.Disabled, "Disabled"));

        public Task<AiDecisionResult> ResetDecisionHistoryAsync(ApplicationSettings settings, CancellationToken cancellationToken) => Task.FromResult(new AiDecisionResult(AiAvailabilityState.Disabled, "Disabled"));
    }

    private sealed class RecordingSavedSearchStore : ISavedCatalogSearchStore
    {
        private readonly SavedCatalogSearch _search = new(
            "saved:finance",
            "Finance",
            "finance",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

        public int ListCallCount { get; private set; }

        public Task<IReadOnlyList<SavedCatalogSearch>> ListAsync(CancellationToken cancellationToken)
        {
            ListCallCount++;
            return Task.FromResult<IReadOnlyList<SavedCatalogSearch>>([_search]);
        }

        public Task<SavedCatalogSearch> SaveAsync(SavedCatalogSearch search, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RemoveAsync(string searchId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ClearAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
