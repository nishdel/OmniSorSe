using System.Reflection;

namespace OpenSorSe.Core;

/// <summary>Provides the current product version used in persisted provenance and UI metadata.</summary>
public static class ApplicationVersionInfo
{
    /// <summary>Gets the current semantic product version.</summary>
    public static string Current { get; } = ReadInformationalVersion().Split('+', 2)[0];

    /// <summary>Gets the concise version displayed in the desktop application.</summary>
    public static string Display => Current;

    /// <summary>Gets the bounded source revision injected by release validation.</summary>
    public static string SourceRevision { get; } = ReadMetadata("SourceRevision") ?? "unversioned";

    /// <summary>Gets the build configuration injected into assembly metadata.</summary>
    public static string BuildConfiguration { get; } = ReadMetadata("BuildConfiguration") ?? "Unknown";

    /// <summary>Gets a concise traceable identity suitable for About, health, and support data.</summary>
    public static string Provenance => $"{Current} ({SourceRevision}, {BuildConfiguration})";

    private static string ReadInformationalVersion() =>
        typeof(ApplicationVersionInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.0.0+unversioned";

    private static string? ReadMetadata(string key) =>
        typeof(ApplicationVersionInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))?
            .Value;
}
