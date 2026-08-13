using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using OmniSorSe.ExplorerProtocol;

namespace OpenSorSe.Application.Explorer;

/// <summary>Defines the exact indexed-source scope granted to one future OmniExplorer session.</summary>
public sealed record ExplorerSessionScope(
    IReadOnlyCollection<string> AuthorizedSourceIds,
    bool IncludeAuthorizedPaths = false);

/// <summary>Contains connection material returned once to an explicitly authorized local launcher.</summary>
public sealed record ExplorerSessionGrant(
    string Transport,
    string Endpoint,
    string SessionId,
    string AuthorizationToken,
    DateTimeOffset ExpiresAtUtc,
    int ProtocolMajor,
    int ProtocolMinor);

/// <summary>Identifies the lifecycle of the optional local protocol host.</summary>
public enum ExplorerProtocolHostState
{
    /// <summary>No session has requested the host.</summary>
    Unavailable,
    /// <summary>The local transport is starting.</summary>
    Waiting,
    /// <summary>The host can accept an authorized connection.</summary>
    Authorized,
    /// <summary>At least one local connection is active.</summary>
    Connected,
    /// <summary>The requested client protocol is incompatible.</summary>
    Incompatible,
    /// <summary>The most recent connection ended.</summary>
    Disconnected,
    /// <summary>A session reached its absolute expiry.</summary>
    Expired,
    /// <summary>The application is revoking sessions and stopping transport work.</summary>
    ShuttingDown,
}

/// <summary>Creates and revokes local read-only Explorer sessions without exposing persistence internals.</summary>
public interface IExplorerProtocolHost : IAsyncDisposable
{
    /// <summary>Gets the current optional-host lifecycle state.</summary>
    ExplorerProtocolHostState State { get; }

    /// <summary>Starts the local host on demand and returns one short-lived session grant.</summary>
    Task<ExplorerSessionGrant> CreateSessionAsync(
        ExplorerSessionScope scope,
        TimeSpan? lifetime = null,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes one grant immediately.</summary>
    Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>Observes the first authenticated use of one exact local Explorer session.</summary>
public interface IExplorerSessionConnectionObserver
{
    /// <summary>Waits for the first attributable connection outcome or a bounded timeout.</summary>
    Task<ExplorerSessionConnectionOutcome?> WaitForConnectionOutcomeAsync(
        string sessionId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>Identifies an attributable first-use outcome without exposing session secrets.</summary>
public enum ExplorerSessionConnectionOutcome
{
    /// <summary>A compatible authenticated request completed successfully.</summary>
    Authenticated,
    /// <summary>The intended session identifier was presented with invalid authorization.</summary>
    AuthenticationRejected,
    /// <summary>Authorization succeeded but the requested protocol major was incompatible.</summary>
    IncompatibleVersion,
    /// <summary>The session expired or was revoked before connection completed.</summary>
    Revoked,
}

/// <summary>Reports whether the separately installed optional OmniBrille companion can be launched.</summary>
public interface IExplorerCompanionPresence
{
    /// <summary>Gets whether a reviewed companion installation is available.</summary>
    bool IsAvailable { get; }

    /// <summary>Gets a plain-language state without probing arbitrary filesystem locations.</summary>
    string Status { get; }
}

/// <summary>Safely reports that OmniBrille is not installed or configured.</summary>
public sealed class UnavailableExplorerCompanionPresence : IExplorerCompanionPresence
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public string Status => "OmniBrille is an optional separate companion and is not installed or configured.";
}

internal enum ExplorerSessionValidation
{
    Valid,
    Unauthorized,
    Expired,
}

internal sealed record ExplorerSessionValidationResult(
    ExplorerSessionValidation Validation,
    ExplorerSessionContext? Session);

internal sealed record ExplorerNodeIdentity(
    ExplorerNodeKind Kind,
    string SourceId,
    string? RelativePath = null,
    string? FileId = null);

internal sealed class ExplorerSessionContext
{
    private readonly byte[] _tokenHash;
    private readonly byte[] _nodeSecret;
    private readonly ConcurrentDictionary<string, ExplorerNodeIdentity> _nodesById = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _idsByIdentity = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource<ExplorerSessionConnectionOutcome> _firstConnectionOutcome =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ExplorerSessionContext(
        string sessionId,
        byte[] tokenHash,
        byte[] nodeSecret,
        ExplorerSessionScope scope,
        DateTimeOffset expiresAtUtc)
    {
        SessionId = sessionId;
        _tokenHash = tokenHash;
        _nodeSecret = nodeSecret;
        AuthorizedSourceIds = scope.AuthorizedSourceIds.ToHashSet(StringComparer.Ordinal);
        IncludeAuthorizedPaths = scope.IncludeAuthorizedPaths;
        ExpiresAtUtc = expiresAtUtc;
    }

    public string SessionId { get; }
    public IReadOnlySet<string> AuthorizedSourceIds { get; }
    public bool IncludeAuthorizedPaths { get; }
    public DateTimeOffset ExpiresAtUtc { get; }

    public bool IsAuthorizedSource(string? sourceId) =>
        !string.IsNullOrWhiteSpace(sourceId) && AuthorizedSourceIds.Contains(sourceId);

    public bool TokenMatches(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > ExplorerProtocolDefaults.MaximumTokenCharacters)
        {
            return false;
        }

        var candidate = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return CryptographicOperations.FixedTimeEquals(_tokenHash, candidate);
    }

    public void MarkConnectionOutcome(ExplorerSessionConnectionOutcome outcome) =>
        _firstConnectionOutcome.TrySetResult(outcome);

    public Task<ExplorerSessionConnectionOutcome> WaitForConnectionOutcomeAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        _firstConnectionOutcome.Task.WaitAsync(timeout, cancellationToken);

    public string RegisterNode(ExplorerNodeIdentity identity)
    {
        var key = string.Join(
            '\u001f',
            ((int)identity.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture),
            identity.SourceId,
            identity.RelativePath ?? string.Empty,
            identity.FileId ?? string.Empty);
        return _idsByIdentity.GetOrAdd(key, _ =>
        {
            using var hmac = new HMACSHA256(_nodeSecret);
            var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(key));
            var id = "n1_" + Convert.ToHexString(digest).ToLowerInvariant();
            _nodesById[id] = identity;
            return id;
        });
    }

    public bool TryResolveNode(string nodeId, out ExplorerNodeIdentity? identity)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || nodeId.Length > ExplorerProtocolDefaults.MaximumNodeIdCharacters)
        {
            identity = null;
            return false;
        }

