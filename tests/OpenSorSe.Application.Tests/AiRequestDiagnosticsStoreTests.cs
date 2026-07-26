using OpenSorSe.Application.AI;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies opt-in, bounded, session-only AI request diagnostics.</summary>
public sealed class AiRequestDiagnosticsStoreTests
{
    /// <summary>Verifies disabled capture retains nothing and disabling clears prior raw records.</summary>
    [Fact]
    public void Record_Disabled_DropsDataAndDisableClearsEnabledHistory()
    {
        var store = new AiRequestDiagnosticsStore();
        store.Record(Create("ignored"));
        Assert.Empty(store.GetRecent());

        store.SetEnabled(true);
        store.Record(Create("kept"));
        Assert.Single(store.GetRecent());

        store.SetEnabled(false);
        Assert.Empty(store.GetRecent());
        Assert.False(store.IsEnabled);
    }

    /// <summary>Verifies newest-first retention, the 20-record cap, and credential redaction.</summary>
    [Fact]
    public void Record_Enabled_RetainsNewestTwentyAndRedactsSecrets()
    {
        var store = new AiRequestDiagnosticsStore();
        store.SetEnabled(true);
        for (var index = 0; index < AiRequestDiagnosticLimits.MaximumRetainedRequests + 3; index++)
        {
            store.Record(Create($"request-{index:D2}") with
            {
                Prompt = """{"apiKey":"top-secret","authorization":"Bearer abc"}""",
                Response = "password=hunter2",
            });
        }

        var recent = store.GetRecent();
        Assert.Equal(AiRequestDiagnosticLimits.MaximumRetainedRequests, recent.Count);
        Assert.Equal("request-22", recent[0].RequestId);
        Assert.Equal("request-03", recent[^1].RequestId);
        Assert.DoesNotContain("top-secret", recent[0].Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("abc", recent[0].Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", recent[0].Response, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", recent[0].Prompt, StringComparison.Ordinal);
    }

    private static AiRequestDiagnostic Create(string id) => new(
        id,
        DateTimeOffset.UnixEpoch,
        AiSuggestionKind.FileRename,
        "http://127.0.0.1:11434",
        "model",
        30,
        [],
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch.AddSeconds(1),
        TimeSpan.FromSeconds(1),
        "Completed",
        200,
        AiProviderFailureKind.None,
        10,
        10,
        10,
        10,
        "Accepted",
        [],
        1,
        1,
        0,
        "{}",
        "{}");
}
