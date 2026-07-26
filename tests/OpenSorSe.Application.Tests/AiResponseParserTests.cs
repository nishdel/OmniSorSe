using System.Text.Json;
using OpenSorSe.Application.AI;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies exact whole-response validation of untrusted AI JSON.</summary>
public sealed class AiResponseParserTests
{
    private readonly AiResponseParser _parser = new();

    /// <summary>A grounded stem is accepted and the application appends the original extension.</summary>
    [Fact]
    public void ParseFileRename_ValidStem_PreservesExtensionOutsideModel()
    {
        var result = _parser.ParseFileRename(
            RenameJson("file:1", "invoice-draft", "0.51"),
            RenameRequest());

        var value = Assert.IsType<AiParsedFileRename>(result.Value);
        Assert.True(result.IsValid);
        Assert.False(result.IsNoSuggestion);
        Assert.Equal("invoice-draft", value.SuggestedStem);
        Assert.Equal("invoice-draft.pdf", value.SuggestedFileName);
        Assert.Equal(0.51, value.Confidence);
    }

    /// <summary>Explicit no-suggestion output is valid and non-actionable.</summary>
    [Fact]
    public void ParseFileRename_NoSuggestion_IsAcceptedWithoutValue()
    {
        var result = _parser.ParseFileRename(
            """{"taskId":"file-rename-v2","status":"no_suggestion","reason":"The current name is already clear."}""",
            RenameRequest());

        Assert.True(result.IsValid);
        Assert.True(result.IsNoSuggestion);
        Assert.Null(result.Value);
    }

    /// <summary>Malformed shapes are rejected and only diagnostic format failures are repairable.</summary>
    [Theory]
    [InlineData("", false)]
    [InlineData("{invalid", true)]
    [InlineData("```json\n{}\n```", true)]
    [InlineData("[]", true)]
    [InlineData("{\"taskId\":\"file-rename-v2\",\"status\":\"suggestion\",\"reason\":\"why\"}", true)]
    public void ParseFileRename_InvalidEnvelope_IsRejected(string json, bool repairable)
    {
        var result = _parser.ParseFileRename(json, RenameRequest());

        Assert.False(result.IsValid);
        Assert.Equal(repairable, result.CanRepair);
    }

    /// <summary>Identity, path, grounding, extension, collision, no-change, and type failures close the whole response.</summary>
    [Theory]
    [InlineData("other", "invoice-draft", "0.5", AiResponseFailureKind.UnsafeIdentity)]
    [InlineData("file:1", "../invoice", "0.5", AiResponseFailureKind.UnsafePath)]
    [InlineData("file:1", "C:\\invoice", "0.5", AiResponseFailureKind.UnsafePath)]
    [InlineData("file:1", "invoice.pdf", "0.5", AiResponseFailureKind.ModelMisuse)]
    [InlineData("file:1", "invented-subject", "0.5", AiResponseFailureKind.ModelMisuse)]
    [InlineData("file:1", "draft-invoice", "0.5", AiResponseFailureKind.ModelMisuse)]
    [InlineData("file:1", "invoice-draft", "\"high\"", AiResponseFailureKind.ModelMisuse)]
    public void ParseFileRename_UnsafeOrInconsistentValue_IsRejected(
        string sourceId,
        string stem,
        string confidence,
        AiResponseFailureKind expected)
    {
        var result = _parser.ParseFileRename(
            RenameJson(sourceId, stem, confidence),
            RenameRequest(["invoice-draft.pdf"]));

        Assert.False(result.IsValid);
        Assert.Equal(expected, result.FailureKind);
    }

    /// <summary>Unknown properties are rejected so prompt, schema, DTO, and validator stay exact.</summary>
    [Fact]
    public void ParseFileRename_UnknownProperty_IsRejected()
    {
        var result = _parser.ParseFileRename(
            RenameJson("file:1", "invoice-draft", "0.5", ",\"futureField\":\"x\""),
            RenameRequest());

        Assert.False(result.IsValid);
        Assert.True(result.CanRepair);
    }

