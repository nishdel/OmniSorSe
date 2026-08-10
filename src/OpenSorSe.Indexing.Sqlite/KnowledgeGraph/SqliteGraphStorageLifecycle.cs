using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.KnowledgeGraph;

namespace OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

/// <summary>
/// Provisions and inventories the isolated SQLite graph stores behind one inter-process lifecycle fence.
/// Inspection of an unprovisioned location is non-mutating.
/// </summary>
public sealed class SqliteGraphStorageLifecycle : IGraphStorageLifecycle, IGraphDerivedStoreRecoveryProvider
{
    private const long RequiredReserveBytes = 32L * 1024L * 1024L;
    private const string GraphFileName = "knowledge-graph.db";
    private const string DecisionFileName = "knowledge-decisions.db";
    private const string StagedGraphFileName = ".knowledge-graph.bootstrap.db";
    private const string StagedDecisionFileName = ".knowledge-decisions.bootstrap.db";
    private const string BootstrapMarkerFileName = ".knowledge-data.bootstrap.json";
    private const string LifecycleLockFileName = ".knowledge-data.lifecycle.lock";
    private const string DerivedRecoveryJournalFileName = ".knowledge-graph.recovery.json";
    private const string StagedRecoveryGraphFileName = ".knowledge-graph.recovery.staging.db";
    private const string DerivedRecoveryConfirmation = GraphDerivedStoreRecoveryService.RecoveryConfirmation;
    private readonly string _directoryPath;
    private readonly string _graphDatabasePath;
    private readonly string _decisionDatabasePath;
    private readonly string _stagedGraphDatabasePath;
    private readonly string _stagedDecisionDatabasePath;
    private readonly string _bootstrapMarkerPath;
    private readonly string _lifecycleLockPath;
    private readonly string _derivedRecoveryJournalPath;
    private readonly string _stagedRecoveryGraphPath;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    /// <summary>Creates a composite lifecycle rooted in the application-owned index directory.</summary>
    public SqliteGraphStorageLifecycle(string directoryPath, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        _directoryPath = Path.GetFullPath(directoryPath);
        _graphDatabasePath = Path.Combine(_directoryPath, GraphFileName);
        _decisionDatabasePath = Path.Combine(_directoryPath, DecisionFileName);
        _stagedGraphDatabasePath = Path.Combine(_directoryPath, StagedGraphFileName);
        _stagedDecisionDatabasePath = Path.Combine(_directoryPath, StagedDecisionFileName);
        _bootstrapMarkerPath = Path.Combine(_directoryPath, BootstrapMarkerFileName);
        _lifecycleLockPath = Path.Combine(_directoryPath, LifecycleLockFileName);
        _derivedRecoveryJournalPath = Path.Combine(_directoryPath, DerivedRecoveryJournalFileName);
        _stagedRecoveryGraphPath = Path.Combine(_directoryPath, StagedRecoveryGraphFileName);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets the graph database path owned by this lifecycle.</summary>
    public string GraphDatabasePath => _graphDatabasePath;

    /// <summary>Gets the authoritative decision database path owned by this lifecycle.</summary>
    public string DecisionDatabasePath => _decisionDatabasePath;

    /// <inheritdoc />
    public async Task<GraphStorageProvisioningState> GetProvisioningStateAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasAnyProvisioningArtifact())
        {
            return GraphStorageProvisioningState.Unprovisioned;
        }

        var lifecycleLock = new SqliteKnowledgeLifecycleLock(_lifecycleLockPath, _timeProvider);
        await using var lease = await lifecycleLock
            .AcquireAsync(SqliteKnowledgeInfrastructure.LifecycleTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (!HasAnyProvisioningArtifact())
        {
            return GraphStorageProvisioningState.Unprovisioned;
        }

        if (File.Exists(_bootstrapMarkerPath) ||
            File.Exists(_stagedGraphDatabasePath) ||
            File.Exists(_stagedDecisionDatabasePath) ||
            File.Exists(_derivedRecoveryJournalPath) ||
            File.Exists(_stagedRecoveryGraphPath) ||
            File.Exists(_graphDatabasePath) != File.Exists(_decisionDatabasePath))
        {
            return GraphStorageProvisioningState.RepairRequired;
        }

        if (!File.Exists(_graphDatabasePath))
        {
            return GraphStorageProvisioningState.Unprovisioned;
        }

        return ValidateFinalStores()
            ? GraphStorageProvisioningState.Provisioned
            : GraphStorageProvisioningState.RepairRequired;
    }

