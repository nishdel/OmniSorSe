using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.Storage;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Presents active storage measurements and conservative cleanup in Settings.</summary>
public sealed class StorageManagementViewModel : ViewModelBase
{
    private readonly IApplicationStorageService _service;
    private string _summaryText = "Refresh to measure active application storage.";
    private IReadOnlyList<StorageUsageRow> _usageRows = [];
    private bool _isBusy;

    /// <summary>Creates storage management using the active path and persistence authorities.</summary>
    public StorageManagementViewModel(IApplicationStorageService service, IApplicationPathProvider paths)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        ArgumentNullException.ThrowIfNull(paths);
        ActiveDataDirectory = paths.Paths.DataDirectory;
        ActiveCacheDirectory = paths.Paths.CacheDirectory;
        PreservedStateDirectory = paths.Paths.StateDirectory;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        ReclaimCommand = new AsyncRelayCommand(ReclaimAsync, () => !IsBusy);
    }

    /// <summary>Gets the active data directory; a saved location change applies only after restart.</summary>
    public string ActiveDataDirectory { get; }

    /// <summary>Gets the active cache directory.</summary>
    public string ActiveCacheDirectory { get; }

    /// <summary>Gets the unchanged profile state location containing operation history.</summary>
    public string PreservedStateDirectory { get; }

    /// <summary>Gets measurement, cleanup or failure status.</summary>
    public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }

    /// <summary>Gets the physical storage categories and separately labeled logical index details.</summary>
    public IReadOnlyList<StorageUsageRow> UsageRows { get => _usageRows; private set => SetProperty(ref _usageRows, value); }

    /// <summary>Gets whether storage work is active.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
                ReclaimCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets the refresh action.</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>Gets the explicit rebuildable-cache reclamation action.</summary>
    public IAsyncRelayCommand ReclaimCommand { get; }

    private async Task RefreshAsync() => await RunAsync(async () =>
    {
        var usage = await _service.GetUsageAsync().ConfigureAwait(true);
        Show(usage);
        SummaryText = $"Active storage: {Format(usage.TotalBytes)}. Rebuildable caches: {Format(usage.CacheBytes)} / {Format(usage.MaximumCacheBytes)}. Database detail rows are already included in the total. Retained copies from earlier storage locations are additional recovery data.";
    }).ConfigureAwait(true);

    private async Task ReclaimAsync() => await RunAsync(async () =>
    {
        var result = await _service.ReclaimAsync().ConfigureAwait(true);
        Show(result.Usage);
        SummaryText = $"Reclaimed {Format(result.ReclaimedBytes)}. {result.Message}";
    }).ConfigureAwait(true);

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SummaryText = $"Storage maintenance could not complete ({exception.GetType().Name}). Durable state was not removed. Check the storage location and retry.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Show(ApplicationStorageUsage usage) => UsageRows = usage.Categories
        .Select(row => new StorageUsageRow(row.Name, Format(row.Bytes), row.IncludedInTotal ? "Physical storage" : "Included in library total"))
        .ToArray();

    private static string Format(long bytes) => bytes >= 1024L * 1024L * 1024L
        ? $"{bytes / (1024d * 1024d * 1024d):F2} GiB"
        : $"{bytes / (1024d * 1024d):F2} MiB";
}

/// <summary>Contains one accessible storage measurement row.</summary>
public sealed record StorageUsageRow(string Name, string SizeText, string Detail);
