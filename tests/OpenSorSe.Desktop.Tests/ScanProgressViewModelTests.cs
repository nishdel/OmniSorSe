using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Desktop.Tests;

/// <summary>
/// Verifies passive scan-progress presentation and cancellation signaling.
/// </summary>
public sealed class ScanProgressViewModelTests
{
    /// <summary>
    /// Verifies starting and applying a scanner snapshot updates only presentation state.
    /// </summary>
    [Fact]
    public void ApplyProgress_UpdatesPresentedScannerSnapshot()
    {
        var viewModel = new ScanProgressViewModel();
        viewModel.Start();

        viewModel.ApplyProgress(new ScanProgress("C:\\Scan", new ScanStatistics(4, 2, 1), TimeSpan.FromSeconds(3)));

        Assert.Equal(ScanProgressStage.Scanning, viewModel.Stage);
        Assert.Equal("C:\\Scan", viewModel.CurrentFolder);
        Assert.Equal(4L, viewModel.FilesFound);
        Assert.Equal(2L, viewModel.FoldersScanned);
        Assert.Equal(TimeSpan.FromSeconds(3), viewModel.Elapsed);
    }

    /// <summary>Verifies ETA remains withheld until comparable work supplies enough deterministic samples.</summary>
    [Fact]
    public void Estimate_AppearsOnlyAfterSufficientComparableWork()
    {
        using var viewModel = new ScanProgressViewModel(new MutableTimeProvider());
        viewModel.Start();

        viewModel.ApplyProgress(new ScanProgress(
            "C:\\Scan",
            new ScanStatistics(1, 1, 0),
            TimeSpan.Zero,
            0,
            12,
            "root:one"));
        Assert.Equal("Estimating…", viewModel.EstimatedRemainingText);

        viewModel.ApplyProgress(new ScanProgress(
            "C:\\Scan\\five",
            new ScanStatistics(8, 8, 0),
            TimeSpan.FromSeconds(4),
            6,
            6,
            "root:one"));

        Assert.NotNull(viewModel.EstimatedRemaining);
        Assert.Contains("remaining", viewModel.EstimatedRemainingText, StringComparison.Ordinal);
        Assert.Contains(viewModel.EstimatedRemainingText!, viewModel.StatusText, StringComparison.Ordinal);
    }

    /// <summary>Verifies changing workload class discards a stale rate instead of mixing heterogeneous work.</summary>
    [Fact]
    public void Estimate_WorkloadChangeResetsToEstimating()
    {
        using var viewModel = new ScanProgressViewModel(new MutableTimeProvider());
        viewModel.Start();
        viewModel.ApplyProgress(new ScanProgress(null, new ScanStatistics(0, 1, 0), TimeSpan.Zero, 0, 10, "root:one"));
        viewModel.ApplyProgress(new ScanProgress(null, new ScanStatistics(6, 7, 0), TimeSpan.FromSeconds(3), 6, 4, "root:one"));
        Assert.NotNull(viewModel.EstimatedRemaining);

        viewModel.ApplyProgress(new ScanProgress(null, new ScanStatistics(6, 8, 0), TimeSpan.FromSeconds(4), 0, 20, "root:two"));

        Assert.Null(viewModel.EstimatedRemaining);
        Assert.Equal("Estimating…", viewModel.EstimatedRemainingText);
    }

