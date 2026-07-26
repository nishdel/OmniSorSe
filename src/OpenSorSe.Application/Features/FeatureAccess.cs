using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Features;

/// <summary>
/// Describes reusable visibility and availability requirements for one user-facing feature.
/// </summary>
/// <param name="RequiresAdvanced">Whether advanced mode is required.</param>
/// <param name="RequiresAi">Whether the global AI switch is required.</param>
/// <param name="Capability">The independently enabled AI capability, when applicable.</param>
/// <param name="RequiresSemanticSearch">Whether local Semantic Search Beta must be enabled.</param>
public sealed record FeatureRequirement(
    bool RequiresAdvanced = false,
    bool RequiresAi = false,
    AiCapability? Capability = null,
    bool RequiresSemanticSearch = false)
{
    /// <summary>Gets an always-available regular feature requirement.</summary>
    public static FeatureRequirement Regular { get; } = new();

    /// <summary>Gets a non-AI feature that requires advanced mode.</summary>
    public static FeatureRequirement Advanced { get; } = new(RequiresAdvanced: true);

    /// <summary>Gets the local Semantic Search Beta feature requirement.</summary>
    public static FeatureRequirement SemanticSearch { get; } = new(RequiresSemanticSearch: true);

    /// <summary>Creates a requirement for one regular AI capability.</summary>
    public static FeatureRequirement ForAi(AiCapability capability) => new(RequiresAi: true, Capability: capability);

    /// <summary>Creates a requirement for one advanced AI capability.</summary>
    public static FeatureRequirement ForAdvancedAi(AiCapability? capability = null) =>
        new(RequiresAdvanced: true, RequiresAi: true, Capability: capability);
}

/// <summary>
/// Centralizes the AI and advanced-feature truth table used by application and presentation layers.
/// </summary>
public static class FeatureAccess
{
    /// <summary>Evaluates one feature requirement against validated application settings.</summary>
    public static bool IsEnabled(ApplicationSettings settings, FeatureRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(requirement);

        if (requirement.RequiresAdvanced && !settings.Features.ShowAdvancedFeatures)
        {
            return false;
        }

        if ((requirement.RequiresAi || requirement.Capability is not null) && !settings.Ai.Enabled)
        {
            return false;
        }

        if (requirement.RequiresSemanticSearch && !settings.SemanticSearch.Enabled)
        {
            return false;
        }

        return requirement.Capability is null || settings.Ai.IsCapabilityEnabled(requirement.Capability.Value);
    }
}
