using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpenSorSe.Desktop.ViewModels;
using System.ComponentModel;

namespace OpenSorSe.Desktop.Views;

/// <summary>Hosts live, process-memory-only AI request diagnostics.</summary>
public partial class AiRequestDiagnosticsWindow : Window
{
    /// <summary>Initializes the XAML window.</summary>
    public AiRequestDiagnosticsWindow() => InitializeComponent();

    /// <summary>Initializes the window with its observable presentation model.</summary>
    public AiRequestDiagnosticsWindow(AiDiagnosticsViewModel viewModel) : this() =>
        Attach(viewModel);

    private void Attach(AiDiagnosticsViewModel viewModel)
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender is not AiDiagnosticsViewModel { AutoScroll: true } ||
            eventArgs.PropertyName is not (nameof(AiDiagnosticsViewModel.SelectedSession) or nameof(AiDiagnosticsViewModel.StageText)))
            return;
        foreach (var surface in TextSurfaces())
        {
            surface.CaretIndex = surface.Text?.Length ?? 0;
        }
    }

    private IEnumerable<DiagnosticTextBox> TextSurfaces() =>
        new[] { OverviewText, SystemText, UserText, RequestText, RawText, ExtractedText, ParsedText, ValidationTextBox };

    private void OnWordWrapChanged(object? sender, RoutedEventArgs eventArgs)
    {
        var wrapping = DataContext is AiDiagnosticsViewModel { WordWrap: true } ? TextWrapping.Wrap : TextWrapping.NoWrap;
        foreach (var surface in TextSurfaces()) surface.TextWrapping = wrapping;
    }

    private async void OnSaveJson(object? sender, RoutedEventArgs eventArgs) =>
        await SaveAsync("Save AI diagnostic report as JSON", "ai-diagnostic.json", "JSON", "*.json",
            (DataContext as AiDiagnosticsViewModel)?.BuildJsonReport() ?? "{}");

    private async void OnSaveText(object? sender, RoutedEventArgs eventArgs) =>
        await SaveAsync("Save AI diagnostic report as text", "ai-diagnostic.txt", "Text", "*.txt",
            (DataContext as AiDiagnosticsViewModel)?.BuildTextReport() ?? "");

    private async void OnCopySection(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not AiDiagnosticsViewModel viewModel || sender is not Button { Tag: string section }) return;
        var text = section switch
        {
            "overview" => viewModel.StageText,
            "system" => viewModel.SystemPrompt,
            "user" => viewModel.UserPrompt,
            "request" => viewModel.RequestJson,
            "raw" => viewModel.RawHttpResponse,
            "extracted" => viewModel.ExtractedAssistantResponse,
            "parsed" => viewModel.ParsedStructuredResponse,
            "validation" => viewModel.ValidationText,
            _ => "",
        };
        await viewModel.CopyAsync(text);
    }

    private async Task SaveAsync(string title, string suggestedName, string label, string pattern, string content)
    {
        if (!StorageProvider.CanSave) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType(label) { Patterns = [pattern] }],
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
    }
}
