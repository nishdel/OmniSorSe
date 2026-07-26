using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSorSe.Application.Structure;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies preview-first restructuring, repeat protection, history, and comparison.</summary>
public sealed class StructureHistoryTests
{
    /// <summary>Verifies preview creates durable review state without changing source files.</summary>
    [Fact]
    public async Task Preview_RecordsProposalWithoutMutationOrProtection()
    {
        using var temporary = new TemporaryDirectory();
        temporary.Write("invoice.pdf", "invoice");
        var history = new MemoryHistoryStore();
        var service = CreateService(history);

        var first = await service.PreviewAsync(temporary.Path, false, CancellationToken.None);
        var second = await service.PreviewAsync(temporary.Path, false, CancellationToken.None);

        Assert.True(first.HasProposal);
        Assert.Equal(RestructuringProtectionState.FirstRun, first.ProtectionState);
        Assert.True(File.Exists(temporary.PathFor("invoice.pdf")));
        Assert.False(Directory.Exists(temporary.PathFor("Documents")));
        Assert.All(history.Records, record => Assert.Equal(RestructuringStatus.Previewed, record.Status));
        Assert.Equal(RestructuringProtectionState.PreviousIncomplete, second.ProtectionState);
    }

    /// <summary>Verifies disabled Advanced mode blocks even a direct service invocation before snapshot/history work.</summary>
    [Fact]
    public async Task Preview_AdvancedDisabled_PerformsNoHistoryOrFilesystemWork()
    {
        using var temporary = new TemporaryDirectory();
        temporary.Write("invoice.pdf", "invoice");
        var history = new MemoryHistoryStore();
        var snapshots = new CountingSnapshotService();
        var service = new FolderRestructuringService(
            snapshots,
            history,
            new Configuration(advancedEnabled: false));

        var result = await service.PreviewAsync(
            temporary.Path,
            false,
            CancellationToken.None);

        Assert.False(result.HasProposal);
        Assert.Equal(0, snapshots.CaptureCount);
        Assert.Empty(history.Records);
        Assert.True(File.Exists(temporary.PathFor("invoice.pdf")));
    }

    /// <summary>Verifies exact confirmation is required before a preview can mutate files.</summary>
    [Fact]
    public async Task Apply_MismatchedConfirmation_IsRejectedWithoutMutation()
    {
        using var temporary = new TemporaryDirectory();
        temporary.Write("photo.jpg", "photo");
        var service = CreateService(new MemoryHistoryStore());
        var plan = Assert.IsType<RestructuringPlan>(
            (await service.PreviewAsync(temporary.Path, false, CancellationToken.None)).Plan);

        var result = await service.ApplyAsync(plan, "wrong-preview", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RestructuringApprovalState.Rejected, result.Record.ApprovalState);
        Assert.True(File.Exists(temporary.PathFor("photo.jpg")));
        Assert.False(File.Exists(temporary.PathFor("Images", "photo.jpg")));
    }

