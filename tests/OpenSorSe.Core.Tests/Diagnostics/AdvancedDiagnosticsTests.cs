using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;

namespace OpenSorSe.Core.Tests.Diagnostics;

/// <summary>Verifies the common bounded, privacy-aware, process-session diagnostics framework.</summary>
public sealed class AdvancedDiagnosticsTests
{
    /// <summary>Verifies the master and category switches independently prevent detailed sessions.</summary>
    [Fact]
    public void BeginSession_MasterOrCategoryDisabled_RetainsNothing()
    {
        var collector = new InMemoryDiagnosticsCollector();
        collector.Configure(new DiagnosticsSettings
        {
            EnableDiagnostics = false,
            AiDiagnostics = true,
        });

        Assert.Null(collector.BeginSession(DiagnosticCategory.Ai, "Disabled master"));
        collector.Configure(new DiagnosticsSettings
        {
            EnableDiagnostics = true,
            AiDiagnostics = false,
            ScanningDiagnostics = true,
        });
        Assert.Null(collector.BeginSession(DiagnosticCategory.Ai, "Disabled category"));
        Assert.NotNull(collector.BeginSession(DiagnosticCategory.Scanning, "Enabled category"));
    }

    /// <summary>Verifies planned toggles cannot create sessions until the category is truthfully instrumented.</summary>
    [Fact]
    public void BeginSession_PlannedCategoryToggle_RetainsNothing()
    {
        var collector = new InMemoryDiagnosticsCollector();
        collector.Configure(new DiagnosticsSettings
        {
            EnableDiagnostics = true,
            DuplicateDetectionDiagnostics = true,
            SearchAndIndexingDiagnostics = true,
            RulesAndOrganisationDiagnostics = true,
            FileOperationDiagnostics = true,
            PerformanceDiagnostics = true,
        });

        foreach (var category in DiagnosticCategoryRegistry.All.Where(item => !item.IsInstrumented))
        {
            Assert.False(collector.IsCategoryEnabled(category.Category));
            Assert.Null(collector.BeginSession(category.Category, "Unsupported"));
        }

        Assert.Empty(collector.GetRecent());
    }

    /// <summary>Verifies immutable events are ordered and selected or complete history can be cleared.</summary>
    [Fact]
    public void Publish_OrdersEventsAndSupportsClearing()
    {
        var collector = Enabled();
        var first = collector.BeginSession(DiagnosticCategory.Ai, "First")!;
        collector.Publish(
            first,
            "One",
            DiagnosticStatus.Active,
            DiagnosticSeverity.Information,
            DiagnosticSection.Inputs,
            "First event");
        collector.Publish(
            first,
            "Two",
            DiagnosticStatus.Succeeded,
            DiagnosticSeverity.Information,
            DiagnosticSection.Outputs,
            "Second event");
        var second = collector.BeginSession(DiagnosticCategory.Scanning, "Second")!;

        var session = collector.Get(first)!;
        Assert.True(session.Events[0].Sequence < session.Events[1].Sequence);
        Assert.Equal(DiagnosticStatus.Active, session.Status);
        collector.Clear(first);
        Assert.Null(collector.Get(first));
        Assert.NotNull(collector.Get(second));
        collector.ClearAll();
        Assert.Empty(collector.GetRecent());
    }

