using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Core.Diagnostics;

/// <summary>Applies classification-aware redaction and unconditional secret removal.</summary>
public sealed partial class DiagnosticsRedactor : IDiagnosticsRedactor
{
    /// <inheritdoc />
    public DiagnosticField Redact(
        DiagnosticCategory category,
        DiagnosticField field,
        bool showUnredactedContent)
    {
        ArgumentNullException.ThrowIfNull(field);
        var name = Bound(field.Name, 200);
        var credentialSafe = RedactSecrets(field.Value);
        var value = field.Classification == DiagnosticDataClassification.Secret ||
                    SecretNameRegex().IsMatch(name)
            ? "[REDACTED_SECRET]"
            : field.Classification switch
            {
                DiagnosticDataClassification.Path when !showUnredactedContent => RedactPath(credentialSafe),
                DiagnosticDataClassification.Content when !showUnredactedContent => "[REDACTED_CONTENT]",
                DiagnosticDataClassification.Metadata when !showUnredactedContent => "[REDACTED_METADATA]",
                _ => credentialSafe,
            };
        return new DiagnosticField(name, Bound(value, DiagnosticLimits.MaximumTextCharacters), field.Classification);
    }

    /// <summary>Removes credential-like values even when unredacted content was explicitly enabled.</summary>
    /// <param name="value">The candidate diagnostic text.</param>
    /// <returns>Text with supported credential patterns removed.</returns>
    public static string RedactSecrets(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var redacted = PrivateKeyRegex().Replace(value, "[REDACTED_PRIVATE_KEY]");
        redacted = AuthorizationValueRegex().Replace(redacted, "$1[REDACTED_SECRET]");
        redacted = SecretLiteralRegex().Replace(redacted, "[REDACTED_SECRET]");
        redacted = AuthorizationRegex().Replace(redacted, "$1[REDACTED_SECRET]");
        return SecretPropertyRegex().Replace(redacted, "$1[REDACTED_SECRET]$3");
    }

    private static string RedactPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return $"[REDACTED_PATH:{StableToken(value)}]";
    }

    private static string StableToken(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static string Bound(string? value, int maximum)
    {
        var normalized = value ?? string.Empty;
        if (normalized.Length <= maximum)
        {
            return normalized;
        }

        var prefixLength = Math.Max(0, maximum - DiagnosticLimits.TruncationMarker.Length);
        return normalized[..prefixLength] + DiagnosticLimits.TruncationMarker;
    }

    [GeneratedRegex("(?i)(\\\"?authorization\\\"?\\s*[:=]\\s*\\\"?)(?:bearer\\s+)?[^\\r\\n,;\\\"]+")]
    private static partial Regex AuthorizationRegex();

    [GeneratedRegex("(?i)\\b((?:bearer|basic)\\s+)[a-z0-9._~+/=-]{8,}")]
    private static partial Regex AuthorizationValueRegex();

    [GeneratedRegex("(?i)\\b(?:gh[pousr]_[a-z0-9]{20,}|github_pat_[a-z0-9_]{20,}|sk-[a-z0-9_-]{20,}|AKIA[0-9A-Z]{16}|eyJ[a-z0-9_-]{8,}\\.[a-z0-9_-]{8,}\\.[a-z0-9_-]{8,})\\b")]
    private static partial Regex SecretLiteralRegex();

    [GeneratedRegex("(?is)-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----.*?-----END (?:RSA |EC |OPENSSH )?PRIVATE KEY-----")]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex("(?i)(\\\"?(?:api[_-]?key|access[_-]?token|refresh[_-]?token|token|password|secret|client[_-]?secret|credential|cookie|set-cookie)\\\"?\\s*[:=]\\s*\\\"?)([^\\\",\\r\\n;}]+)(\\\"?)")]
    private static partial Regex SecretPropertyRegex();

    [GeneratedRegex("(?i)(^|[\\s_-])(authorization|proxy[_-]?authorization|api[_-]?key|access[_-]?token|refresh[_-]?token|token|password|secret|client[_-]?secret|credential|cookie|set-cookie)([\\s_-]|$)")]
    private static partial Regex SecretNameRegex();
}

/// <summary>Thread-safe, bounded, observer-safe implementation of the common diagnostics contracts.</summary>
public sealed class InMemoryDiagnosticsCollector : IDiagnosticsCollector
{
    private readonly object _sync = new();
    private readonly LinkedList<DiagnosticSession> _sessions = [];
    private readonly IDiagnosticsRedactor _redactor;
    private DiagnosticsSettings _settings = new();
    private long _eventSequence;

