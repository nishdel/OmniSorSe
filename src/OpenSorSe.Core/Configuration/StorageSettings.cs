namespace OpenSorSe.Core.Configuration;

/// <summary>Controls application-owned storage without granting access to source files.</summary>
public sealed class StorageSettings
{
    /// <summary>Gets the requested storage root; null keeps the platform default. Changes apply on restart.</summary>
    public string? DirectoryPath { get; init; }

    /// <summary>Gets the configurable combined budget for regenerable caches.</summary>
    public int MaximumCacheSizeMiB { get; init; } = 512;

    /// <summary>Gets the age after which abandoned temporary files may be reclaimed.</summary>
    public int TemporaryRetentionDays { get; init; } = 7;

    /// <summary>Validates path and resource limits without creating a directory.</summary>
    public void Validate()
    {
        if (DirectoryPath is not null &&
            (string.IsNullOrWhiteSpace(DirectoryPath) || !Path.IsPathFullyQualified(DirectoryPath) ||
             DirectoryPath.Length > 1_024 || DirectoryPath.Any(char.IsControl)) ||
            MaximumCacheSizeMiB is < 16 or > 65_536 || TemporaryRetentionDays is < 1 or > 365)
        {
            throw new ConfigurationValidationException("Storage needs an absolute folder, a cache limit of 16–65536 MiB, and temporary retention of 1–365 days.");
        }
    }
}
