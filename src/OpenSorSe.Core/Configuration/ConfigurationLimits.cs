namespace OpenSorSe.Core.Configuration;

/// <summary>
/// Defines fixed bounds for OmniSorSe-owned configuration persistence.
/// </summary>
public static class ConfigurationLimits
{
    /// <summary>Gets the maximum encoded size of the complete settings file.</summary>
    public const long MaximumSettingsFileBytes = 1024L * 1024;
}
