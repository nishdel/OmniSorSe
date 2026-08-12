using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OmniSorSe.ExplorerProtocol;
using OpenSorSe.Core.Diagnostics;

namespace OpenSorSe.Application.Explorer;

/// <summary>Creates the strict JSON settings shared by the framed protocol host and future clients.</summary>
public static class ExplorerProtocolJson
{
    /// <summary>Creates independent bounded options without runtime-type metadata.</summary>
    public static JsonSerializerOptions CreateOptions() => new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };
}

internal sealed class ExplorerProtocolDispatcher : IDisposable
{
    private readonly ExplorerSessionManager _sessions;
    private readonly ExplorerReadService _reads;
    private readonly JsonSerializerOptions _json;
    private readonly SemaphoreSlim _concurrency = new(
        ExplorerProtocolDefaults.MaximumConcurrentRequests,
        ExplorerProtocolDefaults.MaximumConcurrentRequests);
    private readonly IDiagnosticsEventSink? _diagnostics;
    private int _queued;
    private bool _disposed;

    public ExplorerProtocolDispatcher(
        ExplorerSessionManager sessions,
        ExplorerReadService reads,
        IDiagnosticsEventSink? diagnostics = null)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _reads = reads ?? throw new ArgumentNullException(nameof(reads));
        _json = ExplorerProtocolJson.CreateOptions();
        _diagnostics = DiagnosticsIsolation.Protect(diagnostics);
    }

    public async Task<ExplorerResponseEnvelope> DispatchAsync(
        ExplorerRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsValidRequestId(request.RequestId))
        {
            return Error(string.Empty, ExplorerErrorCode.MalformedRequest, "The request identifier is invalid.");
        }

        var authorization = _sessions.Validate(request.SessionId, request.AuthorizationToken);
        if (authorization.Validation == ExplorerSessionValidation.Expired)
        {
            RecordAuthorizationFailure("expired");
            return Error(request.RequestId, ExplorerErrorCode.SessionExpired, "The Explorer session expired. Request a new local session.");
        }

        if (authorization.Validation != ExplorerSessionValidation.Valid || authorization.Session is null)
        {
            RecordAuthorizationFailure("unauthorized");
            return Error(request.RequestId, ExplorerErrorCode.Unauthorized, "Valid local Explorer session authorization is required.");
        }

        if (request.ProtocolMajor != ExplorerProtocolVersion.Major)
        {
            RecordAuthorizationFailure("incompatible-protocol");
            return Error(request.RequestId, ExplorerErrorCode.UnsupportedProtocol, "The requested Explorer protocol major version is unsupported.");
        }

        if (Interlocked.Increment(ref _queued) > ExplorerProtocolDefaults.MaximumQueuedRequests)
        {
            Interlocked.Decrement(ref _queued);
            return Error(
                request.RequestId,
                ExplorerErrorCode.TemporarilyUnavailable,
                "The local Explorer service is busy; retry after the current bounded requests complete.",
                retryable: true);
        }

        var admitted = false;
        string? diagnosticSession = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            admitted = true;
            diagnosticSession = _diagnostics?.BeginSession(
                DiagnosticCategory.SearchAndIndexing,
                "Explorer protocol request",
                [
                    new DiagnosticField("Operation", request.Operation.ToString()),
                    new DiagnosticField("Protocol", ExplorerProtocolVersion.Display),
                ]);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(ExplorerProtocolDefaults.RequestTimeoutSeconds));
            var payload = await DispatchAuthorizedAsync(
                authorization.Session,
                request.Operation,
                request.Payload,
                timeout.Token).ConfigureAwait(false);
            var response = new ExplorerResponseEnvelope(
                ExplorerProtocolVersion.Major,
                request.RequestId,
                true,
                JsonSerializer.SerializeToElement(payload, payload.GetType(), _json),
                null);
            _diagnostics?.Complete(
                diagnosticSession,
                DiagnosticStatus.Succeeded,
                stopwatch.Elapsed,
                "The bounded read-only Explorer request completed.",
                fields: ResponseDiagnosticFields(payload));
            return response;
        }
        catch (ExplorerProtocolException exception)
        {
            _diagnostics?.Complete(
                diagnosticSession,
                DiagnosticStatus.Rejected,
                stopwatch.Elapsed,
                "The Explorer request was rejected by a protocol boundary.",
                DiagnosticSeverity.Warning,
                [new DiagnosticField("Failure category", exception.Code.ToString())]);
            return Error(request.RequestId, exception.Code, exception.Message, exception.Retryable);
        }
        catch (OperationCanceledException)
        {
            _diagnostics?.Complete(
                diagnosticSession,
                DiagnosticStatus.Cancelled,
                stopwatch.Elapsed,
                "The Explorer request was cancelled or reached its bounded timeout.",
                DiagnosticSeverity.Warning);
            return Error(request.RequestId, ExplorerErrorCode.Cancelled, "The Explorer request was cancelled or timed out.", retryable: true);
        }
        catch (JsonException)
        {
            _diagnostics?.Complete(
                diagnosticSession,
                DiagnosticStatus.Rejected,
                stopwatch.Elapsed,
                "The Explorer request payload was malformed.",
                DiagnosticSeverity.Warning);
            return Error(request.RequestId, ExplorerErrorCode.MalformedRequest, "The request payload is malformed.");
        }
        catch (Exception)
        {
            _diagnostics?.Complete(
                diagnosticSession,
                DiagnosticStatus.Failed,
                stopwatch.Elapsed,
                "An unexpected Explorer request failure was isolated.",
                DiagnosticSeverity.Error);
            return Error(request.RequestId, ExplorerErrorCode.InternalFailure, "The local Explorer request failed safely.", retryable: true);
        }
        finally
        {
            Interlocked.Decrement(ref _queued);
            if (admitted)
            {
                _concurrency.Release();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _concurrency.Dispose();
    }

    private async Task<object> DispatchAuthorizedAsync(
        ExplorerSessionContext session,
        ExplorerOperation operation,
        JsonElement payload,
        CancellationToken cancellationToken) =>
        operation switch
        {
            ExplorerOperation.GetProtocolInfo => _reads.GetProtocolInfo(),
            ExplorerOperation.GetAccessibleRoots => await _reads.GetAccessibleRootsAsync(session, cancellationToken).ConfigureAwait(false),
            ExplorerOperation.GetChildren => await _reads.GetChildrenAsync(
                session,
                Deserialize<ExplorerChildrenRequest>(payload),
                cancellationToken).ConfigureAwait(false),
            ExplorerOperation.GetNeighborhood => await _reads.GetNeighborhoodAsync(
                session,
                Deserialize<ExplorerNeighborhoodRequest>(payload),
                cancellationToken).ConfigureAwait(false),
            ExplorerOperation.Search => await _reads.SearchAsync(
                session,
                Deserialize<ExplorerSearchRequest>(payload),
                cancellationToken).ConfigureAwait(false),
            ExplorerOperation.GetRelated => await _reads.GetRelatedAsync(
                session,
                Deserialize<ExplorerRelatedRequest>(payload),
                cancellationToken).ConfigureAwait(false),
            ExplorerOperation.GetNodeDetails => await _reads.GetNodeDetailsAsync(
                session,
                Deserialize<ExplorerNodeDetailsRequest>(payload),
                cancellationToken).ConfigureAwait(false),
            _ => throw new ExplorerProtocolException(ExplorerErrorCode.CapabilityUnavailable, "The requested read-only operation is unavailable."),
        };

    private T Deserialize<T>(JsonElement payload) where T : class =>
        payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? throw new JsonException("A request payload is required.")
            : payload.Deserialize<T>(_json) ?? throw new JsonException("A request payload is required.");

    private void RecordAuthorizationFailure(string category)
    {
        var session = _diagnostics?.BeginSession(
            DiagnosticCategory.SearchAndIndexing,
            "Explorer authorization",
            [new DiagnosticField("Result", category)]);
        _diagnostics?.Complete(
            session,
            DiagnosticStatus.Rejected,
            TimeSpan.Zero,
            "A local Explorer authorization or negotiation request was rejected.",
            DiagnosticSeverity.Warning);
    }

    private static IReadOnlyList<DiagnosticField> ResponseDiagnosticFields(object payload)
    {
        var fields = new List<DiagnosticField>
        {
            new("Response type", payload.GetType().Name),
        };
        switch (payload)
        {
            case ExplorerNodePage page:
                fields.Add(new("Node count", page.Nodes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                fields.Add(new("Truncated", page.IsTruncated ? "Yes" : "No"));
                break;
            case ExplorerNeighborhood neighborhood:
                fields.Add(new("Node count", neighborhood.Nodes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                fields.Add(new("Edge count", neighborhood.Edges.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                fields.Add(new("Truncated", neighborhood.IsTruncated ? "Yes" : "No"));
                break;
            case ExplorerSearchResult search:
                fields.Add(new("Result count", search.Results.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                fields.Add(new("Truncated", search.IsTruncated ? "Yes" : "No"));
                break;
            case ExplorerRelatedResult related:
                fields.Add(new("Node count", related.Nodes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                fields.Add(new("Edge count", related.Edges.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                fields.Add(new("Truncated", related.IsTruncated ? "Yes" : "No"));
                break;
            case ExplorerNodeDetails details:
                fields.Add(new("Node kind", details.Node.Kind.ToString()));
                break;
            case ExplorerProtocolInfo info:
                fields.Add(new("Capabilities", info.Capabilities.ToString()));
                break;
        }

        return fields;
    }

    private static bool IsValidRequestId(string requestId) =>
        !string.IsNullOrWhiteSpace(requestId) &&
        requestId.Length <= ExplorerProtocolDefaults.MaximumRequestIdCharacters &&
        requestId.All(character => !char.IsControl(character));

    private static ExplorerResponseEnvelope Error(
        string requestId,
        ExplorerErrorCode code,
        string message,
        bool retryable = false) =>
        new(
            ExplorerProtocolVersion.Major,
            requestId,
            false,
            null,
            new ExplorerProtocolError(code, message, retryable));
}

/// <summary>
/// Hosts Explorer Protocol v1 over an on-demand, current-user-only local named pipe.
/// It never creates a TCP listener and remains dormant while OmniExplorer is absent.
/// </summary>
public sealed class NamedPipeExplorerProtocolHost : IExplorerProtocolHost
{
    private readonly ExplorerSessionManager _sessions;
    private readonly ExplorerReadService _reads;
    private readonly ExplorerProtocolDispatcher _dispatcher;
    private readonly JsonSerializerOptions _json = ExplorerProtocolJson.CreateOptions();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly IDiagnosticsEventSink? _diagnostics;
    private readonly object _lifecycleLock = new();
    private readonly ConcurrentDictionary<long, Task> _connections = new();
    private readonly string _pipeName;
    private Task? _acceptLoop;
    private long _connectionSequence;
    private bool _disposed;
    private ExplorerProtocolHostState _state = ExplorerProtocolHostState.Unavailable;

    /// <summary>Creates a dormant host over the current provider-neutral read model.</summary>
    public NamedPipeExplorerProtocolHost(
        IExplorerDataSource source,
        OpenSorSe.Core.Platform.IPathSemantics paths,
        IDiagnosticsEventSink? diagnostics = null,
        TimeProvider? timeProvider = null)
    {
        _sessions = new ExplorerSessionManager(timeProvider);
        _reads = new ExplorerReadService(source, paths);
        _diagnostics = DiagnosticsIsolation.Protect(diagnostics);
        _dispatcher = new ExplorerProtocolDispatcher(_sessions, _reads, _diagnostics);
        _pipeName = "omnisorse-explorer-v1-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }

    /// <inheritdoc />
    public ExplorerProtocolHostState State => _state;

    /// <inheritdoc />
    public async Task<ExplorerSessionGrant> CreateSessionAsync(
        ExplorerSessionScope scope,
        TimeSpan? lifetime = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _reads.ValidateScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        EnsureStarted();
        var (session, token) = _sessions.Create(scope, lifetime);
        _state = ExplorerProtocolHostState.Authorized;
        RecordLifecycle(
            "Explorer session authorized",
            DiagnosticStatus.Succeeded,
            [
                new DiagnosticField("Authorized source count", session.AuthorizedSourceIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new DiagnosticField("Protocol", ExplorerProtocolVersion.Display),
                new DiagnosticField("Path projection", session.IncludeAuthorizedPaths ? "Authorized" : "Omitted"),
            ]);
        return new ExplorerSessionGrant(
            "named-pipe",
            _pipeName,
            session.SessionId,
            token,
            session.ExpiresAtUtc,
            ExplorerProtocolVersion.Major,
            ExplorerProtocolVersion.Minor);
    }

    /// <inheritdoc />
    public Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sessions.Revoke(sessionId);
        _state = ExplorerProtocolHostState.Disconnected;
        RecordLifecycle("Explorer session revoked", DiagnosticStatus.Succeeded);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _state = ExplorerProtocolHostState.ShuttingDown;
        _shutdown.Cancel();
        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during application shutdown.
            }
        }

        var connections = _connections.Values.ToArray();
        if (connections.Length > 0)
        {
            try
            {
                await Task.WhenAll(connections).ConfigureAwait(false);
            }
            catch
            {
                // Every connection isolates its own faults; shutdown only waits for completion.
            }
        }

        _dispatcher.Dispose();
        _sessions.Dispose();
        _shutdown.Dispose();
        _state = ExplorerProtocolHostState.Unavailable;
        RecordLifecycle("Explorer protocol host stopped", DiagnosticStatus.Succeeded);
    }

    private void EnsureStarted()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_acceptLoop is not null)
            {
                return;
            }

            _state = ExplorerProtocolHostState.Waiting;
            _acceptLoop = AcceptLoopAsync(_shutdown.Token);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    ExplorerProtocolDefaults.MaximumConcurrentRequests + ExplorerProtocolDefaults.MaximumQueuedRequests,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                    ExplorerProtocolDefaults.MaximumRequestBytes,
                    ExplorerProtocolDefaults.MaximumResponseBytes);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                _state = ExplorerProtocolHostState.Connected;
                TrackConnection(HandleConnectionSafelyAsync(pipe, cancellationToken));
                pipe = null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                if (pipe is not null)
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                _state = ExplorerProtocolHostState.Disconnected;
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void TrackConnection(Task connection)
    {
        var id = Interlocked.Increment(ref _connectionSequence);
        _connections[id] = connection;
        _ = connection.ContinueWith(
            _ =>
            {
                _connections.TryRemove(id, out var ignored);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task HandleConnectionSafelyAsync(NamedPipeServerStream pipe, CancellationToken hostCancellation)
    {
        await using (pipe.ConfigureAwait(false))
        {
            try
            {
                using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(hostCancellation);
                requestCancellation.CancelAfter(TimeSpan.FromSeconds(ExplorerProtocolDefaults.RequestTimeoutSeconds));
                var request = await ReadRequestAsync(pipe, requestCancellation.Token).ConfigureAwait(false);
                var dispatch = _dispatcher.DispatchAsync(request, requestCancellation.Token);
                var disconnect = MonitorDisconnectAsync(pipe, requestCancellation, dispatch);
                var response = await dispatch.ConfigureAwait(false);
                await disconnect.ConfigureAwait(false);
                await WriteResponseAsync(pipe, response, requestCancellation.Token).ConfigureAwait(false);
                if (response.Error?.Code == ExplorerErrorCode.UnsupportedProtocol)
                {
                    _state = ExplorerProtocolHostState.Incompatible;
                }
                else if (response.Error?.Code == ExplorerErrorCode.SessionExpired)
                {
                    _state = ExplorerProtocolHostState.Expired;
                }
            }
            catch (OperationCanceledException)
            {
                // A disconnected, timed-out, or shutting-down client needs no modal/global failure.
            }
            catch (Exception)
            {
                // Per-connection framing/transport failures are isolated and never terminate OmniSorSe.
            }
            finally
            {
                if (!_disposed && _state is not ExplorerProtocolHostState.Incompatible and not ExplorerProtocolHostState.Expired)
                {
                    _state = ExplorerProtocolHostState.Disconnected;
                }
            }
        }
    }

    private static async Task MonitorDisconnectAsync(
        NamedPipeServerStream pipe,
        CancellationTokenSource requestCancellation,
        Task requestTask)
    {
        if (requestTask.IsCompleted)
        {
            return;
        }

        using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation.Token);
        var probeBuffer = new byte[1];
        var disconnectProbe = pipe.ReadAsync(probeBuffer, probeCancellation.Token).AsTask();
        var completed = await Task.WhenAny(requestTask, disconnectProbe).ConfigureAwait(false);
        if (completed == requestTask)
        {
            probeCancellation.Cancel();
            try
            {
                await disconnectProbe.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException)
            {
                // Completing provider work cancels the otherwise pending disconnect probe.
            }

            return;
        }

        try
        {
            _ = await disconnectProbe.ConfigureAwait(false);
            requestCancellation.Cancel();
        }
        catch (IOException)
        {
            requestCancellation.Cancel();
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            // Host shutdown or the request timeout already supplied the terminal cancellation.
        }
    }

    private async Task<ExplorerRequestEnvelope> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length <= 0 || length > ExplorerProtocolDefaults.MaximumRequestBytes)
        {
            throw new InvalidDataException("The request frame length is invalid.");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ExplorerRequestEnvelope>(payload, _json) ??
            throw new JsonException("The request envelope is missing.");
    }

    private async Task WriteResponseAsync(
        Stream stream,
        ExplorerResponseEnvelope response,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response, _json);
        if (payload.Length > ExplorerProtocolDefaults.MaximumResponseBytes)
        {
            response = new ExplorerResponseEnvelope(
                ExplorerProtocolVersion.Major,
                response.RequestId,
                false,
                null,
                new ExplorerProtocolError(
                    ExplorerErrorCode.RequestTooLarge,
                    "The bounded response could not fit within the protocol frame limit.",
                    false));
            payload = JsonSerializer.SerializeToUtf8Bytes(response, _json);
        }

        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
        await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private void RecordLifecycle(
        string operation,
        DiagnosticStatus status,
        IReadOnlyList<DiagnosticField>? fields = null)
    {
        var session = _diagnostics?.BeginSession(DiagnosticCategory.SearchAndIndexing, operation, fields);
        _diagnostics?.Complete(
            session,
            status,
            TimeSpan.Zero,
            "A local read-only Explorer protocol lifecycle boundary changed.");
    }
}
