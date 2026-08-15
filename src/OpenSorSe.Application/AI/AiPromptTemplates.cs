namespace OpenSorSe.Application.AI;

/// <summary>Contains reviewable, versioned small-model prompt instructions.</summary>
public static class AiPromptTemplates
{
    /// <summary>Gets the file-rename prompt-template version.</summary>
    public const string FileRenamePromptVersion = "2.1";

    /// <summary>Gets the folder-structure prompt-template version.</summary>
    public const string FolderStructurePromptVersion = "2.0";

    /// <summary>Gets the document-text interpretation prompt-template version.</summary>
    public const string DocumentInterpretationPromptVersion = "1.1";

    /// <summary>Gets the structured-repair prompt-template version.</summary>
    public const string RepairPromptVersion = "1.0";

    /// <summary>Gets the intended lower and upper local-model size range.</summary>
    public const string IntendedModelSizeRange = "approximately 2B through 8B parameters";

    /// <summary>Gets the numbered file-rename rules in stable order.</summary>
    public static IReadOnlyList<string> FileRenameRules { get; } =
    [
        "1. Use only the labelled INPUT evidence. Do not invent dates, people, organizations, subjects, or document facts.",
        "2. Copy sourceFileId exactly.",
        "3. Return one filename stem only. Do not include the extension, a path, a command, or more than one alternative.",
        "4. Use letters, numbers, and single hyphens only. Do not start or end with a hyphen.",
        "5. Prefer token order: explicit date, subject, document type. Use yyyy-MM-dd for an explicit date.",
        "6. Keep suggestedStem within maximumStemLength. Do not use reserved device names.",
        "7. Return no_suggestion when the evidence is ambiguous, the current stem is already clear, or a grounded improvement is not possible.",
        "8. Keep reason user-facing and at or below 160 characters. Do not include hidden reasoning or analysis.",
        "9. Return JSON only. Do not return Markdown or prose outside the JSON object.",
    ];

    /// <summary>Gets the numbered folder-structure rules in stable order.</summary>
    public static IReadOnlyList<string> FolderStructureRules { get; } =
    [
        "1. Use only the labelled INPUT records and allowedFolderNames. Do not invent file facts or folder names.",
        "2. Copy every supplied sourceFileId exactly and assign each one exactly once.",
        "3. Use only returned folderId values in parentFolderId and assignments.",
        "4. Use unique folderId values in the form folder-NNN.",
        "5. Folder names must be one value from allowedFolderNames and one portable component, never a path.",
        "6. Do not exceed maximumFolders, maximumDepth, or maximumComponentLength. Do not create cycles.",
        "7. Use the fallback folder Other when classification is uncertain.",
        "8. Return no_suggestion only when no useful hierarchy is justified; omit folders and assignments in that response.",
        "9. Keep reason user-facing and at or below 160 characters. Do not include hidden reasoning or analysis.",
        "10. Return JSON only. Do not return Markdown, commands, filesystem actions, or prose outside the JSON object.",
    ];

    /// <summary>Gets the numbered document-text interpretation rules in stable order.</summary>
    public static IReadOnlyList<string> DocumentInterpretationRules { get; } =
    [
        "1. Use only supplied text and filename; do not invent facts.",
        "2. Copy sourceFileId exactly. Use null or empty arrays for unsupported values.",
        "3. Dates must be explicit and formatted yyyy-MM-dd.",
        "4. Do not provide legal, financial, medical, identity, path, command, or filesystem conclusions.",
        "5. Keep reason at or below 160 characters. Do not include hidden reasoning.",
        "6. Return one JSON object only; no Markdown or prose.",
    ];

    /// <summary>Gets the numbered one-pass structured-repair rules in stable order.</summary>
    public static IReadOnlyList<string> RepairRules { get; } =
    [
        "1. Correct only the supplied prior response for the same taskId.",
        "2. Apply the exact validationError and responseSchema.",
        "3. Preserve supplied opaque identities; do not add or replace identities.",
        "4. Do not add facts, paths, commands, filesystem actions, alternatives, or explanation.",
        "5. Return exactly one corrected JSON object. Do not return Markdown or prose.",
    ];
}
