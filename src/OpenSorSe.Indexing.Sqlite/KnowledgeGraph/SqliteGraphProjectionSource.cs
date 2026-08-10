using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

/// <summary>
/// Exports immutable, bounded graph observations from the existing schema-3 deep index.
/// The adapter never opens an original source file and never exposes an absolute path.
/// </summary>
public sealed partial class SqliteGraphProjectionSource : IGraphProjectionSource
{
    private const int MaximumSnapshotAttempts = 3;
    private const long MaximumSnapshotRows = 250_000;
    private const int SnapshotApplicationId = 0x4F534B53; // OSKS
    private const int SnapshotSchemaVersion = 1;
    private const string EmptyManifestHash = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";
    private readonly string _deepIndexDatabasePath;
    private readonly string _snapshotPath;
    private readonly IPathSemantics _pathSemantics;
    private readonly IFileIdentityProvider _fileIdentityProvider;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _disposeSync = new();
    private SqliteConnection? _authorityConnection;
    private SourceFileStamp _authorityFileStamp;
    private ActiveSnapshot? _activeSnapshot;
    private Task? _disposeTask;
    private int _disposeState;

    /// <summary>Creates a schema-3 projection adapter for one application-owned deep index.</summary>
    public SqliteGraphProjectionSource(
        string deepIndexDatabasePath,
        IPathSemantics? pathSemantics = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deepIndexDatabasePath);
        _deepIndexDatabasePath = Path.GetFullPath(deepIndexDatabasePath);
        _snapshotPath = string.Concat(_deepIndexDatabasePath, ".knowledge-graph-snapshot");
        _pathSemantics = pathSemantics ?? PlatformServices.CurrentPathSemantics;
        _fileIdentityProvider = FileIdentityProviderFactory.CreateCurrent();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<GraphProjectionSnapshot> OpenCompletedSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await Task.Run(
                () => OpenCompletedSnapshotCore(cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<GraphProjectionPage> ReadPageAsync(
        GraphProjectionSnapshot snapshot,
        GraphProjectionCursor? cursor,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(snapshot);
        if (maximumCount is <= 0 or > GraphLimits.MaximumProjectionPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await Task.Run(
                () => ReadPageCore(snapshot, cursor, maximumCount, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<GraphAuthoritySnapshot> ValidateAuthorityAsync(
        GraphAuthorityRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        ValidateAuthorityRequest(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await Task.Run(
                () => ValidateAuthorityCore(cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposeState, 1);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _authorityConnection?.Dispose();
            _authorityConnection = null;
            _activeSnapshot = null;
            DeleteTransientSnapshot(_snapshotPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    private GraphProjectionSnapshot OpenCompletedSnapshotCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceExists = File.Exists(_deepIndexDatabasePath);
        var sourceStamp = ReadSourceFileStamp(sourceExists);
        if (_activeSnapshot is not null &&
            _activeSnapshot.SourceExisted == sourceExists &&
            _activeSnapshot.SourceStamp == sourceStamp &&
            SnapshotAuthorityIsUnchanged(_activeSnapshot))
        {
            return _activeSnapshot.Snapshot;
        }

        EnsureAuthorityConnection(sourceExists, sourceStamp);
        for (var attempt = 0; attempt < MaximumSnapshotAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var beforeVersion = ReadDataVersion();
            var beforeStamp = ReadSourceFileStamp(sourceExists);
            var stagingPath = string.Concat(_snapshotPath, ".tmp-", Guid.NewGuid().ToString("N"));
            try
            {
                var snapshot = sourceExists
                    ? CaptureSnapshot(stagingPath, cancellationToken)
                    : CaptureEmptySnapshot(stagingPath, cancellationToken);
                var afterVersion = ReadDataVersion();
                var sourceStillExists = File.Exists(_deepIndexDatabasePath);
                var afterStamp = ReadSourceFileStamp(sourceStillExists);
                if (beforeVersion != afterVersion ||
                    sourceStillExists != sourceExists ||
                    beforeStamp != afterStamp)
                {
                    DeleteTransientSnapshot(stagingPath);
                    sourceExists = sourceStillExists;
                    sourceStamp = afterStamp;
                    EnsureAuthorityConnection(sourceExists, sourceStamp);
                    continue;
                }

                PromoteSnapshot(stagingPath);
                _authorityFileStamp = afterStamp;
                _activeSnapshot = new ActiveSnapshot(snapshot, afterVersion, sourceExists, afterStamp);
                return snapshot;
            }
            catch
            {
                DeleteTransientSnapshot(stagingPath);
                throw;
            }
        }

        throw new GraphPersistenceException(
            "source-snapshot-busy",
            "The deep index changed repeatedly while OpenSorSe was creating a consistent graph manifest. Retry after indexing reaches a durable boundary.");
    }

    private GraphProjectionPage ReadPageCore(
        GraphProjectionSnapshot snapshot,
        GraphProjectionCursor? cursor,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        var active = _activeSnapshot;
        if (active is null ||
            !string.Equals(active.Snapshot.ManifestId, snapshot.ManifestId, StringComparison.Ordinal) ||
            active.Snapshot.Revision != snapshot.Revision ||
            !File.Exists(_snapshotPath))
        {
            throw new GraphPersistenceException(
                "source-snapshot-expired",
                "The immutable graph source snapshot is no longer available. Start a new reconciliation generation.");
        }

        var position = DecodeCursor(cursor);
        using var connection = OpenSnapshotConnection(_snapshotPath, readOnly: true);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT kind_name, stable_key, payload_json
            FROM snapshot_observations
            WHERE kind_name > $kind OR (kind_name = $kind AND stable_key > $key)
            ORDER BY kind_name, stable_key
            LIMIT $maximum;
            """;
        command.Parameters.AddWithValue("$kind", position.KindName);
        command.Parameters.AddWithValue("$key", position.StableKey);
        command.Parameters.AddWithValue("$maximum", maximumCount + 1);
        var observations = new List<GraphProjectionObservation>(maximumCount + 1);
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                observations.Add(GraphCanonicalSerializer.DeserializeObservation(reader.GetString(2)));
            }
        }

        var hasMore = observations.Count > maximumCount;
        if (hasMore)
        {
            observations.RemoveAt(observations.Count - 1);
        }

        GraphProjectionCursor? next = null;
        if (hasMore && observations.Count > 0)
        {
            var last = observations[^1];
            next = EncodeCursor(position.PageSequence + 1, last.Kind.ToString(), last.StableKey);
        }

        return new GraphProjectionPage(
            snapshot.ManifestId,
            snapshot.Revision,
            position.PageSequence,
            observations.Count,
            GraphCanonicalSerializer.CalculatePageHash(observations),
            observations,
            next,
            !hasMore);
    }

    private GraphAuthoritySnapshot ValidateAuthorityCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = _activeSnapshot;
        if (active is null)
        {
            return UnavailableAuthority("source-snapshot-unavailable");
        }

        try
        {
            if (!SnapshotAuthorityIsUnchanged(active))
            {
                return new GraphAuthoritySnapshot(
                    true,
                    false,
                    IncrementSaturated(active.Snapshot.PrivacySequence),
                    PendingAuthorityId("legacy", ReadDataVersion()),
                    "source-reconciliation-pending")
                {
                    CurrentSourceManifestId = PendingAuthorityId("source", ReadDataVersion()),
                    CurrentSourceRevision = IncrementSaturated(active.Snapshot.Revision),
                };
            }

            return new GraphAuthoritySnapshot(
                true,
                true,
                active.Snapshot.PrivacySequence,
                active.Snapshot.LegacyDecisionManifestId,
                "authority-current")
            {
                CurrentSourceManifestId = active.Snapshot.ManifestId,
                CurrentSourceRevision = active.Snapshot.Revision,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or SqliteException or UnauthorizedAccessException)
        {
            return UnavailableAuthority("source-authority-unavailable");
        }
    }

    private bool SnapshotAuthorityIsUnchanged(ActiveSnapshot snapshot)
    {
        if (File.Exists(_deepIndexDatabasePath) != snapshot.SourceExisted)
        {
            return false;
        }

        return !snapshot.SourceExisted ||
            (ReadSourceFileStamp(sourceExists: true) == snapshot.SourceStamp &&
             ReadDataVersion() == snapshot.DataVersion);
    }

    private void EnsureAuthorityConnection(bool sourceExists, SourceFileStamp sourceStamp)
    {
        if (!sourceExists)
        {
            _authorityConnection?.Dispose();
            _authorityConnection = null;
            _authorityFileStamp = SourceFileStamp.Missing;
            return;
        }

        if (_authorityConnection is not null && _authorityFileStamp == sourceStamp)
        {
            return;
        }

        _authorityConnection?.Dispose();
        _authorityConnection = OpenDeepIndexConnection();
        _authorityFileStamp = sourceStamp;
    }

    private SourceFileStamp ReadSourceFileStamp(bool sourceExists)
    {
        if (!sourceExists)
        {
            return SourceFileStamp.Missing;
        }

        try
        {
            var database = new FileInfo(_deepIndexDatabasePath);
            var wal = new FileInfo(string.Concat(_deepIndexDatabasePath, "-wal"));
            var identity = _fileIdentityProvider.Capture(_deepIndexDatabasePath);

            return new SourceFileStamp(
                database.Length,
                database.CreationTimeUtc.Ticks,
                database.LastWriteTimeUtc.Ticks,
                wal.Exists,
                wal.Exists ? wal.Length : 0,
                wal.Exists ? wal.LastWriteTimeUtc.Ticks : 0,
                identity.Identity ?? string.Empty);
        }
        catch (FileNotFoundException)
        {
            return SourceFileStamp.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return SourceFileStamp.Missing;
        }
    }

    private long ReadDataVersion()
    {
        if (_authorityConnection is null)
        {
            return 0;
        }

        using var command = _authorityConnection.CreateCommand();
        command.CommandText = "PRAGMA data_version;";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private void PromoteSnapshot(string stagingPath)
    {
        _activeSnapshot = null;
        DeleteTransientSnapshot(_snapshotPath);
        File.Move(stagingPath, _snapshotPath);
    }

    private static GraphProjectionCursor EncodeCursor(long pageSequence, string kindName, string stableKey)
    {
        var value = string.Concat(
            pageSequence.ToString(CultureInfo.InvariantCulture),
            ".",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(kindName)),
            ".",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(stableKey)));
        return new GraphProjectionCursor(value);
    }

    private static CursorPosition DecodeCursor(GraphProjectionCursor? cursor)
    {
        if (cursor is null)
        {
            return new CursorPosition(0, string.Empty, string.Empty);
        }

        if (cursor.Value.Length > 1024)
        {
            throw new ArgumentException("The graph source continuation cursor is oversized.", nameof(cursor));
        }

        var parts = cursor.Value.Split('.', StringSplitOptions.None);
        if (parts.Length != 3 ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pageSequence) ||
            pageSequence <= 0)
        {
            throw new ArgumentException("The graph source continuation cursor is invalid.", nameof(cursor));
        }

        try
        {
            var kindName = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
            var stableKey = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
            if (!Enum.TryParse<GraphProjectionObservationKind>(kindName, ignoreCase: false, out _) ||
                string.IsNullOrWhiteSpace(stableKey) ||
                stableKey.Length > GraphLimits.MaximumStableIdCharacters)
            {
                throw new ArgumentException("The graph source continuation cursor is invalid.", nameof(cursor));
            }

            return new CursorPosition(pageSequence, kindName, stableKey);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The graph source continuation cursor is invalid.", nameof(cursor), exception);
        }
    }

    private SqliteConnection OpenDeepIndexConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _deepIndexDatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ToString());
        try
        {
            connection.Open();
            ExecutePragma(connection, "PRAGMA query_only = ON;");
            ExecutePragma(connection, $"PRAGMA busy_timeout = {SqliteKnowledgeInfrastructure.BusyTimeoutMilliseconds};");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static SqliteConnection OpenSnapshotConnection(string path, bool readOnly)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();
        ExecutePragma(connection, "PRAGMA foreign_keys = ON;");
        ExecutePragma(connection, $"PRAGMA busy_timeout = {SqliteKnowledgeInfrastructure.BusyTimeoutMilliseconds};");
        if (readOnly)
        {
            ExecutePragma(connection, "PRAGMA query_only = ON;");
        }
        else
        {
            ExecutePragma(connection, "PRAGMA journal_mode = DELETE;");
            ExecutePragma(connection, "PRAGMA synchronous = FULL;");
        }

        return connection;
    }

    private static void ExecutePragma(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void DeleteTransientSnapshot(string path)
    {
        foreach (var candidate in new[] { path, string.Concat(path, "-wal"), string.Concat(path, "-shm") })
        {
            try
            {
                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
            }
            catch (IOException)
            {
                // A later capture uses a unique staging file and retries cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Disposal is best effort; no source or authoritative database is affected.
            }
        }
    }

    private static void ValidateAuthorityRequest(GraphAuthorityRequest request)
    {
        if (request.StableKeys.Count > GraphLimits.MaximumStableTraversalNodes ||
            request.StableKeys.Any(key => string.IsNullOrWhiteSpace(key) || key.Length > GraphLimits.MaximumStableIdCharacters) ||
            string.IsNullOrWhiteSpace(request.OperationCode) || request.OperationCode.Length > 64)
        {
            throw new ArgumentException("The graph authority request exceeds its safety bounds.", nameof(request));
        }
    }

    private static GraphAuthoritySnapshot UnavailableAuthority(string reasonCode) => new(
        false,
        false,
        0,
        string.Empty,
        reasonCode)
    {
        CurrentSourceManifestId = null,
        CurrentSourceRevision = 0,
    };

    private static string PendingAuthorityId(string scope, long revision) =>
        string.Concat("pending:", scope, ":", Math.Max(0, revision).ToString(CultureInfo.InvariantCulture));

    private static long IncrementSaturated(long value) => value == long.MaxValue ? long.MaxValue : value + 1;

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

    private sealed record ActiveSnapshot(
        GraphProjectionSnapshot Snapshot,
        long DataVersion,
        bool SourceExisted,
        SourceFileStamp SourceStamp);

    private readonly record struct SourceFileStamp(
        long DatabaseLength,
        long DatabaseCreationTicks,
        long DatabaseLastWriteTicks,
        bool WalExists,
        long WalLength,
        long WalLastWriteTicks,
        string FileIdentity)
    {
        internal static SourceFileStamp Missing { get; } = new(0, 0, 0, false, 0, 0, string.Empty);
    }

    private readonly record struct CursorPosition(long PageSequence, string KindName, string StableKey);
}
