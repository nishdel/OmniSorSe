using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Media;

/// <summary>Reads bounded image headers and common EXIF fields without decoding pixel data.</summary>
public sealed class ImageMediaMetadataProvider : IMediaMetadataProvider
{
    private const int MaximumHeaderBytes = 1_048_576;

    /// <inheritdoc />
    public string Name => "bounded-image-header";

    /// <inheritdoc />
    public string Version => "2.2.0";

    /// <inheritdoc />
    public bool Supports(MediaKind kind, string normalizedExtension) =>
        kind == MediaKind.Image && MediaFormatRegistry.Images.Contains(normalizedExtension, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<MediaCapability> DetectCapabilityAsync(MediaKind kind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new MediaCapability(
            MediaCapabilityKind.ImageMetadata,
            kind is MediaKind.None or MediaKind.Image,
            Name,
            Version,
            "JPEG, PNG, WebP, BMP, and TIFF headers are read locally within a one-MiB bound."));
    }

    /// <inheritdoc />
    public async Task<MediaMetadataResult> ExtractAsync(
        FileEntry file,
        MediaIntelligenceSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(settings);
        var started = Stopwatch.StartNew();
        try
        {
            var info = new FileInfo(file.FullPath);
            info.Refresh();
            if (!info.Exists)
            {
                return Failure(MediaExtractionStatus.Failed, started.Elapsed, "The image disappeared before metadata extraction.");
            }

            var length = (int)Math.Min(info.Length, MaximumHeaderBytes);
            if (length < 8)
            {
                return Failure(MediaExtractionStatus.Failed, started.Elapsed, "The image header is incomplete.");
            }

            var bytes = new byte[length];
            await using (var stream = new FileStream(
                file.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            }

            var extension = Path.GetExtension(file.FullPath).ToLowerInvariant();
            var warnings = new List<string>();
            var parsed = extension switch
            {
                ".png" => ParsePng(bytes),
                ".jpg" or ".jpeg" => ParseJpeg(bytes, warnings),
                ".bmp" => ParseBmp(bytes),
                ".webp" => ParseWebP(bytes),
                ".tif" or ".tiff" => ParseTiff(bytes, warnings),
                _ => null,
            };
            if (parsed is null)
            {
                return Failure(MediaExtractionStatus.Failed, started.Elapsed, "The image header is malformed or unsupported.");
            }

            var metadata = new MediaMetadata
            {
                Kind = MediaKind.Image,
                Container = extension.TrimStart('.').Replace("jpg", "jpeg", StringComparison.Ordinal),
                Width = ValidDimension(parsed.Width),
                Height = ValidDimension(parsed.Height),
                DeviceMake = Bound(parsed.Exif.Make, 128),
                DeviceModel = Bound(parsed.Exif.Model, 128),
                Orientation = parsed.Exif.Orientation is >= 1 and <= 8 ? parsed.Exif.Orientation : null,
                CapturedAtUtc = parsed.Exif.CapturedAtUtc,
                CaptureTimestampText = Bound(parsed.Exif.CaptureTimestampText, 64),
                Latitude = ValidLatitude(parsed.Exif.Latitude),
                Longitude = ValidLongitude(parsed.Exif.Longitude),
            };
            if (metadata.Width is null || metadata.Height is null)
            {
                warnings.Add("Image dimensions were unavailable from the bounded header.");
            }

            return new MediaMetadataResult(
                warnings.Count == 0 ? MediaExtractionStatus.Completed : MediaExtractionStatus.PartiallyCompleted,
                metadata,
                Name,
                Version,
                started.Elapsed,
                warnings.Distinct(StringComparer.Ordinal).Take(16).ToArray(),
                warnings.Count == 0
                    ? "Image metadata was extracted locally."
                    : "Image metadata was extracted locally with bounded warnings.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return Failure(MediaExtractionStatus.Failed, started.Elapsed, "The image metadata could not be read safely.");
        }
    }

    private MediaMetadataResult Failure(MediaExtractionStatus status, TimeSpan duration, string message) =>
        new(status, null, Name, Version, duration, [], message);

    private static ParsedImage? ParsePng(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 24 || !bytes[..8].SequenceEqual(signature) ||
            !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        return new ParsedImage(
            BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4)),
            ExifData.Empty);
    }

