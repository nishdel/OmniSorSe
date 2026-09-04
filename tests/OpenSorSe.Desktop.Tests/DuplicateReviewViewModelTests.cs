using OpenSorSe.Application.Models;
using OpenSorSe.Application.ChangePlans;
using OpenSorSe.Core.Platform;
using OpenSorSe.Desktop.Services;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Executor.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Verifies Duplicate View navigation state and bounded non-destructive launcher coordination.</summary>
public sealed class DuplicateReviewViewModelTests
{
    /// <summary>Verifies selecting a group opens the drawer and the explicit close action clears only presentation state.</summary>
    [Fact]
    public void CloseDrawer_SelectedGroup_ClearsSelectionWithoutLaunchingAnything()
    {
        var launcher = new RecordingLauncher();
        using var viewModel = new DuplicateReviewViewModel(launcher);
        viewModel.LoadSnapshot(CreateSnapshot("session:drawer", 2));
        viewModel.SelectedGroupRow = Assert.Single(viewModel.VisibleGroupRows);

        Assert.True(viewModel.IsDrawerOpen);
        Assert.True(viewModel.CloseDrawerCommand.CanExecute(null));

        viewModel.CloseDrawerCommand.Execute(null);

        Assert.False(viewModel.IsDrawerOpen);
        Assert.Null(viewModel.SelectedGroup);
        Assert.Empty(launcher.OpenedFiles);
    }

    /// <summary>Verifies a two-member group opens exactly its two known files through the injected abstraction.</summary>
    [Fact]
    public async Task OpenBothFiles_ExactPair_UsesKnownPathsOnly()
    {
        var launcher = new RecordingLauncher();
        using var viewModel = new DuplicateReviewViewModel(launcher);
        viewModel.LoadSnapshot(CreateSnapshot("session:one", 2));
        viewModel.SelectedGroup = Assert.Single(viewModel.VisibleGroups);

        Assert.True(viewModel.CanOpenBothFiles);
        Assert.Equal(0, viewModel.SelectedMemberCount);
        await viewModel.OpenBothFilesCommand.ExecuteAsync(null);

        Assert.Equal(["C:\\Duplicates\\file-0.txt", "C:\\Duplicates\\file-1.txt"], launcher.OpenedFiles);
        Assert.Equal(StatusKind.Success, viewModel.Status.Kind);
    }

    /// <summary>Verifies cleanup selection is independent from the bounded external-open operation.</summary>
    [Fact]
    public void OpenSelectedFiles_LargeGroup_AllowsCleanupSelectionButDisablesBulkOpenAboveFive()
    {
        var launcher = new RecordingLauncher { FailPath = "C:\\Duplicates\\file-1.txt" };
        using var viewModel = new DuplicateReviewViewModel(launcher);
        viewModel.LoadSnapshot(CreateSnapshot("session:one", 6));
        viewModel.SelectedGroup = Assert.Single(viewModel.VisibleGroups);

        foreach (var row in viewModel.MemberRows)
        {
            row.IsSelected = true;
        }

        Assert.Equal(6, viewModel.SelectedMemberCount);
        Assert.True(viewModel.MemberRows[^1].IsSelected);
        Assert.False(viewModel.CanOpenSelectedFiles);
        Assert.False(viewModel.OpenSelectedFilesCommand.CanExecute(null));
        Assert.Empty(launcher.OpenedFiles);
    }

    /// <summary>Verifies the keeper helper is explicit and the global recovery selection can be cleared.</summary>
    [Fact]
    public void SelectionHelpersKeepOneKnownCopyAndCanClearAcrossGroups()
    {
        using var viewModel = new DuplicateReviewViewModel(null, new RecordingRemovalPlanFactory());
        viewModel.LoadSnapshot(CreateMultiGroupSnapshot());
        viewModel.SelectedGroup = viewModel.VisibleGroups[0];

        viewModel.SelectAllButFirstCommand.Execute(null);

        Assert.False(viewModel.MemberRows[0].IsSelected);
        Assert.True(viewModel.MemberRows[1].IsSelected);
        Assert.Equal(1, viewModel.PendingRemovalCount);
        Assert.True(viewModel.CanRequestRemovalPlan);

        viewModel.SelectedGroup = viewModel.VisibleGroups[1];
        viewModel.SelectAllButFirstCommand.Execute(null);
        Assert.Equal(2, viewModel.PendingRemovalCount);

        viewModel.ClearRemovalSelectionsCommand.Execute(null);
        Assert.Equal(0, viewModel.PendingRemovalCount);
        Assert.All(viewModel.MemberRows, row => Assert.False(row.IsSelected));
    }

