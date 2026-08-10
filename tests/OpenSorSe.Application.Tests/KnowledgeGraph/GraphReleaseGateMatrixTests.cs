using OpenSorSe.Application.KnowledgeGraph;

namespace OpenSorSe.Application.Tests.KnowledgeGraph;

/// <summary>
/// Provides independent, deterministic oracles for high-risk normal-CI rows in the v2.0
/// automated-test matrix that are broader than one example-based unit test.
/// </summary>
public sealed class GraphReleaseGateMatrixTests
{
    // This is the acceptance table committed in CONCURRENCY_CANCELLATION_AND_RESOURCE_MODEL.md,
    // not a table inferred from GraphStateValidator.
    private static readonly IReadOnlyDictionary<GraphRunControlState, IReadOnlySet<GraphRunControlState>> DocumentedRunTransitions =
        new Dictionary<GraphRunControlState, IReadOnlySet<GraphRunControlState>>
        {
            [GraphRunControlState.Pending] = Set(GraphRunControlState.Running, GraphRunControlState.CancelRequested),
            [GraphRunControlState.Running] = Set(GraphRunControlState.PauseRequested, GraphRunControlState.CancelRequested, GraphRunControlState.Complete),
            [GraphRunControlState.PauseRequested] = Set(GraphRunControlState.Running, GraphRunControlState.Paused, GraphRunControlState.CancelRequested),
            [GraphRunControlState.Paused] = Set(GraphRunControlState.Running, GraphRunControlState.CancelRequested),
            [GraphRunControlState.CancelRequested] = Set(GraphRunControlState.Cancelled),
            [GraphRunControlState.Cancelled] = Set<GraphRunControlState>(),
            [GraphRunControlState.Complete] = Set<GraphRunControlState>(),
        };

    // Job cancellation/retry preconditions require provider context. This table verifies the
    // pairwise transitions currently documented by the provider-neutral contract; provider
    // tests remain responsible for attempt append-only and cancellation-intent preconditions.
    private static readonly IReadOnlyDictionary<GraphJobExecutionState, IReadOnlySet<GraphJobExecutionState>> DocumentedPairwiseJobTransitions =
        new Dictionary<GraphJobExecutionState, IReadOnlySet<GraphJobExecutionState>>
        {
            [GraphJobExecutionState.Pending] = Set(
                GraphJobExecutionState.Running,
                GraphJobExecutionState.Cancelled,
                GraphJobExecutionState.WaitingForDependency,
                GraphJobExecutionState.WaitingForResources,
                GraphJobExecutionState.PermanentFailure),
            [GraphJobExecutionState.Running] = Set(
                GraphJobExecutionState.Complete,
                GraphJobExecutionState.Cancelled,
                GraphJobExecutionState.RetryableFailure,
                GraphJobExecutionState.PermanentFailure,
                GraphJobExecutionState.WaitingForDependency,
                GraphJobExecutionState.WaitingForResources),
            [GraphJobExecutionState.RetryableFailure] = Set(GraphJobExecutionState.Pending, GraphJobExecutionState.Running, GraphJobExecutionState.Cancelled),
            [GraphJobExecutionState.PermanentFailure] = Set(GraphJobExecutionState.Pending, GraphJobExecutionState.Cancelled),
            [GraphJobExecutionState.WaitingForDependency] = Set(GraphJobExecutionState.Pending, GraphJobExecutionState.Running, GraphJobExecutionState.Cancelled, GraphJobExecutionState.PermanentFailure),
            [GraphJobExecutionState.WaitingForResources] = Set(GraphJobExecutionState.Pending, GraphJobExecutionState.Running, GraphJobExecutionState.Cancelled, GraphJobExecutionState.PermanentFailure),
            [GraphJobExecutionState.Cancelled] = Set<GraphJobExecutionState>(),
            [GraphJobExecutionState.Complete] = Set<GraphJobExecutionState>(),
        };

    /// <summary>Exhaustively validates every defined four-axis state vector against independent invariants.</summary>
    [Fact]
    public void StateValidator_EveryDefinedVector_MatchesIndependentOracle()
    {
        var validator = new GraphStateValidator();
        foreach (var run in Enum.GetValues<GraphRunControlState>())
            foreach (var job in Enum.GetValues<GraphJobExecutionState>())
                foreach (var freshness in Enum.GetValues<GraphFreshnessState>())
                    foreach (var integrity in Enum.GetValues<GraphIntegrityState>())
                    {
                        var state = new GraphStateVector(run, job, freshness, integrity);
                        var expected = IsValidVector(run, job);
                        var actual = validator.Validate(state);

                        Assert.True(
                            actual.IsValid == expected,
                            $"Unexpected state result for {run}/{job}/{freshness}/{integrity}: {actual.ErrorCode}.");
                    }
    }

