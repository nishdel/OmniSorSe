using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenSorSe.Core;
using System.ComponentModel;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Views;

/// <summary>
/// Displays the current application settings surface.
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
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        if (_viewModel is null && DataContext is SettingsViewModel viewModel)
        {
            _viewModel = viewModel;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.FocusRequested += OnFocusRequested;
            SubscribeDraft(_viewModel.Draft);
        }

        if (_viewModel?.LastFocusRequest is { } target)
        {
            OnFocusRequested(target);
        }
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
        _viewModel.FocusRequested += OnFocusRequested;
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
            _viewModel.FocusRequested -= OnFocusRequested;
        }

        if (_draft is not null)
        {
            _draft.PropertyChanged -= OnDraftPropertyChanged;
        }

        _viewModel = null;
        _draft = null;
    }

    private void OnFocusRequested(SettingsFocusTarget target) =>
        Dispatcher.UIThread.Post(
            () => FocusRequestedTarget(target),
            DispatcherPriority.Loaded);

    private void FocusRequestedTarget(SettingsFocusTarget target)
    {
        var control = target == SettingsFocusTarget.AiAssistance
            ? AiSettingsHeading
            : SettingsPageHeading;
        control.BringIntoView();
        control.Focus();
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

    private async void ExportStateClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel is null || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanSave: true } storage)
        {
            return;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export OmniSorSe state",
            SuggestedFileName = $"OmniSorSe-state-{ApplicationVersionInfo.Current}.oms-state",
            FileTypeChoices = [new FilePickerFileType("OmniSorSe state archive") { Patterns = ["*.oms-state"] }],
        });
        if (file?.Path.IsFile == true)
        {
            await _viewModel.ExportStateAsync(file.Path.LocalPath);
        }
    }

    private async void PreviewRestoreClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel is null || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Preview OmniSorSe state restore",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("OmniSorSe state archive") { Patterns = ["*.oms-state"] }],
        });
        if (files.Count == 1 && files[0].Path.IsFile)
        {
            await _viewModel.PreviewStateRestoreAsync(files[0].Path.LocalPath);
        }
    }
}
