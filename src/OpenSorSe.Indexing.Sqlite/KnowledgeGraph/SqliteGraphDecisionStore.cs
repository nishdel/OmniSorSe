using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

/// <summary>Persists the authoritative graph-native decision ledger in an isolated SQLite database.</summary>
public sealed class SqliteGraphDecisionStore : IGraphDecisionStore
{
    private const string ClearConfirmation = "CLEAR GRAPH DECISIONS";
    private const string RestoreConfirmation = "RESTORE GRAPH DECISIONS";
    private const int MaximumReadCount = 1_000;
    private const int MaximumOrdinaryBackups = 5;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _databasePath;
    private readonly string _backupDirectory;
    private readonly string _lifecycleLockPath;
    private readonly string _restoreJournalPath;
    private readonly string _restorePreviousPath;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private SqliteConnection? _activeWriterConnection;
    private CancellationToken _writerCancellationToken;
    private int _disposeState;
    private bool _initialized;

    /// <summary>Creates an isolated decision store at an application-owned path.</summary>
    public SqliteGraphDecisionStore(string databasePath, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(_databasePath)
            ?? throw new ArgumentException("The decision database must have a parent directory.", nameof(databasePath));
        _backupDirectory = Path.Combine(directory, "backups", "knowledge-decisions");
        _lifecycleLockPath = Path.Combine(directory, ".knowledge-data.lifecycle.lock");
        _restoreJournalPath = Path.Combine(directory, ".knowledge-decisions.restore.json");
        _restorePreviousPath = Path.Combine(directory, ".knowledge-decisions.restore.previous.db");
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets the fully qualified application-owned database path.</summary>
    public string DatabasePath => _databasePath;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_initialized)
            {
                return;
            }

