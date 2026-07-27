#pragma warning disable CS1591

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OpenSorSe.Extensions.Abstractions;

namespace OpenSorSe.Application.Plugins;

/// <summary>
/// Parses an untrusted plugin manifest into a bounded immutable host model.
/// </summary>
/// <remarks>
/// Parsing is intentionally strict: comments, trailing commas, unknown or
/// duplicate members, excessive depth/size, invalid identifiers, unsafe paths,
/// incompatible versions, and inconsistent contribution/capability data are
/// rejected before any assembly is loaded. Compatibility issues remain
/// inspectable but can never become activation-ready.
/// </remarks>
public sealed partial class PluginManifestParser : IPluginManifestParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        MaxDepth = PluginLimits.MaximumManifestDepth,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    [GeneratedRegex(
        "^[a-z0-9](?:[a-z0-9.-]{0,126}[a-z0-9])?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(
        "^[A-Za-z_][A-Za-z0-9_.+`]{0,511}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex EntryTypeRegex();

    [GeneratedRegex(
        "^[a-z0-9](?:[a-z0-9.-]{0,62}[a-z0-9])?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex RuntimeIdentifierRegex();

    public PluginManifestParseResult ParseFile(string manifestPath, bool expectedBuiltIn = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        try
        {
            var info = new FileInfo(manifestPath);
            if (!info.Exists)
            {
                return Invalid("manifest.missing", "The plugin manifest is missing.");
            }

            if (info.Length <= 0 || info.Length > PluginLimits.MaximumManifestBytes)
            {
                return Invalid("manifest.size", "The plugin manifest is empty or exceeds the size limit.");
            }

            return Parse(File.ReadAllBytes(manifestPath), expectedBuiltIn);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Invalid("manifest.read", "The plugin manifest could not be read safely.");
        }
    }

    public PluginManifestParseResult Parse(ReadOnlySpan<byte> utf8Json, bool expectedBuiltIn = false)
    {
        if (utf8Json.Length <= 0 || utf8Json.Length > PluginLimits.MaximumManifestBytes)
        {
            return Invalid("manifest.size", "The plugin manifest is empty or exceeds the size limit.");
        }

        PluginManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PluginManifest>(utf8Json, SerializerOptions);
        }
        catch (JsonException)
        {
            return Invalid("manifest.json", "The plugin manifest is malformed or contains unsupported fields.");
        }

        if (manifest is null)
        {
            return Invalid("manifest.json", "The plugin manifest does not contain an object.");
        }

        var issues = Validate(manifest, expectedBuiltIn);
        return new PluginManifestParseResult(
            issues.Any(issue => issue.IsBlocking) ? null : Clone(manifest),
            Array.AsReadOnly(issues.ToArray()));
    }

    public static PluginManifest Clone(PluginManifest manifest) =>
        manifest with
        {
            Contributions = Array.AsReadOnly(manifest.Contributions.ToArray()),
            Capabilities = Array.AsReadOnly(manifest.Capabilities.ToArray()),
            Dependencies = Array.AsReadOnly(manifest.Dependencies.ToArray()),
            SupportedRuntimeIdentifiers = Array.AsReadOnly(
                manifest.SupportedRuntimeIdentifiers.ToArray()),
        };

    public static bool TryVersion(string? value, out Version version)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 64 ||
            value.StartsWith('+') ||
            value.Split('.').Length is < 2 or > 4 ||
            !value.Split('.').All(part =>
                part.Length > 0 &&
                part.All(char.IsAsciiDigit)))
        {
            version = new Version();
            return false;
        }

        return Version.TryParse(value, out version!);
    }

    public static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Length > 512 ||
            Path.IsPathRooted(path) ||
            path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');
        return normalized.Split('/').All(segment =>
            segment.Length > 0 &&
            segment is not "." and not ".." &&
            !segment.Any(char.IsControl));
    }

    private static List<PluginManifestIssue> Validate(
        PluginManifest manifest,
        bool expectedBuiltIn)
    {
        var issues = new List<PluginManifestIssue>();
        if (manifest.ManifestSchemaVersion != PluginLimits.CurrentManifestSchemaVersion)
        {
            Add(issues, "manifest.schema", "The plugin manifest schema version is unsupported.");
        }

        if (!ValidIdentifier(manifest.PluginId))
        {
            Add(issues, "manifest.plugin-id", "The plugin ID is invalid.");
        }

        if (!TryVersion(manifest.PluginVersion, out _))
        {
            Add(issues, "manifest.version", "The plugin version is invalid.");
        }

        if (!TryVersion(manifest.MinimumOpenSorSeVersion, out var minimum))
        {
            Add(issues, "manifest.minimum-host", "The minimum OpenSorSe version is invalid.");
        }

        Version? maximum = null;
        if (manifest.MaximumOpenSorSeVersion is not null)
        {
            if (!TryVersion(manifest.MaximumOpenSorSeVersion, out var parsedMaximum))
            {
                Add(issues, "manifest.maximum-host", "The maximum OpenSorSe version is invalid.");
            }
            else
            {
                maximum = parsedMaximum;
            }
        }

        if (minimum is not null && maximum is not null && minimum > maximum)
        {
            Add(issues, "manifest.host-range", "The supported OpenSorSe version range is contradictory.");
        }

        if (!Bounded(manifest.DisplayName, 256) ||
            !Bounded(manifest.Description, PluginLimits.MaximumStringCharacters) ||
            !Bounded(manifest.Publisher, 256) ||
            !Bounded(manifest.LicenseIdentifier, 128) ||
            !Bounded(manifest.RuntimeCompatibility, 64))
        {
            Add(issues, "manifest.required-text", "Required plugin text is missing or excessive.");
        }

        if (!string.Equals(manifest.RuntimeCompatibility, "net8.0", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new PluginManifestIssue(
                "manifest.runtime",
                "The plugin runtime is incompatible with the v1.5 host.",
                IsBlocking: false));
        }

        if (!IsSafeRelativePath(manifest.EntryAssembly) ||
            !string.Equals(Path.GetExtension(manifest.EntryAssembly), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            Add(issues, "manifest.entry-assembly", "The entry assembly path is invalid.");
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryType) ||
            !EntryTypeRegex().IsMatch(manifest.EntryType))
        {
            Add(issues, "manifest.entry-type", "The entry type is invalid.");
        }

        if (manifest.BuiltIn != expectedBuiltIn)
        {
            Add(
                issues,
                "manifest.origin",
                expectedBuiltIn
                    ? "A built-in plugin manifest must declare built-in status."
                    : "An external plugin may not claim built-in status.");
        }

        ValidateOptionalUri(manifest.Homepage, "manifest.homepage", issues);
        ValidateOptionalUri(manifest.SourceRepository, "manifest.source", issues);

        if (manifest.Contributions is null ||
            manifest.Capabilities is null ||
            manifest.Dependencies is null ||
            manifest.SupportedRuntimeIdentifiers is null)
        {
            Add(issues, "manifest.collections", "Manifest collections must be present.");
            return issues;
        }

        if (manifest.Contributions.Count is < 1 or > PluginLimits.MaximumContributionsPerPlugin ||
            manifest.Dependencies.Count > PluginLimits.MaximumDependenciesPerPlugin ||
            manifest.SupportedRuntimeIdentifiers.Count > PluginLimits.MaximumRuntimeIdentifiersPerPlugin)
        {
            Add(issues, "manifest.bounds", "The manifest contains an excessive number of contributions or dependencies.");
        }

        if (manifest.SupportedRuntimeIdentifiers.Any(runtimeIdentifier =>
                string.IsNullOrWhiteSpace(runtimeIdentifier) ||
                !RuntimeIdentifierRegex().IsMatch(runtimeIdentifier)) ||
            manifest.SupportedRuntimeIdentifiers
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != manifest.SupportedRuntimeIdentifiers.Count)
        {
            Add(
                issues,
                "manifest.runtime-identifiers",
                "Runtime identifiers must be bounded, valid, and unique.");
        }

        if (manifest.ContainsNativeDependencies &&
            manifest.SupportedRuntimeIdentifiers.Count == 0)
        {
            Add(
                issues,
                "manifest.native-platform",
                "A plugin with native dependencies must declare its supported runtime identifiers.");
        }

        foreach (var contribution in manifest.Contributions)
        {
            if (contribution is null ||
                !ValidIdentifier(contribution.ContributionId) ||
                !Bounded(contribution.DisplayName, 256) ||
                !Enum.IsDefined(contribution.ExtensionPoint) ||
                contribution.Priority is < -10_000 or > 10_000)
            {
                Add(issues, "manifest.contribution", "A declared contribution is invalid.");
            }
        }

        if (manifest.Contributions
            .Where(value => value is not null)
            .Select(value => value.ContributionId)
            .Distinct(StringComparer.Ordinal)
            .Count() != manifest.Contributions.Count)
        {
            Add(issues, "manifest.contribution-duplicate", "Contribution IDs must be unique within a plugin.");
        }

        if (manifest.Capabilities.Any(capability => !Enum.IsDefined(capability)) ||
            manifest.Capabilities.Distinct().Count() != manifest.Capabilities.Count)
        {
            Add(issues, "manifest.capability", "Declared capabilities must be supported and unique.");
        }

        foreach (var dependency in manifest.Dependencies)
        {
            if (dependency is null ||
                !ValidIdentifier(dependency.PluginId) ||
                !TryVersion(dependency.MinimumVersion, out var dependencyMinimum))
            {
                Add(issues, "manifest.dependency", "A plugin dependency is invalid.");
                continue;
            }

            if (string.Equals(dependency.PluginId, manifest.PluginId, StringComparison.Ordinal))
            {
                Add(issues, "manifest.dependency-self", "A plugin cannot depend on itself.");
            }

            if (dependency.MaximumVersion is { } dependencyMaximumText)
            {
                if (!TryVersion(dependencyMaximumText, out var dependencyMaximum) ||
                    dependencyMinimum > dependencyMaximum)
                {
                    Add(issues, "manifest.dependency-range", "A plugin dependency range is invalid.");
                }
            }
        }

        if (manifest.Dependencies
            .Where(value => value is not null)
            .Select(value => value.PluginId)
            .Distinct(StringComparer.Ordinal)
            .Count() != manifest.Dependencies.Count)
        {
            Add(issues, "manifest.dependency-conflict", "A plugin may declare each dependency only once.");
        }

        if (manifest.Integrity is not null &&
            (!string.Equals(manifest.Integrity.Algorithm, "SHA-256", StringComparison.OrdinalIgnoreCase) ||
             manifest.Integrity.Hash.Length != 64 ||
             !manifest.Integrity.Hash.All(char.IsAsciiHexDigit)))
        {
            Add(issues, "manifest.integrity", "Manifest integrity metadata is invalid.");
        }

        return issues;
    }

    private static bool ValidIdentifier(string? value) =>
        value is { Length: <= PluginLimits.MaximumIdentifierCharacters } &&
        IdentifierRegex().IsMatch(value) &&
        !IsReservedFileNameSegment(value);

    internal static bool IsReservedFileNameSegment(string value)
    {
        var name = value.Split('.')[0].TrimEnd(' ', '.');
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               name.Length == 4 &&
               (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
               name[3] is >= '1' and <= '9';
    }

    private static bool Bounded(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum &&
        !value.Any(char.IsControl);

    private static void ValidateOptionalUri(
        string? value,
        string code,
        List<PluginManifestIssue> issues)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length > PluginLimits.MaximumStringCharacters ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http"))
        {
            Add(issues, code, "An optional plugin link is invalid.");
        }
    }

    private static void Add(
        List<PluginManifestIssue> issues,
        string code,
        string message) =>
        issues.Add(new PluginManifestIssue(code, message));

    private static PluginManifestParseResult Invalid(string code, string message) =>
        new(null, [new PluginManifestIssue(code, message)]);
}
