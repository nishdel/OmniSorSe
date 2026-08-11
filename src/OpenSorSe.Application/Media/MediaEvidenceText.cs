using System.Globalization;
using OpenSorSe.Application.Content;

namespace OpenSorSe.Application.Media;

/// <summary>Projects structured media evidence into bounded deterministic indexing fields.</summary>
public static class MediaEvidenceText
{
    /// <summary>Creates plain searchable metadata without including OCR, transcripts, GPS precision, or descriptions.</summary>
    public static string CreateMetadataText(IndexedMediaEvidence? evidence)
    {
        if (evidence is null)
        {
            return string.Empty;
        }

        var metadata = evidence.Metadata;
        var values = new List<string>
        {
            evidence.Kind.ToString(),
            metadata.Container ?? string.Empty,
            metadata.Width.HasValue && metadata.Height.HasValue ? $"{metadata.Width}x{metadata.Height}" : string.Empty,
            metadata.Duration.HasValue ? FormatDuration(metadata.Duration.Value) : string.Empty,
            metadata.VideoCodec ?? string.Empty,
            metadata.AudioCodec ?? string.Empty,
            metadata.Title ?? string.Empty,
            metadata.Artist ?? string.Empty,
            metadata.Album ?? string.Empty,
            metadata.Track ?? string.Empty,
            metadata.DeviceMake ?? string.Empty,
            metadata.DeviceModel ?? string.Empty,
            metadata.CapturedAtUtc?.ToString("yyyy MM MMMM", CultureInfo.InvariantCulture) ?? metadata.CaptureTimestampText ?? string.Empty,
        };
        values.AddRange(metadata.TextFields.OrderBy(item => item.Key, StringComparer.Ordinal).SelectMany(item => new[] { item.Key, item.Value }));
        return Bound(ContentText.Normalize(string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value)))), 8_192);
    }

    /// <summary>Creates provenance-aware embedded metadata fields for the existing content model.</summary>
    public static IReadOnlyList<ExtractedMetadataField> CreateMetadataFields(IndexedMediaEvidence? evidence)
    {
        if (evidence is null)
        {
            return [];
        }

        var metadata = evidence.Metadata;
        var fields = new List<ExtractedMetadataField>();
        Add(fields, "Media type", evidence.Kind.ToString());
        Add(fields, "Container", metadata.Container);
        Add(fields, "Dimensions", metadata.Width.HasValue && metadata.Height.HasValue ? $"{metadata.Width} x {metadata.Height}" : null);
        Add(fields, "Duration", metadata.Duration.HasValue ? FormatDuration(metadata.Duration.Value) : null);
        Add(fields, "Video codec", metadata.VideoCodec);
        Add(fields, "Audio codec", metadata.AudioCodec);
        Add(fields, "Title", metadata.Title);
        Add(fields, "Artist", metadata.Artist);
        Add(fields, "Album", metadata.Album);
        Add(fields, "Track", metadata.Track);
        Add(fields, "Device make", metadata.DeviceMake);
        Add(fields, "Device model", metadata.DeviceModel);
        Add(fields, "Capture date", metadata.CapturedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? metadata.CaptureTimestampText);
        Add(fields, "Orientation", metadata.Orientation?.ToString(CultureInfo.InvariantCulture));
        Add(fields, "GPS latitude", metadata.Latitude?.ToString("0.######", CultureInfo.InvariantCulture));
        Add(fields, "GPS longitude", metadata.Longitude?.ToString("0.######", CultureInfo.InvariantCulture));
        foreach (var item in metadata.TextFields.OrderBy(item => item.Key, StringComparer.Ordinal).Take(16))
        {
            Add(fields, item.Key, item.Value);
        }

        return fields.AsReadOnly();
    }

    private static void Add(ICollection<ExtractedMetadataField> fields, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields.Add(new ExtractedMetadataField(name, Bound(value, 512), ContentProvenance.EmbeddedMetadata));
        }
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    private static string Bound(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters ? value : value[..maximumCharacters];
}
