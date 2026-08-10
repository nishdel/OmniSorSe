using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.KnowledgeGraph;

namespace OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

/// <summary>Classifies failures from an application-owned knowledge database.</summary>
public enum SqliteKnowledgeFailureKind
{
    /// <summary>The database is temporarily busy or locked.</summary>
    Busy,
    /// <summary>The storage volume has insufficient space.</summary>
    Full,
    /// <summary>The application lacks permission to access the database.</summary>
    PermissionDenied,
    /// <summary>The database cannot be opened or an input/output operation failed.</summary>
    InputOutput,
    /// <summary>The database or its logical schema is corrupt.</summary>
    Corrupt,
    /// <summary>The database schema is newer than this application supports.</summary>
    UnsupportedSchema,
    /// <summary>A persisted constraint or operation precondition failed.</summary>
    Constraint,
    /// <summary>An unexpected provider failure occurred.</summary>
    Unknown,
}

/// <summary>Represents one precisely classified knowledge-store failure.</summary>
public sealed class SqliteKnowledgeStoreException : GraphPersistenceException
{
    /// <summary>Creates a classified provider exception.</summary>
    public SqliteKnowledgeStoreException(
        SqliteKnowledgeFailureKind kind,
        string message,
        Exception? innerException = null)
        : base(ToReasonCode(kind), message, innerException, ToDisposition(kind))
    {
        Kind = kind;
    }

    /// <summary>Gets the stable failure classification.</summary>
    public SqliteKnowledgeFailureKind Kind { get; }

    private static string ToReasonCode(SqliteKnowledgeFailureKind kind) => kind switch
    {
        SqliteKnowledgeFailureKind.Busy => "sqlite-busy",
        SqliteKnowledgeFailureKind.Full => "sqlite-full",
        SqliteKnowledgeFailureKind.PermissionDenied => "sqlite-permission-denied",
        SqliteKnowledgeFailureKind.InputOutput => "sqlite-input-output",
        SqliteKnowledgeFailureKind.Corrupt => "sqlite-corrupt",
        SqliteKnowledgeFailureKind.UnsupportedSchema => "sqlite-unsupported-schema",
        SqliteKnowledgeFailureKind.Constraint => "sqlite-constraint",
        _ => "sqlite-unknown",
    };

    private static GraphPersistenceFailureDisposition ToDisposition(SqliteKnowledgeFailureKind kind) => kind switch
    {
        SqliteKnowledgeFailureKind.Busy or SqliteKnowledgeFailureKind.InputOutput =>
            GraphPersistenceFailureDisposition.Retryable,
        SqliteKnowledgeFailureKind.Full => GraphPersistenceFailureDisposition.WaitingForResources,
        _ => GraphPersistenceFailureDisposition.Permanent,
    };
}

