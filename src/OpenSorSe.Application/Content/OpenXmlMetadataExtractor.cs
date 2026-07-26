using System.IO.Compression;
using System.Xml;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Content;

/// <summary>Reads bounded DOCX and XLSX ZIP/XML parts without loading macros or external relationships.</summary>
public sealed class OpenXmlMetadataExtractor : IMetadataExtractor
{
    private const int MaximumXmlCharacters = 1_000_000;

    /// <inheritdoc />
    public bool Supports(string normalizedExtension) => normalizedExtension is ".docx" or ".xlsx";

    /// <inheritdoc />
    public Task<MetadataExtractionResult> ExtractAsync(
        FileEntry file,
        long maximumInputBytes,
        int maximumPages,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new FileInfo(file.FullPath);
        if (!info.Exists)
        {
            return Task.FromResult(Empty("Open XML content was unavailable."));
        }

        if (info.Length > maximumInputBytes)
        {
            return Task.FromResult(Empty("Open XML metadata was skipped because the file exceeds the configured content bound."));
        }

        try
        {
            using var stream = new FileStream(
                file.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var fields = ReadCoreProperties(archive, cancellationToken);
            var extension = Path.GetExtension(file.FullPath).ToLowerInvariant();
            var text = extension == ".docx"
                ? ReadTextPart(archive, "word/document.xml", "t", cancellationToken)
                : ReadTextPart(archive, "xl/sharedStrings.xml", "t", cancellationToken);
            int? pageCount = null;
            if (extension == ".xlsx")
            {
                var sheets = ReadAttributeValues(
                    archive,
                    "xl/workbook.xml",
                    "sheet",
                    "name",
                    cancellationToken);
                fields.Add(new ExtractedMetadataField(
                    "Sheet count",
                    sheets.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ContentProvenance.EmbeddedMetadata));
                foreach (var sheet in sheets.Take(32))
                {
                    fields.Add(new ExtractedMetadataField(
                        "Sheet name",
                        sheet,
                        ContentProvenance.EmbeddedMetadata));
                }
            }

            var normalized = ContentText.Normalize(text);
            return Task.FromResult(new MetadataExtractionResult(
                Array.AsReadOnly(fields.ToArray()),
                normalized.Length == 0 ? null : normalized,
                normalized.Length >= ContentText.ReliableTextMinimumLength,
                pageCount,
                [])
            {
                RawNativeText = text.Length == 0 ? null : text,
                ExtractionStrategies = [$"Open XML {extension.TrimStart('.').ToUpperInvariant()} native text"],
            });
        }
        catch (InvalidDataException)
        {
            return Task.FromResult(Empty("Open XML metadata was malformed and was skipped."));
        }
        catch (XmlException)
        {
            return Task.FromResult(Empty("Open XML metadata was malformed and was skipped."));
        }
    }

    private static List<ExtractedMetadataField> ReadCoreProperties(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        var fields = new List<ExtractedMetadataField>();
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["title"] = "Document title",
            ["creator"] = "Author",
            ["subject"] = "Subject",
            ["keywords"] = "Keywords",
            ["lastModifiedBy"] = "Last modified by",
            ["created"] = "Document created",
            ["modified"] = "Document modified",
            ["revision"] = "Revision",
        };
        var entry = archive.GetEntry("docProps/core.xml");
        if (entry is null || entry.Length > MaximumXmlCharacters * 4L)
        {
            return fields;
        }

        using var reader = CreateReader(entry);
        while (!reader.EOF)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element &&
                mappings.TryGetValue(reader.LocalName, out var displayName))
            {
                var value = reader.ReadElementContentAsString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    fields.Add(new ExtractedMetadataField(
                        displayName,
                        value,
                        ContentProvenance.EmbeddedMetadata));
                }

                continue;
            }

            reader.Read();
        }

        return fields;
    }

    private static string ReadTextPart(
        ZipArchive archive,
        string partName,
        string elementName,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(partName);
        if (entry is null || entry.Length > MaximumXmlCharacters * 4L)
        {
            return string.Empty;
        }

        var text = new System.Text.StringBuilder();
        using var reader = CreateReader(entry);
        while (!reader.EOF)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element &&
                reader.LocalName == elementName)
            {
                var value = reader.ReadElementContentAsString();
                if (text.Length + value.Length > ContentText.MaximumTextCharacters)
                {
                    break;
                }

                text.Append(value).Append(' ');
                continue;
            }

            reader.Read();
        }

        return text.ToString();
    }

    private static IReadOnlyList<string> ReadAttributeValues(
        ZipArchive archive,
        string partName,
        string elementName,
        string attributeName,
        CancellationToken cancellationToken)
    {
        var values = new List<string>();
        var entry = archive.GetEntry(partName);
        if (entry is null || entry.Length > MaximumXmlCharacters * 4L)
        {
            return values;
        }

        using var reader = CreateReader(entry);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element &&
                reader.LocalName == elementName &&
                reader.GetAttribute(attributeName) is { } value &&
                !string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static XmlReader CreateReader(ZipArchiveEntry entry)
    {
        var settings = new XmlReaderSettings
        {
            Async = false,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumXmlCharacters,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };
        return XmlReader.Create(entry.Open(), settings);
    }

    private static MetadataExtractionResult Empty(string warning) =>
        new([], null, false, null, [warning]);
}
