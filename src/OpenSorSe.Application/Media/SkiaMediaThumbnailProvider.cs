using System.Security.Cryptography;
using System.Text;
using OpenSorSe.Core.Configuration;
using SkiaSharp;

namespace OpenSorSe.Application.Media;

/// <summary>Creates bounded cached previews for supported still images without reading them at Search time.</summary>
public sealed class SkiaMediaThumbnailProvider : IMediaThumbnailProvider
{
    private readonly IConfigurationService _configurationService;
    private readonly string _cacheRoot;

    /// <summary>Initializes the application-owned preview cache.</summary>
    public SkiaMediaThumbnailProvider(IConfigurationService configurationService, string cacheRoot)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        _cacheRoot = Path.GetFullPath(cacheRoot);
    }

    /// <inheritdoc />
    public Task<MediaCapability> DetectCapabilityAsync(MediaKind kind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var available = kind == MediaKind.Image;
        return Task.FromResult(new MediaCapability(
            MediaCapabilityKind.Thumbnail,
            available,
            "SkiaSharp",
            typeof(SKCodec).Assembly.GetName().Version?.ToString(),
            available
                ? "Bounded still-image previews are available locally."
                : "Cached previews are currently implemented only for supported still images."));
    }

    /// <inheritdoc />
    public async Task<string?> GetThumbnailAsync(
        string fullPath,
        IndexedMediaEvidence evidence,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentNullException.ThrowIfNull(evidence);
        cancellationToken.ThrowIfCancellationRequested();
        if (evidence.Kind != MediaKind.Image || !MediaFormatRegistry.Images.Contains(Path.GetExtension(fullPath), StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var settings = _configurationService.Current.MediaIntelligence;
        var source = new FileInfo(Path.GetFullPath(fullPath));
        source.Refresh();
        if (!source.Exists || source.Length > settings.MaximumMediaFileSizeMiB * 1024L * 1024L)
        {
            return null;
        }

        if (evidence.Metadata.Width is { } knownWidth &&
            evidence.Metadata.Height is { } knownHeight &&
            (long)knownWidth * knownHeight > settings.MaximumThumbnailSourcePixels)
        {
            return null;
        }

        Directory.CreateDirectory(_cacheRoot);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(
            '|',
            source.FullName,
            source.Length,
            source.LastWriteTimeUtc.Ticks,
            settings.MaximumThumbnailDimension,
            evidence.Metadata.Orientation)))).ToLowerInvariant();
        var outputPath = Path.Combine(_cacheRoot, $"{key}.png");
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        return await Task.Run(
                () => CreateThumbnail(source, outputPath, settings, evidence.Metadata.Orientation, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(_cacheRoot))
        {
            return Task.CompletedTask;
        }

        foreach (var file in Directory.EnumerateFiles(_cacheRoot, "*.png", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(file);
        }

        return Task.CompletedTask;
    }

    private static string? CreateThumbnail(
        FileInfo source,
        string outputPath,
        MediaIntelligenceSettings settings,
        int? evidenceOrientation,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(
                source.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            using var codec = SKCodec.Create(stream);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
                (long)codec.Info.Width * codec.Info.Height > settings.MaximumThumbnailSourcePixels)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var scale = Math.Min(
                1f,
                settings.MaximumThumbnailDimension / (float)Math.Max(codec.Info.Width, codec.Info.Height));
            var dimensions = codec.GetScaledDimensions(scale);
            if (dimensions.Width <= 0 || dimensions.Height <= 0 ||
                dimensions.Width > settings.MaximumThumbnailDimension ||
                dimensions.Height > settings.MaximumThumbnailDimension)
            {
                return null;
            }

            var targetInfo = new SKImageInfo(dimensions.Width, dimensions.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var bitmap = new SKBitmap(targetInfo);
            var decode = codec.GetPixels(targetInfo, bitmap.GetPixels());
            if (decode is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var oriented = ApplyOrientation(bitmap, evidenceOrientation, cancellationToken);
            var outputBitmap = oriented ?? bitmap;
            using var image = SKImage.FromBitmap(outputBitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
            if (encoded is null || encoded.Size <= 0 || encoded.Size > 16L * 1024 * 1024)
            {
                return null;
            }

            var temporary = $"{outputPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    encoded.SaveTo(output);
                    output.Flush(flushToDisk: true);
                }

                File.Move(temporary, outputPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }

            return outputPath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static SKBitmap? ApplyOrientation(
        SKBitmap source,
        int? evidenceOrientation,
        CancellationToken cancellationToken)
    {
        var orientation = evidenceOrientation.GetValueOrDefault(1);
        if (orientation is < 2 or > 8)
        {
            return null;
        }

        var swapsDimensions = orientation is >= 5 and <= 8;
        var output = new SKBitmap(
            swapsDimensions ? source.Height : source.Width,
            swapsDimensions ? source.Width : source.Height,
            source.ColorType,
            source.AlphaType);
        try
        {
            for (var y = 0; y < output.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < output.Width; x++)
                {
                    var (sourceX, sourceY) = orientation switch
                    {
                        2 => (source.Width - 1 - x, y),
                        3 => (source.Width - 1 - x, source.Height - 1 - y),
                        4 => (x, source.Height - 1 - y),
                        5 => (y, x),
                        6 => (y, source.Height - 1 - x),
                        7 => (source.Width - 1 - y, source.Height - 1 - x),
                        8 => (source.Width - 1 - y, x),
                        _ => (x, y),
                    };
                    output.SetPixel(x, y, source.GetPixel(sourceX, sourceY));
                }
            }

            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }
}