            if (File.Exists(_restoreJournalPath))
            {
                var restoreLock = new SqliteKnowledgeLifecycleLock(_lifecycleLockPath, _timeProvider);
                await using var restoreLease = await restoreLock.AcquireAsync(
                        SqliteKnowledgeInfrastructure.LifecycleTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
                await Task.Run(
                        () => RecoverInterruptedDecisionRestore(cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await SqliteKnowledgeInfrastructure.InitializeAsync(
                    _databasePath,
                    _lifecycleLockPath,
                    SqliteKnowledgeDecisionSchema.ApplicationId,
                    SqliteKnowledgeDecisionSchema.Version,
                    SqliteKnowledgeDecisionSchema.RequiredTables,
                    SqliteKnowledgeDecisionSchema.CreateVersionOne,
                    "decision_meta",
                    "decision_migration_history",
                    _timeProvider,
                    cancellationToken,
                    SqliteKnowledgeDecisionSchema.RequiredColumns,
                    SqliteKnowledgeDecisionSchema.RequiredIndexes)
                .ConfigureAwait(false);
            var lifecycleLock = new SqliteKnowledgeLifecycleLock(_lifecycleLockPath, _timeProvider);
            await using var lease = await lifecycleLock.AcquireAsync(
                    SqliteKnowledgeInfrastructure.LifecycleTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
            using var connection = OpenConnection();
            EnsureLegacyMirrorExtension(connection);
            RecoverInterruptedBackups(connection);
            ValidateDecisionState(connection);
            RetireBackupsBelowPrivacyFloor(connection);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<GraphDecisionSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                long sequence;
                long generation;
                string retainedHash;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "SELECT current_decision_sequence, decision_checkpoint_hash, active_store_generation FROM decision_recovery_state WHERE singleton_id = 1;";
                    using var reader = command.ExecuteReader();
                    if (!reader.Read())
                    {
                        throw SqliteKnowledgeInfrastructure.Corrupt("The decision recovery checkpoint is missing.");
                    }

                    sequence = reader.GetInt64(0);
                    retainedHash = reader.GetString(1);
                    generation = reader.GetInt64(2);
                }

                var calculatedHash = CalculateLedgerHash(connection);
                return new GraphDecisionSnapshot(
                    sequence,
                    $"decision-checkpoint-{generation.ToString(CultureInfo.InvariantCulture)}-{sequence.ToString(CultureInfo.InvariantCulture)}",
                    retainedHash,
                    string.Equals(retainedHash, calculatedHash, StringComparison.Ordinal));
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<GraphControlSettings> GetControlSettingsAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                var json = SqliteKnowledgeInfrastructure.ExecuteScalar(
                    connection,
                    "SELECT settings_json FROM graph_settings WHERE singleton_id = 1;") as string;
                return json is null
                    ? new GraphControlSettings()
                    : JsonSerializer.Deserialize<GraphControlSettings>(json, JsonOptions)
                      ?? throw SqliteKnowledgeInfrastructure.Corrupt("The graph control settings payload is malformed.");
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<GraphControlSettings> SetControlSettingsAsync(
        GraphControlSettings settings,
        long expectedRevision,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateControlSettings(settings);
        if (expectedRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        }

        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                var retainedRevision = Convert.ToInt64(
                    ExecuteScalar(connection, transaction, "SELECT COALESCE((SELECT settings_version FROM graph_settings WHERE singleton_id = 1), 0);") ?? 0,
                    CultureInfo.InvariantCulture);
                if (retainedRevision != expectedRevision)
                {
                    throw new InvalidOperationException("Graph control settings changed. Refresh and retry.");
                }

                var saved = settings with { Revision = checked(expectedRevision + 1) };
                var json = JsonSerializer.Serialize(saved, JsonOptions);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO graph_settings(
                        singleton_id, settings_version, settings_json, settings_fingerprint,
                        updated_utc_ticks, decision_sequence)
                    VALUES (1, $version, $json, $fingerprint, $now,
                            (SELECT current_decision_sequence FROM decision_recovery_state WHERE singleton_id = 1))
                    ON CONFLICT(singleton_id) DO UPDATE SET
                        settings_version = excluded.settings_version,
                        settings_json = excluded.settings_json,
                        settings_fingerprint = excluded.settings_fingerprint,
                        updated_utc_ticks = excluded.updated_utc_ticks,
                        decision_sequence = excluded.decision_sequence;
                    """,
                    ("$version", saved.Revision), ("$json", json),
                    ("$fingerprint", Hash(json)), ("$now", nowUtc.UtcTicks));
                transaction.Commit();
                return saved;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GraphDecisionEntry> AppendAsync(
        GraphDecisionCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command);
        return await AppendRecoverableDecisionAsync(command, nowUtc, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphDecisionEntry>> ReadAsync(
        long afterSequence,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (afterSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(afterSequence));
        }

        maximumCount = Math.Clamp(maximumCount, 1, MaximumReadCount);
        return RunAsync<IReadOnlyList<GraphDecisionEntry>>(
            () =>
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT decision_id, decision_sequence, payload_json, created_utc_ticks,
                           substr(idempotency_key, instr(idempotency_key, ':') + 1)
                    FROM graph_native_decisions
                    WHERE decision_sequence > $after
                    ORDER BY decision_sequence
                    LIMIT $maximum;
                    """;
                SqliteKnowledgeInfrastructure.AddParameters(
                    command,
                    ("$after", afterSequence),
                    ("$maximum", maximumCount));
                var entries = new List<GraphDecisionEntry>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var persistedCommand = JsonSerializer.Deserialize<GraphDecisionCommand>(reader.GetString(2), JsonOptions)
                        ?? throw SqliteKnowledgeInfrastructure.Corrupt("A graph-native decision payload is malformed.");
                    entries.Add(new GraphDecisionEntry(
                        reader.GetString(0),
                        reader.GetInt64(1),
                        persistedCommand,
                        new DateTimeOffset(reader.GetInt64(3), TimeSpan.Zero),
                        reader.GetString(4)));
                }

                return entries;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GraphOperationResult> ClearAsync(
        string confirmationText,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(confirmationText, ClearConfirmation, StringComparison.Ordinal))
        {
            return new GraphOperationResult(false, $"Type {ClearConfirmation} to clear graph-native decisions.", 0);
        }

        var affected = await ClearWithPreparedBackupAsync(nowUtc, cancellationToken).ConfigureAwait(false);
        return new GraphOperationResult(true, "Graph-native decisions were cleared. Original files were not changed.", affected);
    }

    /// <inheritdoc />
    public Task<string?> GetLegacyMirrorManifestIdAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                return SqliteKnowledgeInfrastructure.ExecuteScalar(
                    connection,
                    "SELECT manifest_id FROM legacy_mirror_ingest_manifests WHERE state = 'Complete' ORDER BY completed_utc_ticks DESC, manifest_id DESC LIMIT 1;") as string;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task BeginLegacyMirrorAsync(
        string manifestId,
        long expectedCount,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateStableDecisionId(manifestId, nameof(manifestId));
        if (expectedCount is < 0 or > GraphLimits.MaximumLegacyDecisionMirrorRows)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedCount));
        }

        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                using (var existing = connection.CreateCommand())
                {
                    existing.Transaction = transaction;
                    existing.CommandText =
                        "SELECT expected_count, state FROM legacy_mirror_ingest_manifests WHERE manifest_id = $manifest;";
                    existing.Parameters.AddWithValue("$manifest", manifestId);
                    using var reader = existing.ExecuteReader();
                    if (reader.Read())
                    {
                        if (reader.GetInt64(0) != expectedCount)
                        {
                            throw new InvalidOperationException(
                                "The legacy decision mirror manifest was already started with a different row count.");
                        }

                        var state = reader.GetString(1);
                        if (state is not ("Capturing" or "Complete"))
                        {
                            throw SqliteKnowledgeInfrastructure.Corrupt(
                                "The legacy decision mirror manifest has an invalid durable state.");
                        }

                        transaction.Commit();
                        return 0;
                    }
                }

                ExecuteNonQuery(
                    connection,
                    transaction,
                    "DELETE FROM legacy_mirror_ingest_manifests WHERE state = 'Capturing' AND manifest_id <> $manifest;",
                    ("$manifest", manifestId));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO legacy_mirror_ingest_manifests(
                        manifest_id, expected_count, staged_count, next_page_sequence,
                        state, created_utc_ticks, updated_utc_ticks)
                    VALUES ($manifest, $expected, 0, 0, 'Capturing', $now, $now);
                    """,
                    ("$manifest", manifestId), ("$expected", expectedCount), ("$now", nowUtc.UtcTicks));
                transaction.Commit();
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task StageLegacyMirrorPageAsync(
        string manifestId,
        long pageSequence,
        IReadOnlyList<GraphLegacyDecisionObservation> observations,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateStableDecisionId(manifestId, nameof(manifestId));
        ArgumentNullException.ThrowIfNull(observations);
        if (pageSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSequence));
        }

        if (observations.Count > GraphLimits.MaximumProjectionPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(observations));
        }

        var ordered = observations.OrderBy(item => item.StableKey, StringComparer.Ordinal).ToArray();
        var pageKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var observation in ordered)
        {
            ValidateLegacyMirrorObservation(observation);
            if (!pageKeys.Add($"{observation.DecisionNamespace}\u001f{observation.LegacyDecisionKey}"))
            {
                throw new ArgumentException("The legacy mirror page contains a duplicate decision key.", nameof(observations));
            }
        }

        var pageHash = GraphCanonicalSerializer.CalculatePageHash(ordered);
        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                using (var existingPage = connection.CreateCommand())
                {
                    existingPage.Transaction = transaction;
                    existingPage.CommandText =
                        "SELECT canonical_page_hash, row_count FROM legacy_mirror_ingest_pages WHERE manifest_id = $manifest AND page_sequence = $page;";
                    SqliteKnowledgeInfrastructure.AddParameters(
                        existingPage,
                        ("$manifest", manifestId),
                        ("$page", pageSequence));
                    using var reader = existingPage.ExecuteReader();
                    if (reader.Read())
                    {
                        if (!string.Equals(reader.GetString(0), pageHash, StringComparison.Ordinal) ||
                            reader.GetInt32(1) != ordered.Length)
                        {
                            throw new InvalidOperationException(
                                "A legacy mirror page sequence was replayed with different immutable content.");
                        }

                        transaction.Commit();
                        return 0;
                    }
                }

                long expectedCount;
                long stagedCount;
                long nextPageSequence;
                string state;
                using (var manifest = connection.CreateCommand())
                {
                    manifest.Transaction = transaction;
                    manifest.CommandText =
                        "SELECT expected_count, staged_count, next_page_sequence, state FROM legacy_mirror_ingest_manifests WHERE manifest_id = $manifest;";
                    manifest.Parameters.AddWithValue("$manifest", manifestId);
                    using var reader = manifest.ExecuteReader();
                    if (!reader.Read())
                    {
                        throw new InvalidOperationException("The legacy mirror manifest has not been started.");
                    }

                    expectedCount = reader.GetInt64(0);
                    stagedCount = reader.GetInt64(1);
                    nextPageSequence = reader.GetInt64(2);
                    state = reader.GetString(3);
                }

                if (!string.Equals(state, "Capturing", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("A completed legacy mirror manifest cannot accept new pages.");
                }

                if (pageSequence != nextPageSequence)
                {
                    throw new InvalidOperationException("Legacy mirror pages must be staged in contiguous sequence order.");
                }

                if (ordered.Length == 0)
                {
                    if (expectedCount != 0 || stagedCount != 0 || pageSequence != 0)
                    {
                        throw new InvalidOperationException(
                            "Only an empty declared legacy mirror manifest may stage an empty terminal page.");
                    }

                    transaction.Commit();
                    return 0;
                }

                if (ordered.Length > expectedCount - stagedCount)
                {
                    throw new InvalidOperationException("The legacy mirror page exceeds the manifest's declared row count.");
                }

                ExecuteNonQuery(
                    connection,
                    transaction,
                    "INSERT INTO legacy_mirror_ingest_pages(manifest_id, page_sequence, canonical_page_hash, row_count, created_utc_ticks) VALUES ($manifest, $page, $hash, $count, $now);",
                    ("$manifest", manifestId), ("$page", pageSequence), ("$hash", pageHash),
                    ("$count", ordered.Length), ("$now", nowUtc.UtcTicks));
                foreach (var observation in ordered)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var payload = JsonSerializer.Serialize(observation, JsonOptions);
                    if (payload.Length > 65_536)
                    {
                        throw new ArgumentOutOfRangeException(nameof(observations), "A legacy mirror payload exceeds its durable bound.");
                    }

                    ExecuteNonQuery(
                        connection,
                        transaction,
                        """
                        INSERT INTO legacy_mirror_ingest_rows(
                            manifest_id, page_sequence, stable_key, legacy_kind, legacy_key,
                            canonical_row_hash, payload_json, is_present, observed_sequence, observed_utc_ticks)
                        VALUES ($manifest, $page, $stable, $kind, $key, $hash, $payload, $present, $sequence, $observed);
                        """,
                        ("$manifest", manifestId), ("$page", pageSequence), ("$stable", observation.StableKey),
                        ("$kind", observation.DecisionNamespace), ("$key", observation.LegacyDecisionKey),
                        ("$hash", observation.CanonicalRowHash), ("$payload", payload),
                        ("$present", observation.IsRetired ? 0 : 1), ("$sequence", observation.Revision),
                        ("$observed", observation.ObservedAtUtc.UtcTicks));
                }

                ExecuteNonQuery(
                    connection,
                    transaction,
                    "UPDATE legacy_mirror_ingest_manifests SET staged_count = staged_count + $count, next_page_sequence = next_page_sequence + 1, updated_utc_ticks = $now WHERE manifest_id = $manifest AND state = 'Capturing';",
                    ("$count", ordered.Length), ("$now", nowUtc.UtcTicks), ("$manifest", manifestId));
                transaction.Commit();
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task CompleteLegacyMirrorAsync(
        string manifestId,
        long expectedCount,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateStableDecisionId(manifestId, nameof(manifestId));
        if (expectedCount is < 0 or > GraphLimits.MaximumLegacyDecisionMirrorRows)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedCount));
        }

        return RunAsync(
            () =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                long retainedExpected;
                long stagedCount;
                string state;
                using (var manifest = connection.CreateCommand())
                {
                    manifest.Transaction = transaction;
                    manifest.CommandText =
                        "SELECT expected_count, staged_count, state FROM legacy_mirror_ingest_manifests WHERE manifest_id = $manifest;";
                    manifest.Parameters.AddWithValue("$manifest", manifestId);
                    using var reader = manifest.ExecuteReader();
                    if (!reader.Read())
                    {
                        throw new InvalidOperationException("The legacy mirror manifest has not been started.");
                    }

                    retainedExpected = reader.GetInt64(0);
                    stagedCount = reader.GetInt64(1);
                    state = reader.GetString(2);
                }

                if (retainedExpected != expectedCount)
                {
                    throw new InvalidOperationException("The legacy mirror completion count does not match its durable manifest.");
                }

                if (string.Equals(state, "Complete", StringComparison.Ordinal))
                {
                    transaction.Commit();
                    return 0;
                }

                if (!string.Equals(state, "Capturing", StringComparison.Ordinal) || stagedCount != expectedCount)
                {
                    throw new InvalidOperationException("The legacy mirror cannot publish until every declared row is durably staged.");
                }

                var retainedRows = new List<(string Kind, string StableKey, string RowHash)>();
                using (var rows = connection.CreateCommand())
                {
                    rows.Transaction = transaction;
                    rows.CommandText =
                        "SELECT stable_key, canonical_row_hash FROM legacy_mirror_ingest_rows WHERE manifest_id = $manifest ORDER BY stable_key;";
                    rows.Parameters.AddWithValue("$manifest", manifestId);
                    using var reader = rows.ExecuteReader();
                    while (reader.Read())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        retainedRows.Add((GraphProjectionObservationKind.LegacyDecision.ToString(), reader.GetString(0), reader.GetString(1)));
                    }
                }

                if (retainedRows.Count != expectedCount)
                {
                    throw SqliteKnowledgeInfrastructure.Corrupt("The legacy mirror staged-row count disagrees with its durable manifest.");
                }

                var aggregateHash = GraphCanonicalSerializer.CalculateManifestHash(retainedRows);
                const string manifestPrefix = "kg-legacy:";
                if (manifestId.StartsWith(manifestPrefix, StringComparison.Ordinal) &&
                    !string.Equals(manifestId[manifestPrefix.Length..], aggregateHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The staged legacy mirror does not match the immutable manifest identity.");
                }

                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO legacy_relationship_decision_mirror(
                        legacy_kind, legacy_key, manifest_id, canonical_row_hash, payload_json,
                        is_present, observed_sequence, observed_utc_ticks)
                    SELECT legacy_kind, legacy_key, manifest_id, canonical_row_hash, payload_json,
                           is_present, observed_sequence, observed_utc_ticks
                    FROM legacy_mirror_ingest_rows
                    WHERE manifest_id = $manifest
                    ON CONFLICT(legacy_kind, legacy_key) DO UPDATE SET
                        manifest_id = excluded.manifest_id,
                        canonical_row_hash = excluded.canonical_row_hash,
                        payload_json = excluded.payload_json,
                        is_present = excluded.is_present,
                        observed_sequence = excluded.observed_sequence,
                        observed_utc_ticks = excluded.observed_utc_ticks;
                    """,
                    ("$manifest", manifestId));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE legacy_relationship_decision_mirror
                    SET is_present = 0, payload_json = NULL, manifest_id = $manifest, observed_utc_ticks = $now
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM legacy_mirror_ingest_rows staged
                        WHERE staged.manifest_id = $manifest
                          AND staged.legacy_kind = legacy_relationship_decision_mirror.legacy_kind
                          AND staged.legacy_key = legacy_relationship_decision_mirror.legacy_key);
                    """,
                    ("$manifest", manifestId), ("$now", nowUtc.UtcTicks));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "UPDATE legacy_mirror_ingest_manifests SET state = 'Complete', canonical_aggregate_hash = $hash, completed_utc_ticks = $now, updated_utc_ticks = $now WHERE manifest_id = $manifest AND state = 'Capturing';",
                    ("$hash", aggregateHash), ("$now", nowUtc.UtcTicks), ("$manifest", manifestId));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "DELETE FROM legacy_mirror_ingest_manifests WHERE manifest_id <> $manifest;",
                    ("$manifest", manifestId));

                transaction.Commit();
                return 0;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphDecisionRecoveryPoint>> GetRecoveryPointsAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!Directory.Exists(_backupDirectory))
        {
            return [];
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var lifecycleLock = new SqliteKnowledgeLifecycleLock(_lifecycleLockPath, _timeProvider);
            await using var lease = await lifecycleLock.AcquireAsync(
                    SqliteKnowledgeInfrastructure.LifecycleTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
            return await Task.Run(
                    () => ReadRecoveryPoints(cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<GraphOperationResult> RestoreAsync(
        string recoveryPointId,
        string confirmationText,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateStableDecisionId(recoveryPointId, nameof(recoveryPointId));
        if (!string.Equals(confirmationText, RestoreConfirmation, StringComparison.Ordinal))
        {
            return new GraphOperationResult(false, $"Type {RestoreConfirmation} to restore graph-native decisions.", 0);
        }

        ThrowIfDisposed();
        if (!Directory.Exists(_backupDirectory))
        {
            return new GraphOperationResult(false, "No managed graph-decision recovery points are available.", 0);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var lifecycleLock = new SqliteKnowledgeLifecycleLock(_lifecycleLockPath, _timeProvider);
            await using var lease = await lifecycleLock.AcquireAsync(
                    SqliteKnowledgeInfrastructure.LifecycleTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
            return await Task.Run(
                    () => RestoreRecoveryPoint(recoveryPointId, nowUtc, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
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
        // Release rather than dispose the managed gate so a caller that passed
        // its first disposal check just before fencing can wake, observe the
        // disposed state, and fail without performing I/O.
        _gate.Release();
    }

    private static GraphDecisionEntry? ReadDuplicate(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string idempotencyKey,
        GraphDecisionCommand command,
        string commandHash)
    {
        using var duplicate = connection.CreateCommand();
        duplicate.Transaction = transaction;
        duplicate.CommandText =
            "SELECT decision_id, decision_sequence, created_utc_ticks FROM graph_native_decisions WHERE idempotency_key = $key;";
        duplicate.Parameters.AddWithValue("$key", idempotencyKey);
        using var reader = duplicate.ExecuteReader();
        return reader.Read()
            ? new GraphDecisionEntry(
                reader.GetString(0),
                reader.GetInt64(1),
                command,
                new DateTimeOffset(reader.GetInt64(2), TimeSpan.Zero),
                commandHash)
            : null;
    }

    private static GraphDecisionEntry ApplyNewDecision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GraphDecisionCommand command,
        DateTimeOffset nowUtc,
        string payload,
        string commandHash,
        string idempotencyKey,
        string decisionId,
        string aliasId,
        bool advancePrivacyFloor)
    {
        var (currentSequence, previousHash) = ReadCheckpoint(connection, transaction);
        if (currentSequence != command.ExpectedSequence)
        {
            throw new InvalidOperationException(
                $"The graph decision sequence changed from {command.ExpectedSequence} to {currentSequence}. Refresh and retry.");
        }

        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO graph_native_decisions(
                decision_id, idempotency_key, decision_type, target_kind, target_key,
                payload_json, supersedes_decision_id, is_tombstone, created_utc_ticks)
            VALUES ($id, $idempotency, $type, $targetKind, $targetKey, $payload, NULL, $tombstone, $created);
            """,
            ("$id", decisionId),
            ("$idempotency", idempotencyKey),
            ("$type", command.Kind.ToString()),
            ("$targetKind", command.NodeKind?.Value ?? "graph"),
            ("$targetKey", command.SubjectId),
            ("$payload", payload),
            ("$tombstone", command.Kind is GraphDecisionKind.Forget or GraphDecisionKind.RemoveAlias or GraphDecisionKind.UnlinkNodes ? 1 : 0),
            ("$created", nowUtc.UtcTicks));
        var sequence = Convert.ToInt64(ExecuteScalar(connection, transaction, "SELECT last_insert_rowid();"), CultureInfo.InvariantCulture);
        ApplyMaterializedDecision(connection, transaction, command, decisionId, aliasId, sequence, nowUtc);
        var isPrivacyDecision = command.Kind is GraphDecisionKind.Forget or GraphDecisionKind.Exclude;
        var checkpointHash = Hash($"{previousHash}|{sequence.ToString(CultureInfo.InvariantCulture)}|{commandHash}");
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE decision_recovery_state
            SET current_decision_sequence = $sequence,
                current_privacy_sequence = current_privacy_sequence + $privacy,
                minimum_restorable_privacy_sequence = CASE
                    WHEN $advanceFloor = 1
                    THEN MAX(minimum_restorable_privacy_sequence, current_privacy_sequence + $privacy)
                    ELSE minimum_restorable_privacy_sequence
                END,
                decision_checkpoint_hash = $hash,
                updated_utc_ticks = $now
            WHERE singleton_id = 1;
            """,
            ("$sequence", sequence),
            ("$privacy", isPrivacyDecision ? 1 : 0),
            ("$advanceFloor", advancePrivacyFloor ? 1 : 0),
            ("$hash", checkpointHash),
            ("$now", nowUtc.UtcTicks));
        return new GraphDecisionEntry(decisionId, sequence, command, nowUtc, commandHash);
    }

    private static void ApplyMaterializedDecision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GraphDecisionCommand command,
        string decisionId,
        string aliasId,
        long sequence,
        DateTimeOffset nowUtc)
    {
        switch (command.Kind)
        {
            case GraphDecisionKind.CreateManualEntity:
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO graph_manual_entities(entity_id, entity_type, display_name, created_by_decision_id, last_decision_sequence, is_deleted, updated_utc_ticks)
                    VALUES ($id, $type, $name, $decision, $sequence, 0, $now);
                    """,
                    ("$id", command.SubjectId),
                    ("$type", command.NodeKind?.Value ?? GraphNodeKind.ManualEntity.Value),
                    ("$name", command.Label!),
                    ("$decision", decisionId),
                    ("$sequence", sequence),
                    ("$now", nowUtc.UtcTicks));
                break;
            case GraphDecisionKind.RenameManualEntity:
                RequireAffected(
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "UPDATE graph_manual_entities SET display_name = $name, last_decision_sequence = $sequence, updated_utc_ticks = $now WHERE entity_id = $id AND is_deleted = 0;",
                        ("$name", command.Label!), ("$sequence", sequence), ("$now", nowUtc.UtcTicks), ("$id", command.SubjectId)),
                    "The manual entity no longer exists.");
                break;
            case GraphDecisionKind.AddAlias:
                var aliasCount = Convert.ToInt32(
                    ExecuteScalar(connection, transaction, "SELECT COUNT(*) FROM graph_entity_aliases WHERE entity_id = $id AND is_deleted = 0;", ("$id", command.SubjectId)),
                    CultureInfo.InvariantCulture);
                if (aliasCount >= GraphLimits.MaximumAliasesPerNode)
                {
                    throw new InvalidOperationException("The manual entity has reached the bounded alias limit.");
                }

                var normalized = NormalizeAlias(command.Label!);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    INSERT INTO graph_entity_aliases(alias_id, entity_id, normalized_alias, display_alias, created_by_decision_id, last_decision_sequence, is_deleted, updated_utc_ticks)
                    VALUES ($aliasId, $entity, $normalized, $display, $decision, $sequence, 0, $now)
                    ON CONFLICT(entity_id, normalized_alias) DO UPDATE SET
                        display_alias = excluded.display_alias,
                        last_decision_sequence = excluded.last_decision_sequence,
                        is_deleted = 0,
                        updated_utc_ticks = excluded.updated_utc_ticks;
                    """,
                    ("$aliasId", aliasId),
                    ("$entity", command.SubjectId),
                    ("$normalized", normalized),
                    ("$display", command.Label!),
                    ("$decision", decisionId),
                    ("$sequence", sequence),
                    ("$now", nowUtc.UtcTicks));
                break;
            case GraphDecisionKind.RemoveAlias:
                RequireAffected(
                    ExecuteNonQuery(
                        connection,
                        transaction,
                        "UPDATE graph_entity_aliases SET is_deleted = 1, last_decision_sequence = $sequence, updated_utc_ticks = $now WHERE entity_id = $entity AND normalized_alias = $alias AND is_deleted = 0;",
                        ("$sequence", sequence), ("$now", nowUtc.UtcTicks), ("$entity", command.SubjectId), ("$alias", NormalizeAlias(command.Label!))),
                    "The selected alias no longer exists.");
                break;
            case GraphDecisionKind.Forget when command.NodeKind == GraphNodeKind.ManualEntity:
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "UPDATE graph_manual_entities SET is_deleted = 1, last_decision_sequence = $sequence, updated_utc_ticks = $now WHERE entity_id = $id;",
                    ("$sequence", sequence), ("$now", nowUtc.UtcTicks), ("$id", command.SubjectId));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "UPDATE graph_entity_aliases SET is_deleted = 1, last_decision_sequence = $sequence, updated_utc_ticks = $now WHERE entity_id = $id;",
                    ("$sequence", sequence), ("$now", nowUtc.UtcTicks), ("$id", command.SubjectId));
                break;
        }
    }

    private async Task<GraphDecisionEntry> AppendRecoverableDecisionAsync(
        GraphDecisionCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureInitialized();
            var lifecycleLock = new SqliteKnowledgeLifecycleLock(_lifecycleLockPath, _timeProvider);
            await using var lease = await lifecycleLock.AcquireAsync(
                    SqliteKnowledgeInfrastructure.LifecycleTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
            return await Task.Run(
                    () => AppendRecoverableDecisionCore(command, nowUtc, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private GraphDecisionEntry AppendRecoverableDecisionCore(
        GraphDecisionCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_backupDirectory);
        var payload = JsonSerializer.Serialize(command, JsonOptions);
        var commandHash = Hash(payload);
        var idempotencyKey = $"{command.ExpectedSequence.ToString(CultureInfo.InvariantCulture)}:{commandHash}";
        using var source = OpenConnection();
        using var transaction = source.BeginTransaction(deferred: false);
        ValidateEnabledSettingsFence(source, transaction, command.ExpectedControlSettingsRevision);
        var isPrivacyDecision = command.Kind is GraphDecisionKind.Forget or GraphDecisionKind.Exclude;
        var duplicate = ReadDuplicate(source, transaction, idempotencyKey, command, commandHash);
        if (duplicate is not null)
        {
            var (privacy, floor, _) = ReadPrivacyState(source, transaction);
            if (isPrivacyDecision && floor < privacy)
            {
                throw SqliteKnowledgeInfrastructure.Corrupt(
                    "A committed privacy decision is missing its verified privacy-floor backup boundary.");
            }

            return duplicate;
        }

        var (currentSequence, _) = ReadCheckpoint(source, transaction);
        if (currentSequence != command.ExpectedSequence)
        {
            throw new InvalidOperationException(
                $"The graph decision sequence changed from {command.ExpectedSequence} to {currentSequence}. Refresh and retry.");
        }

        var (currentPrivacy, _, storeGeneration) = ReadPrivacyState(source, transaction);
        var nextPrivacy = checked(currentPrivacy + (isPrivacyDecision ? 1 : 0));
        var expectedSequence = checked(currentSequence + 1);
        var decisionId = $"decision-{Guid.NewGuid():N}";
        var aliasId = $"alias-{Guid.NewGuid():N}";
        var retiredPaths = isPrivacyDecision
            ? ReadBackupPathsBelowPrivacyFloor(source, transaction, nextPrivacy).ToList()
            : [];
        PreparedDecisionBackup? prepared = null;
        try
        {
            EnsureBackupCapacity(source, transaction);
            prepared = PrepareDecisionBackup(
                isPrivacyDecision ? "privacy-decision" : "manual-decision",
                isPrivacyDecision ? "privacy" : "ordinary",
                pinned: isPrivacyDecision,
                storeGeneration,
                expectedSequence,
                nextPrivacy,
                (clone, cloneTransaction) =>
                {
                    var cloneEntry = ApplyNewDecision(
                        clone,
                        cloneTransaction,
                        command,
                        nowUtc,
                        payload,
                        commandHash,
                        idempotencyKey,
                        decisionId,
                        aliasId,
                        advancePrivacyFloor: isPrivacyDecision);
                    if (cloneEntry.Sequence != expectedSequence)
                    {
                        throw SqliteKnowledgeInfrastructure.Corrupt(
                            "The prepared privacy backup produced an unexpected decision sequence.");
                    }

                    if (isPrivacyDecision)
                    {
                        MarkBackupsBelowPrivacyFloor(clone, cloneTransaction, nextPrivacy, nowUtc);
                    }
                },
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            var entry = ApplyNewDecision(
                source,
                transaction,
                command,
                nowUtc,
                payload,
                commandHash,
                idempotencyKey,
                decisionId,
                aliasId,
                advancePrivacyFloor: isPrivacyDecision);
            InsertCommittedBackup(source, transaction, prepared, nowUtc);
            if (isPrivacyDecision)
            {
                MarkBackupsBelowPrivacyFloor(source, transaction, nextPrivacy, nowUtc, prepared.BackupId);
            }
            else
            {
                retiredPaths.AddRange(MarkExcessOrdinaryBackups(source, transaction, nowUtc));
            }

            transaction.Commit();
            DeleteBackupArtifacts(retiredPaths);
            return entry;
        }
        catch
        {
            if (prepared is not null)
            {
                DeletePreparedBackup(prepared);
            }

            throw;
        }
    }

    private async Task<int> ClearWithPreparedBackupAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureInitialized();
            var lifecycleLock = new SqliteKnowledgeLifecycleLock(_lifecycleLockPath, _timeProvider);
            await using var lease = await lifecycleLock.AcquireAsync(
                    SqliteKnowledgeInfrastructure.LifecycleTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
            return await Task.Run(
                    () => ClearWithPreparedBackupCore(nowUtc, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private int ClearWithPreparedBackupCore(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_backupDirectory);
        using var source = OpenConnection();
        using var transaction = source.BeginTransaction(deferred: false);
        var count = Convert.ToInt32(
            ExecuteScalar(source, transaction, "SELECT COUNT(*) FROM graph_native_decisions;"),
            CultureInfo.InvariantCulture);
        var (currentPrivacy, _, storeGeneration) = ReadPrivacyState(source, transaction);
        var nextPrivacy = checked(currentPrivacy + 1);
        var nextGeneration = checked(storeGeneration + 1);
        var retiredPaths = ReadBackupPathsBelowPrivacyFloor(source, transaction, nextPrivacy);
        PreparedDecisionBackup? prepared = null;
        try
        {
            EnsureBackupCapacity(source, transaction);
            prepared = PrepareDecisionBackup(
                "privacy-clear",
                "privacy",
                pinned: true,
                nextGeneration,
                maximumDecisionSequence: 0,
                nextPrivacy,
                (clone, cloneTransaction) =>
                {
                    ApplyClear(clone, cloneTransaction, nextPrivacy, nextGeneration, nowUtc);
                    MarkBackupsBelowPrivacyFloor(clone, cloneTransaction, nextPrivacy, nowUtc);
                },
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            ApplyClear(source, transaction, nextPrivacy, nextGeneration, nowUtc);
            InsertCommittedBackup(source, transaction, prepared, nowUtc);
            MarkBackupsBelowPrivacyFloor(source, transaction, nextPrivacy, nowUtc, prepared.BackupId);
            transaction.Commit();
            DeleteBackupArtifacts(retiredPaths);
            return count;
        }
        catch
        {
            if (prepared is not null)
            {
                DeletePreparedBackup(prepared);
            }

            throw;
        }
    }

    private static void ApplyClear(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long privacySequence,
        long storeGeneration,
        DateTimeOffset nowUtc)
    {
        ExecuteNonQuery(connection, transaction, "DELETE FROM graph_entity_aliases;");
        ExecuteNonQuery(connection, transaction, "DELETE FROM graph_manual_entities;");
        ExecuteNonQuery(connection, transaction, "DELETE FROM graph_native_decisions;");
        ExecuteNonQuery(connection, transaction, "DELETE FROM sqlite_sequence WHERE name = 'graph_native_decisions';");
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE decision_recovery_state
            SET current_decision_sequence = 0,
                current_privacy_sequence = $privacy,
                minimum_restorable_privacy_sequence = $privacy,
                active_store_generation = $generation,
                decision_checkpoint_hash = $hash,
                updated_utc_ticks = $now
            WHERE singleton_id = 1;
            """,
            ("$privacy", privacySequence),
            ("$generation", storeGeneration),
            ("$hash", EmptyHash),
            ("$now", nowUtc.UtcTicks));
    }

    private PreparedDecisionBackup PrepareDecisionBackup(
        string reason,
        string backupClass,
        bool pinned,
        long storeGeneration,
        long maximumDecisionSequence,
        long privacySequence,
        Action<SqliteConnection, SqliteTransaction> mutateClone,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var backupId = $"decision-backup-{Guid.NewGuid():N}";
        var finalName = $"{backupId}.db";
        var stagingPath = Path.Combine(_backupDirectory, finalName + ".staging");
        var finalPath = Path.Combine(_backupDirectory, finalName);
        var manifestPath = finalPath + ".manifest.json";
        try
        {
            using (var backupSource = OpenUnpooledConnection(_databasePath, readOnly: true))
            using (var destination = OpenUnpooledConnection(stagingPath, readOnly: false, configureJournal: false))
            {
                backupSource.BackupDatabase(destination);
            }

            using (var clone = OpenUnpooledConnection(stagingPath, readOnly: false))
            {
                using (var cloneTransaction = clone.BeginTransaction(deferred: false))
                {
                    mutateClone(clone, cloneTransaction);
                    cloneTransaction.Commit();
                }

                ValidateDecisionState(clone);
                ExecuteNonQuery(clone, transaction: null, "PRAGMA wal_checkpoint(TRUNCATE);");
                ExecuteScalar(clone, transaction: null, "PRAGMA journal_mode = DELETE;");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var bytes = new FileInfo(stagingPath).Length;
            string checksum;
            using (var stream = File.OpenRead(stagingPath))
            {
                checksum = Convert.ToHexString(SHA256.HashData(stream));
            }

            File.Move(stagingPath, finalPath);
            var committedAt = _timeProvider.GetUtcNow();
            WriteCommittedManifest(
                manifestPath,
                new BackupManifest(
                    backupId,
                    "Committed",
                    finalName,
                    checksum,
                    bytes,
                    SqliteKnowledgeDecisionSchema.Version,
                    storeGeneration,
                    maximumDecisionSequence,
                    privacySequence,
                    pinned,
                    committedAt));
            return new PreparedDecisionBackup(
                backupId,
                finalName,
                finalPath,
                manifestPath,
                checksum,
                bytes,
                storeGeneration,
                maximumDecisionSequence,
                privacySequence,
                reason,
                backupClass,
                pinned,
                committedAt);
        }
        catch
        {
            TryDelete(stagingPath);
            TryDelete(stagingPath + "-wal");
            TryDelete(stagingPath + "-shm");
            TryDelete(finalPath);
            TryDelete(manifestPath);
            TryDelete(manifestPath + ".staging");
            throw;
        }
    }

    private static SqliteConnection OpenUnpooledConnection(
        string path,
        bool readOnly,
        bool configureJournal = true)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();
        ExecuteNonQuery(connection, transaction: null, "PRAGMA foreign_keys = ON;");
        ExecuteNonQuery(connection, transaction: null, $"PRAGMA busy_timeout = {SqliteKnowledgeInfrastructure.BusyTimeoutMilliseconds};");
        if (!readOnly && configureJournal)
        {
            ExecuteScalar(connection, transaction: null, "PRAGMA journal_mode = WAL;");
            ExecuteNonQuery(connection, transaction: null, "PRAGMA synchronous = FULL;");
        }

        return connection;
    }

    private static (long PrivacySequence, long PrivacyFloor, long StoreGeneration) ReadPrivacyState(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT current_privacy_sequence, minimum_restorable_privacy_sequence, active_store_generation FROM decision_recovery_state WHERE singleton_id = 1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw SqliteKnowledgeInfrastructure.Corrupt("The decision recovery checkpoint is missing.");
        }

        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static void ValidateEnabledSettingsFence(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long expectedRevision)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT settings_version, settings_json, settings_fingerprint FROM graph_settings WHERE singleton_id = 1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("Knowledge Graph is disabled; no graph-native decision was appended.");
        }

        var revision = reader.GetInt64(0);
        var json = reader.GetString(1);
        var fingerprint = reader.GetString(2);
        var settings = JsonSerializer.Deserialize<GraphControlSettings>(json, JsonOptions)
            ?? throw SqliteKnowledgeInfrastructure.Corrupt("The graph control settings payload is malformed.");
        if (!string.Equals(fingerprint, Hash(json), StringComparison.Ordinal) || settings.Revision != revision)
        {
            throw SqliteKnowledgeInfrastructure.Corrupt("The graph control settings fence is malformed.");
        }

        if (!settings.IsEnabled || !settings.ConsentConfirmed || revision != expectedRevision)
        {
            throw new InvalidOperationException("Knowledge Graph control settings changed or were disabled before the decision append.");
        }
    }

    private static IReadOnlyList<string> ReadBackupPathsBelowPrivacyFloor(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long privacyFloor)
    {
        var paths = new List<string>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT relative_path FROM decision_backup_catalog WHERE state NOT IN ('Superseded', 'Abandoned') AND privacy_sequence < $floor;";
        command.Parameters.AddWithValue("$floor", privacyFloor);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            paths.Add(Path.GetFileName(reader.GetString(0)));
        }

        return paths;
    }

    private static void MarkBackupsBelowPrivacyFloor(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long privacyFloor,
        DateTimeOffset nowUtc,
        string? exceptBackupId = null)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE decision_backup_catalog
            SET state = 'Superseded', superseded_utc_ticks = $now
            WHERE state NOT IN ('Superseded', 'Abandoned')
              AND privacy_sequence < $floor
              AND ($except IS NULL OR backup_id <> $except);
            """,
            ("$now", nowUtc.UtcTicks),
            ("$floor", privacyFloor),
            ("$except", exceptBackupId));
    }

    private static void InsertCommittedBackup(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PreparedDecisionBackup backup,
        DateTimeOffset nowUtc)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO decision_backup_catalog(
                backup_id, store_generation, reason, backup_class, state, relative_path,
                sha256, byte_length, schema_version, minimum_decision_sequence,
                maximum_decision_sequence, privacy_sequence, created_utc_ticks,
                verified_utc_ticks, committed_utc_ticks, is_pinned)
            VALUES ($id, $generation, $reason, $class, 'Committed', $path,
                    $sha, $bytes, $schema, 0, $maximum, $privacy, $created,
                    $created, $created, $pinned);
            """,
            ("$id", backup.BackupId),
            ("$generation", backup.StoreGeneration),
            ("$reason", Bound(backup.Reason, 128)),
            ("$class", backup.BackupClass),
            ("$path", backup.RelativePath),
            ("$sha", backup.Sha256),
            ("$bytes", backup.ByteLength),
            ("$schema", SqliteKnowledgeDecisionSchema.Version),
            ("$maximum", backup.MaximumDecisionSequence),
            ("$privacy", backup.PrivacySequence),
            ("$created", nowUtc.UtcTicks),
            ("$pinned", backup.IsPinned ? 1 : 0));
    }

    private static IReadOnlyList<string> MarkExcessOrdinaryBackups(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset nowUtc)
    {
        var stale = new List<(string Id, string RelativePath)>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT backup_id, relative_path
                FROM decision_backup_catalog
                WHERE state = 'Committed' AND is_pinned = 0 AND backup_class = 'ordinary'
                ORDER BY committed_utc_ticks DESC, backup_id DESC
                LIMIT -1 OFFSET $retain;
                """;
            command.Parameters.AddWithValue("$retain", MaximumOrdinaryBackups);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                stale.Add((reader.GetString(0), Path.GetFileName(reader.GetString(1))));
            }
        }

        foreach (var item in stale)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                "UPDATE decision_backup_catalog SET state = 'Superseded', superseded_utc_ticks = $now WHERE backup_id = $id AND state = 'Committed';",
                ("$now", nowUtc.UtcTicks), ("$id", item.Id));
        }

        return stale.Select(item => item.RelativePath).ToArray();
    }

    private void EnsureBackupCapacity(SqliteConnection decision, SqliteTransaction transaction)
    {
        const long requiredReserve = 32L * 1024L * 1024L;
        const long backupGrowthAllowance = 1L * 1024L * 1024L;
        var decisionBytes = DatabaseFamilyLength(_databasePath);
        var prospectiveBackupBytes = checked(Math.Max(decisionBytes, backupGrowthAllowance));
        var directory = Path.GetDirectoryName(_databasePath)!;
        var graphPath = Path.Combine(directory, "knowledge-graph.db");
        var graphBytes = DatabaseFamilyLength(graphPath);
        var backupBytes = 0L;
        if (Directory.Exists(_backupDirectory))
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(_backupDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    backupBytes = checked(backupBytes + FileLength(path));
                }
            }
            catch (OverflowException exception)
            {
                throw new SqliteKnowledgeStoreException(
                    SqliteKnowledgeFailureKind.Full,
                    "Knowledge backup storage exceeds the supported accounting range.",
                    exception);
            }
        }

        var maximum = SqliteGraphStore.DefaultMaximumDatabaseBytes;
        if (File.Exists(graphPath))
        {
            try
            {
                using var graph = OpenUnpooledConnection(graphPath, readOnly: true);
                var raw = ExecuteScalar(
                    graph,
                    transaction: null,
                    "SELECT COALESCE((SELECT value FROM graph_meta WHERE key = 'maximum_total_storage_bytes'), (SELECT value FROM graph_meta WHERE key = 'maximum_database_bytes'), $default);",
                    ("$default", SqliteGraphStore.DefaultMaximumDatabaseBytes.ToString(CultureInfo.InvariantCulture)));
                maximum = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (exception is SqliteException or FormatException or OverflowException)
            {
                throw new SqliteKnowledgeStoreException(
                    SqliteKnowledgeFailureKind.Corrupt,
                    "The combined knowledge-storage quota could not be validated before backup.",
                    exception);
            }
        }

        long projectedTotal;
        try
        {
            projectedTotal = checked(graphBytes + decisionBytes + backupBytes + prospectiveBackupBytes);
        }
        catch (OverflowException exception)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.Full,
                "The combined knowledge-storage projection exceeds the supported accounting range.",
                exception);
        }

        if (maximum is < GraphLimits.MinimumStorageQuotaBytes or > GraphLimits.MaximumStorageQuotaBytes ||
            projectedTotal > maximum)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.Full,
                "The configured combined knowledge-storage quota cannot retain another verified decision backup.");
        }

        var root = Path.GetPathRoot(directory);
        if (!string.IsNullOrWhiteSpace(root))
        {
            try
            {
                if (new DriveInfo(root).AvailableFreeSpace < checked(prospectiveBackupBytes + requiredReserve))
                {
                    throw new SqliteKnowledgeStoreException(
                        SqliteKnowledgeFailureKind.Full,
                        "The storage volume cannot retain a decision backup and the required recovery reserve.");
                }
            }
            catch (IOException exception)
            {
                throw new SqliteKnowledgeStoreException(
                    SqliteKnowledgeFailureKind.InputOutput,
                    "Available storage could not be verified before decision backup.",
                    exception);
            }
        }

        _ = ExecuteScalar(decision, transaction, "SELECT current_decision_sequence FROM decision_recovery_state WHERE singleton_id = 1;")
            ?? throw SqliteKnowledgeInfrastructure.Corrupt("The decision recovery checkpoint is missing during quota validation.");
    }

    private static long DatabaseFamilyLength(string path) =>
        checked(FileLength(path) + FileLength(path + "-wal") + FileLength(path + "-shm"));

    private static long FileLength(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    private void DeleteBackupArtifacts(IEnumerable<string> relativePaths)
    {
        foreach (var relativePath in relativePaths.Distinct(StringComparer.Ordinal))
        {
            var safeName = Path.GetFileName(relativePath);
            var finalPath = Path.Combine(_backupDirectory, safeName);
            TryDelete(finalPath);
            TryDelete(finalPath + ".manifest.json");
            TryDelete(finalPath + ".staging");
            TryDelete(finalPath + "-wal");
            TryDelete(finalPath + "-shm");
        }
    }

    private static void DeletePreparedBackup(PreparedDecisionBackup backup)
    {
        TryDelete(backup.FinalPath);
        TryDelete(backup.FinalPath + "-wal");
        TryDelete(backup.FinalPath + "-shm");
        TryDelete(backup.ManifestPath);
        TryDelete(backup.ManifestPath + ".staging");
    }

    private IReadOnlyList<GraphDecisionRecoveryPoint> ReadRecoveryPoints(CancellationToken cancellationToken)
    {
        var candidates = ReadManagedRecoveryCandidates(cancellationToken);
        var floor = ReadAuthoritativeRecoveryFloor(candidates);
        return candidates
            .OrderByDescending(item => item.Manifest?.CommittedAtUtc ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.RecoveryPointId, StringComparer.Ordinal)
            .Take(64)
            .Select(item => new GraphDecisionRecoveryPoint(
                item.RecoveryPointId,
                item.Manifest?.MaximumDecisionSequence ?? 0,
                item.Manifest?.PrivacySequence ?? 0,
                item.Manifest?.StoreGeneration ?? 0,
                item.Manifest?.CommittedAtUtc ?? DateTimeOffset.UnixEpoch,
                item.Manifest?.IsPinned ?? false,
                string.Equals(item.StatusCode, "verified", StringComparison.Ordinal) &&
                item.Manifest!.PrivacySequence >= floor,
                string.Equals(item.StatusCode, "verified", StringComparison.Ordinal) && item.Manifest!.PrivacySequence < floor
                    ? "privacy-floor-stale"
                    : item.StatusCode))
            .ToArray();
    }

    private GraphOperationResult RestoreRecoveryPoint(
        string recoveryPointId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        RecoverInterruptedDecisionRestore(cancellationToken);
        var candidates = ReadManagedRecoveryCandidates(cancellationToken);
        var selected = candidates.SingleOrDefault(item =>
            string.Equals(item.RecoveryPointId, recoveryPointId, StringComparison.Ordinal));
        if (selected is null)
        {
            return new GraphOperationResult(false, "The selected managed graph-decision recovery point does not exist.", 0);
        }

        if (string.Equals(selected.StatusCode, "unsupported-schema", StringComparison.Ordinal))
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.UnsupportedSchema,
                "The selected recovery point was created by a newer OpenSorSe decision schema.");
        }

        if (!string.Equals(selected.StatusCode, "verified", StringComparison.Ordinal) || selected.Manifest is null)
        {
            throw SqliteKnowledgeInfrastructure.Corrupt(
                "The selected managed graph-decision recovery point failed manifest, checksum, or SQLite integrity validation.");
        }

        var privacyFloor = ReadAuthoritativeRecoveryFloor(candidates);
        if (selected.Manifest.PrivacySequence < privacyFloor)
        {
            return new GraphOperationResult(
                false,
                "The selected recovery point predates the durable privacy floor and cannot be restored.",
                0);
        }

        var stagingPath = string.Concat(_databasePath, ".restore.staging.", Guid.NewGuid().ToString("N"));
        var journal = new DecisionRestoreJournal(
            selected.RecoveryPointId,
            Path.GetFileName(stagingPath),
            Path.GetFileName(_restorePreviousPath),
            selected.Manifest.Sha256,
            "Prepared",
            nowUtc);
        try
        {
            CopyFileDurably(selected.BackupPath, stagingPath, cancellationToken);
            ValidateRestoreDatabase(stagingPath, selected.Manifest);
            WriteRestoreJournal(journal);
            journal = journal with { State = "Promoting" };
            WriteRestoreJournal(journal);
            PromoteRestore(journal, cancellationToken);

            using (var restored = OpenUnpooledConnection(_databasePath, readOnly: false))
            {
                SqliteKnowledgeInfrastructure.Validate(
                    restored,
                    SqliteKnowledgeDecisionSchema.ApplicationId,
                    SqliteKnowledgeDecisionSchema.Version,
                    SqliteKnowledgeDecisionSchema.RequiredTables,
                    "decision_meta",
                    "decision_migration_history",
                    SqliteKnowledgeDecisionSchema.CreateVersionOne,
                    SqliteKnowledgeDecisionSchema.RequiredColumns,
                    SqliteKnowledgeDecisionSchema.RequiredIndexes);
                EnsureLegacyMirrorExtension(restored);
                using var transaction = restored.BeginTransaction(deferred: false);
                ExecuteNonQuery(
                    restored,
                    transaction,
                    "UPDATE decision_recovery_state SET minimum_restorable_privacy_sequence = MAX(minimum_restorable_privacy_sequence, $floor), updated_utc_ticks = $now WHERE singleton_id = 1;",
                    ("$floor", privacyFloor), ("$now", nowUtc.UtcTicks));
                ExecuteNonQuery(
                    restored,
                    transaction,
                    "UPDATE decision_backup_catalog SET state = 'Superseded', superseded_utc_ticks = $now WHERE state = 'Committed';",
                    ("$now", nowUtc.UtcTicks));
                foreach (var candidate in candidates.Where(item =>
                             string.Equals(item.StatusCode, "verified", StringComparison.Ordinal) &&
                             item.Manifest is not null && item.Manifest.PrivacySequence >= privacyFloor))
                {
                    UpsertRecoveredBackupCatalog(restored, transaction, candidate, nowUtc);
                }

                transaction.Commit();
                ValidateDecisionState(restored);
                RecoverInterruptedBackups(restored);
                RetireBackupsBelowPrivacyFloor(restored);
            }

            _initialized = true;
            TryDelete(_restorePreviousPath);
            TryDelete(_restorePreviousPath + "-wal");
            TryDelete(_restorePreviousPath + "-shm");
            TryDelete(_restoreJournalPath);
            var decisionCount = selected.Manifest.MaximumDecisionSequence > int.MaxValue
                ? int.MaxValue
                : (int)selected.Manifest.MaximumDecisionSequence;
            return new GraphOperationResult(
                true,
                "The verified graph-decision recovery point was restored. Original files were not changed.",
                decisionCount);
        }
        catch
        {
            if (!File.Exists(_restoreJournalPath))
            {
                TryDelete(stagingPath);
            }

            throw;
        }
    }

    private IReadOnlyList<ManagedRecoveryCandidate> ReadManagedRecoveryCandidates(
        CancellationToken cancellationToken)
    {
        var candidates = new List<ManagedRecoveryCandidate>();
        if (!Directory.Exists(_backupDirectory))
        {
            return candidates;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(
                     _backupDirectory,
                     "decision-backup-*.db.manifest.json",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(manifestPath);
            const string suffix = ".db.manifest.json";
            var recoveryPointId = fileName.EndsWith(suffix, StringComparison.Ordinal)
                ? fileName[..^suffix.Length]
                : fileName;
            BackupManifest? manifest = null;
            var status = "corrupt";
            var backupPath = Path.Combine(_backupDirectory, string.Concat(recoveryPointId, ".db"));
            try
            {
                var payload = File.ReadAllBytes(manifestPath);
                if (payload.Length > 64 * 1024)
                {
                    throw new InvalidDataException("Recovery manifest exceeds its bounded size.");
                }

                manifest = JsonSerializer.Deserialize<BackupManifest>(payload, JsonOptions)
                    ?? throw new InvalidDataException("Recovery manifest is empty.");
                if (!string.Equals(manifest.BackupId, recoveryPointId, StringComparison.Ordinal) ||
                    !string.Equals(manifest.State, "Committed", StringComparison.Ordinal) ||
                    !string.Equals(manifest.RelativePath, string.Concat(recoveryPointId, ".db"), StringComparison.Ordinal) ||
                    !string.Equals(Path.GetFileName(manifest.RelativePath), manifest.RelativePath, StringComparison.Ordinal) ||
                    manifest.ByteLength < 0 || manifest.MaximumDecisionSequence < 0 ||
                    manifest.PrivacySequence < 0 || manifest.StoreGeneration <= 0 ||
                    manifest.CommittedAtUtc == default || manifest.Sha256.Length != 64 ||
                    manifest.Sha256.Any(character => !Uri.IsHexDigit(character)))
                {
                    throw new InvalidDataException("Recovery manifest fields are invalid.");
                }

                if (manifest.SchemaVersion > SqliteKnowledgeDecisionSchema.Version)
                {
                    status = "unsupported-schema";
                }
                else if (manifest.SchemaVersion != SqliteKnowledgeDecisionSchema.Version ||
                         !File.Exists(backupPath) || new FileInfo(backupPath).Length != manifest.ByteLength ||
                         !string.Equals(HashFile(backupPath, cancellationToken), manifest.Sha256, StringComparison.Ordinal))
                {
                    status = "corrupt";
                }
                else
                {
                    ValidateRestoreDatabase(backupPath, manifest);
                    status = "verified";
                }
            }
            catch (SqliteKnowledgeStoreException exception) when (
                exception.Kind == SqliteKnowledgeFailureKind.UnsupportedSchema)
            {
                status = "unsupported-schema";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                               JsonException or InvalidDataException or SqliteException or
                                               SqliteKnowledgeStoreException)
            {
                status = "corrupt";
            }

            candidates.Add(new ManagedRecoveryCandidate(
                recoveryPointId,
                backupPath,
                manifestPath,
                manifest,
                status));
        }

        return candidates;
    }

    private long ReadAuthoritativeRecoveryFloor(IReadOnlyList<ManagedRecoveryCandidate> candidates)
    {
        var floor = candidates
            .Where(item => item.Manifest is not null)
            .Select(item => item.Manifest!.PrivacySequence)
            .DefaultIfEmpty(0)
            .Max();
        if (!File.Exists(_databasePath))
        {
            return floor;
        }

        try
        {
            using var primary = OpenUnpooledConnection(_databasePath, readOnly: true);
            var version = Convert.ToInt32(ExecuteScalar(primary, transaction: null, "PRAGMA user_version;") ?? 0, CultureInfo.InvariantCulture);
            if (version > SqliteKnowledgeDecisionSchema.Version)
            {
                throw new SqliteKnowledgeStoreException(
                    SqliteKnowledgeFailureKind.UnsupportedSchema,
                    $"Decision schema {version} is newer than supported schema {SqliteKnowledgeDecisionSchema.Version}.");
            }

            var retained = Convert.ToInt64(
                ExecuteScalar(
                    primary,
                    transaction: null,
                    "SELECT minimum_restorable_privacy_sequence FROM decision_recovery_state WHERE singleton_id = 1;") ?? 0,
                CultureInfo.InvariantCulture);
            floor = Math.Max(floor, retained);
        }
        catch (SqliteKnowledgeStoreException exception) when (exception.Kind != SqliteKnowledgeFailureKind.UnsupportedSchema)
        {
            // A corrupt primary cannot lower the independently retained managed-backup privacy floor.
        }
        catch (SqliteException)
        {
            // A corrupt primary cannot lower the independently retained managed-backup privacy floor.
        }

        return floor;
    }

    private void ValidateRestoreDatabase(string path, BackupManifest manifest)
    {
        using var backup = OpenUnpooledConnection(path, readOnly: true);
        SqliteKnowledgeInfrastructure.Validate(
            backup,
            SqliteKnowledgeDecisionSchema.ApplicationId,
            SqliteKnowledgeDecisionSchema.Version,
            SqliteKnowledgeDecisionSchema.RequiredTables,
            "decision_meta",
            "decision_migration_history",
            SqliteKnowledgeDecisionSchema.CreateVersionOne,
            SqliteKnowledgeDecisionSchema.RequiredColumns,
            SqliteKnowledgeDecisionSchema.RequiredIndexes);
        ValidateDecisionState(backup);
        using var checkpoint = backup.CreateCommand();
        checkpoint.CommandText =
            "SELECT current_decision_sequence, current_privacy_sequence, active_store_generation FROM decision_recovery_state WHERE singleton_id = 1;";
        using var reader = checkpoint.ExecuteReader();
        if (!reader.Read() || reader.GetInt64(0) != manifest.MaximumDecisionSequence ||
            reader.GetInt64(1) != manifest.PrivacySequence ||
            reader.GetInt64(2) != manifest.StoreGeneration)
        {
            throw SqliteKnowledgeInfrastructure.Corrupt(
                "The managed recovery manifest disagrees with its decision checkpoint.");
        }
    }

    private void PromoteRestore(DecisionRestoreJournal journal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stagingPath = ResolveRestoreArtifact(journal.StagingFileName);
        if (!File.Exists(stagingPath) ||
            !string.Equals(HashFile(stagingPath, cancellationToken), journal.Sha256, StringComparison.Ordinal))
        {
            throw SqliteKnowledgeInfrastructure.Corrupt("The prepared decision restore artifact failed checksum validation.");
        }

        TryDelete(_databasePath + "-wal");
        TryDelete(_databasePath + "-shm");
        TryDelete(_restorePreviousPath);
        if (File.Exists(_databasePath))
        {
            File.Replace(stagingPath, _databasePath, _restorePreviousPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(stagingPath, _databasePath);
        }

        if (!string.Equals(HashFile(_databasePath, cancellationToken), journal.Sha256, StringComparison.Ordinal))
        {
            RollBackRestorePromotion();
            throw SqliteKnowledgeInfrastructure.Corrupt("The promoted decision recovery point failed checksum validation.");
        }
    }

    private void RecoverInterruptedDecisionRestore(CancellationToken cancellationToken)
    {
        if (!File.Exists(_restoreJournalPath))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        DecisionRestoreJournal journal;
        try
        {
            var payload = File.ReadAllBytes(_restoreJournalPath);
            if (payload.Length > 16 * 1024)
            {
                throw new InvalidDataException("Restore journal exceeds its bounded size.");
            }

            journal = JsonSerializer.Deserialize<DecisionRestoreJournal>(payload, JsonOptions)
                ?? throw new InvalidDataException("Restore journal is empty.");
            _ = ResolveRestoreArtifact(journal.StagingFileName);
            if (!string.Equals(journal.PreviousFileName, Path.GetFileName(_restorePreviousPath), StringComparison.Ordinal) ||
                journal.Sha256.Length != 64 || journal.Sha256.Any(character => !Uri.IsHexDigit(character)) ||
                journal.State is not ("Prepared" or "Promoting"))
            {
                throw new InvalidDataException("Restore journal fields are invalid.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.Corrupt,
                "The interrupted decision restore journal is malformed and requires review.",
                exception);
        }

        var stagingPath = ResolveRestoreArtifact(journal.StagingFileName);
        if (string.Equals(journal.State, "Prepared", StringComparison.Ordinal))
        {
            TryDelete(stagingPath);
            TryDelete(_restoreJournalPath);
            return;
        }

        if (File.Exists(_databasePath) &&
            string.Equals(HashFile(_databasePath, cancellationToken), journal.Sha256, StringComparison.Ordinal))
        {
            TryDelete(stagingPath);
            TryDelete(_restorePreviousPath);
            TryDelete(_restoreJournalPath);
            return;
        }

        if (File.Exists(stagingPath) &&
            string.Equals(HashFile(stagingPath, cancellationToken), journal.Sha256, StringComparison.Ordinal))
        {
            PromoteRestore(journal, cancellationToken);
            TryDelete(_restorePreviousPath);
            TryDelete(_restoreJournalPath);
            return;
        }

        if (File.Exists(_restorePreviousPath))
        {
            RollBackRestorePromotion();
            TryDelete(_restoreJournalPath);
        }

        throw SqliteKnowledgeInfrastructure.Corrupt(
            "The interrupted decision restore could not verify either the selected recovery point or rollback artifact.");
    }

    private void RollBackRestorePromotion()
    {
        TryDelete(_databasePath + "-wal");
        TryDelete(_databasePath + "-shm");
        if (!File.Exists(_restorePreviousPath))
        {
            return;
        }

        if (File.Exists(_databasePath))
        {
            File.Replace(_restorePreviousPath, _databasePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(_restorePreviousPath, _databasePath);
        }
    }

    private string ResolveRestoreArtifact(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            !fileName.StartsWith(string.Concat(Path.GetFileName(_databasePath), ".restore.staging."), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Restore journal contains an invalid managed artifact name.");
        }

        return Path.Combine(Path.GetDirectoryName(_databasePath)!, fileName);
    }

    private void WriteRestoreJournal(DecisionRestoreJournal journal)
    {
        var stagingPath = string.Concat(_restoreJournalPath, ".staging");
        TryDelete(stagingPath);
        var payload = JsonSerializer.SerializeToUtf8Bytes(journal, JsonOptions);
        using (var stream = new FileStream(
                   stagingPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None,
                   4096,
                   FileOptions.WriteThrough))
        {
            stream.Write(payload);
            stream.Flush(flushToDisk: true);
        }

        File.Move(stagingPath, _restoreJournalPath, overwrite: true);
    }

    private static void CopyFileDurably(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough);
        CopyStreamWithCancellation(source, destination, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        destination.Flush(flushToDisk: true);
    }

    internal static void CopyStreamWithCancellation(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = GC.AllocateUninitializedArray<byte>(64 * 1024);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            destination.Write(buffer, 0, read);
        }
    }

    private static string HashFile(string path, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        return HashStreamWithCancellation(stream, cancellationToken);
    }

    internal static string HashStreamWithCancellation(Stream stream, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = GC.AllocateUninitializedArray<byte>(64 * 1024);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, read);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void UpsertRecoveredBackupCatalog(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ManagedRecoveryCandidate candidate,
        DateTimeOffset nowUtc)
    {
        var manifest = candidate.Manifest!;
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO decision_backup_catalog(
                backup_id, store_generation, reason, backup_class, state, relative_path,
                sha256, byte_length, schema_version, minimum_decision_sequence,
                maximum_decision_sequence, privacy_sequence, created_utc_ticks,
                verified_utc_ticks, committed_utc_ticks, is_pinned)
            VALUES ($id, $generation, 'recovery-inventory', $class, 'Committed', $path,
                    $sha, $bytes, $schema, 0, $maximum, $privacy, $created, $now, $committed, $pinned)
            ON CONFLICT(backup_id) DO UPDATE SET
                store_generation = excluded.store_generation,
                reason = excluded.reason,
                backup_class = excluded.backup_class,
                state = excluded.state,
                relative_path = excluded.relative_path,
                sha256 = excluded.sha256,
                byte_length = excluded.byte_length,
                schema_version = excluded.schema_version,
                maximum_decision_sequence = excluded.maximum_decision_sequence,
                privacy_sequence = excluded.privacy_sequence,
                verified_utc_ticks = excluded.verified_utc_ticks,
                committed_utc_ticks = excluded.committed_utc_ticks,
                superseded_utc_ticks = NULL,
                is_pinned = excluded.is_pinned;
            """,
            ("$id", manifest.BackupId), ("$generation", manifest.StoreGeneration),
            ("$class", manifest.IsPinned ? "privacy" : "ordinary"), ("$path", manifest.RelativePath),
            ("$sha", manifest.Sha256), ("$bytes", manifest.ByteLength), ("$schema", manifest.SchemaVersion),
            ("$maximum", manifest.MaximumDecisionSequence), ("$privacy", manifest.PrivacySequence),
            ("$created", manifest.CommittedAtUtc.UtcTicks), ("$now", nowUtc.UtcTicks),
            ("$committed", manifest.CommittedAtUtc.UtcTicks), ("$pinned", manifest.IsPinned ? 1 : 0));
    }

    private static void WriteCommittedManifest(string path, BackupManifest manifest)
    {
        var stagingPath = path + ".staging";
        var payload = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        using (var stream = new FileStream(
                   stagingPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None,
                   bufferSize: 4096,
                   FileOptions.WriteThrough))
        {
            stream.Write(payload);
            stream.Flush(flushToDisk: true);
        }

        File.Move(stagingPath, path);
    }

    private static (long Sequence, string Hash) ReadCheckpoint(
        SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT current_decision_sequence, decision_checkpoint_hash FROM decision_recovery_state WHERE singleton_id = 1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw SqliteKnowledgeInfrastructure.Corrupt("The decision recovery checkpoint is missing.");
        }

        return (reader.GetInt64(0), reader.GetString(1));
    }

    private void EnsureLegacyMirrorExtension(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        var hasMigrationTable = Convert.ToInt32(
            ExecuteScalar(
                connection,
                transaction,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'decision_extension_migrations';") ?? 0,
            CultureInfo.InvariantCulture) == 1;
        var extensionTables = new[]
        {
            "legacy_mirror_ingest_manifests",
            "legacy_mirror_ingest_pages",
            "legacy_mirror_ingest_rows",
        };

        if (!hasMigrationTable)
        {
            foreach (var table in extensionTables)
            {
                var exists = Convert.ToInt32(
                    ExecuteScalar(
                        connection,
                        transaction,
                        "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;",
                        ("$name", table)) ?? 0,
                    CultureInfo.InvariantCulture);
                if (exists != 0)
                {
                    throw SqliteKnowledgeInfrastructure.Corrupt(
                        "The legacy mirror staging schema exists without a completed migration marker.");
                }
            }

            var started = _timeProvider.GetUtcNow().UtcTicks;
            ExecuteNonQuery(connection, transaction, SqliteKnowledgeDecisionSchema.CreateLegacyMirrorExtension);
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO decision_extension_migrations(
                    extension_name, schema_version, migration_id, migration_checksum,
                    started_utc_ticks, completed_utc_ticks, application_version)
                VALUES ($name, $version, $id, $checksum, $started, $completed, $application);
                """,
                ("$name", SqliteKnowledgeDecisionSchema.LegacyMirrorExtensionName),
                ("$version", SqliteKnowledgeDecisionSchema.LegacyMirrorExtensionVersion),
                ("$id", "legacy-mirror-ingestion-v1"),
                ("$checksum", Hash(SqliteKnowledgeDecisionSchema.CreateLegacyMirrorExtension)),
                ("$started", started),
                ("$completed", _timeProvider.GetUtcNow().UtcTicks),
                ("$application", typeof(SqliteGraphDecisionStore).Assembly.GetName().Version?.ToString() ?? "unknown"));
        }

        ValidateLegacyMirrorExtension(connection, transaction);
        transaction.Commit();
    }

    private static void ValidateLegacyMirrorExtension(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using (var migration = connection.CreateCommand())
        {
            migration.Transaction = transaction;
            migration.CommandText =
                "SELECT schema_version, migration_id, migration_checksum, started_utc_ticks, completed_utc_ticks FROM decision_extension_migrations WHERE extension_name = $name;";
            migration.Parameters.AddWithValue("$name", SqliteKnowledgeDecisionSchema.LegacyMirrorExtensionName);
            using var reader = migration.ExecuteReader();
            if (!reader.Read())
            {
                throw SqliteKnowledgeInfrastructure.Corrupt("The legacy mirror extension migration is incomplete.");
            }

            var version = reader.GetInt32(0);
            if (version > SqliteKnowledgeDecisionSchema.LegacyMirrorExtensionVersion)
            {
                throw new SqliteKnowledgeStoreException(
                    SqliteKnowledgeFailureKind.UnsupportedSchema,
                    $"Legacy mirror schema {version} is newer than supported schema {SqliteKnowledgeDecisionSchema.LegacyMirrorExtensionVersion}.");
            }

            if (version != SqliteKnowledgeDecisionSchema.LegacyMirrorExtensionVersion ||
                !string.Equals(reader.GetString(1), "legacy-mirror-ingestion-v1", StringComparison.Ordinal) ||
                !string.Equals(reader.GetString(2), Hash(SqliteKnowledgeDecisionSchema.CreateLegacyMirrorExtension), StringComparison.Ordinal) ||
                reader.GetInt64(3) < 0 || reader.GetInt64(4) < reader.GetInt64(3) || reader.Read())
            {
                throw SqliteKnowledgeInfrastructure.Corrupt("The legacy mirror extension migration marker is invalid.");
            }
        }

        var requiredColumns = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["legacy_mirror_ingest_manifests"] =
                ["manifest_id", "expected_count", "staged_count", "next_page_sequence", "state", "canonical_aggregate_hash"],
            ["legacy_mirror_ingest_pages"] =
                ["manifest_id", "page_sequence", "canonical_page_hash", "row_count"],
            ["legacy_mirror_ingest_rows"] =
                ["manifest_id", "page_sequence", "stable_key", "legacy_kind", "legacy_key", "canonical_row_hash", "payload_json"],
        };
        foreach (var table in requiredColumns)
        {
            var tableName = SqliteKnowledgeInfrastructure.RequireSqlIdentifier(table.Key);
            var actual = new HashSet<string>(StringComparer.Ordinal);
            using var columns = connection.CreateCommand();
            columns.Transaction = transaction;
            columns.CommandText = $"PRAGMA table_info([{tableName}]);";
            using var reader = columns.ExecuteReader();
            while (reader.Read())
            {
                actual.Add(reader.GetString(1));
            }

            if (!table.Value.All(actual.Contains))
            {
                throw SqliteKnowledgeInfrastructure.Corrupt(
                    $"The legacy mirror extension table '{table.Key}' is missing required columns.");
            }
        }

        var indexPresent = Convert.ToInt32(
            ExecuteScalar(
                connection,
                transaction,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_legacy_mirror_ingest_rows_page';") ?? 0,
            CultureInfo.InvariantCulture) == 1;
        if (!indexPresent)
        {
            throw SqliteKnowledgeInfrastructure.Corrupt("The legacy mirror extension paging index is missing.");
        }
    }

    private void ValidateDecisionState(SqliteConnection connection)
    {
        long sequence;
        long privacy;
        long floor;
        long generation;
        string checkpoint;
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT current_decision_sequence, current_privacy_sequence, minimum_restorable_privacy_sequence, active_store_generation, decision_checkpoint_hash FROM decision_recovery_state WHERE singleton_id = 1;";
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                throw SqliteKnowledgeInfrastructure.Corrupt("The decision recovery checkpoint is missing.");
            }

            sequence = reader.GetInt64(0);
            privacy = reader.GetInt64(1);
            floor = reader.GetInt64(2);
            generation = reader.GetInt64(3);
            checkpoint = reader.GetString(4);
        }

        if (sequence < 0 || privacy < 0 || floor < 0 || floor > privacy || generation <= 0)
        {
            throw SqliteKnowledgeInfrastructure.Corrupt("The decision recovery state violates monotonic privacy or generation invariants.");
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT COUNT(*), COALESCE(MIN(decision_sequence), 0), COALESCE(MAX(decision_sequence), 0) FROM graph_native_decisions;";
            using var reader = command.ExecuteReader();
            reader.Read();
            var count = reader.GetInt64(0);
            var minimum = reader.GetInt64(1);
            var maximum = reader.GetInt64(2);
            if (count != sequence || maximum != sequence || (sequence == 0 ? minimum != 0 : minimum != 1))
            {
                throw SqliteKnowledgeInfrastructure.Corrupt("The graph-native decision ledger contains a sequence gap.");
            }
        }

        if (!string.Equals(checkpoint, CalculateLedgerHash(connection), StringComparison.Ordinal))
        {
            throw SqliteKnowledgeInfrastructure.Corrupt("The graph-native decision checkpoint does not match the append-only ledger.");
        }

        using var settings = connection.CreateCommand();
        settings.CommandText = "SELECT settings_version, settings_json, settings_fingerprint FROM graph_settings WHERE singleton_id = 1;";
        using var settingsReader = settings.ExecuteReader();
        if (settingsReader.Read())
        {
            var saved = JsonSerializer.Deserialize<GraphControlSettings>(settingsReader.GetString(1), JsonOptions)
                ?? throw SqliteKnowledgeInfrastructure.Corrupt("The graph control settings payload is malformed.");
            if (settingsReader.GetInt64(0) != saved.Revision ||
                !string.Equals(settingsReader.GetString(2), Hash(settingsReader.GetString(1)), StringComparison.Ordinal))
            {
                throw SqliteKnowledgeInfrastructure.Corrupt("The graph control settings fingerprint or revision is invalid.");
            }

            ValidateControlSettings(saved);
        }
    }

    private void RecoverInterruptedBackups(SqliteConnection connection)
    {
        Directory.CreateDirectory(_backupDirectory);
        var floor = Convert.ToInt64(
            SqliteKnowledgeInfrastructure.ExecuteScalar(
                connection,
                "SELECT minimum_restorable_privacy_sequence FROM decision_recovery_state WHERE singleton_id = 1;"),
            CultureInfo.InvariantCulture);
        var interrupted = new List<(string Id, string State, string RelativePath, string? Sha256, long Privacy)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT backup_id, state, relative_path, sha256, privacy_sequence FROM decision_backup_catalog WHERE state IN ('Staging', 'Verified') ORDER BY created_utc_ticks, backup_id;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                interrupted.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt64(4)));
            }
        }

        foreach (var item in interrupted)
        {
            var fileName = Path.GetFileName(item.RelativePath);
            var finalPath = Path.Combine(_backupDirectory, fileName);
            var stagingPath = finalPath + ".staging";
            var manifestPath = finalPath + ".manifest.json";
            var recovered = false;
            if (item.Privacy >= floor && item.Sha256 is not null && File.Exists(finalPath) && File.Exists(manifestPath))
            {
                try
                {
                    using var stream = File.OpenRead(finalPath);
                    var checksum = Convert.ToHexString(SHA256.HashData(stream));
                    using var backup = SqliteKnowledgeInfrastructure.OpenConnection(finalPath, readOnly: true);
                    SqliteKnowledgeInfrastructure.Validate(
                        backup,
                        SqliteKnowledgeDecisionSchema.ApplicationId,
                        SqliteKnowledgeDecisionSchema.Version,
                        SqliteKnowledgeDecisionSchema.RequiredTables,
                        "decision_meta",
                        "decision_migration_history",
                        SqliteKnowledgeDecisionSchema.CreateVersionOne,
                        SqliteKnowledgeDecisionSchema.RequiredColumns,
                        SqliteKnowledgeDecisionSchema.RequiredIndexes);
                    recovered = string.Equals(checksum, item.Sha256, StringComparison.Ordinal);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException or SqliteKnowledgeStoreException)
                {
                    recovered = false;
                }
            }

            if (recovered)
            {
                SqliteKnowledgeInfrastructure.ExecuteNonQuery(
                    connection,
                    "UPDATE decision_backup_catalog SET state = 'Committed', committed_utc_ticks = COALESCE(committed_utc_ticks, $now) WHERE backup_id = $id;",
                    ("$now", _timeProvider.GetUtcNow().UtcTicks), ("$id", item.Id));
            }
            else
            {
                TryDelete(stagingPath);
                TryDelete(finalPath);
                TryDelete(manifestPath);
                SqliteKnowledgeInfrastructure.ExecuteNonQuery(
                    connection,
                    "UPDATE decision_backup_catalog SET state = 'Abandoned', superseded_utc_ticks = $now WHERE backup_id = $id;",
                    ("$now", _timeProvider.GetUtcNow().UtcTicks), ("$id", item.Id));
            }
        }
    }

    private void RetireBackupsBelowPrivacyFloor(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        var floor = Convert.ToInt64(
            ExecuteScalar(
                connection,
                transaction,
                "SELECT minimum_restorable_privacy_sequence FROM decision_recovery_state WHERE singleton_id = 1;"),
            CultureInfo.InvariantCulture);
        var paths = ReadBackupPathsBelowPrivacyFloor(connection, transaction, floor);
        MarkBackupsBelowPrivacyFloor(connection, transaction, floor, _timeProvider.GetUtcNow());
        transaction.Commit();
        DeleteBackupArtifacts(paths);
    }

    private static string CalculateLedgerHash(SqliteConnection connection)
    {
        var hash = EmptyHash;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT decision_sequence, idempotency_key FROM graph_native_decisions ORDER BY decision_sequence;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var idempotency = reader.GetString(1);
            var separator = idempotency.IndexOf(':', StringComparison.Ordinal);
            var commandHash = separator >= 0 ? idempotency[(separator + 1)..] : idempotency;
            hash = Hash($"{hash}|{reader.GetInt64(0).ToString(CultureInfo.InvariantCulture)}|{commandHash}");
        }

        return hash;
    }

    private static void ValidateCommand(GraphDecisionCommand command)
    {
        if (command.ExpectedSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Expected sequence cannot be negative.");
        }

        if (command.ExpectedControlSettingsRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Expected control-settings revision cannot be negative.");
        }

        ValidateStableDecisionId(command.SubjectId, nameof(command.SubjectId));
        if (command.TargetId is not null)
        {
            ValidateStableDecisionId(command.TargetId, nameof(command.TargetId));
        }

        if (command.Label is not null)
        {
            ValidateBounded(command.Label, nameof(command.Label), GraphLimits.MaximumLabelCharacters);
        }

        if (command.Reason is { Length: > GraphLimits.MaximumDecisionReasonCharacters })
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Decision reason exceeds the bounded limit.");
        }

        var relationshipFieldCount =
            (command.RelationshipSourceNodeId is null ? 0 : 1) +
            (command.RelationshipTargetNodeId is null ? 0 : 1) +
            (command.RelationshipKind is null ? 0 : 1) +
            (command.RelationshipScope is null ? 0 : 1);
        if (relationshipFieldCount is > 0 and < 4 ||
            relationshipFieldCount > 0 && command.Kind is not (GraphDecisionKind.UnlinkNodes or GraphDecisionKind.NeverMerge))
        {
            throw new ArgumentException("Relationship decision provenance must be complete and limited to unlink decisions.", nameof(command));
        }

        if (relationshipFieldCount == 4)
        {
            ValidateStableDecisionId(command.RelationshipSourceNodeId!, nameof(command.RelationshipSourceNodeId));
            ValidateStableDecisionId(command.RelationshipTargetNodeId!, nameof(command.RelationshipTargetNodeId));
            if (string.Equals(command.RelationshipSourceNodeId, command.RelationshipTargetNodeId, StringComparison.Ordinal))
            {
                throw new ArgumentException("Relationship decision endpoints must be different.", nameof(command));
            }

            ValidateBounded(command.RelationshipScope!, nameof(command.RelationshipScope), 256);
            if (command.RelationshipScope!.Any(character => character == '\0' || char.IsControl(character)))
            {
                throw new ArgumentException("Relationship decision scope contains invalid characters.", nameof(command));
            }
        }

        if (command.Kind is GraphDecisionKind.CreateManualEntity or GraphDecisionKind.RenameManualEntity or
            GraphDecisionKind.AddAlias or GraphDecisionKind.RemoveAlias && string.IsNullOrWhiteSpace(command.Label))
        {
            throw new ArgumentException("This graph decision requires a bounded label.", nameof(command));
        }
    }

    private static void ValidateLegacyMirrorObservation(GraphLegacyDecisionObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ValidateStableDecisionId(observation.StableKey, nameof(observation));
        ValidateStableDecisionId(observation.DecisionNamespace, nameof(observation));
        ValidateStableDecisionId(observation.LegacyDecisionKey, nameof(observation));
        ValidateBounded(observation.ActionCode, nameof(observation), 128);
        if (ContainsInvalidDecisionText(observation.ActionCode) ||
            observation.Revision < 0 || observation.ObservedAtUtc.UtcTicks < 0 ||
            observation.CanonicalRowHash.Length != 64 ||
            observation.CanonicalRowHash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("A legacy decision mirror observation is malformed.", nameof(observation));
        }
    }

    private static void ValidateControlSettings(GraphControlSettings settings)
    {
        var expectedConcurrency = settings.ResourceMode switch
        {
            IndexingResourceMode.Eco => 1,
            IndexingResourceMode.Balanced => 2,
            IndexingResourceMode.Fast => 4,
            _ => 0,
        };
        if (!Enum.IsDefined(settings.ResourceMode) || settings.MaximumConcurrency != expectedConcurrency ||
            settings.MaximumConcurrency is < 1 or > GraphLimits.MaximumWorkerConcurrency || settings.Revision < 0 ||
            settings.PauseBelowBatteryPercentage is < 0 or > 100 ||
            settings.ProcessingWindowStartHour is < 0 or > 23 ||
            settings.ProcessingWindowEndHour is < 0 or > 23 ||
            settings.IsEnabled && !settings.ConsentConfirmed)
        {
            throw new ArgumentException("Graph control settings are invalid or exceed stable v2.0 limits.", nameof(settings));
        }
    }

    private static void ValidateBounded(string value, string name, int maximum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > maximum)
        {
            throw new ArgumentOutOfRangeException(name, $"The value exceeds the {maximum}-character limit.");
        }
    }

    private static void ValidateStableDecisionId(string value, string name)
    {
        ValidateBounded(value, name, GraphLimits.MaximumStableIdCharacters);
        if (Path.IsPathRooted(value) || value.Contains('/') || value.Contains('\\') ||
            ContainsInvalidDecisionText(value) ||
            value.Split(':', StringSplitOptions.RemoveEmptyEntries).Any(part => part is "." or ".."))
        {
            throw new ArgumentException("Graph decision identifiers must be stable opaque IDs, never file-system paths.", name);
        }
    }

    private static bool ContainsInvalidDecisionText(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\0' || char.IsControl(character) || char.IsLowSurrogate(character))
            {
                return true;
            }

            if (!char.IsHighSurrogate(character))
            {
                continue;
            }

            if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
            {
                return true;
            }

            index++;
        }

        return false;
    }

    private static string NormalizeAlias(string value) => value.Trim().Normalize().ToUpperInvariant();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string EmptyHash => "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";
    private static string Bound(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];

    private SqliteConnection OpenConnection()
    {
        var cancellationToken = _writerCancellationToken;
        var connection = SqliteKnowledgeInfrastructure.OpenConnection(
            _databasePath,
            cancellationToken: cancellationToken);
        if (cancellationToken.CanBeCanceled)
        {
            Interlocked.Exchange(ref _activeWriterConnection, connection);
            if (cancellationToken.IsCancellationRequested)
            {
                Interlocked.CompareExchange(ref _activeWriterConnection, null, connection);
                connection.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        return connection;
    }

    private Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken) =>
        RunAsyncCore(action, cancellationToken);

    private async Task<T> RunAsyncCore<T>(Func<T> action, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureInitialized();
            _writerCancellationToken = cancellationToken;
            using var cancellationRegistration = cancellationToken.Register(
                static state => ((SqliteGraphDecisionStore)state!).InterruptActiveWriter(),
                this);
            return await Task.Run(
                    () => SqliteKnowledgeInfrastructure.ExecuteWithBusyRetry(
                        action,
                        cancellationToken,
                        "The graph decision operation failed"),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _activeWriterConnection, null);
            _writerCancellationToken = default;
            _gate.Release();
        }
    }

    private void InterruptActiveWriter()
    {
        var connection = Volatile.Read(ref _activeWriterConnection);
        if (connection is not null)
        {
            SqliteKnowledgeInfrastructure.TryInterrupt(connection);
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException(
                "The Knowledge Graph decision store has not been initialized through the consent-gated storage lifecycle.");
        }
    }

    private static int ExecuteNonQuery(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params (string Name, object? Value)[] parameters) =>
        SqliteKnowledgeInfrastructure.ExecuteNonQuery(connection, transaction, sql, parameters);

    private static object? ExecuteScalar(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        SqliteKnowledgeInfrastructure.AddParameters(command, parameters);
        return command.ExecuteScalar();
    }

    private static void RequireAffected(int affected, string message)
    {
        if (affected != 1)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Retention cleanup is best effort; a future maintenance pass retries it.
        }
        catch (UnauthorizedAccessException)
        {
            // Retention cleanup is best effort; a future maintenance pass retries it.
        }
    }

    private sealed record BackupManifest(
        string BackupId,
        string State,
        string RelativePath,
        string Sha256,
        long ByteLength,
        int SchemaVersion,
        long StoreGeneration,
        long MaximumDecisionSequence,
        long PrivacySequence,
        bool IsPinned,
        DateTimeOffset CommittedAtUtc);

    private sealed record PreparedDecisionBackup(
        string BackupId,
        string RelativePath,
        string FinalPath,
        string ManifestPath,
        string Sha256,
        long ByteLength,
        long StoreGeneration,
        long MaximumDecisionSequence,
        long PrivacySequence,
        string Reason,
        string BackupClass,
        bool IsPinned,
        DateTimeOffset CommittedAtUtc);

    private sealed record ManagedRecoveryCandidate(
        string RecoveryPointId,
        string BackupPath,
        string ManifestPath,
        BackupManifest? Manifest,
        string StatusCode);

    private sealed record DecisionRestoreJournal(
        string RecoveryPointId,
        string StagingFileName,
        string PreviousFileName,
        string Sha256,
        string State,
        DateTimeOffset StartedAtUtc);
}
