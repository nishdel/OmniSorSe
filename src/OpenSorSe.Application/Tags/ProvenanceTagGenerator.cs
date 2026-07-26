using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.Models;

namespace OpenSorSe.Application.Tags;

/// <summary>Derives bounded provenance-aware system and candidate tags from local metadata and text.</summary>
public static class ProvenanceTagGenerator
{
    private const int MaximumGeneratedTags = 32;
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "about", "after", "before", "could", "document", "file", "from", "have",
        "into", "other", "page", "that", "their", "there", "these", "this", "with",
        "aber", "datei", "dokument", "eine", "einer", "eines", "seite", "seiten",
        "und", "oder", "diese", "dieser", "dieses", "mit", "von", "zum", "zur",
    };

    /// <summary>Generates deterministic tags without promoting uncertain text candidates to confirmed state.</summary>
    public static IReadOnlyList<TagAssociation> Generate(
        string fullPath,
        string sourceFingerprint,
        IReadOnlyList<ExtractedMetadataField> metadata,
        string? nativeText,
        string? ocrText,
        DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(fullPath) ||
            !Path.IsPathRooted(fullPath) ||
            string.IsNullOrWhiteSpace(sourceFingerprint) ||
            metadata is null ||
            createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A known path, source fingerprint, metadata, and UTC timestamp are required.");
        }

        var tags = new List<TagAssociation>();
        var extension = Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant();
        if (extension.Length > 0)
        {
            Add(
                tags,
                fullPath,
                extension,
                $"File type: {extension.ToUpperInvariant()}",
                "File type",
                TagSource.FileType,
                TagAcceptanceState.Accepted,
                1,
                true,
                sourceFingerprint,
                createdAtUtc,
                "Confirmed from the known filename extension.");
        }

        foreach (var field in metadata)
        {
            if (field.Name is "Created UTC" or "Modified UTC" or "Document created" or "Document modified" &&
                DateTimeOffset.TryParse(
                    field.Value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var date))
            {
                var year = date.Year.ToString(CultureInfo.InvariantCulture);
                Add(
                    tags,
                    fullPath,
                    year,
                    year,
                    "Date",
                    TagSource.Date,
                    TagAcceptanceState.Accepted,
                    1,
                    true,
                    sourceFingerprint,
                    createdAtUtc,
                    $"Confirmed from {field.Name.ToLowerInvariant()}.");
            }

            if (field.Name == "Keywords")
            {
                foreach (var keyword in field.Value.Split(
                    [',', ';', '|'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(8))
                {
                    Add(
                        tags,
                        fullPath,
                        keyword,
                        keyword,
                        "Embedded metadata",
                        TagSource.EmbeddedMetadata,
                        TagAcceptanceState.Suggested,
                        0.9,
                        false,
                        sourceFingerprint,
                        createdAtUtc,
                        "Suggested from embedded document keywords.");
                }
            }
        }

        var folder = Directory.GetParent(fullPath)?.Name;
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Add(
                tags,
                fullPath,
                folder,
                folder,
                "Folder context",
                TagSource.FolderContext,
                TagAcceptanceState.Suggested,
                0.8,
                false,
                sourceFingerprint,
                createdAtUtc,
                "Suggested from the current containing-folder name.");
        }

        AddTextCandidates(
            tags,
            fullPath,
            nativeText,
            "Native text",
            TagSource.Deterministic,
            0.65,
            sourceFingerprint,
            createdAtUtc);
        AddTextCandidates(
            tags,
            fullPath,
            ocrText,
            "OCR candidate",
            TagSource.OcrCandidate,
            0.55,
            sourceFingerprint,
            createdAtUtc);
        return Array.AsReadOnly(tags
            .GroupBy(tag => tag.NormalizedValue, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(tag => tag.AcceptanceState == TagAcceptanceState.Accepted)
                .ThenByDescending(tag => tag.Confidence)
                .First())
            .OrderByDescending(tag => tag.AcceptanceState == TagAcceptanceState.Accepted)
            .ThenBy(tag => tag.NormalizedValue, StringComparer.Ordinal)
            .Take(MaximumGeneratedTags)
            .ToArray());
    }

    private static void AddTextCandidates(
        List<TagAssociation> tags,
        string fullPath,
        string? text,
        string category,
        TagSource source,
        double confidence,
        string fingerprint,
        DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var candidates = text.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(value => value.Length is >= 5 and <= 32 && !StopWords.Contains(value))
            .GroupBy(value => value, StringComparer.Ordinal)
            .Select(group => new { Value = group.Key, Count = group.Count() })
            .OrderByDescending(candidate => candidate.Count)
            .ThenBy(candidate => candidate.Value, StringComparer.Ordinal)
            .Take(5);
        foreach (var candidate in candidates)
        {
            Add(
                tags,
                fullPath,
                candidate.Value,
                candidate.Value,
                category,
                source,
                TagAcceptanceState.Suggested,
                confidence,
                false,
                fingerprint,
                createdAtUtc,
                source == TagSource.OcrCandidate
                    ? "Suggested from bounded local OCR text; not confirmed."
                    : "Suggested from bounded native text; not confirmed.");
        }
    }

    private static void Add(
        List<TagAssociation> tags,
        string fullPath,
        string rawValue,
        string displayName,
        string category,
        TagSource source,
        TagAcceptanceState state,
        double confidence,
        bool isSystem,
        string fingerprint,
        DateTimeOffset createdAtUtc,
        string explanation)
    {
        var normalized = Normalize(rawValue);
        if (normalized.Length is 0 or > 64 ||
            tags.Any(tag => string.Equals(tag.NormalizedValue, normalized, StringComparison.Ordinal)))
        {
            return;
        }

        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)))[..16]
            .ToLowerInvariant();
        tags.Add(new TagAssociation(
            $"tag:generated:{identity}:{source}:{normalized}",
            fullPath,
            ContentText.NormalizeField(displayName, 64),
            normalized,
            category,
            source,
            state,
            explanation,
            createdAtUtc)
        {
            Confidence = confidence,
            UpdatedAtUtc = createdAtUtc,
            ProvenanceDetails = explanation,
            SourceFingerprint = fingerprint,
            IsSystem = isSystem,
        });
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        var separator = false;
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLower(character, CultureInfo.InvariantCulture));
                separator = false;
            }
            else if (!separator)
            {
                builder.Append('-');
                separator = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}
