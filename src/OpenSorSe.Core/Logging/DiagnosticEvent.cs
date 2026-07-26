using Microsoft.Extensions.Logging;

namespace OpenSorSe.Core.Logging;

/// <summary>
/// Represents one bounded, process-session diagnostic event suitable for user-facing inspection.
/// </summary>
public sealed record DiagnosticEvent(
    long Sequence,
    DateTimeOffset TimestampUtc,
    LogLevel Severity,
    string Category,
    string Summary,
    int EventId,
    string? EventName,
    string? ExceptionType,
    string? ExceptionSummary);

/// <summary>Defines fixed bounds for inspectable process-session diagnostics.</summary>
public static class DiagnosticEventLimits
{
    /// <summary>Maximum number of recent events retained in memory.</summary>
    public const int MaximumRetainedEvents = 500;

    /// <summary>Maximum user-facing event summary length.</summary>
    public const int MaximumSummaryLength = 1000;

}
