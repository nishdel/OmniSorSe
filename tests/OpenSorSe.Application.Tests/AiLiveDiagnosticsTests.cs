using OpenSorSe.Application.AI;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies opt-in live, bounded, privacy-aware diagnostics.</summary>
public sealed class AiLiveDiagnosticsTests
{
    /// <summary>Disabled diagnostics neither create nor expose a session.</summary>
    [Fact]
    public void Begin_WhenDisabled_DoesNotCreateSession()
    {
        var collector = new AiDiagnosticsCollector();
        var events = 0;
        collector.SessionChanged += (_, _) => events++;

        var id = collector.Begin(AiSuggestionKind.FileRename, "model", "http://127.0.0.1:11434");

        Assert.Null(id);
        Assert.Empty(collector.GetRecent());
        Assert.Equal(0, events);
    }

    /// <summary>Enabled sessions update live, preserve order, and observer failures are isolated.</summary>
    [Fact]
    public void EnabledSession_PublishesOrderedStagesAndSurvivesObserverFailure()
    {
        var collector = new AiDiagnosticsCollector();
        collector.Configure(true, true);
        collector.SessionChanged += (_, _) => throw new InvalidOperationException("Observer failure");
        var id = collector.Begin(AiSuggestionKind.FileRename, "model", "http://127.0.0.1:11434");

        collector.ReportStage(id, "Building system prompt", AiDiagnosticState.Succeeded, TimeSpan.FromMilliseconds(1));
        collector.ReportStage(id, "Request sent", AiDiagnosticState.Succeeded, TimeSpan.FromMilliseconds(2));
        collector.Complete(id, AiDiagnosticState.Succeeded, false, TimeSpan.FromMilliseconds(3));

        var session = Assert.Single(collector.GetRecent());
        Assert.Equal(
            ["Building system prompt", "Request sent"],
            session.Stages.Where(stage => stage.State != AiDiagnosticState.Pending).Select(stage => stage.Name));
        Assert.Equal(AiDiagnosticState.Succeeded, session.Status);
    }

    /// <summary>Default mode redacts content while explicit unredacted mode retains exact values.</summary>
    [Fact]
    public void Capture_RespectsRedactedAndUnredactedModes()
    {
        const string sensitive = """{"fileName":"tax-return.pdf","relativePath":"Finance/2025"}""";
        var collector = new AiDiagnosticsCollector();
        collector.Configure(true, false);
        var redactedId = collector.Begin(AiSuggestionKind.FileRename, "model", "http://127.0.0.1:11434")!;
        collector.Capture(redactedId, AiDiagnosticContentKind.UserPrompt, sensitive);
        Assert.DoesNotContain("tax-return.pdf", collector.GetRecent()[0].UserPrompt, StringComparison.Ordinal);

        collector.Configure(true, true);
        var exactId = collector.Begin(AiSuggestionKind.FileRename, "model", "http://127.0.0.1:11434")!;
        collector.Capture(exactId, AiDiagnosticContentKind.UserPrompt, sensitive);
        Assert.Equal(sensitive, collector.GetRecent()[0].UserPrompt);
    }

    /// <summary>History is bounded to 20 and can be cleared individually or entirely.</summary>
    [Fact]
    public void History_IsBoundedAndClearable()
    {
        var collector = new AiDiagnosticsCollector();
        collector.Configure(true, false);
        for (var index = 0; index < 23; index++)
            collector.Begin(AiSuggestionKind.FolderStructure, $"model-{index}", "http://127.0.0.1:11434");

        Assert.Equal(AiRequestDiagnosticLimits.MaximumRetainedRequests, collector.GetRecent().Count);
        var first = collector.GetRecent()[0].RequestId;
        collector.Clear(first);
        Assert.Equal(19, collector.GetRecent().Count);
        collector.Configure(false, false);
        Assert.Empty(collector.GetRecent());
    }

    /// <summary>Invalid reason types produce the precise diagnostic required for model troubleshooting.</summary>
    [Fact]
    public void Inspect_InvalidReasonObject_ReportsActualType()
    {
        var inspected = AiDiagnosticValidationInspector.Inspect(
            """{"taskId":"file-rename-v2","status":"suggestion","reason":{"text":"clearer"}}""",
            AiPromptBuilder.FileRenameTaskId);

        var reason = Assert.Single(inspected.Checks, check => check.PropertyName == "reason");
        Assert.False(reason.Passed);
        Assert.Contains("received object", reason.Message, StringComparison.Ordinal);
    }

    /// <summary>Safe validation explanations remain visible in redacted mode while actual model values do not.</summary>
    [Fact]
    public void SetValidation_RedactedMode_KeepsSafeFailureExplanation()
    {
        var collector = new AiDiagnosticsCollector();
        collector.Configure(true, false);
        var id = collector.Begin(
            AiSuggestionKind.FileRename,
            "model",
            "http://127.0.0.1:11434")!;
        collector.SetValidation(
            id,
            """{"suggestedStem":"private-invoice"}""",
            [
                new AiDiagnosticValidation(
                    "reason",
                    true,
                    "string",
                    null,
                    "object",
                    """{"private":"content"}""",
                    false,
                    "Expected `reason` to be a string, but received object."),
            ],
            ["Expected `reason` to be a string, but received object."]);

        var session = Assert.Single(collector.GetRecent());
        Assert.Contains(
            session.Errors,
            value => value.Contains("received object", StringComparison.Ordinal));
        Assert.DoesNotContain("private-invoice", session.ParsedStructuredResponse, StringComparison.Ordinal);
        Assert.DoesNotContain(
            session.Validation,
            value => value.ActualValue.Contains("private", StringComparison.Ordinal));
    }

    /// <summary>Prompt task, template, and schema identities remain visible without exposing content.</summary>
    [Fact]
    public void SetContract_RetainsSafeVersionMetadataInRedactedMode()
    {
        var common = new OpenSorSe.Core.Diagnostics.InMemoryDiagnosticsCollector();
        var collector = new AiDiagnosticsCollector(common);
        collector.Configure(true, false);
        var id = collector.Begin(
            AiSuggestionKind.FileRename,
            "model",
            "http://127.0.0.1:11434")!;

        collector.SetContract(
            id,
            AiPromptBuilder.FileRenameTaskId,
            AiPromptTemplates.FileRenamePromptVersion,
            AiStructuredOutputContracts.GetSchemaSha256(AiSuggestionKind.FileRename));

        var contract = Assert.Single(
            common.Get(id)!.Events,
            item => item.Stage == "Prompt contract");
        Assert.Equal(
            AiPromptBuilder.FileRenameTaskId,
            contract.Fields.Single(field => field.Name == "Task ID").Value);
        Assert.Equal(
            AiPromptTemplates.FileRenamePromptVersion,
            contract.Fields.Single(field => field.Name == "Prompt version").Value);
        Assert.Equal(
            64,
            contract.Fields.Single(field => field.Name == "Schema SHA-256").Value.Length);
    }
}
