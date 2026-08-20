using System.Text;
using System.Diagnostics;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.SmartTags;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Tests;

/// <summary>Validates bounded local Smart Tag taxonomy, classification, and simple-text evidence.</summary>
public sealed class SmartTagClassificationTests
{
    /// <summary>User labels normalize deterministically while punctuation-only input is rejected.</summary>
    [Fact]
    public void UserTagInputIsBoundedLanguagePreservingAndRequiresMeaningfulText()
    {
        Assert.Equal("personal-project-ä", SmartTagUserInput.NormalizeCanonicalKey("  Personal   Project Ä  "));
        Assert.Throws<ArgumentException>(() => SmartTagUserInput.NormalizeCanonicalKey("!!!"));
        Assert.Throws<ArgumentException>(() => SmartTagUserInput.NormalizeDisplayName(new string('x', SmartTagLimits.MaximumDisplayNameCharacters + 1)));
    }

    /// <summary>The built-in resources remain small, typed, canonical, and independently versioned.</summary>
    [Fact]
    public void BuiltInTaxonomyIsBoundedAndCanonical()
    {
        var taxonomy = SmartTagTaxonomy.LoadBuiltIn();

        Assert.Equal("1.0", taxonomy.Version);
        Assert.Equal("en", taxonomy.Locale);
        Assert.InRange(taxonomy.Definitions.Count(item => item.Type == SmartTagType.Theme), 8, 12);
        Assert.InRange(taxonomy.Definitions.Count(item => item.Type == SmartTagType.DocumentType), 15, 25);
        Assert.Equal(taxonomy.Definitions.Count, taxonomy.ById.Count);
        Assert.All(taxonomy.Definitions, item =>
        {
            Assert.StartsWith(item.Type == SmartTagType.Theme ? "theme." : "document-type.", item.TagId, StringComparison.Ordinal);
            Assert.Equal(taxonomy.Version, item.TaxonomyVersion);
            Assert.NotEmpty(item.Aliases);
        });
    }

    /// <summary>Duplicate canonical identities and ambiguous aliases fail closed.</summary>
    [Fact]
    public void TaxonomyRejectsDuplicateAliasesWithinOneType()
    {
        var first = Definition("theme.one", "one", "One", "shared");
        var second = Definition("theme.two", "two", "Two", "shared");

        Assert.Throws<InvalidDataException>(() => new SmartTagTaxonomy("test", "en", [first, second]));
    }

    /// <summary>Native content can produce explainable Strong Theme and Document Type classifications.</summary>
    [Fact]
    public async Task ClassifierUsesContentAndProducesBoundedStrongEvidence()
    {
        var classifier = new DeterministicSmartTagClassifier(SmartTagTaxonomy.LoadBuiltIn());

        var result = await classifier.ClassifyAsync(Request(
            fileName: "scan_0042.pdf",
            extractedText: "INVOICE Invoice number 2025-44. Amount due and payment terms for Example Company."));

        Assert.Equal(SmartTagClassificationState.Classified, result.State);
        Assert.Contains(result.Candidates, item => item.TagId == "document-type.invoice" && item.Confidence == ContentIntelligenceConfidence.Strong);
        Assert.Contains(result.Candidates, item => item.TagId == "theme.finance" && item.Confidence == ContentIntelligenceConfidence.Strong);
        Assert.All(result.Candidates, item => Assert.InRange(item.Evidence.Count, 1, SmartTagLimits.MaximumEvidencePerAssignment));
    }

    /// <summary>A misleading filename does not classify a file when actual content supplies no semantic support.</summary>
    [Fact]
    public async Task ClassifierDoesNotUseFilenameAsSoleSemanticAuthority()
    {
        var classifier = new DeterministicSmartTagClassifier(SmartTagTaxonomy.LoadBuiltIn());

        var result = await classifier.ClassifyAsync(Request(
            fileName: "invoice-finance-contract.txt",
            extractedText: null));

        Assert.Equal(SmartTagClassificationState.NoEvidence, result.State);
        Assert.Empty(result.Candidates);
    }

