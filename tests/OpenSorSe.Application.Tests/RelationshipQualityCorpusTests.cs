using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.SmartTags;

namespace OpenSorSe.Application.Tests;

/// <summary>Provides a small deterministic positive/negative corpus for relationship quality policy.</summary>
public sealed class RelationshipQualityCorpusTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Gets named, deterministic positive and negative relationship fixtures.</summary>
    public static TheoryData<string, RelationshipFileDocument, RelationshipFileDocument, bool> Cases => new()
    {
        { "exact duplicate", Document("a", "alpha.txt", hash: "same"), Document("b", "copy.txt", hash: "same"), true },
        { "weak same stem", Document("a", "project status draft.txt"), Document("b", "project status final.txt"), false },
        { "project identifier plus lexical context", Document("a", "project-4421-report.pdf", ["project phoenix"]), Document("b", "project-4421-budget.xlsx", ["project phoenix"]), true },
        { "generic entity only", WithConcept(Document("a", "minutes.txt"), Entity("Microsoft")), WithConcept(Document("b", "notes.txt"), Entity("Microsoft")), false },
        { "same accepted tag only", WithTag(Document("a", "alpha.txt"), "finance"), WithTag(Document("b", "omega.txt"), "finance"), false },
        { "same timestamp only", Document("a", "alpha.txt"), Document("b", "omega.txt"), false },
        { "repeated boilerplate fingerprint", Document("a", "terms-a.txt") with { ExtractedText = Boilerplate }, Document("b", "terms-b.txt") with { ExtractedText = Boilerplate }, false },
        { "AI concepts without deterministic corroboration", WithConcept(Document("a", "alpha.txt"), Topic("strategy", true), Topic("planning", true)), WithConcept(Document("b", "omega.txt"), Topic("strategy", true), Topic("planning", true)), false },
    };

    /// <summary>Verifies the compact corpus preserves strong positives and suppresses weak coincidence.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Discover_QualityCorpus_MatchesExpectedOutcome(
        string name,
        RelationshipFileDocument first,
        RelationshipFileDocument second,
        bool expected)
    {
        _ = name;
        var engine = new DeterministicRelationshipEngine(new FixedTimeProvider(Now));

        var actual = engine.Discover(first, [second], 10);

        Assert.Equal(expected, actual.Count == 1);
    }

    /// <summary>Verifies correlated content derivatives share one capped family and do not fabricate authority.</summary>
    [Fact]
    public void Discover_CorrelatedContentDerivatives_DoNotStackAcrossFamilies()
    {
        var first = Document("a", "alpha.txt") with
        {
            ExtractedText = Boilerplate,
            OcrText = Boilerplate,
            Summary = Boilerplate,
        };
        var second = Document("b", "omega.txt") with
        {
            ExtractedText = Boilerplate,
            OcrText = Boilerplate,
            Summary = Boilerplate,
        };

        var result = new DeterministicRelationshipEngine(new FixedTimeProvider(Now)).Discover(first, [second], 10);

        Assert.Empty(result);
    }

    /// <summary>Verifies retained evidence identifies independent families and never fabricates percentages.</summary>
    [Fact]
    public void Discover_QualifiedPair_RetainsBoundedFamilyProvenance()
    {
        var first = WithTag(Document("a", "project-4421-report.pdf", ["phoenix"]), "finance");
        var second = WithTag(Document("b", "project-4421-budget.xlsx", ["phoenix"]), "finance");

        var relationship = Assert.Single(
            new DeterministicRelationshipEngine(new FixedTimeProvider(Now)).Discover(first, [second], 10)).Relationship;

        Assert.InRange(relationship.Evidence.Count, 2, RelationshipLimits.MaximumEvidencePerRelationship);
        Assert.Contains(relationship.Evidence, item => item.Family == RelationshipEvidenceFamily.Identity);
        Assert.Contains(relationship.Evidence, item => item.Family == RelationshipEvidenceFamily.TagAuthority);
        Assert.DoesNotContain(relationship.Evidence, item => item.Explanation.Contains('%'));
    }

    private const string Boilerplate =
        "This repeated bounded example boilerplate is intentionally identical across otherwise unrelated documents.";

    private static RelationshipFileDocument Document(
        string id,
        string name,
        IReadOnlyList<string>? keywords = null,
        string? hash = null) => new()
        {
            FileId = id,
            SourceId = "source",
            SourceName = "Synthetic source",
            FullPath = "/synthetic/records/" + name,
            RelativePath = "records/" + name,
            FileName = name,
            FolderName = "records",
            Extension = Path.GetExtension(name),
            ContentHash = hash,
            CreationTimeUtc = Now,
            ModifiedTimeUtc = Now,
            MetadataText = "synthetic",
            Keywords = keywords ?? [],
            IsFullyIndexed = true,
        };

    private static RelationshipFileDocument WithTag(RelationshipFileDocument document, string tag) => document with
    {
        TagEvidence =
        [
            new RelationshipTagEvidence(
                tag,
                tag,
                SmartTagType.UserTag,
                ContentIntelligenceConfidence.Strong,
                SmartTagOrigin.User,
                SmartTagAssignmentState.Accepted,
                SmartTagDecision.Accepted),
        ],
    };

    private static RelationshipFileDocument WithConcept(
        RelationshipFileDocument document,
        params ContentConcept[] concepts) => document with
        {
            ContentIntelligence = new IndexedContentIntelligence
            {
                Topics = concepts.Where(item => item.Kind == ContentConceptKind.Topic).ToArray(),
                Entities = concepts.Where(item => item.Kind != ContentConceptKind.Topic).ToArray(),
                Provider = "synthetic",
                ProviderVersion = "1",
                ProcessingFingerprint = "synthetic",
            },
        };

    private static ContentConcept Entity(string value) => Concept(ContentConceptKind.Organization, value, false);

    private static ContentConcept Topic(string value, bool ai) => Concept(ContentConceptKind.Topic, value, ai);

    private static ContentConcept Concept(ContentConceptKind kind, string value, bool ai) => new()
    {
        Kind = kind,
        DisplayName = value,
        NormalizedValue = value.ToLowerInvariant(),
        Confidence = ContentIntelligenceConfidence.Strong,
        Provider = ai ? "ollama" : "deterministic",
        ProviderVersion = "1",
        Origin = ai ? ContentIntelligenceOrigin.AiDerived : ContentIntelligenceOrigin.Deterministic,
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
