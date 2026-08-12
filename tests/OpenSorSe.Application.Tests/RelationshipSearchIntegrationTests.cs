using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;
using System.Data.Common;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies relationship-aware Search integration without a desktop UI or live dependency.</summary>
public sealed class RelationshipSearchIntegrationTests
{
    /// <summary>Verifies an exact result stays first while a direct related file is added with real evidence.</summary>
    [Fact]
    public async Task RelationshipExpansionPreservesExactPriorityAndExplanation()
    {
        var relationshipSource = new RelationshipSource();
        var service = CreateService(relationshipSource);

        var result = await service.SearchAsync(
            new SearchRequest("mercedes-invoice.pdf"),
            CancellationToken.None);

        Assert.Equal(SemanticState.Ready, result.State);
        Assert.Equal(["invoice", "warranty"], result.Hits.Select(hit => Assert.IsType<string>(hit.FileId)).ToArray());
        Assert.Contains(
            "invoice number INV-2026-42",
            Assert.Single(result.Hits[1].RankingComponents!, component =>
                component.Kind == SearchRankingSignalKind.RelationshipContext).Explanation,
            StringComparison.Ordinal);
        Assert.True(relationshipSource.RequestedMaximum <= RelationshipLimits.MaximumSearchExpansions);
    }

    /// <summary>Verifies the user can disable contextual expansion for an otherwise identical query.</summary>
    [Fact]
    public async Task RelationshipExpansionCanBeDisabledPerSearch()
    {
        var relationshipSource = new RelationshipSource();
        var service = CreateService(relationshipSource);

        var result = await service.SearchAsync(
            new SearchRequest("mercedes-invoice.pdf", IncludeRelationshipContext: false),
            CancellationToken.None);

        Assert.Equal("invoice", Assert.Single(result.Hits).FileId);
        Assert.Equal(0, relationshipSource.InvocationCount);
    }

    /// <summary>Verifies relationship-store unavailability cannot block ordinary local Search.</summary>
    [Fact]
    public async Task RelationshipFailureFallsBackToOrdinarySearch()
    {
        var service = CreateService(new RelationshipSource(new InvalidDataException("synthetic")));

        var result = await service.SearchAsync(
            new SearchRequest("mercedes-invoice.pdf"),
            CancellationToken.None);

        Assert.Equal(SemanticState.Ready, result.State);
        Assert.Equal("invoice", Assert.Single(result.Hits).FileId);
    }

    /// <summary>Verifies a provider-level busy/locked failure cannot block ordinary local Search.</summary>
    [Fact]
    public async Task RelationshipDatabaseFailureFallsBackToOrdinarySearch()
    {
        var service = CreateService(new RelationshipSource(new SyntheticDatabaseException()));

        var result = await service.SearchAsync(
            new SearchRequest("mercedes-invoice.pdf"),
            CancellationToken.None);

        Assert.Equal(SemanticState.Ready, result.State);
        Assert.Equal("invoice", Assert.Single(result.Hits).FileId);
    }

    private static SemanticSearchService CreateService(IRelationshipSearchSource relationships)
    {
        var embeddings = new FeatureHashingEmbeddingProvider();
        return new SemanticSearchService(
            new Configuration(),
            embeddings,
            new EmptySemanticStore(),
            new ProgressiveSource(
            [
                Document("invoice", "mercedes-invoice.pdf", "vehicle invoice number INV-2026-42"),
                Document("warranty", "warranty-card.txt", "coverage details"),
                Document("distractor", "garden-notes.txt", "unrelated notes"),
            ]),
            relationshipSearchSource: relationships);
    }

    private static ProgressiveSearchDocument Document(string id, string name, string metadata) => new()
    {
        FileId = id,
        FullPath = Path.Combine(Path.GetTempPath(), "OmniSorSe-synthetic", name),
        FileName = name,
        RelativePath = name,
        FolderName = "OmniSorSe-synthetic",
        MetadataText = metadata,
        IsFullyIndexed = true,
    };

    private sealed class Configuration : IConfigurationService
    {
        public ApplicationSettings Current { get; } = new()
        {
            SemanticSearch = new SemanticSearchSettings
            {
                Enabled = true,
                MaximumDocumentCount = 100,
                MaximumResultCount = 20,
            },
        };

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptySemanticStore : ISemanticIndexStore
    {
        public Task<IReadOnlyList<SemanticIndexEntry>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SemanticIndexEntry>>([]);

        public Task ReplaceAsync(IReadOnlyList<SemanticIndexEntry> entries, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ProgressiveSource(IReadOnlyList<ProgressiveSearchDocument> documents) : IProgressiveSearchSource
    {
        public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsAsync(
            int maximumCount,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>(documents.Take(maximumCount).ToArray());

        public Task<SearchCoverage> GetCoverageAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SearchCoverage(documents.Count, documents.Count, documents.Count, 0, 0, documents.Count));

        public Task<IReadOnlyList<string>> GetExcludedPathsAsync(
            int maximumCount,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class RelationshipSource(Exception? failure = null) : IRelationshipSearchSource
    {
        public int InvocationCount { get; private set; }

        public int RequestedMaximum { get; private set; }

        public Task<IReadOnlyList<RelationshipSearchExpansion>> ExpandAsync(
            IReadOnlyList<string> seedFileIds,
            int maximumCount,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            RequestedMaximum = maximumCount;
            if (failure is not null)
            {
                return Task.FromException<IReadOnlyList<RelationshipSearchExpansion>>(failure);
            }

            return Task.FromResult<IReadOnlyList<RelationshipSearchExpansion>>(
                seedFileIds.Contains("invoice", StringComparer.Ordinal)
                    ?
                    [
                        new RelationshipSearchExpansion(
                            "invoice",
                            "warranty",
                            RelationshipType.SamePurchase,
                            RelationshipConfidence.High,
                            "Same invoice number INV-2026-42.",
                            "Mercedes Purchase"),
                    ]
                    : []);
        }
    }

    private sealed class SyntheticDatabaseException() : DbException("synthetic database busy");
}
