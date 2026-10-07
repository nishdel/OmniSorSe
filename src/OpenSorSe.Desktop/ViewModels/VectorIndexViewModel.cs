using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.Semantic;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Presents optional learned-index controls without performing provider or database work.</summary>
public sealed class VectorIndexViewModel : ViewModelBase, IDisposable
{
    private readonly VectorIndexCoordinator _coordinator;
    private readonly CancellationTokenSource _lifetime = new();
    private string _statusText;
    private bool _disposed;

    /// <summary>Observes the application-owned vector worker.</summary>
    public VectorIndexViewModel(VectorIndexCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _statusText = coordinator.CurrentStatus.Message;
        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(false));
        RebuildCommand = new AsyncRelayCommand(() => RefreshAsync(true));
        PauseCommand = new RelayCommand(coordinator.Pause);
        ResumeCommand = new RelayCommand(coordinator.Resume);
        coordinator.StatusChanged += OnStatusChanged;
    }

    /// <summary>Gets model availability, indexing progress and actionable fallback information.</summary>
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    /// <summary>Checks the configured model and updates missing or stale vectors.</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>Regenerates only the disposable vector index.</summary>
    public IAsyncRelayCommand RebuildCommand { get; }

    /// <summary>Pauses background embedding work.</summary>
    public IRelayCommand PauseCommand { get; }

    /// <summary>Resumes background embedding work.</summary>
    public IRelayCommand ResumeCommand { get; }

    private async Task RefreshAsync(bool rebuild)
    {
        try
        {
            await _coordinator.RefreshAsync(rebuild, _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            StatusText = "Semantic indexing is unavailable. Keyword Search remains available; inspect the local model and retry.";
        }
    }

    private void OnStatusChanged(object? sender, VectorIndexStatus status)
    {
        void Apply()
        {
            if (!_disposed)
            {
                StatusText = status.Message;
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _coordinator.StatusChanged -= OnStatusChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
