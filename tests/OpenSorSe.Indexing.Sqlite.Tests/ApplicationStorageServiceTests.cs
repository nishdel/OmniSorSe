using Microsoft.Data.Sqlite;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.Models;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.Storage;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Indexing.Sqlite.Tests;

/// <summary>Verifies storage management against real providers and application-owned file layouts.</summary>
public sealed class ApplicationStorageServiceTests
{
    /// <summary>Cleanup preserves durable files and legacy decisions, removes eligible cache data, and reports non-duplicated physical totals.</summary>
    [Fact]
    public async Task CleanupPreservesAuthorityAndActiveTemporaryWork()
    {
        var root = Path.Combine(Path.GetTempPath(), $"omnisorse-storage-service-{Guid.NewGuid():N}");
        var paths = new ApplicationPathProvider(HostPlatformKind.Windows, _ => null, root, Path.Combine(root, "local"));
        paths.EnsureOwnedDirectories();
        try
        {
            var configuration = new JsonConfigurationService(paths.SettingsFilePath);
            await configuration.InitializeAsync(CancellationToken.None);
            using var logging = new LoggingService();
            await using var index = new SqliteDeepIndexStore(Path.Combine(paths.Paths.DataDirectory, "index", "deep-index.db"), PlatformServices.CurrentPathSemantics);
            await index.InitializeAsync();
            var content = new JsonContentStore(Path.Combine(paths.Paths.CacheDirectory, "content-index.json"), logging);
            var semantic = new JsonSemanticIndexStore(Path.Combine(paths.Paths.CacheDirectory, "semantic-index.json"), logging);
            var sourcePath = Path.Combine(root, "original.txt");
            await File.WriteAllTextAsync(sourcePath, "original source content");
            await content.UpsertAsync(new ContentRecord(sourcePath, 23, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                [], "original source content", null, OcrStatus.Skipped, null, [])
            {
                Tags = [new TagAssociation("keep", "file", "Keep", "keep", "Topic", TagSource.AiSuggestion,
                    TagAcceptanceState.Accepted, "User accepted", DateTimeOffset.UnixEpoch)],
            }, CancellationToken.None);
            var authority = new[]
            {
                Path.Combine(paths.Paths.StateDirectory, "operation-journal.json"),
                Path.Combine(paths.Paths.DataDirectory, "decision-history.json"),
                Path.Combine(paths.Paths.DataDirectory, "index", "knowledge-decisions.db"),
            };
            foreach (var file in authority)
            {
                await File.WriteAllTextAsync(file, "durable authority remains");
            }

            var temporary = Path.Combine(paths.Paths.CacheDirectory, "media-temporary");
            Directory.CreateDirectory(temporary);
            var expired = Path.Combine(temporary, "expired.tmp");
            var active = Path.Combine(temporary, "active.tmp");
            await File.WriteAllTextAsync(expired, "abandoned");
            await File.WriteAllTextAsync(active, "still in use");
            File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-30));
            var previews = Path.Combine(paths.Paths.CacheDirectory, "media-thumbnails");
            Directory.CreateDirectory(previews);
            var ownedPreview = Path.Combine(previews, new string('a', 64) + ".png");
            var unknownPreview = Path.Combine(previews, "unrecognized.png");
            await File.WriteAllTextAsync(ownedPreview, "owned preview");
            await File.WriteAllTextAsync(unknownPreview, "do not infer ownership from extension alone");
            var service = new ApplicationStorageService(paths, configuration, index, content, semantic);

            var before = await service.GetUsageAsync();
            Assert.Equal(before.TotalBytes, before.Categories.Where(category => category.IncludedInTotal).Sum(category => category.Bytes));
            await service.ReclaimAsync();

            Assert.False(File.Exists(expired));
            Assert.True(File.Exists(active));
            Assert.False(File.Exists(ownedPreview));
            Assert.True(File.Exists(unknownPreview));
            Assert.Equal("original source content", await File.ReadAllTextAsync(sourcePath));
            Assert.Equal(TagAcceptanceState.Accepted, Assert.Single(Assert.Single(await content.ListAsync(CancellationToken.None)).Tags).AcceptanceState);
            foreach (var file in authority)
            {
                Assert.Equal("durable authority remains", await File.ReadAllTextAsync(file));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}
