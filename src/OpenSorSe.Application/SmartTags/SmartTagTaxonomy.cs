using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSorSe.Application.SmartTags;

/// <summary>Loads and validates the small, local, versioned built-in Smart Tag taxonomy.</summary>
public sealed class SmartTagTaxonomy
{
    private const string ResourceSuffix = "SmartTags.Resources.smart-tags.en.v1.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Initializes a validated immutable taxonomy.</summary>
    public SmartTagTaxonomy(string version, string locale, IReadOnlyList<SmartTagDefinition> definitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        ArgumentNullException.ThrowIfNull(definitions);
        Version = version;
        Locale = locale;
        Definitions = Validate(version, locale, definitions);
        ById = Definitions.ToDictionary(item => item.TagId, StringComparer.Ordinal);
    }

    /// <summary>Gets the taxonomy version.</summary>
    public string Version { get; }
    /// <summary>Gets the display locale.</summary>
    public string Locale { get; }
    /// <summary>Gets validated definitions in stable ID order.</summary>
    public IReadOnlyList<SmartTagDefinition> Definitions { get; }
    /// <summary>Gets definitions by canonical stable ID.</summary>
    public IReadOnlyDictionary<string, SmartTagDefinition> ById { get; }

    /// <summary>Loads the assembly-owned English v1 taxonomy.</summary>
    public static SmartTagTaxonomy LoadBuiltIn()
    {
        var assembly = typeof(SmartTagTaxonomy).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(ResourceSuffix, StringComparison.Ordinal));
        if (resourceName is null)
        {
            throw new InvalidDataException("The built-in Smart Tag taxonomy resource is missing.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException("The built-in Smart Tag taxonomy resource could not be opened.");
        var document = JsonSerializer.Deserialize<TaxonomyDocument>(stream, JsonOptions)
            ?? throw new InvalidDataException("The built-in Smart Tag taxonomy is empty.");
        var definitions = (document.Definitions ?? [])
            .Select(item => new SmartTagDefinition
            {
                TagId = item.Id ?? string.Empty,
                Type = ParseType(item.Type),
                CanonicalKey = item.Key ?? string.Empty,
                DisplayName = item.Label ?? string.Empty,
                ParentTagId = item.ParentId,
                TaxonomyVersion = document.Version ?? string.Empty,
                Origin = SmartTagOrigin.BuiltInTaxonomy,
                IsBuiltIn = true,
                Aliases = Array.AsReadOnly((item.Aliases ?? []).ToArray()),
                StrongPhrases = Array.AsReadOnly((item.StrongPhrases ?? []).ToArray()),
            })
            .ToArray();
        return new SmartTagTaxonomy(document.Version ?? string.Empty, document.Locale ?? string.Empty, definitions);
    }

    private static IReadOnlyList<SmartTagDefinition> Validate(
        string version,
        string locale,
        IReadOnlyList<SmartTagDefinition> definitions)
    {
        if (version.Length > 32 || locale.Length > 16 ||
            definitions.Count is < 1 or > SmartTagLimits.MaximumTaxonomyDefinitions)
        {
            throw new InvalidDataException("The built-in Smart Tag taxonomy exceeds its supported bounds.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<(SmartTagType Type, string Key)>();
        foreach (var item in definitions)
        {
            if (item.Type == SmartTagType.UserTag ||
                !IsCanonical(item.TagId, 96) || !IsCanonical(item.CanonicalKey, 64) ||
                string.IsNullOrWhiteSpace(item.DisplayName) ||
                item.DisplayName.Length > SmartTagLimits.MaximumDisplayNameCharacters ||
                item.TaxonomyVersion != version || !ids.Add(item.TagId) ||
                !keys.Add((item.Type, item.CanonicalKey)) ||
                item.Aliases.Count is < 1 or > 12 || item.StrongPhrases.Count > 8 ||
                item.Aliases.Concat(item.StrongPhrases).Any(value =>
                    string.IsNullOrWhiteSpace(value) || value.Length > 64 || value != value.Trim().ToLowerInvariant()))
            {
                throw new InvalidDataException("The built-in Smart Tag taxonomy contains an invalid or duplicate definition.");
            }
        }

        if (definitions.Any(item => item.ParentTagId is not null &&
            (!ids.Contains(item.ParentTagId) || item.ParentTagId == item.TagId)))
        {
            throw new InvalidDataException("The built-in Smart Tag taxonomy contains an invalid parent reference.");
        }

        var aliasOwners = new HashSet<(SmartTagType Type, string Alias)>();
        if (definitions.SelectMany(item => item.Aliases.Select(alias => (item.Type, Alias: alias)))
            .Any(alias => !aliasOwners.Add(alias)))
        {
            throw new InvalidDataException("The built-in Smart Tag taxonomy contains an ambiguous alias within one type.");
        }

        return Array.AsReadOnly(definitions.OrderBy(item => item.TagId, StringComparer.Ordinal).ToArray());
    }

    private static bool IsCanonical(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength &&
        value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '-');

    private static SmartTagType ParseType(string? value) => value switch
    {
        "theme" => SmartTagType.Theme,
        "document-type" => SmartTagType.DocumentType,
        _ => throw new InvalidDataException("The built-in Smart Tag taxonomy contains an unsupported type."),
    };

    private sealed record TaxonomyDocument(
        string? Version,
        string? Locale,
        IReadOnlyList<TaxonomyItem>? Definitions);

    private sealed record TaxonomyItem(
        string? Id,
        string? Type,
        string? Key,
        string? Label,
        string? ParentId,
        IReadOnlyList<string>? Aliases,
        IReadOnlyList<string>? StrongPhrases);
}
