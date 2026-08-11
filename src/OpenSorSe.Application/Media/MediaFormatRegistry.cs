using OpenSorSe.Core.Configuration;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Media;

/// <summary>Provides the exact conservative media extension policy used by extraction and Search.</summary>
public static class MediaFormatRegistry
{
    private static readonly HashSet<string> ImageExtensions = new(
        [".bmp", ".jpeg", ".jpg", ".png", ".tif", ".tiff", ".webp"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AudioExtensions = new(
        [".flac", ".m4a", ".mp3", ".wav"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> VideoExtensions = new(
        [".avi", ".mkv", ".mov", ".mp4"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the supported media family based only on a normalized filename extension.</summary>
    public static MediaKind Classify(string pathOrExtension)
    {
        var extension = pathOrExtension.StartsWith(".", StringComparison.Ordinal)
            ? pathOrExtension
            : Path.GetExtension(pathOrExtension);
        extension = extension.ToLowerInvariant();
        if (ImageExtensions.Contains(extension))
        {
            return MediaKind.Image;
        }

        if (AudioExtensions.Contains(extension))
        {
            return MediaKind.Audio;
        }

        return VideoExtensions.Contains(extension) ? MediaKind.Video : MediaKind.None;
    }

    /// <summary>Gets the immutable supported image-extension set.</summary>
    public static IReadOnlyCollection<string> Images => ImageExtensions;

    /// <summary>Gets the immutable supported audio-extension set.</summary>
    public static IReadOnlyCollection<string> Audio => AudioExtensions;

    /// <summary>Gets the immutable supported video-extension set.</summary>
    public static IReadOnlyCollection<string> Video => VideoExtensions;
}

/// <summary>Truthfully reports that no transcription runtime is configured by the desktop application.</summary>
public sealed class UnavailableMediaTranscriptionProvider : IMediaTranscriptionProvider
{
    /// <inheritdoc />
    public string Name => "transcription-not-configured";

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public Task<MediaCapability> DetectCapabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new MediaCapability(
            MediaCapabilityKind.Transcription,
            false,
            Name,
            Version,
            "Transcription is not configured. Metadata and ordinary Search remain available."));
    }

    /// <inheritdoc />
    public Task<MediaTranscriptionResult> TranscribeAsync(
        FileEntry file,
        MediaMetadata metadata,
        MediaIntelligenceSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new MediaTranscriptionResult(
            MediaExtractionStatus.Unavailable,
            null,
            [],
            Name,
            Version,
            TimeSpan.Zero,
            [],
            "Transcription is not configured."));
    }
}

/// <summary>Truthfully reports that no local visual-description provider is configured.</summary>
public sealed class UnavailableMediaVisualDescriptionProvider : IMediaVisualDescriptionProvider
{
    /// <inheritdoc />
    public string Name => "visual-description-not-configured";

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public Task<MediaCapability> DetectCapabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new MediaCapability(
            MediaCapabilityKind.VisualDescription,
            false,
            Name,
            Version,
            "Visual descriptions require a compatible explicitly configured local provider."));
    }

    /// <inheritdoc />
    public Task<MediaDescriptionResult> DescribeAsync(
        FileEntry file,
        IReadOnlyList<VideoFrameSample> representativeFrames,
        MediaIntelligenceSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new MediaDescriptionResult(
            MediaExtractionStatus.Unavailable,
            null,
            [],
            Name,
            Version,
            TimeSpan.Zero,
            [],
            "Visual descriptions are not configured."));
    }
}
