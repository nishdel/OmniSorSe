using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace OpenSorSe.Application.AI;

/// <summary>Keeps exact Ollama JSON Schemas and matching wire DTOs beside prompt contracts.</summary>
public static class AiStructuredOutputContracts
{
    /// <summary>Gets the short deterministic system prompt for one task.</summary>
    public static string GetSystemPrompt(AiSuggestionKind kind) => kind switch
    {
        AiSuggestionKind.FileRename =>
            "Suggest one filename stem from supplied evidence. Return one JSON object matching the schema; no Markdown or prose.",
        AiSuggestionKind.FolderStructure =>
            "Assign supplied opaque file IDs to a small declared folder hierarchy. Return one JSON object matching the schema; no Markdown or prose.",
        AiSuggestionKind.DocumentTextInterpretation =>
            "Extract review-only metadata from supplied text. Return one JSON object matching the schema; no Markdown or prose.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Gets the short deterministic system prompt for the single optional repair attempt.</summary>
    public const string RepairSystemPrompt =
        "Repair one prior structured response. Return one corrected JSON object matching the schema; no Markdown or prose.";

    /// <summary>Gets the exact stable schema JSON supplied to Ollama.</summary>
    public static string GetSchemaJson(AiSuggestionKind kind) => kind switch
    {
        AiSuggestionKind.FileRename => RenameSchema,
        AiSuggestionKind.FolderStructure => FolderSchema,
        AiSuggestionKind.DocumentTextInterpretation => DocumentSchema,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Gets the exact provider JSON Schema for one generation capability.</summary>
    public static JsonElement GetSchema(AiSuggestionKind kind)
    {
        using var document = JsonDocument.Parse(GetSchemaJson(kind));
        return document.RootElement.Clone();
    }

    /// <summary>Gets a stable lowercase SHA-256 identity for the exact schema supplied to Ollama.</summary>
    public static string GetSchemaSha256(AiSuggestionKind kind) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(GetSchemaJson(kind))))
            .ToLowerInvariant();

    private const string RenameSchema =
        """{"oneOf":[{"type":"object","additionalProperties":false,"required":["taskId","status","sourceFileId","suggestedStem","reason"],"properties":{"taskId":{"const":"file-rename-v2"},"status":{"const":"suggestion"},"sourceFileId":{"type":"string","minLength":1,"maxLength":32},"suggestedStem":{"type":"string","minLength":1,"maxLength":120},"reason":{"type":"string","minLength":1,"maxLength":160},"confidence":{"type":["number","null"],"minimum":0,"maximum":1}}},{"type":"object","additionalProperties":false,"required":["taskId","status","reason"],"properties":{"taskId":{"const":"file-rename-v2"},"status":{"const":"no_suggestion"},"reason":{"type":"string","minLength":1,"maxLength":160}}}]}""";

    private const string FolderSchema =
        """{"oneOf":[{"type":"object","additionalProperties":false,"required":["taskId","status","folders","assignments","reason"],"properties":{"taskId":{"const":"folder-structure-v2"},"status":{"const":"suggestion"},"folders":{"type":"array","minItems":1,"maxItems":8,"items":{"type":"object","additionalProperties":false,"required":["folderId","name","parentFolderId"],"properties":{"folderId":{"type":"string","pattern":"^folder-[0-9]{3}$"},"name":{"type":"string","minLength":1,"maxLength":64},"parentFolderId":{"type":["string","null"],"maxLength":10,"pattern":"^folder-[0-9]{3}$"}}}},"assignments":{"type":"array","minItems":1,"maxItems":12,"items":{"type":"object","additionalProperties":false,"required":["sourceFileId","folderId"],"properties":{"sourceFileId":{"type":"string","minLength":1,"maxLength":32},"folderId":{"type":"string","pattern":"^folder-[0-9]{3}$"}}}},"reason":{"type":"string","minLength":1,"maxLength":160}}},{"type":"object","additionalProperties":false,"required":["taskId","status","reason"],"properties":{"taskId":{"const":"folder-structure-v2"},"status":{"const":"no_suggestion"},"reason":{"type":"string","minLength":1,"maxLength":160}}}]}""";

    private const string DocumentSchema =
        """{"oneOf":[{"type":"object","additionalProperties":false,"required":["taskId","status","sourceFileId","documentType","title","tags","dates","issuer","suggestedFolder","reason"],"properties":{"taskId":{"const":"document-text-interpretation-v1"},"status":{"const":"suggestion"},"sourceFileId":{"type":"string","minLength":1,"maxLength":32},"documentType":{"type":["string","null"],"maxLength":96},"title":{"type":["string","null"],"maxLength":240},"tags":{"type":"array","maxItems":12,"items":{"type":"string","minLength":1,"maxLength":64}},"dates":{"type":"array","maxItems":8,"items":{"type":"string","pattern":"^[0-9]{4}-[0-9]{2}-[0-9]{2}$"}},"issuer":{"type":["string","null"],"maxLength":160},"suggestedFolder":{"type":["string","null"],"maxLength":64},"reason":{"type":"string","minLength":1,"maxLength":160},"confidence":{"type":["number","null"],"minimum":0,"maximum":1}}},{"type":"object","additionalProperties":false,"required":["taskId","status","reason"],"properties":{"taskId":{"const":"document-text-interpretation-v1"},"status":{"const":"no_suggestion"},"reason":{"type":"string","minLength":1,"maxLength":160}}}]}""";
}

