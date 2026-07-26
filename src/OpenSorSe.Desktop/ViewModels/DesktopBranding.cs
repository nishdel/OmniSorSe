namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Centralizes the concise product identity used by the native desktop shell.
/// </summary>
public static class DesktopBranding
{
    /// <summary>Gets the product name used for the window title and sidebar heading.</summary>
    public const string ProductName = "OpenSorSe";

    /// <summary>Gets the expanded product name shown beneath the heading.</summary>
    public const string ExpandedName = "OPEN SORT AND SEARCH";

    /// <summary>Gets the restrained product promise shown in the shell.</summary>
    public const string Tagline = "Find clarity in your files";

    /// <summary>Gets the Avalonia resource URI for the official compact application mark.</summary>
    public const string AppIconResourceUri = "avares://OpenSorSe/Assets/opensorse-app-icon.png";
}
