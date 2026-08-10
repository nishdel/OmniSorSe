using System.Diagnostics;
using OpenSorSe.Application.KnowledgeGraph;

namespace OpenSorSe.Application.Tests.KnowledgeGraph;

/// <summary>
/// Provides reproducible, deliberately generous regression ceilings for isolated Knowledge Graph algorithms.
/// These samples are not end-to-end throughput measurements or universal desktop performance claims.
/// </summary>
public sealed class KnowledgeGraphPerformanceRegressionTests
{
    private static readonly int[] ProjectionScales = [500, 2_000, 5_000];
    private static readonly int[] IdentityScales = [1_000, 10_000, 50_000];

    /// <summary>Samples per-observation projection at several synthetic scales and guards against a large cost-shape regression.</summary>
    [Fact]
    [Trait("Category", "PerformanceRegression")]
    public void MechanicalProjection_MultiScaleCostRemainsWithinRegressionCeilings()
    {
        var builder = new DeterministicGraphProjectionBuilder(new ConservativeGraphIdentityResolver());
        _ = builder.Build(TestGraphData.File("warmup"), Snapshot(1), TestGraphData.Now);
        var samples = ProjectionScales
            .Select(count => MeasureProjection(builder, count))
            .ToArray();

        foreach (var sample in samples)
        {
            Assert.Equal(sample.ItemCount * 9L, sample.OutputCount);
            Assert.True(
                sample.Elapsed < TimeSpan.FromSeconds(15),
                $"Projection of {sample.ItemCount:N0} synthetic observations took {sample.Elapsed}.");
            Assert.True(
                sample.AllocatedBytes < 512L * 1024L * 1024L,
                $"Projection allocated {sample.AllocatedBytes:N0} bytes for {sample.ItemCount:N0} synthetic observations.");
        }

        AssertGenerousCostShape(samples, "mechanical projection");
    }

    /// <summary>Samples independent stable-identity resolutions at several scales and guards their deterministic cost shape.</summary>
    [Fact]
    [Trait("Category", "PerformanceRegression")]
    public void StableIdentityResolution_MultiScaleCostRemainsWithinRegressionCeilings()
    {
        var resolver = new ConservativeGraphIdentityResolver();
        _ = resolver.Resolve(IdentityInput("warmup"));
        var samples = IdentityScales
            .Select(count => MeasureIdentityResolution(resolver, count))
            .ToArray();

        foreach (var sample in samples)
        {
            Assert.Equal(0, sample.FailureCount);
            Assert.Equal((long)sample.ItemCount, sample.OutputCount);
            Assert.NotEqual(sample.FirstOutput, sample.LastOutput);
            Assert.True(
                sample.Elapsed < TimeSpan.FromSeconds(15),
                $"Resolution of {sample.ItemCount:N0} synthetic identities took {sample.Elapsed}.");
            Assert.True(
                sample.AllocatedBytes < 512L * 1024L * 1024L,
                $"Identity resolution allocated {sample.AllocatedBytes:N0} bytes for {sample.ItemCount:N0} keys.");
        }

        AssertGenerousCostShape(samples, "stable identity resolution");
    }

    /// <summary>Already-requested cancellation is observed before deterministic projection allocates a component.</summary>
    [Fact]
    [Trait("Category", "PerformanceRegression")]
    public void MechanicalProjection_AlreadyCancelledRequestCompletesWithinLatencyCeiling()
    {
        var builder = new DeterministicGraphProjectionBuilder(new ConservativeGraphIdentityResolver());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var stopwatch = Stopwatch.StartNew();

        Assert.ThrowsAny<OperationCanceledException>(() => builder.Build(
            TestGraphData.File(),
            Snapshot(1),
            TestGraphData.Now,
            cancellation.Token));

        stopwatch.Stop();
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(250),
            $"Already-requested cancellation took {stopwatch.Elapsed}.");
    }

    private static GraphProjectionSnapshot Snapshot(int count) => new(
        "performance-manifest",
        1,
        "legacy-performance",
        1,
        TestGraphData.Now,
        "performance-manifest-hash",
        count,
        [new GraphObservationKindCount(GraphProjectionObservationKind.File, count)]);

    private static CostSample MeasureProjection(
        DeterministicGraphProjectionBuilder builder,
        int observationCount)
    {
        var snapshot = Snapshot(observationCount);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        long projectedRows = 0;

        for (var index = 0; index < observationCount; index++)
        {
            var component = builder.Build(
                TestGraphData.File($"synthetic-{index:D5}"),
                snapshot,
                TestGraphData.Now);
            projectedRows += component.Nodes.Count + component.Edges.Count + component.Facts.Count;
        }

        stopwatch.Stop();
        return new CostSample(
            observationCount,
            projectedRows,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            stopwatch.Elapsed);
    }

    private static IdentityCostSample MeasureIdentityResolution(
        ConservativeGraphIdentityResolver resolver,
        int identityCount)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        string? first = null;
        string? last = null;
        var resolved = 0;
        var failures = 0;

        for (var index = 0; index < identityCount; index++)
        {
            var result = resolver.Resolve(IdentityInput($"file-{index:D5}"));
            if (result.Status == GraphIdentityResolutionStatus.Resolved && result.NodeId is not null)
            {
                resolved++;
                first ??= result.NodeId;
                last = result.NodeId;
            }
            else
            {
                failures++;
            }
        }

        stopwatch.Stop();
        return new IdentityCostSample(
            identityCount,
            resolved,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            stopwatch.Elapsed,
            failures,
            first,
            last);
    }

    private static GraphIdentityInput IdentityInput(string key) => new()
    {
        Kind = GraphNodeKind.File,
        Scope = "file",
        CanonicalKey = key,
        ExistingStableId = key,
        NormalizationVersion = "existing-id-v1",
    };

    private static void AssertGenerousCostShape<TSample>(
        IReadOnlyList<TSample> samples,
        string operation)
        where TSample : CostSample
    {
        for (var index = 1; index < samples.Count; index++)
        {
            var smaller = samples[index - 1];
            var larger = samples[index];
            var scale = larger.ItemCount / (double)smaller.ItemCount;
            var maximumAllocation = checked((long)(smaller.AllocatedBytes * scale * 2) + (16L * 1024L * 1024L));
            var maximumElapsedTicks = Math.Max(
                (long)(smaller.Elapsed.Ticks * scale * 4),
                TimeSpan.FromSeconds(2).Ticks);
            Assert.True(
                larger.AllocatedBytes <= maximumAllocation,
                $"{operation} allocation changed from {smaller.AllocatedBytes:N0} bytes for {smaller.ItemCount:N0} items " +
                $"to {larger.AllocatedBytes:N0} bytes for {larger.ItemCount:N0} items.");
            Assert.True(
                larger.Elapsed.Ticks <= maximumElapsedTicks,
                $"{operation} elapsed time changed from {smaller.Elapsed} for {smaller.ItemCount:N0} items " +
                $"to {larger.Elapsed} for {larger.ItemCount:N0} items.");
        }
    }

    private record CostSample(
        int ItemCount,
        long OutputCount,
        long AllocatedBytes,
        TimeSpan Elapsed);

    private sealed record IdentityCostSample(
        int ItemCount,
        long OutputCount,
        long AllocatedBytes,
        TimeSpan Elapsed,
        int FailureCount,
        string? FirstOutput,
        string? LastOutput)
        : CostSample(ItemCount, OutputCount, AllocatedBytes, Elapsed);
}
