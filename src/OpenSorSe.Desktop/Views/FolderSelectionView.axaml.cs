using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Views;

/// <summary>
/// Displays validated scan-root selection controls.
/// </summary>
public partial class FolderSelectionView : UserControl
{
    /// <summary>
    /// Initializes the folder-selection view.
    /// </summary>
    public FolderSelectionView()
    {
        InitializeComponent();
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not FolderSelectionViewModel viewModel ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { CanPickFolder: true } storageProvider)
        {
            return;
        }

        try
        {
            var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select a folder to scan",
                AllowMultiple = false,
            });
            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                viewModel.AddFolder(path);
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceWarning(
                "The folder picker failed safely ({0}).",
                exception.GetType().Name);
            viewModel.ReportFolderPickerFailure();
        }
    }
}
