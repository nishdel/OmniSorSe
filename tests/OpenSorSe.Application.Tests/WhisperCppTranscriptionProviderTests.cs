using System.Text.Json;
using OpenSorSe.Application.Media;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Tests;

/// <summary>Validates the optional, user-managed whisper.cpp process boundary.</summary>
public sealed class WhisperCppTranscriptionProviderTests
{
    /// <summary>A missing model is a truthful optional capability state and never launches a process.</summary>
    [Fact]
    public async Task MissingModelIsNotConfiguredWithoutLaunchingRuntime()
    {
        using var fixture = new Fixture();
        var runner = new WhisperRunner();
        using var provider = fixture.CreateProvider(runner, modelPath: Path.Combine(fixture.Root, "missing.bin"));

        var capability = await provider.DetectCapabilityAsync(CancellationToken.None);

        Assert.False(capability.IsAvailable);
        Assert.Equal(MediaCapabilityState.NotConfigured, capability.State);
        Assert.Contains("not found", capability.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Calls);
    }

    /// <summary>A model-like path that is too small is rejected before process execution.</summary>
    [Fact]
    public async Task InvalidModelIsRejectedWithoutLaunchingRuntime()
    {
        using var fixture = new Fixture();
        var invalid = Path.Combine(fixture.Root, "invalid-model.bin");
        File.WriteAllBytes(invalid, [1, 2, 3]);
        var runner = new WhisperRunner();
        using var provider = fixture.CreateProvider(runner, invalid);

        var capability = await provider.DetectCapabilityAsync(CancellationToken.None);

        Assert.False(capability.IsAvailable);
        Assert.Equal(MediaCapabilityState.InvalidConfiguration, capability.State);
        Assert.Contains("too small", capability.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Calls);
    }

    /// <summary>A missing executable is an unavailable optional capability and never launches work.</summary>
    [Fact]
    public async Task MissingRuntimeIsUnavailableWithoutLaunchingTranscription()
    {
        using var fixture = new Fixture();
        var runner = new WhisperRunner();
        using var provider = new WhisperCppTranscriptionProvider(
            fixture.Configuration,
            new MissingToolLocator(),
            runner,
            fixture.TemporaryRoot);

        var capability = await provider.DetectCapabilityAsync(CancellationToken.None);

        Assert.False(capability.IsAvailable);
        Assert.Equal(MediaCapabilityState.Unavailable, capability.State);
        Assert.Contains("not found", capability.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Calls);
    }

    /// <summary>Duration controls reject expensive input before runtime or temporary-workspace use.</summary>
    [Fact]
    public async Task DurationLimitSkipsRuntimeAndLeavesNoWorkspace()
    {
        using var fixture = new Fixture();
        var runner = new WhisperRunner();
        using var provider = fixture.CreateProvider(runner);

        var result = await provider.TranscribeAsync(
            fixture.File,
            new MediaMetadata { Kind = MediaKind.Audio, Duration = TimeSpan.FromHours(3) },
            fixture.Settings,
            CancellationToken.None);

        Assert.Equal(MediaExtractionStatus.LimitExceeded, result.Status);
        Assert.Empty(runner.Calls);
        Assert.Empty(Directory.EnumerateDirectories(fixture.TemporaryRoot));
    }

    /// <summary>Bounded whisper.cpp JSON produces searchable text and sane millisecond segments.</summary>
    [Fact]
    public async Task SuccessfulLocalTranscriptionParsesBoundedTimestampedSegmentsAndCleansWorkspace()
    {
        using var fixture = new Fixture();
        var runner = new WhisperRunner();
        using var provider = fixture.CreateProvider(runner);

        var result = await provider.TranscribeAsync(
            fixture.File,
            new MediaMetadata { Kind = MediaKind.Audio, Duration = TimeSpan.FromSeconds(8) },
            fixture.Settings,
            CancellationToken.None);

        Assert.Equal(MediaExtractionStatus.Completed, result.Status);
        Assert.Equal("Grafana monitoring stack Raspberry Pi", result.Text);
        var segment = Assert.Single(result.Segments);
        Assert.Equal(TimeSpan.FromMilliseconds(250), segment.Start);
        Assert.Equal(TimeSpan.FromMilliseconds(2750), segment.End);
        Assert.Contains(runner.Calls, call => call.Contains("-oj"));
        Assert.Empty(Directory.EnumerateDirectories(fixture.TemporaryRoot));
    }