    /// <summary>Equally strong mutually exclusive document types remain unresolved.</summary>
    [Fact]
    public async Task ClassifierReportsConflictingDocumentTypes()
    {
        var classifier = new DeterministicSmartTagClassifier(SmartTagTaxonomy.LoadBuiltIn());

        var result = await classifier.ClassifyAsync(Request(
            fileName: "scan.pdf",
            extractedText: "INVOICE number 41. TOTAL PAID yesterday."));

        Assert.Equal(SmartTagClassificationState.ConflictingEvidence, result.State);
        Assert.DoesNotContain(result.Candidates, item => item.Type == SmartTagType.DocumentType);
    }

    /// <summary>Correlated derived evidence is not added when direct source evidence already matched.</summary>
    [Fact]
    public async Task ClassifierDoesNotDoubleCountDerivedEvidenceFromSameContent()
    {
        var classifier = new DeterministicSmartTagClassifier(SmartTagTaxonomy.LoadBuiltIn());
        var intelligence = new IndexedContentIntelligence
        {
            Topics = [Concept("Finance")],
            Keywords = ["invoice", "payment"],
            Entities = [],
            Provider = "test",
            ProviderVersion = "1",
            ProcessingFingerprint = "fp",
        };

        var result = await classifier.ClassifyAsync(Request(
            fileName: "scan.pdf",
            extractedText: "INVOICE number 41. Amount due by Friday.",
            intelligence: intelligence));

        var invoice = Assert.Single(result.Candidates, item => item.TagId == "document-type.invoice");
        Assert.DoesNotContain(invoice.Evidence, item => item.EvidenceKey.StartsWith("derived:", StringComparison.Ordinal));
    }

    /// <summary>Representative documents protect v2.7 discovery from accidental broad classifier drift.</summary>
    [Theory]
    [MemberData(nameof(RepresentativeClassificationCorpus))]
    public async Task RepresentativeCorpusProducesExpectedDocumentType(
        string fileName,
        string text,
        string expectedDocumentType)
    {
        var classifier = new DeterministicSmartTagClassifier(SmartTagTaxonomy.LoadBuiltIn());

        var result = await classifier.ClassifyAsync(Request(fileName, text));

        Assert.Equal(SmartTagClassificationState.Classified, result.State);
        Assert.Contains(result.Candidates, candidate =>
            candidate.TagId == expectedDocumentType &&
            candidate.Type == SmartTagType.DocumentType);
    }

    /// <summary>Provides bounded, deterministic real-world classification examples used as a release corpus.</summary>
    public static TheoryData<string, string, string> RepresentativeClassificationCorpus => new()
    {
        { "scan-0042.pdf", "INVOICE NUMBER 55. Amount due. Payment terms are net 30.", "document-type.invoice" },
        { "account.pdf", "ACCOUNT STATEMENT. Statement period January. Opening balance and closing balance.", "document-type.statement" },
        { "signed-copy.pdf", "THIS AGREEMENT states the terms and conditions and is signed by both parties.", "document-type.contract" },
        { "quarterly.pdf", "EXECUTIVE SUMMARY. Findings and recommendations for the quarter.", "document-type.report" },
        { "confirmation.pdf", "BOOKING CONFIRMATION. Reservation number AB42 for the hotel.", "document-type.booking" },
        { "paper.pdf", "ABSTRACT INTRODUCTION. Research methodology, methods and results, and references.", "document-type.research-paper" },
        { "scratch.md", "PERSONAL NOTES. Reminder and observations for tomorrow.", "document-type.notes" },
    };

    /// <summary>Filename and quoted sample terminology alone do not turn documentation into an invoice.</summary>
    [Theory]
    [InlineData("invoice-contract.pdf", "A sparse document with no supported classification evidence.")]
    [InlineData("template-guide.md", "This documentation contains sample invoice language for testing only.")]
    public async Task RepresentativeCorpusDoesNotPromoteWeakOrMisleadingInvoiceEvidence(
        string fileName,
        string text)
    {
        var classifier = new DeterministicSmartTagClassifier(SmartTagTaxonomy.LoadBuiltIn());

        var result = await classifier.ClassifyAsync(Request(fileName, text));

        Assert.DoesNotContain(result.Candidates, candidate => candidate.TagId == "document-type.invoice");
    }

