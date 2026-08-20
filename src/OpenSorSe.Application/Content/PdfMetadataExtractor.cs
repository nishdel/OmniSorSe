using System.Text;
using System.Text.RegularExpressions;
using OpenSorSe.Scanner.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace OpenSorSe.Application.Content;

/// <summary>Reads bounded PDF metadata and page-level native text without executing embedded content.</summary>
public sealed class PdfMetadataExtractor : IMetadataExtractor
{
    /// <summary>Hard safety ceiling applied before any managed or native PDF parser is invoked.</summary>
    public const long HardMaximumInputBytes = 64L * 1024 * 1024;

    /// <summary>Small compatibility prefix inspected when the primary parser rejects a damaged PDF.</summary>
    public const int MaximumFallbackPrefixBytes = 4 * 1024 * 1024;

    private const int MaximumPageTextInputCharacters = ContentText.MaximumTextCharacters * 2;
    private static readonly Regex PageRegex = new(@"/Type\s*/Page\b", RegexOptions.CultureInvariant);
    private static readonly Regex TextRegex = new(@"\((?<value>(?:\\.|[^\\)])*)\)\s*T[Jj]", RegexOptions.CultureInvariant);

    /// <inheritdoc />
    public bool Supports(string normalizedExtension) => normalizedExtension == ".pdf";

