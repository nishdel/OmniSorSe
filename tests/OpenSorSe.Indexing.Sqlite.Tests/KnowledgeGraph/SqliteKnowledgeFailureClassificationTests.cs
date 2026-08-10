using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

namespace OpenSorSe.Indexing.Sqlite.Tests.KnowledgeGraph;

/// <summary>Verifies SQLite failures map to provider-neutral durable-work behavior.</summary>
public sealed class SqliteKnowledgeFailureClassificationTests
{
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