    /// <summary>Verifies unknown rows cannot be routed into the launcher by a stale or internal command call.</summary>
    [Fact]
    public void OpenFile_UnknownRow_IsRejectedBeforeLauncher()
    {
        var launcher = new RecordingLauncher();
        using var viewModel = new DuplicateReviewViewModel(launcher);
        viewModel.LoadSnapshot(CreateSnapshot("session:one", 2));
        viewModel.SelectedGroup = Assert.Single(viewModel.VisibleGroups);
        var unknown = new DuplicateFileRow(CreateFile("unknown", "C:\\Outside\\unknown.txt", "group:other"));

        Assert.False(viewModel.OpenFileCommand.CanExecute(unknown));
        Assert.Empty(launcher.OpenedFiles);
    }

    /// <summary>Verifies a forged row cannot reuse a known opaque ID with a different path.</summary>
    [Fact]
    public void OpenFile_ForgedRowWithKnownId_IsRejectedBeforeLauncher()
    {
        var launcher = new RecordingLauncher();
        using var viewModel = new DuplicateReviewViewModel(launcher);
        viewModel.LoadSnapshot(CreateSnapshot("session:one", 2));
        viewModel.SelectedGroup = Assert.Single(viewModel.VisibleGroups);
        var forged = new DuplicateFileRow(CreateFile("file:0", "C:\\Outside\\forged.txt", "group:one"));

        Assert.False(viewModel.OpenFileCommand.CanExecute(forged));
        Assert.Empty(launcher.OpenedFiles);
    }

