using System.Buffers.Binary;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Content;

/// <summary>Reads bounded PNG and JPEG dimensions directly from headers.</summary>
public sealed class ImageMetadataExtractor : IMetadataExtractor
{
    private const int MaximumHeaderBytes = 1_048_576;

    /// <inheritdoc />
    public bool Supports(string normalizedExtension) =>
        normalizedExtension is ".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff";

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
            return Empty("Image metadata was unavailable.");
        }

        if (info.Length > maximumInputBytes)
        {
            return Empty("Image metadata was skipped because the file exceeds the configured content bound.");
        }

        var bytesToRead = (int)Math.Min(info.Length, MaximumHeaderBytes);
        var buffer = new byte[bytesToRead];
        await using (var stream = new FileStream(
            file.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        var extension = Path.GetExtension(file.FullPath).ToLowerInvariant();
        var dimensions = extension == ".png"
            ? ReadPngDimensions(buffer)
            : extension is ".jpg" or ".jpeg"
                ? ReadJpegDimensions(buffer)
                : null;
        var fields = new List<ExtractedMetadataField>();
        if (dimensions is { } value)
        {
            fields.Add(new ExtractedMetadataField(
                "Image width",
                value.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ContentProvenance.EmbeddedMetadata));
            fields.Add(new ExtractedMetadataField(
                "Image height",
                value.Height.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ContentProvenance.EmbeddedMetadata));
        }

        return new MetadataExtractionResult(
            Array.AsReadOnly(fields.ToArray()),
            null,
            false,
            1,
            dimensions is null ? ["Image dimensions were unavailable from the bounded header."] : []);
    }

    private static (int Width, int Height)? ReadPngDimensions(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        return bytes.Length >= 24 && bytes[..8].SequenceEqual(signature)
            ? (BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4)),
               BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4)))
            : null;
    }

    private static (int Width, int Height)? ReadJpegDimensions(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8)
        {
            return null;
        }

        var index = 2;
        while (index + 8 < bytes.Length)
        {
            if (bytes[index] != 0xff)
            {
                index++;
                continue;
            }

            var marker = bytes[index + 1];
            if (marker is 0xd8 or 0xd9)
            {
                index += 2;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index + 2, 2));
            if (length < 2 || index + 2 + length > bytes.Length)
            {
                return null;
            }

            if (marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or
                0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf)
            {
                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index + 5, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index + 7, 2));
                return (width, height);
            }

            index += 2 + length;
        }

        return null;
    }

    private static MetadataExtractionResult Empty(string warning) =>
        new([], null, false, null, [warning]);
}