        return _nodesById.TryGetValue(nodeId, out identity);
    }

    public void ClearSecrets()
    {
        _firstConnectionOutcome.TrySetResult(ExplorerSessionConnectionOutcome.Revoked);
        CryptographicOperations.ZeroMemory(_tokenHash);
        CryptographicOperations.ZeroMemory(_nodeSecret);
        _nodesById.Clear();
        _idsByIdentity.Clear();
    }
}

internal sealed class ExplorerSessionManager : IDisposable
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinimumLifetime = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, ExplorerSessionContext> _sessions = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    public ExplorerSessionManager(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    public (ExplorerSessionContext Session, string Token) Create(
        ExplorerSessionScope scope,
        TimeSpan? requestedLifetime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scope);
        var sourceIds = scope.AuthorizedSourceIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (sourceIds.Length is 0 or > ExplorerProtocolDefaults.MaximumAuthorizedSources ||
            sourceIds.Any(value => value.Length > ExplorerProtocolDefaults.MaximumSourceIdCharacters || value.Any(char.IsControl)))
        {
            throw new ArgumentException("The Explorer session must contain a bounded set of valid indexed source identifiers.", nameof(scope));
        }

        var lifetime = requestedLifetime ?? DefaultLifetime;
        if (lifetime < MinimumLifetime || lifetime > MaximumLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedLifetime), "Explorer session lifetime must be between 15 seconds and 15 minutes.");
        }

        var normalizedScope = scope with { AuthorizedSourceIds = sourceIds };
        while (true)
        {
            var sessionId = RandomSecret(16);
            var token = RandomSecret(32);
            var context = new ExplorerSessionContext(
                sessionId,
                SHA256.HashData(Encoding.UTF8.GetBytes(token)),
                RandomNumberGenerator.GetBytes(32),
                normalizedScope,
                _timeProvider.GetUtcNow().Add(lifetime));
            if (_sessions.TryAdd(sessionId, context))
            {
                return (context, token);
            }

            context.ClearSecrets();
        }
    }

    public ExplorerSessionValidationResult Validate(string sessionId, string token)
    {
        if (_disposed ||
            string.IsNullOrWhiteSpace(sessionId) ||
            sessionId.Length > ExplorerProtocolDefaults.MaximumSessionIdCharacters ||
            !_sessions.TryGetValue(sessionId, out var session) ||
            !session.TokenMatches(token))
        {
            return new ExplorerSessionValidationResult(ExplorerSessionValidation.Unauthorized, null);
        }

        if (_timeProvider.GetUtcNow() >= session.ExpiresAtUtc)
        {
            Revoke(sessionId);
            return new ExplorerSessionValidationResult(ExplorerSessionValidation.Expired, null);
        }

        return new ExplorerSessionValidationResult(ExplorerSessionValidation.Valid, session);
    }

    public void MarkConnectionOutcome(string sessionId, ExplorerSessionConnectionOutcome outcome)
    {
        if (!_disposed && !string.IsNullOrWhiteSpace(sessionId) && _sessions.TryGetValue(sessionId, out var session))
        {
            session.MarkConnectionOutcome(outcome);
        }
    }

    public async Task<ExplorerSessionConnectionOutcome?> WaitForConnectionOutcomeAsync(
        string sessionId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(sessionId) || !_sessions.TryGetValue(sessionId, out var session))
        {
            return ExplorerSessionConnectionOutcome.Revoked;
        }

        try
        {
            return await session.WaitForConnectionOutcomeAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public void Revoke(string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId) && _sessions.TryRemove(sessionId, out var session))
        {
            session.ClearSecrets();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var sessionId in _sessions.Keys)
        {
            Revoke(sessionId);
        }
    }

    private static string RandomSecret(int bytes) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

