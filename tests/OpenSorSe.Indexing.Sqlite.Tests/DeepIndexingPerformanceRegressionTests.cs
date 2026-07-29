using System.Diagnostics;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Indexing.Sqlite.Tests;

/// <summary>Runs bounded synthetic throughput checks separately identifiable from functional tests.</summary>
public sealed class DeepIndexingPerformanceRegressionTests
{
    /// <summary>Verifies batched durable queue writes stay bounded as synthetic source size increases.</summary>
    [Theory]
    [Trait("Category", "PerformanceRegression")]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    public async Task BatchedDiscoveryWritesRemainResponsive(int fileCount)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "OpenSorSe-index-performance",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "deep-index.db");
        try
        {
            await using var store = new SqliteDeepIndexStore(
                databasePath,
                PlatformServices.CurrentPathSemantics);
            await store.InitializeAsync();
            var source = new IndexingSource(
                "synthetic",
                root,
                "Synthetic performance source",
                IndexingLevel.Basic,
                true,
                true,
                0,
                []);
            await store.UpsertSourceAsync(source);
            var runId = await store.BeginRunAsync(source.Id, DateTimeOffset.UnixEpoch);
            var observations = Enumerable.Range(0, fileCount)
                .Select(index => new IndexingFileObservation(
                    Path.Combine(root, $"file-{index:D8}.txt"),
                    $"file-{index:D8}.txt",
                    $"identity-{index:D8}",
                    "synthetic-volume",
                    1024 + index,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    FileAttributes.Normal,
                    $"metadata-{index:D8}"))
                .ToArray();

            var stopwatch = Stopwatch.StartNew();
            foreach (var batch in observations.Chunk(256))
            {
                await store.EnqueueDiscoveredFilesAsync(runId, batch, "performance-processor", 3);
            }

            await store.CompleteDiscoveryAsync(
                runId,
                new HashSet<string>(observations.Select(item => item.RelativePath)),
                DateTimeOffset.UnixEpoch);
            stopwatch.Stop();

            var progress = await store.GetProgressAsync(1024L * 1024 * 1024, DateTimeOffset.UnixEpoch);
            Assert.Equal(fileCount, progress.TotalDiscovered);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(30),
                $"{fileCount:N0} synthetic durable queue writes took {stopwatch.Elapsed}.");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
