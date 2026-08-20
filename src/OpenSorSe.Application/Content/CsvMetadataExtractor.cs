using System.Text;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Content;

/// <summary>Streams a bounded, local projection of CSV/TSV cells for Search and derived intelligence.</summary>
public sealed class CsvMetadataExtractor : IMetadataExtractor
{
    private const int MaximumRows = 200;
    private const int MaximumColumnsPerRow = 256;

    /// <inheritdoc />
    public bool Supports(string normalizedExtension) => normalizedExtension is ".csv" or ".tsv";

    /// <inheritdoc />
    public async Task<MetadataExtractionResult> ExtractAsync(
        FileEntry file,
        long maximumInputBytes,
        int maximumPages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (maximumInputBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumInputBytes));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var info = new FileInfo(file.FullPath);
        info.Refresh();
        if (!info.Exists)
        {
            throw new FileNotFoundException("The delimited text file no longer exists.", file.FullPath);
        }

        if (info.Length > maximumInputBytes)
        {
            return Empty("Delimited text extraction was skipped because the file exceeds the configured input-size bound.");
        }

        try
        {
            await using var stream = new FileStream(
                file.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 16 * 1024,
                leaveOpen: false);

            var delimiter = Path.GetExtension(file.FullPath).Equals(".tsv", StringComparison.OrdinalIgnoreCase)
                ? '\t'
                : await DetectDelimiterAsync(reader, cancellationToken).ConfigureAwait(false);
            stream.Position = 0;
            reader.DiscardBufferedData();

            var output = new StringBuilder();
            var row = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;
            var pendingQuotedQuote = false;
            var previousWasCarriageReturn = false;
            var rowCount = 0;
            var truncated = false;
            var reachedEndOfStream = false;
            var buffer = new char[4096];
            while (rowCount < MaximumRows && output.Length < ContentText.MaximumTextCharacters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    reachedEndOfStream = true;
                    break;
                }

                for (var index = 0; index < read; index++)
                {
                    var value = buffer[index];
                    if (previousWasCarriageReturn)
                    {
                        previousWasCarriageReturn = false;
                        if (value == '\n')
                        {
                            continue;
                        }
                    }

                    if (pendingQuotedQuote)
                    {
                        pendingQuotedQuote = false;
                        if (value == '"')
                        {
                            AppendBounded(cell, '"');
                            continue;
                        }

                        quoted = false;
                    }

                    if (value == '"')
                    {
                        if (quoted && index + 1 < read && buffer[index + 1] == '"')
                        {
                            AppendBounded(cell, '"');
                            index++;
                        }
                        else if (quoted && index + 1 == read)
                        {
                            pendingQuotedQuote = true;
                        }
                        else
                        {
                            quoted = !quoted;
                        }

                        continue;
                    }

                    if (!quoted && value == delimiter)
                    {
                        AddCell(row, cell);
                        continue;
                    }

                    if (!quoted && (value == '\r' || value == '\n'))
                    {
                        AddCell(row, cell);
                        AppendRow(output, row);
                        row.Clear();
                        rowCount++;
                        previousWasCarriageReturn = value == '\r';
                        if (rowCount >= MaximumRows || output.Length >= ContentText.MaximumTextCharacters)
                        {
                            truncated = true;
                            break;
                        }

                        continue;
                    }

                    AppendBounded(cell, value);
                }

                if (truncated)
                {
                    break;
                }
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                AddCell(row, cell);
                AppendRow(output, row);
            }

            if (pendingQuotedQuote)
            {
                quoted = false;
            }

            var malformed = quoted;
            truncated |= !reachedEndOfStream;
            var raw = output.ToString();
            var normalized = ContentText.Normalize(raw);
            var warnings = new List<string>();
            if (truncated)
            {
                warnings.Add("Delimited text extraction reached its bounded row or character limit.");
            }

            if (malformed)
            {
                warnings.Add("Delimited text contained an unterminated quoted field; bounded evidence was retained.");
            }

            return new MetadataExtractionResult(
                [],
                normalized.Length == 0 ? null : normalized,
                normalized.Length >= ContentText.ReliableTextMinimumLength,
                null,
                warnings)
            {
                RawNativeText = raw.Length == 0 ? null : raw,
                WasTruncated = truncated,
                ExtractionStrategies = ["Bounded native delimited-text cells"],
            };
        }
        catch (DecoderFallbackException)
        {
            return Empty("Delimited text encoding was unsupported or malformed and was skipped.");
        }
    }

    private static async Task<char> DetectDelimiterAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var sample = new char[4096];
        var read = await reader.ReadAsync(sample.AsMemory(), cancellationToken).ConfigureAwait(false);
        var counts = new Dictionary<char, int> { [','] = 0, [';'] = 0, ['\t'] = 0 };
        var quoted = false;
        for (var index = 0; index < read; index++)
        {
            var value = sample[index];
            if (value == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && counts.ContainsKey(value))
            {
                counts[value]++;
            }
            else if (!quoted && (value == '\r' || value == '\n'))
            {
                break;
            }
        }

        return counts.OrderByDescending(item => item.Value).ThenBy(item => item.Key).First().Value == 0
            ? ','
            : counts.OrderByDescending(item => item.Value).ThenBy(item => item.Key).First().Key;
    }

    private static void AddCell(List<string> row, StringBuilder cell)
    {
        if (row.Count < MaximumColumnsPerRow)
        {
            var normalized = ContentText.Normalize(cell.ToString());
            if (normalized.Length > 0)
            {
                row.Add(normalized);
            }
        }

        cell.Clear();
    }

    private static void AppendRow(StringBuilder output, IReadOnlyList<string> row)
    {
        foreach (var value in row)
        {
            if (output.Length >= ContentText.MaximumTextCharacters)
            {
                return;
            }

            if (output.Length > 0)
            {
                output.Append(' ');
            }

            var remaining = ContentText.MaximumTextCharacters - output.Length;
            output.Append(value.AsSpan(0, Math.Min(remaining, value.Length)));
        }
    }

    private static void AppendBounded(StringBuilder builder, char value)
    {
        if (builder.Length < ContentText.MaximumTextCharacters)
        {
            builder.Append(value);
        }
    }

    private static MetadataExtractionResult Empty(string warning) =>
        new([], null, false, null, [warning]);
}