    /// <inheritdoc />
    public async Task ProvisionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_directoryPath);
        var lifecycleLock = new SqliteKnowledgeLifecycleLock(_lifecycleLockPath, _timeProvider);
        await using var lease = await lifecycleLock
            .AcquireAsync(SqliteKnowledgeInfrastructure.LifecycleTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (File.Exists(_graphDatabasePath) && File.Exists(_decisionDatabasePath) &&
            !File.Exists(_bootstrapMarkerPath) &&
            !File.Exists(_stagedGraphDatabasePath) && !File.Exists(_stagedDecisionDatabasePath) &&
            !File.Exists(_derivedRecoveryJournalPath) && !File.Exists(_stagedRecoveryGraphPath))
        {
            if (!ValidateFinalStores())
            {
                throw SqliteKnowledgeInfrastructure.Corrupt("Existing knowledge sidecars require repair before they can be opened.");
            }

            return;
        }

        if (File.Exists(_graphDatabasePath) != File.Exists(_decisionDatabasePath) &&
            !File.Exists(_bootstrapMarkerPath))
        {
            throw SqliteKnowledgeInfrastructure.Corrupt("Only one knowledge sidecar exists; explicit repair is required.");
        }

        if (File.Exists(_derivedRecoveryJournalPath) || File.Exists(_stagedRecoveryGraphPath))
        {
            throw SqliteKnowledgeInfrastructure.Corrupt(
                "An explicit derived-graph recovery is incomplete and must be reviewed before provisioning can continue.");
        }

        if (!File.Exists(_bootstrapMarkerPath))
        {
            if (File.Exists(_stagedGraphDatabasePath) || File.Exists(_stagedDecisionDatabasePath))
            {
                throw SqliteKnowledgeInfrastructure.Corrupt("Uncatalogued knowledge bootstrap artifacts require explicit repair.");
            }

            EnsureProvisioningCapacity();
            WriteBootstrapMarker();
        }
        else
        {
            ValidateBootstrapMarker();
        }

        await InitializeStagedStoreAsync(
                _stagedGraphDatabasePath,
                SqliteKnowledgeGraphSchema.ApplicationId,
                SqliteKnowledgeGraphSchema.Version,
                SqliteKnowledgeGraphSchema.RequiredTables,
                SqliteKnowledgeGraphSchema.CreateVersionOne,
                "graph_meta",
                "graph_migration_history",
                SqliteKnowledgeGraphSchema.RequiredColumns,
                SqliteKnowledgeGraphSchema.RequiredIndexes,
                cancellationToken)
            .ConfigureAwait(false);
        await InitializeStagedStoreAsync(
                _stagedDecisionDatabasePath,
                SqliteKnowledgeDecisionSchema.ApplicationId,
                SqliteKnowledgeDecisionSchema.Version,
                SqliteKnowledgeDecisionSchema.RequiredTables,
                SqliteKnowledgeDecisionSchema.CreateVersionOne,
                "decision_meta",
                "decision_migration_history",
                SqliteKnowledgeDecisionSchema.RequiredColumns,
                SqliteKnowledgeDecisionSchema.RequiredIndexes,
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        PromoteIfMissing(_stagedGraphDatabasePath, _graphDatabasePath, ValidateGraphDatabase);
        cancellationToken.ThrowIfCancellationRequested();
        PromoteIfMissing(_stagedDecisionDatabasePath, _decisionDatabasePath, ValidateDecisionDatabase);
        if (!ValidateFinalStores())
        {
            throw SqliteKnowledgeInfrastructure.Corrupt("The provisioned knowledge sidecars failed final validation.");
        }

        DeleteOwnedBootstrapArtifact(_stagedGraphDatabasePath);
        DeleteOwnedBootstrapArtifact(_stagedDecisionDatabasePath);
        DeleteOwnedBootstrapArtifact(_stagedGraphDatabasePath + ".lifecycle.lock");
        DeleteOwnedBootstrapArtifact(_stagedDecisionDatabasePath + ".lifecycle.lock");
        DeleteOwnedBootstrapArtifact(_bootstrapMarkerPath);
    }

    /// <inheritdoc />
    public async Task<GraphStorageBreakdown> GetStorageBreakdownAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasAnyProvisioningArtifact())
        {
            return GraphStorageBreakdown.Empty;
        }

        var lifecycleLock = new SqliteKnowledgeLifecycleLock(_lifecycleLockPath, _timeProvider);
        await using var lease = await lifecycleLock
            .AcquireAsync(SqliteKnowledgeInfrastructure.LifecycleTimeout, cancellationToken)
            .ConfigureAwait(false);
        var inventoryVerified = !File.Exists(_bootstrapMarkerPath) &&
                                !File.Exists(_stagedGraphDatabasePath) &&
                                !File.Exists(_stagedDecisionDatabasePath) &&
                                !File.Exists(_derivedRecoveryJournalPath) &&
                                !File.Exists(_stagedRecoveryGraphPath) &&
                                File.Exists(_graphDatabasePath) == File.Exists(_decisionDatabasePath);
        var derived = DatabaseFamilyLength(_graphDatabasePath);
        var decisions = DatabaseFamilyLength(_decisionDatabasePath);
        var backups = ReadVerifiedBackupBytes(ref inventoryVerified);
        var maximum = ReadMaximumBytes(ref inventoryVerified);
        long total;
        try
        {
            total = checked(derived + decisions + backups);
        }
        catch (OverflowException)
        {
            total = long.MaxValue;
            inventoryVerified = false;
        }

        return new GraphStorageBreakdown
        {
            DerivedStoreBytes = derived,
            DecisionLedgerBytes = decisions,
            VerifiedBackupBytes = backups,
            TotalBytes = total,
            MaximumBytes = maximum,
            RequiredReserveBytes = RequiredReserveBytes,
            IsInventoryVerified = inventoryVerified,
        };
    }

    /// <inheritdoc />
    public async Task<GraphOperationResult> RecoverDerivedStoreAsync(
        string confirmationText,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!string.Equals(confirmationText, DerivedRecoveryConfirmation, StringComparison.Ordinal))
        {
            return new GraphOperationResult(
                false,
                $"Type {DerivedRecoveryConfirmation} to quarantine and rebuild only the derived graph store.",
                0);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(_directoryPath))
        {
            return new GraphOperationResult(false, "Graph storage has not been provisioned.", 0);
        }

        var lifecycleLock = new SqliteKnowledgeLifecycleLock(_lifecycleLockPath, _timeProvider);
        await using var lease = await lifecycleLock
            .AcquireAsync(SqliteKnowledgeInfrastructure.LifecycleTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (File.Exists(_bootstrapMarkerPath) || File.Exists(_stagedGraphDatabasePath) ||
            File.Exists(_stagedDecisionDatabasePath))
        {
            throw SqliteKnowledgeInfrastructure.Corrupt(
                "Bootstrap artifacts require a separate reviewed recovery; the derived store was not replaced.");
        }

        ValidateDecisionDatabaseForRecovery();
        if (File.Exists(_derivedRecoveryJournalPath))
        {
            return CompleteDerivedStoreRecovery(ReadDerivedRecoveryJournal(), cancellationToken);
        }

        if (File.Exists(_stagedRecoveryGraphPath))
        {
            DeleteDerivedRecoveryStaging();
        }

        var validationFailure = GetGraphValidationFailure();
        if (validationFailure is null)
        {
            return new GraphOperationResult(false, "The derived graph store is healthy and was not replaced.", 0);
        }

        if (validationFailure.Kind != SqliteKnowledgeFailureKind.Corrupt)
        {
            throw validationFailure;
        }

        EnsureProvisioningCapacity();
        var recoveryId = Guid.NewGuid().ToString("N");
        var quarantineFileName = $".knowledge-graph.quarantine.{recoveryId}.db";
        var journal = new DerivedRecoveryJournal(
            Version: 1,
            RecoveryId: recoveryId,
            StagingFileName: StagedRecoveryGraphFileName,
            QuarantineFileName: quarantineFileName,
            StagedSha256: string.Empty,
            State: "Prepared",
            CreatedUtcTicks: _timeProvider.GetUtcNow().UtcTicks);
        try
        {
            await InitializeStagedStoreAsync(
                    _stagedRecoveryGraphPath,
                    SqliteKnowledgeGraphSchema.ApplicationId,
                    SqliteKnowledgeGraphSchema.Version,
                    SqliteKnowledgeGraphSchema.RequiredTables,
                    SqliteKnowledgeGraphSchema.CreateVersionOne,
                    "graph_meta",
                    "graph_migration_history",
                    SqliteKnowledgeGraphSchema.RequiredColumns,
                    SqliteKnowledgeGraphSchema.RequiredIndexes,
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            journal = journal with { StagedSha256 = HashFile(_stagedRecoveryGraphPath, cancellationToken) };
            ValidateDecisionDatabaseForRecovery();
            WriteDerivedRecoveryJournal(journal);
            return CompleteDerivedStoreRecovery(journal, cancellationToken);
        }
        catch
        {
            if (!File.Exists(_derivedRecoveryJournalPath))
            {
                DeleteDerivedRecoveryStaging();
            }

            throw;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private GraphOperationResult CompleteDerivedStoreRecovery(
        DerivedRecoveryJournal journal,
        CancellationToken cancellationToken)
    {
        ValidateDerivedRecoveryJournal(journal);
        ValidateDecisionDatabaseForRecovery();
        var stagingExists = File.Exists(_stagedRecoveryGraphPath);
        if (stagingExists &&
            !string.Equals(HashFile(_stagedRecoveryGraphPath, cancellationToken), journal.StagedSha256, StringComparison.Ordinal))
        {
            throw SqliteKnowledgeInfrastructure.Corrupt(
                "The staged derived-graph replacement failed checksum validation.");
        }

        if (string.Equals(journal.State, "Prepared", StringComparison.Ordinal))
        {
            if (!stagingExists)
            {
                throw SqliteKnowledgeInfrastructure.Corrupt(
                    "The reviewed derived-graph recovery is missing its staged replacement.");
            }

            journal = journal with { State = "Quarantining" };
            WriteDerivedRecoveryJournal(journal);
        }

        if (string.Equals(journal.State, "Quarantining", StringComparison.Ordinal))
        {
            QuarantineGraphFamily(journal, cancellationToken);
            journal = journal with { State = "Promoting" };
            WriteDerivedRecoveryJournal(journal);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_graphDatabasePath))
        {
            if (!File.Exists(_stagedRecoveryGraphPath))
            {
                throw SqliteKnowledgeInfrastructure.Corrupt(
                    "The reviewed derived-graph recovery has neither a promoted nor staged replacement.");
            }

            File.Move(_stagedRecoveryGraphPath, _graphDatabasePath);
        }

        if (!string.Equals(HashFile(_graphDatabasePath, cancellationToken), journal.StagedSha256, StringComparison.Ordinal))
        {
            throw SqliteKnowledgeInfrastructure.Corrupt(
                "The promoted derived-graph replacement failed checksum validation.");
        }

        ValidateGraphDatabase(_graphDatabasePath);
        ValidateDecisionDatabaseForRecovery();
        DeleteDerivedRecoveryStaging();
        DeleteOwnedBootstrapArtifact(_derivedRecoveryJournalPath + ".staging");
        DeleteOwnedBootstrapArtifact(_derivedRecoveryJournalPath);
        return new GraphOperationResult(
            true,
            "The corrupt derived graph store was quarantined and replaced. Decisions, the deep index, and original files were not changed.",
            1);
    }

    private void QuarantineGraphFamily(DerivedRecoveryJournal journal, CancellationToken cancellationToken)
    {
        var quarantinePath = ResolveQuarantinePath(journal);
        MoveToQuarantine(_graphDatabasePath, quarantinePath, cancellationToken);
        MoveToQuarantine(_graphDatabasePath + "-wal", quarantinePath + "-wal", cancellationToken);
        MoveToQuarantine(_graphDatabasePath + "-shm", quarantinePath + "-shm", cancellationToken);
    }

    private static void MoveToQuarantine(
        string sourcePath,
        string quarantinePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(sourcePath) && File.Exists(quarantinePath))
        {
            throw SqliteKnowledgeInfrastructure.Corrupt(
                "Both active and quarantined graph artifacts exist; automatic replacement stopped for review.");
        }

        if (File.Exists(sourcePath))
        {
            File.Move(sourcePath, quarantinePath);
        }
    }

    private SqliteKnowledgeStoreException? GetGraphValidationFailure()
    {
        if (!File.Exists(_graphDatabasePath))
        {
            return SqliteKnowledgeInfrastructure.Corrupt("The rebuildable derived graph store is missing.");
        }

        try
        {
            ValidateGraphDatabase(_graphDatabasePath);
            return null;
        }
        catch (SqliteKnowledgeStoreException exception)
        {
            return exception;
        }
        catch (SqliteException exception)
        {
            return SqliteKnowledgeInfrastructure.Map(
                exception,
                "The derived graph store could not be validated for reviewed recovery.");
        }
        catch (IOException exception)
        {
            return new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.InputOutput,
                "The derived graph store could not be read for reviewed recovery.",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.PermissionDenied,
                "The derived graph store could not be read for reviewed recovery.",
                exception);
        }
    }

    private void ValidateDecisionDatabaseForRecovery()
    {
        if (!File.Exists(_decisionDatabasePath))
        {
            throw SqliteKnowledgeInfrastructure.Corrupt(
                "The authoritative graph-decision store is missing; derived-store replacement is blocked.");
        }

        try
        {
            ValidateDecisionDatabase(_decisionDatabasePath);
        }
        catch (SqliteKnowledgeStoreException)
        {
            throw;
        }
        catch (SqliteException exception)
        {
            throw SqliteKnowledgeInfrastructure.Map(
                exception,
                "The authoritative graph-decision store could not be validated.");
        }
        catch (IOException exception)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.InputOutput,
                "The authoritative graph-decision store could not be read for reviewed derived-store recovery.",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.PermissionDenied,
                "The authoritative graph-decision store could not be read for reviewed derived-store recovery.",
                exception);
        }
    }

    private bool HasAnyProvisioningArtifact() =>
        File.Exists(_graphDatabasePath) || File.Exists(_decisionDatabasePath) ||
        File.Exists(_bootstrapMarkerPath) || File.Exists(_stagedGraphDatabasePath) ||
        File.Exists(_stagedDecisionDatabasePath) || File.Exists(_derivedRecoveryJournalPath) ||
        File.Exists(_stagedRecoveryGraphPath);

    private async Task InitializeStagedStoreAsync(
        string databasePath,
        int applicationId,
        int schemaVersion,
        IReadOnlySet<string> requiredTables,
        string schemaSql,
        string metaTable,
        string migrationTable,
        IReadOnlyDictionary<string, IReadOnlySet<string>> requiredColumns,
        IReadOnlySet<string> requiredIndexes,
        CancellationToken cancellationToken)
    {
        if (File.Exists(databasePath))
        {
            ValidateDatabase(
                databasePath,
                applicationId,
                schemaVersion,
                requiredTables,
                schemaSql,
                metaTable,
                migrationTable,
                requiredColumns,
                requiredIndexes);
            return;
        }

        await SqliteKnowledgeInfrastructure.InitializeAsync(
                databasePath,
                databasePath + ".lifecycle.lock",
                applicationId,
                schemaVersion,
                requiredTables,
                schemaSql,
                metaTable,
                migrationTable,
                _timeProvider,
                cancellationToken,
                requiredColumns,
                requiredIndexes)
            .ConfigureAwait(false);
        PrepareSingleFileForPromotion(databasePath);
        ValidateDatabase(
            databasePath,
            applicationId,
            schemaVersion,
            requiredTables,
            schemaSql,
            metaTable,
            migrationTable,
            requiredColumns,
            requiredIndexes);
    }

    private static void PrepareSingleFileForPromotion(string databasePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();
        SqliteKnowledgeInfrastructure.ExecuteNonQuery(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
        SqliteKnowledgeInfrastructure.ExecuteNonQuery(connection, "PRAGMA journal_mode = DELETE;");
    }

    private void PromoteIfMissing(string stagedPath, string finalPath, Action<string> validator)
    {
        if (File.Exists(finalPath))
        {
            validator(finalPath);
            return;
        }

        if (!File.Exists(stagedPath))
        {
            throw SqliteKnowledgeInfrastructure.Corrupt("A recoverable knowledge bootstrap is missing one staged sidecar.");
        }

        validator(stagedPath);
        File.Move(stagedPath, finalPath);
        validator(finalPath);
    }

    private bool ValidateFinalStores()
    {
        try
        {
            ValidateGraphDatabase(_graphDatabasePath);
            ValidateDecisionDatabase(_decisionDatabasePath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          SqliteException or SqliteKnowledgeStoreException)
        {
            return false;
        }
    }

    private static void ValidateGraphDatabase(string path) =>
        ValidateDatabase(
            path,
            SqliteKnowledgeGraphSchema.ApplicationId,
            SqliteKnowledgeGraphSchema.Version,
            SqliteKnowledgeGraphSchema.RequiredTables,
            SqliteKnowledgeGraphSchema.CreateVersionOne,
            "graph_meta",
            "graph_migration_history",
            SqliteKnowledgeGraphSchema.RequiredColumns,
            SqliteKnowledgeGraphSchema.RequiredIndexes);

    private static void ValidateDecisionDatabase(string path) =>
        ValidateDatabase(
            path,
            SqliteKnowledgeDecisionSchema.ApplicationId,
            SqliteKnowledgeDecisionSchema.Version,
            SqliteKnowledgeDecisionSchema.RequiredTables,
            SqliteKnowledgeDecisionSchema.CreateVersionOne,
            "decision_meta",
            "decision_migration_history",
            SqliteKnowledgeDecisionSchema.RequiredColumns,
            SqliteKnowledgeDecisionSchema.RequiredIndexes);

    private static void ValidateDatabase(
        string path,
        int applicationId,
        int version,
        IReadOnlySet<string> requiredTables,
        string schemaSql,
        string metaTable,
        string migrationTable,
        IReadOnlyDictionary<string, IReadOnlySet<string>> requiredColumns,
        IReadOnlySet<string> requiredIndexes)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();
        SqliteKnowledgeInfrastructure.ExecuteNonQuery(connection, "PRAGMA foreign_keys = ON;");
        SqliteKnowledgeInfrastructure.ExecuteNonQuery(
            connection,
            $"PRAGMA busy_timeout = {SqliteKnowledgeInfrastructure.BusyTimeoutMilliseconds};");
        var actualApplicationId = SqliteKnowledgeInfrastructure.ReadPragmaInt(connection, "application_id");
        var actualVersion = SqliteKnowledgeInfrastructure.ReadPragmaInt(connection, "user_version");
        if (actualApplicationId == applicationId && actualVersion > version)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.UnsupportedSchema,
                $"Knowledge schema {actualVersion} is newer than supported schema {version}.");
        }

        SqliteKnowledgeInfrastructure.Validate(
            connection,
            applicationId,
            version,
            requiredTables,
            metaTable,
            migrationTable,
            schemaSql,
            requiredColumns,
            requiredIndexes);
    }

    private void WriteBootstrapMarker()
    {
        var marker = JsonSerializer.SerializeToUtf8Bytes(new BootstrapMarker(
            Version: 1,
            GraphFileName,
            DecisionFileName,
            StagedGraphFileName,
            StagedDecisionFileName,
            CreatedUtcTicks: _timeProvider.GetUtcNow().UtcTicks));
        using var stream = new FileStream(
            _bootstrapMarkerPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough);
        stream.Write(marker);
        stream.Flush(flushToDisk: true);
    }

    private void WriteDerivedRecoveryJournal(DerivedRecoveryJournal journal)
    {
        ValidateDerivedRecoveryJournal(journal);
        var stagingPath = _derivedRecoveryJournalPath + ".staging";
        DeleteOwnedBootstrapArtifact(stagingPath);
        var payload = JsonSerializer.SerializeToUtf8Bytes(journal);
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

        File.Move(stagingPath, _derivedRecoveryJournalPath, overwrite: true);
    }

    private DerivedRecoveryJournal ReadDerivedRecoveryJournal()
    {
        try
        {
            var payload = File.ReadAllBytes(_derivedRecoveryJournalPath);
            if (payload.Length > 16 * 1024)
            {
                throw new InvalidDataException("Derived recovery journal exceeds its bounded size.");
            }

            var journal = JsonSerializer.Deserialize<DerivedRecoveryJournal>(payload)
                ?? throw new InvalidDataException("Derived recovery journal is empty.");
            ValidateDerivedRecoveryJournal(journal);
            return journal;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.Corrupt,
                "The derived graph recovery journal is malformed and requires review.",
                exception);
        }
        catch (IOException exception)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.InputOutput,
                "The derived graph recovery journal could not be read.",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.PermissionDenied,
                "The derived graph recovery journal could not be read.",
                exception);
        }
    }

    private void ValidateDerivedRecoveryJournal(DerivedRecoveryJournal journal)
    {
        if (journal.Version != 1 ||
            !Guid.TryParseExact(journal.RecoveryId, "N", out _) ||
            !string.Equals(journal.StagingFileName, StagedRecoveryGraphFileName, StringComparison.Ordinal) ||
            !string.Equals(
                journal.QuarantineFileName,
                $".knowledge-graph.quarantine.{journal.RecoveryId}.db",
                StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(journal.QuarantineFileName), journal.QuarantineFileName, StringComparison.Ordinal) ||
            journal.StagedSha256 is not { Length: 64 } ||
            journal.StagedSha256.Any(character => !Uri.IsHexDigit(character)) ||
            journal.State is not ("Prepared" or "Quarantining" or "Promoting") ||
            journal.CreatedUtcTicks < 0)
        {
            throw new InvalidDataException("Derived recovery journal fields are invalid.");
        }
    }

    private string ResolveQuarantinePath(DerivedRecoveryJournal journal)
    {
        ValidateDerivedRecoveryJournal(journal);
        return Path.Combine(_directoryPath, journal.QuarantineFileName);
    }

    private void DeleteDerivedRecoveryStaging()
    {
        DeleteOwnedBootstrapArtifact(_stagedRecoveryGraphPath);
        DeleteOwnedBootstrapArtifact(_stagedRecoveryGraphPath + "-wal");
        DeleteOwnedBootstrapArtifact(_stagedRecoveryGraphPath + "-shm");
        DeleteOwnedBootstrapArtifact(_stagedRecoveryGraphPath + ".lifecycle.lock");
    }

    private void ValidateBootstrapMarker()
    {
        try
        {
            using var stream = new FileStream(_bootstrapMarkerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var marker = JsonSerializer.Deserialize<BootstrapMarker>(stream);
            if (marker is null || marker.Version != 1 || marker.CreatedUtcTicks < 0 ||
                !string.Equals(marker.GraphFileName, GraphFileName, StringComparison.Ordinal) ||
                !string.Equals(marker.DecisionFileName, DecisionFileName, StringComparison.Ordinal) ||
                !string.Equals(marker.StagedGraphFileName, StagedGraphFileName, StringComparison.Ordinal) ||
                !string.Equals(marker.StagedDecisionFileName, StagedDecisionFileName, StringComparison.Ordinal))
            {
                throw SqliteKnowledgeInfrastructure.Corrupt("The knowledge bootstrap marker is invalid.");
            }
        }
        catch (JsonException exception)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.Corrupt,
                "The knowledge bootstrap marker is malformed.",
                exception);
        }
    }

    private void EnsureProvisioningCapacity()
    {
        var root = Path.GetPathRoot(_directoryPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        try
        {
            if (new DriveInfo(root).AvailableFreeSpace < RequiredReserveBytes)
            {
                throw new SqliteKnowledgeStoreException(
                    SqliteKnowledgeFailureKind.Full,
                    "Knowledge sidecars cannot be provisioned without the required recovery reserve.");
            }
        }
        catch (IOException exception)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.InputOutput,
                "Available storage could not be verified before knowledge-sidecar provisioning.",
                exception);
        }
    }

    private long ReadVerifiedBackupBytes(ref bool inventoryVerified)
    {
        var backupDirectory = Path.Combine(_directoryPath, "backups", "knowledge-decisions");
        var catalogued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        if (File.Exists(_decisionDatabasePath))
        {
            try
            {
                using var connection = SqliteKnowledgeInfrastructure.OpenConnection(_decisionDatabasePath, readOnly: true);
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT relative_path, byte_length, sha256 FROM decision_backup_catalog WHERE state = 'Committed' AND sha256 IS NOT NULL ORDER BY backup_id;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var relative = reader.GetString(0);
                    var fileName = Path.GetFileName(relative);
                    if (!string.Equals(relative, fileName, StringComparison.Ordinal) || reader.IsDBNull(1) || reader.IsDBNull(2))
                    {
                        inventoryVerified = false;
                        continue;
                    }

                    var path = Path.Combine(backupDirectory, fileName);
                    var manifest = path + ".manifest.json";
                    catalogued.Add(fileName);
                    catalogued.Add(fileName + ".manifest.json");
                    if (!File.Exists(path) || !File.Exists(manifest) || FileLength(path) != reader.GetInt64(1) ||
                        !string.Equals(HashFile(path), reader.GetString(2), StringComparison.OrdinalIgnoreCase))
                    {
                        inventoryVerified = false;
                        continue;
                    }

                    total = checked(total + FileLength(path) + FileLength(manifest));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              SqliteException or SqliteKnowledgeStoreException or OverflowException)
            {
                inventoryVerified = false;
            }
        }

        if (Directory.Exists(backupDirectory))
        {
            try
            {
                inventoryVerified &= Directory.EnumerateFiles(backupDirectory, "decision-backup-*", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .All(fileName => fileName is not null && catalogued.Contains(fileName));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                inventoryVerified = false;
            }
        }

        return total;
    }

    private long ReadMaximumBytes(ref bool inventoryVerified)
    {
        if (!File.Exists(_graphDatabasePath))
        {
            return SqliteGraphStore.DefaultMaximumDatabaseBytes;
        }

        try
        {
            using var connection = SqliteKnowledgeInfrastructure.OpenConnection(_graphDatabasePath, readOnly: true);
            var value = SqliteKnowledgeInfrastructure.ExecuteScalar(
                connection,
                "SELECT COALESCE((SELECT value FROM graph_meta WHERE key = 'maximum_total_storage_bytes'), (SELECT value FROM graph_meta WHERE key = 'maximum_database_bytes'), $default);",
                ("$default", SqliteGraphStore.DefaultMaximumDatabaseBytes.ToString(CultureInfo.InvariantCulture)));
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is SqliteException or SqliteKnowledgeStoreException or FormatException or OverflowException)
        {
            inventoryVerified = false;
            return SqliteGraphStore.DefaultMaximumDatabaseBytes;
        }
    }

    private static string HashFile(string path, CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
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

    private static long DatabaseFamilyLength(string path) =>
        checked(FileLength(path) + FileLength(path + "-wal") + FileLength(path + "-shm"));

    private static long FileLength(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    private static void DeleteOwnedBootstrapArtifact(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record BootstrapMarker(
        int Version,
        string GraphFileName,
        string DecisionFileName,
        string StagedGraphFileName,
        string StagedDecisionFileName,
        long CreatedUtcTicks);

    private sealed record DerivedRecoveryJournal(
        int Version,
        string RecoveryId,
        string StagingFileName,
        string QuarantineFileName,
        string StagedSha256,
        string State,
        long CreatedUtcTicks);
}
