using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Semantic;
using System.Text.Json;

namespace OpenSorSe.Application.Tests;

/// <summary>Validates deterministic, bounded, evidence-backed relationship discovery and ranking.</summary>
public sealed class RelationshipEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Verifies an identical content fingerprint produces a document-set relationship with retained evidence.</summary>
    [Fact]
    public void Discover_IdenticalContent_ProducesExplainableDocumentSet()
    {
        var engine = CreateEngine();
        var first = Document("a", "notes.txt", hash: "same-content");
        var second = Document("b", "copy.txt", hash: "same-content");

        var proposal = Assert.Single(engine.Discover(first, [second], 10));

        Assert.Equal(RelationshipType.DocumentSet, proposal.Relationship.Type);
        Assert.Equal(RelationshipConfidence.High, proposal.Relationship.Confidence);
        Assert.Contains(proposal.Relationship.Evidence, item => item.Kind == RelationshipEvidenceKind.DuplicateContent);
        Assert.Contains("Identical content fingerprint", proposal.Relationship.Explanation, StringComparison.Ordinal);
        Assert.NotNull(proposal.Collection);
    }

    /// <summary>Verifies concrete identifiers and purchase terms produce a purchase relationship without AI.</summary>
    [Fact]
    public void Discover_InvoiceIdentifierAndKeywords_ProducesPurchaseContext()
    {
        var engine = CreateEngine();
        var invoice = Document("a", "mercedes-invoice-2026-1234.pdf", keywords: ["mercedes", "invoice"]);
        var receipt = Document("b", "mercedes-receipt-2026-1234.pdf", keywords: ["mercedes", "receipt"]);

        var proposal = Assert.Single(engine.Discover(invoice, [receipt], 10));

        Assert.Equal(RelationshipType.SamePurchase, proposal.Relationship.Type);
        Assert.Contains(proposal.Relationship.Evidence, item => item.Kind == RelationshipEvidenceKind.Filename);
        Assert.Contains(proposal.Relationship.Evidence, item => item.Kind == RelationshipEvidenceKind.Keyword);
        Assert.Equal("deterministic-evidence", proposal.Relationship.Algorithm);
        Assert.NotNull(proposal.Collection);
    }

    /// <summary>Verifies an identifier found only in metadata is not misreported as filename evidence.</summary>
    [Fact]
    public void Discover_MetadataIdentifier_ReportsMetadataEvidence()
    {
        var engine = CreateEngine();
        var first = Document("a", "receipt.pdf", keywords: ["invoice"]) with
        {
            MetadataText = "invoice INV-2026-4431",
        };
        var second = Document("b", "warranty.pdf", keywords: ["invoice"]) with
        {
            MetadataText = "reference INV-2026-4431",
        };

        var relationship = Assert.Single(engine.Discover(first, [second], 10)).Relationship;

        Assert.Contains(relationship.Evidence, item => item.Kind == RelationshipEvidenceKind.Metadata);
        Assert.DoesNotContain(
            relationship.Evidence,
            item => item.Kind == RelationshipEvidenceKind.Filename &&
                    item.Explanation.Contains("identifier", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Verifies weak folder proximity does not create speculative relationships.</summary>
    [Fact]
    public void Discover_OnlySharedFolder_DoesNotCreateRelationship()
    {
        var engine = CreateEngine();
        var first = Document("a", "unrelated-alpha.txt", folder: "shared");
        var second = Document("b", "different-omega.txt", folder: "shared");

        Assert.Empty(engine.Discover(first, [second], 10));
    }

    /// <summary>Verifies semantic similarity alone cannot fabricate a relationship.</summary>
    [Fact]
    public void Discover_SemanticOnly_DoesNotCreateRelationship()
    {
        var engine = CreateEngine();
        var first = Document("a", "alpha.txt", semantic: [1f, 0f]);
        var second = Document("b", "omega.txt", semantic: [1f, 0f]);

        Assert.Empty(engine.Discover(first, [second], 10));
    }

    /// <summary>Verifies identical inputs produce identical relationships, evidence, confidence, and ordering.</summary>
    [Fact]
    public void Discover_IdenticalInputs_IsDeterministic()
    {
        var engine = CreateEngine();
        var target = Document("z", "dolomites-trip-8842.txt", keywords: ["dolomites", "travel"]);
        var candidates = new[]
        {
            Document("b", "dolomites-hotel-8842.pdf", keywords: ["dolomites", "travel"]),
            Document("a", "dolomites-ticket-8842.pdf", keywords: ["dolomites", "travel"]),
        };

        var first = engine.Discover(target, candidates, 10);
        var second = engine.Discover(target, candidates.Reverse().ToArray(), 10);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.All(first, item => Assert.Equal(Now, item.Relationship.CreatedAtUtc));
        Assert.Equal(first.Select(item => item.Relationship.Id).Order(StringComparer.Ordinal), first.Select(item => item.Relationship.Id));
    }

    /// <summary>Verifies an explicit per-file privacy suppression prevents analysis.</summary>
    [Fact]
    public void Discover_SuppressedTarget_ReturnsNoRelationships()
    {
        var engine = CreateEngine();
        var target = Document("a", "invoice-1234.pdf", hash: "same") with { RelationshipAnalysisSuppressed = true };
        var candidate = Document("b", "invoice-copy-1234.pdf", hash: "same");

        Assert.Empty(engine.Discover(target, [candidate], 10));
    }

    /// <summary>Verifies cooperative cancellation is observed during bounded candidate evaluation.</summary>
    [Fact]
    public void Discover_Cancelled_StopsPromptly()
    {
        var engine = CreateEngine();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => engine.Discover(
            Document("a", "invoice-1234.pdf"),
            [Document("b", "invoice-copy-1234.pdf")],
            10,
            cancellation.Token));
    }

    /// <summary>Verifies malformed indexed filenames fail closed before analysis.</summary>
    [Fact]
    public void CreateFeatures_MalformedUnicode_FailsClosed()
    {
        var engine = CreateEngine();
        var malformed = Document("a", "safe.txt") with { FileName = "bad\ud800.txt" };

        Assert.Throws<InvalidDataException>(() => engine.CreateFeatures(malformed));
    }

    /// <summary>Verifies relationship context remains below precise literal matches and exposes its actual evidence.</summary>
    [Fact]
    public void Rank_ExactFilename_RemainsAboveRelationshipExpansion()
    {
        var ranker = new HybridSearchRanker(new FeatureHashingEmbeddingProvider(), new SearchSnippetFactory());
        var interpretation = new SearchInterpretation("mercedes invoice", "mercedes invoice", ["mercedes", "invoice"], []);
        var exact = Candidate("exact", "mercedes invoice.pdf");
        var contextual = Candidate("related", "warranty.pdf") with
        {
            RelationshipContext = new SearchRelationshipContext(
                "exact",
                RelationshipType.SamePurchase,
                RelationshipConfidence.High,
                "Same invoice number",
                "Mercedes Purchase"),
        };

        var ranked = ranker.Rank(interpretation, [contextual, exact], 10, CancellationToken.None);

        Assert.Equal("exact", ranked[0].Document.FileId);
        var expanded = Assert.Single(ranked, item => item.Document.FileId == "related");
        var component = Assert.Single(expanded.Components, item => item.Kind == SearchRankingSignalKind.RelationshipContext);
        Assert.Equal("Same invoice number", component.Explanation);
    }

    /// <summary>Verifies direct relationship expansion ordering remains deterministic for otherwise identical candidates.</summary>
    [Fact]
    public void Rank_RelationshipContext_UsesStablePathTieBreak()
    {
        var ranker = new HybridSearchRanker(new FeatureHashingEmbeddingProvider(), new SearchSnippetFactory());
        var interpretation = new SearchInterpretation("query absent", "query absent", ["query", "absent"], []);
        var context = new SearchRelationshipContext("seed", RelationshipType.SameTopic, RelationshipConfidence.Medium, "Shared keyword: battery", null);
        var alpha = Candidate("a", "alpha.pdf") with { FullPath = "/files/a.pdf", RelationshipContext = context };
        var beta = Candidate("b", "beta.pdf") with { FullPath = "/files/b.pdf", RelationshipContext = context };

        var ranked = ranker.Rank(interpretation, [beta, alpha], 10, CancellationToken.None);

        Assert.Equal(["a", "b"], ranked.Select(item => item.Document.FileId));
    }

    private static DeterministicRelationshipEngine CreateEngine() => new(new FixedTimeProvider(Now));

    private static RelationshipFileDocument Document(
        string id,
        string name,
        string folder = "records",
        string? hash = null,
        IReadOnlyList<string>? keywords = null,
        IReadOnlyList<float>? semantic = null) => new()
        {
            FileId = id,
            SourceId = "source",
            SourceName = "Synthetic source",
            FullPath = "/synthetic/" + folder + "/" + name,
            RelativePath = folder + "/" + name,
            FileName = name,
            FolderName = folder,
            Extension = Path.GetExtension(name),
            ContentHash = hash,
            CreationTimeUtc = Now,
            ModifiedTimeUtc = Now.AddMinutes(30),
            MetadataText = "synthetic",
            Keywords = keywords ?? [],
            SemanticRepresentation = semantic,
            IsFullyIndexed = true,
        };

    private static SearchCandidateDocument Candidate(string id, string name) => new()
    {
        FileId = id,
        FullPath = "/files/" + name,
        RelativePath = name,
        FileName = name,
        Extension = Path.GetExtension(name),
        FileType = "document",
        IsFullyIndexed = true,
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
