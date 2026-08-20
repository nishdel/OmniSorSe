using System.Text;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Media;
using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.SmartTags;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Scanner;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Indexing;

/// <summary>Runs bounded application-owned metadata, extraction, OCR, enrichment, and search stages.</summary>
public sealed class DefaultIndexingStageProcessor : IIndexingStageProcessor, IIndexingProcessorFingerprint
{
    private static readonly HashSet<string> StopWords = new(
        ["and", "the", "for", "with", "from", "that", "this", "into", "your", "are", "was", "were", "has", "have"],
        StringComparer.OrdinalIgnoreCase);

    private readonly IConfigurationService _configurationService;
    private readonly IContentIndexingService _contentIndexingService;
    private readonly IContentStore _contentStore;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IFileHasher _fileHasher;
    private readonly IIndexingEnrichmentProvider? _enrichmentProvider;
    private readonly IRelationshipService? _relationshipService;
    private readonly IMediaIntelligenceService? _mediaIntelligenceService;
    private readonly IContentIntelligenceProvider? _contentIntelligenceProvider;
    private readonly ISmartTagClassifier? _smartTagClassifier;

    /// <summary>Initializes the provider-independent application stage processor.</summary>
    public DefaultIndexingStageProcessor(
        IConfigurationService configurationService,
        IFileHasher fileHasher,
        IContentIndexingService contentIndexingService,
        IContentStore contentStore,
        IEmbeddingProvider embeddingProvider,
        IIndexingEnrichmentProvider? enrichmentProvider = null,
        IRelationshipService? relationshipService = null,
        IMediaIntelligenceService? mediaIntelligenceService = null,
        IContentIntelligenceProvider? contentIntelligenceProvider = null,
        ISmartTagClassifier? smartTagClassifier = null)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _fileHasher = fileHasher ?? throw new ArgumentNullException(nameof(fileHasher));
        _contentIndexingService = contentIndexingService ?? throw new ArgumentNullException(nameof(contentIndexingService));
        _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
        _embeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
        _enrichmentProvider = enrichmentProvider;
        _relationshipService = relationshipService;
        _mediaIntelligenceService = mediaIntelligenceService;
        _contentIntelligenceProvider = contentIntelligenceProvider;
        _smartTagClassifier = smartTagClassifier;
    }

    /// <inheritdoc />
    public async Task<IndexingStageOutput> ProcessAsync(
        IndexingWorkItem workItem,
        DeepIndexingSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return workItem.Stage switch
            {
                IndexingStage.FileDiscovered => ProcessDiscovery(workItem),
                IndexingStage.MetadataIndexed => ProcessMetadata(workItem),
                IndexingStage.ContentFingerprinted => await FingerprintAsync(workItem, cancellationToken).ConfigureAwait(false),
                IndexingStage.TextExtracted => await ExtractTextAsync(workItem, settings, ocr: false, cancellationToken).ConfigureAwait(false),
                IndexingStage.OcrProcessed => await ExtractTextAsync(workItem, settings, ocr: true, cancellationToken).ConfigureAwait(false),
                IndexingStage.SummaryKeywordsGenerated => await EnrichAsync(workItem, settings, cancellationToken).ConfigureAwait(false),
                IndexingStage.SmartTagsClassified => await ClassifySmartTagsAsync(workItem, cancellationToken).ConfigureAwait(false),
                IndexingStage.SemanticRepresentationGenerated => ProcessSemanticRepresentation(workItem, settings),
                IndexingStage.SearchIndexUpdated or
                IndexingStage.FileFullyIndexed => Complete(),
                IndexingStage.RelationshipAnalysisCompleted => await AnalyzeRelationshipsAsync(workItem, settings, cancellationToken).ConfigureAwait(false),
                _ => Permanent("unsupported-stage"),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(IndexingFailureCategory.PermissionDenied, "access-denied", isRetryable: false);
        }
        catch (FileNotFoundException)
        {
            return Failure(IndexingFailureCategory.NotFound, "file-not-found", isRetryable: false);
        }
        catch (DirectoryNotFoundException)
        {
            return Failure(IndexingFailureCategory.NotFound, "folder-not-found", isRetryable: false);
        }
        catch (IOException)
        {
            return Failure(IndexingFailureCategory.TransientIo, "file-io-failure", isRetryable: true);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or InvalidDataException)
        {
            return Permanent(exception.GetType().Name);
        }
    }

    /// <summary>Creates the stable processor fingerprint used for incremental invalidation.</summary>
    public string CreateProcessorFingerprint(DeepIndexingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var content = _configurationService.Current.Content;
        var media = _configurationService.Current.MediaIntelligence;
        var intelligence = _configurationService.Current.ContentIntelligence;
        var value = string.Join(
            "|",
            DeepIndexingVersion.ProcessorVersion,
            settings.DefaultLevel,
            settings.MaximumExtractedTextCharacters,
            settings.MaximumOcrTextCharacters,
            settings.MaximumSemanticChunksPerDocument,
            settings.OcrProcessingEnabled,
            settings.AiProcessingEnabled,
            settings.SummaryProcessingEnabled,
            settings.SemanticProcessingEnabled,
            settings.RelationshipAnalysisEnabled,
            settings.MaximumRelationshipCandidates,
            settings.MaximumRelationshipsPerFile,
            settings.MaximumSmartCollectionMembers,
            string.Join(',', settings.RelationshipExcludedExtensions.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)),
            settings.ArchiveIndexingEnabled,
            settings.BinaryAndExecutableMetadataOnly,
            content.MaximumPagesPerDocument,
            content.MaximumFileSizeMiB,
            content.OcrLanguage,
            content.PdfRasterizationDpi,
            media.Enabled,
            media.ImageMetadataEnabled,
            media.ImageOcrEnabled,
            media.AudioMetadataEnabled,
            media.AudioTranscriptionEnabled,
            media.VideoMetadataEnabled,
            media.VideoTranscriptionEnabled,
            media.VideoFrameAnalysisEnabled,
            media.VisualDescriptionsEnabled,
            media.MaximumMediaFileSizeMiB,
            media.MaximumAudioDurationMinutes,
            media.MaximumVideoDurationMinutes,
            media.MaximumVideoFrames,
            media.MaximumVideoOcrFrames,
            media.MaximumTranscriptCharacters,
            media.MaximumDescriptionCharacters,
            media.WhisperExecutablePath ?? string.Empty,
            media.WhisperModelPath ?? string.Empty,
            media.TranscriptionTimeoutSeconds,
            intelligence.Enabled,
            intelligence.TopicExtractionEnabled,
            intelligence.EntityExtractionEnabled,
            intelligence.SummaryGenerationEnabled,
            intelligence.MaximumInputCharacters,
            intelligence.MaximumTopics,
            intelligence.MaximumEntities,
            intelligence.MaximumKeywords,
            intelligence.MaximumSummaryCharacters,
            intelligence.MaximumEvidenceExcerptCharacters,
            _contentIntelligenceProvider?.Version ?? "none",
            _embeddingProvider.Dimensions,
            _enrichmentProvider?.Version ?? "none");
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }

    private static IndexingStageOutput ProcessDiscovery(IndexingWorkItem workItem) =>
        (workItem.Observation.Attributes & FileAttributes.ReparsePoint) != 0
            ? new IndexingStageOutput
            {
                Status = IndexingStageStatus.Skipped,
                ErrorCode = "symbolic-link-not-followed",
                StopsFile = true,
            }
            : Complete();

    private static IndexingStageOutput ProcessMetadata(IndexingWorkItem workItem)
    {
        var info = new FileInfo(workItem.FullPath);
        info.Refresh();
        if (!info.Exists)
        {
            return Failure(IndexingFailureCategory.NotFound, "file-not-found", isRetryable: false);
        }

        var currentFingerprint = PhysicalIndexFileDiscovery.CreateMetadataFingerprint(
            info.Length,
            new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            info.Attributes);
        return string.Equals(currentFingerprint, workItem.Observation.MetadataFingerprint, StringComparison.Ordinal)
            ? Complete()
            : Failure(IndexingFailureCategory.FileChanged, "metadata-changed-during-queue", isRetryable: true);
    }

    private async Task<IndexingStageOutput> FingerprintAsync(
        IndexingWorkItem workItem,
        CancellationToken cancellationToken)
    {
        var result = await _fileHasher.HashAsync(
            [CreateFileEntry(workItem)],
            cancellationToken).ConfigureAwait(false);
        var hash = result.Files.Single().Hash?.Value;
        if (!string.IsNullOrWhiteSpace(hash))
        {
            var fileEntry = CreateFileEntry(workItem);
            var media = _mediaIntelligenceService is null ||
                _mediaIntelligenceService.Classify(workItem.FullPath) == MediaKind.None
                ? null
                : await _mediaIntelligenceService
                    .ExtractMetadataAsync(fileEntry, workItem.MediaEvidence, cancellationToken)
                    .ConfigureAwait(false);
            if (media?.Status == MediaExtractionStatus.Unavailable)
            {
                return new IndexingStageOutput
                {
                    Status = IndexingStageStatus.WaitingForDependency,
                    ContentHash = hash,
                    WaitingDependency = "media metadata provider",
                    FailureCategory = IndexingFailureCategory.DependencyUnavailable,
                    ErrorCode = "media-metadata-provider-unavailable",
                    IsRetryable = true,
                    MediaEvidence = media.Evidence,
                };
            }

            if (media?.Status == MediaExtractionStatus.Failed)
            {
                return new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Failed,
                    ContentHash = hash,
                    FailureCategory = IndexingFailureCategory.TransientIo,
                    ErrorCode = "media-metadata-provider-failed",
                    IsRetryable = true,
                    MediaEvidence = media.Evidence,
                };
            }

            return new IndexingStageOutput
            {
                Status = IndexingStageStatus.Complete,
                ContentHash = hash,
                MediaEvidence = media?.Evidence,
            };
        }

        var issue = result.Issues.FirstOrDefault();
        return issue?.Kind switch
        {
            FileHashIssueKind.AccessDenied =>
                Failure(IndexingFailureCategory.PermissionDenied, "access-denied", isRetryable: false),
            FileHashIssueKind.FileUnavailable =>
                Failure(IndexingFailureCategory.NotFound, "file-not-found", isRetryable: false),
            FileHashIssueKind.FileChangedDuringHashing =>
                Failure(IndexingFailureCategory.FileChanged, "file-changed-during-fingerprint", isRetryable: true),
            FileHashIssueKind.ReparsePointSkipped =>
                new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Skipped,
                    ErrorCode = "symbolic-link-not-followed",
                    StopsFile = true,
                },
            FileHashIssueKind.FileUnreadable =>
                Failure(IndexingFailureCategory.FileLocked, "file-locked-or-unreadable", isRetryable: true),
            _ => Failure(IndexingFailureCategory.TransientIo, "file-unreadable", isRetryable: true),
        };
    }

    private async Task<IndexingStageOutput> ExtractTextAsync(
        IndexingWorkItem workItem,
        DeepIndexingSettings settings,
        bool ocr,
        CancellationToken cancellationToken)
    {
        var content = _configurationService.Current.Content;
        var fileEntry = CreateFileEntry(workItem);
        var mediaKind = _mediaIntelligenceService?.Classify(workItem.FullPath) ?? MediaKind.None;
        MediaIntelligenceResult? mediaResult = null;
        if (mediaKind != MediaKind.None && _mediaIntelligenceService is not null)
        {
            mediaResult = ocr
                ? await _mediaIntelligenceService.ExtractAsync(
                    fileEntry,
                    workItem.MediaEvidence,
                    allowOcr: settings.OcrProcessingEnabled,
                    cancellationToken).ConfigureAwait(false)
                : await _mediaIntelligenceService.ExtractMetadataAsync(
                    fileEntry,
                    workItem.MediaEvidence,
                    cancellationToken).ConfigureAwait(false);
        }

        await _contentIndexingService.IndexAsync(
            [fileEntry],
            new ContentIndexingOptions(
                MetadataEnabled: true,
                TextEnabled: true,
                OcrEnabled: mediaKind == MediaKind.None && ocr && settings.OcrProcessingEnabled,
                OcrOnlyWhenTextUnavailable: content.OcrOnlyWhenNativeTextUnavailable,
                OcrLanguage: content.OcrLanguage,
                MaximumPagesPerDocument: content.MaximumPagesPerDocument,
                MaximumFileSizeBytes: content.MaximumFileSizeMiB * 1024L * 1024L),
            cancellationToken).ConfigureAwait(false);
        var record = await _contentStore.GetAsync(workItem.FullPath, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return Failure(IndexingFailureCategory.Permanent, "content-extraction-produced-no-record", isRetryable: false);
        }

        if (!ocr)
        {
            return new IndexingStageOutput
            {
                Status = IndexingStageStatus.Complete,
                ExtractedText = Bound(record.NativeText, settings.MaximumExtractedTextCharacters),
                MediaEvidence = mediaResult?.Evidence,
            };
        }

        if (mediaKind != MediaKind.None)
        {
            var mediaEvidence = mediaResult?.Evidence ?? workItem.MediaEvidence;
            if (mediaResult?.Status == MediaExtractionStatus.Unavailable &&
                RequiresOptionalMediaDependency(mediaKind, _configurationService.Current.MediaIntelligence))
            {
                return new IndexingStageOutput
                {
                    Status = IndexingStageStatus.WaitingForDependency,
                    WaitingDependency = mediaKind == MediaKind.Image ? "OCR" : "media provider",
                    FailureCategory = IndexingFailureCategory.DependencyUnavailable,
                    ErrorCode = "media-provider-unavailable",
                    IsRetryable = true,
                    MediaEvidence = mediaEvidence,
                };
            }

            if (mediaResult?.Status == MediaExtractionStatus.Failed)
            {
                return new IndexingStageOutput
                {
                    Status = IndexingStageStatus.Failed,
                    FailureCategory = IndexingFailureCategory.TransientIo,
                    ErrorCode = "media-provider-failed",
                    IsRetryable = true,
                    MediaEvidence = mediaEvidence,
                };
            }

            return new IndexingStageOutput
            {
                Status = mediaEvidence is null ? IndexingStageStatus.Skipped : IndexingStageStatus.Complete,
                OcrText = Bound(mediaEvidence?.OcrText, settings.MaximumOcrTextCharacters),
                MediaEvidence = mediaEvidence,
            };
        }

        return record.OcrStatus switch
        {
            OcrStatus.Unavailable => new IndexingStageOutput
            {
                Status = IndexingStageStatus.WaitingForDependency,
                WaitingDependency = "OCR",
                FailureCategory = IndexingFailureCategory.DependencyUnavailable,
                ErrorCode = "ocr-unavailable",
                IsRetryable = true,
            },
            OcrStatus.Failed => Failure(IndexingFailureCategory.Permanent, "ocr-failed", isRetryable: false),
            OcrStatus.Pending or OcrStatus.Processing => Failure(
                IndexingFailureCategory.TransientIo,
                "ocr-incomplete",
                isRetryable: true),
            _ => new IndexingStageOutput
            {
                Status = record.OcrStatus == OcrStatus.Skipped
                    ? IndexingStageStatus.Skipped
                    : IndexingStageStatus.Complete,
                OcrText = Bound(record.OcrText, settings.MaximumOcrTextCharacters),
            },
        };
    }

    private async Task<IndexingStageOutput> EnrichAsync(
        IndexingWorkItem workItem,
        DeepIndexingSettings settings,
        CancellationToken cancellationToken)
    {
        var text = Bound(
            string.Join(' ', new[]
            {
                workItem.ExtractedText,
                workItem.OcrText,
                workItem.MediaEvidence?.Transcript,
                workItem.MediaEvidence?.OcrText,
                workItem.MediaEvidence?.VisualDescription,
                MediaEvidenceText.CreateMetadataText(workItem.MediaEvidence),
            }.Where(value => !string.IsNullOrWhiteSpace(value))),
            settings.MaximumExtractedTextCharacters + settings.MaximumOcrTextCharacters) ?? string.Empty;
        var summaryEnabled = settings.SummaryProcessingEnabled && !workItem.SuppressSummary;
        var semanticEnabled = settings.SemanticProcessingEnabled && !workItem.SuppressSemantic;
        var intelligenceSettings = _configurationService.Current.ContentIntelligence;
        IndexedContentIntelligence? contentIntelligence = null;
        if (summaryEnabled && intelligenceSettings.Enabled && _contentIntelligenceProvider is not null &&
            await _contentIntelligenceProvider.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            var intelligenceResult = await _contentIntelligenceProvider.AnalyzeAsync(
                new ContentIntelligenceRequest(CreateContentIntelligenceSources(workItem)),
                intelligenceSettings,
                cancellationToken).ConfigureAwait(false);
            contentIntelligence = intelligenceResult.Intelligence;
        }
        var keywords = summaryEnabled
            ? CreateKeywords(Path.GetFileName(workItem.FullPath), text)
            : [];
        string? summary = null;
        if (settings.AiProcessingEnabled && summaryEnabled)
        {
            if (_enrichmentProvider is null ||
                !await _enrichmentProvider.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
            {
                return new IndexingStageOutput
                {
                    Status = IndexingStageStatus.WaitingForDependency,
                    WaitingDependency = "local AI",
                    FailureCategory = IndexingFailureCategory.DependencyUnavailable,
                    ErrorCode = "local-ai-unavailable",
                    IsRetryable = true,
                };
            }

            var enrichment = await _enrichmentProvider
                .EnrichAsync(Path.GetFileName(workItem.FullPath), Bound(text, 16_384) ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);
            summary = Bound(enrichment.Summary, 2048);
            keywords = enrichment.Keywords
                .Concat(keywords)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => Bound(value.Trim(), 128)!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(64)
                .ToArray();
        }
        else if (workItem.Level == IndexingLevel.Deep && summaryEnabled)
        {
            summary = contentIntelligence?.Summary?.Text ?? CreateExtractiveSummary(text);
        }

        if (contentIntelligence is not null)
        {
            keywords = contentIntelligence.Keywords
                .Concat(keywords)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Min(64, intelligenceSettings.MaximumKeywords))
                .ToArray();
        }

        return new IndexingStageOutput
        {
            Status = IndexingStageStatus.Complete,
            Summary = summary,
            Keywords = keywords,
            ContentIntelligence = contentIntelligence,
            SelectedChunks = workItem.Level == IndexingLevel.Deep && semanticEnabled
                ? CreateChunks(text, settings.MaximumSemanticChunksPerDocument)
                : null,
        };
    }

    private IndexingStageOutput ProcessSemanticRepresentation(
        IndexingWorkItem workItem,
        DeepIndexingSettings settings)
    {
        if (!settings.SemanticProcessingEnabled || workItem.SuppressSemantic)
        {
            return new IndexingStageOutput
            {
                Status = IndexingStageStatus.Skipped,
            };
        }

        var input = string.Join(
            ' ',
            Path.GetFileName(workItem.FullPath),
            Path.GetFileName(Path.GetDirectoryName(workItem.FullPath)),
            workItem.ExtractedText,
            workItem.OcrText,
            workItem.MediaEvidence?.Transcript,
            workItem.MediaEvidence?.OcrText,
            workItem.MediaEvidence?.VisualDescription,
            MediaEvidenceText.CreateMetadataText(workItem.MediaEvidence),
            workItem.ContentIntelligence?.Summary?.Text,
            string.Join(' ', workItem.ContentIntelligence?.Topics.Select(item => item.DisplayName) ?? []),
            string.Join(' ', workItem.ContentIntelligence?.Entities.Select(item => item.DisplayName) ?? []));
        return new IndexingStageOutput
        {
            Status = IndexingStageStatus.Complete,
            SemanticRepresentation = _embeddingProvider.Embed(input),
        };
    }

    private async Task<IndexingStageOutput> ClassifySmartTagsAsync(
        IndexingWorkItem workItem,
        CancellationToken cancellationToken)
    {
        if (_smartTagClassifier is null)
        {
            return new IndexingStageOutput { Status = IndexingStageStatus.Skipped };
        }

        var contentRecord = await _contentStore.GetAsync(workItem.FullPath, cancellationToken).ConfigureAwait(false);
        var metadataText = string.Join(
            ' ',
            contentRecord is null
                ? [MediaEvidenceText.CreateMetadataText(workItem.MediaEvidence)]
                : contentRecord.Metadata.Select(field => $"{field.Name} {field.Value}")
                    .Append(MediaEvidenceText.CreateMetadataText(workItem.MediaEvidence)));
        var fingerprintValue = string.Join(
            "|",
            workItem.ContentHash ?? workItem.Observation.MetadataFingerprint,
            workItem.Observation.MetadataFingerprint,
            contentRecord?.ExtractionFingerprint ?? string.Empty,
            workItem.ContentIntelligence?.ProcessingFingerprint ?? string.Empty,
            workItem.MediaEvidence?.ProcessingFingerprint ?? string.Empty,
            _smartTagClassifier.Name,
            _smartTagClassifier.Version);
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintValue)))
            .ToLowerInvariant();
        var result = await _smartTagClassifier.ClassifyAsync(
            new SmartTagClassificationRequest
            {
                FileId = workItem.FileId,
                FileName = Path.GetFileName(workItem.FullPath),
                RelativePath = workItem.RelativePath,
                ExtractedText = workItem.ExtractedText,
                OcrText = workItem.OcrText,
                Transcript = workItem.MediaEvidence?.Transcript,
                MediaOcrText = workItem.MediaEvidence?.OcrText,
                MetadataText = metadataText,
                ContentIntelligence = workItem.ContentIntelligence,
                InputFingerprint = fingerprint,
            },
            cancellationToken).ConfigureAwait(false);
        return new IndexingStageOutput
        {
            Status = IndexingStageStatus.Complete,
            SmartTagClassification = result,
            ErrorCode = result.State switch
            {
                SmartTagClassificationState.NoEvidence => "smart-tags-no-evidence",
                SmartTagClassificationState.InsufficientEvidence => "smart-tags-insufficient-evidence",
                SmartTagClassificationState.ConflictingEvidence => "smart-tags-conflicting-evidence",
                _ => null,
            },
        };
    }

    private static IReadOnlyList<ContentIntelligenceSourceText> CreateContentIntelligenceSources(IndexingWorkItem workItem)
    {
        var sources = new List<ContentIntelligenceSourceText>(6);
        Add(ContentEvidenceSourceKind.ExtractedText, workItem.ExtractedText);
        Add(ContentEvidenceSourceKind.OcrText, workItem.OcrText);
        Add(ContentEvidenceSourceKind.MediaTranscript, workItem.MediaEvidence?.Transcript);
        Add(ContentEvidenceSourceKind.MediaOcr, workItem.MediaEvidence?.OcrText);
        Add(ContentEvidenceSourceKind.VisualDescription, workItem.MediaEvidence?.VisualDescription);
        Add(ContentEvidenceSourceKind.Metadata, MediaEvidenceText.CreateMetadataText(workItem.MediaEvidence));
        return sources;

        void Add(ContentEvidenceSourceKind kind, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                sources.Add(new ContentIntelligenceSourceText(kind, value));
            }
        }
    }

    private async Task<IndexingStageOutput> AnalyzeRelationshipsAsync(
        IndexingWorkItem workItem,
        DeepIndexingSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.RelationshipAnalysisEnabled || _relationshipService is null)
        {
            return new IndexingStageOutput { Status = IndexingStageStatus.Skipped };
        }

        var result = await _relationshipService
            .AnalyzeFileAsync(workItem.FileId, cancellationToken)
            .ConfigureAwait(false);
        return new IndexingStageOutput
        {
            Status = result.Skipped ? IndexingStageStatus.Skipped : IndexingStageStatus.Complete,
            ErrorCode = result.Skipped ? "relationship-analysis-excluded" : null,
        };
    }

    private static FileEntry CreateFileEntry(IndexingWorkItem workItem) =>
        new(
            workItem.FullPath,
            new FileMetadata(
                Path.GetFileName(workItem.FullPath),
                Path.GetExtension(workItem.FullPath).ToLowerInvariant(),
                workItem.Observation.Length,
                workItem.Observation.CreationTimeUtc,
                workItem.Observation.LastWriteTimeUtc,
                null,
                workItem.Observation.Attributes));

    private static bool RequiresOptionalMediaDependency(MediaKind kind, MediaIntelligenceSettings settings) => kind switch
    {
        MediaKind.Image => settings.ImageOcrEnabled || settings.VisualDescriptionsEnabled,
        MediaKind.Audio => settings.AudioTranscriptionEnabled,
        MediaKind.Video => settings.VideoTranscriptionEnabled || settings.VideoFrameAnalysisEnabled || settings.VisualDescriptionsEnabled,
        _ => false,
    };

    private static IReadOnlyList<string> CreateKeywords(string fileName, string text)
    {
        var builder = new StringBuilder();
        var words = new List<string>();
        foreach (var character in string.Concat(fileName, " ", text))
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                AddWord(builder, words);
            }

            if (words.Count >= 64)
            {
                break;
            }
        }

        AddWord(builder, words);
        return words.Distinct(StringComparer.Ordinal).Take(32).ToArray();
    }

    private static void AddWord(StringBuilder builder, ICollection<string> words)
    {
        if (builder.Length is >= 3 and <= 64)
        {
            var value = builder.ToString();
            if (!StopWords.Contains(value))
            {
                words.Add(value);
            }
        }

        builder.Clear();
    }

    private static string? CreateExtractiveSummary(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Bound(normalized, 512);
    }

    private static IReadOnlyList<string> CreateChunks(string text, int maximumChunks)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        const int chunkLength = 1000;
        var chunks = new List<string>(maximumChunks);
        for (var offset = 0; offset < text.Length && chunks.Count < maximumChunks; offset += chunkLength)
        {
            chunks.Add(text.Substring(offset, Math.Min(chunkLength, text.Length - offset)));
        }

        return chunks.AsReadOnly();
    }

    private static string? Bound(string? value, int maximumCharacters) =>
        string.IsNullOrEmpty(value)
            ? value
            : value.Length <= maximumCharacters ? value : value[..maximumCharacters];

    private static IndexingStageOutput Complete() => new()
    {
        Status = IndexingStageStatus.Complete,
    };

    private static IndexingStageOutput Failure(
        IndexingFailureCategory category,
        string code,
        bool isRetryable) => new()
        {
            Status = IndexingStageStatus.Failed,
            FailureCategory = category,
            ErrorCode = code,
            IsRetryable = isRetryable,
        };

    private static IndexingStageOutput Permanent(string code) =>
        Failure(IndexingFailureCategory.Permanent, code, isRetryable: false);
}