    /// <summary>Related-session identities are stable, bounded, de-duplicated, and never include self-links.</summary>
    [Fact]
    public void Relate_BoundsAndDeduplicatesSessionIdentities()
    {
        var collector = Enabled();
        var sessionId = collector.BeginSession(DiagnosticCategory.Ai, "Generate")!;
        var related = Enumerable.Range(0, 20)
            .Select(index => $"ocr:{index:D2}")
            .ToArray();

        collector.Relate(
            sessionId,
            [sessionId, related[0], related[0], .. related]);

        var session = Assert.Single(collector.GetRecent());
        Assert.Equal(16, session.RelatedSessionIds.Count);
        Assert.Equal(16, session.RelatedSessionIds.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(sessionId, session.RelatedSessionIds);
        Assert.Equal(related.Take(16), session.RelatedSessionIds);
        var exported = new DiagnosticsExportService().ExportJson(session);
        Assert.Contains(related[0], exported, StringComparison.Ordinal);
    }

    /// <summary>Verifies terminal completion is idempotent and later feature events cannot mutate history.</summary>
    [Fact]
    public void Complete_RepeatedOrFollowedByPublish_PreservesFirstTerminalSnapshot()
    {
        var collector = Enabled();
        var id = collector.BeginSession(DiagnosticCategory.Ai, "One request")!;
        collector.Complete(id, DiagnosticStatus.Succeeded, TimeSpan.FromSeconds(1), "First completion");
        var first = collector.Get(id)!;

        collector.Complete(id, DiagnosticStatus.Failed, TimeSpan.FromSeconds(9), "Second completion");
        collector.Publish(
            id,
            "Late event",
            DiagnosticStatus.Failed,
            DiagnosticSeverity.Error,
            DiagnosticSection.WarningsAndErrors,
            "Must not be retained");

        var retained = collector.Get(id)!;
        Assert.Equal(first, retained);
        Assert.Equal(DiagnosticStatus.Succeeded, retained.Status);
        Assert.Single(retained.Events, item => item.Stage == "Completed");
        Assert.DoesNotContain(retained.Events, item => item.Stage == "Late event");
    }

    /// <summary>Verifies per-category history remains bounded to the documented limit.</summary>
    [Fact]
    public void BeginSession_HistoryIsBoundedPerCategory()
    {
        var collector = Enabled();
        for (var index = 0; index < DiagnosticLimits.MaximumRetainedSessionsPerCategory + 4; index++)
        {
            collector.BeginSession(DiagnosticCategory.Ai, $"Request {index}");
        }

        var sessions = collector.GetRecent();
        Assert.Equal(DiagnosticLimits.MaximumRetainedSessionsPerCategory, sessions.Count);
        Assert.Equal("Request 23", sessions[0].Operation);
        Assert.Equal("Request 4", sessions[^1].Operation);
    }

    /// <summary>Verifies concurrent producers retain unique bounded sessions without corrupting events.</summary>
    [Fact]
    public void SimultaneousSessions_RemainUniqueAndBounded()
    {
        var collector = Enabled();

        Parallel.For(0, 80, index =>
        {
            var id = collector.BeginSession(DiagnosticCategory.Scanning, $"Scan {index}");
            collector.Publish(
                id,
                "Progress",
                DiagnosticStatus.Active,
                DiagnosticSeverity.Information,
                DiagnosticSection.Performance,
                "Bounded progress");
            collector.Complete(id, DiagnosticStatus.Succeeded, TimeSpan.FromMilliseconds(index), "Done");
        });

        var retained = collector.GetRecent();
        Assert.Equal(DiagnosticLimits.MaximumRetainedSessionsPerCategory, retained.Count);
        Assert.Equal(retained.Count, retained.Select(item => item.SessionId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(retained, item =>
        {
            Assert.Equal(DiagnosticStatus.Succeeded, item.Status);
            Assert.True(item.Events.Zip(item.Events.Skip(1), (first, second) =>
                first.Sequence < second.Sequence).All(value => value));
        });
    }

    /// <summary>Verifies default redaction, explicit unredacted retention, and unconditional secret removal.</summary>
    [Fact]
    public void Retention_RedactsByClassificationAndNeverRetainsSecrets()
    {
        var collector = Enabled(showUnredacted: false);
        var redacted = collector.BeginSession(
            DiagnosticCategory.OcrAndTextExtraction,
            "Extract",
            [
                new DiagnosticField("Path", @"C:\Private\tax.pdf", DiagnosticDataClassification.Path),
                new DiagnosticField("Text", "private document text", DiagnosticDataClassification.Content),
                new DiagnosticField("Authorization", "Bearer top-secret", DiagnosticDataClassification.Secret),
            ])!;

        var redactedContext = collector.Get(redacted)!.Context;
        Assert.DoesNotContain("tax.pdf", redactedContext.Single(item => item.Name == "Path").Value, StringComparison.Ordinal);
        Assert.Equal("[REDACTED_CONTENT]", redactedContext.Single(item => item.Name == "Text").Value);
        Assert.DoesNotContain("top-secret", redactedContext.Single(item => item.Name == "Authorization").Value, StringComparison.Ordinal);

        collector.Configure(new DiagnosticsSettings
        {
            EnableDiagnostics = true,
            OcrAndTextExtractionDiagnostics = true,
            ShowUnredactedDiagnosticContent = true,
        });
        var exact = collector.BeginSession(
            DiagnosticCategory.OcrAndTextExtraction,
            "Extract",
            [
                new DiagnosticField("Path", @"C:\Private\tax.pdf", DiagnosticDataClassification.Path),
                new DiagnosticField("Text", "private document text", DiagnosticDataClassification.Content),
                new DiagnosticField("Payload", """{"password":"top-secret"}""", DiagnosticDataClassification.Content),
                new DiagnosticField("Authorization header", "Bearer multi word secret"),
                new DiagnosticField("Free-form value", "Bearer abcdefghijklmnopqrstuvwxyz", DiagnosticDataClassification.Content),
                new DiagnosticField("Token value", "github_pat_abcdefghijklmnopqrstuvwxyz123456", DiagnosticDataClassification.Content),
            ])!;
        var exactContext = collector.Get(exact)!.Context;
        Assert.Equal(@"C:\Private\tax.pdf", exactContext.Single(item => item.Name == "Path").Value);
        Assert.Equal("private document text", exactContext.Single(item => item.Name == "Text").Value);
        Assert.DoesNotContain("top-secret", exactContext.Single(item => item.Name == "Payload").Value, StringComparison.Ordinal);
        Assert.Equal(
            "[REDACTED_SECRET]",
            exactContext.Single(item => item.Name == "Authorization header").Value);
        Assert.DoesNotContain(
            "abcdefghijklmnopqrstuvwxyz",
            exactContext.Single(item => item.Name == "Free-form value").Value,
            StringComparison.Ordinal);
        Assert.Equal(
            "[REDACTED_SECRET]",
            exactContext.Single(item => item.Name == "Token value").Value);
    }

    /// <summary>Verifies a failing custom redactor cannot expose the source value or fail the operation.</summary>
    [Fact]
    public void Retention_RedactorFailure_OmitsValueAndContinues()
    {
        var collector = new InMemoryDiagnosticsCollector(new ThrowingRedactor());
        collector.Configure(new DiagnosticsSettings
        {
            EnableDiagnostics = true,
            AiDiagnostics = true,
            ShowUnredactedDiagnosticContent = true,
        });

        var id = collector.BeginSession(
            DiagnosticCategory.Ai,
            "Request",
            [new DiagnosticField("Prompt", "private source value", DiagnosticDataClassification.Content)])!;

        var field = Assert.Single(collector.Get(id)!.Context);
        Assert.Equal("Diagnostic field omitted", field.Name);
        Assert.Equal("[NOT RETAINED: REDACTION FAILED]", field.Value);
        Assert.DoesNotContain("private source value", field.Value, StringComparison.Ordinal);
    }

    /// <summary>Verifies text bounds are visible and manual JSON/text exports contain retained data.</summary>
    [Fact]
    public void Export_UsesBoundedRetainedSnapshots()
    {
        var collector = Enabled(showUnredacted: true);
        var id = collector.BeginSession(DiagnosticCategory.Ai, "Generate")!;
        collector.Publish(
            id,
            "Response",
            DiagnosticStatus.Succeeded,
            DiagnosticSeverity.Information,
            DiagnosticSection.Outputs,
            "Response captured",
            [new DiagnosticField("Text", new string('x', DiagnosticLimits.MaximumTextCharacters + 100), DiagnosticDataClassification.Content)]);
        collector.Complete(id, DiagnosticStatus.Succeeded, TimeSpan.FromSeconds(1), "Completed");
        var session = collector.Get(id)!;
        var exporter = new DiagnosticsExportService();

        Assert.True(session.WasTruncated);
        Assert.Contains(DiagnosticLimits.TruncationMarker, exporter.ExportText(session), StringComparison.Ordinal);
        Assert.Contains($"\"SessionId\": \"{id}\"", exporter.ExportJson(session), StringComparison.Ordinal);
        Assert.Contains("\"ContentRetentionMode\": \"UnredactedSecretsRemoved\"", exporter.ExportJson(session), StringComparison.Ordinal);
        Assert.Contains("Unredacted content retained by explicit opt-in", exporter.ExportText(session), StringComparison.Ordinal);
        Assert.Contains(id, exporter.ExportAllJson([session]), StringComparison.Ordinal);
        Assert.Contains("OpenSorSe Advanced Diagnostic", exporter.ExportAllText([session]), StringComparison.Ordinal);
    }

    /// <summary>Verifies a context-only truncation is explicitly visible on the retained session.</summary>
    [Fact]
    public void BeginSession_ContextBound_SetsTruncationState()
    {
        var collector = Enabled(showUnredacted: true);
        var id = collector.BeginSession(
            DiagnosticCategory.Ai,
            "Bounded context",
            [new DiagnosticField(
                "Prompt",
                new string('p', DiagnosticLimits.MaximumTextCharacters + 10),
                DiagnosticDataClassification.Content)])!;

        Assert.True(collector.Get(id)!.WasTruncated);
    }

    /// <summary>Verifies the event limit is exact and omitted events are counted.</summary>
    [Fact]
    public void Publish_EventLimit_DropsAdditionalEvents()
    {
        var collector = Enabled();
        var id = collector.BeginSession(DiagnosticCategory.Scanning, "Large scan")!;
        for (var index = 0; index < DiagnosticLimits.MaximumEventsPerSession + 7; index++)
        {
            collector.Publish(
                id,
                $"Event {index}",
                DiagnosticStatus.Active,
                DiagnosticSeverity.Information,
                DiagnosticSection.IntermediateResults,
                "Entry");
        }

        var retained = collector.Get(id)!;
        Assert.Equal(DiagnosticLimits.MaximumEventsPerSession, retained.Events.Count);
        Assert.Equal(7, retained.DroppedEventCount);
        Assert.True(retained.WasTruncated);
    }

    /// <summary>Verifies aggregate field and per-session memory bounds cannot be bypassed by many large values.</summary>
    [Fact]
    public void Retention_EnforcesAggregateAndPerSessionMemoryBounds()
    {
        var collector = Enabled(showUnredacted: true);
        var id = collector.BeginSession(
            DiagnosticCategory.Ai,
            "Large request",
            Enumerable.Range(0, 4)
                .Select(index => new DiagnosticField(
                    $"Context {index}",
                    new string((char)('a' + index), DiagnosticLimits.MaximumTextCharacters),
                    DiagnosticDataClassification.Content))
                .ToArray())!;
        for (var index = 0; index < 12; index++)
        {
            collector.Publish(
                id,
                $"Large event {index}",
                DiagnosticStatus.Active,
                DiagnosticSeverity.Information,
                DiagnosticSection.IntermediateResults,
                "Bounded large value",
                [new DiagnosticField(
                    "Content",
                    new string('x', DiagnosticLimits.MaximumTextCharacters),
                    DiagnosticDataClassification.Content)]);
        }

        var retained = collector.Get(id)!;

        Assert.True(retained.WasTruncated);
        Assert.True(
            retained.ApproximateRetainedBytes <=
            DiagnosticLimits.MaximumApproximateRetainedBytesPerSession);
        Assert.True(
            retained.Context.Sum(field => field.Name.Length + field.Value.Length) <=
            DiagnosticLimits.MaximumTextCharactersPerEvent);
        Assert.True(retained.DroppedEventCount > 0);
    }

    /// <summary>Verifies privacy-mode downgrade clears exact content and clear observers are isolated.</summary>
    [Fact]
    public void Configure_DisablingUnredactedRetentionClearsHistoryAndIsolatesObservers()
    {
        var collector = Enabled(showUnredacted: true);
        var clearNotifications = 0;
        collector.SessionsCleared += (_, _) => clearNotifications++;
        collector.SessionsCleared += (_, _) => throw new InvalidOperationException("Observer failure");
        collector.BeginSession(
            DiagnosticCategory.Ai,
            "Sensitive request",
            [new DiagnosticField("Prompt", "exact private prompt", DiagnosticDataClassification.Content)]);

        collector.Configure(new DiagnosticsSettings
        {
            EnableDiagnostics = true,
            AiDiagnostics = true,
            ShowUnredactedDiagnosticContent = false,
        });

        Assert.Empty(collector.GetRecent());
        Assert.Equal(1, clearNotifications);
        Assert.NotNull(collector.BeginSession(DiagnosticCategory.Ai, "Redacted request"));
    }

    /// <summary>Verifies observer failures never escape and disabling performs process-session cleanup.</summary>
    [Fact]
    public void ObserversAndCleanup_AreFailureIsolated()
    {
        var collector = Enabled();
        collector.SessionChanged += (_, _) => throw new InvalidOperationException("Observer failure");

        var id = collector.BeginSession(DiagnosticCategory.Scanning, "Scan");
        collector.Publish(
            id,
            "Progress",
            DiagnosticStatus.Active,
            DiagnosticSeverity.Information,
            DiagnosticSection.Performance,
            "Progress");
        Assert.Single(collector.GetRecent());

        collector.Configure(new DiagnosticsSettings());
        Assert.Empty(collector.GetRecent());
        Assert.Null(collector.BeginSession(DiagnosticCategory.Scanning, "After exit cleanup"));
    }

    private static InMemoryDiagnosticsCollector Enabled(bool showUnredacted = false)
    {
        var collector = new InMemoryDiagnosticsCollector();
        collector.Configure(new DiagnosticsSettings
        {
            EnableDiagnostics = true,
            AiDiagnostics = true,
            OcrAndTextExtractionDiagnostics = true,
            ScanningDiagnostics = true,
            ShowUnredactedDiagnosticContent = showUnredacted,
        });
        return collector;
    }

    private sealed class ThrowingRedactor : IDiagnosticsRedactor
    {
        public DiagnosticField Redact(
            DiagnosticCategory category,
            DiagnosticField field,
            bool showUnredactedContent) =>
            throw new InvalidOperationException("Simulated redactor failure.");
    }
}