    private static ParsedImage? ParseBmp(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 26 || bytes[0] != (byte)'B' || bytes[1] != (byte)'M')
        {
            return null;
        }

        var dibSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(14, 4));
        if (dibSize == 12)
        {
            return new ParsedImage(
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(18, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(20, 2)),
                ExifData.Empty);
        }

        return dibSize >= 40 && bytes.Length >= 26
            ? new ParsedImage(
                Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(18, 4))),
                Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(22, 4))),
                ExifData.Empty)
            : null;
    }

    private static ParsedImage? ParseWebP(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 30 || !bytes[..4].SequenceEqual("RIFF"u8) ||
            !bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return null;
        }

        var chunk = bytes.Slice(12, 4);
        if (chunk.SequenceEqual("VP8X"u8))
        {
            return new ParsedImage(ReadUInt24(bytes.Slice(24, 3)) + 1, ReadUInt24(bytes.Slice(27, 3)) + 1, ExifData.Empty);
        }

        if (chunk.SequenceEqual("VP8L"u8) && bytes[20] == 0x2f)
        {
            var width = 1 + bytes[21] + ((bytes[22] & 0x3f) << 8);
            var height = 1 + (bytes[22] >> 6) + (bytes[23] << 2) + ((bytes[24] & 0x0f) << 10);
            return new ParsedImage(width, height, ExifData.Empty);
        }

        if (chunk.SequenceEqual("VP8 "u8) &&
            bytes.Length >= 30 && bytes[23] == 0x9d && bytes[24] == 0x01 && bytes[25] == 0x2a)
        {
            return new ParsedImage(
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(26, 2)) & 0x3fff,
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(28, 2)) & 0x3fff,
                ExifData.Empty);
        }

        return null;
    }

    private static ParsedImage? ParseJpeg(byte[] bytes, ICollection<string> warnings)
    {
        if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8)
        {
            return null;
        }

        int? width = null;
        int? height = null;
        var exif = ExifData.Empty;
        var index = 2;
        while (index + 4 <= bytes.Length)
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

            if (marker == 0xda)
            {
                break;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(index + 2, 2));
            if (length < 2 || index + 2 + length > bytes.Length)
            {
                warnings.Add("The JPEG metadata segment was truncated.");
                break;
            }

            if (IsStartOfFrame(marker) && length >= 7)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(index + 5, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(index + 7, 2));
            }
            else if (marker == 0xe1 && length >= 10 &&
                     bytes.AsSpan(index + 4, 6).SequenceEqual("Exif\0\0"u8))
            {
                try
                {
                    exif = ExifParser.Parse(bytes, index + 10, length - 8);
                }
                catch (InvalidDataException)
                {
                    warnings.Add("Embedded EXIF metadata was malformed and was ignored.");
                }
            }

            index += 2 + length;
        }

        return width.HasValue || height.HasValue || exif != ExifData.Empty
            ? new ParsedImage(width ?? exif.Width, height ?? exif.Height, exif)
            : null;
    }

    private static ParsedImage? ParseTiff(byte[] bytes, ICollection<string> warnings)
    {
        try
        {
            var exif = ExifParser.Parse(bytes, 0, bytes.Length);
            return new ParsedImage(exif.Width, exif.Height, exif);
        }
        catch (InvalidDataException)
        {
            warnings.Add("Embedded TIFF/EXIF metadata was malformed and was ignored.");
            return null;
        }
    }

    private static bool IsStartOfFrame(byte marker) => marker is
        0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or
        0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf;

    private static int ReadUInt24(ReadOnlySpan<byte> value) => value[0] | value[1] << 8 | value[2] << 16;

    private static int? ValidDimension(int? value) => value is > 0 and <= 1_000_000 ? value : null;

    private static double? ValidLatitude(double? value) => value is >= -90 and <= 90 && double.IsFinite(value.Value) ? value : null;

    private static double? ValidLongitude(double? value) => value is >= -180 and <= 180 && double.IsFinite(value.Value) ? value : null;

    private static string? Bound(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= maximum ? value.Trim() : value.Trim()[..maximum];

    private sealed record ParsedImage(int? Width, int? Height, ExifData Exif);

    private sealed record ExifData(
        int? Width,
        int? Height,
        string? Make,
        string? Model,
        int? Orientation,
        DateTimeOffset? CapturedAtUtc,
        string? CaptureTimestampText,
        double? Latitude,
        double? Longitude)
    {
        public static ExifData Empty { get; } = new(null, null, null, null, null, null, null, null, null);
    }

    private sealed class ExifParser
    {
        private readonly byte[] _data;
        private readonly int _start;
        private readonly int _length;
        private readonly bool _littleEndian;

        private ExifParser(byte[] data, int start, int length)
        {
            _data = data;
            _start = start;
            _length = Math.Min(length, data.Length - start);
            if (_length < 8 || start < 0 || start > data.Length - 8)
            {
                throw new InvalidDataException("EXIF header is truncated.");
            }

            _littleEndian = data[start] == (byte)'I' && data[start + 1] == (byte)'I';
            if (!_littleEndian && !(data[start] == (byte)'M' && data[start + 1] == (byte)'M') || ReadUInt16(2) != 42)
            {
                throw new InvalidDataException("EXIF byte order or marker is invalid.");
            }
        }

        public static ExifData Parse(byte[] data, int start, int length) => new ExifParser(data, start, length).Read();

        private ExifData Read()
        {
            var values = new Dictionary<ushort, IfdValue>();
            var ifd0 = checked((int)ReadUInt32(4));
            ReadIfd(ifd0, values);
            if (ReadPointer(values, 0x8769) is { } exifOffset)
            {
                ReadIfd(exifOffset, values);
            }

            var gps = new Dictionary<ushort, IfdValue>();
            if (ReadPointer(values, 0x8825) is { } gpsOffset)
            {
                ReadIfd(gpsOffset, gps);
            }

            var timestamp = ReadAscii(values, 0x9003) ?? ReadAscii(values, 0x0132);
            var offset = ReadAscii(values, 0x9011);
            var captured = ParseExifDate(timestamp, offset);
            return new ExifData(
                ReadPositiveInt(values, 0xA002) ?? ReadPositiveInt(values, 0x0100),
                ReadPositiveInt(values, 0xA003) ?? ReadPositiveInt(values, 0x0101),
                ReadAscii(values, 0x010F),
                ReadAscii(values, 0x0110),
                ReadPositiveInt(values, 0x0112),
                captured,
                captured.HasValue ? null : timestamp,
                ReadCoordinate(gps, 0x0002, 0x0001, 'S'),
                ReadCoordinate(gps, 0x0004, 0x0003, 'W'));
        }

        private void ReadIfd(int relativeOffset, IDictionary<ushort, IfdValue> values)
        {
            if (!Contains(relativeOffset, 2))
            {
                throw new InvalidDataException("EXIF IFD offset is outside the bounded header.");
            }

            var count = Math.Min(ReadUInt16(relativeOffset), (ushort)128);
            for (var index = 0; index < count; index++)
            {
                var entry = checked(relativeOffset + 2 + index * 12);
                if (!Contains(entry, 12))
                {
                    throw new InvalidDataException("EXIF IFD entry is truncated.");
                }

                var tag = ReadUInt16(entry);
                var type = ReadUInt16(entry + 2);
                var itemCount = ReadUInt32(entry + 4);
                if (itemCount > 1_024)
                {
                    continue;
                }

                var typeSize = type switch { 1 or 2 or 7 => 1, 3 => 2, 4 or 9 => 4, 5 or 10 => 8, _ => 0 };
                if (typeSize == 0 || itemCount > int.MaxValue / typeSize)
                {
                    continue;
                }

                var byteCount = (int)itemCount * typeSize;
                var valueOffset = byteCount <= 4 ? entry + 8 : checked((int)ReadUInt32(entry + 8));
                if (Contains(valueOffset, byteCount))
                {
                    values[tag] = new IfdValue(type, (int)itemCount, valueOffset);
                }
            }
        }

        private int? ReadPointer(IReadOnlyDictionary<ushort, IfdValue> values, ushort tag) =>
            values.TryGetValue(tag, out var value) && value.Type == 4 && value.Count > 0
                ? checked((int)ReadUInt32(value.Offset))
                : null;

        private int? ReadPositiveInt(IReadOnlyDictionary<ushort, IfdValue> values, ushort tag)
        {
            if (!values.TryGetValue(tag, out var value) || value.Count < 1)
            {
                return null;
            }

            var number = value.Type switch
            {
                3 => ReadUInt16(value.Offset),
                4 => ReadUInt32(value.Offset),
                _ => 0u,
            };
            return number is > 0 and <= 1_000_000 ? (int)number : null;
        }

        private string? ReadAscii(IReadOnlyDictionary<ushort, IfdValue> values, ushort tag)
        {
            if (!values.TryGetValue(tag, out var value) || value.Type != 2 || value.Count < 1)
            {
                return null;
            }

            var count = Math.Min(value.Count, 256);
            var text = Encoding.ASCII.GetString(_data, _start + value.Offset, count).TrimEnd('\0', ' ');
            return text.Length == 0 || text.Any(char.IsControl) ? null : text;
        }

        private double? ReadCoordinate(
            IReadOnlyDictionary<ushort, IfdValue> values,
            ushort valueTag,
            ushort referenceTag,
            char negativeReference)
        {
            if (!values.TryGetValue(valueTag, out var value) || value.Type != 5 || value.Count < 3)
            {
                return null;
            }

            var degrees = ReadRational(value.Offset);
            var minutes = ReadRational(value.Offset + 8);
            var seconds = ReadRational(value.Offset + 16);
            if (!degrees.HasValue || !minutes.HasValue || !seconds.HasValue)
            {
                return null;
            }

            var coordinate = degrees.Value + minutes.Value / 60d + seconds.Value / 3600d;
            var reference = ReadAscii(values, referenceTag);
            return reference?.Length > 0 && char.ToUpperInvariant(reference[0]) == negativeReference
                ? -coordinate
                : coordinate;
        }

        private double? ReadRational(int offset)
        {
            if (!Contains(offset, 8))
            {
                return null;
            }

            var numerator = ReadUInt32(offset);
            var denominator = ReadUInt32(offset + 4);
            return denominator == 0 ? null : numerator / (double)denominator;
        }

        private DateTimeOffset? ParseExifDate(string? timestamp, string? offset)
        {
            if (string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(offset) ||
                !DateTime.TryParseExact(timestamp, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                !TimeSpan.TryParseExact(offset, @"hh\:mm", CultureInfo.InvariantCulture, out var zone))
            {
                return null;
            }

            return new DateTimeOffset(date, zone).ToUniversalTime();
        }

        private bool Contains(int relativeOffset, int count) =>
            relativeOffset >= 0 && count >= 0 && relativeOffset <= _length - count;

        private ushort ReadUInt16(int relativeOffset)
        {
            if (!Contains(relativeOffset, 2))
            {
                throw new InvalidDataException("EXIF integer is outside the bounded header.");
            }

            var span = _data.AsSpan(_start + relativeOffset, 2);
            return _littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(span)
                : BinaryPrimitives.ReadUInt16BigEndian(span);
        }

        private uint ReadUInt32(int relativeOffset)
        {
            if (!Contains(relativeOffset, 4))
            {
                throw new InvalidDataException("EXIF integer is outside the bounded header.");
            }

            var span = _data.AsSpan(_start + relativeOffset, 4);
            return _littleEndian
                ? BinaryPrimitives.ReadUInt32LittleEndian(span)
                : BinaryPrimitives.ReadUInt32BigEndian(span);
        }

        private sealed record IfdValue(ushort Type, int Count, int Offset);
    }
}