    /// <summary>A declared parent-child graph and exact opaque assignment produce deterministic paths.</summary>
    [Fact]
    public void ParseFolderStructure_ValidHierarchy_IsAccepted()
    {
        var files = new[] { CreateFile("known:1", "invoice.pdf", "Invoice") };
        var mappings = new[] { new AiPromptSourceMapping("item-001", "known:1", "invoice.pdf") };

        var result = _parser.ParseFolderStructure(
            ValidFolderJson(),
            files,
            mappings,
            ["Finance", "Invoices", "Other"]);

        var value = Assert.IsType<AiParsedFolderStructure>(result.Value);
        Assert.Equal(["Finance", "Finance/Invoices"], value.Folders.Select(folder => folder.LogicalPath));
        Assert.Equal("Finance/Invoices", Assert.Single(value.Items).DestinationFolder);
        Assert.Equal("known:1", Assert.Single(value.Items).FileId);
    }

    /// <summary>Every supplied opaque ID must be assigned exactly once.</summary>
    [Fact]
    public void ParseFolderStructure_MissingAssignment_IsRejected()
    {
        var files = new[]
        {
            CreateFile("known:a", "a.pdf"),
            CreateFile("known:b", "b.pdf"),
        };
        var mappings = new[]
        {
            new AiPromptSourceMapping("item-001", "known:a", "a.pdf"),
            new AiPromptSourceMapping("item-002", "known:b", "b.pdf"),
        };
        const string missing =
            """{"taskId":"folder-structure-v2","status":"suggestion","folders":[{"folderId":"folder-001","name":"Document","parentFolderId":null}],"assignments":[{"sourceFileId":"item-001","folderId":"folder-001"}],"reason":"Group documents."}""";

        var result = _parser.ParseFolderStructure(
            missing,
            files,
            mappings,
            ["Document", "Other"]);

        Assert.False(result.IsValid);
        Assert.False(result.CanRepair);
    }

    /// <summary>A valid explicit folder no-suggestion response remains non-actionable.</summary>
    [Fact]
    public void ParseFolderStructure_NoSuggestion_IsAcceptedWithoutPlan()
    {
        var result = _parser.ParseFolderStructure(
            """{"taskId":"folder-structure-v2","status":"no_suggestion","reason":"Not enough metadata."}""",
            [CreateFile("file:1", "invoice.pdf")]);

        Assert.True(result.IsValid);
        Assert.True(result.IsNoSuggestion);
        Assert.Null(result.Value);
    }

    /// <summary>No-suggestion envelopes cannot smuggle actionable values past exact validation.</summary>
    [Theory]
    [InlineData("{\"taskId\":\"file-rename-v2\",\"status\":\"no_suggestion\",\"reason\":\"No change.\",\"suggestedStem\":\"other\"}", true)]
    [InlineData("{\"taskId\":\"folder-structure-v2\",\"status\":\"no_suggestion\",\"reason\":\"No plan.\",\"folders\":[]}", false)]
    public void ParseNoSuggestion_WithActionableProperties_IsRejected(string json, bool rename)
    {
        var isValid = rename
            ? _parser.ParseFileRename(json, RenameRequest()).IsValid
            : _parser.ParseFolderStructure(json, [CreateFile("file:1", "invoice.pdf")]).IsValid;

        Assert.False(isValid);
    }

    /// <summary>Oversized responses are rejected without a repair request.</summary>
    [Fact]
    public void ParseFileRename_OversizedResponse_IsHardFailure()
    {
        var json = "{\"padding\":\"" + new string('x', AiResponseLimits.MaximumStructuredResponseBytes) + "\"}";

        var result = _parser.ParseFileRename(json, RenameRequest());

        Assert.False(result.IsValid);
        Assert.False(result.CanRepair);
        Assert.Equal(AiResponseFailureKind.HardBound, result.FailureKind);
    }

    /// <summary>A complete document interpretation is validated and mapped to the known source.</summary>
    [Fact]
    public void ParseDocumentInterpretation_ValidResponse_IsAccepted()
    {
        const string json = """
            {"taskId":"document-text-interpretation-v1","status":"suggestion","sourceFileId":"item-001",
             "documentType":"Invoice","title":"Invoice","tags":["Invoice"],
             "dates":["2026-07-24"],"issuer":"Local Studio","suggestedFolder":"Invoice",
             "reason":"Explicit invoice fields are present.","confidence":0.71}
            """;

        var result = _parser.ParseDocumentInterpretation(
            json,
            DocumentRequest(),
            [new AiPromptSourceMapping("item-001", "known:1", "invoice.pdf")]);

        var value = Assert.IsType<AiParsedDocumentInterpretation>(result.Value);
        Assert.Equal("known:1", value.SourceFileId);
        Assert.Equal("Invoice", value.SuggestedFolder);
        Assert.Equal(["invoice"], value.Tags.Select(tag => tag.NormalizedValue));
    }