    /// <summary>Capability inspection reports real in-process activity without inventing a model runtime state.</summary>
    [Fact]
    public async Task CapabilityReportsProcessingWhileOneBoundedJobIsActive()
    {
        using var fixture = new Fixture();
        var runner = new WhisperRunner
        {
            TranscriptionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ReleaseTranscription = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        using var provider = fixture.CreateProvider(runner);
        var transcription = provider.TranscribeAsync(
            fixture.File,
            new MediaMetadata { Kind = MediaKind.Audio, Duration = TimeSpan.FromSeconds(8) },
            fixture.Settings,
            CancellationToken.None);
        await runner.TranscriptionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        MediaCapability capability;
        try
        {
            capability = await provider.DetectCapabilityAsync(CancellationToken.None);
        }
        finally
        {
            runner.ReleaseTranscription.TrySetResult(true);
        }

        Assert.True(capability.IsAvailable);
        Assert.Equal(MediaCapabilityState.Processing, capability.State);
        Assert.Equal(MediaExtractionStatus.Completed, (await transcription).Status);
    }

    /// <summary>Video and unsupported audio containers use safe local ffmpeg preparation before the shared provider.</summary>
    [Fact]
    public async Task VideoUsesOwnedTemporaryAudioAndNeverWritesBesideSource()
    {
        using var fixture = new Fixture("clip.mp4", MediaKind.Video);
        var runner = new WhisperRunner();
        using var provider = fixture.CreateProvider(runner);

        var result = await provider.TranscribeAsync(
            fixture.File,
            new MediaMetadata { Kind = MediaKind.Video, Duration = TimeSpan.FromSeconds(8) },
            fixture.Settings,
            CancellationToken.None);

        Assert.Equal(MediaExtractionStatus.Completed, result.Status);
        var ffmpegCall = Assert.Single(runner.Calls, call => call.Contains("pcm_s16le"));
        Assert.Contains("-nostdin", ffmpegCall);
        Assert.DoesNotContain(ffmpegCall, argument => argument.StartsWith(fixture.SourceDirectory, StringComparison.OrdinalIgnoreCase) && argument.EndsWith(".wav", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Directory.EnumerateDirectories(fixture.TemporaryRoot));
    }

    /// <summary>Cancellation remains cooperative and removes the owned temporary workspace.</summary>
    [Fact]
    public async Task CancellationPropagatesAndCleansWorkspace()
    {
        using var fixture = new Fixture();
        using var source = new CancellationTokenSource();
        var runner = new WhisperRunner { CancelTranscription = source };
        using var provider = fixture.CreateProvider(runner);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.TranscribeAsync(
            fixture.File,
            new MediaMetadata { Kind = MediaKind.Audio, Duration = TimeSpan.FromSeconds(8) },
            fixture.Settings,
            source.Token));

        Assert.Empty(Directory.EnumerateDirectories(fixture.TemporaryRoot));
    }

    /// <summary>Provider timeout is isolated as a failed item and removes the owned workspace.</summary>
    [Fact]
    public async Task TimeoutFailsSafelyAndCleansWorkspace()
    {
        using var fixture = new Fixture();
        var runner = new WhisperRunner { ThrowTimeout = true };
        using var provider = fixture.CreateProvider(runner);

        var result = await provider.TranscribeAsync(
            fixture.File,
            new MediaMetadata { Kind = MediaKind.Audio, Duration = TimeSpan.FromSeconds(8) },
            fixture.Settings,
            CancellationToken.None);

        Assert.Equal(MediaExtractionStatus.Failed, result.Status);
        Assert.Contains("timed out", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateDirectories(fixture.TemporaryRoot));
    }

    /// <summary>Malformed runtime JSON is rejected without publishing or caching partial transcript text.</summary>
    [Fact]
    public async Task MalformedJsonFailsWithoutPartialTranscript()
    {
        using var fixture = new Fixture();
        var runner = new WhisperRunner { Json = "{not-json" };
        using var provider = fixture.CreateProvider(runner);

        var result = await provider.TranscribeAsync(
            fixture.File,
            new MediaMetadata { Kind = MediaKind.Audio, Duration = TimeSpan.FromSeconds(8) },
            fixture.Settings,
            CancellationToken.None);

        Assert.Equal(MediaExtractionStatus.Failed, result.Status);
        Assert.Null(result.Text);
        Assert.Empty(result.Segments);
        Assert.Empty(Directory.EnumerateDirectories(fixture.TemporaryRoot));
    }

    /// <summary>The combined transcript never exceeds its configured bound including separators.</summary>
    [Fact]
    public async Task ProviderIncludesSeparatorsInsideTranscriptBound()
    {
        using var fixture = new Fixture();
        var runner = new WhisperRunner
        {
            Json = JsonSerializer.Serialize(new
            {
                transcription = new[]
                {
                    new { offsets = new { from = 0, to = 100 }, text = new string('a', 3_000) },
                    new { offsets = new { from = 100, to = 200 }, text = new string('b', 2_000) },
                },
            }),
        };
        using var provider = fixture.CreateProvider(runner);
        var settings = new MediaIntelligenceSettings
        {
            WhisperExecutablePath = fixture.RuntimePath,
            WhisperModelPath = fixture.ModelPath,
            FfmpegExecutablePath = fixture.RuntimePath,
            AudioTranscriptionEnabled = true,
            VideoTranscriptionEnabled = true,
            MaximumTranscriptCharacters = 4_096,
            TranscriptionTimeoutSeconds = 60,
        };

        var result = await provider.TranscribeAsync(
            fixture.File,
            new MediaMetadata { Kind = MediaKind.Audio, Duration = TimeSpan.FromSeconds(8) },
            settings,
            CancellationToken.None);

        Assert.Equal(MediaExtractionStatus.Completed, result.Status);
        Assert.Equal(4_096, result.Text!.Length);
        Assert.Equal(' ', result.Text[3_000]);
        Assert.All(result.Text[3_001..], character => Assert.Equal('b', character));
    }

    /// <summary>Runtime/model metadata participate in cache invalidation without hashing a large model repeatedly.</summary>
    [Fact]
    public void ModelMetadataChangesConfigurationFingerprint()
    {
        using var fixture = new Fixture();
        using var provider = fixture.CreateProvider(new WhisperRunner());
        var before = provider.Version;

        File.SetLastWriteTimeUtc(fixture.ModelPath, File.GetLastWriteTimeUtc(fixture.ModelPath).AddSeconds(2));
        var after = provider.Version;

        Assert.NotEqual(before, after);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(string fileName = "speech.wav", MediaKind kind = MediaKind.Audio)
        {
            Root = Path.Combine(Path.GetTempPath(), "OpenSorSe-whisper-tests", Guid.NewGuid().ToString("N"));
            SourceDirectory = Path.Combine(Root, "source");
            TemporaryRoot = Path.Combine(Root, "owned-temp");
            Directory.CreateDirectory(SourceDirectory);
            Directory.CreateDirectory(TemporaryRoot);
            var sourcePath = Path.Combine(SourceDirectory, fileName);
            System.IO.File.WriteAllBytes(sourcePath, [1, 2, 3, 4]);
            ModelPath = Path.Combine(Root, "ggml-model.bin");
            using (var stream = System.IO.File.Create(ModelPath))
            {
                stream.SetLength(1_048_576);
            }

            RuntimePath = Path.Combine(Root, OperatingSystem.IsWindows() ? "whisper-cli.exe" : "whisper-cli");
            System.IO.File.WriteAllBytes(RuntimePath, [1]);
            Settings = new MediaIntelligenceSettings
            {
                WhisperExecutablePath = RuntimePath,
                WhisperModelPath = ModelPath,
                FfmpegExecutablePath = RuntimePath,
                AudioTranscriptionEnabled = true,
                VideoTranscriptionEnabled = true,
                MaximumTranscriptCharacters = 128,
                TranscriptionTimeoutSeconds = 60,
            };
            Configuration = new TestConfiguration(new ApplicationSettings { MediaIntelligence = Settings });
            File = new FileEntry(
                sourcePath,
                new FileMetadata(fileName, Path.GetExtension(fileName), 4, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, FileAttributes.Normal));
            Kind = kind;
        }

        public string Root { get; }
        public string SourceDirectory { get; }
        public string TemporaryRoot { get; }
        public string ModelPath { get; }
        public string RuntimePath { get; }
        public MediaIntelligenceSettings Settings { get; }
        public TestConfiguration Configuration { get; }
        public FileEntry File { get; }
        public MediaKind Kind { get; }

        public WhisperCppTranscriptionProvider CreateProvider(WhisperRunner runner, string? modelPath = null)
        {
            if (modelPath is not null)
            {
                Configuration.Set(new ApplicationSettings
                {
                    MediaIntelligence = new MediaIntelligenceSettings
                    {
                        WhisperExecutablePath = RuntimePath,
                        WhisperModelPath = modelPath,
                        FfmpegExecutablePath = RuntimePath,
                        AudioTranscriptionEnabled = true,
                        VideoTranscriptionEnabled = true,
                        MaximumTranscriptCharacters = 128,
                        TranscriptionTimeoutSeconds = 60,
                    },
                });
            }

            return new WhisperCppTranscriptionProvider(Configuration, new FixedToolLocator(RuntimePath), runner, TemporaryRoot);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class TestConfiguration(ApplicationSettings settings) : IConfigurationService
    {
        public ApplicationSettings Current { get; private set; } = settings;
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }

        public void Set(ApplicationSettings settings) => Current = settings;
    }

    private sealed class FixedToolLocator(string path) : IExternalToolLocator
    {
        public ExternalToolLocation Locate(string commandName, string? configuredPath = null) =>
            new(true, configuredPath ?? path, "Synthetic user-managed tool");
    }

    private sealed class MissingToolLocator : IExternalToolLocator
    {
        public ExternalToolLocation Locate(string commandName, string? configuredPath = null) =>
            new(false, null, "Synthetic tool is unavailable");
    }

    private sealed class WhisperRunner : IMediaProcessRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public CancellationTokenSource? CancelTranscription { get; init; }
        public bool ThrowTimeout { get; init; }
        public TaskCompletionSource<bool>? TranscriptionStarted { get; init; }
        public TaskCompletionSource<bool>? ReleaseTranscription { get; init; }
        public string Json { get; init; } =
            "{\"transcription\":[{\"offsets\":{\"from\":250,\"to\":2750},\"text\":\" Grafana monitoring stack Raspberry Pi \"}]}";

        public Task<MediaProcessResult> ExecuteAsync(
            string executable,
            IReadOnlyList<string> arguments,
            int maximumOutputCharacters,
            int maximumErrorCharacters,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(arguments.ToArray());
            if (arguments.Contains("--help"))
            {
                return Task.FromResult(new MediaProcessResult(0, "whisper.cpp help", string.Empty, false, false));
            }

            if (arguments.Contains("pcm_s16le"))
            {
                File.WriteAllBytes(arguments[^1], new byte[64]);
                return Task.FromResult(new MediaProcessResult(0, string.Empty, string.Empty, false, false));
            }

            if (CancelTranscription is not null)
            {
                CancelTranscription.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (ThrowTimeout)
            {
                throw new TimeoutException("Controlled timeout");
            }

            if (TranscriptionStarted is not null && ReleaseTranscription is not null)
            {
                TranscriptionStarted.TrySetResult(true);
                return CompleteAfterReleaseAsync(arguments, ReleaseTranscription.Task, cancellationToken);
            }

            var outputBase = arguments[Array.IndexOf(arguments.ToArray(), "-of") + 1];
            File.WriteAllText(outputBase + ".json", Json);
            return Task.FromResult(new MediaProcessResult(0, string.Empty, string.Empty, false, false));
        }

        private async Task<MediaProcessResult> CompleteAfterReleaseAsync(
            IReadOnlyList<string> arguments,
            Task release,
            CancellationToken cancellationToken)
        {
            await release.WaitAsync(cancellationToken);
            var outputBase = arguments[Array.IndexOf(arguments.ToArray(), "-of") + 1];
            File.WriteAllText(outputBase + ".json", Json);
            return new MediaProcessResult(0, string.Empty, string.Empty, false, false);
        }
    }
}