    /// <summary>Initializes an empty process-session diagnostics store.</summary>
    /// <param name="redactor">The optional classification-aware retention redactor.</param>
    public InMemoryDiagnosticsCollector(IDiagnosticsRedactor? redactor = null) =>
        _redactor = redactor ?? new DiagnosticsRedactor();

    /// <inheritdoc />
    public bool IsEnabled
    {
        get
        {
            lock (_sync)
            {
                return _settings.EnableDiagnostics;
            }
        }
    }

    /// <inheritdoc />
    public bool ShowUnredactedContent
    {
        get
        {
            lock (_sync)
            {
                return _settings.EnableDiagnostics && _settings.ShowUnredactedDiagnosticContent;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<DiagnosticSessionChangedEventArgs>? SessionChanged;

    /// <inheritdoc />
    public event EventHandler? SessionsCleared;

    /// <inheritdoc />
    public void Configure(DiagnosticsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var cleared = false;
        lock (_sync)
        {
            var discardSensitiveHistory =
                _settings.ShowUnredactedDiagnosticContent &&
                !settings.ShowUnredactedDiagnosticContent;
            _settings = settings;
            if ((!settings.EnableDiagnostics || discardSensitiveHistory) && _sessions.Count > 0)
            {
                _sessions.Clear();
                cleared = true;
            }
        }

        if (cleared)
        {
            PublishCleared();
        }
    }

    /// <inheritdoc />
    public bool IsCategoryEnabled(DiagnosticCategory category)
    {
        lock (_sync)
        {
            return _settings.IsCategoryEnabled(category);
        }
    }

    /// <inheritdoc />
    public string? BeginSession(
        DiagnosticCategory category,
        string operation,
        IReadOnlyList<DiagnosticField>? context = null,
        IReadOnlyCollection<string>? relatedSessionIds = null)
    {
        DiagnosticSession? created;
        var pruned = false;
        lock (_sync)
        {
            if (!_settings.IsCategoryEnabled(category))
            {
                return null;
            }

            var now = DateTimeOffset.UtcNow;
            var prefix = category switch
            {
                DiagnosticCategory.Ai => "ai",
                DiagnosticCategory.OcrAndTextExtraction => "ocr",
                DiagnosticCategory.Scanning => "scan",
                _ => "diagnostic",
            };
            var sessionId = $"{prefix}:{Guid.NewGuid():N}";
            created = new DiagnosticSession(
                sessionId,
                category,
                Bound(operation, 200),
                now,
                null,
                TimeSpan.Zero,
                DiagnosticStatus.Active,
                _settings.ShowUnredactedDiagnosticContent
                    ? DiagnosticContentRetentionMode.UnredactedSecretsRemoved
                    : DiagnosticContentRetentionMode.Redacted,
                BoundRelated(relatedSessionIds),
                RedactFields(category, context),
                [],
                0,
                0);
            created = created with { ApproximateRetainedBytes = Estimate(created) };
            _sessions.AddFirst(created);
            pruned = EnforceSessionBounds(category);
            pruned |= EnforceMemoryBound();
        }

        if (pruned)
        {
            PublishCleared();
        }

        PublishChanged(created, true);
        return created.SessionId;
    }

    /// <inheritdoc />
    public void Publish(
        string? sessionId,
        string stage,
        DiagnosticStatus status,
        DiagnosticSeverity severity,
        DiagnosticSection section,
        string message,
        IReadOnlyList<DiagnosticField>? fields = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        DiagnosticSession? changed = null;
        var pruned = false;
        lock (_sync)
        {
            var node = Find(sessionId);
            if (node is null ||
                node.Value.CompletedAtUtc is not null ||
                !_settings.IsCategoryEnabled(node.Value.Category))
            {
                return;
            }

            var current = node.Value;
            if (current.Events.Count >= DiagnosticLimits.MaximumEventsPerSession)
            {
                changed = current with
                {
                    DroppedEventCount = SaturatingIncrement(current.DroppedEventCount),
                    ApproximateRetainedBytes = Estimate(current),
                };
            }
            else
            {
                var captured = new DiagnosticEvent(
                    Interlocked.Increment(ref _eventSequence),
                    current.SessionId,
                    DateTimeOffset.UtcNow,
                    Bound(stage, 200),
                    status,
                    severity,
                    section,
                    Bound(DiagnosticsRedactor.RedactSecrets(message), 1000),
                    RedactFields(current.Category, fields));
                var events = current.Events.Concat([captured]).ToArray();
                var elapsed = captured.TimestampUtc - current.StartedAtUtc;
                changed = current with
                {
                    Status = DiagnosticStatus.Active,
                    Elapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed,
                    Events = Array.AsReadOnly(events),
                };
                changed = changed with { ApproximateRetainedBytes = Estimate(changed) };
                if (changed.ApproximateRetainedBytes >
                    DiagnosticLimits.MaximumApproximateRetainedBytesPerSession)
                {
                    changed = current with
                    {
                        DroppedEventCount = SaturatingIncrement(current.DroppedEventCount),
                        ApproximateRetainedBytes = Estimate(current),
                    };
                }
            }

            node.Value = changed;
            pruned = EnforceMemoryBound();
            if (pruned && Find(sessionId) is null)
            {
                changed = null;
            }
        }

        if (pruned)
        {
            PublishCleared();
        }

        if (changed is not null)
        {
            PublishChanged(changed, false);
        }
    }

    /// <inheritdoc />
    public void Relate(string? sessionId, params string?[] relatedSessionIds)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        DiagnosticSession? changed = null;
        var pruned = false;
        lock (_sync)
        {
            var node = Find(sessionId);
            if (node is null || !_settings.IsCategoryEnabled(node.Value.Category))
            {
                return;
            }

            var related = node.Value.RelatedSessionIds
                .Concat(relatedSessionIds.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!))
                .Where(value => !string.Equals(value, sessionId, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Take(16)
                .ToArray();
            changed = node.Value with { RelatedSessionIds = Array.AsReadOnly(related) };
            changed = changed with { ApproximateRetainedBytes = Estimate(changed) };
            node.Value = changed;
            pruned = EnforceMemoryBound();
            if (pruned && Find(sessionId) is null)
            {
                changed = null;
            }
        }

        if (pruned)
        {
            PublishCleared();
        }

        if (changed is not null)
        {
            PublishChanged(changed, false);
        }
    }

    /// <inheritdoc />
    public void Complete(
        string? sessionId,
        DiagnosticStatus status,
        TimeSpan elapsed,
        string message,
        DiagnosticSeverity severity = DiagnosticSeverity.Information,
        IReadOnlyList<DiagnosticField>? fields = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        DiagnosticSession? changed = null;
        var pruned = false;
        lock (_sync)
        {
            var node = Find(sessionId);
            if (node is null ||
                node.Value.CompletedAtUtc is not null ||
                !_settings.IsCategoryEnabled(node.Value.Category))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var current = node.Value;
            var boundedElapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
            var completedEvent = new DiagnosticEvent(
                Interlocked.Increment(ref _eventSequence),
                current.SessionId,
                now,
                "Completed",
                status,
                severity,
                status is DiagnosticStatus.Failed or DiagnosticStatus.Rejected or DiagnosticStatus.Cancelled
                    ? DiagnosticSection.WarningsAndErrors
                    : DiagnosticSection.Overview,
                Bound(DiagnosticsRedactor.RedactSecrets(message), 1000),
                RedactFields(current.Category, fields));
            var canRetainEvent = current.Events.Count < DiagnosticLimits.MaximumEventsPerSession;
            changed = current with
            {
                Status = status,
                CompletedAtUtc = now,
                Elapsed = boundedElapsed,
                Events = canRetainEvent
                    ? Array.AsReadOnly(current.Events.Concat([completedEvent]).ToArray())
                    : current.Events,
                DroppedEventCount = canRetainEvent
                    ? current.DroppedEventCount
                    : SaturatingIncrement(current.DroppedEventCount),
            };
            changed = changed with { ApproximateRetainedBytes = Estimate(changed) };
            if (changed.ApproximateRetainedBytes >
                DiagnosticLimits.MaximumApproximateRetainedBytesPerSession)
            {
                changed = current with
                {
                    Status = status,
                    CompletedAtUtc = now,
                    Elapsed = boundedElapsed,
                    DroppedEventCount = SaturatingIncrement(current.DroppedEventCount),
                };
                changed = changed with { ApproximateRetainedBytes = Estimate(changed) };
            }

            node.Value = changed;
            pruned = EnforceMemoryBound();
            if (pruned && Find(sessionId) is null)
            {
                changed = null;
            }
        }

        if (pruned)
        {
            PublishCleared();
        }

        if (changed is not null)
        {
            PublishChanged(changed, false);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DiagnosticSession> GetRecent()
    {
        lock (_sync)
        {
            return Array.AsReadOnly(_sessions.ToArray());
        }
    }

    /// <inheritdoc />
    public DiagnosticSession? Get(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        lock (_sync)
        {
            return Find(sessionId)?.Value;
        }
    }

    /// <inheritdoc />
    public void Clear(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var cleared = false;
        lock (_sync)
        {
            var node = Find(sessionId);
            if (node is not null)
            {
                _sessions.Remove(node);
                cleared = true;
            }
        }

        if (cleared)
        {
            PublishCleared();
        }
    }

    /// <inheritdoc />
    public void ClearAll()
    {
        var cleared = false;
        lock (_sync)
        {
            if (_sessions.Count > 0)
            {
                _sessions.Clear();
                cleared = true;
            }
        }

        if (cleared)
        {
            PublishCleared();
        }
    }

    private LinkedListNode<DiagnosticSession>? Find(string sessionId)
    {
        var node = _sessions.First;
        while (node is not null && !string.Equals(node.Value.SessionId, sessionId, StringComparison.Ordinal))
        {
            node = node.Next;
        }

        return node;
    }

    private IReadOnlyList<DiagnosticField> RedactFields(
        DiagnosticCategory category,
        IReadOnlyList<DiagnosticField>? fields)
    {
        if (fields is null || fields.Count == 0)
        {
            return [];
        }

        var redacted = fields
            .Where(field => field is not null && !string.IsNullOrWhiteSpace(field.Name))
            .Take(100)
            .Select(field => TryRedact(category, field))
            .ToArray();
        var retained = new List<DiagnosticField>(redacted.Length);
        var reserved = "Additional diagnostic fields".Length + DiagnosticLimits.TruncationMarker.Length;
        var budget = Math.Max(0, DiagnosticLimits.MaximumTextCharactersPerEvent - reserved);
        var used = 0;
        var truncated = fields.Count > redacted.Length;
        foreach (var field in redacted)
        {
            var available = budget - used - field.Name.Length;
            if (available <= 0)
            {
                truncated = true;
                break;
            }

            if (field.Value.Length > available)
            {
                var prefixLength = Math.Max(0, available - DiagnosticLimits.TruncationMarker.Length);
                retained.Add(field with
                {
                    Value = field.Value[..prefixLength] + DiagnosticLimits.TruncationMarker,
                });
                truncated = true;
                break;
            }

            retained.Add(field);
            used += field.Name.Length + field.Value.Length;
        }

        if (truncated)
        {
            retained.Add(new DiagnosticField(
                "Additional diagnostic fields",
                DiagnosticLimits.TruncationMarker));
        }

        return Array.AsReadOnly(retained.ToArray());
    }

    private DiagnosticField TryRedact(DiagnosticCategory category, DiagnosticField field)
    {
        try
        {
            return _redactor.Redact(
                category,
                field,
                _settings.EnableDiagnostics && _settings.ShowUnredactedDiagnosticContent);
        }
        catch
        {
            return new DiagnosticField(
                "Diagnostic field omitted",
                "[NOT RETAINED: REDACTION FAILED]");
        }
    }

    private static IReadOnlyList<string> BoundRelated(IReadOnlyCollection<string>? values) =>
        values is null
            ? []
            : Array.AsReadOnly(values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => Bound(value, 200))
                .Distinct(StringComparer.Ordinal)
                .Take(16)
                .ToArray());

    private bool EnforceSessionBounds(DiagnosticCategory category)
    {
        var removed = false;
        while (_sessions.Count > DiagnosticLimits.MaximumRetainedSessions)
        {
            _sessions.RemoveLast();
            removed = true;
        }

        while (_sessions.Count(item => item.Category == category) >
               DiagnosticLimits.MaximumRetainedSessionsPerCategory)
        {
            var node = _sessions.Last;
            while (node is not null && node.Value.Category != category)
            {
                node = node.Previous;
            }

            if (node is null)
            {
                break;
            }

            _sessions.Remove(node);
            removed = true;
        }

        return removed;
    }

    private bool EnforceMemoryBound()
    {
        var removed = false;
        while (_sessions.Count > 1 &&
               _sessions.Sum(item => item.ApproximateRetainedBytes) >
               DiagnosticLimits.MaximumApproximateRetainedBytes)
        {
            _sessions.RemoveLast();
            removed = true;
        }

        return removed;
    }

    private void PublishChanged(DiagnosticSession session, bool isNew)
    {
        foreach (EventHandler<DiagnosticSessionChangedEventArgs> handler in
                 SessionChanged?.GetInvocationList() ?? [])
        {
            try
            {
                handler(this, new DiagnosticSessionChangedEventArgs(session, isNew));
            }
            catch
            {
                // Observer failures must never affect the feature that produced the event.
            }
        }
    }

    private void PublishCleared()
    {
        foreach (EventHandler handler in SessionsCleared?.GetInvocationList() ?? [])
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch
            {
                // Clear observers are isolated from settings and feature behavior.
            }
        }
    }

    private static long Estimate(DiagnosticSession session)
    {
        long characters = session.SessionId.Length + session.Operation.Length;
        characters += session.RelatedSessionIds.Sum(value => value.Length);
        characters += session.Context.Sum(field => field.Name.Length + field.Value.Length);
        foreach (var item in session.Events)
        {
            characters += item.Stage.Length + item.Message.Length;
            characters += item.Fields.Sum(field => field.Name.Length + field.Value.Length);
        }

        return characters * sizeof(char) + session.Events.Count * 192L + 512;
    }

    private static int SaturatingIncrement(int value) =>
        value == int.MaxValue ? int.MaxValue : value + 1;

    private static string Bound(string? value, int maximum)
    {
        var normalized = value ?? string.Empty;
        if (normalized.Length <= maximum)
        {
            return normalized;
        }

        var prefixLength = Math.Max(0, maximum - DiagnosticLimits.TruncationMarker.Length);
        return normalized[..prefixLength] + DiagnosticLimits.TruncationMarker;
    }
}

/// <summary>Formats already-redacted retained sessions for explicit manual export.</summary>
public sealed class DiagnosticsExportService : IDiagnosticsExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <inheritdoc />
    public string ExportJson(DiagnosticSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return JsonSerializer.Serialize(session, JsonOptions);
    }

    /// <inheritdoc />
    public string ExportText(DiagnosticSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var output = new StringBuilder();
        AppendSession(output, session);
        return output.ToString().TrimEnd();
    }

    /// <inheritdoc />
    public string ExportAllJson(IReadOnlyList<DiagnosticSession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        return JsonSerializer.Serialize(sessions, JsonOptions);
    }

    /// <inheritdoc />
    public string ExportAllText(IReadOnlyList<DiagnosticSession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var output = new StringBuilder();
        foreach (var session in sessions)
        {
            if (output.Length > 0)
            {
                output.AppendLine().AppendLine(new string('=', 72)).AppendLine();
            }

            AppendSession(output, session);
        }

        return output.ToString().TrimEnd();
    }

    private static void AppendSession(StringBuilder output, DiagnosticSession session)
    {
        output.AppendLine("OpenSorSe Advanced Diagnostic");
        output.AppendLine($"Session ID: {session.SessionId}");
        output.AppendLine($"Category: {DiagnosticCategoryRegistry.Get(session.Category).DisplayName}");
        output.AppendLine($"Operation: {session.Operation}");
        output.AppendLine($"Started (UTC): {session.StartedAtUtc:O}");
        output.AppendLine($"Elapsed: {session.Elapsed:c}");
        output.AppendLine($"Status: {session.Status}");
        output.AppendLine($"Content retention mode: {RetentionModeLabel(session.ContentRetentionMode)}");
        output.AppendLine($"Related sessions: {(session.RelatedSessionIds.Count == 0 ? "None" : string.Join(", ", session.RelatedSessionIds))}");
        output.AppendLine($"Approximate retained bytes: {session.ApproximateRetainedBytes}");
        output.AppendLine($"Dropped events: {session.DroppedEventCount}");
        AppendFields(output, "Context", session.Context);
        foreach (var group in session.Events.GroupBy(item => item.Section))
        {
            output.AppendLine().AppendLine($"== {SectionName(group.Key)} ==");
            foreach (var item in group.OrderBy(value => value.Sequence))
            {
                output.AppendLine(
                    $"{item.TimestampUtc:O}  {item.Severity,-11}  {item.Status,-18}  {item.Stage}: {item.Message}");
                foreach (var field in item.Fields)
                {
                    output.AppendLine($"  {field.Name}: {field.Value}");
                }
            }
        }
    }

    private static void AppendFields(
        StringBuilder output,
        string heading,
        IReadOnlyList<DiagnosticField> fields)
    {
        if (fields.Count == 0)
        {
            return;
        }

        output.AppendLine().AppendLine($"== {heading} ==");
        foreach (var field in fields)
        {
            output.AppendLine($"{field.Name}: {field.Value}");
        }
    }

    private static string SectionName(DiagnosticSection section) => section switch
    {
        DiagnosticSection.IntermediateResults => "Intermediate results",
        DiagnosticSection.WarningsAndErrors => "Warnings and errors",
        _ => section.ToString(),
    };

    private static string RetentionModeLabel(DiagnosticContentRetentionMode mode) => mode switch
    {
        DiagnosticContentRetentionMode.Redacted =>
            "Redacted before retention",
        DiagnosticContentRetentionMode.UnredactedSecretsRemoved =>
            "Unredacted content retained by explicit opt-in; credentials and secrets removed",
        _ => "Unknown",
    };
}
