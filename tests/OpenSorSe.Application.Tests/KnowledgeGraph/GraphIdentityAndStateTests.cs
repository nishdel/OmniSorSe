using OpenSorSe.Application.KnowledgeGraph;

namespace OpenSorSe.Application.Tests.KnowledgeGraph;

/// <summary>Validates conservative identity and independent durable state axes.</summary>
public sealed class GraphIdentityAndStateTests
{
    /// <summary>Verifies existing provider identities resolve deterministically for every stable reused kind.</summary>
    [Theory]
    [InlineData("file")]
    [InlineData("source")]
    [InlineData("collection")]
    [InlineData("manual-entity")]
    public void Resolve_ExistingStableIdentity_IsDeterministic(string kindValue)
    {
        var resolver = new ConservativeGraphIdentityResolver();
        var kind = new GraphNodeKind(kindValue);
        var input = new GraphIdentityInput
        {
            Kind = kind,
            Scope = kindValue,
            CanonicalKey = kind == GraphNodeKind.ManualEntity ? "manual:stable-123" : "stable-123",
            ExistingStableId = kind == GraphNodeKind.ManualEntity ? "manual:stable-123" : "stable-123",
            NormalizationVersion = "existing-id-v1",
        };

        var first = resolver.Resolve(input);
        var second = resolver.Resolve(input);

        Assert.Equal(GraphIdentityResolutionStatus.Resolved, first.Status);
        Assert.Equal(first, second);
        Assert.Contains("stable-123", first.CanonicalInputs, StringComparison.Ordinal);
        if (kind == GraphNodeKind.ManualEntity)
        {
            Assert.Equal("manual:stable-123", first.NodeId);
        }
        else
        {
            Assert.StartsWith("kg:" + kindValue + ":", first.NodeId, StringComparison.Ordinal);
        }
    }

    /// <summary>Verifies case-insensitive folder identity normalizes slash and case differences.</summary>
    [Fact]
    public void Resolve_FolderCaseInsensitive_ReusesIdentity()
    {
        var resolver = new ConservativeGraphIdentityResolver();
        var first = resolver.Resolve(Folder("Records/Tax"));
        var second = resolver.Resolve(Folder("records\\tax"));

        Assert.Equal(GraphIdentityResolutionStatus.Resolved, first.Status);
        Assert.Equal(first.NodeId, second.NodeId);
        Assert.Equal(first.CanonicalInputs, second.CanonicalInputs);
    }

    /// <summary>Verifies case-sensitive sources keep distinct valid relative folder identities.</summary>
    [Fact]
    public void Resolve_FolderCaseSensitive_PreservesCase()
    {
        var resolver = new ConservativeGraphIdentityResolver();
        var first = resolver.Resolve(Folder("Records", GraphPathComparison.CaseSensitive));
        var second = resolver.Resolve(Folder("records", GraphPathComparison.CaseSensitive));

        Assert.NotEqual(first.NodeId, second.NodeId);
    }

    /// <summary>Verifies absolute and traversal paths fail closed rather than becoming folder identities.</summary>
    [Theory]
    [InlineData("/outside")]
    [InlineData("C:\\outside")]
    [InlineData("safe/../outside")]
    [InlineData("safe/./child")]
    public void Resolve_UnsafeFolder_IsRepairRequired(string path)
    {
        var result = new ConservativeGraphIdentityResolver().Resolve(Folder(path));

        Assert.Equal(GraphIdentityResolutionStatus.RepairRequired, result.Status);
        Assert.Null(result.NodeId);
    }

    /// <summary>Verifies exact content identity includes the hash algorithm version.</summary>
    [Fact]
    public void Resolve_DocumentSet_AlgorithmVersionAffectsIdentity()
    {
        var resolver = new ConservativeGraphIdentityResolver();
        var hash = new string('a', 64);
        var first = resolver.Resolve(DocumentSet(hash, "sha256-v1"));
        var second = resolver.Resolve(DocumentSet(hash, "sha256-v2"));

        Assert.Equal(GraphIdentityResolutionStatus.Resolved, first.Status);
        Assert.NotEqual(first.NodeId, second.NodeId);
    }

