using System.Text.Json;
using OpenSorSe.Core.Persistence;

namespace OpenSorSe.Core.Tests.Persistence;

/// <summary>Verifies bounded atomic persistence and process-local file coordination.</summary>
public sealed class AtomicJsonFileTests
{
    /// <summary>Verifies a complete JSON document replaces the destination without leaving temporary siblings.</summary>
    [Fact]
    public async Task WriteAsync_ValidDocument_ReplacesDestinationAndCleansTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathOf("state.json");
        await File.WriteAllTextAsync(path, """{"value":"old"}""");

        await AtomicJsonFile.WriteAsync(
            path,
            new Document("new"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            1024,
            CancellationToken.None);

        var reloaded = JsonSerializer.Deserialize<Document>(
            await File.ReadAllTextAsync(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("new", reloaded?.Value);
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    /// <summary>Verifies a failed partial serialization preserves the previous valid destination.</summary>
    [Fact]
    public async Task WriteAsync_SerializationFailure_PreservesDestinationAndCleansTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathOf("state.json");
        const string original = """{"value":"healthy"}""";
        await File.WriteAllTextAsync(path, original);
        var cyclic = new CyclicDocument();
        cyclic.Next = cyclic;

        await Assert.ThrowsAsync<JsonException>(() => AtomicJsonFile.WriteAsync(
            path,
            cyclic,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            1024,
            CancellationToken.None));

        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    /// <summary>Verifies capacity rejection uses the owning store's exception and preserves prior state.</summary>
    [Fact]
    public async Task WriteAsync_OversizedDocument_PreservesDestinationAndUsesExceptionFactory()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathOf("state.json");
        const string original = """{"value":"healthy"}""";
        await File.WriteAllTextAsync(path, original);

        var exception = await Assert.ThrowsAsync<CapacityException>(() => AtomicJsonFile.WriteAsync(
            path,
            new Document(new string('x', 256)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            32,
            CancellationToken.None,
            static (actual, maximum) => new CapacityException(actual, maximum)));

        Assert.True(exception.ActualBytes > exception.MaximumBytes);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    /// <summary>Verifies a pre-cancelled write cannot create a directory or touch a destination.</summary>
    [Fact]
    public async Task WriteAsync_PreCancelled_LeavesFilesystemUntouched()
    {
        using var directory = new TemporaryDirectory();
        var parent = directory.PathOf("not-created");
        var path = Path.Combine(parent, "state.json");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => AtomicJsonFile.WriteAsync(
            path,
            new Document("value"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            1024,
            cancellation.Token));

        Assert.False(Directory.Exists(parent));
    }

    /// <summary>Verifies separate coordinators for the same normalized path share one exclusive gate.</summary>
    [Fact]
    public async Task Coordinator_SeparateInstancesForSamePath_SerializeAccess()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathOf("state.json");
        var first = new ApplicationFileAccessCoordinator(path);
        var second = new ApplicationFileAccessCoordinator(Path.GetFullPath(path));
        using var firstLease = await first.AcquireAsync(CancellationToken.None);

        var secondAcquisition = second.AcquireAsync(CancellationToken.None).AsTask();
        await Task.Delay(50);
        Assert.False(secondAcquisition.IsCompleted);

        firstLease.Dispose();
        using var secondLease = await secondAcquisition.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies cancellation while waiting does not corrupt or permanently retain the path gate.</summary>
    [Fact]
    public async Task Coordinator_CancelledWait_DoesNotConsumeGate()
    {
        using var directory = new TemporaryDirectory();
        var coordinator = new ApplicationFileAccessCoordinator(directory.PathOf("state.json"));
        using var lease = await coordinator.AcquireAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var waiting = coordinator.AcquireAsync(cancellation.Token).AsTask();

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => waiting);
        lease.Dispose();

        using var subsequent = await coordinator.AcquireAsync(CancellationToken.None)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies public persistence helpers reject ambiguous relative paths.</summary>
    [Fact]
    public async Task PersistenceHelpers_RelativePaths_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new ApplicationFileAccessCoordinator("state.json"));
        await Assert.ThrowsAsync<ArgumentException>(() => AtomicJsonFile.WriteAsync(
            "state.json",
            new Document("value"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            1024,
            CancellationToken.None));
    }

    private sealed record Document(string Value);

    private sealed class CyclicDocument
    {
        public CyclicDocument? Next { get; set; }
    }

    private sealed class CapacityException(long actualBytes, long maximumBytes) : Exception
    {
        public long ActualBytes { get; } = actualBytes;
        public long MaximumBytes { get; } = maximumBytes;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateDirectory(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"opensorse-atomic-json-{Guid.NewGuid():N}")).FullName;
        }

        public string Path { get; }

        public string PathOf(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            var fullPath = System.IO.Path.GetFullPath(Path);
            Assert.StartsWith(
                System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()),
                fullPath,
                StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
    }
}
