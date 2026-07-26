#pragma warning disable CS1591

using OpenSorSe.Application.Watching;
using OpenSorSe.Desktop.Services;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Executor;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Desktop.Tests;

public sealed class WatchedFoldersViewModelTests
{
    [Fact]
    public async Task AddPauseResumeAndRemove_UseManagerWithoutDeletingFolder()
    {
        using var context = new ViewModelContext();
        var file = Path.Combine(context.Root, "keep.txt");
        await File.WriteAllTextAsync(file, "keep");
        context.ViewModel.FolderPath = context.Root;
        context.ViewModel.DisplayName = "Inbox";

        await context.ViewModel.AddFolderCommand.ExecuteAsync(null);
        var added = Assert.Single(context.ViewModel.Folders);
        context.ViewModel.SelectedFolder = added;
        await context.ViewModel.PauseCommand.ExecuteAsync(null);
        Assert.False(context.ViewModel.SelectedFolder!.IsEnabled);
        await context.ViewModel.ResumeCommand.ExecuteAsync(null);
        Assert.True(context.ViewModel.SelectedFolder!.IsEnabled);
        context.ViewModel.RequestRemoveCommand.Execute(null);
        Assert.True(context.ViewModel.IsRemoveConfirmationPending);
        await context.ViewModel.ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.Empty(context.ViewModel.Folders);
        Assert.True(File.Exists(file));
        Assert.True(Directory.Exists(context.Root));
        Assert.Contains("not deleted", context.ViewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.True(context.Coordinator.RefreshCount >= 3);
    }

    [Fact]
    public async Task Commands_QueueIncrementalReconciliationAndAiRetryWithPreciseLabels()
    {
        using var context = new ViewModelContext();
        var configuration = await context.Manager.AddAsync(
            new WatchedFolderCreateRequest(context.Root, "Root", AiAnalysisEnabled: true),
            CancellationToken.None);
        await context.ViewModel.RefreshAsync();
        context.ViewModel.SelectedFolder = Assert.Single(context.ViewModel.Folders);

        await context.ViewModel.ScanChangesNowCommand.ExecuteAsync(null);
        Assert.Equal(configuration.Id, context.Coordinator.IncrementalId);
        Assert.Contains("Only changed items", context.ViewModel.StatusText, StringComparison.Ordinal);
        await context.ViewModel.FullReconciliationCommand.ExecuteAsync(null);
        Assert.Equal(configuration.Id, context.Coordinator.ReconciliationId);
        Assert.Contains("Unchanged content", context.ViewModel.StatusText, StringComparison.Ordinal);
        await context.ViewModel.RetryAiCommand.ExecuteAsync(null);
        Assert.Equal(configuration.Id, context.Coordinator.AiRetryId);
    }

    [Fact]
    public async Task RuntimeSnapshot_PresentsUnavailableQueueProgressPlansErrorsAndGroupedActivity()
    {
        using var context = new ViewModelContext();
        var configuration = await context.Manager.AddAsync(
            new WatchedFolderCreateRequest(context.Root, "Root"),
            CancellationToken.None);
        await context.ViewModel.RefreshAsync();
        context.ViewModel.SelectedFolder = Assert.Single(context.ViewModel.Folders);
        var activity = new WatchedActivityEntry(
            "activity:1",
            configuration.Id,
            WatchedActivityKind.ReconciliationCompleted,
            DateTimeOffset.UtcNow,
            "3 files updated.",
            "batch:1",
            3);
        var unavailable = configuration with
        {
            Status = WatchedFolderStatus.Unavailable,
            QueuedChangeCount = 7,
            PendingChangePlanCount = 2,
            LastDetectedChangeUtc = DateTimeOffset.UtcNow,
            LastSuccessfulScanUtc = DateTimeOffset.UtcNow,
            LastReconciliationUtc = DateTimeOffset.UtcNow,
            LatestSummary = "Watched folder unavailable.",
            LastError = "Drive disconnected.",
        };

        context.Coordinator.RaiseState(new WatchedFolderRuntimeSnapshot(unavailable, [activity]));

        var row = Assert.Single(context.ViewModel.Folders);
        Assert.Equal("Unavailable", row.AvailabilityText);
        Assert.Equal("7 queued", row.QueueText);
        Assert.Equal("2 pending Change Plans", row.PendingPlanText);
        Assert.True(row.HasError);
        Assert.Equal("Drive disconnected.", row.Error);
        Assert.Equal(activity, Assert.Single(context.ViewModel.RecentActivity));
    }

    [Fact]
    public async Task ReviewSuggestions_OpensPersistedWatchedPlanWithoutApplyingIt()
    {
        using var context = new ViewModelContext();
        var configuration = await context.Manager.AddAsync(
            new WatchedFolderCreateRequest(context.Root, "Root"),
            CancellationToken.None);
        var plan = new ChangePlan(
            ChangePlanSchema.CurrentVersion,
            "plan:watch",
            DateTimeOffset.UtcNow,
            $"watched:{configuration.Id}:batch:1",
            context.Root,
            ChangePlanStatus.AwaitingReview,
            [],
            [],
            null,
            false);
        await context.PlanStore.UpsertAsync(plan, CancellationToken.None);
        await context.ViewModel.RefreshAsync();
        var ready = configuration with { PendingChangePlanCount = 1 };
        context.Coordinator.RaiseState(new WatchedFolderRuntimeSnapshot(ready, []));
        ChangePlan? requested = null;
        context.ViewModel.ReviewPlanRequested += (_, value) => requested = value;

        await context.ViewModel.ReviewSuggestionsCommand.ExecuteAsync(null);

        Assert.Equal(plan, requested);
        Assert.Equal(ChangePlanStatus.AwaitingReview, requested!.Status);
    }

    [Fact]
    public async Task InvalidOverlap_ShowsMeaningfulValidationWithoutAddingDuplicate()
    {
        using var context = new ViewModelContext();
        await context.Manager.AddAsync(
            new WatchedFolderCreateRequest(context.Root, "Parent"),
            CancellationToken.None);
        context.ViewModel.FolderPath = Path.Combine(context.Root, "Child");
        context.ViewModel.DisplayName = "Child";
        Directory.CreateDirectory(context.ViewModel.FolderPath);

        await context.ViewModel.AddFolderCommand.ExecuteAsync(null);

        Assert.Single(await context.Manager.ListAsync(CancellationToken.None));
        Assert.Contains("overlaps", context.ViewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveSettings_PersistsNotificationPreferences()
    {
        using var context = new ViewModelContext();
        var configuration = await context.Manager.AddAsync(
            new WatchedFolderCreateRequest(context.Root, "Root"),
            CancellationToken.None);
        await context.ViewModel.RefreshAsync();
        context.ViewModel.SelectedFolder = Assert.Single(context.ViewModel.Folders);
        context.ViewModel.NotificationLevel = WatchedFolderNotificationLevel.ErrorsOnly;
        context.ViewModel.NotifyWhenPlanReady = false;
        context.ViewModel.NotifyWhenUnavailable = false;

        await context.ViewModel.SaveSettingsCommand.ExecuteAsync(null);

        var saved = Assert.Single(await context.Manager.ListAsync(CancellationToken.None));
        Assert.Equal(configuration.Id, saved.Id);
        Assert.Equal(WatchedFolderNotificationLevel.ErrorsOnly, saved.Notifications.Level);
        Assert.False(saved.Notifications.NotifyWhenPlanReady);
        Assert.False(saved.Notifications.NotifyWhenUnavailable);
    }

    [Fact]
    public async Task NotificationLevelNone_SuppressesRuntimeNotifications()
    {
        using var context = new ViewModelContext();
        var configuration = await context.Manager.AddAsync(
            new WatchedFolderCreateRequest(
                context.Root,
                "Root",
                Notifications: new WatchedFolderNotificationPreferences(
                    WatchedFolderNotificationLevel.None)),
            CancellationToken.None);
        await context.ViewModel.RefreshAsync();
        var notifications = 0;
        context.ViewModel.NotificationRequested += (_, _) => notifications++;

        context.Coordinator.RaiseState(new WatchedFolderRuntimeSnapshot(
            configuration with
            {
                Status = WatchedFolderStatus.Unavailable,
                LastError = "Disconnected.",
                LatestSummary = "Unavailable.",
                PendingChangePlanCount = 1,
            },
            []));

        Assert.Equal(0, notifications);
    }

    private sealed class ViewModelContext : IDisposable
    {
        public ViewModelContext()
        {
            Workspace = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                $"opensorse-watched-viewmodel-{Guid.NewGuid():N}"));
            Root = Directory.CreateDirectory(Path.Combine(Workspace, "root")).FullName;
            Manager = new WatchedFolderManager(new MemoryConfigurationStore(), new WatchedFolderPathPolicy());
            Coordinator = new RecordingCoordinator();
            PlanStore = new InMemoryChangePlanStore();
            ViewModel = new WatchedFoldersViewModel(
                Manager,
                Coordinator,
                new RecordingLauncher(),
                PlanStore);
        }

        public string Workspace { get; }
        public string Root { get; }
        public WatchedFolderManager Manager { get; }
        public RecordingCoordinator Coordinator { get; }
        public InMemoryChangePlanStore PlanStore { get; }
        public WatchedFoldersViewModel ViewModel { get; }

        public void Dispose()
        {
            ViewModel.Dispose();
            var fullPath = Path.GetFullPath(Workspace);
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), fullPath, StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
    }

    private sealed class MemoryConfigurationStore : IWatchedFolderConfigurationStore
    {
        private IReadOnlyList<WatchedFolderConfiguration> _values = [];
        public Task<IReadOnlyList<WatchedFolderConfiguration>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_values);
        public Task SaveAsync(
            IReadOnlyList<WatchedFolderConfiguration> configurations,
            CancellationToken cancellationToken)
        {
            _values = configurations.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCoordinator : IWatchedFolderCoordinator
    {
        public event EventHandler<WatchedFolderRuntimeSnapshot>? StateChanged;
        public event EventHandler<WatchedActivityEntry>? ActivityPublished;
        public int RefreshCount { get; private set; }
        public string? IncrementalId { get; private set; }
        public string? ReconciliationId { get; private set; }
        public string? AiRetryId { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RefreshAsync(CancellationToken cancellationToken)
        {
            RefreshCount++;
            return Task.CompletedTask;
        }
        public Task ScanChangesNowAsync(string configurationId, CancellationToken cancellationToken)
        {
            IncrementalId = configurationId;
            return Task.CompletedTask;
        }
        public Task ReconcileNowAsync(string configurationId, CancellationToken cancellationToken)
        {
            ReconciliationId = configurationId;
            return Task.CompletedTask;
        }
        public Task RetryAiAsync(string configurationId, CancellationToken cancellationToken)
        {
            AiRetryId = configurationId;
            return Task.CompletedTask;
        }
        public void RaiseState(WatchedFolderRuntimeSnapshot snapshot) =>
            StateChanged?.Invoke(this, snapshot);
        public void RaiseActivity(WatchedActivityEntry activity) =>
            ActivityPublished?.Invoke(this, activity);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingLauncher : IExternalFileLauncher
    {
        public Task<ExternalLaunchResult> OpenFileAsync(string fullPath, CancellationToken cancellationToken) =>
            Task.FromResult(ExternalLaunchResult.Success("Opened."));
        public Task<ExternalLaunchResult> OpenContainingFolderAsync(string fullPath, CancellationToken cancellationToken) =>
            Task.FromResult(ExternalLaunchResult.Success("Folder opened."));
    }
}
