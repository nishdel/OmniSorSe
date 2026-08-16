namespace OpenSorSe.Application.Semantic;

/// <summary>Identifies one canonical, typed discovery facet.</summary>
public enum DiscoveryFacetKind
{
    /// <summary>Schema-6 Theme Smart Tags.</summary>
    Theme,
    /// <summary>Schema-6 Document Type Smart Tags.</summary>
    DocumentType,
    /// <summary>Explicit schema-6 User Tags.</summary>
    UserTag,
    /// <summary>Stable file categories derived from extensions.</summary>
    FileType,
    /// <summary>Filesystem-created calendar year.</summary>
    CreatedYear,
    /// <summary>Filesystem-modified calendar year.</summary>
    ModifiedYear,
}

/// <summary>Contains one canonical facet value and its current-context result count.</summary>
public sealed record DiscoveryFacetValue(
    string CanonicalId,
    string DisplayName,
    long Count,
    bool IsSelected = false);

/// <summary>Contains a bounded, database-aggregated facet group.</summary>
public sealed record DiscoveryFacetGroup(
    DiscoveryFacetKind Kind,
    string DisplayName,
    IReadOnlyList<DiscoveryFacetValue> Values);

/// <summary>Contains one canonical query/filter state shared by Search, facets, and Saved Views.</summary>
public sealed record DiscoveryQueryState(
    string QueryText,
    IReadOnlyList<SearchFilter> Filters);

/// <summary>Requests context-sensitive counts without loading file/tag associations into memory.</summary>
public sealed record DiscoveryFacetRequest(
    string TopicText,
    IReadOnlyList<SearchFilter> Filters,
    int MaximumValuesPerFacet = 30);

/// <summary>Contains all bounded facet groups for the current canonical query state.</summary>
public sealed record DiscoveryFacetSnapshot(
    IReadOnlyList<DiscoveryFacetGroup> Groups,
    bool IsAvailable = true)
{
    /// <summary>Gets the safe fallback returned by providers without facet aggregation.</summary>
    public static DiscoveryFacetSnapshot Unavailable { get; } = new([], false);
}

/// <summary>Provides database-backed facet counts for one canonical discovery request.</summary>
public interface IFacetedDiscoverySource
{
    /// <summary>Returns bounded counts using OR within a facet and AND across facet types.</summary>
    Task<DiscoveryFacetSnapshot> GetFacetCountsAsync(
        DiscoveryFacetRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(DiscoveryFacetSnapshot.Unavailable);
}

/// <summary>Stores dynamic Saved Views as query rules rather than copied file membership.</summary>
public sealed record SavedDiscoveryView(
    string Id,
    string Name,
    DiscoveryQueryState Query,
    int Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Persists bounded local Saved View rules.</summary>
public interface ISavedDiscoveryViewStore
{
    /// <summary>Lists validated views in stable display order.</summary>
    Task<IReadOnlyList<SavedDiscoveryView>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces one view with the same stable identifier.</summary>
    Task<SavedDiscoveryView> SaveAsync(
        SavedDiscoveryView view,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes one Saved View without touching files or index membership.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Atomically replaces the library after an explicit validated state-restore review.</summary>
    async Task ReplaceAllReviewedAsync(
        IReadOnlyList<SavedDiscoveryView> views,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(views);
        foreach (var current in await ListAsync(cancellationToken).ConfigureAwait(false))
        {
            await DeleteAsync(current.Id, cancellationToken).ConfigureAwait(false);
        }

        foreach (var view in views)
        {
            await SaveAsync(view, cancellationToken).ConfigureAwait(false);
        }
    }
}