    /// <summary>Exhaustively validates defined transitions against the committed concurrency-model rules.</summary>
    [Fact]
    public void StateValidator_EveryDefinedAxisTransition_MatchesCommittedConcurrencyModel()
    {
        var validator = new GraphStateValidator();
        foreach (var from in Enum.GetValues<GraphRunControlState>())
            foreach (var to in Enum.GetValues<GraphRunControlState>())
            {
                var before = State(from, GraphJobExecutionState.Complete);
                var after = State(to, GraphJobExecutionState.Complete);
                var expected = from == to || DocumentedRunTransitions[from].Contains(to);
                Assert.True(
                    validator.ValidateTransition(before, after).IsValid == expected,
                    $"Unexpected run transition result for {from} -> {to}.");
            }

        foreach (var from in Enum.GetValues<GraphJobExecutionState>())
            foreach (var to in Enum.GetValues<GraphJobExecutionState>())
            {
                var before = State(GraphRunControlState.Running, from);
                var after = State(GraphRunControlState.Running, to);
                var expected = from == to || DocumentedPairwiseJobTransitions[from].Contains(to);
                Assert.True(
                    validator.ValidateTransition(before, after).IsValid == expected,
                    $"Unexpected job transition result for {from} -> {to}.");
            }
    }

    /// <summary>Verifies unknown persisted values on every independent axis fail closed.</summary>
    [Fact]
    public void StateValidator_UnknownValueOnEveryAxis_IsRejectedAsCorruption()
    {
        var validator = new GraphStateValidator();
        var valid = State(GraphRunControlState.Running, GraphJobExecutionState.Complete);
        var invalid = new[]
        {
            valid with { RunControl = (GraphRunControlState)int.MaxValue },
            valid with { JobExecution = (GraphJobExecutionState)int.MaxValue },
            valid with { Freshness = (GraphFreshnessState)int.MaxValue },
            valid with { Integrity = (GraphIntegrityState)int.MaxValue },
        };

        Assert.All(invalid, state =>
        {
            var result = validator.Validate(state);
            Assert.False(result.IsValid);
            Assert.Equal("unknown-state", result.ErrorCode);
        });
    }

    /// <summary>Verifies cancellation at every pure durable stage leaves no published or running claim.</summary>
    [Theory]
    [InlineData(GraphProjectionStage.CandidatesExtracted)]
    [InlineData(GraphProjectionStage.CandidatesNormalized)]
    [InlineData(GraphProjectionStage.IdentityResolved)]
    [InlineData(GraphProjectionStage.EdgesPrepared)]
    [InlineData(GraphProjectionStage.ComponentValidated)]
    public async Task Projection_CancellationAtEachPureDurableStage_DoesNotPublish(
        GraphProjectionStage cancellationStage)
    {
        var source = new FakeGraphProjectionSource([TestGraphData.Source()]);
        var store = new FakeGraphStore();
        var decisions = new FakeGraphDecisionStore();
        using var cancellation = new CancellationTokenSource();
        store.StageAdvanceHook = stage =>
        {
            if (stage == cancellationStage)
            {
                cancellation.Cancel();
            }
        };
        await using var coordinator = Coordinator(source, store, decisions);
        await coordinator.InitializeAsync();
        var appliedBefore = store.Coverage.AppliedRevision;

        await Assert.ThrowsAsync<OperationCanceledException>(() => coordinator.ReconcileAsync(cancellation.Token));

        Assert.Empty(store.Components);
        Assert.DoesNotContain(GraphProjectionStage.ComponentPublished, store.StageHistory);
        Assert.Equal(appliedBefore, store.Coverage.AppliedRevision);
        Assert.Equal(GraphJobExecutionState.Cancelled, store.LastFailureState);
        Assert.Equal(0, (await store.GetStatusAsync()).RunningCount);
    }