    /// <summary>Verifies a confirmed plan moves only reviewed files and records an applied snapshot.</summary>
    [Fact]
    public async Task Apply_ConfirmedPreview_MovesBoundedFilesAndRecordsSuccess()
    {
        using var temporary = new TemporaryDirectory();
        temporary.Write("invoice.pdf", "invoice");
        temporary.Write("photo.jpg", "photo");
        var history = new MemoryHistoryStore();
        var service = CreateService(history);
        var plan = Assert.IsType<RestructuringPlan>(
            (await service.PreviewAsync(temporary.Path, false, CancellationToken.None)).Plan);

        var result = await service.ApplyAsync(plan, plan.OperationId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(RestructuringStatus.Applied, result.Status);
        Assert.NotNull(result.Record.AppliedSnapshot);
        Assert.True(File.Exists(temporary.PathFor("Documents", "invoice.pdf")));
        Assert.True(File.Exists(temporary.PathFor("Images", "photo.jpg")));
        Assert.False(File.Exists(temporary.PathFor("invoice.pdf")));
        Assert.Equal(2, result.Record.ItemOutcomes.Count);
        Assert.All(result.Record.ItemOutcomes, outcome =>
            Assert.Equal(RestructuringItemStatus.Succeeded, outcome.Status));
    }

    /// <summary>Verifies an unchanged successfully organized root suppresses redundant full proposals.</summary>
    [Fact]
    public async Task Preview_AfterSuccessfulApply_IsAlreadyOrganized()
    {
        using var temporary = new TemporaryDirectory();
        temporary.Write("invoice.pdf", "invoice");
        var history = new MemoryHistoryStore();
        var service = CreateService(history);
        var plan = Assert.IsType<RestructuringPlan>(
            (await service.PreviewAsync(temporary.Path, false, CancellationToken.None)).Plan);
        Assert.True((await service.ApplyAsync(plan, plan.OperationId, CancellationToken.None)).Succeeded);

        var repeat = await service.PreviewAsync(temporary.Path, false, CancellationToken.None);

        Assert.False(repeat.HasProposal);
        Assert.Equal(RestructuringProtectionState.AlreadyOrganized, repeat.ProtectionState);
        Assert.Contains("already", repeat.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies only new root-level files receive an incremental proposal.</summary>
    [Fact]
    public async Task Preview_NewFileAfterApply_IsIncremental()
    {
        using var temporary = new TemporaryDirectory();
        temporary.Write("invoice.pdf", "invoice");
        var history = new MemoryHistoryStore();
        var service = CreateService(history);
        var first = Assert.IsType<RestructuringPlan>(
            (await service.PreviewAsync(temporary.Path, false, CancellationToken.None)).Plan);
        Assert.True((await service.ApplyAsync(first, first.OperationId, CancellationToken.None)).Succeeded);
        temporary.Write("new-photo.jpg", "photo");

        var incremental = await service.PreviewAsync(temporary.Path, false, CancellationToken.None);

        Assert.True(incremental.HasProposal);
        Assert.Equal(RestructuringProtectionState.NewFilesOnly, incremental.ProtectionState);
        var plan = Assert.IsType<RestructuringPlan>(incremental.Plan);
        Assert.True(plan.IsIncremental);
        Assert.Equal("new-photo.jpg", Assert.Single(plan.Moves).SourceRelativePath);
    }

    /// <summary>Verifies manual edits to previously applied files are detected rather than suppressed.</summary>
    [Fact]
    public async Task Preview_ManualChangeAfterApply_IsMateriallyChanged()
    {
        using var temporary = new TemporaryDirectory();
        temporary.Write("invoice.pdf", "invoice");
        var history = new MemoryHistoryStore();
        var service = CreateService(history);
        var first = Assert.IsType<RestructuringPlan>(
            (await service.PreviewAsync(temporary.Path, false, CancellationToken.None)).Plan);
        Assert.True((await service.ApplyAsync(first, first.OperationId, CancellationToken.None)).Succeeded);
        File.Move(
            temporary.PathFor("Documents", "invoice.pdf"),
            temporary.PathFor("invoice-renamed.pdf"));

        var changed = await service.PreviewAsync(temporary.Path, false, CancellationToken.None);

        Assert.Equal(RestructuringProtectionState.MateriallyChanged, changed.ProtectionState);
        Assert.True(changed.HasProposal);
    }

    /// <summary>Verifies an explicit override bypasses protection while retaining an honest no-change result.</summary>
    [Fact]
    public async Task Preview_ExplicitOverride_BypassesProtection()
    {
        using var temporary = new TemporaryDirectory();
        temporary.Write("invoice.pdf", "invoice");
        var history = new MemoryHistoryStore();
        var service = CreateService(history);
        var first = Assert.IsType<RestructuringPlan>(
            (await service.PreviewAsync(temporary.Path, false, CancellationToken.None)).Plan);
        Assert.True((await service.ApplyAsync(first, first.OperationId, CancellationToken.None)).Succeeded);

        var overridden = await service.PreviewAsync(temporary.Path, true, CancellationToken.None);

        Assert.Equal(RestructuringProtectionState.AlreadyOrganized, overridden.ProtectionState);
        Assert.False(overridden.HasProposal);
        Assert.Contains("override", overridden.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies a root change after preview fails closed before any reviewed move begins.</summary>
    [Fact]
    public async Task Apply_ChangedAfterPreview_FailsClosed()
    {
        using var temporary = new TemporaryDirectory();
        temporary.Write("invoice.pdf", "invoice");
        var service = CreateService(new MemoryHistoryStore());
        var plan = Assert.IsType<RestructuringPlan>(
            (await service.PreviewAsync(temporary.Path, false, CancellationToken.None)).Plan);
        temporary.Write("later.txt", "later");

        var result = await service.ApplyAsync(plan, plan.OperationId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RestructuringStatus.Failed, result.Status);
        Assert.True(File.Exists(temporary.PathFor("invoice.pdf")));
        Assert.False(File.Exists(temporary.PathFor("Documents", "invoice.pdf")));
    }

    /// <summary>Verifies a tampered traversal destination is rejected and cannot escape the root.</summary>
    [Fact]
    public async Task Apply_TraversalDestination_IsRejectedBeforeMutation()
    {
        using var temporary = new TemporaryDirectory();
        temporary.Write("invoice.pdf", "invoice");
        var service = CreateService(new MemoryHistoryStore());
        var plan = Assert.IsType<RestructuringPlan>(
            (await service.PreviewAsync(temporary.Path, false, CancellationToken.None)).Plan);
        var tampered = plan with
        {
            Moves = [new RestructuringMove("invoice.pdf", Path.Combine("..", "escaped.pdf"))],
        };
        var escaped = Path.GetFullPath(Path.Combine(temporary.Path, "..", "escaped.pdf"));
        if (File.Exists(escaped))
        {
            File.Delete(escaped);
        }

        var result = await service.ApplyAsync(
            tampered,
            tampered.OperationId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(temporary.PathFor("invoice.pdf")));
        Assert.False(File.Exists(escaped));
    }

    /// <summary>Verifies cancellation returns a controlled record and leaves source files in place.</summary>
    [Fact]
    public async Task Apply_CancelledBeforeMutation_RecordsCancellation()
    {
        using var temporary = new TemporaryDirectory();
        temporary.Write("invoice.pdf", "invoice");
        var service = CreateService(new MemoryHistoryStore());
        var plan = Assert.IsType<RestructuringPlan>(
            (await service.PreviewAsync(temporary.Path, false, CancellationToken.None)).Plan);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.ApplyAsync(plan, plan.OperationId, cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(RestructuringStatus.Cancelled, result.Status);
        Assert.True(File.Exists(temporary.PathFor("invoice.pdf")));
    }

    /// <summary>Verifies different roots never inherit repeat protection from one another.</summary>
    [Fact]
    public async Task Preview_UnrelatedRoot_RemainsFirstRun()
    {
        using var firstRoot = new TemporaryDirectory();
        using var secondRoot = new TemporaryDirectory();
        firstRoot.Write("one.txt", "one");
        secondRoot.Write("two.txt", "two");
        var history = new MemoryHistoryStore();
        var service = CreateService(history);
        var plan = Assert.IsType<RestructuringPlan>(
            (await service.PreviewAsync(firstRoot.Path, false, CancellationToken.None)).Plan);
        Assert.True((await service.ApplyAsync(plan, plan.OperationId, CancellationToken.None)).Succeeded);

        var other = await service.PreviewAsync(secondRoot.Path, false, CancellationToken.None);

        Assert.Equal(RestructuringProtectionState.FirstRun, other.ProtectionState);
    }

    /// <summary>Verifies the JSON history store round-trips, replaces lifecycle state, and clears safely.</summary>
    [Fact]
    public async Task JsonStore_RoundTripsUpsertAndClear()
    {
        using var temporary = new TemporaryDirectory();
        using var appData = new TemporaryDirectory();
        temporary.Write("invoice.pdf", "invoice");
        var store = new JsonStructureHistoryStore(
            appData.PathFor("history.json"),
            new Logging());
        var service = new FolderRestructuringService(
            new FolderStructureSnapshotService(),
            store,
            new Configuration(advancedEnabled: true));
        var preview = Assert.IsType<RestructuringPlan>(
            (await service.PreviewAsync(temporary.Path, false, CancellationToken.None)).Plan);

        Assert.Single(await store.ListAsync(CancellationToken.None));
        Assert.True((await service.ApplyAsync(preview, preview.OperationId, CancellationToken.None)).Succeeded);
        var applied = Assert.Single(await store.ListAsync(CancellationToken.None));
        Assert.Equal(RestructuringStatus.Applied, applied.Status);

        await store.ClearAsync(CancellationToken.None);
        Assert.Empty(await store.ListAsync(CancellationToken.None));
        Assert.True(File.Exists(temporary.PathFor("Documents", "invoice.pdf")));
    }

    /// <summary>Verifies absent or corrupt optional history cannot activate protection.</summary>
    [Fact]
    public async Task JsonStore_MissingOrCorruptHistory_RecoversEmpty()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.PathFor("history.json");
        var store = new JsonStructureHistoryStore(path, new Logging());
        Assert.Empty(await store.ListAsync(CancellationToken.None));

        await File.WriteAllTextAsync(path, "{ corrupt");

        Assert.Empty(await store.ListAsync(CancellationToken.None));
    }

    /// <summary>Verifies comparison identifies added, removed, moved, renamed, and unchanged nodes.</summary>
    [Fact]
    public void Comparison_ClassifiesAllSupportedChanges()
    {
        var before = Snapshot(
            Node("same.txt", "same"),
            Node("old.txt", "removed"),
            Node(Path.Combine("A", "move.txt"), "move"),
            Node(Path.Combine("A", "rename.txt"), "rename"));
        var after = Snapshot(
            Node("same.txt", "same"),
            Node("new.txt", "added"),
            Node(Path.Combine("B", "move.txt"), "move"),
            Node(Path.Combine("A", "renamed.txt"), "rename"));

        var changes = new StructureComparisonService().Compare(before, after);

        Assert.Contains(changes, change => change.Kind == StructureChangeKind.Unchanged);
        Assert.Contains(changes, change => change.Kind == StructureChangeKind.Added);
        Assert.Contains(changes, change => change.Kind == StructureChangeKind.Removed);
        Assert.Contains(changes, change => change.Kind == StructureChangeKind.Moved);
        Assert.Contains(changes, change => change.Kind == StructureChangeKind.Renamed);
    }

    private static FolderRestructuringService CreateService(IStructureHistoryStore history) =>
        new(
            new FolderStructureSnapshotService(),
            history,
            new Configuration(advancedEnabled: true));

    private static StructureNode Node(string path, string identity) =>
        new(path, false, 1, DateTimeOffset.UnixEpoch, identity);

    private static FolderStructureSnapshot Snapshot(params StructureNode[] nodes) =>
        new(
            Path.GetFullPath(Path.GetTempPath()),
            "root",
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UnixEpoch,
            nodes);

    private sealed class MemoryHistoryStore : IStructureHistoryStore
    {
        public List<RestructuringHistoryRecord> Records { get; } = [];

        public Task<IReadOnlyList<RestructuringHistoryRecord>> ListAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RestructuringHistoryRecord>>(
                Records.OrderByDescending(record => record.StartedAtUtc).ToArray());

        public Task UpsertAsync(
            RestructuringHistoryRecord record,
            CancellationToken cancellationToken)
        {
            Records.RemoveAll(candidate => candidate.OperationId == record.OperationId);
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            Records.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class CountingSnapshotService : IFolderStructureSnapshotService
    {
        public int CaptureCount { get; private set; }

        public Task<FolderStructureSnapshot> CaptureAsync(
            string rootPath,
            CancellationToken cancellationToken)
        {
            CaptureCount++;
            throw new InvalidOperationException("Disabled restructuring must not capture a root.");
        }
    }

    private sealed class Configuration(bool advancedEnabled) : IConfigurationService
    {
        public ApplicationSettings Current { get; private set; } = new()
        {
            Features = new FeatureSettings { ShowAdvancedFeatures = advancedEnabled },
        };

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class Logging : ILoggingService
    {
        public void Initialize(LogLevel minimumLevel) { }
        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;
        public void Dispose() { }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"opensorse-structure-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string PathFor(params string[] parts) =>
            parts.Aggregate(Path, System.IO.Path.Combine);

        public void Write(string relativePath, string content)
        {
            var path = PathFor(relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
