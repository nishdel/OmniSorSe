using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Core.Tasks;

/// <summary>
/// Tracks independent background operations without owning their business logic.
/// </summary>
public sealed class TaskManager : ITaskManager
{
    private readonly ConcurrentDictionary<Guid, TaskExecution> _activeTasks = new();
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a task manager that reports task failures through centralized logging.
    /// </summary>
    /// <param name="loggingService">The logging service used for task diagnostics.</param>
    public TaskManager(ILoggingService loggingService)
    {
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(TaskManager));
    }

    /// <inheritdoc />
    public IReadOnlyCollection<BackgroundTaskSnapshot> ActiveTasks =>
        _activeTasks.Values.Select(execution => execution.CreateSnapshot()).ToArray();

    /// <inheritdoc />
    public event Action<BackgroundTaskSnapshot>? TaskChanged;

    /// <inheritdoc />
    public async Task<BackgroundTaskSnapshot> RunAsync(
        string name,
        Func<CancellationToken, IProgress<double>, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(operation);

        var execution = new TaskExecution(name, cancellationToken);
        if (!_activeTasks.TryAdd(execution.Id, execution))
        {
            throw new InvalidOperationException("Unable to register the background task.");
        }

        BackgroundTaskSnapshot? finalSnapshot = null;
        try
        {
            Publish(execution.CreateSnapshot());
            Publish(execution.SetStatus(BackgroundTaskStatus.Running));
            var progress = new InlineProgress(value =>
            {
                var snapshot = execution.TrySetProgress(value);
                if (snapshot is not null)
                {
                    Publish(snapshot);
                }
            });

            await Task.Run(
                () => operation(execution.CancellationSource.Token, progress),
                CancellationToken.None).ConfigureAwait(false);
            execution.SetStatus(BackgroundTaskStatus.Completed);
        }
        catch (OperationCanceledException) when (execution.CancellationSource.IsCancellationRequested)
        {
            execution.SetStatus(BackgroundTaskStatus.Cancelled);
        }
        catch (Exception exception)
        {
            execution.SetStatus(BackgroundTaskStatus.Failed, exception);
            _logger.LogError(
                exception,
                "Background task {TaskName} failed.",
                name);
        }
        finally
        {
            finalSnapshot = execution.CreateSnapshot();
            Publish(finalSnapshot);
            _activeTasks.TryRemove(execution.Id, out _);
            execution.CancellationSource.Dispose();
        }

        return finalSnapshot;
    }

    /// <inheritdoc />
    public bool TryCancel(Guid taskId)
    {
        if (!_activeTasks.TryGetValue(taskId, out var execution))
        {
            return false;
        }

        try
        {
            execution.CancellationSource.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private void Publish(BackgroundTaskSnapshot snapshot)
    {
        var handlers = TaskChanged?.GetInvocationList().Cast<Action<BackgroundTaskSnapshot>>().ToArray()
            ?? Array.Empty<Action<BackgroundTaskSnapshot>>();
        foreach (var handler in handlers)
        {
            try
            {
                handler(snapshot);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "A background task observer failed.");
            }
        }
    }

    private sealed class TaskExecution
    {
        private readonly object _sync = new();
        private BackgroundTaskStatus _status = BackgroundTaskStatus.Pending;
        private double? _progress;
        private Exception? _failure;

        public TaskExecution(string name, CancellationToken cancellationToken)
        {
            Id = Guid.NewGuid();
            Name = name;
            CancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        public Guid Id { get; }

        public string Name { get; }

        public CancellationTokenSource CancellationSource { get; }

        public BackgroundTaskSnapshot CreateSnapshot()
        {
            lock (_sync)
            {
                return CreateSnapshotCore();
            }
        }

        public BackgroundTaskSnapshot SetStatus(
            BackgroundTaskStatus status,
            Exception? failure = null)
        {
            lock (_sync)
            {
                _status = status;
                _failure = failure;
                return CreateSnapshotCore();
            }
        }

        public BackgroundTaskSnapshot? TrySetProgress(double progress)
        {
            lock (_sync)
            {
                if (_status is BackgroundTaskStatus.Completed or
                    BackgroundTaskStatus.Cancelled or
                    BackgroundTaskStatus.Failed)
                {
                    return null;
                }

                _progress = Math.Clamp(progress, 0d, 1d);
                return CreateSnapshotCore();
            }
        }

        private BackgroundTaskSnapshot CreateSnapshotCore() =>
            new(Id, Name, _status, _progress, _failure);
    }

    private sealed class InlineProgress : IProgress<double>
    {
        private readonly Action<double> _report;

        public InlineProgress(Action<double> report)
        {
            _report = report;
        }

        public void Report(double value)
        {
            _report(value);
        }
    }
}
