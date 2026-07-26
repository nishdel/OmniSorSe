namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Identifies a top-level destination hosted by the desktop application shell.
/// </summary>
public enum NavigationDestination
{
    /// <summary>Displays the application overview.</summary>
    Dashboard,

    /// <summary>Displays scan-related controls.</summary>
    Scan,

    /// <summary>Displays processed file results.</summary>
    Results,

    /// <summary>Displays exact duplicate review for the active scan.</summary>
    Duplicates,

    /// <summary>Displays opt-in, application-owned saved result snapshots.</summary>
    Catalog,

    /// <summary>Searches metadata across opt-in, application-owned saved result snapshots.</summary>
    CatalogSearch,

    /// <summary>Searches the bounded local deterministic semantic index.</summary>
    SemanticSearch,

    /// <summary>Compares stored metadata from two explicit historical catalog snapshots.</summary>
    CatalogComparison,

    /// <summary>Reviews folder restructuring previews, applied history, and structure diagrams.</summary>
    StructureHistory,

    /// <summary>Displays rule-management controls.</summary>
    Rules,

    /// <summary>Displays application settings.</summary>
    Settings,

    /// <summary>Displays local logging health and aggregate diagnostics.</summary>
    Diagnostics,

    /// <summary>Displays explicit undo-record sessions supplied by the application controller.</summary>
    History,

    /// <summary>Displays local contextual product help.</summary>
    Help,

    /// <summary>Displays static application metadata and external-resource requests.</summary>
    About,
}