    /// <summary>Verifies malformed and non-hex content hashes do not create document sets.</summary>
    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("0123456789abcdeg")]
    public void Resolve_InvalidDocumentHash_IsRepairRequired(string hash)
    {
        var result = new ConservativeGraphIdentityResolver().Resolve(DocumentSet(hash, "sha256-v1"));

        Assert.Equal(GraphIdentityResolutionStatus.RepairRequired, result.Status);
    }

    /// <summary>Verifies real-world entity kinds remain confirmation-required suggestions.</summary>
    [Theory]
    [InlineData("project")]
    [InlineData("organization")]
    [InlineData("purchase")]
    [InlineData("trip")]
    [InlineData("person")]
    [InlineData("place")]
    [InlineData("event")]
    [InlineData("topic")]
    public void Resolve_RealWorldCandidate_RequiresConfirmation(string code)
    {
        var result = new ConservativeGraphIdentityResolver().Resolve(new GraphIdentityInput
        {
            Kind = new GraphNodeKind(code),
            Scope = "source-1",
            CanonicalKey = "candidate",
            NormalizationVersion = "candidate-v1",
        });

        Assert.Equal(GraphIdentityResolutionStatus.SuggestionRequired, result.Status);
        Assert.Null(result.NodeId);
    }

    /// <summary>Verifies malformed Unicode fails closed at the identity boundary.</summary>
    [Fact]
    public void Resolve_MalformedUnicode_IsRepairRequired()
    {
        var result = new ConservativeGraphIdentityResolver().Resolve(new GraphIdentityInput
        {
            Kind = GraphNodeKind.File,
            Scope = "file",
            CanonicalKey = "bad\ud800",
            ExistingStableId = "bad\ud800",
            NormalizationVersion = "existing-id-v1",
        });

        Assert.Equal(GraphIdentityResolutionStatus.RepairRequired, result.Status);
    }

    /// <summary>Verifies persisted stable IDs are never silently trimmed or Unicode-normalized.</summary>
    [Theory]
    [InlineData(" stable-id")]
    [InlineData("stable-id ")]
    [InlineData("manual:e\u0301")]
    public void Resolve_NoncanonicalExistingIdentity_IsRepairRequired(string stableId)
    {
        var kind = stableId.StartsWith("manual:", StringComparison.Ordinal)
            ? GraphNodeKind.ManualEntity
            : GraphNodeKind.File;

        var result = new ConservativeGraphIdentityResolver().Resolve(new GraphIdentityInput
        {
            Kind = kind,
            Scope = "test",
            CanonicalKey = stableId,
            ExistingStableId = stableId,
            NormalizationVersion = "test-v1",
        });

        Assert.Equal(GraphIdentityResolutionStatus.RepairRequired, result.Status);
        Assert.Equal("noncanonical-stable-id", result.ReasonCode);
    }

    /// <summary>Verifies a legal running-to-paused drain transition is accepted.</summary>
    [Fact]
    public void ValidateTransition_RunningToPauseRequested_IsValid()
    {
        var validator = new GraphStateValidator();
        var before = State(GraphRunControlState.Running, GraphJobExecutionState.Running);
        var after = State(GraphRunControlState.PauseRequested, GraphJobExecutionState.Running);

        Assert.True(validator.ValidateTransition(before, after).IsValid);
    }

    /// <summary>Verifies a running job cannot be persisted under a paused run.</summary>
    [Fact]
    public void Validate_PausedWithRunningJob_IsInvalid()
    {
        var result = new GraphStateValidator().Validate(State(GraphRunControlState.Paused, GraphJobExecutionState.Running));

        Assert.False(result.IsValid);
        Assert.Equal("running-without-admission", result.ErrorCode);
    }

    /// <summary>Verifies terminal cancellation cannot retain waiting or claimable work.</summary>
    [Theory]
    [InlineData(GraphJobExecutionState.Pending)]
    [InlineData(GraphJobExecutionState.Running)]
    [InlineData(GraphJobExecutionState.WaitingForDependency)]
    [InlineData(GraphJobExecutionState.WaitingForResources)]
    public void Validate_CancelledWithActiveWork_IsInvalid(GraphJobExecutionState job)
    {
        var result = new GraphStateValidator().Validate(State(GraphRunControlState.Cancelled, job));

        Assert.False(result.IsValid);
        Assert.Equal("cancel-not-acknowledged", result.ErrorCode);
    }

    /// <summary>Verifies a terminal run cannot be rewritten in place.</summary>
    [Theory]
    [InlineData(GraphRunControlState.Complete)]
    [InlineData(GraphRunControlState.Cancelled)]
    public void ValidateTransition_TerminalRunCannotRestart(GraphRunControlState terminal)
    {
        var validator = new GraphStateValidator();
        var before = State(terminal, GraphJobExecutionState.Complete);
        var after = State(GraphRunControlState.Running, GraphJobExecutionState.Complete);

        var result = validator.ValidateTransition(before, after);

        Assert.False(result.IsValid);
        Assert.Equal("illegal-run-transition", result.ErrorCode);
    }

    /// <summary>Verifies freshness and integrity may change independently under legal transitions.</summary>
    [Fact]
    public void ValidateTransition_FreshnessAndIntegrity_AreIndependent()
    {
        var validator = new GraphStateValidator();
        var before = State(GraphRunControlState.Running, GraphJobExecutionState.Complete);
        var after = before with
        {
            Freshness = GraphFreshnessState.Stale,
            Integrity = GraphIntegrityState.RepairRequired,
        };

        Assert.True(validator.ValidateTransition(before, after).IsValid);
    }

    private static GraphIdentityInput Folder(
        string path,
        GraphPathComparison comparison = GraphPathComparison.CaseInsensitive) => new()
        {
            Kind = GraphNodeKind.Folder,
            Scope = "source-1",
            CanonicalKey = path,
            NormalizationVersion = "path-v1",
            PathComparison = comparison,
        };

    private static GraphIdentityInput DocumentSet(string hash, string algorithm) => new()
    {
        Kind = GraphNodeKind.DocumentSet,
        Scope = "content",
        CanonicalKey = hash,
        NormalizationVersion = "exact-v1",
        HashAlgorithmVersion = algorithm,
    };

    private static GraphStateVector State(GraphRunControlState run, GraphJobExecutionState job) =>
        new(run, job, GraphFreshnessState.Current, GraphIntegrityState.Valid);
}
