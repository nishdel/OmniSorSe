using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

namespace OpenSorSe.Indexing.Sqlite.Tests.KnowledgeGraph;

/// <summary>Verifies SQLite failures map to provider-neutral durable-work behavior.</summary>
public sealed class SqliteKnowledgeFailureClassificationTests
{
    /// <summary>Ensures dynamic schema identifiers cannot introduce SQL or PRAGMA syntax.</summary>
    [Theory]
    [InlineData("graph_nodes; DROP TABLE graph_edges;")]
    [InlineData("graph_nodes]")]
    [InlineData("graph nodes")]
    [InlineData("--comment")]
    public void InternalSqlIdentifier_RejectsSyntaxBearingValues(string identifier)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => SqliteKnowledgeInfrastructure.RequireSqlIdentifier(identifier));

        Assert.Equal("identifier", exception.ParamName);
    }

    /// <summary>Ensures fixed application-owned schema names remain accepted.</summary>
    [Theory]
    [InlineData("graph_nodes")]
    [InlineData("graph_meta")]
    [InlineData("legacy_mirror_ingest_rows")]
    public void InternalSqlIdentifier_AcceptsFixedSchemaNames(string identifier) =>
        Assert.Equal(identifier, SqliteKnowledgeInfrastructure.RequireSqlIdentifier(identifier));

    /// <summary>Ensures transient, resource, and permanent failures cannot be misclassified by the coordinator.</summary>
    [Theory]
    [InlineData(SqliteKnowledgeFailureKind.Busy, GraphPersistenceFailureDisposition.Retryable)]
    [InlineData(SqliteKnowledgeFailureKind.InputOutput, GraphPersistenceFailureDisposition.Retryable)]
    [InlineData(SqliteKnowledgeFailureKind.Unknown, GraphPersistenceFailureDisposition.Permanent)]
    [InlineData(SqliteKnowledgeFailureKind.Full, GraphPersistenceFailureDisposition.WaitingForResources)]
    [InlineData(SqliteKnowledgeFailureKind.PermissionDenied, GraphPersistenceFailureDisposition.Permanent)]
    [InlineData(SqliteKnowledgeFailureKind.Corrupt, GraphPersistenceFailureDisposition.Permanent)]
    [InlineData(SqliteKnowledgeFailureKind.UnsupportedSchema, GraphPersistenceFailureDisposition.Permanent)]
    [InlineData(SqliteKnowledgeFailureKind.Constraint, GraphPersistenceFailureDisposition.Permanent)]
    public void ClassifiedFailure_ExposesProviderNeutralDisposition(
        SqliteKnowledgeFailureKind kind,
        GraphPersistenceFailureDisposition expected)
    {
        var failure = new SqliteKnowledgeStoreException(kind, "Synthetic classified provider failure.");

        Assert.Equal(expected, failure.Disposition);
        Assert.StartsWith("sqlite-", failure.ReasonCode, StringComparison.Ordinal);
    }
}
