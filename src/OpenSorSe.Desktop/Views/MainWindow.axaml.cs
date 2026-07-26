using Avalonia.Controls;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Views;

/// <summary>
/// Hosts the initial OpenSorSe desktop application shell.
/// </summary>
public partial class MainWindow : Window
{
    private bool _catalogInitializationStarted;

    /// <summary>
    /// Initialises the window for Avalonia's XAML runtime loader and designer.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initialises the window with its runtime presentation model.
    /// </summary>
    /// <param name="viewModel">The view model supplied by application composition.</param>
    public MainWindow(MainViewModel viewModel)
        : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    /// <inheritdoc />
    protected override async void OnOpened(EventArgs eventArgs)
    {
        base.OnOpened(eventArgs);
        if (_catalogInitializationStarted || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _catalogInitializationStarted = true;
        try
        {
            await viewModel.InitializeCatalogAsync();
        }
        catch (Exception)
        {
            viewModel.Notifications.Publish(new NotificationRequest(
                NotificationSeverity.Warning,
                "Application-owned catalog data could not be initialized. OpenSorSe remains available for read-only scans."));
        }
    }

    private async void OnNavigationButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is Button { DataContext: NavigationItem item })
        {
            await viewModel.NavigateAsync(item.Destination);
        }
    }
}