    /// <summary>Unsafe interpretation identity, date, path, and extra values fail closed.</summary>
    [Theory]
    [InlineData("""{"taskId":"document-text-interpretation-v1","status":"suggestion","sourceFileId":"other","documentType":"Invoice","title":null,"tags":[],"dates":[],"issuer":null,"suggestedFolder":null,"reason":"why","confidence":0.5}""")]
    [InlineData("""{"taskId":"document-text-interpretation-v1","status":"suggestion","sourceFileId":"item-001","documentType":"Invoice","title":null,"tags":[],"dates":["24/07/2026"],"issuer":null,"suggestedFolder":null,"reason":"why","confidence":0.5}""")]
    [InlineData("""{"taskId":"document-text-interpretation-v1","status":"suggestion","sourceFileId":"item-001","documentType":"Invoice","title":null,"tags":[],"dates":[],"issuer":null,"suggestedFolder":"../outside","reason":"why","confidence":0.5}""")]
    [InlineData("""{"taskId":"document-text-interpretation-v1","status":"no_suggestion","reason":"why","tags":[]}""")]
    public void ParseDocumentInterpretation_UnsafeResponse_IsRejected(string json)
    {
        var result = _parser.ParseDocumentInterpretation(
            json,
            DocumentRequest(),
            [new AiPromptSourceMapping("item-001", "known:1", "invoice.pdf")]);

        Assert.False(result.IsValid);
    }

    /// <summary>A reserved folder identity is a semantic safety rejection and cannot trigger repair.</summary>
    [Fact]
    public void ParseDocumentInterpretation_ReservedFolder_IsNotRepairable()
    {
        const string json =
            """{"taskId":"document-text-interpretation-v1","status":"suggestion","sourceFileId":"item-001","documentType":"Invoice","title":null,"tags":[],"dates":[],"issuer":null,"suggestedFolder":"CON","reason":"why","confidence":0.5}""";

        var result = _parser.ParseDocumentInterpretation(
            json,
            DocumentRequest(),
            [new AiPromptSourceMapping("item-001", "known:1", "invoice.pdf")]);

        Assert.False(result.IsValid);
        Assert.False(result.CanRepair);
        Assert.Equal(AiResponseFailureKind.ModelMisuse, result.FailureKind);
    }

    private static string RenameJson(string sourceId, string stem, string confidence, string extra = "")
    {
        var escaped = stem
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return
            $"{{\"taskId\":\"file-rename-v2\",\"status\":\"suggestion\",\"sourceFileId\":\"{sourceId}\",\"suggestedStem\":\"{escaped}\",\"reason\":\"A bounded reason.\",\"confidence\":{confidence}{extra}}}";
    }

    private static string ValidFolderJson() => """
        {
          "taskId":"folder-structure-v2",
          "status":"suggestion",
          "folders":[
            {"folderId":"folder-002","name":"Invoices","parentFolderId":"folder-001"},
            {"folderId":"folder-001","name":"Finance","parentFolderId":null}
          ],
          "assignments":[{"sourceFileId":"item-001","folderId":"folder-002"}],
          "reason":"Group the supplied invoice."
        }
        """;

    private static AiFileRenameRequest RenameRequest(IReadOnlyList<string>? siblings = null) =>
        new(CreateFile("file:1", "draft-invoice.pdf", "Invoice"), siblings ?? []);

    private static AiDocumentTextRequest DocumentRequest() =>
        new("known:1", "invoice.pdf", "Invoice date 2026-07-24 and issuer Local Studio.", null, []);

    private static ResultFile CreateFile(string id, string name, string classification = "Document") => new(
        id,
        $"C:\\Private\\{name}",
        name,
        Path.GetExtension(name),
        10,
        DateTimeOffset.UnixEpoch,
        FileCategory.Document,
        classification,
        DuplicateStatus.Unique,
        null,
        false);
}
