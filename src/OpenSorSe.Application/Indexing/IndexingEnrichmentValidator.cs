using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.Semantic;

namespace OpenSorSe.Application.Indexing;

/// <summary>Validates model output structure; it does not establish factual truth or grant filesystem authority.</summary>
public static class IndexingEnrichmentValidator
{
    /// <summary>Parses bounded local inference and attaches application-controlled provenance.</summary>
    public static IndexedContentIntelligence Parse(string json, string providerVersion)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > 32_768 || string.IsNullOrWhiteSpace(providerVersion) || providerVersion.Length > 128)
        {
            throw new InvalidDataException("AI enrichment exceeds its bounds.");
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            RequireProperties(root, "summary", "documentType", "category", "tags", "topics", "entities");
            var summary = ReadText(root.GetProperty("summary"), 2048, allowEmpty: true, term: false);
            var documentType = ReadText(root.GetProperty("documentType"), 80, allowEmpty: true);
            var category = ReadText(root.GetProperty("category"), 80, allowEmpty: true);
            var tags = ReadTerms(root.GetProperty("tags"), 32);
            var topics = ReadTerms(root.GetProperty("topics"), 16);
            var entities = root.GetProperty("entities");
            RequireArray(entities, 16);
            var concepts = new List<ContentConcept>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entity in entities.EnumerateArray())
            {
                RequireProperties(entity, "kind", "name");
                var kindText = ReadText(entity.GetProperty("kind"), 32);
                if (!Enum.TryParse<ContentConceptKind>(kindText, ignoreCase: false, out var kind) ||
                    !Enum.IsDefined(kind) || kind is ContentConceptKind.Topic or ContentConceptKind.Keyword ||
                    !string.Equals(kind.ToString(), kindText, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("AI enrichment contains an unsupported entity kind.");
                }

                var name = ReadText(entity.GetProperty("name"), 80);
                var normalized = SearchTextNormalizer.Normalize(name);
                if (!seen.Add($"{kind}:{normalized}"))
                {
                    throw new InvalidDataException("AI enrichment contains duplicate entities.");
                }

                concepts.Add(Concept(kind, name, providerVersion));
            }

            var searchable = tags.Concat(topics).Concat(concepts.Select(item => item.DisplayName))
                .Append(documentType).Append(category)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToArray();
            return new IndexedContentIntelligence
            {
                Origin = ContentIntelligenceOrigin.AiDerived,
                DocumentType = string.IsNullOrEmpty(documentType) ? null : documentType,
                Category = string.IsNullOrEmpty(category) ? null : category,
                Topics = topics.Select(topic => Concept(ContentConceptKind.Topic, topic, providerVersion)).ToArray(),
                Entities = concepts,
                Keywords = searchable,
                Summary = string.IsNullOrEmpty(summary) ? null : new ContentSummaryEvidence
                {
                    Text = summary,
                    Provider = "ollama-indexing",
                    ProviderVersion = providerVersion,
                    Origin = ContentIntelligenceOrigin.AiDerived,
                },
                Provider = "ollama-indexing",
                ProviderVersion = providerVersion,
                ProcessingFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(providerVersion + "|" + json))).ToLowerInvariant(),
            };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("AI enrichment is not a valid JSON object.", exception);
        }
    }

    private static ContentConcept Concept(ContentConceptKind kind, string name, string version) => new()
    {
        Kind = kind,
        DisplayName = name,
        NormalizedValue = SearchTextNormalizer.Normalize(name),
        Confidence = ContentIntelligenceConfidence.Limited,
        Provider = "ollama-indexing",
        ProviderVersion = version,
        Origin = ContentIntelligenceOrigin.AiDerived,
    };

    private static void RequireProperties(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("AI enrichment expected an object.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
            {
                throw new InvalidDataException("AI enrichment contains unknown or duplicate fields.");
            }
        }

        if (seen.Count != names.Length)
        {
            throw new InvalidDataException("AI enrichment is missing required fields.");
        }
    }

    private static IReadOnlyList<string> ReadTerms(JsonElement element, int maximum)
    {
        RequireArray(element, maximum);
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in element.EnumerateArray())
        {
            var value = ReadText(item, 80);
            if (!seen.Add(SearchTextNormalizer.Normalize(value)))
            {
                throw new InvalidDataException("AI enrichment contains duplicate terms.");
            }

            values.Add(value);
        }

        return values;
    }

    private static void RequireArray(JsonElement element, int maximum)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > maximum)
        {
            throw new InvalidDataException("AI enrichment array is malformed or exceeds its bound.");
        }
    }

    private static string ReadText(JsonElement element, int maximum, bool allowEmpty = false, bool term = true)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("AI enrichment expected text.");
        }

        var value = element.GetString()!.Trim();
        if (value.Length > maximum || value.Any(char.IsControl) ||
            (!allowEmpty || value.Length > 0) && !value.Any(char.IsLetterOrDigit) ||
            value.Contains('\\') || value.Contains("../", StringComparison.Ordinal) ||
            value.Contains(":/", StringComparison.Ordinal) ||
            value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(part => part.StartsWith('/')) ||
            term && (value.Contains('/') || value.Contains(':') || value.Contains("..", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("AI enrichment contains invalid or path-like text.");
        }

        return value;
    }
}
