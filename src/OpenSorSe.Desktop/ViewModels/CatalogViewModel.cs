using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.Catalog;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Lists and opens application-owned saved snapshots without reading selected user files.
/// </summary>
public sealed class CatalogViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigurationService _configurationService;
    private readonly IResultsCatalogStore? _catalogStore;
    private readonly ObservableCollection<CatalogEntryRow> _entries = [];
    private CatalogEntryRow? _selectedEntry;
    private bool _isLoading;
    private string _statusText = "Local catalog storage is disabled. Enable it in Settings before a completed scan can be retained.";
    private CancellationTokenSource? _operationCancellation;
    private bool _isDisposed;
    private bool _isClearConfirmationPending;
    private string? _displayNameInput;

    /// <summary>
    /// Initializes catalog presentation state over the active configuration and optional application-owned store.
    /// </summary>
    public CatalogViewModel(IConfigurationService configurationService, IResultsCatalogStore? catalogStore)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _catalogStore = catalogStore;
        Entries = new ReadOnlyObservableCollection<CatalogEntryRow>(_entries);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsLoading);
        OpenSelectedCommand = new AsyncRelayCommand(OpenSelectedAsync, () => !IsLoading && SelectedEntry is not null && IsEnabled);
        RemoveSelectedCommand = new AsyncRelayCommand(RemoveSelectedAsync, () => !IsLoading && SelectedEntry is not null && IsEnabled);
        SaveDisplayNameCommand = new AsyncRelayCommand(SaveDisplayNameAsync, () => !IsLoading && SelectedEntry is not null && IsEnabled);
        RequestClearAllCommand = new RelayCommand(RequestClearAll, () => !IsLoading && IsEnabled && Entries.Count > 0);
        ConfirmClearAllCommand = new AsyncRelayCommand(ConfirmClearAllAsync, () => !IsLoading && IsEnabled && IsClearConfirmationPending);
        CancelClearAllCommand = new RelayCommand(CancelClearAll, () => IsClearConfirmationPending);
    }

    /// <summary>Raised after an entry has been loaded safely and is ready for shell-owned result presentation.</summary>
    public event EventHandler<CatalogEntry>? EntryOpened;

    /// <summary>Raised after explicit catalog maintenance changes the available application-owned entries.</summary>
    public event EventHandler? CatalogChanged;

    /// <summary>Gets persisted catalog entry summaries in newest-first order.</summary>
    public ReadOnlyObservableCollection<CatalogEntryRow> Entries { get; }

    /// <summary>Gets or sets the currently selected catalog entry.</summary>
    public CatalogEntryRow? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                DisplayNameInput = value?.DisplayName;
                OpenSelectedCommand.NotifyCanExecuteChanged();
                RemoveSelectedCommand.NotifyCanExecuteChanged();
                SaveDisplayNameCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets the optional user-controlled name for the selected saved snapshot.</summary>
    public string? DisplayNameInput
    {
        get => _displayNameInput;
        set => SetProperty(ref _displayNameInput, value);
    }

    /// <summary>Gets whether catalog storage is currently enabled in saved application settings.</summary>
    public bool IsEnabled => _configurationService.Current.Catalog.Enabled;

    /// <summary>Gets whether a catalog operation is in progress.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
                OpenSelectedCommand.NotifyCanExecuteChanged();
                RemoveSelectedCommand.NotifyCanExecuteChanged();
                SaveDisplayNameCommand.NotifyCanExecuteChanged();
                RequestClearAllCommand.NotifyCanExecuteChanged();
                ConfirmClearAllCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether no persisted entries are currently available.</summary>
    public bool HasNoEntries => Entries.Count == 0;

    /// <summary>Gets the current user-safe catalog status.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Gets the command that refreshes application-owned catalog metadata.</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>Gets the command that loads the selected entry for shell-owned Results presentation.</summary>
    public IAsyncRelayCommand OpenSelectedCommand { get; }

    /// <summary>Gets the command that removes only the selected application-owned saved snapshot.</summary>
    public IAsyncRelayCommand RemoveSelectedCommand { get; }

    /// <summary>Gets the command that sets, replaces, or clears the selected snapshot's application-owned name.</summary>
    public IAsyncRelayCommand SaveDisplayNameCommand { get; }

    /// <summary>Gets the command that requests a separate explicit confirmation before clearing every saved snapshot.</summary>
    public IRelayCommand RequestClearAllCommand { get; }

    /// <summary>Gets the command that performs an explicitly confirmed clear of application-owned catalog data.</summary>
    public IAsyncRelayCommand ConfirmClearAllCommand { get; }

    /// <summary>Gets the command that cancels a pending clear confirmation without storage access.</summary>
    public IRelayCommand CancelClearAllCommand { get; }

    /// <summary>Gets whether the user has requested, but not yet confirmed, clearing every saved catalog snapshot.</summary>
    public bool IsClearConfirmationPending
    {
        get => _isClearConfirmationPending;
        private set
        {
            if (SetProperty(ref _isClearConfirmationPending, value))
            {
                ConfirmClearAllCommand.NotifyCanExecuteChanged();
                CancelClearAllCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Refreshes catalog summaries while preserving the current selection when possible.
    /// </summary>
    public async Task RefreshAsync()
    {
        OnPropertyChanged(nameof(IsEnabled));
        OpenSelectedCommand.NotifyCanExecuteChanged();
        if (!IsEnabled)
        {
            _entries.Clear();
            SelectedEntry = null;
            NotifyEntryStateChanged();
            StatusText = "Local catalog storage is disabled. Enable it in Settings before a completed scan can be retained.";
            return;
        }

        if (_catalogStore is null)
        {
            StatusText = "The local catalog is unavailable in this application configuration.";
            return;
        }

        var selectedId = SelectedEntry?.Id;
        var cancellation = BeginOperation();
        IsLoading = true;
        try
        {
            var summaries = await _catalogStore.ListAsync(cancellation.Token);
            _entries.Clear();
            foreach (var summary in summaries)
            {
                _entries.Add(CatalogEntryRow.FromSummary(summary));
            }

            SelectedEntry = selectedId is null
                ? null
                : Entries.FirstOrDefault(entry => string.Equals(entry.Id, selectedId, StringComparison.Ordinal));
            NotifyEntryStateChanged();
            StatusText = Entries.Count == 0
                ? "No completed scan snapshots are stored locally."
                : $"{Entries.Count} saved scan snapshot(s) are available locally. Open one to review stored data; it will not be refreshed from the filesystem.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusText = "Catalog refresh was cancelled.";
        }
        catch (Exception)
        {
            StatusText = "The local catalog could not be read. Existing in-memory results remain available.";
        }
        finally
        {
            EndOperation(cancellation);
        }
    }

    private async Task OpenSelectedAsync()
    {
        if (SelectedEntry is null || _catalogStore is null || !IsEnabled)
        {
            return;
        }

        var cancellation = BeginOperation();
        IsLoading = true;
        try
        {
            var entry = await _catalogStore.LoadAsync(SelectedEntry.Id, cancellation.Token);
            if (entry is null)
            {
                StatusText = "The selected saved snapshot is no longer available. Refresh the catalog list.";
                return;
            }

            EntryOpened?.Invoke(this, entry);
            StatusText = "Saved snapshot opened. It has not been refreshed from the filesystem.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusText = "Opening the saved snapshot was cancelled.";
        }
        catch (Exception)
        {
            StatusText = "The selected saved snapshot could not be opened. Existing in-memory results remain available.";
        }
        finally
        {
            EndOperation(cancellation);
        }
    }

    private async Task RemoveSelectedAsync()
    {
        var selected = SelectedEntry;
        if (selected is null || _catalogStore is null || !IsEnabled)
        {
            return;
        }

        var cancellation = BeginOperation();
        IsLoading = true;
        try
        {
            var removed = await _catalogStore.RemoveAsync(selected.Id, cancellation.Token);
            if (removed)
            {
                _entries.Remove(selected);
                SelectedEntry = null;
                NotifyEntryStateChanged();
                StatusText = "The selected saved snapshot was removed from OmniSorSe local catalog data.";
                CatalogChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                StatusText = "The selected saved snapshot is no longer available. Refresh the catalog list.";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusText = "Removing the saved snapshot was cancelled.";
        }
        catch (Exception)
        {
            StatusText = "The selected saved snapshot could not be removed. No selected user file was changed.";
        }
        finally
        {
            EndOperation(cancellation);
        }
    }

    private async Task SaveDisplayNameAsync()
    {
        var selected = SelectedEntry;
        if (selected is null || _catalogStore is null || !IsEnabled)
        {
            return;
        }

        var normalized = string.IsNullOrWhiteSpace(DisplayNameInput) ? null : DisplayNameInput.Trim();
        if (normalized is not null &&
            (normalized.Length > CatalogLimits.MaximumDisplayNameLength || normalized.Any(char.IsControl)))
        {
            StatusText = $"Snapshot names must be no longer than {CatalogLimits.MaximumDisplayNameLength} characters and contain no control characters.";
            return;
        }

        var cancellation = BeginOperation();
        IsLoading = true;
        try
        {
            var entry = await _catalogStore.LoadAsync(selected.Id, cancellation.Token);
            if (entry is null)
            {
                StatusText = "The selected saved snapshot is no longer available. Refresh the catalog list.";
                return;
            }

            var summary = await _catalogStore.SaveAsync(entry with { DisplayName = normalized }, cancellation.Token);
            var index = _entries.IndexOf(selected);
            var updated = CatalogEntryRow.FromSummary(summary);
            if (index >= 0)
            {
                _entries[index] = updated;
            }

            SelectedEntry = updated;
            DisplayNameInput = updated.DisplayName;
            NotifyEntryStateChanged();
            StatusText = normalized is null
                ? "The snapshot name was cleared from OmniSorSe catalog data. The snapshot and scanned files were not changed."
                : "The snapshot name was saved in OmniSorSe catalog data. The snapshot and scanned files were not changed.";
            CatalogChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusText = "Saving the snapshot name was cancelled. Existing catalog data was preserved.";
        }
        catch (Exception)
        {
            StatusText = "The snapshot name could not be saved. Existing catalog data and scanned files were preserved.";
        }
        finally
        {
            EndOperation(cancellation);
        }
    }

    private void RequestClearAll()
    {
        if (!IsEnabled || Entries.Count == 0)
        {
            return;
        }

        IsClearConfirmationPending = true;
        StatusText = "Confirm clear all to remove only OmniSorSe local catalog data. This will not change scanned folders or files.";
    }

    private void CancelClearAll()
    {
        IsClearConfirmationPending = false;
        StatusText = "Clearing local catalog data was cancelled.";
    }

    private async Task ConfirmClearAllAsync()
    {
        if (!IsClearConfirmationPending || _catalogStore is null || !IsEnabled)
        {
            return;
        }

        var cancellation = BeginOperation();
        IsLoading = true;
        try
        {
            await _catalogStore.ClearAsync(cancellation.Token);
            _entries.Clear();
            SelectedEntry = null;
            IsClearConfirmationPending = false;
            NotifyEntryStateChanged();
            StatusText = "OmniSorSe local catalog data was cleared. No selected user file was changed.";
            CatalogChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusText = "Clearing local catalog data was cancelled.";
        }
        catch (Exception)
        {
            StatusText = "OmniSorSe local catalog data could not be cleared. No selected user file was changed.";
        }
        finally
        {
            EndOperation(cancellation);
        }
    }

    /// <summary>
    /// Cancels a pending catalog operation when the desktop shell is disposed.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        _isDisposed = true;
    }

    private CancellationTokenSource BeginOperation()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var current = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _operationCancellation, current);
        previous?.Cancel();
        return current;
    }

    private void EndOperation(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(_operationCancellation, cancellation))
        {
            _operationCancellation = null;
            IsLoading = false;
        }

        cancellation.Dispose();
    }

    private void NotifyEntryStateChanged()
    {
        OnPropertyChanged(nameof(HasNoEntries));
        RequestClearAllCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        SaveDisplayNameCommand.NotifyCanExecuteChanged();
    }
}
