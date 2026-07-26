using Avalonia.Threading;
using OpenSorSe.Core.Diagnostics;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Desktop.Views;

namespace OpenSorSe.Desktop.Services;

/// <summary>Opens or activates the shared non-modal advanced-diagnostics viewer.</summary>
public interface IAdvancedDiagnosticsWindowService
{
    /// <summary>Shows the shared viewer without affecting any feature cancellation token.</summary>
    void Show();
}

/// <summary>Observes common immutable sessions and updates the viewer on the Avalonia UI thread.</summary>
public sealed class AdvancedDiagnosticsWindowCoordinator : IAdvancedDiagnosticsWindowService, IDisposable
{
    private readonly IDiagnosticsCollector _collector;
    private readonly IDiagnosticsExportService _exporter;
    private readonly IClipboardService _clipboard;
    private AdvancedDiagnosticsWindow? _window;
    private bool _automaticOpeningSuppressed;

    /// <summary>Subscribes to the process-local common diagnostic stream.</summary>
    public AdvancedDiagnosticsWindowCoordinator(
        IDiagnosticsCollector collector,
        IDiagnosticsExportService exporter,
        IClipboardService clipboard)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _collector.SessionChanged += OnSessionChanged;
        _collector.SessionsCleared += OnSessionsCleared;
    }

    /// <inheritdoc />
    public void Show() =>
        Dispatcher.UIThread.Post(() =>
        {
            _automaticOpeningSuppressed = false;
            ShowCore(null, false, true);
        });

    /// <inheritdoc />
    public void Dispose()
    {
        _collector.SessionChanged -= OnSessionChanged;
        _collector.SessionsCleared -= OnSessionsCleared;
        _window?.Close();
    }

    private void OnSessionChanged(object? sender, DiagnosticSessionChangedEventArgs eventArgs) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_window is null && _automaticOpeningSuppressed)
            {
                return;
            }

            ShowCore(eventArgs.Session, eventArgs.IsNew, false);
        });

    private void OnSessionsCleared(object? sender, EventArgs eventArgs) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_window?.DataContext is AdvancedDiagnosticsViewModel viewModel)
            {
                viewModel.Reload();
            }
        });

    private void ShowCore(DiagnosticSession? session, bool isNew, bool activate)
    {
        var created = false;
        if (_window is null)
        {
            var createdViewModel = new AdvancedDiagnosticsViewModel(_collector, _exporter, _clipboard);
            _window = new AdvancedDiagnosticsWindow(createdViewModel);
            _window.Closed += (_, _) =>
            {
                _window = null;
                _automaticOpeningSuppressed = true;
            };
            _window.Show();
            created = true;
        }
        else if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (session is not null && _window.DataContext is AdvancedDiagnosticsViewModel viewModel)
        {
            viewModel.Upsert(session, isNew || session.Status == DiagnosticStatus.Active);
        }

        if (created || activate)
        {
            _window.Activate();
        }
    }
}
