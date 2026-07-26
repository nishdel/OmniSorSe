using OpenSorSe.Application.Models;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Presents one accepted application-owned tag for the selected result file.
/// </summary>
public sealed record ResultTagRow(
    string TagId,
    string DisplayName,
    string Category,
    string Source,
    bool IsRemovable,
    TagAcceptanceState AcceptanceState,
    string? Explanation,
    bool CanAccept,
    bool CanReject)
{
    /// <summary>Creates a display row from a validated tag association.</summary>
    public static ResultTagRow FromAssociation(TagAssociation tag) => new(
        tag.TagId,
        tag.DisplayName,
        tag.Category,
        tag.Source switch
        {
            TagSource.Deterministic => "Derived",
            TagSource.AiSuggestion => "AI suggestion",
            TagSource.UserApproved => "User approved",
            TagSource.Preference => "Local preference",
            TagSource.EmbeddedMetadata => "Embedded metadata",
            TagSource.OcrCandidate => "OCR candidate",
            TagSource.SemanticCandidate => "Semantic candidate",
            TagSource.FileType => "File type",
            TagSource.Date => "Date",
            TagSource.FolderContext => "Folder context",
            _ => "Application",
        },
        tag.Source is TagSource.UserApproved or TagSource.AiSuggestion or TagSource.Preference,
        tag.AcceptanceState,
        tag.ProvenanceDetails ?? tag.Explanation,
        tag.AcceptanceState == TagAcceptanceState.Suggested && !tag.IsSystem,
        tag.AcceptanceState == TagAcceptanceState.Suggested && !tag.IsSystem);
}
