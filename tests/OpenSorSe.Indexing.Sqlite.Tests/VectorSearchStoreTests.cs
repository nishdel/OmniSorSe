using Microsoft.Data.Sqlite;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Indexing.Sqlite.Tests;

/// <summary>Checks learned retrieval against authoritative catalog and lifecycle boundaries.</summary>
public sealed class VectorSearchStoreTests
{
    private static readonly SemanticModelIdentity Model = new("ollama", "embedding:test", "digest-1", 2);
    private static readonly DiscoverySearchRequest Query = new("unrelated words", [], 10);

    /// <summary>Semantic top-k considers complete filter eligibility, independently of lexical evidence.</summary>
    [Fact]
    public async Task FiltersApplyBeforeVectorTopK()
    {
        await using var fixture = await Fixture.CreateAsync("strong.pdf", "wanted.txt", "negative.txt");
        await fixture.EmbedAsync("strong.pdf", [1, 0]);
        await fixture.EmbedAsync("wanted.txt", [0.8f, 0.6f]);
        await fixture.EmbedAsync("negative.txt", [-1, 0]);
        var result = await fixture.Store.SearchVectorsAsync(Model, new float[] { 1, 0 },
            new DiscoverySearchRequest("words absent from every filename", [new SearchFilter("extension", SearchFilterKind.Extension, "txt", "Text")], 1));
        Assert.Equal(fixture.Id("wanted.txt"), Assert.Single(result.Matches).FileId);
        Assert.Equal(2, result.EligibleFileCount);
        Assert.Equal(2, result.IndexedFileCount);
    }

    /// <summary>Content/path identity changes fence late model output and remove old retained chunks.</summary>
    [Fact]
    public async Task CatalogChangeRejectsLateCommitAndInvalidatesExistingVectors()
    {
        await using var fixture = await Fixture.CreateAsync("original.txt");
        var snapshot = Assert.Single(await fixture.Store.GetVectorIndexCandidatesAsync(Model, 10));
        await fixture.EmbedAsync("original.txt", [1, 0]);
        await fixture.SqlAsync("UPDATE index_files SET metadata_fingerprint = 'changed' WHERE id = $id;", fixture.Id("original.txt"));
        Assert.False(await fixture.Store.ReplaceVectorsAsync(snapshot, Model, Embeddings(snapshot, [1, 0]), 1_000_000));
        Assert.Empty((await fixture.Store.SearchVectorsAsync(Model, new float[] { 1, 0 }, Query)).Matches);
        Assert.Equal(0, await fixture.CountAsync("index_vector_chunks"));
    }

    /// <summary>No-op catalog writes and source priority changes preserve reusable vectors.</summary>
    [Fact]
    public async Task ProgressAndPriorityChangesDoNotReembedUnchangedEvidence()
    {
        await using var fixture = await Fixture.CreateAsync("retained.txt");
        await fixture.EmbedAsync("retained.txt", [1, 0]);
        await fixture.Store.SetSourcePriorityAsync("source", 77);
        await fixture.SqlAsync("UPDATE index_files SET updated_utc_ticks = updated_utc_ticks + 1 WHERE id = $id;", fixture.Id("retained.txt"));
        Assert.Empty(await fixture.Store.GetVectorIndexCandidatesAsync(Model, 10));
        Assert.Single((await fixture.Store.SearchVectorsAsync(Model, new float[] { 1, 0 }, Query)).Matches);
    }

    /// <summary>Privacy clearing removes chunk text as well as vectors without touching source files.</summary>
    [Fact]
    public async Task SemanticPrivacyClearPurgesVectorsAndPreventsRepopulation()
    {
        await using var fixture = await Fixture.CreateAsync("private.txt");
        var snapshot = Assert.Single(await fixture.Store.GetVectorIndexCandidatesAsync(Model, 10));
        await fixture.EmbedAsync("private.txt", [1, 0]);
        await fixture.Store.ClearFileDataAsync(fixture.Id("private.txt"), IndexedDataKind.SemanticData, DateTimeOffset.UtcNow);
        Assert.Empty(await fixture.Store.GetVectorIndexCandidatesAsync(Model, 10));
        Assert.Empty((await fixture.Store.SearchVectorsAsync(Model, new float[] { 1, 0 }, Query)).Matches);
        Assert.False(await fixture.Store.ReplaceVectorsAsync(snapshot, Model, Embeddings(snapshot, [1, 0]), 1_000_000));
        Assert.Equal(0, await fixture.CountAsync("index_vector_chunks"));
    }

