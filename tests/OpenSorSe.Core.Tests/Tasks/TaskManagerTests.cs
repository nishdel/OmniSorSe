using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Tasks;

namespace OpenSorSe.Core.Tests.Tasks;

/// <summary>
/// Tests shared background task coordination.
/// </summary>
public sealed class TaskManagerTests
{
    /// <summary>
    /// Verifies that a successful operation reports its final completed status.
    /// </summary>
    [Fact]
    public async Task RunAsync_ReturnsCompletedSnapshot()
    {
        using var loggingService = new LoggingService();
        loggingService.Initialize(new LoggingOptions(LogLevel.Trace, FileLoggingEnabled: false));
        var taskManager = new TaskManager(loggingService);

        var result = await taskManager.RunAsync(
            "Foundation test",
            (_, progress) =>
            {
                progress.Report(1d);
                return Task.CompletedTask;
            });

        Assert.Equal(BackgroundTaskStatus.Completed, result.Status);
        Assert.Equal(1d, result.Progress);
        Assert.Empty(taskManager.ActiveTasks);
    }

    /// <summary>Verifies operation failures are retained, logged, and removed from the active set.</summary>
    [Fact]
    public async Task RunAsync_OperationFailure_ReturnsFailedSnapshot()
    {
        using var loggingService = new LoggingService();
        loggingService.Initialize(new LoggingOptions(LogLevel.Trace, FileLoggingEnabled: false));
        var taskManager = new TaskManager(loggingService);

        var result = await taskManager.RunAsync(
            "Failing task",
            (_, _) => throw new InvalidOperationException("controlled failure"));

        Assert.Equal(BackgroundTaskStatus.Failed, result.Status);
        Assert.IsType<InvalidOperationException>(result.Failure);
        Assert.Empty(taskManager.ActiveTasks);
        Assert.Equal(1L, loggingService.GetStatistics().ErrorEntries);
    }

    /// <summary>Verifies caller cancellation reaches the operation and produces a cancelled terminal snapshot.</summary>
    [Fact]
    public async Task RunAsync_CallerCancellation_CancelsTrackedOperation()
    {
        using var loggingService = new LoggingService();
        var taskManager = new TaskManager(loggingService);
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = taskManager.RunAsync(
            "Cancellable task",
            async (token, _) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
            cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(BackgroundTaskStatus.Cancelled, result.Status);
        Assert.Null(result.Failure);
        Assert.Empty(taskManager.ActiveTasks);
    }

    /// <summary>Verifies ID-based cancellation is race-safe and unavailable after terminal cleanup.</summary>
    [Fact]
    public async Task TryCancel_ActiveTask_CancelsOnceWithoutDisposalRace()
    {
        using var loggingService = new LoggingService();
        var taskManager = new TaskManager(loggingService);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = taskManager.RunAsync(
            "Managed cancellation",
            async (token, _) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var taskId = Assert.Single(taskManager.ActiveTasks).Id;

        Assert.True(taskManager.TryCancel(taskId));
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(BackgroundTaskStatus.Cancelled, result.Status);
        Assert.False(taskManager.TryCancel(taskId));
    }

    /// <summary>Verifies observer failures cannot suppress later lifecycle notifications.</summary>
    [Fact]
    public async Task TaskChanged_ObserverFailure_IsIsolated()
    {
        using var loggingService = new LoggingService();
        loggingService.Initialize(new LoggingOptions(LogLevel.Trace, FileLoggingEnabled: false));
        var taskManager = new TaskManager(loggingService);
        var received = new List<BackgroundTaskStatus>();
        taskManager.TaskChanged += _ => throw new InvalidOperationException("observer failure");
        taskManager.TaskChanged += snapshot => received.Add(snapshot.Status);

        var result = await taskManager.RunAsync("Observed task", (_, _) => Task.CompletedTask);

        Assert.Equal(BackgroundTaskStatus.Completed, result.Status);
        Assert.Contains(BackgroundTaskStatus.Pending, received);
        Assert.Contains(BackgroundTaskStatus.Running, received);
        Assert.Equal(BackgroundTaskStatus.Completed, received[^1]);
        Assert.True(loggingService.GetStatistics().ErrorEntries >= 1);
    }

    /// <summary>Verifies progress is clamped and late producer reports cannot mutate terminal history.</summary>
    [Fact]
    public async Task RunAsync_Progress_IsClampedAndIgnoredAfterCompletion()
    {
        using var loggingService = new LoggingService();
        var taskManager = new TaskManager(loggingService);
        IProgress<double>? retainedProgress = null;
        var notifications = 0;
        taskManager.TaskChanged += _ => notifications++;

        var result = await taskManager.RunAsync(
            "Progress task",
            (_, progress) =>
            {
                retainedProgress = progress;
                progress.Report(2d);
                return Task.CompletedTask;
            });
        var terminalNotificationCount = notifications;
        retainedProgress!.Report(0.25d);

        Assert.Equal(1d, result.Progress);
        Assert.Equal(terminalNotificationCount, notifications);
    }

    /// <summary>Verifies independent concurrent tasks retain unique coherent active snapshots.</summary>
    [Fact]
    public async Task RunAsync_ConcurrentTasks_TrackAndCleanUpIndependently()
    {
        using var loggingService = new LoggingService();
        var taskManager = new TaskManager(loggingService);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 32)
            .Select(index => taskManager.RunAsync(
                $"Task {index}",
                async (_, progress) =>
                {
                    progress.Report(index / 31d);
                    if (Interlocked.Increment(ref started) == 32)
                    {
                        allStarted.SetResult();
                    }

                    await release.Task;
                }))
            .ToArray();
        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var active = taskManager.ActiveTasks;
        Assert.Equal(32, active.Count);
        Assert.Equal(32, active.Select(snapshot => snapshot.Id).Distinct().Count());
        Assert.All(active, snapshot => Assert.Equal(BackgroundTaskStatus.Running, snapshot.Status));

        release.SetResult();
        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(results, snapshot => Assert.Equal(BackgroundTaskStatus.Completed, snapshot.Status));
        Assert.Empty(taskManager.ActiveTasks);
    }

    /// <summary>Verifies an unrelated cancellation exception is classified as a task failure.</summary>
    [Fact]
    public async Task RunAsync_UnrelatedOperationCanceledException_IsFailure()
    {
        using var loggingService = new LoggingService();
        var taskManager = new TaskManager(loggingService);

        var result = await taskManager.RunAsync(
            "Unexpected cancellation",
            (_, _) => Task.FromException(new OperationCanceledException("not requested")));

        Assert.Equal(BackgroundTaskStatus.Failed, result.Status);
        Assert.IsType<OperationCanceledException>(result.Failure);
    }
}
