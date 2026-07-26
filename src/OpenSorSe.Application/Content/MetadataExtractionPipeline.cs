using OpenSorSe.Scanner.Models;
using OpenSorSe.Core.Diagnostics;

namespace OpenSorSe.Application.Content;

/// <summary>Runs all applicable metadata extractors with deterministic merge and failure isolation.</summary>
public sealed class MetadataExtractionPipeline : IMetadataExtractionPipeline
{
    private const int MaximumFields = 64;
    private readonly IReadOnlyList<IMetadataExtractor> _extractors;

    /// <summary>Initializes the pipeline with filesystem and format-specific extractors.</summary>
    public MetadataExtractionPipeline(IEnumerable<IMetadataExtractor> extractors)
    {
        ArgumentNullException.ThrowIfNull(extractors);
        _extractors = extractors.ToArray();
        if (_extractors.Count == 0 || _extractors.Any(extractor => extractor is null))
        {
            throw new ArgumentException("At least one non-null metadata extractor is required.", nameof(extractors));
        }
    }

    /// <inheritdoc />
    public async Task<MetadataExtractionResult> ExtractAsync(
        FileEntry file,
        long maximumInputBytes,
        int maximumPages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (string.IsNullOrWhiteSpace(file.FullPath) || maximumInputBytes < 1 || maximumPages < 1)
        {
            throw new ArgumentException("A known file and positive extraction bounds are required.", nameof(file));
        }

        var extension = Path.GetExtension(file.FullPath).ToLowerInvariant();
        var fields = new List<ExtractedMetadataField>();
        var warnings = new List<string>();
        var nativeTextParts = new List<string>();
        var rawNativeTextParts = new List<string>();
        var strategies = new List<string>();
        var pdfPages = new List<PdfPageText>();
        var wasTruncated = false;
        int? pageCount = null;
        foreach (var extractor in _extractors.Where(candidate => candidate.Supports(extension)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                strategies.Add(extractor.GetType().Name);
                var result = await extractor.ExtractAsync(
                    file,
                    maximumInputBytes,
                    maximumPages,
                    cancellationToken).ConfigureAwait(false);
                fields.AddRange(result.Fields);
                warnings.AddRange(result.Warnings);
                wasTruncated |= result.WasTruncated;
                if (!string.IsNullOrWhiteSpace(result.NativeText))
                {
                    nativeTextParts.Add(result.NativeText);
                }
                if (!string.IsNullOrWhiteSpace(result.RawNativeText))
                {
                    rawNativeTextParts.Add(result.RawNativeText);
                }

                pdfPages.AddRange(result.PdfPages);
                pageCount ??= result.PageCount;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                warnings.Add("One metadata reader could not read this file; remaining readers continued.");
            }
        }

        var normalizedText = ContentText.Normalize(string.Join(' ', nativeTextParts));
        var combinedRawNativeText = string.Join(Environment.NewLine, rawNativeTextParts);
        wasTruncated |= nativeTextParts.Sum(part => (long)part.Length) > ContentText.MaximumTextCharacters;
        wasTruncated |= combinedRawNativeText.Length > ContentText.MaximumTextCharacters;
        var maximumRetainedPages = Math.Min(maximumPages, ContentText.MaximumPageRecords);
        wasTruncated |= fields.Count > MaximumFields ||
                        pdfPages.Count > maximumRetainedPages ||
                        warnings.Count > 16;
        var rawNativeText = BoundRaw(combinedRawNativeText);
        var normalizedFields = fields
            .Where(field => field is not null)
            .Select(field => field with
            {
                Name = ContentText.NormalizeField(field.Name, 64),
                Value = ContentText.NormalizeField(field.Value, 2048),
                Confidence = Math.Clamp(field.Confidence, 0, 1),
            })
            .Where(field => field.Name.Length > 0 && field.Value.Length > 0)
            .DistinctBy(field => (field.Name, field.Value, field.Provenance))
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .ThenBy(field => field.Value, StringComparer.Ordinal)
            .Take(MaximumFields)
            .ToArray();
        var normalizedPdfPages = pdfPages
            .Where(page => page.PageNumber > 0)
            .DistinctBy(page => page.PageNumber)
            .OrderBy(page => page.PageNumber)
            .Take(maximumRetainedPages)
            .ToArray();
        if (wasTruncated)
        {
            warnings.Insert(0, "Native extraction retention reached a configured text, page, field, or warning bound.");
        }
        var hasReliableNativeText = normalizedPdfPages.Length > 0
            ? pageCount.GetValueOrDefault(normalizedPdfPages.Length) <= normalizedPdfPages.Length
                && normalizedPdfPages.All(page => page.HasReliableNativeText)
            : normalizedText.Length >= ContentText.ReliableTextMinimumLength;
        return new MetadataExtractionResult(
            Array.AsReadOnly(normalizedFields),
            normalizedText.Length == 0 ? null : normalizedText,
            hasReliableNativeText,
            pageCount,
            Array.AsReadOnly(warnings.Distinct(StringComparer.Ordinal).Take(16).ToArray()))
        {
            PdfPages = Array.AsReadOnly(normalizedPdfPages),
            RawNativeText = rawNativeText.Length == 0 ? null : rawNativeText,
            ExtractionStrategies = Array.AsReadOnly(strategies.Distinct(StringComparer.Ordinal).ToArray()),
            WasTruncated = wasTruncated,
        };
    }

    private static string BoundRaw(string value) =>
        value.Length <= ContentText.MaximumTextCharacters
            ? value
            : value[..ContentText.MaximumTextCharacters];
}

/// <summary>Provides deterministic bounded normalization for extracted local text.</summary>
public static class ContentText
{
    /// <summary>Gets the minimum normalized length considered reliable native text.</summary>
    public const int ReliableTextMinimumLength = 20;

    /// <summary>Gets the maximum text retained per native or OCR source.</summary>
    public const int MaximumTextCharacters = 65_536;

    /// <summary>Gets the maximum bounded page records retained by the extraction feature itself.</summary>
    public const int MaximumPageRecords = 500;

    /// <summary>Normalizes whitespace and removes unsupported controls within the content bound.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var output = new System.Text.StringBuilder(Math.Min(value.Length, MaximumTextCharacters));
        var previousWasSpace = false;
        foreach (var character in value.Normalize(System.Text.NormalizationForm.FormKC))
        {
            if (output.Length >= MaximumTextCharacters)
            {
                break;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                {
                    output.Append(' ');
                    previousWasSpace = true;
                }

                continue;
            }

            if (!char.IsControl(character))
            {
                output.Append(character);
                previousWasSpace = false;
            }
        }

        return output.ToString().Trim();
    }

    /// <summary>Normalizes a short metadata field within an explicit bound.</summary>
    public static string NormalizeField(string? value, int maximumLength)
    {
        var normalized = Normalize(value);
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}
