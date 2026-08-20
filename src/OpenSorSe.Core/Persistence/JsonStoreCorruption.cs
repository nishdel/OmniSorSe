namespace OpenSorSe.Core.Persistence;

/// <summary>Classifies the recovery policy for one application-owned JSON store.</summary>
public enum JsonStoreAuthority
{
    /// <summary>The store contains only derived data and may be rebuilt safely.</summary>
    Rebuildable,
    /// <summary>The store contains user-authored state that must not be silently discarded or overwritten.</summary>
    UserAuthored,
    /// <summary>The store is required to determine whether filesystem mutation or recovery is safe.</summary>
    MutationRecovery,
}

/// <summary>Reports preserved corruption in an authoritative JSON store.</summary>
public sealed class AuthoritativeStoreCorruptionException : IOException
{
    /// <summary>Creates one fail-closed corruption report.</summary>
    public AuthoritativeStoreCorruptionException(
        string storeName,
        string storePath,
        JsonStoreAuthority authority,
        string? preservedCopyPath,
        Exception innerException)
        : base($"The {storeName} is malformed or unsupported and was preserved. Repair or restore this state before continuing the affected operation.", innerException)
    {
        StoreName = storeName;
        StorePath = storePath;
        Authority = authority;
        PreservedCopyPath = preservedCopyPath;
    }

    /// <summary>Gets the non-content store name.</summary>
    public string StoreName { get; }

    /// <summary>Gets the owned store path for explicit recovery UI and maintainer tooling.</summary>
    public string StorePath { get; }

    /// <summary>Gets the store authority classification.</summary>
    public JsonStoreAuthority Authority { get; }

    /// <summary>Gets the forensic copy created without replacing the original, when available.</summary>
    public string? PreservedCopyPath { get; }
}

/// <summary>Preserves authoritative JSON corruption consistently without treating it as empty state.</summary>
public static class JsonStoreCorruption
{
    /// <summary>Creates one diagnostic copy and a fail-closed exception while retaining the original file.</summary>
    public static AuthoritativeStoreCorruptionException Preserve(
        string storeName,
        string storePath,
        JsonStoreAuthority authority,
        Exception cause)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);
        ArgumentNullException.ThrowIfNull(cause);
        var fullPath = Path.GetFullPath(storePath);
        string? copyPath = null;
        try
        {
            if (File.Exists(fullPath))
            {
                copyPath = $"{fullPath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.json";
                File.Copy(fullPath, copyPath, overwrite: false);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            copyPath = null;
        }

        return new AuthoritativeStoreCorruptionException(
            storeName,
            fullPath,
            authority,
            copyPath,
            cause);
    }
}
