using Avalonia.Controls;
using Avalonia.Threading;
using System.ComponentModel;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Views;

/// <summary>
/// Displays supported v0.1 application settings.
/// </summary>
public partial class SettingsView : UserControl
{
    private SettingsViewModel? _viewModel;
    private SettingsDraft? _draft;
    private Avalonia.Vector? _pendingOffset;

    /// <summary>
    /// Initializes the settings view.
    /// </summary>
    public SettingsView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs eventArgs)
    {
        Unsubscribe();
        base.OnDataContextChanged(eventArgs);
        _viewModel = DataContext as SettingsViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SubscribeDraft(_viewModel.Draft);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs eventArgs)
    {
        Unsubscribe();
        base.OnDetachedFromVisualTree(eventArgs);
    }

    private void SubscribeDraft(SettingsDraft draft)
    {
        _draft = draft;
        _draft.PropertyChanged += OnDraftPropertyChanged;
    }

    private void Unsubscribe()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (_draft is not null)
        {
            _draft.PropertyChanged -= OnDraftPropertyChanged;
        }

        _viewModel = null;
        _draft = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(SettingsViewModel.Draft) || _viewModel is null)
        {
            return;
        }

        CaptureAndRestoreOffset();
        if (_draft is not null)
        {
            _draft.PropertyChanged -= OnDraftPropertyChanged;
        }

        SubscribeDraft(_viewModel.Draft);
    }

    private void OnDraftPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(SettingsDraft.AiEnabled)
            or nameof(SettingsDraft.ShowAdvancedFeatures)
            or nameof(SettingsDraft.AiRequestDiagnosticsEnabled))
        {
            CaptureAndRestoreOffset();
        }
    }

    private void CaptureAndRestoreOffset()
    {
        _pendingOffset = SettingsScrollViewer.Offset;
        Dispatcher.UIThread.Post(
            () => Dispatcher.UIThread.Post(RestorePendingOffset, DispatcherPriority.Background),
            DispatcherPriority.Loaded);
    }

    private void RestorePendingOffset()
    {
        if (_pendingOffset is not { } requested)
        {
            return;
        }

        _pendingOffset = null;
        var maximumY = Math.Max(0, SettingsScrollViewer.Extent.Height - SettingsScrollViewer.Viewport.Height);
        SettingsScrollViewer.Offset = new Avalonia.Vector(
            Math.Clamp(requested.X, 0, Math.Max(0, SettingsScrollViewer.Extent.Width - SettingsScrollViewer.Viewport.Width)),
            Math.Clamp(requested.Y, 0, maximumY));
    }
}
