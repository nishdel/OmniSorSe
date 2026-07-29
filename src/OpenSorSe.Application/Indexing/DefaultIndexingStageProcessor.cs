using System.Text;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.Semantic;
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

    /// <summary>Initializes the provider-independent application stage processor.</summary>
    public DefaultIndexingStageProcessor(
        IConfigurationService configurationService,
        IFileHasher fileHasher,
        IContentIndexingService contentIndexingService,
        IContentStore contentStore,
        IEmbeddingProvider embeddingProvider,
        IIndexingEnrichmentProvider? enrichmentProvider = null)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _fileHasher = fileHasher ?? throw new ArgumentNullException(nameof(fileHasher));
        _contentIndexingService = contentIndexingService ?? throw new ArgumentNullException(nameof(contentIndexingService));
        _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
        _embeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
        _enrichmentProvider = enrichmentProvider;
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
                IndexingStage.SemanticRepresentationGenerated => ProcessSemanticRepresentation(workItem, settings),
                IndexingStage.SearchIndexUpdated or
                IndexingStage.RelationshipAnalysisCompleted or
                IndexingStage.FileFullyIndexed => Complete(),
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
            settings.ArchiveIndexingEnabled,
            settings.BinaryAndExecutableMetadataOnly,
            content.MaximumPagesPerDocument,
            content.MaximumFileSizeMiB,
            content.OcrLanguage,
            content.PdfRasterizationDpi,
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
            return new IndexingStageOutput
            {
                Status = IndexingStageStatus.Complete,
                ContentHash = hash,
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
        await _contentIndexingService.IndexAsync(
            [CreateFileEntry(workItem)],
            new ContentIndexingOptions(
                MetadataEnabled: true,
                TextEnabled: true,
                OcrEnabled: ocr && settings.OcrProcessingEnabled,
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
            string.Join(' ', new[] { workItem.ExtractedText, workItem.OcrText }.Where(value => !string.IsNullOrWhiteSpace(value))),
            settings.MaximumExtractedTextCharacters + settings.MaximumOcrTextCharacters) ?? string.Empty;
        var summaryEnabled = settings.SummaryProcessingEnabled && !workItem.SuppressSummary;
        var semanticEnabled = settings.SemanticProcessingEnabled && !workItem.SuppressSemantic;
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
            summary = CreateExtractiveSummary(text);
        }

        return new IndexingStageOutput
        {
            Status = IndexingStageStatus.Complete,
            Summary = summary,
            Keywords = keywords,
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
            workItem.OcrText);
        return new IndexingStageOutput
        {
            Status = IndexingStageStatus.Complete,
            SemanticRepresentation = _embeddingProvider.Embed(input),
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
