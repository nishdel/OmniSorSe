namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Classifies shell destinations for progressive disclosure.</summary>
public enum NavigationGroup
{
    /// <summary>Everyday application workflows.</summary>
    Primary,

    /// <summary>Library and automation workflows shown below the primary tools.</summary>
    Secondary,

    /// <summary>Specialist and maintenance workflows.</summary>
    Advanced,

    /// <summary>Product help and information in the sidebar footer.</summary>
    Footer,
}