    /// <summary>Deleting authoritative file/source records cascades through learned artifacts.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForgetAndRemoveSourceCascadeVectorChunks(bool source)
    {
        await using var fixture = await Fixture.CreateAsync("removed.txt");
        await fixture.EmbedAsync("removed.txt", [1, 0]);
        if (source) await fixture.Store.RemoveSourceAsync("source");
        else await fixture.Store.ForgetFileAsync(fixture.Id("removed.txt"), DateTimeOffset.UtcNow);
        Assert.Equal(0, await fixture.CountAsync("index_vector_documents"));
        Assert.Equal(0, await fixture.CountAsync("index_vector_chunks"));
    }

    /// <summary>Changed model digest cannot reuse same-dimension vectors.</summary>
    [Fact]
    public async Task ModelDigestChangeIsolatedAndRebuildUsesRetainedCatalog()
    {
        await using var fixture = await Fixture.CreateAsync("retained.txt");
        await fixture.EmbedAsync("retained.txt", [1, 0]);
        var nextModel = Model with { Revision = "digest-2" };
        Assert.Empty((await fixture.Store.SearchVectorsAsync(nextModel, new float[] { 1, 0 }, Query)).Matches);
        Assert.Single(await fixture.Store.GetVectorIndexCandidatesAsync(nextModel, 10));
        await fixture.Store.PruneVectorsAsync(nextModel, 1_000_000);
        Assert.Equal(0, await fixture.CountAsync("index_vector_chunks"));
        Assert.Single(await fixture.Store.GetSearchDocumentsAsync(10));
    }

    /// <summary>Learned chunks remain optional quota-bounded data and never replace catalog evidence.</summary>
    [Fact]
    public async Task QuotaFailurePreservesCatalogAndPublishesLogicalStorage()
    {
        await using var fixture = await Fixture.CreateAsync("bounded.txt");
        var document = Assert.Single(await fixture.Store.GetVectorIndexCandidatesAsync(Model, 10));
        Assert.False(await fixture.Store.ReplaceVectorsAsync(document, Model, Embeddings(document, [1, 0]), 1));
        Assert.Equal(0, (await fixture.Store.GetVectorStatusAsync(Model)).StoredBytes);
        await fixture.EmbedAsync("bounded.txt", [1, 0]);
        var storage = await fixture.Store.GetStorageBreakdownAsync(1_000_000);
        Assert.True(storage.VectorDataBytes > 0);
        await fixture.Store.ClearVectorsAsync();
        Assert.Single(await fixture.Store.GetSearchDocumentsAsync(10));
        Assert.Single(await fixture.Store.GetVectorIndexCandidatesAsync(Model, 10));
    }

    /// <summary>Similarity suggestions respect durable rejected decisions before their cap.</summary>
    [Theory]
    [InlineData(RelationshipDecision.Rejected)]
    [InlineData(RelationshipDecision.NeverRelate)]
    public async Task RelatedSuggestionsFilterRejectedPairBeforeTopK(RelationshipDecision decision)
    {
        await using var fixture = await Fixture.CreateAsync("seed.txt", "rejected.txt", "allowed.txt");
        await fixture.EmbedAsync("seed.txt", [1, 0]);
        await fixture.EmbedAsync("rejected.txt", [1, 0]);
        await fixture.EmbedAsync("allowed.txt", [0.7f, 0.7f]);
        var pair = new[] { fixture.Id("seed.txt"), fixture.Id("rejected.txt") }.Order(StringComparer.Ordinal).ToArray();
        await using (var connection = new SqliteConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO relationship_pair_overrides(first_file_id,second_file_id,decision,changed_utc_ticks) VALUES($first,$second,$decision,1);";
            command.Parameters.AddWithValue("$first", pair[0]);
            command.Parameters.AddWithValue("$second", pair[1]);
            command.Parameters.AddWithValue("$decision", (int)decision);
            await command.ExecuteNonQueryAsync();
        }
        var result = await fixture.Store.SearchRelatedVectorsAsync(Model, fixture.Id("seed.txt"), Query with { MaximumCandidateCount = 1 });
        Assert.Equal(fixture.Id("allowed.txt"), Assert.Single(result.Matches).FileId);
        Assert.Equal(0, await fixture.CountAsync("index_relationships"));
    }

