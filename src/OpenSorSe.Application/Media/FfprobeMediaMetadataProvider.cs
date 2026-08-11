using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Media;

/// <summary>Uses an optional local ffprobe executable for bounded audio and video metadata.</summary>
public sealed class FfprobeMediaMetadataProvider : IMediaMetadataProvider
{
    private const int MaximumJsonCharacters = 262_144;
    private readonly IConfigurationService _configurationService;
    private readonly IExternalToolLocator _toolLocator;
    private readonly IMediaProcessRunner _processRunner;

    /// <summary>Initializes the optional ffprobe provider.</summary>
    public FfprobeMediaMetadataProvider(
        IConfigurationService configurationService,
        IExternalToolLocator toolLocator,
        IMediaProcessRunner processRunner)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _toolLocator = toolLocator ?? throw new ArgumentNullException(nameof(toolLocator));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    /// <inheritdoc />
    public string Name => "ffprobe";

    /// <inheritdoc />
    public string Version => "contract-2.2.0";

    /// <inheritdoc />
    public bool Supports(MediaKind kind, string normalizedExtension) => kind switch
    {
        MediaKind.Audio => MediaFormatRegistry.Audio.Contains(normalizedExtension, StringComparer.OrdinalIgnoreCase),
        MediaKind.Video => MediaFormatRegistry.Video.Contains(normalizedExtension, StringComparer.OrdinalIgnoreCase),
        _ => false,
    };

    /// <inheritdoc />
    public async Task<MediaCapability> DetectCapabilityAsync(MediaKind kind, CancellationToken cancellationToken)
    {
        var capabilityKind = kind == MediaKind.Video ? MediaCapabilityKind.VideoMetadata : MediaCapabilityKind.AudioMetadata;
        var location = LocateExecutable();
        if (!location.IsAvailable || string.IsNullOrWhiteSpace(location.ExecutablePath))
        {
            return new MediaCapability(capabilityKind, false, Name, null, "ffprobe was not found. Filename Search remains available.");
        }

        try
        {
            var result = await _processRunner.ExecuteAsync(
                location.ExecutablePath,
                ["-version"],
                2_048,
                2_048,
                TimeSpan.FromSeconds(5),
                cancellationToken).ConfigureAwait(false);
            var version = result.ExitCode == 0
                ? Bound(result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), 128)
                : null;
            return new MediaCapability(
                capabilityKind,
                result.ExitCode == 0,
                Name,
                version,
                result.ExitCode == 0
                    ? "Local audio/video metadata extraction is available."
                    : "ffprobe was found but did not start successfully.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return new MediaCapability(capabilityKind, false, Name, null, "ffprobe capability detection timed out.");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new MediaCapability(capabilityKind, false, Name, null, "ffprobe could not be started safely.");
        }
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
        var kind = MediaFormatRegistry.Classify(file.FullPath);
        var capability = await DetectCapabilityAsync(kind, cancellationToken).ConfigureAwait(false);
        if (!capability.IsAvailable)
        {
            return new MediaMetadataResult(
                MediaExtractionStatus.Unavailable,
                null,
                Name,
                capability.ProviderVersion ?? Version,
                started.Elapsed,
                [],
                capability.Message);
        }

        try
        {
            var executable = LocateExecutable().ExecutablePath
                ?? throw new InvalidOperationException("ffprobe executable disappeared after capability detection.");
            var process = await _processRunner.ExecuteAsync(
                executable,
                ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", "--", file.FullPath],
                MaximumJsonCharacters,
                8_192,
                TimeSpan.FromSeconds(settings.ProviderTimeoutSeconds),
                cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0 || process.StandardOutputTruncated)
            {
                return new MediaMetadataResult(
                    process.StandardOutputTruncated ? MediaExtractionStatus.LimitExceeded : MediaExtractionStatus.Failed,
                    null,
                    Name,
                    capability.ProviderVersion ?? Version,
                    started.Elapsed,
                    [],
                    process.StandardOutputTruncated
                        ? "ffprobe metadata exceeded the configured response bound."
                        : "ffprobe could not read this media file or codec safely.");
            }

            var metadata = Parse(process.StandardOutput, kind);
            return new MediaMetadataResult(
                MediaExtractionStatus.Completed,
                metadata,
                Name,
                capability.ProviderVersion ?? Version,
                started.Elapsed,
                [],
                "Media metadata was extracted by local ffprobe.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return new MediaMetadataResult(MediaExtractionStatus.Failed, null, Name, Version, started.Elapsed, [], "ffprobe metadata extraction timed out.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new MediaMetadataResult(MediaExtractionStatus.Failed, null, Name, Version, started.Elapsed, [], "Media metadata was malformed or could not be read safely.");
        }
    }

    internal static MediaMetadata Parse(string json, MediaKind expectedKind)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        var streams = root.TryGetProperty("streams", out var streamArray) && streamArray.ValueKind == JsonValueKind.Array
            ? streamArray.EnumerateArray().Take(64).ToArray()
            : [];
        var video = streams.FirstOrDefault(stream => String(stream, "codec_type") == "video");
        var audio = streams.FirstOrDefault(stream => String(stream, "codec_type") == "audio");
        var format = root.TryGetProperty("format", out var formatElement) && formatElement.ValueKind == JsonValueKind.Object
            ? formatElement
            : default;
        var tags = MergeTags(format, video, audio);
        var durationSeconds = PositiveDouble(format, "duration") ??
            PositiveDouble(expectedKind == MediaKind.Video ? video : audio, "duration");
        var duration = durationSeconds is > 0 and <= 604_800
            ? TimeSpan.FromSeconds(durationSeconds.Value)
            : (TimeSpan?)null;
        var captured = ParseTimestamp(Tag(tags, "creation_time"));
        var textFields = tags
            .Where(item => item.Key is not ("title" or "artist" or "album" or "track" or "creation_time"))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Take(16)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        return new MediaMetadata
        {
            Kind = expectedKind,
            Container = Bound(String(format, "format_name"), 128),
            Width = PositiveInt(video, "width"),
            Height = PositiveInt(video, "height"),
            Duration = duration,
            FrameRate = ParseRate(String(video, "avg_frame_rate")),
            VideoCodec = Bound(String(video, "codec_name"), 64),
            AudioCodec = Bound(String(audio, "codec_name"), 64),
            BitRate = PositiveLong(format, "bit_rate") ?? PositiveLong(audio, "bit_rate"),
            SampleRate = PositiveInt(audio, "sample_rate"),
            Channels = PositiveInt(audio, "channels"),
            Title = Tag(tags, "title"),
            Artist = Tag(tags, "artist"),
            Album = Tag(tags, "album"),
            Track = Tag(tags, "track"),
            DeviceMake = Tag(tags, "com.apple.quicktime.make") ?? Tag(tags, "make"),
            DeviceModel = Tag(tags, "com.apple.quicktime.model") ?? Tag(tags, "model"),
            CapturedAtUtc = captured,
            CaptureTimestampText = captured.HasValue ? null : Tag(tags, "creation_time"),
            TextFields = textFields,
        };
    }

