using OpenSorSe.Application.Content;
using OpenSorSe.Application.Media;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;
using OpenSorSe.Scanner.Models;
using SkiaSharp;

namespace OpenSorSe.Application.Tests;

/// <summary>Validates bounded local media extraction, optional-provider fallback, and Search evidence.</summary>
public sealed class MediaIntelligenceTests
{
    /// <summary>Verifies the real process boundary classifies its own deadline separately from user cancellation.</summary>
    [Fact]
    public async Task ExternalProcessRunner_TimeoutIsNotReportedAsCallerCancellation()
    {
        var (executable, arguments) = BlockingProcess();
        var runner = new ExternalMediaProcessRunner();

        await Assert.ThrowsAsync<TimeoutException>(() => runner.ExecuteAsync(
            executable,
            arguments,
            1_024,
            1_024,
            TimeSpan.FromMilliseconds(150),
            CancellationToken.None));
    }

    /// <summary>Verifies the real process boundary preserves caller cancellation after process-tree cleanup.</summary>
    [Fact]
    public async Task ExternalProcessRunner_CallerCancellationRemainsCancellation()
    {
        var (executable, arguments) = BlockingProcess();
        var runner = new ExternalMediaProcessRunner();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.ExecuteAsync(
            executable,
            arguments,
            1_024,
            1_024,
            TimeSpan.FromSeconds(10),
            cancellation.Token));

