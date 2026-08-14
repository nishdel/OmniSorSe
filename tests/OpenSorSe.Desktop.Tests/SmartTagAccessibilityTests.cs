using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.SmartTags;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Validates accessible, non-color-only Smart Tag review projections.</summary>
public sealed class SmartTagAccessibilityTests
{
    /// <summary>A Moderate suggestion exposes type, state, confidence, and actions in text.</summary>
    [Fact]
    public void SuggestedThemeHasAccessibleStateAndActions()
    {
        var row = ResultTagRow.FromSmartTag(Tag(
            SmartTagType.Theme,
            SmartTagAssignmentState.Suggested,
            ContentIntelligenceConfidence.Moderate));

        Assert.Equal("Suggestions", row.Category);
        Assert.True(row.CanAccept);
        Assert.True(row.CanReject);
        Assert.Contains("Suggested theme Finance", row.AccessibleName, StringComparison.Ordinal);
        Assert.Contains("Moderate confidence", row.AccessibleName, StringComparison.Ordinal);
        Assert.Contains("Moderate confidence", row.Explanation, StringComparison.Ordinal);
        Assert.Contains("Native content", row.Explanation, StringComparison.Ordinal);
    }

    /// <summary>User Tags are described as explicit authority and are not presented as suggestions.</summary>
    [Fact]
    public void UserTagHasDistinctAccessibleAuthority()
    {
        var row = ResultTagRow.FromSmartTag(Tag(
            SmartTagType.UserTag,
            SmartTagAssignmentState.Accepted,
            ContentIntelligenceConfidence.Strong));

        Assert.Equal("Your tags", row.Category);
        Assert.Equal("You", row.Source);
        Assert.False(row.CanAccept);
        Assert.False(row.CanReject);
        Assert.True(row.IsRemovable);
        Assert.Contains("user tag Finance", row.AccessibleName, StringComparison.Ordinal);
    }

    private static FileSmartTag Tag(
        SmartTagType type,
        SmartTagAssignmentState state,
        ContentIntelligenceConfidence confidence) => new()
        {
            FileId = "file",
            Definition = new SmartTagDefinition
            {
                TagId = type == SmartTagType.UserTag ? "user.finance" : "theme.finance",
                Type = type,
                CanonicalKey = "finance",
                DisplayName = "Finance",
                TaxonomyVersion = type == SmartTagType.UserTag ? "user" : "1.0",
                Origin = type == SmartTagType.UserTag ? SmartTagOrigin.User : SmartTagOrigin.BuiltInTaxonomy,
                IsBuiltIn = type != SmartTagType.UserTag,
            },
            Confidence = confidence,
            Origin = type == SmartTagType.UserTag ? SmartTagOrigin.User : SmartTagOrigin.DeterministicClassifier,
            State = state,
            Decision = type == SmartTagType.UserTag ? SmartTagDecision.Accepted : SmartTagDecision.None,
            Evidence = [new SmartTagEvidence(ContentEvidenceSourceKind.ExtractedText, "native:finance", "Native content matched Finance.")],
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
        };
}