    /// <summary>Verifies an unexpected poison job is observed while later valid work still projects.</summary>
    [Fact]
    public async Task Projection_UnexpectedPoisonJob_DoesNotBlockLaterValidObservation()
    {
        var poison = TestGraphData.Source("poison-source");
        var healthy = TestGraphData.Source("healthy-source");
        var source = new FakeGraphProjectionSource([poison, healthy]);
        var store = new FakeGraphStore();
        var decisions = new FakeGraphDecisionStore();
        var builder = new ThrowForStableKeyBuilder(
            new DeterministicGraphProjectionBuilder(new ConservativeGraphIdentityResolver()),
            poison.StableKey);
        await using var coordinator = Coordinator(source, store, decisions, builder);
        await coordinator.InitializeAsync();

        var result = await coordinator.ReconcileAsync();
        var status = await store.GetStatusAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, store.FailureCount);
        Assert.Equal(GraphJobExecutionState.PermanentFailure, store.LastFailureState);
        Assert.Equal(nameof(ArithmeticException), store.LastFailure?.Category);
        Assert.Equal(0, status.RunningCount);
        Assert.Equal(1, status.PermanentFailureCount);
        var component = Assert.Single(store.Components);
        Assert.Contains(healthy.StableKey, component.ComponentKey, StringComparison.Ordinal);
        Assert.DoesNotContain(store.Components, item => item.ComponentKey.Contains(poison.StableKey, StringComparison.Ordinal));
    }

    /// <summary>Verifies cancellation interrupts a resource adapter wait without claiming or publishing work.</summary>
    [Fact]
    public async Task Projection_CancellationDuringResourceWait_ExitsWithoutClaimOrWatermarkAdvance()
    {
        var source = new FakeGraphProjectionSource([TestGraphData.Source()]);
        var store = new FakeGraphStore();
        var decisions = new FakeGraphDecisionStore();
        var policy = new BlockingResourcePolicy();
        await using var coordinator = Coordinator(source, store, decisions, resourcePolicy: policy);
        await coordinator.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        var appliedBefore = store.Coverage.AppliedRevision;

        var reconciliation = coordinator.ReconcileAsync(cancellation.Token);
        await policy.Entered.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reconciliation);
        var status = await store.GetStatusAsync();
        Assert.Equal(0, status.RunningCount);
        Assert.Equal(0, status.PendingCount);
        Assert.Empty(store.Components);
        Assert.Equal(appliedBefore, store.Coverage.AppliedRevision);
        Assert.Equal(0, source.ReadCount);
    }

    private static bool IsValidVector(GraphRunControlState run, GraphJobExecutionState job)
    {
        var activeOrWaiting = job is GraphJobExecutionState.Pending or GraphJobExecutionState.Running or
            GraphJobExecutionState.WaitingForDependency or GraphJobExecutionState.WaitingForResources;
        if ((run is GraphRunControlState.Cancelled or GraphRunControlState.Complete) && activeOrWaiting)
        {
            return false;
        }

        return job != GraphJobExecutionState.Running ||
            run is GraphRunControlState.Running or GraphRunControlState.PauseRequested or GraphRunControlState.CancelRequested;
    }

    private static GraphStateVector State(GraphRunControlState run, GraphJobExecutionState job) =>
        new(run, job, GraphFreshnessState.Current, GraphIntegrityState.Valid);

    private static IReadOnlySet<T> Set<T>(params T[] values) where T : notnull => new HashSet<T>(values);

    private static GraphProjectionCoordinator Coordinator(
        FakeGraphProjectionSource source,
        FakeGraphStore store,
        FakeGraphDecisionStore decisions,
        IGraphProjectionBuilder? builder = null,
        IGraphResourceAdmissionPolicy? resourcePolicy = null) => new(
            source,
            store,
            decisions,
            builder ?? new DeterministicGraphProjectionBuilder(new ConservativeGraphIdentityResolver()),
            new DeterministicGraphDecisionProjectionBuilder(new ConservativeGraphIdentityResolver()),
            resourcePolicy ?? new FakeGraphResourcePolicy(),
            new FixedGraphTimeProvider(TestGraphData.Now),
            "release-gate-owner");

    private sealed class ThrowForStableKeyBuilder(
        IGraphProjectionBuilder inner,
        string poisonStableKey) : IGraphProjectionBuilder
    {
        public GraphComponentProjection Build(
            GraphProjectionObservation observation,
            GraphProjectionSnapshot snapshot,
            DateTimeOffset validatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(observation.StableKey, poisonStableKey, StringComparison.Ordinal))
            {
                throw new ArithmeticException("Synthetic unexpected projection failure.");
            }

            return inner.Build(observation, snapshot, validatedAtUtc, cancellationToken);
        }
    }

    private sealed class BlockingResourcePolicy : IGraphResourceAdmissionPolicy
    {
        internal TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GraphResourceEligibility> GetEligibilityAsync(
            GraphControlSettings settings,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation-aware synthetic resource wait unexpectedly resumed.");
        }
    }
}