    /// <summary>Verifies indeterminate scans and terminal states never retain a manufactured ETA.</summary>
    [Fact]
    public void Estimate_IndeterminateAndTerminalStatesDoNotDisplayRemainingTime()
    {
        using var viewModel = new ScanProgressViewModel(new MutableTimeProvider());
        viewModel.Start();
        viewModel.ApplyProgress(new ScanProgress(null, new ScanStatistics(2, 1, 0), TimeSpan.FromSeconds(3)));
        Assert.Null(viewModel.EstimatedRemainingText);

        viewModel.ApplyProgress(new ScanProgress(null, new ScanStatistics(8, 8, 0), TimeSpan.FromSeconds(6), 8, 4, "root"));
        viewModel.ApplyProgress(new ScanProgress(null, new ScanStatistics(12, 12, 0), TimeSpan.FromSeconds(8), 12, 0, "root"));
        viewModel.Complete(ScanStatus.Completed);

        Assert.Null(viewModel.EstimatedRemaining);
        Assert.DoesNotContain("remaining", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies cancellation and failure both clear a previously established estimate.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Estimate_CancellationAndFailureClearRemainingTime(bool cancel)
    {
        using var viewModel = new ScanProgressViewModel(new MutableTimeProvider());
        viewModel.Start();
        viewModel.ApplyProgress(new ScanProgress(null, new ScanStatistics(0, 1, 0), TimeSpan.Zero, 0, 12, "root"));
        viewModel.ApplyProgress(new ScanProgress(null, new ScanStatistics(8, 8, 0), TimeSpan.FromSeconds(4), 8, 4, "root"));
        Assert.NotNull(viewModel.EstimatedRemaining);

        if (cancel)
        {
            viewModel.Complete(ScanStatus.Cancelled);
        }
        else
        {
            viewModel.Fail("Reader failed.");
        }

        Assert.Null(viewModel.EstimatedRemaining);
        Assert.DoesNotContain("remaining", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies elapsed time continues through processing stages after scanner snapshots stop.</summary>
    [Fact]
    public void Elapsed_UsesMonotonicOperationLifetimeAndFreezesAtCompletion()
    {
        var time = new MutableTimeProvider();
        using var viewModel = new ScanProgressViewModel(time);
        viewModel.Start();
        time.Advance(TimeSpan.FromSeconds(18.4));

        viewModel.RefreshElapsed();

        Assert.Equal(TimeSpan.FromSeconds(18.4), viewModel.Elapsed);
        Assert.Contains("18.4 s elapsed", viewModel.StatusText, StringComparison.Ordinal);

        viewModel.Complete(ScanStatus.Completed);
        time.Advance(TimeSpan.FromSeconds(10));
        viewModel.RefreshElapsed();

        Assert.Equal(TimeSpan.FromSeconds(18.4), viewModel.Elapsed);
        Assert.Equal("Completed in 18.4 s", viewModel.StatusText);
    }

    /// <summary>Verifies failure and cancellation freeze truthful operation duration.</summary>
    [Theory]
    [InlineData(true, ScanProgressStage.Cancelled, "Cancelled after 42.7 s")]
    [InlineData(false, ScanProgressStage.Failed, "Reader failed. · failed after 42.7 s")]
    public void TerminalState_FreezesElapsed(bool cancel, ScanProgressStage expectedStage, string expectedText)
    {
        var time = new MutableTimeProvider();
        using var viewModel = new ScanProgressViewModel(time);
        viewModel.Start();
        time.Advance(TimeSpan.FromSeconds(42.7));

        if (cancel)
        {
            viewModel.Complete(ScanStatus.Cancelled);
        }
        else
        {
            viewModel.Fail("Reader failed.");
        }

        Assert.Equal(expectedStage, viewModel.Stage);
        Assert.Equal(expectedText, viewModel.StatusText);
    }

    /// <summary>
    /// Verifies terminal scanner statuses are mapped to deterministic presentation stages.
    /// </summary>
    [Theory]
    [InlineData(ScanStatus.Completed, ScanProgressStage.Completed, "Scan completed.")]
    [InlineData(ScanStatus.Cancelled, ScanProgressStage.Cancelled, "Scan cancelled.")]
    public void Complete_MapsTerminalScannerStatus(ScanStatus status, ScanProgressStage expectedStage, string expectedStatus)
    {
        var viewModel = new ScanProgressViewModel();

        viewModel.Complete(status);

        Assert.Equal(expectedStage, viewModel.Stage);
        Assert.Equal(expectedStatus, viewModel.StatusText);
        Assert.False(viewModel.IsActive);
    }

    /// <summary>Verifies a pre-start failure cannot reuse the previous operation's frozen duration.</summary>
    [Fact]
    public void PreStartFailure_AfterCompletedScan_DoesNotReuseFrozenDuration()
    {
        var time = new MutableTimeProvider();
        using var viewModel = new ScanProgressViewModel(time);
        viewModel.Start();
        time.Advance(TimeSpan.FromSeconds(18.4));
        viewModel.Complete(ScanStatus.Completed);

        viewModel.Fail("The workflow is unavailable.");

        Assert.Equal(TimeSpan.Zero, viewModel.Elapsed);
        Assert.Equal("The workflow is unavailable.", viewModel.StatusText);
    }

    /// <summary>
    /// Verifies cancellation is emitted only for an active scan presentation.
    /// </summary>
    [Fact]
    public void RequestCancellation_EmitsOnlyWhileActive()
    {
        var viewModel = new ScanProgressViewModel();
        var requests = 0;
        viewModel.CancelRequested += (_, _) => requests++;

        viewModel.RequestCancellation();
        viewModel.Start();
        viewModel.RequestCancellation();
        viewModel.Complete(ScanStatus.Cancelled);
        viewModel.RequestCancellation();

        Assert.Equal(1, requests);
    }

    /// <summary>
    /// Verifies unsupported scanner statuses are rejected without mutating progress state.
    /// </summary>
    [Fact]
    public void Complete_UnsupportedStatus_Throws()
    {
        var viewModel = new ScanProgressViewModel();

        Assert.Throws<ArgumentOutOfRangeException>(() => viewModel.Complete((ScanStatus)999));

        Assert.Equal(ScanProgressStage.Idle, viewModel.Stage);
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => 1_000;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration) =>
            _timestamp += (long)(duration.TotalSeconds * TimestampFrequency);
    }
}
