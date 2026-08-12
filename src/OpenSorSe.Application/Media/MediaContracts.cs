using OpenSorSe.Core.Configuration;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Media;

/// <summary>Identifies the ordinary media family represented by an indexed file.</summary>
public enum MediaKind
{
    /// <summary>The file is not a supported media input.</summary>
    None,
    /// <summary>A still image.</summary>
    Image,
    /// <summary>An audio recording or track.</summary>
    Audio,
    /// <summary>A video recording or container.</summary>
    Video,
}

/// <summary>Identifies one independently detectable media capability.</summary>
public enum MediaCapabilityKind
{
    /// <summary>Deterministic bounded image-header and EXIF metadata.</summary>
    ImageMetadata,
    /// <summary>Local OCR over supported image inputs.</summary>
    ImageOcr,
    /// <summary>Audio metadata supplied by an available local provider.</summary>
    AudioMetadata,
    /// <summary>Optional local speech transcription.</summary>
    Transcription,
    /// <summary>Video metadata supplied by an available local provider.</summary>
    VideoMetadata,
    /// <summary>Bounded representative-frame extraction.</summary>
    VideoFrameSampling,
    /// <summary>Optional local, unverified visual descriptions.</summary>
    VisualDescription,
    /// <summary>Bounded application-owned preview generation.</summary>
    Thumbnail,
}

/// <summary>Identifies a controlled media-provider outcome.</summary>
public enum MediaExtractionStatus
{
    /// <summary>Useful bounded data was produced.</summary>
    Completed,
    /// <summary>Useful data was produced but one capability or bound was unavailable.</summary>
    PartiallyCompleted,
    /// <summary>Processing was deliberately disabled or not applicable.</summary>
    Skipped,
    /// <summary>The input type is not supported by the provider.</summary>
    Unsupported,
    /// <summary>An optional local provider is not configured or installed.</summary>
    Unavailable,
    /// <summary>The input exceeded a configured resource bound.</summary>
    LimitExceeded,
    /// <summary>The provider failed safely for this file.</summary>
    Failed,
}

/// <summary>Describes a provider capability state without treating optional absence as a failure.</summary>
public enum MediaCapabilityState
{
    /// <summary>The user disabled the capability.</summary>
    Disabled,
    /// <summary>Required user-managed configuration is missing.</summary>
    NotConfigured,
    /// <summary>Configuration exists but cannot be accepted safely.</summary>
    InvalidConfiguration,
    /// <summary>The optional runtime or platform capability is unavailable.</summary>
    Unavailable,
    /// <summary>The capability is ready for bounded work.</summary>
    Available,
    /// <summary>The capability is currently processing work.</summary>
    Processing,
    /// <summary>Capability detection failed safely.</summary>
    Error,
}

/// <summary>Describes one media capability without opening user content.</summary>
public sealed record MediaCapability(
    MediaCapabilityKind Kind,
    bool IsAvailable,
    string Provider,
    string? ProviderVersion,
    string Message)
{
    /// <summary>Gets the user-facing capability state.</summary>
    public MediaCapabilityState State { get; init; } = IsAvailable
        ? MediaCapabilityState.Available
        : MediaCapabilityState.Unavailable;
}

/// <summary>Contains one bounded speech segment with optional playback timing.</summary>
public sealed record MediaTranscriptSegment(TimeSpan Start, TimeSpan? End, string Text);

