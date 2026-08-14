using System.IO.Compression;
using System.Xml;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Content;

/// <summary>Reads bounded DOCX, XLSX, and PPTX ZIP/XML parts without loading macros or external relationships.</summary>
public sealed class OpenXmlMetadataExtractor : IMetadataExtractor
{
    private const int MaximumXmlCharacters = 1_000_000;
    private const int MaximumArchiveEntries = 2048;
    private const long MaximumSelectedUncompressedBytes = 32L * 1024 * 1024;

    /// <inheritdoc />
    public bool Supports(string normalizedExtension) => normalizedExtension is ".docx" or ".xlsx" or ".pptx";

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
            if (archive.Entries.Count > MaximumArchiveEntries)
            {
                return Task.FromResult(Empty("Open XML metadata was skipped because the package contains too many entries."));
            }

            var fields = ReadCoreProperties(archive, cancellationToken);
            var extension = Path.GetExtension(file.FullPath).ToLowerInvariant();
            var text = extension switch
            {
                ".docx" => ReadTextPart(archive, "word/document.xml", "t", cancellationToken),
                ".xlsx" => ReadWorkbookText(archive, cancellationToken),
                ".pptx" => ReadPresentationText(archive, maximumPages, cancellationToken),
                _ => string.Empty,
            };
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
            else if (extension == ".pptx")
            {
                pageCount = SelectOrderedParts(archive, "ppt/slides/slide", ".xml").Count;
                fields.Add(new ExtractedMetadataField(
                    "Slide count",
                    pageCount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ContentProvenance.EmbeddedMetadata));
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

    private static string ReadWorkbookText(ZipArchive archive, CancellationToken cancellationToken)
    {
        var sharedStrings = ReadElementValues(
            archive.GetEntry("xl/sharedStrings.xml"),
            "t",
            cancellationToken);
        var text = new System.Text.StringBuilder();
        var worksheets = SelectOrderedParts(archive, "xl/worksheets/sheet", ".xml");
        foreach (var worksheet in worksheets.Take(256))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var reader = CreateReader(worksheet);
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "c")
                {
                    continue;
                }

                var cellType = reader.GetAttribute("t");
                using var cellReader = reader.ReadSubtree();
                string? rawValue = null;
                var inlineValues = new List<string>();
                var formulas = new List<string>();
                cellReader.Read();
                while (!cellReader.EOF)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (cellReader.NodeType != XmlNodeType.Element)
                    {
                        cellReader.Read();
                        continue;
                    }

                    if (cellReader.LocalName == "v")
                    {
                        rawValue = cellReader.ReadElementContentAsString();
                        continue;
                    }
                    else if (cellReader.LocalName == "t")
                    {
                        inlineValues.Add(cellReader.ReadElementContentAsString());
                        continue;
                    }
                    else if (cellReader.LocalName == "f")
                    {
                        formulas.Add(cellReader.ReadElementContentAsString());
                        continue;
                    }

                    cellReader.Read();
                }

                foreach (var formula in formulas)
                {
                    AppendText(text, formula);
                }

                foreach (var inline in inlineValues)
                {
                    AppendText(text, inline);
                }

                if (cellType == "s" && int.TryParse(rawValue, out var sharedIndex) &&
                    sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
                {
                    AppendText(text, sharedStrings[sharedIndex]);
                }
                else if (!string.IsNullOrWhiteSpace(rawValue))
                {
                    AppendText(text, rawValue);
                }

                if (text.Length >= ContentText.MaximumTextCharacters)
                {
                    return text.ToString();
                }
            }
        }

        if (worksheets.Count == 0)
        {
            foreach (var sharedString in sharedStrings)
            {
                AppendText(text, sharedString);
            }
        }

        return text.ToString();
    }

    private static string ReadPresentationText(
        ZipArchive archive,
        int maximumPages,
        CancellationToken cancellationToken)
    {
        var text = new System.Text.StringBuilder();
        var slideLimit = Math.Clamp(maximumPages, 1, 512);
        foreach (var entry in SelectOrderedParts(archive, "ppt/slides/slide", ".xml").Take(slideLimit))
        {
            AppendPartText(text, entry, "t", cancellationToken);
        }

        foreach (var entry in SelectOrderedParts(archive, "ppt/notesSlides/notesSlide", ".xml").Take(slideLimit))
        {
            AppendPartText(text, entry, "t", cancellationToken);
        }

        return text.ToString();
    }

    private static void AppendPartText(
        System.Text.StringBuilder text,
        ZipArchiveEntry entry,
        string elementName,
        CancellationToken cancellationToken)
    {
        if (entry.Length > MaximumXmlCharacters * 4L)
        {
            return;
        }

        using var reader = CreateReader(entry);
        while (!reader.EOF)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == elementName)
            {
                AppendText(text, reader.ReadElementContentAsString());
                if (text.Length >= ContentText.MaximumTextCharacters)
                {
                    return;
                }

                continue;
            }

            reader.Read();
        }
    }

    private static IReadOnlyList<string> ReadElementValues(
        ZipArchiveEntry? entry,
        string elementName,
        CancellationToken cancellationToken)
    {
        var values = new List<string>();
        if (entry is null || entry.Length > MaximumXmlCharacters * 4L)
        {
            return values;
        }

        using var reader = CreateReader(entry);
        while (!reader.EOF)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == elementName)
            {
                var value = reader.ReadElementContentAsString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }

                continue;
            }

            reader.Read();
        }

        return values;
    }

    private static IReadOnlyList<ZipArchiveEntry> SelectOrderedParts(
        ZipArchive archive,
        string prefix,
        string suffix)
    {
        var selected = archive.Entries
            .Where(entry =>
                entry.FullName.StartsWith(prefix, StringComparison.Ordinal) &&
                entry.FullName.EndsWith(suffix, StringComparison.Ordinal) &&
                !entry.FullName.Contains("..", StringComparison.Ordinal) &&
                entry.Length <= MaximumXmlCharacters * 4L)
            .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
            .Take(512)
            .ToArray();
        return selected.Sum(entry => entry.Length) <= MaximumSelectedUncompressedBytes
            ? selected
            : [];
    }

    private static void AppendText(System.Text.StringBuilder text, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || text.Length >= ContentText.MaximumTextCharacters)
        {
            return;
        }

        if (text.Length > 0)
        {
            text.Append(' ');
        }

        var remaining = ContentText.MaximumTextCharacters - text.Length;
        text.Append(value.AsSpan(0, Math.Min(remaining, value.Length)));
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
