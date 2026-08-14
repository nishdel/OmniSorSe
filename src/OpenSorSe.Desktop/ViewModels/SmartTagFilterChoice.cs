using OpenSorSe.Application.SmartTags;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Presents one canonical schema-6 definition for the existing Search filter surface.</summary>
public sealed record SmartTagFilterChoice(string TagId, string DisplayName, SmartTagType Type)
{
    /// <summary>Gets the understandable type label.</summary>
    public string TypeLabel => Type switch
    {
        SmartTagType.Theme => "Theme",
        SmartTagType.DocumentType => "Document Type",
        _ => "User Tag",
    };
}
