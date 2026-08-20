using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenSorSe.Application.Semantic;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Presents one bounded database-backed facet group while retaining row identity across count refreshes.</summary>
public sealed class DiscoveryFacetGroupRow
{
    private readonly ObservableCollection<DiscoveryFacetValueRow> _values;

    /// <summary>Initializes one facet group.</summary>
    public DiscoveryFacetGroupRow(
        DiscoveryFacetKind kind,
        string displayName,
        IReadOnlyList<DiscoveryFacetValueRow> values)
    {
        Kind = kind;
        DisplayName = displayName;
        _values = new ObservableCollection<DiscoveryFacetValueRow>(values);
        Values = new ReadOnlyObservableCollection<DiscoveryFacetValueRow>(_values);
    }

    /// <summary>Gets the canonical facet kind.</summary>
    public DiscoveryFacetKind Kind { get; }

    /// <summary>Gets the localized group label.</summary>
    public string DisplayName { get; }

    /// <summary>Gets bounded values whose object identities survive asynchronous count refreshes.</summary>
    public ReadOnlyObservableCollection<DiscoveryFacetValueRow> Values { get; }

    /// <summary>Updates counts and selection without replacing rows that may own keyboard focus.</summary>
    public void Apply(IReadOnlyList<DiscoveryFacetValue> values)
    {
        var incomingIds = values.Select(value => value.CanonicalId).ToHashSet(StringComparer.Ordinal);
        for (var index = _values.Count - 1; index >= 0; index--)
        {
            if (!incomingIds.Contains(_values[index].CanonicalId))
            {
                _values.RemoveAt(index);
            }
        }

        foreach (var value in values)
        {
            var existing = _values.FirstOrDefault(row =>
                string.Equals(row.CanonicalId, value.CanonicalId, StringComparison.Ordinal));
            if (existing is null)
            {
                _values.Add(new DiscoveryFacetValueRow(
                    Kind,
                    value.CanonicalId,
                    value.DisplayName,
                    value.Count,
                    value.IsSelected));
            }
            else
            {
                existing.Update(value.Count, value.IsSelected);
            }
        }
    }
}

/// <summary>Presents one canonical facet value, count, and accessible selected state.</summary>
public sealed class DiscoveryFacetValueRow : ObservableObject
{
    private long _count;
    private bool _isSelected;

    /// <summary>Initializes one canonical facet value.</summary>
    public DiscoveryFacetValueRow(
        DiscoveryFacetKind kind,
        string canonicalId,
        string displayName,
        long count,
        bool isSelected)
    {
        Kind = kind;
        CanonicalId = canonicalId;
        DisplayName = displayName;
        _count = count;
        _isSelected = isSelected;
    }

    /// <summary>Gets the canonical facet kind.</summary>
    public DiscoveryFacetKind Kind { get; }

    /// <summary>Gets the language-neutral canonical value identity.</summary>
    public string CanonicalId { get; }

    /// <summary>Gets the display label.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the current-context matching-file count.</summary>
    public long Count => _count;

    /// <summary>Gets whether the value is active in the canonical query.</summary>
    public bool IsSelected => _isSelected;

    /// <summary>Gets a compact value/count label.</summary>
    public string CountLabel => $"{DisplayName} ({Count:N0})";

    /// <summary>Gets a screen-reader label that does not rely on colour.</summary>
    public string AccessibleName => IsSelected
        ? $"{DisplayName}, {Count:N0} matching files, selected"
        : $"{DisplayName}, {Count:N0} matching files, not selected";

    /// <summary>Updates transient count/selection state while retaining this row for UI focus.</summary>
    public void Update(long count, bool isSelected)
    {
        if (SetProperty(ref _count, count, nameof(Count)))
        {
            OnPropertyChanged(nameof(CountLabel));
            OnPropertyChanged(nameof(AccessibleName));
        }

        if (SetProperty(ref _isSelected, isSelected, nameof(IsSelected)))
        {
            OnPropertyChanged(nameof(AccessibleName));
        }
    }

    /// <summary>Maps the display facet to its canonical deterministic Search filter kind.</summary>
    public SearchFilter ToFilter()
    {
        var kind = Kind switch
        {
            DiscoveryFacetKind.Theme => SearchFilterKind.SmartTagTheme,
            DiscoveryFacetKind.DocumentType => SearchFilterKind.SmartTagDocumentType,
            DiscoveryFacetKind.UserTag => SearchFilterKind.SmartTagUser,
            DiscoveryFacetKind.FileType => SearchFilterKind.FileType,
            DiscoveryFacetKind.CreatedYear => SearchFilterKind.CreatedYear,
            DiscoveryFacetKind.ModifiedYear => SearchFilterKind.ModifiedYear,
            _ => throw new InvalidOperationException("The facet cannot be represented as a Search filter."),
        };
        var label = Kind switch
        {
            DiscoveryFacetKind.Theme => "Theme",
            DiscoveryFacetKind.DocumentType => "Document Type",
            DiscoveryFacetKind.UserTag => "User Tag",
            DiscoveryFacetKind.FileType => "File Type",
            DiscoveryFacetKind.CreatedYear => "Created year",
            DiscoveryFacetKind.ModifiedYear => "Modified year",
            _ => Kind.ToString(),
        };
        return new SearchFilter($"{kind}:{CanonicalId}", kind, CanonicalId, $"{label}: {DisplayName}");
    }
}

/// <summary>Presents one local dynamic Saved View rule.</summary>
public sealed record SavedDiscoveryViewRow(
    string Id,
    string Name,
    string Summary,
    SavedDiscoveryView Model)
{
    /// <summary>Creates a display-safe row without evaluating or storing membership.</summary>
    public static SavedDiscoveryViewRow FromModel(SavedDiscoveryView model) => new(
        model.Id,
        model.Name,
        string.IsNullOrWhiteSpace(model.Query.QueryText)
            ? $"{model.Query.Filters.Count:N0} active filter(s)"
            : $"Query plus {model.Query.Filters.Count:N0} active filter(s)",
        model);
}
