using OpenSorSe.Application.Features;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Pairs a shell destination with user-facing navigation presentation.</summary>
public sealed class NavigationItem : ViewModelBase
{
    private bool _isSelected;

    /// <summary>Initializes one stable shell entry.</summary>
    public NavigationItem(
        NavigationDestination destination,
        string label,
        FeatureRequirement requirement,
        NavigationGroup group = NavigationGroup.Primary,
        string? icon = null)
    {
        Destination = destination;
        Label = label;
        Requirement = requirement;
        Group = group;
        Icon = icon;
    }

    /// <summary>Gets the destination represented by this entry.</summary>
    public NavigationDestination Destination { get; }

    /// <summary>Gets the concise user-facing label.</summary>
    public string Label { get; }

    /// <summary>Gets centralized feature requirements.</summary>
    public FeatureRequirement Requirement { get; }

    /// <summary>Gets the progressive-disclosure group.</summary>
    public NavigationGroup Group { get; }

    /// <summary>Gets the compact text icon.</summary>
    public string? Icon { get; }

    /// <summary>Gets whether this entry is currently active.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        private set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Updates active presentation without performing navigation.</summary>
    internal void SetSelected(bool selected) => IsSelected = selected;
}