/// <summary>
/// Provides a bounded operating-system-backed lifecycle lock for create, migrate,
/// backup, restore, and database promotion operations.
/// </summary>
public sealed class SqliteKnowledgeLifecycleLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessGates =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _lockPath;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the lock for one application data directory.</summary>
    public SqliteKnowledgeLifecycleLock(string lockPath, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);
        _lockPath = Path.GetFullPath(lockPath);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Acquires process-local and inter-process ownership within the timeout.</summary>
    public async Task<IAsyncDisposable> AcquireAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var processGate = ProcessGates.GetOrAdd(_lockPath, static _ => new SemaphoreSlim(1, 1));
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await processGate.WaitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.Busy,
                "The application data lifecycle lock could not be acquired within the configured timeout.");
        }

        try
        {
            var started = _timeProvider.GetTimestamp();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_lockPath)
                        ?? throw new InvalidOperationException("The lifecycle lock must have a parent directory."));
                    var stream = new FileStream(
                        _lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 4096,
                        FileOptions.WriteThrough);
                    WriteOwnerMetadata(stream);
                    return new LifecycleLease(stream, processGate);
                }
                catch (IOException exception)
                {
                    if (_timeProvider.GetElapsedTime(started) >= timeout)
                    {
                        throw new SqliteKnowledgeStoreException(
                            SqliteKnowledgeFailureKind.Busy,
                            "Another OpenSorSe process is performing an application data lifecycle operation.",
                            exception);
                    }

                    var remaining = timeout - _timeProvider.GetElapsedTime(started);
                    var delay = remaining < TimeSpan.FromMilliseconds(50)
                        ? remaining
                        : TimeSpan.FromMilliseconds(50);
                    await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            processGate.Release();
            throw;
        }
    }

    private static void WriteOwnerMetadata(FileStream stream)
    {
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ProcessId = Environment.ProcessId,
            ProcessStartUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime(),
            Machine = Environment.MachineName,
        });
        stream.SetLength(0);
        stream.Write(metadata);
        stream.Flush(flushToDisk: true);
    }

    private sealed class LifecycleLease : IAsyncDisposable
    {
        private FileStream? _stream;
        private SemaphoreSlim? _processGate;

        internal LifecycleLease(FileStream stream, SemaphoreSlim processGate)
        {
            _stream = stream;
            _processGate = processGate;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _stream, null)?.Dispose();
            Interlocked.Exchange(ref _processGate, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Contains shared, narrowly scoped SQLite lifecycle and validation helpers.</summary>
internal static class SqliteKnowledgeInfrastructure
{
    internal const int BusyTimeoutMilliseconds = 5_000;
    private const int CancellableBusySliceMilliseconds = 50;
    internal static readonly TimeSpan LifecycleTimeout = TimeSpan.FromSeconds(30);

    internal static SqliteConnection OpenConnection(
        string databasePath,
        bool readOnly = false,
        bool pooling = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            DefaultTimeout = cancellationToken.CanBeCanceled
                ? 1
                : BusyTimeoutMilliseconds / 1_000,
            // Knowledge Graph operations deliberately own short-lived connections.
            // Pooling would retain file handles after store disposal and a global
            // pool clear can invalidate unrelated stores that are still active.
            Pooling = pooling,
        }.ToString());
        using var cancellationRegistration = cancellationToken.Register(
            static state => TryInterrupt((SqliteConnection)state!),
            connection);
        try
        {
            connection.Open();
            ExecuteNonQuery(connection, "PRAGMA foreign_keys = ON;");
            var busyTimeout = cancellationToken.CanBeCanceled
                ? CancellableBusySliceMilliseconds
                : BusyTimeoutMilliseconds;
            ExecuteNonQuery(connection, $"PRAGMA busy_timeout = {busyTimeout};");
            if (!readOnly)
            {
                ExecuteNonQuery(connection, "PRAGMA journal_mode = WAL;");
                ExecuteNonQuery(connection, "PRAGMA synchronous = FULL;");
                ExecuteNonQuery(connection, "PRAGMA wal_autocheckpoint = 1000;");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    internal static void TryInterrupt(SqliteConnection connection)
    {
        try
        {
            SQLitePCL.raw.sqlite3_interrupt(connection.Handle);
        }
        catch (ObjectDisposedException)
        {
            // Disposal won the race with cooperative cancellation.
        }
        catch (InvalidOperationException)
        {
            // The connection was not yet open or already closed.
        }
    }

    internal static T ExecuteWithBusyRetry<T>(
        Func<T> action,
        CancellationToken cancellationToken,
        string operation)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return action();
            }
            catch (SqliteKnowledgeStoreException)
            {
                throw;
            }
            catch (SqliteException exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    $"{operation} was cancelled.",
                    exception,
                    cancellationToken);
            }
            catch (SqliteException exception) when (
                cancellationToken.CanBeCanceled &&
                exception.SqliteErrorCode is 5 or 6 &&
                Stopwatch.GetElapsedTime(started) < TimeSpan.FromMilliseconds(BusyTimeoutMilliseconds))
            {
                // The connection uses a short native busy slice. Retrying the complete
                // transaction keeps the cumulative deadline bounded while allowing a
                // cancellation request to be observed between slices.
            }
            catch (SqliteException exception)
            {
                throw Map(exception, operation);
            }
        }
    }

    internal static async Task InitializeAsync(
        string databasePath,
        string lifecycleLockPath,
        int applicationId,
        int schemaVersion,
        IReadOnlySet<string> requiredTables,
        string createSchemaSql,
        string metaTable,
        string migrationTable,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? requiredColumns = null,
        IReadOnlySet<string>? requiredIndexes = null)
    {
        var lifecycleLock = new SqliteKnowledgeLifecycleLock(lifecycleLockPath, timeProvider);
        await using var lease = await lifecycleLock.AcquireAsync(LifecycleTimeout, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException("A knowledge database must have a parent directory."));

        try
        {
            using var connection = OpenConnection(databasePath);
            var actualApplicationId = ReadPragmaInt(connection, "application_id");
            var actualVersion = ReadPragmaInt(connection, "user_version");
            var hasTables = HasUserTables(connection);

            if (actualApplicationId != 0 && actualApplicationId != applicationId)
            {
                throw new SqliteKnowledgeStoreException(
                    SqliteKnowledgeFailureKind.Corrupt,
                    "The selected database belongs to a different OpenSorSe data store.");
            }

            if (actualVersion > schemaVersion)
            {
                throw new SqliteKnowledgeStoreException(
                    SqliteKnowledgeFailureKind.UnsupportedSchema,
                    $"Knowledge database schema {actualVersion} is newer than supported schema {schemaVersion}.");
            }

            if (actualVersion == 0 && hasTables)
            {
                throw new SqliteKnowledgeStoreException(
                    SqliteKnowledgeFailureKind.Corrupt,
                    "The knowledge database contains unversioned tables and cannot be migrated safely.");
            }

            if (actualVersion < schemaVersion)
            {
                var now = timeProvider.GetUtcNow().UtcTicks;
                var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(createSchemaSql)));
                using var transaction = connection.BeginTransaction();
                ExecuteNonQuery(connection, transaction, createSchemaSql);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    $"INSERT INTO {metaTable}(key, value) VALUES ('schema_version', $version);",
                    ("$version", schemaVersion.ToString(CultureInfo.InvariantCulture)));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    $"INSERT INTO {migrationTable}(schema_version, migration_id, migration_checksum, started_utc_ticks, completed_utc_ticks, application_version) VALUES ($version, $id, $checksum, $started, $completed, $application);",
                    ("$version", schemaVersion),
                    ("$id", $"knowledge-schema-{schemaVersion}"),
                    ("$checksum", checksum),
                    ("$started", now),
                    ("$completed", now),
                    ("$application", typeof(SqliteKnowledgeInfrastructure).Assembly.GetName().Version?.ToString() ?? "unknown"));
                ExecuteNonQuery(connection, transaction, $"PRAGMA application_id = {applicationId};");
                ExecuteNonQuery(connection, transaction, $"PRAGMA user_version = {schemaVersion};");
                transaction.Commit();
            }

            Validate(
                connection,
                applicationId,
                schemaVersion,
                requiredTables,
                metaTable,
                migrationTable,
                createSchemaSql,
                requiredColumns,
                requiredIndexes);
        }
        catch (SqliteKnowledgeStoreException)
        {
            throw;
        }
        catch (SqliteException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "Knowledge database initialization was cancelled.",
                exception,
                cancellationToken);
        }
        catch (SqliteException exception)
        {
            throw Map(exception, "The knowledge database could not be initialized safely.");
        }
    }

    internal static void Validate(
        SqliteConnection connection,
        int applicationId,
        int schemaVersion,
        IReadOnlySet<string> requiredTables,
        string metaTable,
        string migrationTable,
        string? expectedSchemaSql = null,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? requiredColumns = null,
        IReadOnlySet<string>? requiredIndexes = null)
    {
        if (ReadPragmaInt(connection, "application_id") != applicationId)
        {
            throw Corrupt("The knowledge database application identifier is invalid.");
        }

        if (ReadPragmaInt(connection, "user_version") != schemaVersion)
        {
            throw Corrupt("The knowledge database schema markers disagree.");
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA quick_check;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!string.Equals(reader.GetString(0), "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw Corrupt("The knowledge database failed SQLite integrity validation.");
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_key_check;";
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                throw Corrupt("The knowledge database contains an invalid foreign-key reference.");
            }
        }

        var actualTables = new HashSet<string>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                actualTables.Add(reader.GetString(0));
            }
        }

        if (!requiredTables.IsSubsetOf(actualTables))
        {
            var missing = requiredTables.Except(actualTables, StringComparer.Ordinal).Order(StringComparer.Ordinal);
            throw Corrupt($"The knowledge database is missing required tables: {string.Join(", ", missing)}.");
        }

        if (requiredColumns is not null)
        {
            foreach (var requiredTable in requiredColumns)
            {
                var actualColumns = new HashSet<string>(StringComparer.Ordinal);
                using var command = connection.CreateCommand();
                command.CommandText = $"PRAGMA table_info([{requiredTable.Key}]);";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    actualColumns.Add(reader.GetString(1));
                }

                if (!requiredTable.Value.IsSubsetOf(actualColumns))
                {
                    var missing = requiredTable.Value.Except(actualColumns, StringComparer.Ordinal).Order(StringComparer.Ordinal);
                    throw Corrupt($"The knowledge database table '{requiredTable.Key}' is missing required columns: {string.Join(", ", missing)}.");
                }
            }
        }

        if (requiredIndexes is not null)
        {
            var actualIndexes = new HashSet<string>(StringComparer.Ordinal);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index';";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                actualIndexes.Add(reader.GetString(0));
            }

            if (!requiredIndexes.IsSubsetOf(actualIndexes))
            {
                var missing = requiredIndexes.Except(actualIndexes, StringComparer.Ordinal).Order(StringComparer.Ordinal);
                throw Corrupt($"The knowledge database is missing required indexes: {string.Join(", ", missing)}.");
            }
        }

        var metaVersion = ExecuteScalar(
            connection,
            $"SELECT value FROM {metaTable} WHERE key = 'schema_version';") as string;
        if (!string.Equals(metaVersion, schemaVersion.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw Corrupt("The knowledge database metadata schema marker is invalid.");
        }

        using var migration = connection.CreateCommand();
        migration.CommandText =
            $"SELECT migration_id, migration_checksum, started_utc_ticks, completed_utc_ticks FROM {migrationTable} WHERE schema_version = $version;";
        migration.Parameters.AddWithValue("$version", schemaVersion);
        using var migrationReader = migration.ExecuteReader();
        if (!migrationReader.Read())
        {
            throw Corrupt("The knowledge database migration history is incomplete.");
        }

        var expectedMigrationId = $"knowledge-schema-{schemaVersion}";
        if (!string.Equals(migrationReader.GetString(0), expectedMigrationId, StringComparison.Ordinal) ||
            migrationReader.GetInt64(2) < 0 || migrationReader.GetInt64(3) < migrationReader.GetInt64(2))
        {
            throw Corrupt("The knowledge database migration history is invalid.");
        }

        if (expectedSchemaSql is not null)
        {
            var expectedChecksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expectedSchemaSql)));
            if (!string.Equals(migrationReader.GetString(1), expectedChecksum, StringComparison.Ordinal))
            {
                throw Corrupt("The knowledge database migration checksum is invalid.");
            }
        }

        if (migrationReader.Read())
        {
            throw Corrupt("The knowledge database migration history contains duplicate version rows.");
        }
    }

    internal static SqliteKnowledgeStoreException Map(SqliteException exception, string operation)
    {
        var kind = exception.SqliteErrorCode switch
        {
            5 or 6 => SqliteKnowledgeFailureKind.Busy,
            13 => SqliteKnowledgeFailureKind.Full,
            3 or 8 or 23 => SqliteKnowledgeFailureKind.PermissionDenied,
            10 or 14 or 15 => SqliteKnowledgeFailureKind.InputOutput,
            11 or 26 => SqliteKnowledgeFailureKind.Corrupt,
            19 => SqliteKnowledgeFailureKind.Constraint,
            _ => SqliteKnowledgeFailureKind.Unknown,
        };
        return new SqliteKnowledgeStoreException(kind, $"{operation} SQLite error {exception.SqliteErrorCode}.", exception);
    }

    internal static SqliteKnowledgeStoreException Corrupt(string message) =>
        new(SqliteKnowledgeFailureKind.Corrupt, message);

    internal static int ReadPragmaInt(SqliteConnection connection, string pragma) =>
        Convert.ToInt32(ExecuteScalar(connection, $"PRAGMA {pragma};"), CultureInfo.InvariantCulture);

    internal static bool HasUserTables(SqliteConnection connection) =>
        Convert.ToInt32(
            ExecuteScalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';"),
            CultureInfo.InvariantCulture) > 0;

    internal static int ExecuteNonQuery(
        SqliteConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters) =>
        ExecuteNonQuery(connection, transaction: null, sql, parameters);

    internal static int ExecuteNonQuery(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameters(command, parameters);
        return command.ExecuteNonQuery();
    }

    internal static object? ExecuteScalar(
        SqliteConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        return command.ExecuteScalar();
    }

    internal static void AddParameters(
        SqliteCommand command,
        params (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }
}