    /// <inheritdoc />
    public async Task<MetadataExtractionResult> ExtractAsync(
        FileEntry file,
        long maximumInputBytes,
        int maximumPages,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(file.FullPath);
        if (!info.Exists)
        {
            return Empty("PDF content was unavailable.");
        }

        var effectiveMaximumInputBytes = Math.Min(maximumInputBytes, HardMaximumInputBytes);
        if (info.Length > effectiveMaximumInputBytes)
        {
            return Empty("PDF metadata was skipped because the file exceeds the bounded PDF safety limit.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await Task.Run(
                () => ExtractWithPdfPig(
                    file.FullPath,
                    Math.Min(maximumPages, ContentText.MaximumPageRecords),
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Retain the deliberately small legacy reader as a compatibility fallback for
            // damaged files. Its output is never treated as executable content.
            return await ExtractFallbackAsync(
                file.FullPath,
                info.Length,
                Math.Min(maximumPages, ContentText.MaximumPageRecords),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static MetadataExtractionResult ExtractWithPdfPig(
        string fullPath,
        int maximumPages,
        CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(fullPath);
        var pageCount = document.NumberOfPages;
        var fields = new List<ExtractedMetadataField>();
        AddField(fields, "Document title", document.Information.Title);
        AddField(fields, "Author", document.Information.Author);
        AddField(fields, "Subject", document.Information.Subject);
        AddField(fields, "Keywords", document.Information.Keywords);
        AddField(fields, "Application name", document.Information.Creator);
        fields.Add(new ExtractedMetadataField(
            "Page count",
            pageCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ContentProvenance.EmbeddedMetadata));

        var pages = new List<PdfPageText>(Math.Min(pageCount, maximumPages));
        var combined = new StringBuilder();
        var rawCombined = new StringBuilder();
        var processedPages = Math.Min(pageCount, maximumPages);
        for (var pageNumber = 1; pageNumber <= processedPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = ContentOrderTextExtractor.GetText(document.GetPage(pageNumber));
            var boundedRaw = BoundInput(raw, MaximumPageTextInputCharacters);
            var normalized = ContentText.Normalize(boundedRaw);
            var reliable = PdfNativeTextQuality.IsReliable(normalized);
            pages.Add(new PdfPageText(
                pageNumber,
                normalized.Length == 0 ? null : normalized,
                reliable)
            {
                RawNativeText = string.IsNullOrEmpty(boundedRaw) ? null : BoundRaw(boundedRaw),
            });
            if (normalized.Length > 0)
            {
                AppendPageBounded(combined, pageNumber, normalized);
            }
            if (!string.IsNullOrEmpty(boundedRaw))
            {
                AppendPageBounded(rawCombined, pageNumber, boundedRaw);
            }
        }

        var warnings = pageCount > maximumPages
            ? new[] { "PDF page count exceeds the configured extraction bound; only the bounded prefix was inspected." }
            : [];
        var nativeText = ContentText.Normalize(combined.ToString());
        return new MetadataExtractionResult(
            Array.AsReadOnly(fields.ToArray()),
            nativeText.Length == 0 ? null : nativeText,
            pages.Count > 0 && pages.All(page => page.HasReliableNativeText),
            pageCount,
            Array.AsReadOnly(warnings))
        {
            PdfPages = Array.AsReadOnly(pages.ToArray()),
            RawNativeText = rawCombined.Length == 0 ? null : BoundRaw(rawCombined.ToString()),
            ExtractionStrategies = ["PdfPig page-aware native text"],
        };
    }

    private static async Task<MetadataExtractionResult> ExtractFallbackAsync(
        string fullPath,
        long length,
        int maximumPages,
        CancellationToken cancellationToken)
    {
        if (length > MaximumFallbackPrefixBytes)
        {
            return Empty("The primary PDF parser rejected this file; compatibility extraction was skipped because its bounded prefix would be insufficient.");
        }

        var bytes = new byte[(int)length];
        await using (var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var source = Encoding.Latin1.GetString(bytes);
        var pageCount = PageRegex.Matches(source).Count;
        var fields = new List<ExtractedMetadataField>();
        AddDictionaryValue(fields, source, "Title", "Document title");
        AddDictionaryValue(fields, source, "Author", "Author");
        AddDictionaryValue(fields, source, "Subject", "Subject");
        AddDictionaryValue(fields, source, "Keywords", "Keywords");
        AddDictionaryValue(fields, source, "Creator", "Application name");
        fields.Add(new ExtractedMetadataField(
            "Page count",
            pageCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ContentProvenance.EmbeddedMetadata));
        var rawNativeText = pageCount > maximumPages
            ? string.Empty
            : JoinFallbackText(source, cancellationToken);
        var nativeText = ContentText.Normalize(rawNativeText);
        var pages = pageCount == 1
            ? new[]
            {
                new PdfPageText(1, nativeText.Length == 0 ? null : nativeText, PdfNativeTextQuality.IsReliable(nativeText))
                {
                    RawNativeText = rawNativeText.Length == 0 ? null : BoundRaw(rawNativeText),
                },
            }
            : [];
        var warnings = new List<string> { "PDF required the compatibility text reader; page-level extraction may be incomplete." };
        if (pageCount > maximumPages)
        {
            warnings.Add("PDF page count exceeds the configured extraction bound.");
        }

        return new MetadataExtractionResult(
            Array.AsReadOnly(fields.ToArray()),
            nativeText.Length == 0 ? null : nativeText,
            pages.Length > 0 && pages.All(page => page.HasReliableNativeText),
            pageCount,
            Array.AsReadOnly(warnings.ToArray()))
        {
            PdfPages = Array.AsReadOnly(pages),
            RawNativeText = rawNativeText.Length == 0 ? null : BoundRaw(rawNativeText),
            ExtractionStrategies = ["Bounded compatibility PDF text reader"],
        };
    }

    private static void AddField(
        ICollection<ExtractedMetadataField> fields,
        string displayName,
        string? value)
    {
        var normalized = ContentText.NormalizeField(value, 2048);
        if (normalized.Length > 0)
        {
            fields.Add(new ExtractedMetadataField(
                displayName,
                normalized,
                ContentProvenance.EmbeddedMetadata));
        }
    }

    private static void AddDictionaryValue(
        ICollection<ExtractedMetadataField> fields,
        string source,
        string key,
        string displayName)
    {
        var match = Regex.Match(
            source,
            $@"/{Regex.Escape(key)}\s*\((?<value>(?:\\.|[^\\)])*)\)",
            RegexOptions.CultureInvariant);
        if (match.Success)
        {
            AddField(fields, displayName, UnescapePdfText(match.Groups["value"].Value));
        }
    }

    private static void AppendPageBounded(StringBuilder output, int pageNumber, string text)
    {
        if (output.Length >= ContentText.MaximumTextCharacters)
        {
            return;
        }

        if (output.Length > 0)
        {
            output.AppendLine();
        }

        var prefix = new StringBuilder(24)
            .Append("[Page ")
            .Append(pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append("] ")
            .ToString();
        var remaining = ContentText.MaximumTextCharacters - output.Length;
        if (prefix.Length >= remaining)
        {
            output.Append(prefix.AsSpan(0, remaining));
            return;
        }

        output.Append(prefix);
        remaining = ContentText.MaximumTextCharacters - output.Length;
        output.Append(text.AsSpan(0, Math.Min(text.Length, remaining)));
    }

    private static string JoinFallbackText(string source, CancellationToken cancellationToken)
    {
        var output = new StringBuilder(Math.Min(source.Length, ContentText.MaximumTextCharacters));
        var count = 0;
        foreach (Match match in TextRegex.Matches(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count++ >= 2048 || output.Length >= ContentText.MaximumTextCharacters)
            {
                break;
            }

            var value = UnescapePdfText(match.Groups["value"].Value);
            if (output.Length > 0)
            {
                output.Append(' ');
            }

            var remaining = ContentText.MaximumTextCharacters - output.Length;
            output.Append(value.AsSpan(0, Math.Min(value.Length, remaining)));
        }

        return output.ToString();
    }

    private static string UnescapePdfText(string value) => value
        .Replace("\\(", "(", StringComparison.Ordinal)
        .Replace("\\)", ")", StringComparison.Ordinal)
        .Replace("\\n", " ", StringComparison.Ordinal)
        .Replace("\\r", " ", StringComparison.Ordinal)
        .Replace("\\\\", "\\", StringComparison.Ordinal);

    private static MetadataExtractionResult Empty(string warning) =>
        new([], null, false, null, [warning]);

    private static string BoundRaw(string value) =>
        value.Length <= ContentText.MaximumTextCharacters
            ? value
            : value[..ContentText.MaximumTextCharacters];

    private static string BoundInput(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters ? value : value[..maximumCharacters];
}

/// <summary>Applies the documented deterministic PDF-native-text sufficiency policy.</summary>
public static class PdfNativeTextQuality
{
    /// <summary>Returns whether normalized page text is meaningful enough to avoid OCR.</summary>
    public static bool IsReliable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 32)
        {
            return false;
        }

        var meaningful = 0;
        var noisy = 0;
        var longestRun = 1;
        var currentRun = 1;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsLetterOrDigit(character))
            {
                meaningful++;
            }

            if (character == '\uFFFD' || char.IsControl(character))
            {
                noisy++;
            }

            if (index > 0 && character == text[index - 1] && !char.IsWhiteSpace(character))
            {
                currentRun++;
                longestRun = Math.Max(longestRun, currentRun);
            }
            else
            {
                currentRun = 1;
            }
        }

        return meaningful >= 12 &&
               noisy <= Math.Max(1, text.Length / 10) &&
               longestRun < Math.Max(12, text.Length / 3);
    }
}
