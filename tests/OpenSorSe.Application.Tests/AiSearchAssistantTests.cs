using OpenSorSe.Application.AI;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies the privacy, grounding, and deterministic-tier boundaries of optional Search AI.</summary>
public sealed class AiSearchAssistantTests
{
    /// <summary>Verifies disabled assistance never contacts the configured provider.</summary>
    [Fact]
    public async Task DisabledCapabilityPreservesCandidatesWithoutProviderCall()
    {
        var provider = new Provider(Success("result-002", "result-001"));
        var candidates = Candidates(2);

        var result = await new AiSearchAssistant(provider).RerankAsync(
            Interpretation(),
            candidates,
            Settings(enabled: false),
            CancellationToken.None);

        Assert.Equal(AiSearchAssistanceState.Disabled, result.Assistance.State);
        Assert.Equal(candidates, result.Candidates);
        Assert.Equal(0, provider.CallCount);
    }

    /// <summary>Verifies only a finite candidate set is disclosed and no absolute path enters the prompt.</summary>
    [Fact]
    public async Task CandidatePromptIsBoundedAndOmitsPaths()
    {
        var provider = new Provider(Success("result-001"));
        var candidates = Candidates(AiSearchAssistant.MaximumCandidateCount + 8);

        var result = await new AiSearchAssistant(provider).RerankAsync(
            Interpretation(),
            candidates,
            Settings(),
            CancellationToken.None);

        Assert.Equal(AiSearchAssistant.MaximumCandidateCount, result.Assistance.CandidateCount);
        Assert.NotNull(provider.LastRequest);
        Assert.Contains("result-012", provider.LastRequest.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("result-013", provider.LastRequest.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-root", provider.LastRequest.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("FullPath", provider.LastRequest.Prompt, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies a valid response may reorder equally strong known candidates only.</summary>
    [Fact]
    public async Task ValidResponseReordersKnownCandidatesWithinTier()
    {
        var provider = new Provider(Success("result-002", "result-001"));
        var candidates = Candidates(2);

        var result = await new AiSearchAssistant(provider).RerankAsync(
            Interpretation(),
            candidates,
            Settings(),
            CancellationToken.None);

        Assert.Equal(AiSearchAssistanceState.Applied, result.Assistance.State);
        Assert.Equal(["file-002", "file-001"], result.Candidates.Select(item => item.Document.FileId));
        Assert.All(
            result.Candidates,
            item => Assert.Contains(item.Components, component => component.Kind == SearchRankingSignalKind.AiAssistedOrder));
        Assert.Equal(candidates.Select(item => item.Score).Order(), result.Candidates.Select(item => item.Score).Order());
    }

    /// <summary>Verifies local exact-match tiers cannot be crossed by provider-supplied ordering.</summary>
    [Fact]
    public async Task ExactFilenameCannotBeMovedBelowWeakContent()
    {
        RankedSearchCandidate[] candidates =
        [
            Candidate(1, SearchRankingSignalKind.ExactFilename, 1000),
            Candidate(2, SearchRankingSignalKind.ExtractedText, 100),
        ];
        var provider = new Provider(Success("result-002", "result-001"));

        var result = await new AiSearchAssistant(provider).RerankAsync(
            Interpretation(),
            candidates,
            Settings(),
            CancellationToken.None);

        Assert.Equal(["file-001", "file-002"], result.Candidates.Select(item => item.Document.FileId));
        Assert.Equal(AiSearchAssistanceState.NoChange, result.Assistance.State);
    }

    /// <summary>Verifies an invented candidate identifier invalidates the entire untrusted response.</summary>
    [Fact]
    public async Task UnknownCandidateCannotBeIntroduced()
    {
        var provider = new Provider(Success("result-999", "result-001"));
        var candidates = Candidates(2);

        var result = await new AiSearchAssistant(provider).RerankAsync(
            Interpretation(),
            candidates,
            Settings(),
            CancellationToken.None);

        Assert.Equal(AiSearchAssistanceState.InvalidResponse, result.Assistance.State);
        Assert.Equal(candidates, result.Candidates);
        Assert.False(result.Assistance.WasApplied);
    }

    /// <summary>Verifies provider availability, timeout, and missing-model failures all preserve local Search.</summary>
    [Theory]
    [InlineData(AiProviderFailureKind.Unavailable)]
    [InlineData(AiProviderFailureKind.Timeout)]
    [InlineData(AiProviderFailureKind.ModelUnavailable)]
    [InlineData(AiProviderFailureKind.InvalidResponse)]
    public async Task ProviderFailurePreservesDeterministicResults(AiProviderFailureKind failure)
    {
        var provider = new Provider(new AiProviderGenerationResult(null, failure, "synthetic failure"));
        var candidates = Candidates(2);

        var result = await new AiSearchAssistant(provider).RerankAsync(
            Interpretation(),
            candidates,
            Settings(),
            CancellationToken.None);

        Assert.Equal(candidates, result.Candidates);
        Assert.False(result.Assistance.WasApplied);
        Assert.Equal(
            failure == AiProviderFailureKind.InvalidResponse
                ? AiSearchAssistanceState.InvalidResponse
                : AiSearchAssistanceState.Unavailable,
            result.Assistance.State);
    }

    /// <summary>Verifies an expected transport exception cannot escape and disrupt ordinary Search.</summary>
    [Fact]
    public async Task ProviderTransportExceptionPreservesDeterministicResults()
    {
        var provider = new Provider((_, _) => throw new HttpRequestException("Synthetic endpoint stopped."));
        var candidates = Candidates(2);

        var result = await new AiSearchAssistant(provider).RerankAsync(
            Interpretation(),
            candidates,
            Settings(),
            CancellationToken.None);

        Assert.Equal(candidates, result.Candidates);
        Assert.Equal(AiSearchAssistanceState.Unavailable, result.Assistance.State);
        Assert.False(result.Assistance.WasApplied);
    }

    /// <summary>Verifies user cancellation remains cooperative rather than becoming a fallback success.</summary>
    [Fact]
    public async Task CancellationPropagatesPromptly()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new Provider(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Success("result-001");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AiSearchAssistant(provider).RerankAsync(
                Interpretation(),
                Candidates(2),
                Settings(),
                cancellation.Token));
    }

    private static IReadOnlyList<RankedSearchCandidate> Candidates(int count) =>
        Enumerable.Range(1, count)
            .Select(index => Candidate(index, SearchRankingSignalKind.FilenamePrefix, 500 - index))
            .ToArray();

    private static RankedSearchCandidate Candidate(int index, SearchRankingSignalKind kind, double score) =>
        new(
            new SearchCandidateDocument
            {
                FileId = $"file-{index:D3}",
                FullPath = Path.Combine(Path.GetTempPath(), "synthetic-root", $"report-{index:D3}.txt"),
                FileName = $"report-{index:D3}.txt",
                RelativePath = $"report-{index:D3}.txt",
            },
            score,
            [new SearchRankingComponent(kind, "filename", score, "Filename matched")],
            new SearchSnippet(SearchSnippetSource.Filename, "Filename", "report", [new SearchHighlight(0, 6)]));

    private static SearchInterpretation Interpretation() =>
        new("raspberry pi setup", "raspberry pi setup", ["raspberry", "pi", "setup"], []);

    private static AiSettings Settings(bool enabled = true) => new()
    {
        Enabled = enabled,
        SearchAssistanceEnabled = enabled,
        SelectedModel = "small-local:latest",
        RequestTimeoutSeconds = 10,
    };

    private static AiProviderGenerationResult Success(params string[] ids) => new(
        $$"""
        {"taskId":"search-rerank-v1","status":"reranked","orderedCandidateIds":{{System.Text.Json.JsonSerializer.Serialize(ids)}},"summary":"Known results reviewed."}
        """,
        AiProviderFailureKind.None,
        "ok");

    private sealed class Provider : IAiSuggestionProvider
    {
        private readonly Func<AiProviderGenerationRequest, CancellationToken, Task<AiProviderGenerationResult>> _generate;

        public Provider(AiProviderGenerationResult result)
            : this((_, _) => Task.FromResult(result))
        {
        }

        public Provider(Func<AiProviderGenerationRequest, CancellationToken, Task<AiProviderGenerationResult>> generate) =>
            _generate = generate;

        public int CallCount { get; private set; }

        public AiProviderGenerationRequest? LastRequest { get; private set; }

        public Task<AiConnectionResult> GetConnectionAsync(AiSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(new AiConnectionResult(AiAvailabilityState.Connected, "ok", []));

        public Task<AiProviderGenerationResult> GenerateAsync(
            AiProviderGenerationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return _generate(request, cancellationToken);
        }
    }
}
