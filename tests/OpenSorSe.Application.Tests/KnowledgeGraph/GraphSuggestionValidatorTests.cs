using OpenSorSe.Application.KnowledgeGraph;

namespace OpenSorSe.Application.Tests.KnowledgeGraph;

/// <summary>Validates optional suggestions remain bounded, deterministic, inactive, and confirmation-required.</summary>
public sealed class GraphSuggestionValidatorTests
{
    private readonly StrictGraphSuggestionValidator _validator = new();

    /// <summary>Verifies optional suggestion generation is disabled by default.</summary>
    [Fact]
    public void Validate_DefaultOptions_ReturnsNoSuggestions()
    {
        Assert.Empty(_validator.Validate([Candidate()]));
    }

    /// <summary>Verifies identical provider output produces one stable confirmation-required suggestion.</summary>
    [Fact]
    public void Validate_IdenticalInput_IsDeterministicAndInactive()
    {
        var options = new GraphSuggestionOptions(Enabled: true);

        var first = Assert.Single(_validator.Validate([Candidate()], options));
        var second = Assert.Single(_validator.Validate([Candidate()], options));

        Assert.Equal(first.SuggestionId, second.SuggestionId);
        Assert.Equal(first.Kind, second.Kind);
        Assert.Equal(first.Scope, second.Scope);
        Assert.Equal(first.Label, second.Label);
        Assert.Equal(first.ProviderVersion, second.ProviderVersion);
        Assert.Equal(first.SourceStableKeys, second.SourceStableKeys);
        Assert.Equal(first.EvidenceStableKeys, second.EvidenceStableKeys);
        Assert.True(first.RequiresConfirmation);
        Assert.StartsWith("kg:suggestion:", first.SuggestionId, StringComparison.Ordinal);
    }

    /// <summary>Verifies source and evidence order do not change the stable suggestion identity.</summary>
    [Fact]
    public void Validate_KeyOrdering_IsCanonical()
    {
        var first = Candidate() with
        {
            SourceStableKeys = ["file-2", "file-1"],
            EvidenceStableKeys = ["evidence-2", "evidence-1"],
        };
        var second = first with
        {
            SourceStableKeys = ["file-1", "file-2"],
            EvidenceStableKeys = ["evidence-1", "evidence-2"],
        };

        var accepted = _validator.Validate([first, second], new GraphSuggestionOptions(Enabled: true));

        Assert.Single(accepted);
    }

    /// <summary>Verifies malformed untrusted labels, keys, and provider versions fail closed.</summary>
    [Theory]
    [InlineData("bad\0label", "file-1", "provider-v1")]
    [InlineData("Alpha", "bad\0key", "provider-v1")]
    [InlineData("Alpha", "file-1", "bad\0provider")]
    public void Validate_MalformedProviderOutput_IsRejected(string label, string sourceKey, string providerVersion)
    {
        var candidate = Candidate() with
        {
            Label = label,
            SourceStableKeys = [sourceKey],
            ProviderVersion = providerVersion,
        };

        Assert.Empty(_validator.Validate([candidate], new GraphSuggestionOptions(Enabled: true)));
    }

    /// <summary>Verifies duplicate keys cannot inflate evidence or source support.</summary>
    [Fact]
    public void Validate_DuplicateSupportKeys_IsRejected()
    {
        var candidate = Candidate() with { SourceStableKeys = ["file-1", "file-1"] };

        Assert.Empty(_validator.Validate([candidate], new GraphSuggestionOptions(Enabled: true)));
    }

    /// <summary>Malformed provider collections fail closed rather than escaping as a null-reference failure.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_NullProviderCollection_IsRejected(bool nullSourceKeys)
    {
        var candidate = nullSourceKeys
            ? Candidate() with { SourceStableKeys = null! }
            : Candidate() with { EvidenceStableKeys = null! };

        Assert.Empty(_validator.Validate([candidate], new GraphSuggestionOptions(Enabled: true)));
    }

    /// <summary>Callers cannot raise the optional suggestion source-key ceiling above the stable design bound.</summary>
    [Fact]
    public void Validate_SourceKeyOptionAboveStableBound_IsRejected()
    {
        var options = new GraphSuggestionOptions(
            Enabled: true,
            MaximumSourceKeysPerSuggestion: GraphLimits.MaximumSuggestionSourceKeys + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => _validator.Validate([Candidate()], options));
    }

    /// <summary>Verifies configured and global candidate ceilings remain enforced.</summary>
    [Fact]
    public void Validate_MaximumSuggestions_BoundsOutput()
    {
        var candidates = Enumerable.Range(0, 20)
            .Select(index => Candidate() with { Label = string.Concat("Alpha ", index) })
            .ToArray();

        var accepted = _validator.Validate(candidates, new GraphSuggestionOptions(Enabled: true, MaximumSuggestions: 3));

        Assert.Equal(3, accepted.Count);
    }

    /// <summary>Verifies cancellation is checked between untrusted candidates.</summary>
    [Fact]
    public void Validate_Cancelled_StopsPromptly()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            _validator.Validate([Candidate()], new GraphSuggestionOptions(Enabled: true), cancellation.Token));
    }

    private static GraphSuggestionCandidate Candidate() => new(
        GraphSuggestionKind.Project,
        "source-1",
        "Alpha",
        ["file-1"],
        ["evidence-1"],
        "provider-v1");
}