/// <summary>Defines non-configurable defensive limits for Explorer Protocol v1.</summary>
public static class ExplorerProtocolDefaults
{
    /// <summary>Gets the maximum serialized request frame size.</summary>
    public const int MaximumRequestBytes = 64 * 1024;
    /// <summary>Gets the maximum serialized response frame size.</summary>
    public const int MaximumResponseBytes = 1024 * 1024;
    /// <summary>Gets the maximum accepted authorization-token string length.</summary>
    public const int MaximumTokenCharacters = 128;
    /// <summary>Gets the maximum session identifier length.</summary>
    public const int MaximumSessionIdCharacters = 64;
    /// <summary>Gets the maximum opaque node identifier length.</summary>
    public const int MaximumNodeIdCharacters = 80;
    /// <summary>Gets the maximum request identifier length.</summary>
    public const int MaximumRequestIdCharacters = 64;
    /// <summary>Gets the maximum authorized sources in one session.</summary>
    public const int MaximumAuthorizedSources = 64;
    /// <summary>Gets the maximum internal source identifier length.</summary>
    public const int MaximumSourceIdCharacters = 256;
    /// <summary>Gets the maximum documents examined by one structural projection.</summary>
    public const int MaximumDocumentsExamined = 20_000;
    /// <summary>Gets the maximum nodes returned by one request.</summary>
    public const int MaximumNodes = 256;
    /// <summary>Gets the maximum edges returned by one request.</summary>
    public const int MaximumEdges = 512;
    /// <summary>Gets the default structural page size.</summary>
    public const int DefaultPageSize = 100;
    /// <summary>Gets the maximum Search results returned to a client.</summary>
    public const int MaximumSearchResults = 100;
    /// <summary>Gets the maximum Related Files results returned to a client.</summary>
    public const int MaximumRelatedResults = 100;
    /// <summary>Gets the maximum structural traversal depth.</summary>
    public const int MaximumDepth = 2;
    /// <summary>Gets the maximum retained reason/explanation length.</summary>
    public const int MaximumReasonCharacters = 256;
    /// <summary>Gets the maximum concurrent protocol requests.</summary>
    public const int MaximumConcurrentRequests = 4;
    /// <summary>Gets the maximum requests waiting for concurrency admission.</summary>
    public const int MaximumQueuedRequests = 16;
    /// <summary>Gets the absolute operation timeout in seconds.</summary>
    public const int RequestTimeoutSeconds = 15;

    /// <summary>Creates the advertised immutable limit record.</summary>
    public static ExplorerProtocolLimits CreateLimits() => new(
        MaximumRequestBytes,
        MaximumResponseBytes,
        512,
        MaximumNodes,
        MaximumEdges,
        MaximumSearchResults,
        MaximumRelatedResults,
        MaximumDepth,
        240,
        12,
        12,
        MaximumReasonCharacters,
        MaximumConcurrentRequests,
        RequestTimeoutSeconds);
}
