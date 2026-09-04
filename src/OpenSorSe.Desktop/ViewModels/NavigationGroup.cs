namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Classifies shell destinations for progressive disclosure.</summary>
public enum NavigationGroup
{
    /// <summary>Everyday application workflows.</summary>
    Primary,

    /// <summary>Secondary discovery tools that supplement the Scan, Review, and Organize flow.</summary>
    Secondary,

    /// <summary>Durable library and automation workflows.</summary>
    Library,

    /// <summary>Specialist and maintenance workflows.</summary>
    Advanced,

    /// <summary>Product help and information in the sidebar footer.</summary>
    Footer,
}