        Assert.IsNotType<TimeoutException>(exception);
    }

    /// <summary>Verifies only explicitly declared extensions enter media processing.</summary>
    [Theory]
    [InlineData("photo.JPG", MediaKind.Image)]
    [InlineData("scan.tiff", MediaKind.Image)]
    [InlineData("voice.flac", MediaKind.Audio)]
    [InlineData("memo.m4a", MediaKind.Audio)]
    [InlineData("clip.MKV", MediaKind.Video)]
    [InlineData("movie.mov", MediaKind.Video)]
    [InlineData("document.pdf", MediaKind.None)]
    public void FormatRegistry_ClassifiesOnlyDeclaredFormats(string fileName, MediaKind expected)
    {
        Assert.Equal(expected, MediaFormatRegistry.Classify(fileName));
    }

    /// <summary>Verifies local PNG headers yield dimensions without requiring EXIF.</summary>
    [Fact]
    public async Task ImageProvider_ExtractsPngDimensionsWithoutExif()
    {
        using var fixture = new TempFixture("image.png", PngHeader(1_920, 1_080));
        var provider = new ImageMediaMetadataProvider();

        var result = await provider.ExtractAsync(fixture.File, new MediaIntelligenceSettings(), default);

        Assert.Equal(MediaExtractionStatus.Completed, result.Status);
        Assert.Equal(1_920, result.Metadata?.Width);
        Assert.Equal(1_080, result.Metadata?.Height);
        Assert.Null(result.Metadata?.DeviceModel);
        Assert.Null(result.Metadata?.Latitude);
    }

    /// <summary>Verifies a corrupt image fails only its own provider operation.</summary>
    [Fact]
    public async Task ImageProvider_MalformedHeaderFailsPerFileWithoutThrowing()
    {
        using var fixture = new TempFixture("broken.jpg", [0xff, 0xd8, 0xff, 0xe1, 0, 20, 1, 2, 3]);
        var provider = new ImageMediaMetadataProvider();

        var result = await provider.ExtractAsync(fixture.File, new MediaIntelligenceSettings(), default);

        Assert.Equal(MediaExtractionStatus.Failed, result.Status);
        Assert.Null(result.Metadata);
        Assert.Contains("malformed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies bounded TIFF EXIF orientation, device, and sensitive GPS fields are parsed deterministically.</summary>
    [Fact]
    public async Task ImageProvider_ExtractsOrientationDeviceAndGpsFromExif()
    {
        using var fixture = new TempFixture("camera.tiff", ExifTiff());
        var result = await new ImageMediaMetadataProvider()
            .ExtractAsync(fixture.File, new MediaIntelligenceSettings(), default);

        Assert.Equal(MediaExtractionStatus.Completed, result.Status);
        Assert.Equal(4_032, result.Metadata?.Width);
        Assert.Equal(3_024, result.Metadata?.Height);
        Assert.Equal(6, result.Metadata?.Orientation);
        Assert.Equal("Samsung", result.Metadata?.DeviceMake);
        Assert.Equal("SM-G996B", result.Metadata?.DeviceModel);
        Assert.Equal(45.5, result.Metadata!.Latitude!.Value, precision: 6);
        Assert.Equal(7.25, result.Metadata.Longitude!.Value, precision: 6);
    }

    /// <summary>Verifies a valid JPEG APP1 segment carries the same bounded EXIF evidence as TIFF.</summary>
    [Fact]
    public async Task ImageProvider_ExtractsRealJpegApp1ExifLocally()
    {
        using var fixture = new TempFixture("camera.jpg", ExifJpeg());

        var result = await new ImageMediaMetadataProvider()
            .ExtractAsync(fixture.File, new MediaIntelligenceSettings(), default);

        Assert.Equal(MediaExtractionStatus.Completed, result.Status);
        Assert.Equal(4_032, result.Metadata?.Width);
        Assert.Equal(3_024, result.Metadata?.Height);
        Assert.Equal(6, result.Metadata?.Orientation);
        Assert.Equal("Samsung", result.Metadata?.DeviceMake);
        Assert.Equal("SM-G996B", result.Metadata?.DeviceModel);
        Assert.Equal(45.5, result.Metadata!.Latitude!.Value, precision: 6);
        Assert.Equal(7.25, result.Metadata.Longitude!.Value, precision: 6);
    }

    /// <summary>Verifies a disappeared image becomes one isolated actionable failure.</summary>
    [Fact]
    public async Task ImageProvider_FileDisappearingBeforeReadFailsSafely()
    {
        var fixture = new TempFixture("gone.png", PngHeader(10, 10));
        var file = fixture.File;
        fixture.Dispose();

        var result = await new ImageMediaMetadataProvider()
            .ExtractAsync(file, new MediaIntelligenceSettings(), default);

        Assert.Equal(MediaExtractionStatus.Failed, result.Status);
        Assert.Contains("disappeared", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies ffprobe JSON maps into bounded provider-neutral audio metadata.</summary>
    [Fact]
    public async Task FfprobeProvider_UsesArgumentListAndMapsBoundedAudioMetadata()
    {
        using var fixture = new TempFixture("recording.mp3", [1]);
        var runner = new RecordingProcessRunner(
            "ffprobe version synthetic",
            """
            {"streams":[{"codec_type":"audio","codec_name":"flac","sample_rate":"48000","channels":2}],
             "format":{"format_name":"mp3","duration":"65.5","bit_rate":"192000","tags":{"title":"Network notes","artist":"Synthetic"}}}
            """);
        var provider = new FfprobeMediaMetadataProvider(
            new Configuration(),
            new FixedToolLocator("C:\\Tools\\ffprobe.exe"),
            runner);

        var result = await provider.ExtractAsync(fixture.File, new MediaIntelligenceSettings(), default);

        Assert.Equal(MediaExtractionStatus.Completed, result.Status);
        Assert.Equal(TimeSpan.FromSeconds(65.5), result.Metadata?.Duration);
        Assert.Equal("Network notes", result.Metadata?.Title);
        Assert.Equal(48_000, result.Metadata?.SampleRate);
        Assert.Contains(runner.Calls[^1], argument => argument == "--");
        Assert.Equal(fixture.Path, runner.Calls[^1][^1]);
    }

    /// <summary>Verifies malformed external metadata is rejected without escaping.</summary>
    [Fact]
    public async Task FfprobeProvider_MalformedJsonFailsSafely()
    {
        using var fixture = new TempFixture("recording.mp3", [1]);
        var provider = new FfprobeMediaMetadataProvider(
            new Configuration(),
            new FixedToolLocator("ffprobe"),
            new RecordingProcessRunner("ffprobe version synthetic", "{not-json"));

        var result = await provider.ExtractAsync(fixture.File, new MediaIntelligenceSettings(), default);

        Assert.Equal(MediaExtractionStatus.Failed, result.Status);
        Assert.Null(result.Metadata);
    }

    /// <summary>Verifies a provider timeout is an isolated file failure rather than caller cancellation.</summary>
    [Fact]
    public async Task FfprobeProvider_TimeoutFailsFileWithoutCancellingCaller()
    {
        using var fixture = new TempFixture("recording.mp3", [1]);
        var provider = new FfprobeMediaMetadataProvider(
            new Configuration(),
            new FixedToolLocator("ffprobe"),
            new TimeoutAfterVersionProcessRunner("ffprobe version synthetic"));

        var result = await provider.ExtractAsync(fixture.File, new MediaIntelligenceSettings(), default);

        Assert.Equal(MediaExtractionStatus.Failed, result.Status);
        Assert.Contains("timed out", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies bounded video streams map resolution, rates, codecs, device, and capture time.</summary>
    [Fact]
    public async Task FfprobeProvider_MapsVideoMetadataWithoutDecodingFrames()
    {
        using var fixture = new TempFixture("camera.mp4", [1]);
        var provider = new FfprobeMediaMetadataProvider(
            new Configuration(),
            new FixedToolLocator("ffprobe"),
            new RecordingProcessRunner(
                "ffprobe version synthetic",
                """
                {"streams":[
                  {"codec_type":"video","codec_name":"h264","width":3840,"height":2160,"avg_frame_rate":"30000/1001"},
                  {"codec_type":"audio","codec_name":"aac","sample_rate":"48000","channels":2}],
                 "format":{"format_name":"mov,mp4","duration":"30.25","tags":{
                   "creation_time":"2026-07-12T10:15:30Z",
                   "com.apple.quicktime.make":"Samsung",
                   "com.apple.quicktime.model":"SM-G996B"}}}
                """));

        var result = await provider.ExtractAsync(fixture.File, new MediaIntelligenceSettings(), default);

        Assert.Equal(MediaExtractionStatus.Completed, result.Status);
        Assert.Equal(3_840, result.Metadata?.Width);
        Assert.Equal(2_160, result.Metadata?.Height);
        Assert.Equal(30000d / 1001d, result.Metadata!.FrameRate!.Value, precision: 6);
        Assert.Equal("h264", result.Metadata?.VideoCodec);
        Assert.Equal("aac", result.Metadata?.AudioCodec);
        Assert.Equal("Samsung", result.Metadata?.DeviceMake);
        Assert.Equal(DateTimeOffset.Parse("2026-07-12T10:15:30Z"), result.Metadata?.CapturedAtUtc);
    }

    /// <summary>Verifies long videos use capped deterministic interior samples and owned cleanup.</summary>
    [Fact]
    public async Task VideoSampler_UsesDeterministicCappedInteriorFramesAndCleansWorkspace()
    {
        using var fixture = new TempFixture("long video.mp4", [1]);
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OpenSorSe-media-tests", Guid.NewGuid().ToString("N"));
        var runner = new RecordingProcessRunner("ffmpeg version synthetic", string.Empty, createFrameOutput: true);
        var sampler = new FfmpegVideoFrameSampler(
            new Configuration(),
            new FixedToolLocator("ffmpeg"),
            runner,
            root);
        var metadata = new MediaMetadata { Kind = MediaKind.Video, Duration = TimeSpan.FromHours(2) };

        var result = await sampler.SampleAsync(fixture.File, metadata, new MediaIntelligenceSettings(), default);

        Assert.Equal(MediaExtractionStatus.Completed, result.Status);
        Assert.Equal(8, result.Frames.Count);
        Assert.Equal(TimeSpan.FromMinutes(120d / 9d), result.Frames[0].Position);
        Assert.Equal(TimeSpan.FromMinutes(120d * 8d / 9d), result.Frames[^1].Position);
        Assert.All(runner.Calls.Skip(1), call => Assert.Contains(fixture.Path, call));
        Assert.NotNull(result.WorkspacePath);
        sampler.DeleteWorkspace(result.WorkspacePath!);
        Assert.False(Directory.Exists(result.WorkspacePath));
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Verifies duration limits prevent external frame-provider work.</summary>
    [Fact]
    public async Task VideoSampler_DurationLimitAvoidsProviderExecution()
    {
        using var fixture = new TempFixture("too-long.mp4", [1]);
        var runner = new RecordingProcessRunner("unused", "unused", createFrameOutput: true);
        var sampler = new FfmpegVideoFrameSampler(
            new Configuration(),
            new FixedToolLocator("ffmpeg"),
            runner,
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OpenSorSe-media-tests", Guid.NewGuid().ToString("N")));
        var metadata = new MediaMetadata { Kind = MediaKind.Video, Duration = TimeSpan.FromHours(5) };

        var result = await sampler.SampleAsync(fixture.File, metadata, new MediaIntelligenceSettings(), default);

        Assert.Equal(MediaExtractionStatus.LimitExceeded, result.Status);
        Assert.Empty(runner.Calls);
    }

    /// <summary>Verifies a frame-provider timeout removes its owned workspace and remains retryable.</summary>
    [Fact]
    public async Task VideoSampler_TimeoutFailsSafelyAndCleansWorkspace()
    {
        using var fixture = new TempFixture("timeout.mp4", [1]);
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OpenSorSe-media-tests", Guid.NewGuid().ToString("N"));
        var sampler = new FfmpegVideoFrameSampler(
            new Configuration(),
            new FixedToolLocator("ffmpeg"),
            new TimeoutAfterVersionProcessRunner("ffmpeg version synthetic"),
            root);

        var result = await sampler.SampleAsync(
            fixture.File,
            new MediaMetadata { Kind = MediaKind.Video, Duration = TimeSpan.FromMinutes(1) },
            new MediaIntelligenceSettings(),
            default);

        Assert.Equal(MediaExtractionStatus.Failed, result.Status);
        Assert.Contains("timed out", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(!Directory.Exists(root) || Directory.GetDirectories(root).Length == 0);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Verifies disabling Media Intelligence prevents provider execution.</summary>
    [Fact]
    public async Task Coordinator_DisabledCapabilityDoesNotInvokeProviders()
    {
        using var fixture = new TempFixture("photo.png", PngHeader(10, 10));
        var metadata = new FakeMetadataProvider(MediaKind.Image);
        var service = CreateService(
            new ApplicationSettings { MediaIntelligence = new MediaIntelligenceSettings { Enabled = false } },
            metadata);

        var result = await service.ExtractAsync(fixture.File, null, allowOcr: true, default);

        Assert.Equal(MediaExtractionStatus.Skipped, result.Status);
        Assert.Equal(0, metadata.ExtractionCount);
    }

    /// <summary>Verifies unchanged compatible evidence avoids repeated expensive provider work.</summary>
    [Fact]
    public async Task Coordinator_UnchangedEvidenceIsReusedWithoutProviderWork()
    {
        using var fixture = new TempFixture("recording.mp3", [1]);
        var metadata = new FakeMetadataProvider(MediaKind.Audio, duration: TimeSpan.FromMinutes(5));
        var transcription = new FakeTranscriptionProvider("Raspberry Pi monitoring stack");
        var settings = new ApplicationSettings
        {
            MediaIntelligence = new MediaIntelligenceSettings { AudioTranscriptionEnabled = true },
        };
        var service = CreateService(settings, metadata, transcription: transcription);

        var first = await service.ExtractAsync(fixture.File, null, allowOcr: false, default);
        var second = await service.ExtractAsync(fixture.File, first.Evidence, allowOcr: false, default);

        Assert.NotNull(first.Evidence);
        Assert.True(second.Evidence?.CacheHit);
        Assert.Equal("Raspberry Pi monitoring stack", second.Evidence?.Transcript);
        Assert.Equal(1, metadata.ExtractionCount);
        Assert.Equal(1, transcription.CallCount);
    }

    /// <summary>Verifies an extraction-policy change invalidates expensive evidence deterministically.</summary>
    [Fact]
    public async Task Coordinator_ConfigurationChangeInvalidatesCachedEvidence()
    {
        using var fixture = new TempFixture("recording.mp3", [1]);
        var metadata = new FakeMetadataProvider(MediaKind.Audio, duration: TimeSpan.FromMinutes(5));
        var transcription = new FakeTranscriptionProvider("Raspberry Pi monitoring stack");
        var configuration = new Configuration(new ApplicationSettings
        {
            MediaIntelligence = new MediaIntelligenceSettings
            {
                AudioTranscriptionEnabled = true,
                MaximumTranscriptCharacters = 4_096,
            },
        });
        var service = new MediaIntelligenceService(
            configuration,
            [metadata],
            transcription,
            new UnavailableMediaVisualDescriptionProvider(),
            new EmptyFrameSampler(),
            new FakeOcrService(null));
        var first = await service.ExtractAsync(fixture.File, null, allowOcr: false, default);
        await configuration.SaveAsync(new ApplicationSettings
        {
            MediaIntelligence = new MediaIntelligenceSettings
            {
                AudioTranscriptionEnabled = true,
                MaximumTranscriptCharacters = 8_192,
            },
        }, default);

        var second = await service.ExtractAsync(fixture.File, first.Evidence, allowOcr: false, default);

        Assert.False(second.Evidence?.CacheHit);
        Assert.Equal(2, metadata.ExtractionCount);
        Assert.Equal(2, transcription.CallCount);
    }

    /// <summary>Verifies transcript text and timed segments obey deterministic bounds.</summary>
    [Fact]
    public async Task Coordinator_BoundsTranscriptAndTimestampSegments()
    {
        using var fixture = new TempFixture("recording.wav", [1]);
        var transcription = new FakeTranscriptionProvider(new string('a', 8_000));
        var settings = new ApplicationSettings
        {
            MediaIntelligence = new MediaIntelligenceSettings
            {
                AudioTranscriptionEnabled = true,
                MaximumTranscriptCharacters = 4_096,
            },
        };
        var service = CreateService(settings, new FakeMetadataProvider(MediaKind.Audio, TimeSpan.FromMinutes(1)), transcription: transcription);

        var result = await service.ExtractAsync(fixture.File, null, allowOcr: false, default);

        Assert.Equal(4_096, result.Evidence?.Transcript?.Length);
        Assert.Single(result.Evidence?.TranscriptSegments ?? []);
        Assert.Equal(TimeSpan.FromSeconds(3), result.Evidence?.TranscriptSegments[0].Start);
    }

    /// <summary>Verifies unknown audio duration fails closed instead of starting unbounded transcription.</summary>
    [Fact]
    public async Task Coordinator_UnknownAudioDurationSkipsTranscriptionByResourcePolicy()
    {
        using var fixture = new TempFixture("unknown-duration.wav", [1]);
        var transcription = new FakeTranscriptionProvider("should not run");
        var service = CreateService(
            new ApplicationSettings
            {
                MediaIntelligence = new MediaIntelligenceSettings { AudioTranscriptionEnabled = true },
            },
            new FakeMetadataProvider(MediaKind.Audio),
            transcription);

        var result = await service.ExtractAsync(fixture.File, null, allowOcr: false, default);

        Assert.Equal(MediaExtractionStatus.LimitExceeded, result.Status);
        Assert.Equal(0, transcription.CallCount);
        Assert.Null(result.Evidence?.Transcript);
    }

    /// <summary>Verifies an explicitly requested absent dependency remains retryable and is not cached as complete.</summary>
    [Fact]
    public async Task Coordinator_RequestedUnavailableTranscriptionReportsDependencyState()
    {
        using var fixture = new TempFixture("recording.wav", [1]);
        var metadata = new FakeMetadataProvider(MediaKind.Audio, TimeSpan.FromMinutes(1));
        var settings = new ApplicationSettings
        {
            MediaIntelligence = new MediaIntelligenceSettings { AudioTranscriptionEnabled = true },
        };
        var service = CreateService(settings, metadata);

        var first = await service.ExtractAsync(fixture.File, null, allowOcr: false, default);
        var second = await service.ExtractAsync(fixture.File, first.Evidence, allowOcr: false, default);

        Assert.Equal(MediaExtractionStatus.Unavailable, first.Status);
        Assert.NotNull(first.Evidence);
        Assert.False(second.Evidence?.CacheHit);
        Assert.Equal(2, metadata.ExtractionCount);
    }

    /// <summary>Verifies a provider-reported transcription failure is retryable and never cached as complete evidence.</summary>
    [Fact]
    public async Task Coordinator_FailedTranscriptionIsNotAcceptedOrCached()
    {
        using var fixture = new TempFixture("recording.wav", [1]);
        var metadata = new FakeMetadataProvider(MediaKind.Audio, TimeSpan.FromMinutes(1));
        var transcription = new FakeTranscriptionProvider(
            "untrusted partial transcript",
            MediaExtractionStatus.Failed);
        var service = CreateService(
            new ApplicationSettings
            {
                MediaIntelligence = new MediaIntelligenceSettings { AudioTranscriptionEnabled = true },
            },
            metadata,
            transcription);

        var first = await service.ExtractAsync(fixture.File, null, allowOcr: false, default);
        var second = await service.ExtractAsync(fixture.File, first.Evidence, allowOcr: false, default);

        Assert.Equal(MediaExtractionStatus.Failed, first.Status);
        Assert.Null(first.Evidence?.Transcript);
        Assert.False(second.Evidence?.CacheHit);
        Assert.Equal(2, transcription.CallCount);
    }

    /// <summary>Verifies existing OCR becomes image evidence for ordinary Search.</summary>
    [Fact]
    public async Task Coordinator_ImageOcrBecomesBoundedSearchEvidence()
    {
        using var fixture = new TempFixture("terminal.png", PngHeader(10, 10));
        var settings = new ApplicationSettings
        {
            MediaIntelligence = new MediaIntelligenceSettings { ImageOcrEnabled = true },
        };
        var service = CreateService(
            settings,
            new FakeMetadataProvider(MediaKind.Image),
            ocr: new FakeOcrService("docker compose up -d"));

        var result = await service.ExtractAsync(fixture.File, null, allowOcr: true, default);

        Assert.Equal("docker compose up -d", result.Evidence?.OcrText);
    }

    /// <summary>Verifies video-frame OCR is capped by shared policy and projected once.</summary>
    [Fact]
    public async Task Coordinator_VideoFrameOcrUsesOnlyConfiguredRepresentativeFrames()
    {
        using var fixture = new TempFixture("screen-recording.mp4", [1]);
        var settings = new ApplicationSettings
        {
            MediaIntelligence = new MediaIntelligenceSettings
            {
                VideoFrameAnalysisEnabled = true,
                MaximumVideoFrames = 4,
                MaximumVideoOcrFrames = 2,
            },
        };
        var ocr = new FakeOcrService("docker compose logs");
        var frames = new FakeFrameSampler(4);
        var service = CreateService(
            settings,
            new FakeMetadataProvider(MediaKind.Video, TimeSpan.FromMinutes(10)),
            ocr: ocr,
            frames: frames);

        var result = await service.ExtractAsync(fixture.File, null, allowOcr: true, default);

        Assert.Equal(2, ocr.CallCount);
        Assert.Equal(4, result.Evidence?.SampledFrameCount);
        Assert.Contains("docker compose logs", result.Evidence?.OcrText, StringComparison.Ordinal);
        Assert.True(frames.WorkspaceDeleted);
    }

    /// <summary>Verifies oversized files are rejected before any media provider is called.</summary>
    [Fact]
    public async Task Coordinator_OversizedInputIsRejectedBeforeProviderExecution()
    {
        using var fixture = new TempFixture("large.mp3", [1], reportedLength: 2 * 1024 * 1024);
        var metadata = new FakeMetadataProvider(MediaKind.Audio);
        var settings = new ApplicationSettings
        {
            MediaIntelligence = new MediaIntelligenceSettings { MaximumMediaFileSizeMiB = 1 },
        };

        var result = await CreateService(settings, metadata).ExtractAsync(fixture.File, null, false, default);

        Assert.Equal(MediaExtractionStatus.LimitExceeded, result.Status);
        Assert.Equal(0, metadata.ExtractionCount);
    }

    /// <summary>Verifies an unexpected provider fault is converted into a per-file failure.</summary>
    [Fact]
    public async Task Coordinator_ProviderCrashIsIsolatedAsFailedResult()
    {
        using var fixture = new TempFixture("broken.mp3", [1]);
        var metadata = new FakeMetadataProvider(MediaKind.Audio) { ThrowOnExtract = true };

        var result = await CreateService(new ApplicationSettings(), metadata)
            .ExtractAsync(fixture.File, null, false, default);

        Assert.Equal(MediaExtractionStatus.Failed, result.Status);
        Assert.Null(result.Evidence);
        Assert.Contains("continue", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies caller cancellation remains cooperative and is not reported as a provider fault.</summary>
    [Fact]
    public async Task Coordinator_CallerCancellationPropagates()
    {
        using var fixture = new TempFixture("cancel.mp3", [1]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateService(new ApplicationSettings(), new FakeMetadataProvider(MediaKind.Audio))
                .ExtractAsync(fixture.File, null, false, cancellation.Token));
    }

    /// <summary>Verifies an exact filename remains stronger than optional visual evidence.</summary>
    [Fact]
    public void Search_ExactFilenameOutranksWeakVisualDescription()
    {
        var ranker = new HybridSearchRanker(new FeatureHashingEmbeddingProvider(), new SearchSnippetFactory());
        var interpretation = new SearchInterpretation("mountain", "mountain", ["mountain"], []);
        var exact = Candidate("mountain.jpg", null);
        var described = Candidate("IMG_0001.jpg", Evidence(MediaKind.Image) with { VisualDescription = "mountain landscape" });

        var result = ranker.Rank(interpretation, [described, exact], 10, default);

        Assert.Equal("mountain.jpg", result[0].Document.FileName);
        Assert.Contains(result[1].Components, component => component.Kind == SearchRankingSignalKind.MediaVisualDescription);
    }

    /// <summary>Verifies transcript ranking exposes the exact contributing signal and bounded snippet.</summary>
    [Fact]
    public void Search_TranscriptProducesGroundedExplanationAndSnippet()
    {
        var ranker = new HybridSearchRanker(new FeatureHashingEmbeddingProvider(), new SearchSnippetFactory());
        var interpretation = new SearchInterpretation("Raspberry Pi", "Raspberry Pi", ["raspberry", "pi"], []);
        var evidence = Evidence(MediaKind.Audio) with { Transcript = "We moved monitoring onto the Raspberry Pi yesterday." };

        var result = Assert.Single(ranker.Rank(interpretation, [Candidate("memo.m4a", evidence)], 10, default));

        Assert.Contains(result.Components, component => component.Kind == SearchRankingSignalKind.MediaTranscript);
        Assert.Equal(SearchSnippetSource.MediaTranscript, result.Snippet?.Source);
        Assert.Contains("Raspberry Pi", result.Snippet?.Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies embedded device metadata produces a deterministic explanation.</summary>
    [Fact]
    public void Search_DeviceMetadataProducesDeterministicMediaReason()
    {
        var ranker = new HybridSearchRanker(new FeatureHashingEmbeddingProvider(), new SearchSnippetFactory());
        var interpretation = new SearchInterpretation("Samsung SM-G996B", "Samsung SM-G996B", ["samsung", "sm", "g996b"], []);
        var evidence = Evidence(MediaKind.Image) with
        {
            Metadata = Evidence(MediaKind.Image).Metadata with { DeviceMake = "Samsung", DeviceModel = "SM-G996B" },
        };

        var result = Assert.Single(ranker.Rank(interpretation, [Candidate("IMG_0042.jpg", evidence)], 10, default));

        Assert.Contains(result.Components, component => component.Kind == SearchRankingSignalKind.MediaMetadata);
        Assert.Equal("Media metadata", result.Snippet?.SourceLabel);
    }

    /// <summary>Verifies ordinary deterministic Search remains functional without media evidence.</summary>
    [Fact]
    public void Search_AllMediaDisabledStillRanksOrdinaryFilename()
    {
        var ranker = new HybridSearchRanker(new FeatureHashingEmbeddingProvider(), new SearchSnippetFactory());
        var interpretation = new SearchInterpretation("tax records", "tax records", ["tax", "records"], []);

        var result = ranker.Rank(interpretation, [Candidate("tax-records.pdf", null)], 10, default);

        Assert.Equal("tax-records.pdf", Assert.Single(result).Document.FileName);
    }

    /// <summary>Verifies precise GPS remains structured local data rather than ordinary searchable text.</summary>
    [Fact]
    public void MediaMetadataSearchTextOmitsPreciseGpsCoordinates()
    {
        var evidence = Evidence(MediaKind.Image) with
        {
            Metadata = Evidence(MediaKind.Image).Metadata with { Latitude = 45.923456, Longitude = 7.654321 },
        };

        var text = MediaEvidenceText.CreateMetadataText(evidence);
        var fields = MediaEvidenceText.CreateMetadataFields(evidence);

        Assert.DoesNotContain("45.923456", text, StringComparison.Ordinal);
        Assert.Contains(fields, field => field.Name == "GPS latitude" && field.Value == "45.923456");
    }

    /// <summary>Verifies absent optional providers report capability rather than making network requests.</summary>
    [Fact]
    public async Task UnconfiguredOptionalProvidersReportUnavailableWithoutNetworkWork()
    {
        var transcription = await new UnavailableMediaTranscriptionProvider().DetectCapabilityAsync(default);
        var description = await new UnavailableMediaVisualDescriptionProvider().DetectCapabilityAsync(default);

        Assert.False(transcription.IsAvailable);
        Assert.False(description.IsAvailable);
        Assert.Contains("not configured", transcription.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("configured local provider", description.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies still-image previews are bounded, cached, and application-owned.</summary>
    [Fact]
    public async Task ThumbnailProvider_CreatesReusesAndClearsOnlyManagedPreview()
    {
        using var fixture = new TempFixture(
            "preview.png",
            CreateDecodablePng());
        var cache = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OpenSorSe-media-tests", Guid.NewGuid().ToString("N"));
        var provider = new SkiaMediaThumbnailProvider(new Configuration(), cache);

        var first = await provider.GetThumbnailAsync(fixture.Path, Evidence(MediaKind.Image), default);
        var second = await provider.GetThumbnailAsync(fixture.Path, Evidence(MediaKind.Image), default);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.True(File.Exists(first));
        await provider.ClearAsync(default);
        Assert.False(File.Exists(first));
        if (Directory.Exists(cache))
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    /// <summary>Verifies EXIF orientation is applied to the cached preview without writing the source image.</summary>
    [Fact]
    public async Task ThumbnailProvider_AppliesOrientationAndPreservesSource()
    {
        using var fixture = new TempFixture("portrait.png", CreateDecodablePng());
        var sourceBefore = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(fixture.Path)));
        var cache = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OpenSorSe-media-tests", Guid.NewGuid().ToString("N"));
        var provider = new SkiaMediaThumbnailProvider(new Configuration(), cache);
        var evidence = Evidence(MediaKind.Image) with
        {
            Metadata = Evidence(MediaKind.Image).Metadata with { Width = 4, Height = 2, Orientation = 6 },
        };

        var thumbnail = await provider.GetThumbnailAsync(fixture.Path, evidence, default);

        Assert.NotNull(thumbnail);
        using var decoded = SKBitmap.Decode(thumbnail);
        Assert.NotNull(decoded);
        Assert.Equal(2, decoded.Width);
        Assert.Equal(4, decoded.Height);
        Assert.Equal(sourceBefore, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(fixture.Path))));
        await provider.ClearAsync(default);
        if (Directory.Exists(cache))
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    /// <summary>Verifies declared source-pixel limits prevent expensive image decoding.</summary>
    [Fact]
    public async Task ThumbnailProvider_SkipsEvidenceBeyondPixelLimit()
    {
        using var fixture = new TempFixture("huge.png", CreateDecodablePng());
        var cache = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OpenSorSe-media-tests", Guid.NewGuid().ToString("N"));
        var provider = new SkiaMediaThumbnailProvider(
            new Configuration(new ApplicationSettings
            {
                MediaIntelligence = new MediaIntelligenceSettings { MaximumThumbnailSourcePixels = 1_000_000 },
            }),
            cache);
        var evidence = Evidence(MediaKind.Image) with
        {
            Metadata = Evidence(MediaKind.Image).Metadata with { Width = 10_000, Height = 10_000 },
        };

        Assert.Null(await provider.GetThumbnailAsync(fixture.Path, evidence, default));
        Assert.False(Directory.Exists(cache));
    }

    /// <summary>Verifies invalid cross-field video limits fail configuration validation.</summary>
    [Fact]
    public void MediaSettings_RejectUnboundedFrameAndOcrConfiguration()
    {
        var settings = new MediaIntelligenceSettings { MaximumVideoFrames = 2, MaximumVideoOcrFrames = 3 };

        Assert.Throws<ConfigurationValidationException>(settings.Validate);
    }

    /// <summary>Guards bounded image-header and cached-thumbnail cost against catastrophic regression.</summary>
    [Fact]
    [Trait("Category", "PerformanceRegression")]
    public async Task ImageMetadataAndThumbnailCostRemainBounded()
    {
        using var fixture = new TempFixture("performance.png", CreateDecodablePng());
        var metadata = new ImageMediaMetadataProvider();
        var cache = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OpenSorSe-media-tests", Guid.NewGuid().ToString("N"));
        var thumbnails = new SkiaMediaThumbnailProvider(new Configuration(), cache);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        for (var index = 0; index < 250; index++)
        {
            var result = await metadata.ExtractAsync(fixture.File, new MediaIntelligenceSettings(), default);
            Assert.NotNull(result.Metadata);
        }

        var evidence = Evidence(MediaKind.Image);
        var first = await thumbnails.GetThumbnailAsync(fixture.Path, evidence, default);
        for (var index = 0; index < 50; index++)
        {
            Assert.Equal(first, await thumbnails.GetThumbnailAsync(fixture.Path, evidence, default));
        }

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Image metadata and preview work took {stopwatch.Elapsed}.");
        await thumbnails.ClearAsync(default);
        if (Directory.Exists(cache))
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    /// <summary>Guards local OCR and transcript ingestion bounds without depending on live native tools.</summary>
    [Fact]
    [Trait("Category", "PerformanceRegression")]
    public async Task OcrAndTranscriptIngestionCostRemainBounded()
    {
        using var image = new TempFixture("performance.png", CreateDecodablePng());
        using var audio = new TempFixture("performance.wav", [1]);
        var imageService = CreateService(
            new ApplicationSettings { MediaIntelligence = new MediaIntelligenceSettings { ImageOcrEnabled = true } },
            new FakeMetadataProvider(MediaKind.Image),
            ocr: new FakeOcrService("docker compose up -d"));
        var audioService = CreateService(
            new ApplicationSettings { MediaIntelligence = new MediaIntelligenceSettings { AudioTranscriptionEnabled = true } },
            new FakeMetadataProvider(MediaKind.Audio, TimeSpan.FromMinutes(1)),
            transcription: new FakeTranscriptionProvider("raspberry pi monitoring"));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        for (var index = 0; index < 100; index++)
        {
            Assert.NotNull((await imageService.ExtractAsync(image.File, null, true, default)).Evidence?.OcrText);
            Assert.NotNull((await audioService.ExtractAsync(audio.File, null, false, default)).Evidence?.Transcript);
        }

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Synthetic OCR/transcript ingestion took {stopwatch.Elapsed}.");
    }

    /// <summary>Guards bounded audio/video metadata parsing and representative-frame scheduling.</summary>
    [Fact]
    [Trait("Category", "PerformanceRegression")]
    public async Task AudioVideoMetadataAndFrameSamplingCostRemainBounded()
    {
        using var audio = new TempFixture("performance.mp3", [1]);
        using var video = new TempFixture("performance.mp4", [1]);
        var audioProvider = new FfprobeMediaMetadataProvider(
            new Configuration(),
            new FixedToolLocator("ffprobe"),
            new RecordingProcessRunner(
                "ffprobe version synthetic",
                """{"streams":[{"codec_type":"audio","codec_name":"aac","sample_rate":"48000","channels":2}],"format":{"format_name":"mp3","duration":"65"}}"""));
        var videoProvider = new FfprobeMediaMetadataProvider(
            new Configuration(),
            new FixedToolLocator("ffprobe"),
            new RecordingProcessRunner(
                "ffprobe version synthetic",
                """{"streams":[{"codec_type":"video","codec_name":"h264","width":1920,"height":1080,"avg_frame_rate":"30/1"}],"format":{"format_name":"mp4","duration":"7200"}}"""));
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OpenSorSe-media-tests", Guid.NewGuid().ToString("N"));
        var sampler = new FfmpegVideoFrameSampler(
            new Configuration(),
            new FixedToolLocator("ffmpeg"),
            new RecordingProcessRunner("ffmpeg version synthetic", string.Empty, createFrameOutput: true),
            root);
        var settings = new MediaIntelligenceSettings { MaximumVideoFrames = 8 };
        var videoMetadata = new MediaMetadata { Kind = MediaKind.Video, Duration = TimeSpan.FromHours(2) };
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        for (var index = 0; index < 100; index++)
        {
            Assert.NotNull((await audioProvider.ExtractAsync(audio.File, settings, default)).Metadata);
            Assert.NotNull((await videoProvider.ExtractAsync(video.File, settings, default)).Metadata);
        }

        for (var index = 0; index < 20; index++)
        {
            var batch = await sampler.SampleAsync(video.File, videoMetadata, settings, default);
            Assert.Equal(8, batch.Frames.Count);
            sampler.DeleteWorkspace(batch.WorkspacePath!);
        }

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Synthetic audio/video metadata and frame sampling took {stopwatch.Elapsed}.");
        Assert.Empty(Directory.GetDirectories(root));
        Directory.Delete(root);
    }

    private static MediaIntelligenceService CreateService(
        ApplicationSettings settings,
        IMediaMetadataProvider metadata,
        IMediaTranscriptionProvider? transcription = null,
        IOcrService? ocr = null,
        IVideoFrameSampler? frames = null) =>
        new(
            new Configuration(settings),
            [metadata],
            transcription ?? new UnavailableMediaTranscriptionProvider(),
            new UnavailableMediaVisualDescriptionProvider(),
            frames ?? new EmptyFrameSampler(),
            ocr ?? new FakeOcrService(null));

    private static SearchCandidateDocument Candidate(string name, IndexedMediaEvidence? evidence) => new()
    {
        FileId = name,
        FullPath = "/synthetic/" + name,
        RelativePath = name,
        FileName = name,
        Extension = System.IO.Path.GetExtension(name),
        FileType = evidence?.Kind.ToString() ?? "document",
        MediaEvidence = evidence,
        IsFullyIndexed = true,
    };

    private static IndexedMediaEvidence Evidence(MediaKind kind) => new()
    {
        Kind = kind,
        Metadata = new MediaMetadata { Kind = kind, Container = "synthetic" },
        MetadataProvider = "synthetic",
        MetadataProviderVersion = "1",
        ProcessingFingerprint = "synthetic",
        Status = MediaExtractionStatus.Completed,
    };

    private static byte[] PngHeader(int width, int height)
    {
        var bytes = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        return bytes;
    }

    private static byte[] CreateDecodablePng()
    {
        using var bitmap = new SKBitmap(4, 2);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    private static byte[] ExifTiff()
    {
        var bytes = new byte[236];
        bytes[0] = (byte)'I';
        bytes[1] = (byte)'I';
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2, 2), 42);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 8);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), 6);
        WriteIfdEntry(bytes, 10, 0x0100, 4, 1, 4_032);
        WriteIfdEntry(bytes, 22, 0x0101, 4, 1, 3_024);
        WriteIfdEntry(bytes, 34, 0x010F, 2, 8, 86);
        WriteIfdEntry(bytes, 46, 0x0110, 2, 9, 94);
        WriteIfdEntry(bytes, 58, 0x0112, 3, 1, 6);
        WriteIfdEntry(bytes, 70, 0x8825, 4, 1, 120);
        "Samsung\0"u8.CopyTo(bytes.AsSpan(86));
        "SM-G996B\0"u8.CopyTo(bytes.AsSpan(94));

        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(120, 2), 4);
        WriteIfdEntry(bytes, 122, 0x0001, 2, 2, (uint)'N');
        WriteIfdEntry(bytes, 134, 0x0002, 5, 3, 180);
        WriteIfdEntry(bytes, 146, 0x0003, 2, 2, (uint)'E');
        WriteIfdEntry(bytes, 158, 0x0004, 5, 3, 204);
        WriteRationals(bytes, 180, 45, 30, 0);
        WriteRationals(bytes, 204, 7, 15, 0);
        return bytes;
    }

    private static byte[] ExifJpeg()
    {
        var tiff = ExifTiff();
        var segmentLength = checked((ushort)(2 + 6 + tiff.Length));
        var bytes = new byte[2 + 2 + segmentLength + 2];
        bytes[0] = 0xff;
        bytes[1] = 0xd8;
        bytes[2] = 0xff;
        bytes[3] = 0xe1;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4, 2), segmentLength);
        "Exif\0\0"u8.CopyTo(bytes.AsSpan(6, 6));
        tiff.CopyTo(bytes, 12);
        bytes[^2] = 0xff;
        bytes[^1] = 0xd9;
        return bytes;
    }

    private static void WriteIfdEntry(byte[] bytes, int offset, ushort tag, ushort type, uint count, uint value)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), tag);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 2, 2), type);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4, 4), count);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 8, 4), value);
    }

    private static void WriteRationals(byte[] bytes, int offset, uint degrees, uint minutes, uint seconds)
    {
        foreach (var value in new[] { degrees, minutes, seconds })
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4, 4), 1);
            offset += 8;
        }
    }

    private sealed class Configuration : IConfigurationService
    {
        public Configuration(ApplicationSettings? settings = null) => Current = settings ?? new ApplicationSettings();

        public ApplicationSettings Current { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class TempFixture : IDisposable
    {
        private readonly string _root;

        public TempFixture(string fileName, byte[] content, long? reportedLength = null)
        {
            _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OpenSorSe-media-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Path = System.IO.Path.Combine(_root, fileName);
            System.IO.File.WriteAllBytes(Path, content);
            File = new FileEntry(
                Path,
                new FileMetadata(
                    fileName,
                    System.IO.Path.GetExtension(fileName),
                    reportedLength ?? content.Length,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    FileAttributes.Normal));
        }

        public string Path { get; }

        public FileEntry File { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private sealed class FixedToolLocator(string path) : IExternalToolLocator
    {
        public ExternalToolLocation Locate(string commandName, string? configuredPath = null) =>
            new(true, configuredPath ?? path, "Synthetic local tool");
    }

    private static (string Executable, IReadOnlyList<string> Arguments) BlockingProcess() =>
        OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe", ["/d", "/s", "/c", "ping 127.0.0.1 -n 20 >nul"])
            : ("/bin/sh", ["-c", "sleep 20"]);

    private sealed class RecordingProcessRunner(
        string versionOutput,
        string extractionOutput,
        bool createFrameOutput = false) : IMediaProcessRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

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
            if (createFrameOutput && arguments.Count > 0 && arguments[0] != "-version")
            {
                File.WriteAllBytes(arguments[^1], [1, 2, 3]);
            }

            return Task.FromResult(new MediaProcessResult(
                0,
                arguments.Contains("-version") ? versionOutput : extractionOutput,
                string.Empty,
                false,
                false));
        }
    }

    private sealed class TimeoutAfterVersionProcessRunner(string versionOutput) : IMediaProcessRunner
    {
        public Task<MediaProcessResult> ExecuteAsync(
            string executable,
            IReadOnlyList<string> arguments,
            int maximumOutputCharacters,
            int maximumErrorCharacters,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (arguments.Contains("-version"))
            {
                return Task.FromResult(new MediaProcessResult(0, versionOutput, string.Empty, false, false));
            }

            throw new TimeoutException("Synthetic media-provider timeout.");
        }
    }

    private sealed class FakeMetadataProvider(MediaKind kind, TimeSpan? duration = null) : IMediaMetadataProvider
    {
        public bool ThrowOnExtract { get; init; }

        public int ExtractionCount { get; private set; }

        public string Name => "synthetic-metadata";

        public string Version => "1";

        public bool Supports(MediaKind candidate, string normalizedExtension) => candidate == kind;

        public Task<MediaCapability> DetectCapabilityAsync(MediaKind candidate, CancellationToken cancellationToken) =>
            Task.FromResult(new MediaCapability(
                candidate == MediaKind.Image ? MediaCapabilityKind.ImageMetadata : candidate == MediaKind.Audio ? MediaCapabilityKind.AudioMetadata : MediaCapabilityKind.VideoMetadata,
                true,
                Name,
                Version,
                "Available"));

        public Task<MediaMetadataResult> ExtractAsync(FileEntry file, MediaIntelligenceSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExtractionCount++;
            if (ThrowOnExtract)
            {
                throw new InvalidDataException("Synthetic provider crash");
            }

            return Task.FromResult(new MediaMetadataResult(
                MediaExtractionStatus.Completed,
                new MediaMetadata { Kind = kind, Duration = duration, Container = "synthetic" },
                Name,
                Version,
                TimeSpan.FromMilliseconds(1),
                [],
                "Available"));
        }
    }

    private sealed class FakeTranscriptionProvider(
        string text,
        MediaExtractionStatus status = MediaExtractionStatus.Completed) : IMediaTranscriptionProvider
    {
        public int CallCount { get; private set; }

        public string Name => "synthetic-transcription";

        public string Version => "1";

        public Task<MediaCapability> DetectCapabilityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MediaCapability(MediaCapabilityKind.Transcription, true, Name, Version, "Available"));

        public Task<MediaTranscriptionResult> TranscribeAsync(
            FileEntry file,
            MediaMetadata metadata,
            MediaIntelligenceSettings settings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(new MediaTranscriptionResult(
                status,
                text,
                [new MediaTranscriptSegment(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), text)],
                Name,
                Version,
                TimeSpan.FromMilliseconds(1),
                [],
                "Available"));
        }
    }

    private sealed class FakeOcrService(string? text) : IOcrService
    {
        public int CallCount { get; private set; }

        public Task<OcrCapability> GetCapabilityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OcrCapability(true, "synthetic-ocr", "1", [".png"], false, "Available"));

        public Task<OcrCapability> RefreshCapabilityAsync(CancellationToken cancellationToken) => GetCapabilityAsync(cancellationToken);

        public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(new OcrResult(
                text is null ? OcrStatus.Skipped : OcrStatus.Completed,
                text,
                "eng",
                null,
                1,
                [],
                OcrFailureCategory.None,
                TimeSpan.FromMilliseconds(1),
                "synthetic-ocr",
                "1",
                "Available")
            {
                DownstreamText = text,
            });
        }
    }

    private sealed class FakeFrameSampler(int count) : IVideoFrameSampler
    {
        public bool WorkspaceDeleted { get; private set; }

        public string Name => "synthetic-frames";

        public string Version => "1";

        public Task<MediaCapability> DetectCapabilityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MediaCapability(MediaCapabilityKind.VideoFrameSampling, true, Name, Version, "Available"));

        public Task<VideoFrameSampleBatch> SampleAsync(
            FileEntry file,
            MediaMetadata metadata,
            MediaIntelligenceSettings settings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frames = Enumerable.Range(1, count)
                .Select(index => new VideoFrameSample($"synthetic-frame-{index}.png", TimeSpan.FromSeconds(index), 3))
                .ToArray();
            return Task.FromResult(new VideoFrameSampleBatch(
                MediaExtractionStatus.Completed,
                "synthetic-workspace",
                frames,
                Name,
                Version,
                TimeSpan.FromMilliseconds(1),
                [],
                "Available"));
        }

        public void DeleteWorkspace(string workspacePath) => WorkspaceDeleted = true;
    }

    private sealed class EmptyFrameSampler : IVideoFrameSampler
    {
        public string Name => "no-frames";

        public string Version => "1";

        public Task<MediaCapability> DetectCapabilityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MediaCapability(MediaCapabilityKind.VideoFrameSampling, false, Name, Version, "Not configured"));

        public Task<VideoFrameSampleBatch> SampleAsync(FileEntry file, MediaMetadata metadata, MediaIntelligenceSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(new VideoFrameSampleBatch(MediaExtractionStatus.Unavailable, null, [], Name, Version, TimeSpan.Zero, [], "Not configured"));

        public void DeleteWorkspace(string workspacePath)
        {
        }
    }
}