    /// <summary>Malformed vectors are rejected before any persistent update.</summary>
    [Fact]
    public async Task MalformedVectorsDoNotReachStorage()
    {
        await using var fixture = await Fixture.CreateAsync("finite.txt");
        var document = Assert.Single(await fixture.Store.GetVectorIndexCandidatesAsync(Model, 10));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.ReplaceVectorsAsync(document, Model, Embeddings(document, [float.NaN, 1]), 1_000_000));
        Assert.Equal(0, await fixture.CountAsync("index_vector_documents"));
    }

    /// <summary>Document failures back off durably, stop after three attempts, and reset on explicit rebuild.</summary>
    [Fact]
    public async Task FailureBackoffSurvivesRestartAndBoundsAttempts()
    {
        await using var fixture = await Fixture.CreateAsync("failed.txt", "later.txt");
        var document = (await fixture.Store.GetVectorIndexCandidatesAsync(Model, 10)).Single(item => item.FileId == fixture.Id("failed.txt"));
        await fixture.Store.RecordVectorFailureAsync(document, Model);
        await fixture.ReopenAsync();
        var ready = await fixture.Store.GetVectorIndexCandidatesAsync(Model, 10);
        Assert.Equal(fixture.Id("later.txt"), Assert.Single(ready).FileId);
        await fixture.SqlAsync("UPDATE index_vector_failures SET next_retry_utc_ticks = 0 WHERE file_id = $id;", document.FileId);
        Assert.Equal(2, (await fixture.Store.GetVectorIndexCandidatesAsync(Model, 10)).Count);
        await fixture.Store.RecordVectorFailureAsync(document, Model);
        await fixture.Store.RecordVectorFailureAsync(document, Model);
        await fixture.SqlAsync("UPDATE index_vector_failures SET next_retry_utc_ticks = 0 WHERE file_id = $id;", document.FileId);
        Assert.Single(await fixture.Store.GetVectorIndexCandidatesAsync(Model, 10));
        Assert.Equal(1, (await fixture.Store.GetVectorStatusAsync(Model)).FailedFiles);
        await fixture.Store.ClearVectorsAsync();
        Assert.Equal(2, (await fixture.Store.GetVectorIndexCandidatesAsync(Model, 10)).Count);
    }

    /// <summary>Post-hydration validation rejects cleared, private, replaced-model or changed-generation evidence.</summary>
    [Theory]
    [InlineData("privacy")]
    [InlineData("clear")]
    [InlineData("model")]
    [InlineData("generation")]
    [InlineData("forget")]
    public async Task FinalValidationRejectsInvalidatedTargetSnapshot(string mutation)
    {
        await using var fixture = await Fixture.CreateAsync("target.txt");
        await fixture.EmbedAsync("target.txt", [1, 0]);
        var before = await fixture.Store.SearchVectorsAsync(Model, new float[] { 1, 0 }, Query);
        Assert.Single(await fixture.Store.ValidateVectorMatchesAsync(Model, before, Query));
        switch (mutation)
        {
            case "privacy":
                await fixture.Store.SetFilePolicyAsync(fixture.Id("target.txt"), new IndexPrivacyPolicyChange(SuppressSemantic: true), DateTimeOffset.UtcNow);
                break;
            case "clear":
                await fixture.Store.ClearVectorsAsync();
                break;
            case "model":
                await fixture.Store.PruneVectorsAsync(Model with { Revision = "replacement" }, 1_000_000);
                break;
            case "forget":
                await fixture.Store.ForgetFileAsync(fixture.Id("target.txt"), DateTimeOffset.UtcNow);
                break;
            default:
                await fixture.SqlAsync("UPDATE index_files SET metadata_fingerprint='replacement' WHERE id=$id;", fixture.Id("target.txt"));
                await fixture.EmbedAsync("target.txt", [1, 0]);
                var after = await fixture.Store.SearchVectorsAsync(Model, new float[] { 1, 0 }, Query);
                // Text and deterministic chunk identity can stay equal while the source generation changed.
                Assert.Equal(Assert.Single(before.Matches).ChunkId, Assert.Single(after.Matches).ChunkId);
                break;
        }
        Assert.Empty(await fixture.Store.ValidateVectorMatchesAsync(Model, before, Query));
    }

    /// <summary>Late pair decisions and source changes are fenced together with current target generations.</summary>
    [Theory]
    [InlineData("rejected")]
    [InlineData("never")]
    [InlineData("source-generation")]
    [InlineData("source-privacy")]
    public async Task FinalRelatedValidationRechecksPairAndSourceSnapshot(string mutation)
    {
        await using var fixture = await Fixture.CreateAsync("seed.txt", "target.txt");
        await fixture.EmbedAsync("seed.txt", [1, 0]);
        await fixture.EmbedAsync("target.txt", [1, 0]);
        var before = await fixture.Store.SearchRelatedVectorsAsync(Model, fixture.Id("seed.txt"), Query);
        Assert.Single(await fixture.Store.ValidateVectorMatchesAsync(Model, before, Query, fixture.Id("seed.txt")));
        if (mutation is "rejected" or "never")
            await fixture.DecidePairAsync("seed.txt", "target.txt", mutation == "never" ? RelationshipDecision.NeverRelate : RelationshipDecision.Rejected);
        else if (mutation == "source-privacy")
            await fixture.Store.SetFilePolicyAsync(fixture.Id("seed.txt"), new IndexPrivacyPolicyChange(SuppressRelationships: true), DateTimeOffset.UtcNow);
        else
        {
            await fixture.SqlAsync("UPDATE index_files SET metadata_fingerprint='new-seed' WHERE id=$id;", fixture.Id("seed.txt"));
            await fixture.EmbedAsync("seed.txt", [1, 0]);
            Assert.Single((await fixture.Store.SearchRelatedVectorsAsync(Model, fixture.Id("seed.txt"), Query)).Matches);
        }
        Assert.Empty(await fixture.Store.ValidateVectorMatchesAsync(Model, before, Query, fixture.Id("seed.txt")));
    }

    private static VectorChunkEmbedding[] Embeddings(VectorIndexDocument document, float[] vector) =>
        VectorTextChunker.Create(document).Select(chunk => new VectorChunkEmbedding(chunk, vector)).ToArray();

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "omnisorse-vector-tests", Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, string> _ids = new(StringComparer.Ordinal);
        public SqliteDeepIndexStore Store { get; private set; } = null!;
        public string ConnectionString => new SqliteConnectionStringBuilder { DataSource = Path.Combine(_root, "index.db"), ForeignKeys = true, Pooling = false }.ToString();
        public string Id(string name) => _ids[name];
        public static async Task<Fixture> CreateAsync(params string[] names)
        {
            var fixture = new Fixture();
            Directory.CreateDirectory(fixture._root);
            fixture.Store = new SqliteDeepIndexStore(Path.Combine(fixture._root, "index.db"), PlatformServices.CurrentPathSemantics);
            await fixture.Store.InitializeAsync();
            var source = new IndexingSource("source", fixture._root, "Fixture", IndexingLevel.Standard, true, true, 0, []);
            await fixture.Store.UpsertSourceAsync(source);
            var run = await fixture.Store.BeginRunAsync(source.Id, DateTimeOffset.UtcNow);
            var now = DateTimeOffset.UtcNow;
            await fixture.Store.EnqueueDiscoveredFilesAsync(run, names.Select(name => new IndexingFileObservation(
                Path.Combine(fixture._root, name), name, name, "volume", 100, now, now, FileAttributes.Normal, "metadata-" + name)).ToArray(), "processor", 1);
            await fixture.Store.CompleteDiscoveryAsync(run, new HashSet<string>(), now);
            foreach (var document in await fixture.Store.GetSearchDocumentsAsync(100)) fixture._ids[document.FileName] = document.FileId;
            return fixture;
        }
        public async Task EmbedAsync(string name, float[] vector)
        {
            var document = (await Store.GetVectorIndexCandidatesAsync(Model, 100)).Single(item => item.FileId == Id(name));
            Assert.True(await Store.ReplaceVectorsAsync(document, Model, Embeddings(document, vector), 1_000_000));
        }
        public async Task ReopenAsync()
        {
            await Store.DisposeAsync();
            Store = new SqliteDeepIndexStore(Path.Combine(_root, "index.db"), PlatformServices.CurrentPathSemantics);
            await Store.InitializeAsync();
        }
        public async Task SqlAsync(string sql, string id)
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync();
        }
        public async Task DecidePairAsync(string firstName, string secondName, RelationshipDecision decision)
        {
            var pair = new[] { Id(firstName), Id(secondName) }.Order(StringComparer.Ordinal).ToArray();
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO relationship_pair_overrides(first_file_id,second_file_id,decision,changed_utc_ticks) VALUES($first,$second,$decision,1);";
            command.Parameters.AddWithValue("$first", pair[0]);
            command.Parameters.AddWithValue("$second", pair[1]);
            command.Parameters.AddWithValue("$decision", (int)decision);
            await command.ExecuteNonQueryAsync();
        }
        public async Task<long> CountAsync(string table)
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM " + table;
            return (long)(await command.ExecuteScalarAsync())!;
        }
        public async ValueTask DisposeAsync()
        {
            await Store.DisposeAsync();
            Directory.Delete(_root, recursive: true);
        }
    }
}
