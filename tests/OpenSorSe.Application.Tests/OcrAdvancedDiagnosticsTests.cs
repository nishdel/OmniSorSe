using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSorSe.Application.Content;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;
using OpenSorSe.Core.Logging;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies OCR and native-text diagnostics retain distinct bounded pipeline values.</summary>
public sealed class OcrAdvancedDiagnosticsTests
{
    /// <summary>Verifies reliable native page text records the skip decision without invoking OCR.</summary>
    [Fact]
    public async Task RecognizeAsync_ReliableNativeText_RecordsNativeOnlyDecision()
    {
        var collector = EnabledCollector();
        var engine = new DiagnosticOcrEngine();
        var service = new OcrService(
            new Configuration(new ContentSettings { OcrEnabled = true }),
            engine,
            collector);
        var request = Request(@"C:\native.pdf") with
        {
            HasReliableNativeText = true,
            PdfPages =
            [
                new PdfPageText(1, "Normalized native invoice text for this complete page.", true)
                {
                    RawNativeText = "Raw\nnative invoice text for this complete page.",
                },
            ],
        };

        var result = await service.RecognizeAsync(request, CancellationToken.None);

        Assert.Equal(OcrStatus.Skipped, result.Status);
        Assert.Equal(0, engine.Calls);
        var session = Assert.Single(collector.GetRecent());
        Assert.Equal(DiagnosticStatus.Skipped, session.Status);
        Assert.Contains(session.Events, item =>
            item.Fields.Any(field =>
                field.Name == "Raw OCR page text" &&
                field.Value.Contains("Raw\nnative", StringComparison.Ordinal)));
    }

    /// <summary>Verifies unavailable native raw text is not reconstructed from its normalized value.</summary>
    [Fact]
    public async Task RecognizeAsync_ReliableNativeTextWithoutRawValue_LeavesRawValueEmpty()
    {
        var collector = EnabledCollector();
        var engine = new DiagnosticOcrEngine();
        var service = new OcrService(
            new Configuration(new ContentSettings { OcrEnabled = true }),
            engine,
            collector);
        var request = Request(@"C:\native.pdf") with
        {
            HasReliableNativeText = true,
            PdfPages =
            [
                new PdfPageText(
                    1,
                    "Normalized native invoice text for this complete page.",
                    true),
            ],
        };

        var result = await service.RecognizeAsync(request, CancellationToken.None);

        var page = Assert.Single(result.Pages);
        Assert.Null(page.RawText);
        Assert.Equal(
            "Normalized native invoice text for this complete page.",
            page.NormalizedText);
        var session = Assert.Single(collector.GetRecent());
        var pageEvent = Assert.Single(
            session.Events,
            item => item.Stage == "Page 1");
        Assert.Equal(string.Empty, Field(pageEvent, "Raw OCR page text"));
    }

