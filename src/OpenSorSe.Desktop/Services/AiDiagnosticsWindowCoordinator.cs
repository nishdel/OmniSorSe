using Avalonia.Threading;
using OpenSorSe.Application.AI;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Desktop.Views;

namespace OpenSorSe.Desktop.Services;

/// <summary>Observes application diagnostics and owns the optional non-modal window lifecycle.</summary>
public sealed class AiDiagnosticsWindowCoordinator : IDisposable
{
    private readonly IAiDiagnosticsCollector _collector;
    private readonly IClipboardService _clipboard;
    private AiRequestDiagnosticsWindow? _window;

    /// <summary>Subscribes to the process-local diagnostics stream.</summary>
    public AiDiagnosticsWindowCoordinator(IAiDiagnosticsCollector collector, IClipboardService clipboard)
    {
        _collector = collector;
        _clipboard = clipboard;
        _collector.SessionChanged += OnSessionChanged;
    }

    private void OnSessionChanged(object? sender, AiDiagnosticSessionChangedEventArgs args) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_collector.IsEnabled) return;
            if (_window is null)
            {
                var newViewModel = new AiDiagnosticsViewModel(_collector, _clipboard);
                _window = new AiRequestDiagnosticsWindow(newViewModel);
                _window.Closed += (_, _) => _window = null;
                _window.Show();
            }
            if (_window.DataContext is AiDiagnosticsViewModel viewModel)
                viewModel.Upsert(args.Session, args.IsNew || args.Session.Status == AiDiagnosticState.Active);
        });

    /// <inheritdoc />
    public void Dispose()
    {
        _collector.SessionChanged -= OnSessionChanged;
        _window?.Close();
    }
}