    /// <summary>UTF-8 and Markdown content is read locally within configured bounds.</summary>
    [Theory]
    [InlineData(".txt")]
    [InlineData(".md")]
    public async Task PlainTextExtractorReadsBoundedUtf8(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "omnisorse-smart-tags-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "evidence" + extension);
        try
        {
            await File.WriteAllTextAsync(path, "# Budget\nFinance plan for Project Alpha in Stuttgart.", new UTF8Encoding(false));
            var result = await new PlainTextMetadataExtractor().ExtractAsync(Entry(path), 4096, 1, CancellationToken.None);

            Assert.Contains("Finance plan", result.RawNativeText, StringComparison.Ordinal);
            Assert.Contains("Project Alpha", result.NativeText, StringComparison.Ordinal);
            Assert.False(result.WasTruncated);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Oversized simple text is skipped without allocating or reading unbounded content.</summary>
    [Fact]
    public async Task PlainTextExtractorSkipsFilesOverInputBound()
    {
        var root = Path.Combine(Path.GetTempPath(), "omnisorse-smart-tags-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "large.txt");
        try
        {
            await File.WriteAllTextAsync(path, new string('x', 1024));
            var result = await new PlainTextMetadataExtractor().ExtractAsync(Entry(path), 32, 1, CancellationToken.None);

            Assert.Null(result.NativeText);
            Assert.Contains(result.Warnings, warning => warning.Contains("input-size bound", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Malformed bytes and cancellation remain isolated at the extractor boundary.</summary>
    [Fact]
    public async Task PlainTextExtractorRejectsMalformedUtf8AndHonorsCancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), "omnisorse-smart-tags-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "malformed.md");
        try
        {
            await File.WriteAllBytesAsync(path, [0xC3, 0x28]);
            var extractor = new PlainTextMetadataExtractor();
            await Assert.ThrowsAsync<DecoderFallbackException>(() =>
                extractor.ExtractAsync(Entry(path), 4096, 1, CancellationToken.None));

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                extractor.ExtractAsync(Entry(path), 4096, 1, cancellation.Token));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Guards deterministic classification against catastrophic throughput or allocation regression.</summary>
    [Fact]
    [Trait("Category", "PerformanceRegression")]
    public async Task ClassificationRemainsBoundedAcrossTenThousandFiles()
    {
        const int fileCount = 10_000;
        var classifier = new DeterministicSmartTagClassifier(SmartTagTaxonomy.LoadBuiltIn());
        var request = Request(
            fileName: "scan-0042.pdf",
            extractedText: "INVOICE number 2026-44. Amount due for technology equipment and payment terms.");
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();

        var classified = 0;
        for (var index = 0; index < fileCount; index++)
        {
            var result = await classifier.ClassifyAsync(request with
            {
                FileId = "file-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                InputFingerprint = "input-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            if (result.State == SmartTagClassificationState.Classified)
            {
                classified++;
            }
        }

        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(fileCount, classified);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Classifying {fileCount:N0} bounded files took {stopwatch.Elapsed}.");
        Assert.True(allocated < 750_000_000, $"Classifying {fileCount:N0} bounded files allocated {allocated:N0} bytes.");
    }

    private static SmartTagDefinition Definition(string id, string key, string display, string alias) => new()
    {
        TagId = id,
        Type = SmartTagType.Theme,
        CanonicalKey = key,
        DisplayName = display,
        TaxonomyVersion = "test",
        Origin = SmartTagOrigin.BuiltInTaxonomy,
        IsBuiltIn = true,
        Aliases = [alias],
    };

    private static SmartTagClassificationRequest Request(
        string fileName,
        string? extractedText,
        IndexedContentIntelligence? intelligence = null) => new()
        {
            FileId = "file-1",
            FileName = fileName,
            RelativePath = fileName,
            ExtractedText = extractedText,
            ContentIntelligence = intelligence,
            InputFingerprint = "test-input",
        };

    private static ContentConcept Concept(string display) => new()
    {
        Kind = ContentConceptKind.Topic,
        DisplayName = display,
        NormalizedValue = display.ToLowerInvariant(),
        Confidence = ContentIntelligenceConfidence.Strong,
        Provider = "test",
        ProviderVersion = "1",
        Origin = ContentIntelligenceOrigin.Deterministic,
    };

    private static FileEntry Entry(string path)
    {
        var info = new FileInfo(path);
        return new FileEntry(path, new FileMetadata(
            info.Name,
            info.Extension,
            info.Length,
            DateTimeOffset.UnixEpoch,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            DateTimeOffset.UnixEpoch,
            FileAttributes.Normal));
    }
}
