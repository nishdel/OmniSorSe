namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>Describes how durable graph work may respond to a provider failure.</summary>
public enum GraphPersistenceFailureDisposition
{
    /// <summary>The operation requires changed input, repair, or user action.</summary>
    Permanent,
    /// <summary>The operation may be retried under the bounded retry policy.</summary>
    Retryable,
    /// <summary>The operation must wait until storage resources become available.</summary>
    WaitingForResources,
}

/// <summary>
/// Represents a provider-classified graph persistence failure without leaking storage-specific APIs
/// through Application contracts.
/// </summary>
public class GraphPersistenceException : Exception
{
    /// <summary>Initializes a classified persistence failure.</summary>
    public GraphPersistenceException(
        string reasonCode,
        string message,
        Exception? innerException = null,
        GraphPersistenceFailureDisposition disposition = GraphPersistenceFailureDisposition.Permanent)
        : base(message, innerException)
    {
        GraphQueryService.ValidateBounded(reasonCode, 128, allowEmpty: false);
        if (!reasonCode.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':'))
        {
            throw new ArgumentException("A persistence reason code must contain only privacy-safe code characters.", nameof(reasonCode));
        }

        if (!Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }

        ReasonCode = reasonCode;
        Disposition = disposition;
    }

    /// <summary>Gets a bounded privacy-safe failure category.</summary>
    public string ReasonCode { get; }

    /// <summary>Gets the provider-neutral durable-work response classification.</summary>
    public GraphPersistenceFailureDisposition Disposition { get; }
}
