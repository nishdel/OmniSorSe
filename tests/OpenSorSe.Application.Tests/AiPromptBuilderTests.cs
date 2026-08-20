using System.Text.Json;
using OpenSorSe.Application.AI;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies deterministic, bounded, narrowly scoped small-model prompts.</summary>
public sealed class AiPromptBuilderTests
{
    private readonly AiPromptBuilder _builder = new();

    /// <summary>The rename prompt exposes only labelled minimum evidence and the exact schema.</summary>
    [Fact]
    public void BuildFileRenamePrompt_IsLabelledBoundedAndPathFree()
    {
        var file = CreateFile("file:1", "invoice \"draft\".pdf", "C:\\Private\\content-secret\\invoice.pdf");

        var result = _builder.BuildFileRenamePrompt(
            new AiFileRenameRequest(file, ["sibling\\name.pdf", "z.pdf"]),
            EmptyPreferences());

        using var document = JsonDocument.Parse(result.Prompt);
        var root = document.RootElement;
        var input = root.GetProperty("input");
        Assert.Equal(AiPromptBuilder.FileRenameTaskId, result.TaskId);
        Assert.Equal(AiPromptBuilder.FileRenameTaskId, root.GetProperty("taskId").GetString());
        Assert.Equal(AiPromptTemplates.FileRenamePromptVersion, root.GetProperty("promptVersion").GetString());
        Assert.Equal("item-001", input.GetProperty("sourceFileId").GetString());
        Assert.Equal("invoice \"draft\"", input.GetProperty("currentStem").GetString());
        Assert.Equal(".pdf", input.GetProperty("preservedExtension").GetString());
        Assert.Equal("-", input.GetProperty("separator").GetString());
        Assert.Equal("yyyy-MM-dd", input.GetProperty("dateFormat").GetString());
        Assert.Equal(
            AiStructuredOutputContracts.GetSchemaJson(AiSuggestionKind.FileRename),
            root.GetProperty("responseSchema").GetRawText());
        Assert.Equal(
            AiStructuredOutputContracts.GetSystemPrompt(AiSuggestionKind.FileRename),
            result.SystemPrompt);
        Assert.Equal("file:1", Assert.Single(result.SourceMappings).KnownSourceId);
        Assert.DoesNotContain("content-secret", result.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(file.FullPath, result.Prompt, StringComparison.Ordinal);
        Assert.Contains("no_suggestion", result.Prompt, StringComparison.Ordinal);
        Assert.Contains("no Markdown or prose", result.SystemPrompt, StringComparison.Ordinal);
    }

    /// <summary>Grounded organization evidence is bounded, labelled, and contains no source path.</summary>
    [Fact]
    public void BuildFileRenamePrompt_GroundedEvidence_IsBoundedAndReviewable()
    {
        var file = CreateFile("file:1", "scan.pdf", "C:\\Private\\scan.pdf");
        var request = new AiFileRenameRequest(file, [])
        {
            GroundedEvidence = [
                new AiOrganizationEvidence("Document Type", "Invoice", "Accepted"),
                new AiOrganizationEvidence("Theme", "Finance", "Strong deterministic"),
                new AiOrganizationEvidence("User Tag", "Review", "User-created"),
                new AiOrganizationEvidence("Theme", "Insurance", "Accepted"),
                new AiOrganizationEvidence("Theme", "Excluded fifth value", "Accepted"),
            ],
        };

        var result = _builder.BuildFileRenamePrompt(request, EmptyPreferences());

        using var document = JsonDocument.Parse(result.Prompt);
        var evidence = document.RootElement.GetProperty("input").GetProperty("groundedClassificationEvidence");
        Assert.Equal(4, evidence.GetArrayLength());
        Assert.Equal("Invoice", evidence[0].GetProperty("value").GetString());
        Assert.Equal("Accepted", evidence[0].GetProperty("authority").GetString());
        Assert.DoesNotContain("Excluded fifth value", result.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(file.FullPath, result.Prompt, StringComparison.Ordinal);
    }

    /// <summary>Equivalent folder evidence produces byte-identical prompts and stable opaque identities.</summary>
    [Fact]
    public void BuildFolderStructurePrompt_ReorderedInputs_IsDeterministic()
    {
        var first = _builder.BuildFolderStructurePrompt(
            new AiFolderStructureRequest(
                [CreateFile("b", "b.pdf"), CreateFile("a", "a.pdf")],
                ["Zeta", "Alpha", "Alpha"]),
            EmptyPreferences());
        var second = _builder.BuildFolderStructurePrompt(
            new AiFolderStructureRequest(
                [CreateFile("a", "a.pdf"), CreateFile("b", "b.pdf")],
                ["Alpha", "Zeta"]),
            EmptyPreferences());

        Assert.Equal(first.Prompt, second.Prompt);
        Assert.Equal(["a", "b"], first.IncludedSourceIds);
        Assert.Equal(["item-001", "item-002"], first.SourceMappings.Select(mapping => mapping.RequestSourceId));
        Assert.Equal(["a", "b"], first.SourceMappings.Select(mapping => mapping.KnownSourceId));
        Assert.Contains("assign every supplied opaque file ID", first.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\Private", first.Prompt, StringComparison.Ordinal);
    }

    /// <summary>Folder records and folder-name choices are deterministically bounded before transport.</summary>
    [Fact]
    public void BuildFolderStructurePrompt_LargeContext_IsBoundedAndStable()
    {
        var files = Enumerable.Range(0, 40)
            .Reverse()
            .Select(index => CreateFile($"file:{index:D2}", $"file-{index:D2}.pdf"))
            .ToArray();
        var folders = Enumerable.Range(0, 45).Select(index => $"Folder {index:D2}").ToArray();

        var result = _builder.BuildFolderStructurePrompt(
            new AiFolderStructureRequest(files, folders),
            EmptyPreferences());

        Assert.True(result.WasInputBounded);
        Assert.Equal(40, result.TotalInputCount);
        Assert.Equal(AiPromptLimits.MaximumFolderStructureFiles, result.IncludedInputCount);
        Assert.Equal(40 - AiPromptLimits.MaximumFolderStructureFiles, result.OmittedInputCount);
        Assert.Equal("file:00", result.IncludedSourceIds[0]);
        Assert.Equal("file:11", result.IncludedSourceIds[^1]);
        using var document = JsonDocument.Parse(result.Prompt);
        var input = document.RootElement.GetProperty("input");
        Assert.Equal(
            AiPromptLimits.MaximumFolderStructureFiles,
            input.GetProperty("files").GetArrayLength());
        Assert.True(
            input.GetProperty("allowedFolderNames").GetArrayLength() <=
            AiPromptLimits.MaximumAllowedFolderNames);
        Assert.Contains("Other", result.AllowedFolderNames, StringComparer.Ordinal);
        Assert.Equal(AiResponseLimits.MaximumFolders, input.GetProperty("maximumFolders").GetInt32());
        Assert.Equal(AiResponseLimits.MaximumFolderDepth, input.GetProperty("maximumDepth").GetInt32());
    }

    /// <summary>The uncertain-classification fallback is never displaced by a full allowed-name budget.</summary>
    [Fact]
    public void BuildFolderStructurePrompt_FullAllowedNameBudget_AlwaysIncludesOther()
    {
        var existing = Enumerable.Range(1, AiPromptLimits.MaximumExistingFolderNames)
            .Select(index => $"Category {index:D2}")
            .ToArray();
        var preferred = Enumerable.Range(1, AiPromptLimits.MaximumPreferenceValues)
            .Select(index => $"Preferred {index:D2}")
            .ToArray();

        var result = _builder.BuildFolderStructurePrompt(
            new AiFolderStructureRequest([CreateFile("known:1", "invoice.pdf")], existing),
            new AiPreferenceSummary([], preferred, [], []));

        Assert.Contains("Other", result.AllowedFolderNames, StringComparer.Ordinal);
        Assert.True(result.AllowedFolderNames.Count <= AiPromptLimits.MaximumAllowedFolderNames);
    }

    /// <summary>Nearby names are converted to bounded stems instead of leaking a directory listing.</summary>
    [Fact]
    public void BuildFileRenamePrompt_TooManySiblings_BoundsAndReportsInput()
    {
        var siblings = Enumerable.Range(0, 50)
            .Select(index => $"nearby-{index:D2}.pdf")
            .Reverse()
            .ToArray();

        var result = _builder.BuildFileRenamePrompt(
            new AiFileRenameRequest(CreateFile("file:1", "invoice.pdf"), siblings),
            EmptyPreferences());

        Assert.True(result.WasInputBounded);
        using var document = JsonDocument.Parse(result.Prompt);
        var values = document.RootElement.GetProperty("input").GetProperty("nearbyNameStems");
        Assert.Equal(AiPromptLimits.MaximumSiblingFileNames, values.GetArrayLength());
        Assert.Equal("nearby-00", values[0].GetString());
    }

    /// <summary>Unsafe or overlong folder context is omitted and the bounding decision is disclosed.</summary>
    [Fact]
    public void BuildFolderStructurePrompt_UnsafeFolderContext_IsNotSent()
    {
        var unsafeName = new string('f', 300);
        var result = _builder.BuildFolderStructurePrompt(
            new AiFolderStructureRequest(
                [CreateFile("file:1", "invoice.pdf")],
                [unsafeName, "..", "C:\\Private"]),
            EmptyPreferences());

        Assert.True(result.WasInputBounded);
        Assert.DoesNotContain(unsafeName, result.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\Private", result.Prompt, StringComparison.Ordinal);
        Assert.Contains("Other", result.AllowedFolderNames);
    }

    /// <summary>Repair contains the same task/schema, the exact concise error, and the bounded prior response.</summary>
    [Fact]
    public void BuildRepairPrompt_ContainsRequiredSameTaskInputs()
    {
        var original = _builder.BuildFileRenamePrompt(
            new AiFileRenameRequest(CreateFile("file:1", "invoice.pdf"), []),
            EmptyPreferences());

        var repair = _builder.BuildRepairPrompt(
            original,
            "```json\n{}\n```",
            "The response used Markdown.");

        using var document = JsonDocument.Parse(repair.Prompt);
        var input = document.RootElement.GetProperty("input");
        Assert.Equal(original.TaskId, input.GetProperty("originalTaskId").GetString());
        Assert.Equal("```json\n{}\n```", input.GetProperty("priorResponse").GetString());
        Assert.Equal("The response used Markdown.", input.GetProperty("validationError").GetString());
        Assert.Equal(
            AiStructuredOutputContracts.GetSchemaJson(AiSuggestionKind.FileRename),
            document.RootElement.GetProperty("responseSchema").GetRawText());
        Assert.Equal(AiStructuredOutputContracts.RepairSystemPrompt, repair.SystemPrompt);
    }

    /// <summary>Extracted document text remains bounded, provenance-labelled, and path-free.</summary>
    [Fact]
    public void BuildDocumentInterpretationPrompt_BoundsTextAndOmitsPath()
    {
        var request = new AiDocumentTextRequest(
            "known:1",
            "invoice.pdf",
            null,
            null,
            Enumerable.Range(1, 20)
                .Select(page => new OcrPageResult(
                    page,
                    OcrPageTextSource.Ocr,
                    OcrStatus.Completed,
                    new string('x', 2000),
                    null,
                    "OCR"))
                .ToArray());

        var result = _builder.BuildDocumentInterpretationPrompt(request);

        Assert.True(result.WasInputBounded);
        Assert.Equal(AiPromptBuilder.DocumentInterpretationTaskId, result.TaskId);
        Assert.DoesNotContain("C:\\", result.Prompt, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(result.Prompt);
        var input = document.RootElement.GetProperty("input");
        Assert.Equal("item-001", input.GetProperty("sourceFileId").GetString());
        Assert.True(
            input.GetProperty("extractedTextPages").GetArrayLength() <=
            AiPromptLimits.MaximumDocumentTextPages);
        Assert.Contains("Do not provide legal", result.Prompt, StringComparison.Ordinal);
    }

    private static AiPreferenceSummary EmptyPreferences() => new([], [], [], []);

    private static ResultFile CreateFile(string id, string name, string? path = null) => new(
        id,
        path ?? $"C:\\Private\\{name}",
        name,
        Path.GetExtension(name),
        10,
        DateTimeOffset.UnixEpoch,
        FileCategory.Document,
        "Document",
        DuplicateStatus.Unique,
        null,
        false);
}
