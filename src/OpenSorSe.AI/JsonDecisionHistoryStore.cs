using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenSorSe.Application.AI;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Persistence;

namespace OpenSorSe.AI;

/// <summary>
/// Stores bounded local preference decisions in a versioned JSON file outside scan results and user folders.
/// </summary>
public sealed class JsonDecisionHistoryStore : IDecisionHistoryStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _historyFilePath;
    private readonly ApplicationFileAccessCoordinator _fileAccess;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    /// <summary>
    /// Initializes local decision-history persistence at an explicit application-data path.
    /// </summary>
    /// <param name="historyFilePath">The rooted JSON history path owned by OpenSorSe.</param>
    /// <param name="loggingService">The central redacted diagnostic logging service.</param>
    public JsonDecisionHistoryStore(string historyFilePath, ILoggingService loggingService)
    {
        if (string.IsNullOrWhiteSpace(historyFilePath) || !Path.IsPathRooted(historyFilePath))
        {
            throw new ArgumentException("An absolute decision-history path is required.", nameof(historyFilePath));
        }

        _historyFilePath = historyFilePath;
        _fileAccess = new ApplicationFileAccessCoordinator(historyFilePath);
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService))).CreateLogger(nameof(JsonDecisionHistoryStore));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiSuggestionDecision>> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task AppendAsync(AiSuggestionDecision decision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var sanitized = Sanitize(decision);
        cancellationToken.ThrowIfCancellationRequested();
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var decisions = (await LoadCoreAsync(cancellationToken).ConfigureAwait(false)).ToList();
            decisions.Add(sanitized);
            if (decisions.Count > AiDecisionHistoryLimits.MaximumDecisionCount)
            {
                decisions = decisions
                    .OrderBy(item => item.RecordedAtUtc)
                    .TakeLast(AiDecisionHistoryLimits.MaximumDecisionCount)
                    .ToList();
            }

            await SaveCoreAsync(decisions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_historyFilePath))
            {
                File.Delete(_historyFilePath);
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<IReadOnlyList<AiSuggestionDecision>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_historyFilePath))
        {
            return Array.Empty<AiSuggestionDecision>();
        }

        try
        {
            if (new FileInfo(_historyFilePath).Length > AiDecisionHistoryLimits.MaximumHistoryFileBytes)
            {
                throw new InvalidDataException("The local AI decision history exceeds its supported size.");
            }

            await using var stream = File.OpenRead(_historyFilePath);
            var envelope = await JsonSerializer.DeserializeAsync<DecisionHistoryEnvelope>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (envelope is null || envelope.SchemaVersion != CurrentSchemaVersion || envelope.Decisions is null ||
                envelope.Decisions.Count > AiDecisionHistoryLimits.MaximumDecisionCount ||
                envelope.Decisions.Any(decision => decision is null))
            {
                throw new InvalidDataException("The local AI decision history has an unsupported format.");
            }

            return Array.AsReadOnly(envelope.Decisions
                .Select(Sanitize)
                .OrderBy(item => item.RecordedAtUtc)
                .ToArray());
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "The local AI decision history is malformed and will not be used.");
            throw new InvalidDataException("The local AI decision history is malformed.", exception);
        }
        catch (InvalidDataException exception)
        {
            _logger.LogWarning(exception, "The local AI decision history is invalid and will not be used.");
            throw;
        }
    }

    private async Task SaveCoreAsync(IReadOnlyList<AiSuggestionDecision> decisions, CancellationToken cancellationToken)
    {
        await AtomicJsonFile.WriteAsync(
            _historyFilePath,
            new DecisionHistoryEnvelope(CurrentSchemaVersion, decisions.ToArray()),
            JsonOptions,
            AiDecisionHistoryLimits.MaximumHistoryFileBytes,
            cancellationToken,
            static (_, _) => new InvalidDataException(
                "The local AI decision history exceeds its supported size.")).ConfigureAwait(false);
    }

    private static AiSuggestionDecision Sanitize(AiSuggestionDecision decision)
    {
        if (!Enum.IsDefined(decision.Kind) || !Enum.IsDefined(decision.Outcome) ||
            decision.RecordedAtUtc.Offset != TimeSpan.Zero ||
            !IsOptionalValueValid(decision.Extension, AiDecisionHistoryLimits.MaximumExtensionLength) ||
            !IsRequiredValueValid(decision.SuggestedValue, AiDecisionHistoryLimits.MaximumValueLength) ||
            !IsOptionalValueValid(decision.FinalValue, AiDecisionHistoryLimits.MaximumValueLength) ||
            !IsRequiredValueValid(decision.Provider, AiDecisionHistoryLimits.MaximumProviderIdentifierLength) ||
            !IsRequiredValueValid(decision.Model, AiDecisionHistoryLimits.MaximumProviderIdentifierLength))
        {
            throw new InvalidDataException("The local AI decision history contains an invalid decision.");
        }

        return decision with
        {
            Extension = string.IsNullOrWhiteSpace(decision.Extension) ? null : decision.Extension.Trim(),
            SuggestedValue = decision.SuggestedValue.Trim(),
            FinalValue = string.IsNullOrWhiteSpace(decision.FinalValue) ? null : decision.FinalValue.Trim(),
            Provider = decision.Provider.Trim(),
            Model = decision.Model.Trim(),
        };
    }

    private static bool IsRequiredValueValid(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximumLength && !value.Any(char.IsControl);

    private static bool IsOptionalValueValid(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length <= maximumLength && !value.Any(char.IsControl);

    private sealed record DecisionHistoryEnvelope(int SchemaVersion, IReadOnlyList<AiSuggestionDecision>? Decisions);
}
