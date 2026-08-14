using OpenSorSe.Application.Models;
using OpenSorSe.Application.SmartTags;
using OpenSorSe.Application.ContentIntelligence;

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
    /// <summary>Gets the canonical Smart Tag type when this row comes from schema-6 authority.</summary>
    public SmartTagType? TagType { get; init; }

    /// <summary>Gets an accessible description that does not rely on color.</summary>
    public string AccessibleName { get; init; } = DisplayName;

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

    /// <summary>Creates an accessible row from one authoritative schema-6 assignment.</summary>
    public static ResultTagRow FromSmartTag(FileSmartTag tag)
    {
        var isUser = tag.Definition.Type == SmartTagType.UserTag;
        var suggestion = tag.State == SmartTagAssignmentState.Suggested;
        var category = isUser ? "Your tags" : suggestion ? "Suggestions" : "Classifications";
        var state = suggestion ? TagAcceptanceState.Suggested : TagAcceptanceState.Accepted;
        var confidence = isUser ? string.Empty : $", {tag.Confidence} confidence";
        var kind = tag.Definition.Type switch
        {
            SmartTagType.Theme => "theme",
            SmartTagType.DocumentType => "document type",
            _ => "user tag",
        };
        var authority = isUser
            ? "Added explicitly by you."
            : $"{tag.Confidence} confidence {(suggestion ? "suggestion" : "classification")}.";
        var explanation = tag.Evidence.Count == 0
            ? $"{authority} Derived from bounded local indexed evidence."
            : $"{authority} {string.Join("; ", tag.Evidence.Take(SmartTagLimits.MaximumEvidencePerAssignment).Select(item => item.Explanation))}";
        return new ResultTagRow(
            tag.Definition.TagId,
            tag.Definition.DisplayName,
            category,
            isUser ? "You" : tag.State == SmartTagAssignmentState.Accepted ? "Accepted" : tag.State == SmartTagAssignmentState.Automatic ? "Local classification" : "Suggestion",
            true,
            state,
            explanation,
            !isUser && suggestion,
            !isUser)
        {
            TagType = tag.Definition.Type,
            AccessibleName = $"{(suggestion ? "Suggested " : string.Empty)}{kind} {tag.Definition.DisplayName}{confidence}, {category}",
        };
    }
}
