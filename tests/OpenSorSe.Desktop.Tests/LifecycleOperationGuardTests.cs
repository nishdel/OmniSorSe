using OpenSorSe.Desktop;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Protects the process-boundary exception-containment contract.</summary>
public sealed class LifecycleOperationGuardTests
{
    /// <summary>Verifies a shutdown fault is observed and cannot escape the desktop event boundary.</summary>
    [Fact]
    public void TryExecute_ReportsLifecycleFailureWithoutRethrowing()
    {
        var expected = new InvalidOperationException("synthetic lifecycle failure");
        string? reportedOperation = null;
        Exception? reportedException = null;

        var completed = LifecycleOperationGuard.TryExecute(
            "service-disposal",
            () => throw expected,
            (operation, exception) =>
            {
                reportedOperation = operation;
                reportedException = exception;
            });

        Assert.False(completed);
        Assert.Equal("service-disposal", reportedOperation);
        Assert.Same(expected, reportedException);
    }

    /// <summary>Verifies successful lifecycle work is not reported as a failure.</summary>
    [Fact]
    public void TryExecute_ReturnsSuccessWithoutDiagnosticCallback()
    {
        var executed = false;
        var reported = false;

        var completed = LifecycleOperationGuard.TryExecute(
            "startup",
            () => executed = true,
            (_, _) => reported = true);

        Assert.True(completed);
        Assert.True(executed);
        Assert.False(reported);
    }

    /// <summary>Verifies a broken logger cannot turn shutdown cleanup into a process-level fault.</summary>
    [Fact]
    public void TryExecute_ReportingFailure_DoesNotEscapeLifecycleBoundary()
    {
        var completed = LifecycleOperationGuard.TryExecute(
            "shutdown",
            () => throw new IOException("synthetic shutdown failure"),
            (_, _) => throw new InvalidOperationException("synthetic logging failure"));

        Assert.False(completed);
    }
}
