using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Views;

/// <summary>Hosts the shared non-modal advanced-diagnostics viewer.</summary>
public partial class AdvancedDiagnosticsWindow : Window
{
    /// <summary>Initializes the XAML window.</summary>
    public AdvancedDiagnosticsWindow() => InitializeComponent();

    /// <summary>Initializes the window with its unified observable presentation model.</summary>
    public AdvancedDiagnosticsWindow(AdvancedDiagnosticsViewModel viewModel)
        : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private IEnumerable<DiagnosticTextBox> TextSurfaces() =>
        [OverviewText, TimelineText, InputsText, IntermediateText, OutputsText, WarningsText, PerformanceText];

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender is not AdvancedDiagnosticsViewModel { AutoScroll: true } ||
            eventArgs.PropertyName is not (
                nameof(AdvancedDiagnosticsViewModel.SelectedSession) or
                nameof(AdvancedDiagnosticsViewModel.TimelineText)))
        {
            return;
        }

        foreach (var surface in TextSurfaces())
        {
            surface.CaretIndex = surface.Text?.Length ?? 0;
        }
    }

    private void OnWordWrapChanged(object? sender, RoutedEventArgs eventArgs)
    {
        var wrapping = DataContext is AdvancedDiagnosticsViewModel { WordWrap: true }
            ? TextWrapping.Wrap
            : TextWrapping.NoWrap;
        foreach (var surface in TextSurfaces())
        {
            surface.TextWrapping = wrapping;
        }
    }

    private async void OnSaveSelectedJson(object? sender, RoutedEventArgs eventArgs) =>
        await SaveAsync(
            "Export selected diagnostic as JSON",
            "opensorse-diagnostic.json",
            "JSON",
            "*.json",
            (DataContext as AdvancedDiagnosticsViewModel)?.BuildSelectedJson() ?? "{}");

    private async void OnSaveSelectedText(object? sender, RoutedEventArgs eventArgs) =>
        await SaveAsync(
            "Export selected diagnostic as text",
            "opensorse-diagnostic.txt",
            "Text",
            "*.txt",
            (DataContext as AdvancedDiagnosticsViewModel)?.BuildSelectedText() ?? string.Empty);

    private async void OnSaveAll(object? sender, RoutedEventArgs eventArgs) =>
        await SaveAsync(
            "Export all retained diagnostics",
            "opensorse-diagnostics-all.json",
            "JSON",
            "*.json",
            (DataContext as AdvancedDiagnosticsViewModel)?.BuildAllJson() ?? "[]");

    private async Task SaveAsync(
        string title,
        string suggestedName,
        string label,
        string pattern,
        string content)
    {
        var viewModel = DataContext as AdvancedDiagnosticsViewModel;
        if (!StorageProvider.CanSave)
        {
            viewModel?.ReportExportResult(false);
            return;
        }

        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedName,
                FileTypeChoices = [new FilePickerFileType(label) { Patterns = [pattern] }],
            });
            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(content);
            viewModel?.ReportExportResult(true);
        }
        catch
        {
            viewModel?.ReportExportResult(false);
        }
    }
}
