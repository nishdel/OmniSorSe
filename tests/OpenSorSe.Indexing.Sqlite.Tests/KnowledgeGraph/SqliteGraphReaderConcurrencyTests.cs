using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

namespace OpenSorSe.Indexing.Sqlite.Tests.KnowledgeGraph;

/// <summary>Verifies bounded WAL readers remain independent from graph writer admission.</summary>
public sealed class SqliteGraphReaderConcurrencyTests
{
    /// <summary>A held internal writer admission permit does not block an independent read-only query.</summary>
    [Fact]
    public async Task Query_UsesIndependentReadAdmissionWhileWriterGateIsHeld()
    {
        using var directory = new TemporaryDirectory();
        await using var lifecycle = new SqliteGraphStorageLifecycle(directory.Root);
        await lifecycle.ProvisionAsync();
        await using var store = new SqliteGraphStore(lifecycle.GraphDatabasePath);
        await store.InitializeAsync();
        var writerGate = ReadGate(store, "_gate");
        await writerGate.WaitAsync();
        try
        {
            var page = await store.GetNodesAsync(new GraphNodeQuery()).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Empty(page.Items);
            Assert.Null(page.NextCursor);
        }
        finally
        {
            writerGate.Release();
        }
    }

    /// <summary>Disposal waits for admitted readers and repeated disposal observes the same completion.</summary>
    [Fact]
    public async Task DisposeAsync_DrainsAdmittedReadersBeforeClosingStore()
    {
        using var directory = new TemporaryDirectory();
        await using var lifecycle = new SqliteGraphStorageLifecycle(directory.Root);
        await lifecycle.ProvisionAsync();
        var store = new SqliteGraphStore(lifecycle.GraphDatabasePath);
        await store.InitializeAsync();
        var readerGate = ReadGate(store, "_readerGate");
        await readerGate.WaitAsync();

        var disposal = store.DisposeAsync().AsTask();
        await Task.Yield();
        Assert.False(disposal.IsCompleted);

        readerGate.Release();
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
        await store.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => store.GetNodesAsync(new GraphNodeQuery()));
    }

    /// <summary>A writer already queued at disposal is fenced before it can reopen the graph database.</summary>
    [Fact]
    public async Task GraphStore_DisposeFencesQueuedWriterAndReleasesDatabaseFile()
    {
        using var directory = new TemporaryDirectory();
        await using var lifecycle = new SqliteGraphStorageLifecycle(directory.Root);
        await lifecycle.ProvisionAsync();
        var store = new SqliteGraphStore(lifecycle.GraphDatabasePath);
        await store.InitializeAsync();
        var writerGate = ReadGate(store, "_gate");
        await writerGate.WaitAsync();
        var queued = store.SetEnabledAsync(true, consentConfirmed: true, DateTimeOffset.UnixEpoch);

        var disposal = store.DisposeAsync().AsTask();
        writerGate.Release();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => queued);
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
        File.Delete(lifecycle.GraphDatabasePath);
        Assert.False(File.Exists(lifecycle.GraphDatabasePath));
    }

    /// <summary>A queued decision read is fenced by disposal and cannot retain the decision database handle.</summary>
    [Fact]
    public async Task DecisionStore_DisposeFencesQueuedReadAndReleasesDatabaseFile()
    {
        using var directory = new TemporaryDirectory();
        await using var lifecycle = new SqliteGraphStorageLifecycle(directory.Root);
        await lifecycle.ProvisionAsync();
        var store = new SqliteGraphDecisionStore(lifecycle.DecisionDatabasePath);
        await store.InitializeAsync();
        var gate = Assert.IsType<SemaphoreSlim>(
            typeof(SqliteGraphDecisionStore)
                .GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(store));
        await gate.WaitAsync();
        var queued = store.GetSnapshotAsync();

        var disposal = store.DisposeAsync().AsTask();
        gate.Release();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => queued);
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
        File.Delete(lifecycle.DecisionDatabasePath);
        Assert.False(File.Exists(lifecycle.DecisionDatabasePath));
    }

    /// <summary>A cancelled writer does not wait for the full SQLite busy deadline or damage later writes.</summary>
    [Fact]
    public async Task LockedGraphWrite_CancellationInterruptsBusyWaitAndStoreRecovers()
    {
        using var directory = new TemporaryDirectory();
        await using var lifecycle = new SqliteGraphStorageLifecycle(directory.Root);
        await lifecycle.ProvisionAsync();
        await using var store = new SqliteGraphStore(lifecycle.GraphDatabasePath);
        await store.InitializeAsync();
        using (var journalInspection = new SqliteConnection(
                   $"Data Source={lifecycle.GraphDatabasePath};Mode=ReadOnly;Cache=Private;Pooling=False"))
        {
            journalInspection.Open();
            using var journalCommand = journalInspection.CreateCommand();
            journalCommand.CommandText = "PRAGMA journal_mode;";
            Assert.Equal("wal", Convert.ToString(journalCommand.ExecuteScalar(), CultureInfo.InvariantCulture));
        }

        using var blocker = new SqliteConnection(
            $"Data Source={lifecycle.GraphDatabasePath};Mode=ReadWrite;Cache=Private;Pooling=False");
        blocker.Open();
        using var lockCommand = blocker.CreateCommand();
        lockCommand.CommandText = "BEGIN IMMEDIATE;";
        lockCommand.ExecuteNonQuery();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SetEnabledAsync(
                true,
                consentConfirmed: true,
                DateTimeOffset.UnixEpoch,
                cancellation.Token));
        stopwatch.Stop();
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(4),
            $"Cancellation took {stopwatch.Elapsed}, which exceeded the bounded busy-wait budget.");

        lockCommand.CommandText = "ROLLBACK;";
        lockCommand.ExecuteNonQuery();
        var retry = await store.SetEnabledAsync(true, consentConfirmed: true, DateTimeOffset.UnixEpoch);
        Assert.True(retry.Succeeded, retry.Message);
    }

    /// <summary>An independent writer lock is classified as bounded contention rather than corruption.</summary>
    [Fact]
    public async Task LockedGraphWrite_IsClassifiedBusyWithinFiniteDeadlineAndStoreRecovers()
    {
        using var directory = new TemporaryDirectory();
        await using var lifecycle = new SqliteGraphStorageLifecycle(directory.Root);
        await lifecycle.ProvisionAsync();
        await using var store = new SqliteGraphStore(lifecycle.GraphDatabasePath);
        await store.InitializeAsync();
        using var blocker = new SqliteConnection(
            $"Data Source={lifecycle.GraphDatabasePath};Mode=ReadWrite;Cache=Private;Pooling=False");
        blocker.Open();
        using var lockCommand = blocker.CreateCommand();
        lockCommand.CommandText = "BEGIN IMMEDIATE;";
        lockCommand.ExecuteNonQuery();

        var stopwatch = Stopwatch.StartNew();
        var failure = await Assert.ThrowsAsync<SqliteKnowledgeStoreException>(
            () => store.SetEnabledAsync(true, consentConfirmed: true, DateTimeOffset.UnixEpoch));
        stopwatch.Stop();
        Assert.Equal(SqliteKnowledgeFailureKind.Busy, failure.Kind);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(
                SqliteKnowledgeInfrastructure.BusyTimeoutMilliseconds + 3_000),
            $"Busy classification took {stopwatch.Elapsed}, which exceeded the finite provider deadline.");

        lockCommand.CommandText = "ROLLBACK;";
        lockCommand.ExecuteNonQuery();
        var retry = await store.SetEnabledAsync(true, consentConfirmed: true, DateTimeOffset.UnixEpoch);
        Assert.True(retry.Succeeded, retry.Message);
    }

    /// <summary>Decision-ledger cancellation uses the same bounded lock wait and leaves settings unchanged.</summary>
    [Fact]
    public async Task LockedDecisionWrite_CancellationIsPromptAtomicAndRecoverable()
    {
        using var directory = new TemporaryDirectory();
        await using var lifecycle = new SqliteGraphStorageLifecycle(directory.Root);
        await lifecycle.ProvisionAsync();
        await using var store = new SqliteGraphDecisionStore(lifecycle.DecisionDatabasePath);
        await store.InitializeAsync();
        var current = await store.GetControlSettingsAsync();
        var requested = current with { IsEnabled = true, ConsentConfirmed = true };
        using var blocker = new SqliteConnection(
            $"Data Source={lifecycle.DecisionDatabasePath};Mode=ReadWrite;Cache=Private;Pooling=False");
        blocker.Open();
        using var lockCommand = blocker.CreateCommand();
        lockCommand.CommandText = "BEGIN IMMEDIATE;";
        lockCommand.ExecuteNonQuery();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SetControlSettingsAsync(
                requested,
                current.Revision,
                DateTimeOffset.UnixEpoch,
                cancellation.Token));
        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(4));

        lockCommand.CommandText = "ROLLBACK;";
        lockCommand.ExecuteNonQuery();
        Assert.Equal(current, await store.GetControlSettingsAsync());
        var saved = await store.SetControlSettingsAsync(
            requested,
            current.Revision,
            DateTimeOffset.UnixEpoch);
        Assert.True(saved.IsEnabled);
        Assert.Equal(current.Revision + 1, saved.Revision);
    }

    /// <summary>A read request retains one old-or-new WAL snapshot while a bounded writer commits.</summary>
    [Fact]
    public async Task ReadConnection_KeepsOneSnapshotAcrossConcurrentWriterCommit()
    {
        using var directory = new TemporaryDirectory();
        await using var lifecycle = new SqliteGraphStorageLifecycle(directory.Root);
        await lifecycle.ProvisionAsync();
        await using var store = new SqliteGraphStore(lifecycle.GraphDatabasePath);
        await store.InitializeAsync();
        using var snapshot = OpenReadConnection(store);

        Assert.Equal("0", ReadMeta(snapshot, "enabled"));
        var update = await store.SetEnabledAsync(true, consentConfirmed: true, DateTimeOffset.UnixEpoch);
        Assert.True(update.Succeeded);
        Assert.Equal("0", ReadMeta(snapshot, "enabled"));

        using var current = OpenReadConnection(store);
        Assert.Equal("1", ReadMeta(current, "enabled"));
    }

    private static SemaphoreSlim ReadGate(SqliteGraphStore store, string fieldName) =>
        Assert.IsType<SemaphoreSlim>(
            typeof(SqliteGraphStore)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(store));

    private static SqliteConnection OpenReadConnection(SqliteGraphStore store) =>
        Assert.IsType<SqliteConnection>(
            typeof(SqliteGraphStore)
                .GetMethod("OpenReadConnection", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(store, null));

    private static string? ReadMeta(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM graph_meta WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "opensorse-graph-reader-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        internal string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