    /// <summary>Verifies raw engine output, normalized text, downstream text, partial pages, and truncation remain separate.</summary>
    [Fact]
    public async Task RecognizeAsync_OcrFallback_PreservesRawNormalizedAndDownstreamValues()
    {
        using var temporary = new TemporaryFile(".png");
        var collector = EnabledCollector();
        var engine = new DiagnosticOcrEngine
        {
            Result = new OcrResult(
                OcrStatus.PartiallyCompleted,
                "normalized OCR text",
                "eng",
                null,
                2,
                ["One page was blank."],
                OcrFailureCategory.None,
                TimeSpan.FromMilliseconds(12),
                "fake-tesseract",
                "5.5",
                "OCR completed partially.")
            {
                RawExtractedText = " raw\n OCR   text ",
                NormalizedText = "raw OCR text",
                DownstreamText = "normalized OCR text",
                WasTruncated = true,
                Pages =
                [
                    new OcrPageResult(1, OcrPageTextSource.Ocr, OcrStatus.Completed, "page one", null, "Completed")
                    {
                        RawText = " page\none ",
                        NormalizedText = "page one",
                        RenderDpi = 240,
                        RenderedWidth = 1200,
                        RenderedHeight = 1600,
                        PreprocessingSteps = ["Rendered PDF page to bounded grayscale PNG"],
                    },
                    new OcrPageResult(2, OcrPageTextSource.Failed, OcrStatus.Failed, null, null, "Blank page"),
                ],
            },
        };
        var service = new OcrService(
            new Configuration(new ContentSettings { OcrEnabled = true }),
            engine,
            collector);

        var result = await service.RecognizeAsync(Request(temporary.Path), CancellationToken.None);

        Assert.Equal(" raw\n OCR   text ", result.RawExtractedText);
        Assert.Equal("raw OCR text", result.NormalizedText);
        Assert.Equal("normalized OCR text", result.DownstreamText);
        var session = Assert.Single(collector.GetRecent());
        Assert.Equal(DiagnosticStatus.PartiallySucceeded, session.Status);
        var resultEvent = Assert.Single(session.Events, item => item.Stage == "OCR result");
        Assert.Equal(" raw\n OCR   text ", Field(resultEvent, "Raw OCR output"));
        Assert.Equal("raw OCR text", Field(resultEvent, "Normalized OCR text"));
        Assert.Equal("normalized OCR text", Field(resultEvent, "Text supplied downstream"));
        Assert.Equal("True", Field(resultEvent, "Truncated"));
        Assert.Contains(session.Events, item =>
            item.Stage == "Page 1" &&
            Field(item, "Rendered dimensions") == "1200 x 1600");
        Assert.Contains(session.Events, item =>
            item.Stage == "Page 2" &&
            item.Severity == DiagnosticSeverity.Error);
        Assert.Contains(session.Events, item =>
            item.Stage == "Rendered page preview retention" &&
            item.Message.Contains("not retained", StringComparison.Ordinal));
    }

    /// <summary>Verifies caller cancellation is correlated and retained as terminal without being swallowed.</summary>
    [Fact]
    public async Task RecognizeAsync_Cancelled_RecordsCancellationAndPropagates()
    {
        using var temporary = new TemporaryFile(".png");
        var collector = EnabledCollector();
        var engine = new DiagnosticOcrEngine { Block = true };
        var service = new OcrService(
            new Configuration(new ContentSettings { OcrEnabled = true }),
            engine,
            collector);
        using var cancellation = new CancellationTokenSource();

        var running = service.RecognizeAsync(Request(temporary.Path), cancellation.Token);
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal(DiagnosticStatus.Cancelled, Assert.Single(collector.GetRecent()).Status);
    }

    /// <summary>Verifies pre-cancellation closes the newly created session without touching the OCR engine.</summary>
    [Fact]
    public async Task RecognizeAsync_PreCancelled_DoesNotLeakActiveSession()
    {
        var collector = EnabledCollector();
        var engine = new DiagnosticOcrEngine();
        var service = new OcrService(
            new Configuration(new ContentSettings { OcrEnabled = true }),
            engine,
            collector);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RecognizeAsync(Request(@"C:\cancelled.png"), cancellation.Token));

