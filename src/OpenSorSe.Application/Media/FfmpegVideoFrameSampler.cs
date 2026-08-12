using System.Diagnostics;
using System.Globalization;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Media;

/// <summary>Uses optional local ffmpeg to create a strictly bounded, caller-owned frame sample.</summary>
public sealed class FfmpegVideoFrameSampler : IVideoFrameSampler
{
    private const long MaximumEncodedFrameBytes = 16L * 1024 * 1024;
    private readonly IConfigurationService _configurationService;
    private readonly string _temporaryRoot;
    private readonly IExternalToolLocator _toolLocator;
    private readonly IMediaProcessRunner _processRunner;

    /// <summary>Initializes the optional local frame sampler.</summary>
    public FfmpegVideoFrameSampler(
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
    public string Name => "ffmpeg-even-samples";

    /// <inheritdoc />
    public string Version => "2.2.0";

    /// <inheritdoc />
    public async Task<MediaCapability> DetectCapabilityAsync(CancellationToken cancellationToken)
    {
        var location = LocateExecutable();
        if (!location.IsAvailable || string.IsNullOrWhiteSpace(location.ExecutablePath))
        {
            return new MediaCapability(
                MediaCapabilityKind.VideoFrameSampling,
                false,
                Name,
                null,
                "ffmpeg was not found. Video metadata and ordinary Search can continue without frame analysis.");
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
                MediaCapabilityKind.VideoFrameSampling,
                result.ExitCode == 0,
                Name,
                version,
                result.ExitCode == 0
                    ? "Bounded representative-frame extraction is available locally."
                    : "ffmpeg was found but did not start successfully.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return new MediaCapability(MediaCapabilityKind.VideoFrameSampling, false, Name, null, "ffmpeg capability detection timed out.");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new MediaCapability(MediaCapabilityKind.VideoFrameSampling, false, Name, null, "ffmpeg could not be started safely.");
        }
    }

    /// <inheritdoc />
    public async Task<VideoFrameSampleBatch> SampleAsync(
        FileEntry file,
        MediaMetadata metadata,
        MediaIntelligenceSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(settings);
        var started = Stopwatch.StartNew();
        if (metadata.Kind != MediaKind.Video || metadata.Duration is null || metadata.Duration <= TimeSpan.Zero)
        {
            return Result(MediaExtractionStatus.Skipped, null, [], started.Elapsed, "A finite video duration is required for bounded sampling.");
        }

        if (metadata.Duration > TimeSpan.FromMinutes(settings.MaximumVideoDurationMinutes))
        {
            return Result(MediaExtractionStatus.LimitExceeded, null, [], started.Elapsed, "The video exceeds the configured duration limit for frame analysis.");
        }

        var capability = await DetectCapabilityAsync(cancellationToken).ConfigureAwait(false);
        if (!capability.IsAvailable)
        {
            return Result(MediaExtractionStatus.Unavailable, null, [], started.Elapsed, capability.Message);
        }

        var executable = LocateExecutable().ExecutablePath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return Result(MediaExtractionStatus.Unavailable, null, [], started.Elapsed, "ffmpeg became unavailable before frame extraction began.");
        }

        var workspace = CreateWorkspace();
        var samples = new List<VideoFrameSample>();
        var warnings = new List<string>();
        try
        {
            foreach (var position in CalculateSamplePositions(metadata.Duration.Value, settings.MaximumVideoFrames))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outputPath = Path.Combine(workspace, $"frame-{samples.Count + 1:D2}.png");
                var process = await _processRunner.ExecuteAsync(
                    executable,
                    [
                        "-nostdin", "-hide_banner", "-loglevel", "error", "-ss",
                        position.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                        "-i", file.FullPath, "-frames:v", "1",
                        "-vf", "scale=1280:-2:force_original_aspect_ratio=decrease",
                        "-y", outputPath,
                    ],
                    2_048,
                    8_192,
                    TimeSpan.FromSeconds(settings.ProviderTimeoutSeconds),
                    cancellationToken).ConfigureAwait(false);
                if (process.ExitCode != 0 || !File.Exists(outputPath))
                {
                    warnings.Add($"Representative frame {samples.Count + 1} could not be extracted.");
                    continue;
                }

                var length = new FileInfo(outputPath).Length;
                if (length <= 0 || length > MaximumEncodedFrameBytes)
                {
                    File.Delete(outputPath);
                    warnings.Add($"Representative frame {samples.Count + 1} exceeded the encoded-frame bound.");
                    continue;
                }

                samples.Add(new VideoFrameSample(outputPath, position, length));
            }

            var status = samples.Count == 0
                ? MediaExtractionStatus.Failed
                : warnings.Count == 0 ? MediaExtractionStatus.Completed : MediaExtractionStatus.PartiallyCompleted;
            return new VideoFrameSampleBatch(
                status,
                workspace,
                samples.AsReadOnly(),
                Name,
                capability.ProviderVersion ?? Version,
                started.Elapsed,
                warnings.AsReadOnly(),
                samples.Count == 0
                    ? "No representative frames could be produced safely."
                    : $"Produced {samples.Count} bounded, evenly spaced representative frame(s).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DeleteWorkspace(workspace);
            throw;
        }
        catch (TimeoutException)
        {
            DeleteWorkspace(workspace);
            return Result(MediaExtractionStatus.Failed, null, [], started.Elapsed, "Representative-frame extraction timed out and its temporary workspace was removed.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            DeleteWorkspace(workspace);
            return Result(MediaExtractionStatus.Failed, null, [], started.Elapsed, "Representative-frame extraction failed safely for this file.");
        }
    }

    /// <inheritdoc />
    public void DeleteWorkspace(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(workspacePath);
        var root = Path.GetFullPath(_temporaryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The frame workspace is outside OmniSorSe's managed temporary root.");
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }

    /// <summary>Returns exact interior sample positions, capped before allocation or process launch.</summary>
    internal static IReadOnlyList<TimeSpan> CalculateSamplePositions(TimeSpan duration, int maximumFrames)
    {
        if (duration <= TimeSpan.Zero || maximumFrames <= 0)
        {
            return [];
        }

        // At most one sample per five minutes, with one sample for shorter clips and a hard configured cap.
        var count = Math.Min(maximumFrames, Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes / 5d)));
        return Enumerable.Range(1, count)
            .Select(index => TimeSpan.FromTicks(duration.Ticks * index / (count + 1)))
            .ToArray();
    }

    private string CreateWorkspace()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var path = Path.Combine(_temporaryRoot, $"frames-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private ExternalToolLocation LocateExecutable() =>
        _toolLocator.Locate("ffmpeg", _configurationService.Current.MediaIntelligence.FfmpegExecutablePath);

    private VideoFrameSampleBatch Result(
        MediaExtractionStatus status,
        string? workspace,
        IReadOnlyList<VideoFrameSample> frames,
        TimeSpan duration,
        string message) =>
        new(status, workspace, frames, Name, Version, duration, [], message);

    private static string? Bound(string? value, int maximumCharacters) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= maximumCharacters ? value : value[..maximumCharacters];
}
