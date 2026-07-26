#pragma warning disable CS1591

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Logging;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Executor;

/// <summary>Persists bounded Change Plans in an atomic versioned JSON envelope.</summary>
public sealed class JsonChangePlanStore : IChangePlanStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };
    private readonly string _filePath;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public JsonChangePlanStore(string filePath, ILoggingService loggingService)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A Change Plan store path is required.", nameof(filePath));
        }

        _filePath = Path.GetFullPath(filePath);
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(JsonChangePlanStore));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChangePlan>> ListAsync(CancellationToken cancellationToken)
    {
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
    public async Task<ChangePlan?> GetAsync(string planId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planId))
        {
            return null;
        }

        var plans = await ListAsync(cancellationToken).ConfigureAwait(false);
        return plans.FirstOrDefault(plan => string.Equals(plan.PlanId, planId, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public async Task UpsertAsync(ChangePlan plan, CancellationToken cancellationToken)
    {
        Validate(plan);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var plans = (await LoadCoreAsync(cancellationToken).ConfigureAwait(false)).ToList();
            plans.RemoveAll(candidate => string.Equals(candidate.PlanId, plan.PlanId, StringComparison.Ordinal));
            plans.Add(plan);
            var retained = plans
                .OrderByDescending(candidate => candidate.CreatedAtUtc)
                .ThenBy(candidate => candidate.PlanId, StringComparer.Ordinal)
                .Take(ChangePlanSchema.MaximumStoredPlans)
                .ToArray();
            await SaveCoreAsync(retained, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<IReadOnlyList<ChangePlan>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            if (new FileInfo(_filePath).Length > ChangePlanSchema.MaximumStoreFileBytes)
            {
                throw new InvalidDataException("The Change Plan store exceeds its supported size.");
            }

            await using var stream = File.OpenRead(_filePath);
            var envelope = await JsonSerializer.DeserializeAsync<Envelope>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (envelope is null ||
                envelope.SchemaVersion != ChangePlanSchema.CurrentVersion ||
                envelope.Plans is null ||
                envelope.Plans.Count > ChangePlanSchema.MaximumStoredPlans)
            {
                throw new InvalidDataException("The Change Plan store format is unsupported.");
            }

            foreach (var plan in envelope.Plans)
            {
                Validate(plan);
            }

            return Array.AsReadOnly(envelope.Plans
                .OrderByDescending(plan => plan.CreatedAtUtc)
                .ThenBy(plan => plan.PlanId, StringComparer.Ordinal)
                .ToArray());
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
        {
            _logger.LogWarning(exception, "The Change Plan store is malformed or unsupported.");
            return [];
        }
    }

    private async Task SaveCoreAsync(IReadOnlyList<ChangePlan> plans, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidDataException("The Change Plan store path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             new FileStreamOptions
                             {
                                 Access = FileAccess.Write,
                                 Mode = FileMode.CreateNew,
                                 Share = FileShare.None,
                                 Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                             }))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new Envelope(ChangePlanSchema.CurrentVersion, plans),
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (new FileInfo(temporaryPath).Length > ChangePlanSchema.MaximumStoreFileBytes)
            {
                throw new InvalidDataException("The Change Plan store exceeds its supported encoded size.");
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void Validate(ChangePlan plan)
    {
        if (plan is null ||
            plan.SchemaVersion != ChangePlanSchema.CurrentVersion ||
            string.IsNullOrWhiteSpace(plan.PlanId) ||
            plan.CreatedAtUtc.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(plan.RootPath) ||
            !Path.IsPathRooted(plan.RootPath) ||
            plan.RootPath.Length > ChangePlanSchema.MaximumPathLength ||
            !Enum.IsDefined(plan.Status) ||
            plan.Warnings is null ||
            plan.Warnings.Count > 1_000 ||
            plan.Warnings.Any(warning => warning is null || warning.Length > 1_024) ||
            plan.Actions is null ||
            plan.Actions.Count is < 1 or > ChangePlanSchema.MaximumActions ||
            plan.Actions.Select(action => action.ActionId).Distinct(StringComparer.Ordinal).Count() != plan.Actions.Count ||
            plan.Actions.Any(action =>
                action is null ||
                string.IsNullOrWhiteSpace(action.ActionId) ||
                action.ActionId.Length > 256 ||
                !string.Equals(action.PlanId, plan.PlanId, StringComparison.Ordinal) ||
                !Enum.IsDefined(action.ActionType) ||
                !Enum.IsDefined(action.SuggestionSource) ||
                !Enum.IsDefined(action.ValidationState) ||
                !Enum.IsDefined(action.ApprovalState) ||
                action.SourcePath is { } source &&
                    (!Path.IsPathRooted(source) || source.Length > ChangePlanSchema.MaximumPathLength) ||
                string.IsNullOrWhiteSpace(action.DestinationPath) ||
                !Path.IsPathRooted(action.DestinationPath) ||
                action.DestinationPath.Length > ChangePlanSchema.MaximumPathLength ||
                action.ExecutionOrder < 0 ||
                action.Reason is null ||
                action.Reason.Length > 1_024 ||
                action.AiModel?.Length > 256 ||
                action.AiRequestCorrelationId?.Length > 256 ||
                !IsValidProvenance(action.WorkflowProvenance) ||
                action.Warnings is null ||
                action.Warnings.Count > 1_000 ||
                action.Warnings.Any(warning => warning is null || warning.Length > 1_024) ||
                action.Conflicts is null ||
                action.Conflicts.Count > 1_000 ||
                action.Conflicts.Any(conflict =>
                    conflict is null ||
                    !Enum.IsDefined(conflict.Category) ||
                    string.IsNullOrWhiteSpace(conflict.Message) ||
                    conflict.Message.Length > 1_024)))
        {
            throw new InvalidDataException("A Change Plan record is invalid.");
        }
    }

    private static bool IsValidProvenance(ChangeWorkflowProvenance? provenance)
    {
        if (provenance is null)
        {
            return true;
        }

        return IsBounded(provenance.ProfileId, 256) &&
               IsBounded(provenance.ProfileName, 128) &&
               provenance.ProfileRevision >= 1 &&
               IsBounded(provenance.RecipeId, 256) &&
               IsBounded(provenance.RecipeName, 128) &&
               provenance.RecipeRevision >= 1 &&
               provenance.ValuesUsed is not null &&
               provenance.ValuesUsed.Count <= 64 &&
               provenance.ValuesUsed.All(pair =>
                   IsBounded(pair.Key, 128) &&
                   pair.Value is not null &&
                   pair.Value.Length <= 512 &&
                   !pair.Value.Any(char.IsControl)) &&
               IsBoundedList(provenance.EvidenceSources, 128, 256) &&
               IsBoundedList(provenance.Warnings, 128, 1_024) &&
               IsBoundedList(provenance.UnresolvedFields, 64, 128);
    }

    private static bool IsBoundedList(IReadOnlyList<string>? values, int count, int length) =>
        values is not null &&
        values.Count <= count &&
        values.All(value => IsBounded(value, length));

    private static bool IsBounded(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        !value.Any(char.IsControl);

    private sealed record Envelope(int SchemaVersion, IReadOnlyList<ChangePlan>? Plans);
}
