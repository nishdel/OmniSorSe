using OpenSorSe.Application.CatalogSearch;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Presents one bounded named catalog query preset.
/// </summary>
public sealed record SavedCatalogSearchRow(
    string Id,
    string Name,
    string QueryText,
    DateTimeOffset UpdatedAtUtc)
{
    /// <summary>Gets the original creation timestamp retained when the preset is renamed.</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Creates a display row from an application-owned saved query.</summary>
    public static SavedCatalogSearchRow FromModel(SavedCatalogSearch search) => new(
        search.Id,
        search.Name,
        search.QueryText,
        search.UpdatedAtUtc)
    {
        CreatedAtUtc = search.CreatedAtUtc,
    };
}
