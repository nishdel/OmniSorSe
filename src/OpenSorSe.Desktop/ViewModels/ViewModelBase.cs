using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Provides the common observable base type for desktop view models.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    private Action<HelpTopicId>? _helpRequest;
    private HelpTopicId _helpTopic = HelpTopicId.HelpOverview;

    /// <summary>Initializes the shared contextual Help command.</summary>
    protected ViewModelBase()
    {
        HelpCommand = new RelayCommand(RequestHelp, () => _helpRequest is not null);
    }

    /// <summary>Gets the keyboard-accessible contextual Help command.</summary>
    public IRelayCommand HelpCommand { get; }

    /// <summary>Gets the currently registered contextual topic.</summary>
    public HelpTopicId HelpTopic => _helpTopic;

    /// <summary>Configures contextual Help without scattering navigation logic across pages.</summary>
    internal void ConfigureHelp(HelpTopicId topic, Action<HelpTopicId> request)
    {
        _helpTopic = topic;
        _helpRequest = request ?? throw new ArgumentNullException(nameof(request));
        HelpCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Updates a context-sensitive topic while preserving central routing.</summary>
    protected void SetHelpTopic(HelpTopicId topic)
    {
        if (_helpTopic == topic)
        {
            return;
        }

        _helpTopic = topic;
        OnPropertyChanged(nameof(HelpTopic));
    }

    private void RequestHelp() => _helpRequest?.Invoke(_helpTopic);
}
