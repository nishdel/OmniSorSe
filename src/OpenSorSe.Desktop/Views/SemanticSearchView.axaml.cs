using Avalonia.Controls;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Views;

/// <summary>Hosts local Search, durable indexing progress, and explained results.</summary>
public partial class SemanticSearchView : UserControl
{
    /// <summary>Initializes the Search view.</summary>
    public SemanticSearchView()
    {
        InitializeComponent();
    }

    private void OnSearchSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (DataContext is SemanticSearchViewModel viewModel && sender is ListBox listBox)
        {
            viewModel.SetOrganizationSelection(listBox.SelectedItems?.OfType<SemanticSearchHit>() ?? []);
        }
    }
}
