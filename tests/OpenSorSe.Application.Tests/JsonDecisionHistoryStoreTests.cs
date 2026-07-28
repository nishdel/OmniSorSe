using OpenSorSe.AI;
using OpenSorSe.Application.AI;
using OpenSorSe.Core.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSorSe.Application.Tests;

/// <summary>
/// Verifies local decision-history persistence without scan-result persistence or user-folder access.
/// </summary>
public sealed class JsonDecisionHistoryStoreTests
{
    /// <summary>
    /// Verifies a decision round-trips locally and reset removes the isolated history file.
    /// </summary>
    [Fact]
    public async Task AppendLoadAndClearAsync_RoundTripsAndResetsLocalHistory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"opensorse-ai-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "decision-history.json");
        var store = new JsonDecisionHistoryStore(path, new LoggingService());
        var decision = CreateDecision();

        try
        {
            await store.AppendAsync(decision, CancellationToken.None);
            var loaded = await store.LoadAsync(CancellationToken.None);
            await store.ClearAsync(CancellationToken.None);

            Assert.Equal([decision], loaded);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies malformed local persistence fails safely instead of becoming preference context.
    /// </summary>
    [Fact]
    public async Task LoadAsync_MalformedHistory_ThrowsInvalidDataException()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"opensorse-ai-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "decision-history.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, "{invalid");
        var store = new JsonDecisionHistoryStore(path, new LoggingService());

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies invalid decisions are rejected before application-owned persistence is created.
    /// </summary>
    [Fact]
    public async Task AppendAsync_InvalidDecision_RejectsWithoutCreatingHistory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"opensorse-ai-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "decision-history.json");
        var store = new JsonDecisionHistoryStore(path, new LoggingService());
        var invalid = CreateDecision() with { SuggestedValue = "invalid\nvalue" };

        await Assert.ThrowsAsync<InvalidDataException>(() => store.AppendAsync(invalid, CancellationToken.None));

        Assert.False(Directory.Exists(directory));
    }

    /// <summary>
    /// Verifies over-capacity persisted history is treated as invalid rather than loaded into memory or silently truncated.
    /// </summary>
    [Fact]
    public async Task LoadAsync_OverCapacityHistory_ThrowsAndPreservesFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"opensorse-ai-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "decision-history.json");
        Directory.CreateDirectory(directory);
        var decisions = Enumerable.Range(0, AiDecisionHistoryLimits.MaximumDecisionCount + 1)
            .Select(index => CreateDecision() with { RecordedAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(index) })
            .ToArray();
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { SchemaVersion = 1, Decisions = decisions }, options));
        var originalBytes = await File.ReadAllBytesAsync(path);
        var store = new JsonDecisionHistoryStore(path, new LoggingService());

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(CancellationToken.None));
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies separate store instances serialize concurrent append transactions without losing decisions.</summary>
    [Fact]
    public async Task AppendAsync_MultipleInstancesConcurrently_PreservesEveryDecision()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"opensorse-ai-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "decision-history.json");
        try
        {
            var decisions = Enumerable.Range(0, 32)
                .Select(index => CreateDecision() with
                {
                    SuggestedValue = $"value-{index:D2}",
                    FinalValue = $"value-{index:D2}",
                    RecordedAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(index),
                })
                .ToArray();

            await Task.WhenAll(decisions.Select(decision =>
                new JsonDecisionHistoryStore(path, new LoggingService())
                    .AppendAsync(decision, CancellationToken.None)));

            var loaded = await new JsonDecisionHistoryStore(path, new LoggingService())
                .LoadAsync(CancellationToken.None);
            Assert.Equal(decisions, loaded);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static AiSuggestionDecision CreateDecision() => new(
        AiSuggestionDecisionKind.Tags,
        AiSuggestionDecisionOutcome.Accepted,
        ".pdf",
        "finance",
        "finance",
        "Ollama",
        "model",
        DateTimeOffset.UnixEpoch);
}