        Assert.Equal(0, engine.Calls);
        Assert.Equal(DiagnosticStatus.Cancelled, Assert.Single(collector.GetRecent()).Status);
    }

    /// <summary>Verifies an engine cannot bypass service text, page, step, or warning retention bounds.</summary>
    [Fact]
    public async Task RecognizeAsync_OversizedEngineResult_IsBoundedAtServiceBoundary()
    {
        using var temporary = new TemporaryFile(".png");
        const int limit = 4096;
        var oversized = new string('x', limit + 1000);
        var engine = new DiagnosticOcrEngine
        {
            Result = new OcrResult(
                OcrStatus.Completed,
                oversized,
                "eng",
                null,
                30,
                Enumerable.Range(0, 20).Select(index => $"warning {index}").ToArray(),
                OcrFailureCategory.None,
                TimeSpan.FromMilliseconds(1),
                "fake-tesseract",
                "5.5",
                "Completed")
            {
                RawExtractedText = "\n" + oversized,
                NormalizedText = oversized,
                DownstreamText = oversized,
                Pages = Enumerable.Range(1, 30)
                    .Select(index => new OcrPageResult(
                        index,
                        OcrPageTextSource.Ocr,
                        OcrStatus.Completed,
                        oversized,
                        null,
                        "Completed")
                    {
                        RawText = "\n" + oversized,
                        NormalizedText = oversized,
                        PreprocessingSteps = Enumerable.Range(0, 20).Select(step => $"step {step}").ToArray(),
                    })
                    .ToArray(),
            },
        };
        var service = new OcrService(
            new Configuration(new ContentSettings
            {
                OcrEnabled = true,
                MaximumOcrTextCharacters = limit,
                MaximumPagesPerDocument = 25,
            }),
            engine);

        var result = await service.RecognizeAsync(
            Request(temporary.Path) with { MaximumTextCharacters = limit, MaximumPages = 25 },
            CancellationToken.None);

        Assert.True(result.WasTruncated);
        Assert.Equal(limit, result.ExtractedText!.Length);
        Assert.Equal(limit, result.RawExtractedText!.Length);
        Assert.StartsWith("\n", result.RawExtractedText, StringComparison.Ordinal);
        Assert.Equal(25, result.Pages.Count);
        Assert.All(result.Pages, page =>
        {
            Assert.True(page.RawText!.Length <= limit);
            Assert.True(page.NormalizedText!.Length <= limit);
            Assert.True(page.PreprocessingSteps.Count <= 16);
        });
        Assert.True(result.Warnings.Count <= 16);
        Assert.Contains(result.Warnings, warning => warning.Contains("retention reached", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Feature page results honor content settings while detailed diagnostic page events remain independently bounded.</summary>
    [Fact]
    public async Task RecognizeAsync_MoreThanDiagnosticPageLimit_DoesNotChangeFeatureResult()
    {
        using var temporary = new TemporaryFile(".pdf");
        var pageCount = DiagnosticLimits.MaximumPageRecords + 20;
        var engine = new DiagnosticOcrEngine
        {
            Result = new OcrResult(
                OcrStatus.Completed,
                "normalized downstream text",
                "eng",
                null,
                pageCount,
                [],
                OcrFailureCategory.None,
                TimeSpan.FromMilliseconds(1),
                "fake-tesseract",
                "5.5",
                "Completed")
            {
                RawExtractedText = null,
                NormalizedText = "normalized downstream text",
                DownstreamText = "normalized downstream text",
                Pages = Enumerable.Range(1, pageCount)
                    .Select(page => new OcrPageResult(
                        page,
                        OcrPageTextSource.Ocr,
                        OcrStatus.Completed,
                        $"page {page}",
                        null,
                        "Completed"))
                    .ToArray(),
            },
        };
        var collector = EnabledCollector();
        var service = new OcrService(
            new Configuration(new ContentSettings
            {
                OcrEnabled = true,
                MaximumPagesPerDocument = pageCount,
            }),
            engine,
            collector);

        var result = await service.RecognizeAsync(
            Request(temporary.Path) with { MaximumPages = pageCount },
            CancellationToken.None);

        Assert.Equal(pageCount, result.Pages.Count);
        var session = Assert.Single(collector.GetRecent());
        Assert.Equal(
            DiagnosticLimits.MaximumPageRecords,
            session.Events.Count(item => item.Fields.Any(field => field.Name == "Page number")));
        Assert.Contains(session.Events, item =>
            item.Stage == "Page diagnostics bounded" &&
            Field(item, "Omitted page records") == "20");
        var resultEvent = Assert.Single(session.Events, item => item.Stage == "OCR result");
        Assert.Equal(string.Empty, Field(resultEvent, "Raw OCR output"));
        Assert.Equal("normalized downstream text", Field(resultEvent, "Normalized OCR text"));
    }

    /// <summary>Verifies scan-to-extraction correlation and downstream native/OCR separation.</summary>
    [Fact]
    public async Task ContentIndexing_CorrelatesScanAndExtractionSession()
    {
        using var temporary = new TemporaryFile(".pdf");
        var collector = EnabledCollector();
        var configuration = new Configuration(new ContentSettings
        {
            MetadataExtractionEnabled = true,
            OcrEnabled = true,
        });
        var ocr = new OcrService(configuration, new DiagnosticOcrEngine(), collector);
        var store = new MemoryStore();
        var pipeline = new FixedMetadataPipeline();
        var service = new ContentIndexingService(
            configuration,
            pipeline,
            ocr,
            store,
            new Logging(),
            collector);
        var info = new FileInfo(temporary.Path);
        var file = new FileEntry(
            temporary.Path,
            new FileMetadata(
                info.Name,
                info.Extension,
                info.Length,
                DateTimeOffset.UnixEpoch,
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                DateTimeOffset.UnixEpoch,
                FileAttributes.Normal))
        {
            ScanDiagnosticSessionId = "scan:parent",
        };

        await service.IndexAsync([file], CancellationToken.None);

        var session = Assert.Single(collector.GetRecent());
        Assert.Contains("scan:parent", session.RelatedSessionIds);
        Assert.Contains(session.Events, item =>
            item.Stage == "Native text extraction" &&
            Field(item, "Raw native embedded text") == " raw\nnative ");
        Assert.Contains(session.Events, item =>
            item.Stage == "Downstream text" &&
            Field(item, "Native text supplied downstream") == "raw native");
        Assert.Equal(session.SessionId, (await store.GetAsync(temporary.Path, CancellationToken.None))!.DiagnosticSessionId);
    }

    private static string Field(OpenSorSe.Core.Diagnostics.DiagnosticEvent item, string name) =>
        item.Fields.Single(field => field.Name == name).Value;

    private static OcrRequest Request(string path) => new(
        path,
        "eng",
        50L * 1024 * 1024,
        25,
        TimeSpan.FromSeconds(30),
        false);

    private static InMemoryDiagnosticsCollector EnabledCollector()
    {
        var collector = new InMemoryDiagnosticsCollector();
        collector.Configure(new DiagnosticsSettings
        {
            EnableDiagnostics = true,
            OcrAndTextExtractionDiagnostics = true,
            ShowUnredactedDiagnosticContent = true,
        });
        return collector;
    }

    private sealed class DiagnosticOcrEngine : IOcrEngine
    {
        public int Calls { get; private set; }
        public bool Block { get; init; }
        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public OcrResult Result { get; init; } = new(
            OcrStatus.Completed,
            "recognized local text",
            "eng",
            null,
            1,
            [],
            OcrFailureCategory.None,
            TimeSpan.FromMilliseconds(1),
            "fake-tesseract",
            "5.5",
            "Completed")
        {
            RawExtractedText = " recognized\n local text ",
            NormalizedText = "recognized local text",
            DownstreamText = "recognized local text",
        };

        public Task<OcrCapability> DetectCapabilityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OcrCapability(
                true,
                "fake-tesseract",
                "5.5",
                [".png", ".pdf"],
                true,
                "Available"));

        public async Task<OcrResult> RecognizeAsync(
            OcrRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Started.TrySetResult(true);
            if (Block)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return Result;
        }
    }

    private sealed class FixedMetadataPipeline : IMetadataExtractionPipeline
    {
        public Task<MetadataExtractionResult> ExtractAsync(
            FileEntry file,
            long maximumInputBytes,
            int maximumPages,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MetadataExtractionResult(
                [],
                "raw native",
                false,
                1,
                [])
            {
                RawNativeText = " raw\nnative ",
                ExtractionStrategies = ["Fake native extractor"],
                PdfPages = [new PdfPageText(1, "raw native", false) { RawNativeText = " raw\nnative " }],
            });
    }

    private sealed class Configuration(ContentSettings content) : IConfigurationService
    {
        public ApplicationSettings Current { get; private set; } = new() { Content = content };
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryStore : IContentStore
    {
        private ContentRecord? _record;
        public Task<ContentRecord?> GetAsync(string fullPath, CancellationToken cancellationToken) =>
            Task.FromResult(_record);
        public Task<IReadOnlyList<ContentRecord>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ContentRecord>>(_record is null ? [] : [_record]);
        public Task UpsertAsync(ContentRecord record, CancellationToken cancellationToken)
        {
            _record = record;
            return Task.CompletedTask;
        }
        public Task RemoveMissingAsync(IReadOnlyCollection<string> knownPaths, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken)
        {
            _record = null;
            return Task.CompletedTask;
        }
    }

    private sealed class Logging : ILoggingService
    {
        public void Initialize(LogLevel minimumLevel)
        {
        }

        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;

        public void Dispose()
        {
        }
    }

    private sealed class TemporaryFile : IDisposable
    {
        public TemporaryFile(string extension)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"opensorse-diagnostic-{Guid.NewGuid():N}{extension}");
            File.WriteAllBytes(Path, [1, 2, 3]);
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }
}