    private ExternalToolLocation LocateExecutable() =>
        _toolLocator.Locate("ffprobe", _configurationService.Current.MediaIntelligence.FfprobeExecutablePath);

    private static Dictionary<string, string> MergeTags(params JsonElement[] elements)
    {
        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in elements)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var property in tags.EnumerateObject().Take(64))
            {
                var name = Bound(property.Name.ToLowerInvariant(), 128);
                var value = property.Value.ValueKind == JsonValueKind.String ? Bound(property.Value.GetString(), 512) : null;
                if (!string.IsNullOrWhiteSpace(name) &&
                    !name.Any(char.IsControl) &&
                    !string.IsNullOrWhiteSpace(value) &&
                    !value.Any(char.IsControl))
                {
                    output.TryAdd(name, value);
                }
            }
        }

        return output;
    }

    private static string? Tag(IReadOnlyDictionary<string, string> tags, string name) =>
        tags.TryGetValue(name, out var value) ? value : null;

    private static string? String(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? PositiveInt(JsonElement element, string property) =>
        int.TryParse(String(element, property), NumberStyles.None, CultureInfo.InvariantCulture, out var textValue) && textValue > 0
            ? textValue
            : element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.TryGetInt32(out var numeric) && numeric > 0
                ? numeric
                : null;

    private static long? PositiveLong(JsonElement element, string property) =>
        long.TryParse(String(element, property), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;

    private static double? PositiveDouble(JsonElement element, string property) =>
        double.TryParse(String(element, property), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
        value > 0 && double.IsFinite(value) ? value : null;

    private static double? ParseRate(string? value)
    {
        var parts = value?.Split('/', 2);
        if (parts is not { Length: 2 } ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) ||
            denominator <= 0)
        {
            return null;
        }

        var rate = numerator / denominator;
        return rate is > 0 and <= 1_000 && double.IsFinite(rate) ? rate : null;
    }

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp) &&
        timestamp.Year is >= 1601 and <= 9998 ? timestamp.ToUniversalTime() : null;

    private static string? Bound(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= maximum ? value.Trim() : value.Trim()[..maximum];
}
