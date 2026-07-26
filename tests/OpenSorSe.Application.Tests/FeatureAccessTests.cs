using OpenSorSe.Application.Features;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies the centralized AI/advanced feature truth table.</summary>
public sealed class FeatureAccessTests
{
    /// <summary>Verifies local Semantic Search is independent from AI and advanced mode.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void SemanticSearchRequirement_UsesItsIndependentSetting(bool enabled, bool expected)
    {
        var settings = new ApplicationSettings
        {
            SemanticSearch = new SemanticSearchSettings { Enabled = enabled },
        };

        Assert.Equal(expected, FeatureAccess.IsEnabled(settings, FeatureRequirement.SemanticSearch));
    }

    /// <summary>Verifies regular and advanced non-AI features are independent from AI.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void IsEnabled_NonAiRequirements_UseOnlyAdvancedFlag(bool ai, bool advanced)
    {
        var settings = Settings(ai, advanced, rename: true, folder: true);

        Assert.True(FeatureAccess.IsEnabled(settings, FeatureRequirement.Regular));
        Assert.Equal(advanced, FeatureAccess.IsEnabled(settings, FeatureRequirement.Advanced));
    }

    /// <summary>Verifies regular AI capabilities require master plus their independent capability.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void IsEnabled_AiCapability_RequiresMasterAndCapability(bool ai, bool capability, bool expected)
    {
        var settings = Settings(ai, advanced: false, rename: capability, folder: false);

        Assert.Equal(expected, FeatureAccess.IsEnabled(
            settings,
            FeatureRequirement.ForAi(AiCapability.FileRenameSuggestions)));
    }

    /// <summary>Verifies advanced AI features require both independent master switches.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void IsEnabled_AdvancedAi_RequiresBothMasters(bool ai, bool advanced, bool expected)
    {
        Assert.Equal(expected, FeatureAccess.IsEnabled(
            Settings(ai, advanced, rename: true, folder: true),
            FeatureRequirement.ForAdvancedAi()));
    }

    private static ApplicationSettings Settings(bool ai, bool advanced, bool rename, bool folder) => new()
    {
        Features = new FeatureSettings { ShowAdvancedFeatures = advanced },
        Ai = new AiSettings
        {
            Enabled = ai,
            FileRenameSuggestionsEnabled = rename,
            FolderStructureSuggestionsEnabled = folder,
        },
    };
}
