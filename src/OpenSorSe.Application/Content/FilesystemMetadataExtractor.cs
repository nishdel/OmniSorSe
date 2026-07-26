using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Content;

/// <summary>Projects ordinary read-only filesystem metadata with explicit provenance.</summary>
public sealed class FilesystemMetadataExtractor : IMetadataExtractor
{
    /// <inheritdoc />
    public bool Supports(string normalizedExtension) => true;

    /// <inheritdoc />
    public Task<MetadataExtractionResult> ExtractAsync(
        FileEntry file,
        long maximumInputBytes,
        int maximumPages,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = file.Metadata;
        var fields = new List<ExtractedMetadataField>
        {
            Field("File name", metadata?.FileName ?? Path.GetFileName(file.FullPath)),
            Field("Extension", NormalizeExtension(metadata?.Extension ?? Path.GetExtension(file.FullPath))),
            Field("MIME type", ResolveMimeType(metadata?.Extension ?? Path.GetExtension(file.FullPath))),
        };
        if (metadata?.SizeInBytes is { } size)
        {
            fields.Add(Field("File size bytes", size.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        AddDate(fields, "Created UTC", metadata?.CreationTimeUtc);
        AddDate(fields, "Modified UTC", metadata?.LastWriteTimeUtc);
        return Task.FromResult(new MetadataExtractionResult(
            Array.AsReadOnly(fields.Where(field => field.Value.Length > 0).ToArray()),
            null,
            false,
            null,
            []));
    }

    private static ExtractedMetadataField Field(string name, string value) =>
        new(name, value, ContentProvenance.Filesystem);

    private static void AddDate(List<ExtractedMetadataField> fields, string name, DateTimeOffset? value)
    {
        if (value is not null)
        {
            fields.Add(Field(name, value.Value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    private static string NormalizeExtension(string extension) =>
        string.IsNullOrWhiteSpace(extension) ? "(none)" : extension.Trim().ToLowerInvariant();

    private static string ResolveMimeType(string extension) => extension.Trim().ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".tif" or ".tiff" => "image/tiff",
        ".txt" or ".md" => "text/plain",
        _ => "application/octet-stream",
    };
}
