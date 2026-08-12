using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenSorSe.Application.Content;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Media;

/// <summary>Coordinates optional media providers while preserving bounds, cancellation, and per-file isolation.</summary>
public sealed class MediaIntelligenceService : IMediaIntelligenceService
{
    private readonly IConfigurationService _configurationService;
    private readonly IReadOnlyList<IMediaMetadataProvider> _metadataProviders;
    private readonly IMediaTranscriptionProvider _transcriptionProvider;
    private readonly IMediaVisualDescriptionProvider _descriptionProvider;
    private readonly IVideoFrameSampler _frameSampler;
    private readonly IOcrService _ocrService;
    private readonly IDiagnosticsEventSink? _diagnostics;

    /// <summary>Initializes the provider-neutral media coordinator.</summary>
    public MediaIntelligenceService(
        IConfigurationService configurationService,
        IEnumerable<IMediaMetadataProvider> metadataProviders,
        IMediaTranscriptionProvider transcriptionProvider,
        IMediaVisualDescriptionProvider descriptionProvider,
        IVideoFrameSampler frameSampler,
        IOcrService ocrService,
        IDiagnosticsEventSink? diagnostics = null)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _metadataProviders = (metadataProviders ?? throw new ArgumentNullException(nameof(metadataProviders)))
            .OrderBy(provider => provider.Name, StringComparer.Ordinal)
            .ToArray();
        _transcriptionProvider = transcriptionProvider ?? throw new ArgumentNullException(nameof(transcriptionProvider));
        _descriptionProvider = descriptionProvider ?? throw new ArgumentNullException(nameof(descriptionProvider));
        _frameSampler = frameSampler ?? throw new ArgumentNullException(nameof(frameSampler));
        _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
        _diagnostics = DiagnosticsIsolation.Protect(diagnostics);
    }

    /// <inheritdoc />
    public MediaKind Classify(string fullPath) => MediaFormatRegistry.Classify(fullPath);

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var output = new List<MediaCapability>();
        foreach (var provider in _metadataProviders)
        {
            foreach (var kind in new[] { MediaKind.Image, MediaKind.Audio, MediaKind.Video })
            {
                if (provider.Supports(kind, FirstExtension(kind)))
                {
                    output.Add(await provider.DetectCapabilityAsync(kind, cancellationToken).ConfigureAwait(false));
                }
            }
        }

        var ocr = await _ocrService.GetCapabilityAsync(cancellationToken).ConfigureAwait(false);
        output.Add(new MediaCapability(
            MediaCapabilityKind.ImageOcr,
            ocr.IsAvailable,
            ocr.EngineIdentifier,
            ocr.EngineVersion,
            ocr.Message));
        output.Add(await _transcriptionProvider.DetectCapabilityAsync(cancellationToken).ConfigureAwait(false));
        output.Add(await _frameSampler.DetectCapabilityAsync(cancellationToken).ConfigureAwait(false));
        output.Add(await _descriptionProvider.DetectCapabilityAsync(cancellationToken).ConfigureAwait(false));
        return output
            .GroupBy(capability => capability.Kind)
            .Select(group => group.OrderByDescending(capability => capability.IsAvailable).ThenBy(capability => capability.Provider, StringComparer.Ordinal).First())
            .OrderBy(capability => capability.Kind)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<MediaIntelligenceResult> ExtractMetadataAsync(
        FileEntry file,
        IndexedMediaEvidence? existing,
        CancellationToken cancellationToken) =>
        await RunIsolatedAsync(
            file,
            "Extract bounded media metadata",
            () => ExtractMetadataCoreAsync(file, existing, cancellationToken),
            cancellationToken).ConfigureAwait(false);

    private async Task<MediaIntelligenceResult> ExtractMetadataCoreAsync(
        FileEntry file,
        IndexedMediaEvidence? existing,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        var settings = _configurationService.Current.MediaIntelligence;
        var kind = Classify(file.FullPath);
        if (!settings.Enabled || kind == MediaKind.None || !MetadataEnabled(kind, settings))
        {
            return new MediaIntelligenceResult(MediaExtractionStatus.Skipped, null, [], "Media metadata is disabled or not applicable.");
        }

        var fingerprint = CreateMetadataFingerprint(settings);
        if (existing is not null &&
            existing.Kind == kind &&
            existing.Status is not (MediaExtractionStatus.Unavailable or MediaExtractionStatus.Failed) &&
            string.Equals(existing.ProcessingFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return new MediaIntelligenceResult(existing.Status, existing with { CacheHit = true, ProcessingDuration = TimeSpan.Zero }, [], "Compatible media metadata was reused.");
        }

        var maximumBytes = settings.MaximumMediaFileSizeMiB * 1024L * 1024L;
        if (file.Metadata?.SizeInBytes is < 0 || file.Metadata?.SizeInBytes > maximumBytes)
        {
            return new MediaIntelligenceResult(MediaExtractionStatus.LimitExceeded, null, [], "The media file exceeds the configured metadata-processing size limit.");
        }

        var provider = _metadataProviders.FirstOrDefault(candidate =>
            candidate.Supports(kind, Path.GetExtension(file.FullPath).ToLowerInvariant()));
        if (provider is null)
        {
            return new MediaIntelligenceResult(MediaExtractionStatus.Unsupported, null, [], "No bounded metadata provider supports this media format.");
        }

        var result = await provider.ExtractAsync(file, settings, cancellationToken).ConfigureAwait(false);
        if (result.Metadata is null)
        {
            return new MediaIntelligenceResult(result.Status, null, [], result.Message);
        }

        return new MediaIntelligenceResult(
            result.Status,
            new IndexedMediaEvidence
            {
                Kind = kind,
                Metadata = result.Metadata,
                MetadataProvider = result.Provider,
                MetadataProviderVersion = result.ProviderVersion,
                ProcessingFingerprint = fingerprint,
                Status = result.Status,
                Warnings = result.Warnings.Take(16).ToArray(),
                ProcessingDuration = result.ProcessingDuration,
            },
            [new MediaCapability(
                kind == MediaKind.Image ? MediaCapabilityKind.ImageMetadata : kind == MediaKind.Audio ? MediaCapabilityKind.AudioMetadata : MediaCapabilityKind.VideoMetadata,
                result.Status is MediaExtractionStatus.Completed or MediaExtractionStatus.PartiallyCompleted,
                result.Provider,
                result.ProviderVersion,
                result.Message)],
            result.Message);
    }

    /// <inheritdoc />
    public async Task<MediaIntelligenceResult> ExtractAsync(
        FileEntry file,
        IndexedMediaEvidence? existing,
        bool allowOcr,
        CancellationToken cancellationToken) =>
        await RunIsolatedAsync(
            file,
            "Extract bounded media evidence",
            () => ExtractCoreAsync(file, existing, allowOcr, cancellationToken),
            cancellationToken).ConfigureAwait(false);

    private async Task<MediaIntelligenceResult> ExtractCoreAsync(
        FileEntry file,
        IndexedMediaEvidence? existing,
        bool allowOcr,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        var settings = _configurationService.Current.MediaIntelligence;
        var kind = Classify(file.FullPath);
        if (!settings.Enabled || kind == MediaKind.None)
        {
            return new MediaIntelligenceResult(MediaExtractionStatus.Skipped, null, [], "Media Intelligence is disabled or not applicable.");
        }

        var fingerprint = CreateProcessingFingerprint(settings, allowOcr);
        if (existing is not null &&
            existing.Kind == kind &&
            existing.Status is not (MediaExtractionStatus.Unavailable or MediaExtractionStatus.Failed) &&
            string.Equals(existing.ProcessingFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return new MediaIntelligenceResult(
                existing.Status,
                existing with { CacheHit = true, ProcessingDuration = TimeSpan.Zero },
                [],
                "Compatible derived media evidence was reused.");
        }

        var maximumBytes = settings.MaximumMediaFileSizeMiB * 1024L * 1024L;
        if (file.Metadata?.SizeInBytes is < 0 || file.Metadata?.SizeInBytes > maximumBytes)
        {
            return new MediaIntelligenceResult(MediaExtractionStatus.LimitExceeded, null, [], "The media file exceeds the configured processing-size limit.");
        }

        var provider = _metadataProviders.FirstOrDefault(candidate =>
            candidate.Supports(kind, Path.GetExtension(file.FullPath).ToLowerInvariant()));
        if (provider is null || !MetadataEnabled(kind, settings))
        {
            return new MediaIntelligenceResult(MediaExtractionStatus.Skipped, null, [], "Metadata extraction is disabled or no provider supports this media format.");
        }

        var started = Stopwatch.StartNew();
        var capabilities = new List<MediaCapability>();
        var warnings = new List<string>();
        var metadataResult = await provider.ExtractAsync(file, settings, cancellationToken).ConfigureAwait(false);
        capabilities.Add(new MediaCapability(
            kind == MediaKind.Image ? MediaCapabilityKind.ImageMetadata : kind == MediaKind.Audio ? MediaCapabilityKind.AudioMetadata : MediaCapabilityKind.VideoMetadata,
            metadataResult.Status is MediaExtractionStatus.Completed or MediaExtractionStatus.PartiallyCompleted,
            metadataResult.Provider,
            metadataResult.ProviderVersion,
            metadataResult.Message));
        warnings.AddRange(metadataResult.Warnings);
        if (metadataResult.Metadata is null)
        {
            return new MediaIntelligenceResult(metadataResult.Status, null, capabilities.AsReadOnly(), metadataResult.Message);
        }

        var metadata = metadataResult.Metadata;
        string? transcript = null;
        IReadOnlyList<MediaTranscriptSegment> transcriptSegments = [];
        string? ocrText = null;
        string? description = null;
        IReadOnlyList<string> visualTags = [];
        var transcriptionProvider = (string?)null;
        var descriptionProvider = (string?)null;
        var sampledFrameCount = 0;
        var ocrFrameCount = 0;
        var waitingForRequestedProvider = false;
        var requestedProviderFailed = false;
        var resourceLimitApplied = false;
        VideoFrameSampleBatch? frames = null;

        try
        {
            if (kind == MediaKind.Audio && settings.AudioTranscriptionEnabled && !ShouldTranscribe(kind, metadata, settings))
            {
                resourceLimitApplied = true;
                warnings.Add("Audio transcription was skipped because duration was unavailable or exceeded the configured limit.");
            }

            if (kind == MediaKind.Video && settings.VideoTranscriptionEnabled && !ShouldTranscribe(kind, metadata, settings))
            {
                resourceLimitApplied = true;
                warnings.Add("Video transcription was skipped because duration was unavailable or exceeded the configured limit.");
            }

            if (kind == MediaKind.Video && settings.VideoFrameAnalysisEnabled && !WithinVideoDuration(metadata, settings))
            {
                resourceLimitApplied = true;
                warnings.Add("Representative-frame analysis was skipped because duration was unavailable or exceeded the configured limit.");
            }

            if (ShouldTranscribe(kind, metadata, settings))
            {
                var capability = await _transcriptionProvider.DetectCapabilityAsync(cancellationToken).ConfigureAwait(false);
                capabilities.Add(capability);
                transcriptionProvider = _transcriptionProvider.Name;
                if (capability.IsAvailable)
                {
                    var result = await _transcriptionProvider.TranscribeAsync(file, metadata, settings, cancellationToken).ConfigureAwait(false);
                    if (result.Status is MediaExtractionStatus.Completed or MediaExtractionStatus.PartiallyCompleted)
                    {
                        transcript = BoundNormalize(result.Text, settings.MaximumTranscriptCharacters);
                        transcriptSegments = NormalizeSegments(result.Segments, settings.MaximumTranscriptCharacters);
                    }

                    waitingForRequestedProvider |= result.Status == MediaExtractionStatus.Unavailable;
                    requestedProviderFailed |= result.Status == MediaExtractionStatus.Failed;
                    resourceLimitApplied |= result.Status == MediaExtractionStatus.LimitExceeded;
                    warnings.AddRange(result.Warnings);
                }
                else
                {
                    waitingForRequestedProvider = true;
                    warnings.Add(capability.Message);
                }
            }

            if (kind == MediaKind.Image && allowOcr && settings.ImageOcrEnabled)
            {
                var result = await RecognizeAsync(file.FullPath, settings, cancellationToken).ConfigureAwait(false);
                ocrText = result.Status == OcrStatus.Completed
                    ? result.DownstreamText ?? result.ExtractedText
                    : null;
                waitingForRequestedProvider |= result.Status == OcrStatus.Unavailable;
                requestedProviderFailed |= result.Status == OcrStatus.Failed;
                capabilities.Add(new MediaCapability(MediaCapabilityKind.ImageOcr, result.Status != OcrStatus.Unavailable, result.EngineIdentifier, result.EngineVersion, result.Message));
                warnings.AddRange(result.Warnings);
            }

            if (kind == MediaKind.Video && settings.VideoFrameAnalysisEnabled && WithinVideoDuration(metadata, settings))
            {
                frames = await _frameSampler.SampleAsync(file, metadata, settings, cancellationToken).ConfigureAwait(false);
                capabilities.Add(new MediaCapability(MediaCapabilityKind.VideoFrameSampling, frames.Status is MediaExtractionStatus.Completed or MediaExtractionStatus.PartiallyCompleted, frames.Provider, frames.ProviderVersion, frames.Message));
                sampledFrameCount = frames.Frames.Count;
                waitingForRequestedProvider |= frames.Status == MediaExtractionStatus.Unavailable;
                requestedProviderFailed |= frames.Status == MediaExtractionStatus.Failed;
                resourceLimitApplied |= frames.Status == MediaExtractionStatus.LimitExceeded;
                warnings.AddRange(frames.Warnings);
                if (allowOcr && settings.MaximumVideoOcrFrames > 0)
                {
                    var frameTexts = new List<string>();
                    foreach (var frame in frames.Frames.Take(settings.MaximumVideoOcrFrames))
                    {
                        var result = await RecognizeAsync(frame.ImagePath, settings, cancellationToken).ConfigureAwait(false);
                        var text = result.Status == OcrStatus.Completed
                            ? result.DownstreamText ?? result.ExtractedText
                            : null;
                        waitingForRequestedProvider |= result.Status == OcrStatus.Unavailable;
                        requestedProviderFailed |= result.Status == OcrStatus.Failed;
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            frameTexts.Add($"[{FormatPosition(frame.Position)}] {text}");
                            ocrFrameCount++;
                        }

                        warnings.AddRange(result.Warnings);
                    }

                    ocrText = BoundNormalize(string.Join(Environment.NewLine, frameTexts), _configurationService.Current.Content.MaximumOcrTextCharacters);
                }
            }

            if (settings.VisualDescriptionsEnabled)
            {
                var capability = await _descriptionProvider.DetectCapabilityAsync(cancellationToken).ConfigureAwait(false);
                capabilities.Add(capability);
                descriptionProvider = _descriptionProvider.Name;
                if (capability.IsAvailable)
                {
                    var result = await _descriptionProvider.DescribeAsync(file, frames?.Frames ?? [], settings, cancellationToken).ConfigureAwait(false);
                    if (result.Status is MediaExtractionStatus.Completed or MediaExtractionStatus.PartiallyCompleted)
                    {
                        description = BoundNormalize(result.Description, settings.MaximumDescriptionCharacters);
                        visualTags = result.Tags
                            .Select(tag => BoundNormalize(tag, 64))
                            .Where(tag => !string.IsNullOrWhiteSpace(tag))
                            .Cast<string>()
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(32)
                            .ToArray();
                    }

                    waitingForRequestedProvider |= result.Status == MediaExtractionStatus.Unavailable;
                    requestedProviderFailed |= result.Status == MediaExtractionStatus.Failed;
                    resourceLimitApplied |= result.Status == MediaExtractionStatus.LimitExceeded;
                    warnings.AddRange(result.Warnings);
                }
                else
                {
                    waitingForRequestedProvider = true;
                    warnings.Add(capability.Message);
                }
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(frames?.WorkspacePath))
            {
                _frameSampler.DeleteWorkspace(frames.WorkspacePath);
            }
        }

        var boundedWarnings = warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => BoundNormalize(warning, 256)!)
            .Distinct(StringComparer.Ordinal)
            .Take(16)
            .ToArray();
        var status = metadataResult.Status;
        if (requestedProviderFailed)
        {
            status = MediaExtractionStatus.Failed;
        }
        else if (waitingForRequestedProvider)
        {
            status = MediaExtractionStatus.Unavailable;
        }
        else if (resourceLimitApplied)
        {
            status = MediaExtractionStatus.LimitExceeded;
        }
        else if (boundedWarnings.Length > 0 && status == MediaExtractionStatus.Completed)
        {
            status = MediaExtractionStatus.PartiallyCompleted;
        }

        var evidence = new IndexedMediaEvidence
        {
            Kind = kind,
            Metadata = metadata,
            Transcript = transcript,
            TranscriptSegments = transcriptSegments,
            OcrText = ocrText,
            VisualDescription = description,
            VisualTags = visualTags,
            MetadataProvider = metadataResult.Provider,
            MetadataProviderVersion = metadataResult.ProviderVersion,
            TranscriptionProvider = transcriptionProvider,
            DescriptionProvider = descriptionProvider,
            SampledFrameCount = sampledFrameCount,
            OcrFrameCount = ocrFrameCount,
            ProcessingFingerprint = fingerprint,
            Status = status,
            Warnings = boundedWarnings,
            ProcessingDuration = started.Elapsed,
        };
        var message = status switch
        {
            MediaExtractionStatus.Completed => "Bounded media evidence is available for local Search.",
            MediaExtractionStatus.PartiallyCompleted => "Bounded media evidence is available with warnings.",
            MediaExtractionStatus.Unavailable => "A requested optional media capability is not currently available.",
            MediaExtractionStatus.Failed => "A requested media provider failed safely; this file can be retried.",
            MediaExtractionStatus.LimitExceeded => "Some media processing was skipped by the configured resource limits.",
            _ => "Media processing completed with a bounded status.",
        };
        return new MediaIntelligenceResult(status, evidence, capabilities.AsReadOnly(), message);
    }

    private async Task<MediaIntelligenceResult> RunIsolatedAsync(
        FileEntry file,
        string operation,
        Func<Task<MediaIntelligenceResult>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        var started = Stopwatch.StartNew();
        var kind = Classify(file.FullPath);
        var session = _diagnostics?.BeginSession(
            DiagnosticCategory.OcrAndTextExtraction,
            operation,
            [
                new DiagnosticField("Source file", file.FullPath, DiagnosticDataClassification.Path),
                new DiagnosticField("Media type", kind.ToString()),
            ]);
        try
        {
            var result = await action().ConfigureAwait(false);
            var evidence = result.Evidence;
            var terminalStatus = result.Status switch
            {
                MediaExtractionStatus.Completed => DiagnosticStatus.Succeeded,
                MediaExtractionStatus.PartiallyCompleted or MediaExtractionStatus.LimitExceeded => DiagnosticStatus.PartiallySucceeded,
                MediaExtractionStatus.Skipped or MediaExtractionStatus.Unsupported => DiagnosticStatus.Skipped,
                _ => DiagnosticStatus.Failed,
            };
            var severity = terminalStatus switch
            {
                DiagnosticStatus.Failed => DiagnosticSeverity.Error,
                DiagnosticStatus.PartiallySucceeded => DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Information,
            };
            _diagnostics?.Publish(
                session,
                "Media evidence",
                terminalStatus,
                severity,
                DiagnosticSection.Outputs,
                result.Message,
                [
                    new DiagnosticField("Provider", evidence?.MetadataProvider ?? "none"),
                    new DiagnosticField("Cache", evidence?.CacheHit == true ? "hit" : "miss"),
                    new DiagnosticField("Sampled frame count", (evidence?.SampledFrameCount ?? 0).ToString(CultureInfo.InvariantCulture)),
                    new DiagnosticField("OCR status", string.IsNullOrWhiteSpace(evidence?.OcrText) ? "no bounded text" : "bounded text available"),
                    new DiagnosticField("Transcription status", string.IsNullOrWhiteSpace(evidence?.Transcript) ? "no bounded text" : "bounded text available"),
                    new DiagnosticField("Generated evidence characters", ((evidence?.OcrText?.Length ?? 0) + (evidence?.Transcript?.Length ?? 0) + (evidence?.VisualDescription?.Length ?? 0)).ToString(CultureInfo.InvariantCulture)),
                    new DiagnosticField("Warning count", (evidence?.Warnings.Count ?? 0).ToString(CultureInfo.InvariantCulture)),
                ]);
            _diagnostics?.Complete(session, terminalStatus, started.Elapsed, result.Message, severity);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _diagnostics?.Complete(
                session,
                DiagnosticStatus.Cancelled,
                started.Elapsed,
                "Media extraction was cancelled cooperatively.",
                DiagnosticSeverity.Warning);
            throw;
        }
        catch (Exception exception)
        {
            _diagnostics?.Complete(
                session,
                DiagnosticStatus.Failed,
                started.Elapsed,
                "The media provider failed safely for this file.",
                DiagnosticSeverity.Error,
                [new DiagnosticField("Error category", exception.GetType().Name)]);
            return new MediaIntelligenceResult(
                MediaExtractionStatus.Failed,
                null,
                [],
                "Media extraction failed safely for this file. Other files can continue indexing.");
        }
    }

    internal string CreateProcessingFingerprint(MediaIntelligenceSettings settings, bool allowOcr)
    {
        var value = string.Join(
            '|',
            "media-2.3.0",
            allowOcr,
            settings.Enabled,
            settings.ImageMetadataEnabled,
            settings.ImageOcrEnabled,
            settings.VisualDescriptionsEnabled,
            settings.AudioMetadataEnabled,
            settings.AudioTranscriptionEnabled,
            settings.VideoMetadataEnabled,
            settings.VideoTranscriptionEnabled,
            settings.VideoFrameAnalysisEnabled,
            settings.MaximumMediaFileSizeMiB,
            settings.MaximumAudioDurationMinutes,
            settings.MaximumVideoDurationMinutes,
            settings.MaximumVideoFrames,
            settings.MaximumVideoOcrFrames,
            settings.MaximumTranscriptCharacters,
            settings.MaximumDescriptionCharacters,
            settings.ProviderTimeoutSeconds,
            settings.FfprobeExecutablePath ?? string.Empty,
            settings.FfmpegExecutablePath ?? string.Empty,
            settings.WhisperExecutablePath ?? string.Empty,
            settings.WhisperModelPath ?? string.Empty,
            settings.TranscriptionTimeoutSeconds,
            string.Join(',', _metadataProviders.Select(provider => $"{provider.Name}:{provider.Version}")),
            $"{_transcriptionProvider.Name}:{_transcriptionProvider.Version}",
            $"{_descriptionProvider.Name}:{_descriptionProvider.Version}",
            $"{_frameSampler.Name}:{_frameSampler.Version}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private string CreateMetadataFingerprint(MediaIntelligenceSettings settings)
    {
        var value = string.Join(
            '|',
            "media-metadata-2.3.0",
            settings.Enabled,
            settings.ImageMetadataEnabled,
            settings.AudioMetadataEnabled,
            settings.VideoMetadataEnabled,
            settings.MaximumMediaFileSizeMiB,
            settings.ProviderTimeoutSeconds,
            settings.FfprobeExecutablePath ?? string.Empty,
            string.Join(',', _metadataProviders.Select(provider => $"{provider.Name}:{provider.Version}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private async Task<OcrResult> RecognizeAsync(
        string fullPath,
        MediaIntelligenceSettings mediaSettings,
        CancellationToken cancellationToken)
    {
        var content = _configurationService.Current.Content;
        return await _ocrService.RecognizeAsync(
            new OcrRequest(
                fullPath,
                content.OcrLanguage,
                mediaSettings.MaximumMediaFileSizeMiB * 1024L * 1024L,
                1,
                TimeSpan.FromSeconds(Math.Min(content.MaximumOcrDurationSeconds, mediaSettings.ProviderTimeoutSeconds)),
                HasReliableNativeText: false)
            {
                MaximumTextCharacters = content.MaximumOcrTextCharacters,
                MaximumTemporaryStorageBytes = content.MaximumTemporaryStorageMiB * 1024L * 1024L,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static bool MetadataEnabled(MediaKind kind, MediaIntelligenceSettings settings) => kind switch
    {
        MediaKind.Image => settings.ImageMetadataEnabled,
        MediaKind.Audio => settings.AudioMetadataEnabled,
        MediaKind.Video => settings.VideoMetadataEnabled,
        _ => false,
    };

    private static bool ShouldTranscribe(MediaKind kind, MediaMetadata metadata, MediaIntelligenceSettings settings) =>
        kind switch
        {
            MediaKind.Audio => settings.AudioTranscriptionEnabled &&
                metadata.Duration is not null &&
                metadata.Duration > TimeSpan.Zero &&
                metadata.Duration <= TimeSpan.FromMinutes(settings.MaximumAudioDurationMinutes),
            MediaKind.Video => settings.VideoTranscriptionEnabled && WithinVideoDuration(metadata, settings),
            _ => false,
        };

    private static bool WithinVideoDuration(MediaMetadata metadata, MediaIntelligenceSettings settings) =>
        metadata.Duration is not null && metadata.Duration <= TimeSpan.FromMinutes(settings.MaximumVideoDurationMinutes);

    private static IReadOnlyList<MediaTranscriptSegment> NormalizeSegments(
        IReadOnlyList<MediaTranscriptSegment> segments,
        int maximumCharacters)
    {
        var output = new List<MediaTranscriptSegment>();
        var remaining = maximumCharacters;
        foreach (var segment in segments.Take(512))
        {
            if (segment.Start < TimeSpan.Zero || segment.End < segment.Start || remaining <= 0)
            {
                continue;
            }

            var text = BoundNormalize(segment.Text, Math.Min(2_048, remaining));
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            output.Add(segment with { Text = text });
            remaining -= text.Length;
        }

        return output.AsReadOnly();
    }

    private static string FormatPosition(TimeSpan position) =>
        position.TotalHours >= 1
            ? position.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : position.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    private static string FirstExtension(MediaKind kind) => kind switch
    {
        MediaKind.Image => ".jpg",
        MediaKind.Audio => ".mp3",
        MediaKind.Video => ".mp4",
        _ => string.Empty,
    };

    private static string? BoundNormalize(string? value, int maximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = ContentText.Normalize(value);
        return normalized.Length <= maximumCharacters ? normalized : normalized[..maximumCharacters];
    }
}