    /// <summary>Verifies cancellation stops an active shell-open loop and remains a controlled page state.</summary>
    [Fact]
    public async Task OpenBothFiles_Cancelled_StopsLauncherLoop()
    {
        var launcher = new RecordingLauncher { Block = true };
        using var viewModel = new DuplicateReviewViewModel(launcher);
        viewModel.LoadSnapshot(CreateSnapshot("session:one", 2));
        viewModel.SelectedGroup = Assert.Single(viewModel.VisibleGroups);

        var running = viewModel.OpenBothFilesCommand.ExecuteAsync(null);
        await launcher.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.CancelOpenCommand.Execute(null);
        await running;

        Assert.False(viewModel.IsOpening);
        Assert.Equal(StatusKind.Information, viewModel.Status.Kind);
        Assert.Contains("cancellation", viewModel.Status.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies removal requires a keeper, creates a Change Plan, and refreshes only after successful execution.</summary>
    [Fact]
    public async Task RequestRemovalPlan_RequiresKeeperAndRefreshesAfterExecution()
    {
        var factory = new RecordingRemovalPlanFactory();
        using var viewModel = new DuplicateReviewViewModel(null, factory);
        viewModel.LoadSnapshot(CreateSnapshot("session:removal", 2));
        viewModel.SelectedGroup = Assert.Single(viewModel.VisibleGroups);
        ChangePlan? created = null;
        viewModel.ChangePlanCreated += (_, plan) => created = plan;

        Assert.False(viewModel.CanRequestRemovalPlan);
        viewModel.MemberRows[0].IsSelected = true;
        Assert.True(viewModel.CanRequestRemovalPlan);

        await viewModel.RequestRemovalPlanCommand.ExecuteAsync(null);

        Assert.NotNull(created);
        Assert.Equal("file:0", Assert.Single(factory.Removed).Id);
        Assert.Equal("file:1", Assert.Single(factory.Kept).Id);
        Assert.Single(viewModel.VisibleGroups);

        viewModel.ApplyExecutedChangePlan(created!);
        Assert.Empty(viewModel.VisibleGroups);
        Assert.False(viewModel.HasDuplicateGroups);
        Assert.Contains("moved to the recovery area", viewModel.Status.Message, StringComparison.Ordinal);

        viewModel.RevertExecutedChangePlan(created!);
        Assert.Single(viewModel.VisibleGroups);
        Assert.True(viewModel.HasDuplicateGroups);
    }

    /// <summary>Verifies selection gates cannot create a plan that removes every known copy.</summary>
    [Fact]
    public void RequestRemovalPlan_AllMembersSelected_IsDisabled()
    {
        using var viewModel = new DuplicateReviewViewModel(null, new RecordingRemovalPlanFactory());
        viewModel.LoadSnapshot(CreateSnapshot("session:safety", 2));
        viewModel.SelectedGroup = Assert.Single(viewModel.VisibleGroups);
        foreach (var row in viewModel.MemberRows)
        {
            row.IsSelected = true;
        }

        Assert.False(viewModel.CanRequestRemovalPlan);
        Assert.False(viewModel.RequestRemovalPlanCommand.CanExecute(null));
    }

    /// <summary>Verifies selections persist across groups and produce one keeper-safe review plan.</summary>
    [Fact]
    public async Task RequestRemovalPlan_MultipleGroupsCombinesSelectionsAndPreservesEachKeeper()
    {
        var factory = new RecordingRemovalPlanFactory();
        using var viewModel = new DuplicateReviewViewModel(null, factory);
        viewModel.LoadSnapshot(CreateMultiGroupSnapshot());
        ChangePlan? created = null;
        viewModel.ChangePlanCreated += (_, plan) => created = plan;

        viewModel.SelectedGroup = viewModel.VisibleGroups[0];
        viewModel.MemberRows[0].IsSelected = true;
        viewModel.SelectedGroup = viewModel.VisibleGroups[1];
        viewModel.MemberRows[0].IsSelected = true;

        Assert.Equal(2, viewModel.PendingRemovalCount);
        Assert.Equal(2, viewModel.AffectedRemovalGroupCount);
        Assert.True(viewModel.CanRequestRemovalPlan);

        await viewModel.RequestRemovalPlanCommand.ExecuteAsync(null);

        Assert.NotNull(created);
        Assert.Equal(2, factory.Removed.Count);
        Assert.Equal(2, factory.Kept.Count);
        Assert.Equal(2, factory.Kept.Select(file => file.DuplicateGroupId).Distinct(StringComparer.Ordinal).Count());

        viewModel.ApplyExecutedChangePlan(created!);
        Assert.Empty(viewModel.VisibleGroups);
    }

    /// <summary>Verifies cancelling a combined request emits no plan and preserves the explicit selections for retry.</summary>
    [Fact]
    public async Task RequestRemovalPlan_MultipleGroupsCanBeCancelledWithoutLosingSelections()
    {
        var factory = new RecordingRemovalPlanFactory { Block = true };
        using var viewModel = new DuplicateReviewViewModel(null, factory);
        viewModel.LoadSnapshot(CreateMultiGroupSnapshot());
        var plansCreated = 0;
        viewModel.ChangePlanCreated += (_, _) => plansCreated++;

        viewModel.SelectedGroup = viewModel.VisibleGroups[0];
        viewModel.MemberRows[0].IsSelected = true;
        viewModel.SelectedGroup = viewModel.VisibleGroups[1];
        viewModel.MemberRows[0].IsSelected = true;

        var running = viewModel.RequestRemovalPlanCommand.ExecuteAsync(null);
        await factory.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.CancelRemovalPlanCommand.Execute(null);
        await running;

        Assert.Equal(0, plansCreated);
        Assert.Equal(2, viewModel.PendingRemovalCount);
        Assert.Equal(2, viewModel.AffectedRemovalGroupCount);
        Assert.Equal(StatusKind.Information, viewModel.Status.Kind);
        Assert.Contains("cancelled", viewModel.Status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, viewModel.VisibleGroups.Count);
    }

    /// <summary>Verifies replacing Results context cancels launch work tied to the previous snapshot.</summary>
    [Fact]
    public async Task LoadSnapshot_NewIdentity_CancelsActiveLauncherLoop()
    {
        var launcher = new RecordingLauncher { Block = true };
        using var viewModel = new DuplicateReviewViewModel(launcher);
        viewModel.LoadSnapshot(CreateSnapshot("session:one", 2));
        viewModel.SelectedGroup = Assert.Single(viewModel.VisibleGroups);

        var running = viewModel.OpenBothFilesCommand.ExecuteAsync(null);
        await launcher.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.LoadSnapshot(CreateSnapshot("session:two", 2));
        await running;

        Assert.False(viewModel.IsOpening);
        Assert.Null(viewModel.SelectedGroup);
        Assert.Single(launcher.OpenedFiles);
    }

    /// <summary>Verifies filter and group state survive the same snapshot but clear for a new snapshot identity.</summary>
    [Fact]
    public void LoadSnapshot_SameIdentityPreservesState_NewIdentityClearsIt()
    {
        var first = CreateSnapshot("session:one", 3);
        using var viewModel = new DuplicateReviewViewModel();
        viewModel.LoadSnapshot(first);
        viewModel.SelectedGroup = Assert.Single(viewModel.VisibleGroups);
        viewModel.FilterText = "no match";

        viewModel.LoadSnapshot(first);
        Assert.Equal("no match", viewModel.FilterText);
        Assert.NotNull(viewModel.SelectedGroup);

        viewModel.LoadSnapshot(CreateSnapshot("session:two", 2));
        Assert.Null(viewModel.FilterText);
        Assert.Null(viewModel.SelectedGroup);
    }

    private static ResultsSnapshot CreateSnapshot(string sessionId, int memberCount)
    {
        const string groupId = "group:one";
        var files = Enumerable.Range(0, memberCount)
            .Select(index => CreateFile($"file:{index}", $"C:\\Duplicates\\file-{index}.txt", groupId))
            .ToArray();
        var group = new ResultDuplicateGroup(
            groupId,
            1,
            files.Select(file => file.Id).ToArray(),
            memberCount,
            10,
            10L * (memberCount - 1));
        return new ResultsSnapshot(
            sessionId,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            files,
            [new ResultDirectory("C:\\Duplicates", "Duplicates")],
            [group],
            [],
            [],
            new ResultsSnapshotStatistics(memberCount, 1, 1, memberCount, 0, 0, 0),
            true);
    }

    private static ResultsSnapshot CreateMultiGroupSnapshot()
    {
        var files = new[]
        {
            CreateFile("one:a", "C:\\Duplicates\\one-a.txt", "group:one"),
            CreateFile("one:b", "C:\\Duplicates\\one-b.txt", "group:one"),
            CreateFile("two:a", "C:\\Duplicates\\two-a.txt", "group:two"),
            CreateFile("two:b", "C:\\Duplicates\\two-b.txt", "group:two"),
        };
        var groups = new[]
        {
            new ResultDuplicateGroup("group:one", 1, ["one:a", "one:b"], 2, 10, 10),
            new ResultDuplicateGroup("group:two", 2, ["two:a", "two:b"], 2, 10, 10),
        };
        return new ResultsSnapshot(
            "session:multi",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            files,
            [new ResultDirectory("C:\\Duplicates", "Duplicates")],
            groups,
            [],
            [],
            new ResultsSnapshotStatistics(4, 1, 2, 4, 0, 0, 0),
            true);
    }

    private static ResultFile CreateFile(string id, string path, string groupId) => new(
        id,
        path,
        CrossPlatformPath.GetFileName(path),
        Path.GetExtension(path),
        10,
        DateTimeOffset.UnixEpoch,
        FileCategory.Document,
        "Document",
        DuplicateStatus.Duplicate,
        groupId,
        false);

    private sealed class RecordingLauncher : IExternalFileLauncher
    {
        public List<string> OpenedFiles { get; } = [];

        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? FailPath { get; init; }

        public bool Block { get; init; }

        public async Task<ExternalLaunchResult> OpenFileAsync(string fullPath, CancellationToken cancellationToken)
        {
            OpenedFiles.Add(fullPath);
            Started.TrySetResult(true);
            if (Block)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return string.Equals(fullPath, FailPath, StringComparison.Ordinal)
                ? ExternalLaunchResult.Failure("Missing")
                : ExternalLaunchResult.Success("Opened");
        }

        public Task<ExternalLaunchResult> OpenContainingFolderAsync(string fullPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ExternalLaunchResult.Success("Opened"));
        }
    }

    private sealed class RecordingRemovalPlanFactory : IDuplicateRemovalPlanFactory
    {
        public IReadOnlyList<ResultFile> Removed { get; private set; } = [];
        public IReadOnlyList<ResultFile> Kept { get; private set; } = [];
        public bool Block { get; init; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ChangePlan> CreateDuplicateRemovalPlanAsync(
            IReadOnlyList<ResultFile> filesToRemove,
            IReadOnlyList<ResultFile> filesToKeep,
            string? sourceScanId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Removed = filesToRemove;
            Kept = filesToKeep;
            Started.TrySetResult(true);
            if (Block)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var actions = filesToRemove.Select((file, index) => new ProposedChangeAction(
                $"action:{index}",
                "plan:duplicate",
                ChangeActionType.MoveFile,
                file.FullPath,
                $"C:\\Duplicates\\.opensorse\\duplicate-recovery\\{file.DisplayFileName}",
                file.DisplayFileName,
                file.DisplayFileName,
                new FileIdentitySnapshot(file.Id, file.SizeInBytes ?? 0, file.LastWriteTimeUtc ?? DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null),
                ChangeSuggestionSource.DuplicateAnalysis,
                "Safe duplicate removal.",
                ChangeValidationState.Valid,
                ChangeApprovalState.Approved,
                index + 1,
                [],
                [],
                false,
                null,
                null)).ToArray();
            return new ChangePlan(
                ChangePlanSchema.CurrentVersion,
                "plan:duplicate",
                DateTimeOffset.UnixEpoch,
                sourceScanId,
                "C:\\Duplicates",
                ChangePlanStatus.Approved,
                actions,
                [],
                DateTimeOffset.UnixEpoch,
                false);
        }
    }
}
