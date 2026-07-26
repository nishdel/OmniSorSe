using Microsoft.Extensions.Logging;

namespace OpenSorSe.Core.Logging;

internal sealed class DiagnosticEventBuffer
{
    private readonly Queue<DiagnosticEvent> _events = new();
    private readonly object _syncRoot = new();
    private long _sequence;

    public void Add(LogLevel severity, string category, EventId eventId, string message, Exception? exception)
    {
        try
        {
            var diagnosticEvent = new DiagnosticEvent(
                Interlocked.Increment(ref _sequence),
                DateTimeOffset.UtcNow,
                severity,
                Sanitize(category, 256),
                Sanitize(message, DiagnosticEventLimits.MaximumSummaryLength),
                eventId.Id,
                string.IsNullOrWhiteSpace(eventId.Name) ? null : Sanitize(eventId.Name, 128),
                exception?.GetType().Name,
                null);

            lock (_syncRoot)
            {
                _events.Enqueue(diagnosticEvent);
                while (_events.Count > DiagnosticEventLimits.MaximumRetainedEvents)
                {
                    _events.Dequeue();
                }
            }
        }
        catch
        {
            // Diagnostic capture is best-effort and must never break the logged operation.
        }
    }

    public IReadOnlyList<DiagnosticEvent> SnapshotNewestFirst()
    {
        lock (_syncRoot)
        {
            return Array.AsReadOnly(_events.Reverse().ToArray());
        }
    }

    private static string Sanitize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "No diagnostic summary was supplied.";
        }

        var normalized = new string(value
            .Select(character => char.IsControl(character) && character is not '\t' ? ' ' : character)
            .ToArray())
            .Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}
