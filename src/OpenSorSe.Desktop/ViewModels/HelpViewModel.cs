using CommunityToolkit.Mvvm.Input;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Presents centrally registered local Help topics and previous-page navigation.</summary>
public sealed class HelpViewModel : ViewModelBase
{
    private HelpTopic _selectedTopic = HelpCatalog.Get(HelpTopicId.HelpOverview);
    private NavigationDestination? _previousDestination;

    /// <summary>Initializes the local topic catalog.</summary>
    public HelpViewModel()
    {
        Topics = HelpCatalog.Topics;
        BackCommand = new RelayCommand(RequestBack, () => PreviousDestination is not null);
    }

    /// <summary>Raised when the user requests return to the remembered page.</summary>
    public event EventHandler? BackRequested;

    /// <summary>Gets registered topics in stable order.</summary>
    public IReadOnlyList<HelpTopic> Topics { get; }

    /// <summary>Gets or sets the selected structured topic.</summary>
    public HelpTopic SelectedTopic
    {
        get => _selectedTopic;
        set => SetProperty(ref _selectedTopic, value ?? HelpCatalog.Get(HelpTopicId.HelpOverview));
    }

    /// <summary>Gets the page from which contextual Help was opened.</summary>
    public NavigationDestination? PreviousDestination
    {
        get => _previousDestination;
        private set
        {
            if (SetProperty(ref _previousDestination, value))
            {
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(BackLabel));
                BackCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether a previous page can be requested.</summary>
    public bool CanGoBack => PreviousDestination is not null;

    /// <summary>Gets a beginner-friendly back label.</summary>
    public string BackLabel => PreviousDestination is null ? "No previous page" : "Back to previous page";

    /// <summary>Gets the command that returns to the originating visible destination.</summary>
    public IRelayCommand BackCommand { get; }

    /// <summary>Selects a known topic and records the originating destination.</summary>
    public void Open(HelpTopicId topicId, NavigationDestination? previousDestination)
    {
        SelectedTopic = HelpCatalog.Get(topicId);
        PreviousDestination = previousDestination is NavigationDestination.Help ? null : previousDestination;
    }

    private void RequestBack() => BackRequested?.Invoke(this, EventArgs.Empty);
}
