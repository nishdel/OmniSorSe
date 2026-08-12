using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Media;

/// <summary>
/// Uses an optional user-managed whisper.cpp CLI and model for bounded local speech transcription.
/// The provider never downloads a runtime or model and owns every temporary/output path it creates.
/// </summary>
public sealed class WhisperCppTranscriptionProvider : IMediaTranscriptionProvider, IDisposable
{
    private const int MaximumJsonCharacters = 2_097_152;
    private const int MaximumSegments = 512;
    private readonly IConfigurationService _configurationService;
    private readonly IExternalToolLocator _toolLocator;
    private readonly IMediaProcessRunner _processRunner;
    private readonly string _temporaryRoot;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _activeOperations;
    private bool _disposed;

    /// <summary>Initializes the optional local whisper.cpp provider.</summary>
    public WhisperCppTranscriptionProvider(
        IConfigurationService configurationService,
        IExternalToolLocator toolLocator,
        IMediaProcessRunner processRunner,
        string temporaryRoot)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _toolLocator = toolLocator ?? throw new ArgumentNullException(nameof(toolLocator));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryRoot);
        _temporaryRoot = Path.GetFullPath(temporaryRoot);
    }

    /// <inheritdoc />
    public string Name => "whisper.cpp-cli";

    /// <inheritdoc />
    public string Version => CreateConfigurationFingerprint(_configurationService.Current.MediaIntelligence);

    /// <inheritdoc />
    public async Task<MediaCapability> DetectCapabilityAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var settings = _configurationService.Current.MediaIntelligence;
        var model = InspectModel(settings.WhisperModelPath);
        if (model.State != MediaCapabilityState.Available)
        {
            return new MediaCapability(MediaCapabilityKind.Transcription, false, Name, Version, model.Message)
            {
                State = model.State,
            };
        }

        var location = LocateRuntime(settings);
        if (!location.IsAvailable || string.IsNullOrWhiteSpace(location.ExecutablePath))
        {
            return new MediaCapability(
                MediaCapabilityKind.Transcription,
                false,
                Name,
                Version,
                "whisper.cpp was not found. Configure an absolute whisper-cli path; ordinary Search remains available.")
            {
                State = MediaCapabilityState.Unavailable,
            };
        }

        if (Volatile.Read(ref _activeOperations) > 0)
        {
            return new MediaCapability(
                MediaCapabilityKind.Transcription,
                true,
                Name,
                Version,
                "Local speech transcription is processing one bounded media item.")
            {
                State = MediaCapabilityState.Processing,
            };
        }

        try
        {
            var result = await _processRunner.ExecuteAsync(
                location.ExecutablePath,
                ["--help"],
                8_192,
                8_192,
                TimeSpan.FromSeconds(5),
                cancellationToken).ConfigureAwait(false);
            var available = result.ExitCode == 0 && !result.StandardOutputTruncated;
            return new MediaCapability(
                MediaCapabilityKind.Transcription,
                available,
                Name,
                Version,
                available
                    ? "Local speech transcription is available with the configured whisper.cpp runtime and model."
                    : "whisper.cpp was found but its command-line capability could not be validated.")
            {
                State = available ? MediaCapabilityState.Available : MediaCapabilityState.Error,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return new MediaCapability(MediaCapabilityKind.Transcription, false, Name, Version, "whisper.cpp capability detection timed out.")
            {
                State = MediaCapabilityState.Error,
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new MediaCapability(MediaCapabilityKind.Transcription, false, Name, Version, "whisper.cpp could not be started safely.")
            {
                State = MediaCapabilityState.Error,
            };
        }
    }

    /// <inheritdoc />
    public async Task<MediaTranscriptionResult> TranscribeAsync(
        FileEntry file,
        MediaMetadata metadata,
        MediaIntelligenceSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfDisposed();
        var started = Stopwatch.StartNew();
        if (metadata.Kind is not (MediaKind.Audio or MediaKind.Video) || metadata.Duration is null || metadata.Duration <= TimeSpan.Zero)
        {
            return Result(MediaExtractionStatus.Skipped, started.Elapsed, "A finite audio or video duration is required for transcription.");
        }

        var limit = metadata.Kind == MediaKind.Video
            ? TimeSpan.FromMinutes(settings.MaximumVideoDurationMinutes)
            : TimeSpan.FromMinutes(settings.MaximumAudioDurationMinutes);
        if (metadata.Duration > limit)
        {
            return Result(MediaExtractionStatus.LimitExceeded, started.Elapsed, "The media duration exceeds the configured local transcription limit.");
        }

        var capability = await DetectCapabilityAsync(cancellationToken).ConfigureAwait(false);
        if (!capability.IsAvailable)
        {
            return Result(MediaExtractionStatus.Unavailable, started.Elapsed, capability.Message);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _activeOperations);
        string? workspace = null;
        try
        {
            var runtime = LocateRuntime(settings).ExecutablePath;
            var model = InspectModel(settings.WhisperModelPath).Path;
            if (string.IsNullOrWhiteSpace(runtime) || string.IsNullOrWhiteSpace(model))
            {
                return Result(MediaExtractionStatus.Unavailable, started.Elapsed, "The configured whisper.cpp runtime or model became unavailable.");
            }

            workspace = CreateWorkspace();
            var input = await PrepareInputAsync(file, metadata.Kind, settings, workspace, cancellationToken).ConfigureAwait(false);
            if (input is null)
            {
                return Result(MediaExtractionStatus.Unavailable, started.Elapsed, "ffmpeg is required to prepare this audio/container for local transcription.");
            }

            var outputBase = Path.Combine(workspace, "transcript");
            var process = await _processRunner.ExecuteAsync(
                runtime,
                ["-m", model, "-f", input, "-oj", "-of", outputBase, "-np"],
                16_384,
                16_384,
                TimeSpan.FromSeconds(settings.TranscriptionTimeoutSeconds),
                cancellationToken).ConfigureAwait(false);
            var jsonPath = outputBase + ".json";
            if (process.ExitCode != 0 || process.StandardOutputTruncated || !File.Exists(jsonPath))
            {
                return Result(
                    process.StandardOutputTruncated ? MediaExtractionStatus.LimitExceeded : MediaExtractionStatus.Failed,
                    started.Elapsed,
                    process.StandardOutputTruncated
                        ? "whisper.cpp output exceeded its diagnostic bound."
                        : "whisper.cpp could not transcribe this media file safely.");
            }

            var json = await ReadBoundedAsync(jsonPath, MaximumJsonCharacters, cancellationToken).ConfigureAwait(false);
            var parsed = Parse(json, settings.MaximumTranscriptCharacters);
            return new MediaTranscriptionResult(
                MediaExtractionStatus.Completed,
                parsed.Text,
                parsed.Segments,
                Name,
                Version,
                started.Elapsed,
                [],
                "Speech was transcribed locally by the configured whisper.cpp runtime.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return Result(MediaExtractionStatus.Failed, started.Elapsed, "Local transcription timed out and its temporary workspace was removed.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Result(MediaExtractionStatus.Failed, started.Elapsed, "Local transcription failed safely; the file can be retried.");
        }
        finally
        {
            if (workspace is not null)
            {
                DeleteWorkspace(workspace);
            }

            Interlocked.Decrement(ref _activeOperations);
            _gate.Release();
        }
    }

    /// <summary>Releases the conservative single-job transcription gate.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
    }

    internal static (string Text, IReadOnlyList<MediaTranscriptSegment> Segments) Parse(string json, int maximumCharacters)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        if (!document.RootElement.TryGetProperty("transcription", out var transcription) ||
            transcription.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The whisper.cpp response did not contain a transcription array.");
        }

        var segments = new List<MediaTranscriptSegment>();
        var text = new StringBuilder(Math.Min(maximumCharacters, 4096));
        foreach (var item in transcription.EnumerateArray().Take(MaximumSegments))
        {
            var value = item.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String
                ? Normalize(textElement.GetString())
                : null;
            if (string.IsNullOrWhiteSpace(value) || text.Length >= maximumCharacters)
            {
                continue;
            }

            var separatorLength = text.Length > 0 ? 1 : 0;
            var remaining = maximumCharacters - text.Length - separatorLength;
            if (remaining <= 0)
            {
                break;
            }

            value = value[..Math.Min(value.Length, remaining)];
            if (text.Length > 0)
            {
                text.Append(' ');
            }

            text.Append(value);
            if (item.TryGetProperty("offsets", out var offsets) && offsets.ValueKind == JsonValueKind.Object &&
                TryMilliseconds(offsets, "from", out var from) && TryMilliseconds(offsets, "to", out var to) && to >= from)
            {
                segments.Add(new MediaTranscriptSegment(TimeSpan.FromMilliseconds(from), TimeSpan.FromMilliseconds(to), value));
            }
        }

        if (text.Length == 0)
        {
            throw new InvalidDataException("The whisper.cpp response contained no bounded transcript text.");
        }

        return (text.ToString(), segments.AsReadOnly());
    }

    internal string CreateConfigurationFingerprint(MediaIntelligenceSettings settings)
    {
        var value = string.Join(
            '|',
            "whisper.cpp-contract-2.3.0",
            FileIdentity(settings.WhisperExecutablePath),
            FileIdentity(settings.WhisperModelPath),
            settings.TranscriptionTimeoutSeconds,
            settings.MaximumTranscriptCharacters);
        return "2.3.0-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..16];
    }

    private async Task<string?> PrepareInputAsync(
        FileEntry file,
        MediaKind kind,
        MediaIntelligenceSettings settings,
        string workspace,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(file.FullPath).ToLowerInvariant();
        if (kind == MediaKind.Audio && extension is ".wav" or ".mp3" or ".flac" or ".ogg")
        {
            return file.FullPath;
        }

        var ffmpeg = _toolLocator.Locate("ffmpeg", settings.FfmpegExecutablePath);
        if (!ffmpeg.IsAvailable || string.IsNullOrWhiteSpace(ffmpeg.ExecutablePath))
        {
            return null;
        }

        var output = Path.Combine(workspace, "speech.wav");
        var process = await _processRunner.ExecuteAsync(
            ffmpeg.ExecutablePath,
            ["-nostdin", "-hide_banner", "-loglevel", "error", "-i", file.FullPath, "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", "-y", output],
            2_048,
            8_192,
            TimeSpan.FromSeconds(settings.ProviderTimeoutSeconds),
            cancellationToken).ConfigureAwait(false);
        return process.ExitCode == 0 && File.Exists(output) && new FileInfo(output).Length > 44 ? output : null;
    }

    private ExternalToolLocation LocateRuntime(MediaIntelligenceSettings settings) =>
        _toolLocator.Locate("whisper-cli", settings.WhisperExecutablePath);

    private static (MediaCapabilityState State, string? Path, string Message) InspectModel(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return (MediaCapabilityState.NotConfigured, null, "A local whisper.cpp model is not configured. No model is downloaded automatically.");
        }

        if (!Path.IsPathFullyQualified(configuredPath))
        {
            return (MediaCapabilityState.InvalidConfiguration, null, "The configured whisper.cpp model path must be absolute.");
        }

        try
        {
            var fullPath = Path.GetFullPath(configuredPath);
            if (!File.Exists(fullPath))
            {
                return (MediaCapabilityState.NotConfigured, null, "The configured whisper.cpp model file was not found.");
            }

            var length = new FileInfo(fullPath).Length;
            return length >= 1_048_576
                ? (MediaCapabilityState.Available, fullPath, "The configured local model is available.")
                : (MediaCapabilityState.InvalidConfiguration, null, "The configured whisper.cpp model file is too small to be a valid model.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (MediaCapabilityState.Error, null, "The configured whisper.cpp model file could not be inspected safely.");
        }
    }

    private string CreateWorkspace()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var workspace = Path.Combine(_temporaryRoot, $"transcription-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    private void DeleteWorkspace(string workspace)
    {
        try
        {
            var fullPath = Path.GetFullPath(workspace);
            var root = _temporaryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return;
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Cleanup is best effort: a cleanup failure must not hide the operation result.
            // Application-owned stale workspaces are eligible for normal cache maintenance.
        }
    }

    private MediaTranscriptionResult Result(MediaExtractionStatus status, TimeSpan duration, string message) =>
        new(status, null, [], Name, Version, duration, [], message);

    private static async Task<string> ReadBoundedAsync(string path, int maximumCharacters, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length > maximumCharacters * 4L)
        {
            throw new InvalidDataException("The transcription JSON exceeded its bounded size.");
        }

        var value = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return value.Length <= maximumCharacters
            ? value
            : throw new InvalidDataException("The transcription JSON exceeded its bounded size.");
    }

    private static bool TryMilliseconds(JsonElement offsets, string name, out double value)
    {
        value = 0;
        return offsets.TryGetProperty(name, out var element) &&
            element.TryGetDouble(out value) &&
            double.IsFinite(value) &&
            value is >= 0 and <= 2_592_000_000;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string FileIdentity(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
            {
                return path;
            }

            var info = new FileInfo(path);
            return string.Create(CultureInfo.InvariantCulture, $"{Path.GetFullPath(path)}:{info.Length}:{info.LastWriteTimeUtc.Ticks}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"unavailable:{path}";
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
