using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Core.Tests;

/// <summary>Verifies restart migration publication, authority preservation and failure recovery.</summary>
public sealed class ApplicationStorageLocationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"omnisorse-storage-{Guid.NewGuid():N}");

    /// <summary>Preserves an existing library when no location override exists.</summary>
    [Fact]
    public async Task DefaultLocationDoesNotForkExistingProfile()
    {
        var paths = Paths();
        using var lease = ProfileOwnershipLease.Acquire(paths.Paths.StateDirectory);
        var resolved = await ApplicationStorageLocation.ResolveAsync(paths, new StorageSettings(), lease);
        Assert.Same(paths, resolved);
        Assert.False(File.Exists(Path.Combine(paths.Paths.ConfigurationDirectory, "storage-location.json")));
    }

    /// <summary>Copies closed stores and their sidecars, leaves source recovery copies, and keeps journal identity.</summary>
    [Fact]
    public async Task MigrationCopiesAuthorityAndRestartReusesActiveGeneration()
    {
        var paths = Paths();
        using var lease = ProfileOwnershipLease.Acquire(paths.Paths.StateDirectory);
        var originals = new Dictionary<string, string>
        {
            [Path.Combine(paths.Paths.DataDirectory, "index", "deep-index.db")] = "catalog with accepted tags",
            [Path.Combine(paths.Paths.DataDirectory, "index", "deep-index.db-wal")] = "closed write ahead log",
            [Path.Combine(paths.Paths.DataDirectory, "index", "knowledge-decisions.db")] = "graph decisions",
            [Path.Combine(paths.Paths.DataDirectory, "decision-history.json")] = "learned user decisions",
            [Path.Combine(paths.Paths.CacheDirectory, "content-index.json")] = "rebuildable content",
        };
        foreach (var original in originals)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(original.Key)!);
            await File.WriteAllTextAsync(original.Key, original.Value);
        }

        var journal = Path.Combine(paths.Paths.StateDirectory, "operation-journal.json");
        await File.WriteAllTextAsync(journal, "undo authority");
        var settings = new StorageSettings { DirectoryPath = Path.Combine(_root, "secondary") };
        var moved = await ApplicationStorageLocation.ResolveAsync(paths, settings, lease);

        Assert.Equal(paths.SettingsFilePath, moved.SettingsFilePath);
        Assert.Equal(paths.Paths.StateDirectory, moved.Paths.StateDirectory);
        Assert.Equal("undo authority", await File.ReadAllTextAsync(journal));
        foreach (var original in originals)
        {
            Assert.Equal(original.Value, await File.ReadAllTextAsync(original.Key));
            var targetRoot = original.Key.EndsWith("content-index.json", StringComparison.Ordinal)
                ? moved.Paths.CacheDirectory : moved.Paths.DataDirectory;
            Assert.Equal(original.Value, await File.ReadAllTextAsync(Path.Combine(targetRoot,
                Path.GetRelativePath(paths.Paths.DataDirectory, original.Key))));
        }

        await File.WriteAllTextAsync(Path.Combine(moved.Paths.DataDirectory, "decision-history.json"), "new active decision");
        var restarted = await ApplicationStorageLocation.ResolveAsync(paths, settings, lease);
        Assert.Equal(moved.Paths, restarted.Paths);
        Assert.Equal("new active decision", await File.ReadAllTextAsync(Path.Combine(restarted.Paths.DataDirectory, "decision-history.json")));
    }

    /// <summary>Returning to the platform root preserves the latest authority and the original recovery copy.</summary>
    [Fact]
    public async Task ReturnToDefaultCopiesCurrentGenerationWithoutOverwritingOriginal()
    {
        var paths = Paths();
        using var lease = ProfileOwnershipLease.Acquire(paths.Paths.StateDirectory);
        var original = Path.Combine(paths.Paths.DataDirectory, "decision-history.json");
        await File.WriteAllTextAsync(original, "original");
        var moved = await ApplicationStorageLocation.ResolveAsync(paths,
            new StorageSettings { DirectoryPath = Path.Combine(_root, "secondary") }, lease);
        await File.WriteAllTextAsync(Path.Combine(moved.Paths.DataDirectory, "decision-history.json"), "latest");

        var returned = await ApplicationStorageLocation.ResolveAsync(paths, new StorageSettings(), lease);
        Assert.Equal("latest", await File.ReadAllTextAsync(Path.Combine(returned.Paths.DataDirectory, "decision-history.json")));
        Assert.Equal("original", await File.ReadAllTextAsync(original));
        Assert.NotEqual(paths.Paths.DataDirectory, returned.Paths.DataDirectory);
    }

    /// <summary>A corrupted receipt never silently opens or replaces the original library.</summary>
    [Fact]
    public async Task CorruptReceiptFailsClosed()
    {
        var paths = Paths();
        using var lease = ProfileOwnershipLease.Acquire(paths.Paths.StateDirectory);
        var receipt = Path.Combine(paths.Paths.ConfigurationDirectory, "storage-location.json");
        await File.WriteAllTextAsync(receipt, "not valid JSON");
        await Assert.ThrowsAsync<IOException>(() => ApplicationStorageLocation.ResolveAsync(paths, new StorageSettings(), lease));
        Assert.Equal("not valid JSON", await File.ReadAllTextAsync(receipt));
    }

    /// <summary>An unavailable custom location never creates a replacement empty library.</summary>
    [Fact]
    public async Task MissingGenerationMarkerFailsClosed()
    {
        var paths = Paths();
        using var lease = ProfileOwnershipLease.Acquire(paths.Paths.StateDirectory);
        var settings = new StorageSettings { DirectoryPath = Path.Combine(_root, "secondary") };
        var moved = await ApplicationStorageLocation.ResolveAsync(paths, settings, lease);
        File.Delete(Path.Combine(Path.GetDirectoryName(moved.Paths.DataDirectory)!, "storage-generation.json"));
        await Assert.ThrowsAsync<IOException>(() => ApplicationStorageLocation.ResolveAsync(paths, settings, lease));
    }

    /// <summary>Losing the active receipt never republishes stale original authority, even after changing the requested root.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingActiveReceiptCannotRestoreStaleOriginal(bool changeRequestedRoot)
    {
        var paths = Paths();
        using var lease = ProfileOwnershipLease.Acquire(paths.Paths.StateDirectory);
        var original = Path.Combine(paths.Paths.DataDirectory, "decision-history.json");
        await File.WriteAllTextAsync(original, "old recovery copy");
        var settings = new StorageSettings { DirectoryPath = Path.Combine(_root, "secondary") };
        var moved = await ApplicationStorageLocation.ResolveAsync(paths, settings, lease);
        var active = Path.Combine(moved.Paths.DataDirectory, "decision-history.json");
        await File.WriteAllTextAsync(active, "latest durable choice");
        var receipt = Path.Combine(paths.Paths.ConfigurationDirectory, "storage-location.json");
        File.Delete(receipt);

        await Assert.ThrowsAsync<IOException>(() => ApplicationStorageLocation.ResolveAsync(paths,
            changeRequestedRoot ? new StorageSettings() : settings, lease));
        Assert.False(File.Exists(receipt));
        Assert.Equal("latest durable choice", await File.ReadAllTextAsync(active));
        Assert.Equal("old recovery copy", await File.ReadAllTextAsync(original));
    }

    /// <summary>Cancellation and inaccessible targets do not publish a new active location.</summary>
    [Fact]
    public async Task FailedOrCancelledCopyDoesNotPublishReceipt()
    {
        var paths = Paths();
        using var lease = ProfileOwnershipLease.Acquire(paths.Paths.StateDirectory);
        var original = Path.Combine(paths.Paths.DataDirectory, "decision-history.json");
        await File.WriteAllTextAsync(original, "preserve me");
        var blocked = Path.Combine(_root, "not-a-directory");
        await File.WriteAllTextAsync(blocked, "unrelated file");
        await Assert.ThrowsAsync<IOException>(() => ApplicationStorageLocation.ResolveAsync(paths,
            new StorageSettings { DirectoryPath = blocked }, lease));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ApplicationStorageLocation.ResolveAsync(paths,
            new StorageSettings { DirectoryPath = Path.Combine(_root, "secondary") }, lease, new CancellationToken(true)));
        Assert.False(File.Exists(Path.Combine(paths.Paths.ConfigurationDirectory, "storage-location.json")));
        Assert.Equal("preserve me", await File.ReadAllTextAsync(original));
        Assert.Equal("unrelated file", await File.ReadAllTextAsync(blocked));
    }

    /// <summary>Migration cannot run without the existing one-writer profile boundary.</summary>
    [Fact]
    public async Task MigrationRequiresProfileOwnership()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => ApplicationStorageLocation.ResolveAsync(Paths(),
            new StorageSettings(), ProfileOwnershipLease.NotRequired));
    }

    /// <summary>The destination cannot become part of the tree being copied or inspected as a cache.</summary>
    [Fact]
    public async Task DestinationInsideOwnedIndexIsRejected()
    {
        var paths = Paths();
        using var lease = ProfileOwnershipLease.Acquire(paths.Paths.StateDirectory);
        await Assert.ThrowsAsync<IOException>(() => ApplicationStorageLocation.ResolveAsync(paths,
            new StorageSettings { DirectoryPath = Path.Combine(paths.Paths.DataDirectory, "index", "nested") }, lease));
        Assert.False(File.Exists(Path.Combine(paths.Paths.ConfigurationDirectory, "storage-location.json")));
    }

    /// <summary>A partial unpublished copy is ignored on retry; only a fully verified generation becomes active.</summary>
    [Fact]
    public async Task PartialCopyRetryPublishesFreshGenerationAndPreservesOriginals()
    {
        var paths = Paths();
        using var lease = ProfileOwnershipLease.Acquire(paths.Paths.StateDirectory);
        Directory.CreateDirectory(Path.Combine(paths.Paths.DataDirectory, "index"));
        var database = Path.Combine(paths.Paths.DataDirectory, "index", "deep-index.db");
        var catalogue = Path.Combine(paths.Paths.DataDirectory, "catalog.json");
        await File.WriteAllTextAsync(database, "first copied store");
        await File.WriteAllTextAsync(catalogue, "blocked later store");
        var settings = new StorageSettings { DirectoryPath = Path.Combine(_root, "secondary") };
        using (new FileStream(catalogue, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() => ApplicationStorageLocation.ResolveAsync(paths, settings, lease));
        }

        Assert.False(File.Exists(Path.Combine(paths.Paths.ConfigurationDirectory, "storage-location.json")));
        var resolved = await ApplicationStorageLocation.ResolveAsync(paths, settings, lease);
        Assert.Equal("first copied store", await File.ReadAllTextAsync(Path.Combine(resolved.Paths.DataDirectory, "index", "deep-index.db")));
        Assert.Equal("blocked later store", await File.ReadAllTextAsync(Path.Combine(resolved.Paths.DataDirectory, "catalog.json")));
        Assert.Equal("first copied store", await File.ReadAllTextAsync(database));
        var generations = Path.GetDirectoryName(Path.GetDirectoryName(resolved.Paths.DataDirectory)!)!;
        Assert.Equal(2, Directory.GetDirectories(generations).Length);
    }

    /// <summary>Existing settings without a storage group retain defaults and unrelated updates preserve overrides.</summary>
    [Fact]
    public async Task StorageSettingsAreBackwardCompatibleAndPreservedByCopies()
    {
        var paths = Paths();
        var configuration = new JsonConfigurationService(paths.SettingsFilePath);
        await File.WriteAllTextAsync(paths.SettingsFilePath, "{\"Features\":{\"ShowAdvancedFeatures\":true}}");
        await configuration.InitializeAsync(CancellationToken.None);
        Assert.Equal(512, configuration.Current.Storage.MaximumCacheSizeMiB);
        var expected = new StorageSettings { DirectoryPath = Path.Combine(_root, "configured"), MaximumCacheSizeMiB = 128 };
        await configuration.SaveAsync(new ApplicationSettings { Storage = expected }, CancellationToken.None);
        await configuration.InitializeAsync(CancellationToken.None);
        var copied = configuration.Current.WithLogging(new LoggingSettings()).WithDiagnostics(new DiagnosticsSettings())
            .WithShellFeatureSwitches(false, true).WithFilesPageDetailsPanelWidthRatio(0.3);
        Assert.Equal(expected.DirectoryPath, copied.Storage.DirectoryPath);
        Assert.Equal(expected.MaximumCacheSizeMiB, copied.Storage.MaximumCacheSizeMiB);
    }

    private IApplicationPathProvider Paths()
    {
        var paths = new ApplicationPathProvider(HostPlatformKind.Windows, _ => null, _root, Path.Combine(_root, "local"));
        paths.EnsureOwnedDirectories();
        return paths;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