/// <summary>Defines the exact case-sensitive file-rename response DTO.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiFileRenameResponseContract
{
    /// <summary>Gets the exact versioned task identity.</summary>
    [JsonPropertyName("taskId"), JsonPropertyOrder(0)]
    public string? TaskId { get; init; }

    /// <summary>Gets suggestion or no_suggestion.</summary>
    [JsonPropertyName("status"), JsonPropertyOrder(1)]
    public string? Status { get; init; }

    /// <summary>Gets the copied request-local opaque file identity.</summary>
    [JsonPropertyName("sourceFileId"), JsonPropertyOrder(2)]
    public string? SourceFileId { get; init; }

    /// <summary>Gets the extension-free suggested filename stem.</summary>
    [JsonPropertyName("suggestedStem"), JsonPropertyOrder(3)]
    public string? SuggestedStem { get; init; }

    /// <summary>Gets the bounded user-facing explanation.</summary>
    [JsonPropertyName("reason"), JsonPropertyOrder(4)]
    public string? Reason { get; init; }

    /// <summary>Gets an optional bounded confidence value.</summary>
    [JsonPropertyName("confidence"), JsonPropertyOrder(5)]
    public double? Confidence { get; init; }
}

/// <summary>Defines the exact case-sensitive folder-structure response DTO.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiFolderStructureResponseContract
{
    /// <summary>Gets the exact versioned task identity.</summary>
    [JsonPropertyName("taskId"), JsonPropertyOrder(0)]
    public string? TaskId { get; init; }

    /// <summary>Gets suggestion or no_suggestion.</summary>
    [JsonPropertyName("status"), JsonPropertyOrder(1)]
    public string? Status { get; init; }

    /// <summary>Gets the declared bounded folder records.</summary>
    [JsonPropertyName("folders"), JsonPropertyOrder(2)]
    public IReadOnlyList<AiFolderContract>? Folders { get; init; }

    /// <summary>Gets the exact opaque file assignments.</summary>
    [JsonPropertyName("assignments"), JsonPropertyOrder(3)]
    public IReadOnlyList<AiFolderAssignmentContract>? Assignments { get; init; }

    /// <summary>Gets the bounded user-facing explanation.</summary>
    [JsonPropertyName("reason"), JsonPropertyOrder(4)]
    public string? Reason { get; init; }
}

/// <summary>Defines one exact declared folder in the wire contract.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiFolderContract
{
    /// <summary>Gets the request-local folder identity.</summary>
    [JsonPropertyName("folderId"), JsonPropertyOrder(0)]
    public string? FolderId { get; init; }

    /// <summary>Gets one declared safe folder component.</summary>
    [JsonPropertyName("name"), JsonPropertyOrder(1)]
    public string? Name { get; init; }

    /// <summary>Gets the optional parent identity.</summary>
    [JsonPropertyName("parentFolderId"), JsonPropertyOrder(2)]
    public string? ParentFolderId { get; init; }
}

/// <summary>Defines one exact opaque source-to-folder assignment in the wire contract.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiFolderAssignmentContract
{
    /// <summary>Gets one copied request-local file identity.</summary>
    [JsonPropertyName("sourceFileId"), JsonPropertyOrder(0)]
    public string? SourceFileId { get; init; }

    /// <summary>Gets one declared folder identity.</summary>
    [JsonPropertyName("folderId"), JsonPropertyOrder(1)]
    public string? FolderId { get; init; }
}

/// <summary>Defines the exact case-sensitive extracted-text interpretation response DTO.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiDocumentInterpretationResponseContract
{
    /// <summary>Gets the exact versioned task identity.</summary>
    [JsonPropertyName("taskId"), JsonPropertyOrder(0)]
    public string? TaskId { get; init; }

    /// <summary>Gets suggestion or no_suggestion.</summary>
    [JsonPropertyName("status"), JsonPropertyOrder(1)]
    public string? Status { get; init; }

    /// <summary>Gets the copied request-local opaque file identity.</summary>
    [JsonPropertyName("sourceFileId"), JsonPropertyOrder(2)]
    public string? SourceFileId { get; init; }

    /// <summary>Gets a bounded document type supported by explicit text.</summary>
    [JsonPropertyName("documentType"), JsonPropertyOrder(3)]
    public string? DocumentType { get; init; }

    /// <summary>Gets a bounded title supported by explicit text.</summary>
    [JsonPropertyName("title"), JsonPropertyOrder(4)]
    public string? Title { get; init; }

    /// <summary>Gets bounded tags supported by explicit text.</summary>
    [JsonPropertyName("tags"), JsonPropertyOrder(5)]
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>Gets explicit ISO dates supported by text.</summary>
    [JsonPropertyName("dates"), JsonPropertyOrder(6)]
    public IReadOnlyList<string>? Dates { get; init; }

    /// <summary>Gets a bounded issuer supported by explicit text.</summary>
    [JsonPropertyName("issuer"), JsonPropertyOrder(7)]
    public string? Issuer { get; init; }

    /// <summary>Gets one optional safe relative folder component.</summary>
    [JsonPropertyName("suggestedFolder"), JsonPropertyOrder(8)]
    public string? SuggestedFolder { get; init; }

    /// <summary>Gets the bounded user-facing explanation.</summary>
    [JsonPropertyName("reason"), JsonPropertyOrder(9)]
    public string? Reason { get; init; }

    /// <summary>Gets an optional bounded confidence value.</summary>
    [JsonPropertyName("confidence"), JsonPropertyOrder(10)]
    public double? Confidence { get; init; }
}
