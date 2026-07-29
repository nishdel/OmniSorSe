using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.Models;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;
using OpenSorSe.Core.Logging;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies bounded metadata extraction, local OCR gating, cache reuse, and failure isolation.</summary>
public sealed class ContentPipelineTests
{
    /// <summary>Verifies simple PDF metadata and native text retain distinct provenance.</summary>
    [Fact]
    public async Task PdfExtractor_BoundedDocument_ExtractsMetadataAndNativeText()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.PathFor("sample.pdf");
        await File.WriteAllTextAsync(
            path,
            "%PDF-1.4 /Title (Quarterly Report) /Author (Ada) /Type /Page BT (Revenue increased substantially during this quarter) Tj ET");
        var file = Entry(path);
        var extractor = new PdfMetadataExtractor();

        var result = await extractor.ExtractAsync(file, 1024 * 1024, 25, CancellationToken.None);

        Assert.Contains(result.Fields, field =>
            field.Name == "Document title" &&
            field.Value == "Quarterly Report" &&
            field.Provenance == ContentProvenance.EmbeddedMetadata);
        Assert.Contains("Revenue increased", result.NativeText, StringComparison.Ordinal);
        Assert.True(result.HasReliableNativeText);
        Assert.Equal(1, result.PageCount);
    }

    /// <summary>Verifies DOCX properties and text are read from selected XML parts without macro execution.</summary>
    [Fact]
    public async Task OpenXmlExtractor_Docx_ReadsCoreAndDocumentPartsOnly()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.PathFor("sample.docx");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "docProps/core.xml", """
                <cp:coreProperties xmlns:cp="urn:cp" xmlns:dc="urn:dc"><dc:title>Plan</dc:title><dc:creator>Grace</dc:creator></cp:coreProperties>
                """);
            WriteEntry(archive, "word/document.xml", """
                <w:document xmlns:w="urn:w"><w:body><w:p><w:r><w:t>Project alpha schedule and milestones</w:t></w:r></w:p></w:body></w:document>
                """);
            WriteEntry(archive, "word/vbaProject.bin", "must-not-be-opened");
            WriteEntry(archive, "_rels/.rels", """<Relationships><Relationship Target="https://example.invalid"/></Relationships>""");
        }

        var result = await new OpenXmlMetadataExtractor().ExtractAsync(
            Entry(path),
            1024 * 1024,
            25,
            CancellationToken.None);

        Assert.Contains(result.Fields, field => field.Name == "Document title" && field.Value == "Plan");
        Assert.Contains(result.Fields, field => field.Name == "Author" && field.Value == "Grace");
        Assert.Contains("Project alpha", result.NativeText, StringComparison.Ordinal);
        Assert.True(result.HasReliableNativeText);
    }

    /// <summary>Verifies XLSX sheet names and shared text are extracted deterministically.</summary>
    [Fact]
    public async Task OpenXmlExtractor_Xlsx_ReadsSheetsAndSharedStrings()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.PathFor("sample.xlsx");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "xl/workbook.xml", """
                <workbook><sheets><sheet name="Budget"/><sheet name="Actuals"/></sheets></workbook>
                """);
            WriteEntry(archive, "xl/sharedStrings.xml", """
                <sst><si><t>Annual finance forecast and approved budget</t></si></sst>
                """);
        }

        var result = await new OpenXmlMetadataExtractor().ExtractAsync(
            Entry(path),
            1024 * 1024,
            25,
            CancellationToken.None);

        Assert.Contains(result.Fields, field => field.Name == "Sheet count" && field.Value == "2");
        Assert.Equal(2, result.Fields.Count(field => field.Name == "Sheet name"));
        Assert.Contains("finance forecast", result.NativeText, StringComparison.Ordinal);
    }

    /// <summary>Verifies a highly compressed oversized XML part is rejected before decompression can expand it.</summary>
    [Fact]
    public async Task OpenXmlExtractor_CompressedOversizedPart_IsSkipped()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.PathFor("compressed-bomb.docx");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "word/document.xml", new string('x', 4_000_001));
        }

        var result = await new OpenXmlMetadataExtractor().ExtractAsync(
            Entry(path),
            1024 * 1024,
            25,
            CancellationToken.None);

        Assert.Null(result.NativeText);
        Assert.False(result.HasReliableNativeText);
    }

    /// <summary>Verifies traversal-shaped archive names cannot substitute for an approved Open XML part.</summary>
    [Fact]
    public async Task OpenXmlExtractor_TraversalShapedEntry_IsIgnored()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.PathFor("traversal.docx");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            WriteEntry(
                archive,
                "../word/document.xml",
                "<w:document xmlns:w=\"urn:w\"><w:t>must not be indexed</w:t></w:document>");
        }

        var result = await new OpenXmlMetadataExtractor().ExtractAsync(
            Entry(path),
            1024 * 1024,
            25,
            CancellationToken.None);

        Assert.Null(result.NativeText);
        Assert.DoesNotContain(result.Fields, field => field.Value.Contains("must not", StringComparison.Ordinal));
    }

    /// <summary>Verifies nested archive payloads are not recursively opened by document extraction.</summary>
    [Fact]
    public async Task OpenXmlExtractor_DeepNestedArchive_IsIgnored()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.PathFor("nested.docx");
        using var nestedStream = new MemoryStream();
        using (var nested = new ZipArchive(nestedStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                nested,
                "word/document.xml",
                "<w:document xmlns:w=\"urn:w\"><w:t>nested private text</w:t></w:document>");
        }

        nestedStream.Position = 0;
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var nestedEntry = archive.CreateEntry(
                string.Join('/', Enumerable.Repeat("nested", 32)) + "/payload.docx");
            using var output = nestedEntry.Open();
            await nestedStream.CopyToAsync(output);
        }

        var result = await new OpenXmlMetadataExtractor().ExtractAsync(
            Entry(path),
            1024 * 1024,
            25,
            CancellationToken.None);

        Assert.Null(result.NativeText);
        Assert.DoesNotContain(result.Fields, field => field.Value.Contains("nested private", StringComparison.Ordinal));
    }

    /// <summary>Verifies PNG dimensions are read from a bounded header.</summary>
    [Fact]
    public async Task ImageExtractor_Png_ReadsDimensions()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.PathFor("image.png");
        var bytes = new byte[24];
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        signature.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), 640);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), 480);
        await File.WriteAllBytesAsync(path, bytes);

        var result = await new ImageMetadataExtractor().ExtractAsync(
            Entry(path),
            1024,
            25,
            CancellationToken.None);

        Assert.Contains(result.Fields, field => field.Name == "Image width" && field.Value == "640");
        Assert.Contains(result.Fields, field => field.Name == "Image height" && field.Value == "480");
    }

    /// <summary>Verifies malformed format-specific metadata does not stop other applicable extractors.</summary>
    [Fact]
    public async Task MetadataPipeline_MalformedOpenXml_PreservesFilesystemMetadata()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.PathFor("broken.docx");
        await File.WriteAllTextAsync(path, "not a zip package");
        var pipeline = new MetadataExtractionPipeline(
            [new FilesystemMetadataExtractor(), new OpenXmlMetadataExtractor()]);

        var result = await pipeline.ExtractAsync(Entry(path), 1024, 25, CancellationToken.None);

        Assert.Contains(result.Fields, field =>
            field.Name == "File name" &&
            field.Provenance == ContentProvenance.Filesystem);
        Assert.Contains(result.Warnings, warning => warning.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Verifies one readable page cannot suppress OCR for another insufficient PDF page.</summary>
    [Fact]
    public async Task MetadataPipeline_MixedPdf_DoesNotClaimAllNativeTextIsReliable()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.PathFor("mixed.pdf");
        await File.WriteAllBytesAsync(path, "%PDF-1.7"u8.ToArray());
        var pages = new[]
        {
            new PdfPageText(1, "Readable native text for a complete invoice document page.", true),
            new PdfPageText(2, "scan", false),
        };
        var pipeline = new MetadataExtractionPipeline([new FixedPdfExtractor(pages)]);

        var result = await pipeline.ExtractAsync(Entry(path), 1024, 25, CancellationToken.None);

        Assert.False(result.HasReliableNativeText);
        Assert.Equal(2, result.PdfPages.Count);
    }

    /// <summary>Diagnostic page limits do not truncate the feature's configured native-page context.</summary>
    [Fact]
    public async Task MetadataPipeline_MoreThanDiagnosticPageLimit_PreservesConfiguredPages()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.PathFor("large.pdf");
        await File.WriteAllBytesAsync(path, "%PDF-1.7"u8.ToArray());
        var pages = Enumerable.Range(1, DiagnosticLimits.MaximumPageRecords + 20)
            .Select(page => new PdfPageText(page, $"Readable native page {page} with sufficient text.", true))
            .ToArray();
        var pipeline = new MetadataExtractionPipeline([new FixedPdfExtractor(pages)]);

        var result = await pipeline.ExtractAsync(
            Entry(path),
            1024,
            DiagnosticLimits.MaximumPageRecords + 20,
            CancellationToken.None);

        Assert.Equal(DiagnosticLimits.MaximumPageRecords + 20, result.PdfPages.Count);
        Assert.False(result.WasTruncated);
    }

    /// <summary>Verifies OCR disabled state rejects work before the engine is invoked.</summary>
    [Fact]
    public async Task OcrService_Disabled_DoesNotInvokeEngine()
    {
        var engine = new FakeOcrEngine();
        var service = new OcrService(new Configuration(new ContentSettings()), engine);

        var result = await service.RecognizeAsync(Request("C:\\known.png"), CancellationToken.None);

        Assert.Equal(OcrStatus.Skipped, result.Status);
        Assert.Equal(OcrFailureCategory.Disabled, result.FailureCategory);
        Assert.Equal(0, engine.RecognizeCount);
        Assert.Equal(0, engine.DetectCount);
    }

    /// <summary>Verifies reliable native text skips an enabled engine under the safe default policy.</summary>
    [Fact]
    public async Task OcrService_NativeTextAvailable_SkipsEngine()
    {
        var engine = new FakeOcrEngine();
        var service = new OcrService(
            new Configuration(new ContentSettings { OcrEnabled = true }),
            engine);

        var result = await service.RecognizeAsync(
            Request("C:\\known.pdf") with
            {
                HasReliableNativeText = true,
                PdfPages =
                [
                    new PdfPageText(1, "Reliable native text for this complete document page.", true),
                ],
            },
            CancellationToken.None);

        Assert.Equal(OcrStatus.Skipped, result.Status);
        Assert.Equal(0, engine.RecognizeCount);
        var page = Assert.Single(result.Pages);
        Assert.Equal(OcrPageTextSource.NativeText, page.TextSource);
        Assert.Equal(OcrStatus.Skipped, page.Status);
    }

    /// <summary>Verifies the file-size bound is checked before invoking a fake local engine.</summary>
    [Fact]
    public async Task OcrService_OversizedInput_DoesNotInvokeEngine()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.PathFor("large.png");
        await File.WriteAllBytesAsync(path, new byte[2048]);
        var engine = new FakeOcrEngine();
        var service = new OcrService(
            new Configuration(new ContentSettings { OcrEnabled = true, MaximumFileSizeMiB = 1 }),
            engine);

        var result = await service.RecognizeAsync(
            Request(path) with { MaximumFileBytes = 1024 },
            CancellationToken.None);

        Assert.Equal(OcrFailureCategory.FileTooLarge, result.FailureCategory);
        Assert.Equal(0, engine.RecognizeCount);
    }

    /// <summary>Verifies caller cancellation propagates through the OCR abstraction.</summary>
    [Fact]
    public async Task OcrService_Cancelled_PropagatesCancellation()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.PathFor("cancel.png");
        await File.WriteAllBytesAsync(path, [1]);
        var engine = new FakeOcrEngine { Block = true };
        var service = new OcrService(
            new Configuration(new ContentSettings { OcrEnabled = true }),
            engine);
        using var cancellation = new CancellationTokenSource();

        var running = service.RecognizeAsync(Request(path), cancellation.Token);
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    /// <summary>Verifies unchanged files reuse application-owned content instead of repeating OCR.</summary>
    [Fact]
    public async Task ContentIndexing_UnchangedSource_ReusesCache()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.PathFor("scan.png");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var configuration = new Configuration(new ContentSettings
        {
            MetadataExtractionEnabled = true,
            OcrEnabled = true,
        });
        var engine = new FakeOcrEngine();
        var store = new MemoryContentStore();
        var indexing = new ContentIndexingService(
            configuration,
            new MetadataExtractionPipeline([new FilesystemMetadataExtractor()]),
            new OcrService(configuration, engine),
            store,
            new Logging());
        var file = Entry(path);

        var first = await indexing.IndexAsync([file], CancellationToken.None);
        var second = await indexing.IndexAsync([file], CancellationToken.None);

        Assert.Equal(1, first.IndexedCount);
        Assert.Equal(1, second.CacheHitCount);
        Assert.Equal(1, engine.RecognizeCount);
        Assert.Single(await store.ListAsync(CancellationToken.None));
    }

    /// <summary>Verifies legacy cache records are stale-but-readable and accepted user tags survive reprocessing.</summary>
    [Fact]
    public async Task ContentIndexing_LegacyFingerprint_ReprocessesAndPreservesAcceptedUserTags()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.PathFor("scan.png");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var configuration = new Configuration(new ContentSettings
        {
            MetadataExtractionEnabled = true,
            OcrEnabled = true,
        });
        var store = new MemoryContentStore();
        var source = new FileInfo(path);
        var userTag = new TagAssociation(
            "tag:user:finance",
            Path.GetFullPath(path),
            "Finance",
            "finance",
            "User",
            TagSource.UserApproved,
            TagAcceptanceState.Accepted,
            "User-created",
            DateTimeOffset.UnixEpoch);
        await store.UpsertAsync(
            new ContentRecord(
                Path.GetFullPath(path),
                source.Length,
                new DateTimeOffset(source.LastWriteTimeUtc, TimeSpan.Zero),
                DateTimeOffset.UnixEpoch,
                [],
                null,
                null,
                OcrStatus.Skipped,
                null,
                [])
            {
                Tags = [userTag],
                ExtractionFingerprint = null,
            },
            CancellationToken.None);
        var engine = new FakeOcrEngine();
        var indexing = new ContentIndexingService(
            configuration,
            new MetadataExtractionPipeline([new FilesystemMetadataExtractor()]),
            new OcrService(configuration, engine),
            store,
            new Logging());

        var result = await indexing.IndexAsync([Entry(path)], CancellationToken.None);

        Assert.Equal(1, result.IndexedCount);
        var updated = Assert.Single(await store.ListAsync(CancellationToken.None));
        Assert.NotNull(updated.ExtractionFingerprint);
        Assert.Contains(updated.Tags, tag =>
            tag.Source == TagSource.UserApproved &&
            tag.NormalizedValue == "finance");
    }

    /// <summary>Verifies the JSON content store recovers from malformed legacy/local data and writes a valid replacement.</summary>
    [Fact]
    public async Task JsonContentStore_MalformedFile_RecoversWithAtomicReplacement()
    {
        using var temporary = new TemporaryDirectory();
        var storePath = temporary.PathFor("content.json");
        await File.WriteAllTextAsync(storePath, "{ broken");
        var store = new JsonContentStore(storePath, new Logging());

        Assert.Empty(await store.ListAsync(CancellationToken.None));
        var record = new ContentRecord(
            Path.GetFullPath(temporary.PathFor("known.txt")),
            0,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            [new ExtractedMetadataField("File name", "known.txt", ContentProvenance.Filesystem)],
            null,
            null,
            OcrStatus.Skipped,
            null,
            []);
        await store.UpsertAsync(record, CancellationToken.None);

        Assert.Single(await store.ListAsync(CancellationToken.None));
        Assert.Empty(Directory.GetFiles(temporary.Path, "*.tmp"));
    }

    private static OcrRequest Request(string path) => new(
        path,
        "eng",
        50L * 1024 * 1024,
        25,
        TimeSpan.FromSeconds(30),
        false);

    private static FileEntry Entry(string path)
    {
        var info = new FileInfo(path);
        return new FileEntry(path, new FileMetadata(
            info.Name,
            info.Extension,
            info.Exists ? info.Length : null,
            DateTimeOffset.UnixEpoch,
            info.Exists ? new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero) : null,
            DateTimeOffset.UnixEpoch,
            FileAttributes.Normal));
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
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

    private sealed class FixedPdfExtractor(IReadOnlyList<PdfPageText> pages) : IMetadataExtractor
    {
        public bool Supports(string normalizedExtension) =>
            normalizedExtension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

        public Task<MetadataExtractionResult> ExtractAsync(
            FileEntry file,
            long maximumInputBytes,
            int maximumPages,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MetadataExtractionResult(
                [],
                string.Join(' ', pages.Select(page => page.NativeText)),
                pages.All(page => page.HasReliableNativeText),
                pages.Count,
                [])
            {
                PdfPages = pages,
            });
    }

    private sealed class FakeOcrEngine : IOcrEngine
    {
        public int DetectCount { get; private set; }
        public int RecognizeCount { get; private set; }
        public bool Block { get; init; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<OcrCapability> DetectCapabilityAsync(CancellationToken cancellationToken)
        {
            DetectCount++;
            return Task.FromResult(new OcrCapability(true, "fake", "1", [".png"], false, "Available"));
        }

        public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
        {
            RecognizeCount++;
            Started.TrySetResult(true);
            if (Block)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new OcrResult(
                OcrStatus.Completed,
                "recognized local text",
                request.Language,
                0.8,
                1,
                [],
                OcrFailureCategory.None,
                TimeSpan.FromMilliseconds(1),
                "fake",
                "1",
                "Completed");
        }
    }

    private sealed class MemoryContentStore : IContentStore
    {
        private readonly Dictionary<string, ContentRecord> _records = new(StringComparer.OrdinalIgnoreCase);
        public Task<ContentRecord?> GetAsync(string fullPath, CancellationToken cancellationToken) =>
            Task.FromResult(_records.GetValueOrDefault(Path.GetFullPath(fullPath)));
        public Task<IReadOnlyList<ContentRecord>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ContentRecord>>(_records.Values.ToArray());
        public Task UpsertAsync(ContentRecord record, CancellationToken cancellationToken)
        {
            _records[Path.GetFullPath(record.FullPath)] = record;
            return Task.CompletedTask;
        }
        public Task RemoveMissingAsync(IReadOnlyCollection<string> knownPaths, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken)
        {
            _records.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class Logging : ILoggingService
    {
        public void Initialize(LogLevel minimumLevel) { }
        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;
        public void Dispose() { }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"opensorse-content-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public string PathFor(string fileName) => System.IO.Path.Combine(Path, fileName);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