/// <summary>Contains deterministic embedded or container metadata for one media file.</summary>
public sealed record MediaMetadata
{
    /// <summary>Gets the detected media family.</summary>
    public required MediaKind Kind { get; init; }
    /// <summary>Gets the normalized container or image format name.</summary>
    public string? Container { get; init; }
    /// <summary>Gets width in pixels when present and valid.</summary>
    public int? Width { get; init; }
    /// <summary>Gets height in pixels when present and valid.</summary>
    public int? Height { get; init; }
    /// <summary>Gets playback duration when present and within supported bounds.</summary>
    public TimeSpan? Duration { get; init; }
    /// <summary>Gets video frame rate when the container reports a finite value.</summary>
    public double? FrameRate { get; init; }
    /// <summary>Gets the primary video codec name.</summary>
    public string? VideoCodec { get; init; }
    /// <summary>Gets the primary audio codec name.</summary>
    public string? AudioCodec { get; init; }
    /// <summary>Gets the reported aggregate or stream bitrate.</summary>
    public long? BitRate { get; init; }
    /// <summary>Gets audio sample rate in hertz.</summary>
    public int? SampleRate { get; init; }
    /// <summary>Gets the audio channel count.</summary>
    public int? Channels { get; init; }
    /// <summary>Gets a bounded embedded title.</summary>
    public string? Title { get; init; }
    /// <summary>Gets a bounded embedded artist.</summary>
    public string? Artist { get; init; }
    /// <summary>Gets a bounded embedded album.</summary>
    public string? Album { get; init; }
    /// <summary>Gets a bounded embedded track identifier.</summary>
    public string? Track { get; init; }
    /// <summary>Gets the camera or device manufacturer.</summary>
    public string? DeviceMake { get; init; }
    /// <summary>Gets the camera or device model.</summary>
    public string? DeviceModel { get; init; }
    /// <summary>Gets EXIF orientation from 1 through 8.</summary>
    public int? Orientation { get; init; }
    /// <summary>Gets the embedded capture timestamp when it includes a reliable offset.</summary>
    public DateTimeOffset? CapturedAtUtc { get; init; }
    /// <summary>Gets bounded original timestamp text when no reliable UTC offset exists.</summary>
    public string? CaptureTimestampText { get; init; }
    /// <summary>Gets embedded latitude. It remains local and is never reverse-geocoded.</summary>
    public double? Latitude { get; init; }
    /// <summary>Gets embedded longitude. It remains local and is never reverse-geocoded.</summary>
    public double? Longitude { get; init; }
    /// <summary>Gets bounded additional embedded textual fields in deterministic name order.</summary>
    public IReadOnlyDictionary<string, string> TextFields { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>Contains one isolated metadata-provider response.</summary>
public sealed record MediaMetadataResult(
    MediaExtractionStatus Status,
    MediaMetadata? Metadata,
    string Provider,
    string ProviderVersion,
    TimeSpan ProcessingDuration,
    IReadOnlyList<string> Warnings,
    string Message);

/// <summary>Contains one bounded optional transcription response.</summary>
public sealed record MediaTranscriptionResult(
    MediaExtractionStatus Status,
    string? Text,
    IReadOnlyList<MediaTranscriptSegment> Segments,
    string Provider,
    string ProviderVersion,
    TimeSpan ProcessingDuration,
    IReadOnlyList<string> Warnings,
    string Message);

/// <summary>Contains one bounded optional visual-description response.</summary>
public sealed record MediaDescriptionResult(
    MediaExtractionStatus Status,
    string? Description,
    IReadOnlyList<string> Tags,
    string Provider,
    string ProviderVersion,
    TimeSpan ProcessingDuration,
    IReadOnlyList<string> Warnings,
    string Message);

/// <summary>Describes one representative video frame in an application-owned temporary workspace.</summary>
public sealed record VideoFrameSample(string ImagePath, TimeSpan Position, long EncodedBytes);

/// <summary>Contains a caller-owned bounded video-frame sample set.</summary>
public sealed record VideoFrameSampleBatch(
    MediaExtractionStatus Status,
    string? WorkspacePath,
    IReadOnlyList<VideoFrameSample> Frames,
    string Provider,
    string ProviderVersion,
    TimeSpan ProcessingDuration,
    IReadOnlyList<string> Warnings,
    string Message);

/// <summary>Contains durable, provider-neutral media evidence stored with a content fingerprint.</summary>
public sealed record IndexedMediaEvidence
{
    /// <summary>Gets the detected media family.</summary>
    public required MediaKind Kind { get; init; }
    /// <summary>Gets deterministic embedded or container metadata.</summary>
    public required MediaMetadata Metadata { get; init; }
    /// <summary>Gets bounded locally produced speech text.</summary>
    public string? Transcript { get; init; }
    /// <summary>Gets bounded transcript segments with timestamps when the provider supplies them.</summary>
    public IReadOnlyList<MediaTranscriptSegment> TranscriptSegments { get; init; } = [];
    /// <summary>Gets bounded OCR text from an image or representative video frames.</summary>
    public string? OcrText { get; init; }
    /// <summary>Gets an optional unverified local-provider description.</summary>
    public string? VisualDescription { get; init; }
    /// <summary>Gets bounded optional description tags.</summary>
    public IReadOnlyList<string> VisualTags { get; init; } = [];
    /// <summary>Gets the metadata provider identity.</summary>
    public string MetadataProvider { get; init; } = string.Empty;
    /// <summary>Gets the metadata provider version.</summary>
    public string MetadataProviderVersion { get; init; } = string.Empty;
    /// <summary>Gets the transcription provider identity when an attempt was made.</summary>
    public string? TranscriptionProvider { get; init; }
    /// <summary>Gets the visual-description provider identity when an attempt was made.</summary>
    public string? DescriptionProvider { get; init; }
    /// <summary>Gets the number of representative video frames successfully produced.</summary>
    public int SampledFrameCount { get; init; }
    /// <summary>Gets the number of representative frames that produced OCR text.</summary>
    public int OcrFrameCount { get; init; }
    /// <summary>Gets whether this result reused a compatible local extraction record.</summary>
    public bool CacheHit { get; init; }
    /// <summary>Gets the stable settings/provider fingerprint for incremental invalidation.</summary>
    public required string ProcessingFingerprint { get; init; }
    /// <summary>Gets the combined controlled outcome.</summary>
    public MediaExtractionStatus Status { get; init; }
    /// <summary>Gets bounded privacy-safe provider warnings.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
    /// <summary>Gets total measured processing time.</summary>
    public TimeSpan ProcessingDuration { get; init; }
}

/// <summary>Contains one complete bounded media pass for the existing content pipeline.</summary>
public sealed record MediaIntelligenceResult(
    MediaExtractionStatus Status,
    IndexedMediaEvidence? Evidence,
    IReadOnlyList<MediaCapability> Capabilities,
    string Message);

/// <summary>Extracts deterministic metadata for one supported media family.</summary>
public interface IMediaMetadataProvider
{
    /// <summary>Gets a stable provider identity used by cache provenance.</summary>
    string Name { get; }
    /// <summary>Gets a stable provider implementation version.</summary>
    string Version { get; }
    /// <summary>Returns whether this provider understands the normalized extension and media kind.</summary>
    bool Supports(MediaKind kind, string normalizedExtension);
    /// <summary>Detects local capability without opening the user file.</summary>
    Task<MediaCapability> DetectCapabilityAsync(MediaKind kind, CancellationToken cancellationToken);
    /// <summary>Extracts bounded metadata without executing embedded content.</summary>
    Task<MediaMetadataResult> ExtractAsync(FileEntry file, MediaIntelligenceSettings settings, CancellationToken cancellationToken);
}

/// <summary>Abstracts optional local speech transcription without selecting a runtime in Application code.</summary>
public interface IMediaTranscriptionProvider
{
    /// <summary>Gets a stable provider identity.</summary>
    string Name { get; }
    /// <summary>Gets a stable provider implementation/model version.</summary>
    string Version { get; }
    /// <summary>Detects local capability without opening user media.</summary>
    Task<MediaCapability> DetectCapabilityAsync(CancellationToken cancellationToken);
    /// <summary>Creates bounded speech text for one known media file.</summary>
    Task<MediaTranscriptionResult> TranscribeAsync(FileEntry file, MediaMetadata metadata, MediaIntelligenceSettings settings, CancellationToken cancellationToken);
}

/// <summary>Abstracts optional local visual descriptions as unverified derived evidence.</summary>
public interface IMediaVisualDescriptionProvider
{
    /// <summary>Gets a stable provider identity.</summary>
    string Name { get; }
    /// <summary>Gets a stable provider/model version.</summary>
    string Version { get; }
    /// <summary>Detects local capability without opening user media.</summary>
    Task<MediaCapability> DetectCapabilityAsync(CancellationToken cancellationToken);
    /// <summary>Describes a bounded image or representative frame set.</summary>
    Task<MediaDescriptionResult> DescribeAsync(FileEntry file, IReadOnlyList<VideoFrameSample> representativeFrames, MediaIntelligenceSettings settings, CancellationToken cancellationToken);
}

/// <summary>Produces a bounded deterministic representative-frame set for a video.</summary>
public interface IVideoFrameSampler
{
    /// <summary>Gets a stable provider identity.</summary>
    string Name { get; }
    /// <summary>Gets a stable provider version.</summary>
    string Version { get; }
    /// <summary>Detects local frame-extraction capability.</summary>
    Task<MediaCapability> DetectCapabilityAsync(CancellationToken cancellationToken);
    /// <summary>Samples deterministic evenly spaced frames within explicit bounds.</summary>
    Task<VideoFrameSampleBatch> SampleAsync(FileEntry file, MediaMetadata metadata, MediaIntelligenceSettings settings, CancellationToken cancellationToken);
    /// <summary>Deletes only the verified application-owned workspace returned by this provider.</summary>
    void DeleteWorkspace(string workspacePath);
}

/// <summary>Coordinates media providers, OCR reuse, bounds, caching, diagnostics, and failure isolation.</summary>
public interface IMediaIntelligenceService
{
    /// <summary>Returns the supported media family for one filename.</summary>
    MediaKind Classify(string fullPath);
    /// <summary>Returns a bounded current capability snapshot without opening user media.</summary>
    Task<IReadOnlyList<MediaCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken);
    /// <summary>Runs only bounded deterministic metadata extraction for Basic indexing.</summary>
    Task<MediaIntelligenceResult> ExtractMetadataAsync(FileEntry file, IndexedMediaEvidence? existing, CancellationToken cancellationToken);
    /// <summary>Runs or reuses one bounded media extraction pass.</summary>
    Task<MediaIntelligenceResult> ExtractAsync(FileEntry file, IndexedMediaEvidence? existing, bool allowOcr, CancellationToken cancellationToken);
}

/// <summary>Creates and reuses bounded application-owned previews without changing source media.</summary>
public interface IMediaThumbnailProvider
{
    /// <summary>Gets local preview capability for the supplied media kind.</summary>
    Task<MediaCapability> DetectCapabilityAsync(MediaKind kind, CancellationToken cancellationToken);
    /// <summary>Returns a managed thumbnail path or null when the source is unsupported or unsafe to decode.</summary>
    Task<string?> GetThumbnailAsync(string fullPath, IndexedMediaEvidence evidence, CancellationToken cancellationToken);
    /// <summary>Clears application-owned preview files without changing source media.</summary>
    Task ClearAsync(CancellationToken cancellationToken);
}
