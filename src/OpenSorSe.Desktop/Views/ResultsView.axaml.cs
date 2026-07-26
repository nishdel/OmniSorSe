using Avalonia.Controls;
using Avalonia.Input;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Desktop.ViewModels;
using System.ComponentModel;

namespace OpenSorSe.Desktop.Views;

/// <summary>
/// Displays a read-only review of planning and conflict-resolution results.
/// </summary>
public partial class ResultsView : UserControl
{
    private ResultsViewModel? _viewModel;

    /// <summary>
    /// Initializes the results-review view.
    /// </summary>
    public ResultsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        DetachViewModel();
        _viewModel = DataContext as ResultsViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        ApplySplitLayout();
    }

    private void DetachViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(ResultsViewModel.HasSelectedDetails) or
            nameof(ResultsViewModel.DetailsPanelWidthRatio))
        {
            ApplySplitLayout();
        }
    }

    private async void OnFilesDetailsSplitterDragCompleted(object? sender, VectorEventArgs eventArgs)
    {
        if (_viewModel is null || !_viewModel.HasSelectedDetails)
        {
            return;
        }

        var tableColumn = FilesSplitGrid.ColumnDefinitions[0];
        var detailsColumn = FilesSplitGrid.ColumnDefinitions[2];
        var availableWidth = tableColumn.ActualWidth + detailsColumn.ActualWidth;
        if (availableWidth <= 0)
        {
            return;
        }

        await _viewModel.SetDetailsPanelWidthRatioAsync(detailsColumn.ActualWidth / availableWidth);
        ApplySplitLayout();
    }

    private void ApplySplitLayout()
    {
        if (_viewModel?.HasSelectedDetails != true)
        {
            var hiddenColumns = FilesSplitGrid.ColumnDefinitions;
            hiddenColumns[0].MinWidth = 0;
            hiddenColumns[0].Width = new GridLength(1, GridUnitType.Star);
            hiddenColumns[1].Width = new GridLength(0);
            hiddenColumns[2].MinWidth = 0;
            hiddenColumns[2].Width = new GridLength(0);
            return;
        }

        var ratio = Math.Clamp(
            _viewModel.DetailsPanelWidthRatio,
            FeatureSettings.MinimumFilesPageDetailsPanelWidthRatio,
            FeatureSettings.MaximumFilesPageDetailsPanelWidthRatio);
        var columns = FilesSplitGrid.ColumnDefinitions;
        columns[0].MinWidth = ResultsViewModel.MinimumFileTableWidth;
        columns[0].Width = new GridLength(1 - ratio, GridUnitType.Star);
        columns[1].Width = new GridLength(12);
        columns[2].MinWidth = ResultsViewModel.MinimumDetailsPanelWidth;
        columns[2].Width = new GridLength(ratio, GridUnitType.Star);
    }
}
