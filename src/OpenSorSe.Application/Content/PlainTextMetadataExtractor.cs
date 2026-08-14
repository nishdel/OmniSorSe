using System.Text;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Content;

/// <summary>Reads bounded ordinary text and Markdown without modifying source files or invoking a provider.</summary>
public sealed class PlainTextMetadataExtractor : IMetadataExtractor
{
    private static readonly HashSet<string> SupportedExtensions = new(
        [".txt", ".md", ".markdown", ".text"],
        StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool Supports(string normalizedExtension) => SupportedExtensions.Contains(normalizedExtension);

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
            throw new FileNotFoundException("The text file no longer exists.", file.FullPath);
        }

        if (info.Length > maximumInputBytes)
        {
            return new MetadataExtractionResult(
                [],
                null,
                false,
                null,
                ["Native text extraction was skipped because the file exceeds the configured input-size bound."]);
        }

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
        var builder = new StringBuilder(Math.Min(ContentText.MaximumTextCharacters, (int)Math.Min(info.Length, int.MaxValue)));
        var buffer = new char[4096];
        var truncated = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            var remaining = ContentText.MaximumTextCharacters - builder.Length;
            if (remaining <= 0)
            {
                truncated = true;
                break;
            }

            builder.Append(buffer, 0, Math.Min(remaining, read));
            if (read > remaining)
            {
                truncated = true;
                break;
            }
        }

        var raw = builder.ToString();
        var normalized = ContentText.Normalize(raw);
        return new MetadataExtractionResult(
            [],
            normalized.Length == 0 ? null : normalized,
            normalized.Length >= ContentText.ReliableTextMinimumLength,
            null,
            truncated ? ["Native text retention reached the configured character bound."] : [])
        {
            RawNativeText = raw.Length == 0 ? null : raw,
            WasTruncated = truncated,
        };
    }
}
