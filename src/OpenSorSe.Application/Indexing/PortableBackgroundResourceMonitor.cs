using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Indexing;

/// <summary>
/// Applies portable time-window policy and degrades unsupported idle, power, and battery signals gracefully.
/// </summary>
public sealed class PortableBackgroundResourceMonitor : IBackgroundResourceMonitor
{
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a portable monitor with an optional controllable clock.</summary>
    public PortableBackgroundResourceMonitor(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<BackgroundResourceEligibility> GetEligibilityAsync(
        DeepIndexingSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        var unsupportedPolicies = new List<string>(3);
        if (settings.ProcessOnlyWhileIdle)
        {
            unsupportedPolicies.Add("idle detection");
        }

        if (settings.ProcessOnlyWhileConnectedToPower)
        {
            unsupportedPolicies.Add("power-source detection");
        }

        if (settings.PauseBelowBatteryPercentage.HasValue)
        {
            unsupportedPolicies.Add("battery-level detection");
        }

        if (unsupportedPolicies.Count > 0)
        {
            return Task.FromResult(new BackgroundResourceEligibility(
                false,
                string.Concat(
                    "Waiting for resource policy because this host cannot provide ",
                    string.Join(", ", unsupportedPolicies),
                    ". Disable that restriction or use a host integration that supports it.")));
        }

        if (settings.ProcessingWindowStartHour is not { } start ||
            settings.ProcessingWindowEndHour is not { } end)
        {
            return Task.FromResult(new BackgroundResourceEligibility(true, null));
        }

        var hour = _timeProvider.GetLocalNow().Hour;
        var within = start < end
            ? hour >= start && hour < end
            : hour >= start || hour < end;
        return Task.FromResult(new BackgroundResourceEligibility(
            within,
            within ? null : "Waiting for the configured background-processing time window."));
    }
}
