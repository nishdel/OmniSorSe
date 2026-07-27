using Microsoft.Extensions.Logging;
using OpenSorSe.Application.Models;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application;

/// <summary>Tracks non-persistent sequential processing sessions without owning pipeline logic.</summary>
/// <remarks>
/// Session transitions are protected by a private lock and published as
/// immutable snapshots. Caller cancellation is passed to the orchestrator and
/// recorded as a cancelled session rather than success; no session record is a
/// persistence or file-operation authorization.
/// </remarks>
public sealed class ProcessingSessionManager : IProcessingSessionManager
{
    private readonly IProcessingOrchestrator _orchestrator;
    private readonly ILogger _logger;
    private readonly object _syncRoot = new();
    private readonly List<ProcessingSession> _sessions = [];

    /// <summary>Initializes session tracking around an orchestrator.</summary>
    /// <param name="orchestrator">The sequential non-destructive processing orchestrator.</param>
    /// <param name="loggingService">The centralized diagnostic logging service.</param>
    public ProcessingSessionManager(IProcessingOrchestrator orchestrator, ILoggingService loggingService)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService))).CreateLogger(nameof(ProcessingSessionManager));
    }

    /// <inheritdoc />
    public event EventHandler<ProcessingSession>? SessionChanged;

    /// <inheritdoc />
    public IReadOnlyList<ProcessingSession> Sessions { get { lock (_syncRoot) { return _sessions.ToArray(); } } }

    /// <inheritdoc />
    public async Task<ProcessingSessionResult> RunAsync(ProcessingRequest request, IProgress<ProcessingProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = new ProcessingSession($"session:{Guid.NewGuid():N}", DateTimeOffset.UtcNow, null, ProcessingSessionStatus.Running, null);
        AddOrReplace(session);
        try
        {
            var processing = await _orchestrator.ProcessAsync(request, progress, cancellationToken).ConfigureAwait(false);
            var terminal = session with { CompletedAtUtc = DateTimeOffset.UtcNow, Status = processing.Status == ProcessingStatus.Cancelled ? ProcessingSessionStatus.Cancelled : ProcessingSessionStatus.Completed };
            AddOrReplace(terminal);
            return new ProcessingSessionResult(terminal, processing);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var terminal = session with { CompletedAtUtc = DateTimeOffset.UtcNow, Status = ProcessingSessionStatus.Cancelled };
            AddOrReplace(terminal);
            return new ProcessingSessionResult(terminal, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Processing session failed unexpectedly.");
            var terminal = session with { CompletedAtUtc = DateTimeOffset.UtcNow, Status = ProcessingSessionStatus.Failed, FailureMessage = "The processing session could not be completed." };
            AddOrReplace(terminal);
            return new ProcessingSessionResult(terminal, null);
        }
    }

    /// <inheritdoc />
    public bool TryClose(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ProcessingSession? closed;
        lock (_syncRoot)
        {
            var index = _sessions.FindIndex(session => string.Equals(session.Id, sessionId, StringComparison.Ordinal));
            if (index < 0 || _sessions[index].Status == ProcessingSessionStatus.Running) return false;
            closed = _sessions[index] with { Status = ProcessingSessionStatus.Closed };
            _sessions[index] = closed;
        }
        SessionChanged?.Invoke(this, closed);
        return true;
    }

    private void AddOrReplace(ProcessingSession session)
    {
        lock (_syncRoot)
        {
            var index = _sessions.FindIndex(candidate => candidate.Id == session.Id);
            if (index < 0) _sessions.Add(session); else _sessions[index] = session;
        }
        SessionChanged?.Invoke(this, session);
    }
}
