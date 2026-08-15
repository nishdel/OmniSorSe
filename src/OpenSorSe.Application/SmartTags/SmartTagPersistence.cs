using OpenSorSe.Application.Catalog;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.Models;

namespace OpenSorSe.Application.SmartTags;

/// <summary>Contains exact canonical filters. Values within one type are OR; populated types are AND.</summary>
public sealed record SmartTagFilter(
    IReadOnlyList<string>? ThemeTagIds = null,
    IReadOnlyList<string>? DocumentTypeTagIds = null,
    IReadOnlyList<string>? UserTagIds = null,
    bool IncludeSuggestions = false);

/// <summary>Reports one bounded tag mutation without exposing storage details.</summary>
public sealed record SmartTagOperationResult(bool Applied, int AffectedCount, string Message);

/// <summary>Contains one safely resolved legacy association candidate.</summary>
public sealed record LegacySmartTagImport(
    string FullPath,
    string DisplayName,
    string NormalizedValue,
    SmartTagDecision Decision,
    bool IsUserOwned,
    string? BuiltInTagId);

/// <summary>Owns durable schema-6 Smart Tag state behind provider-neutral application contracts.</summary>
public interface ISmartTagStore
{
    /// <summary>Resolves one active durable file identity from its current path.</summary>
    Task<string?> ResolveActiveFileIdAsync(string fullPath, CancellationToken cancellationToken = default);
    /// <summary>Resolves bounded current paths to active durable identities without requiring callers to issue N+1 queries.</summary>
    async Task<IReadOnlyDictionary<string, string>> ResolveActiveFileIdsAsync(
        IReadOnlyList<string> fullPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fullPaths);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in fullPaths.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileId = await ResolveActiveFileIdAsync(path, cancellationToken).ConfigureAwait(false);
            if (fileId is not null)
            {
                result[path] = fileId;
            }
        }

        return result;
    }
    /// <summary>Marks only stale Smart Tag stages for deferred reclassification.</summary>
    Task<int> PrepareStaleClassificationsAsync(
        string classifierVersion,
        string taxonomyVersion,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);
    /// <summary>Returns the durable definitions currently available to filtering and review.</summary>
    Task<IReadOnlyList<SmartTagDefinition>> GetSmartTagDefinitionsAsync(CancellationToken cancellationToken = default);
    /// <summary>Returns effective assignments for one stable file identity.</summary>
    Task<IReadOnlyList<FileSmartTag>> GetFileSmartTagsAsync(string fileId, CancellationToken cancellationToken = default);
    /// <summary>Returns effective assignments for bounded file identities.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<FileSmartTag>>> GetFileSmartTagsAsync(
        IReadOnlyList<string> fileIds,
        CancellationToken cancellationToken = default);
    /// <summary>Creates or reuses a normalized user tag and assigns it explicitly.</summary>
    Task<SmartTagOperationResult> AddUserTagAsync(
        string fileId,
        string displayName,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);
    /// <summary>Removes one explicit user association or records rejection of a generated assignment.</summary>
    Task<SmartTagOperationResult> RemoveTagAsync(
        string fileId,
        string tagId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);
    /// <summary>Records explicit acceptance or rejection without rewriting generated provenance.</summary>
    Task<SmartTagOperationResult> SetTagDecisionAsync(
        string fileId,
        string tagId,
        SmartTagDecision decision,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);
    /// <summary>Clears explicit accept/reject authority so generated candidates may be reconsidered.</summary>
    Task<SmartTagOperationResult> ResetTagDecisionsAsync(
        string? fileId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);
    /// <summary>Removes generated classifications while retaining user tags, accepted authority, and rejection decisions.</summary>
    Task<SmartTagOperationResult> ClearGeneratedSmartTagsAsync(
        string? fileId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);
    /// <summary>Returns bounded active file identities using OR-within-type and AND-across-type semantics.</summary>
    Task<IReadOnlyList<string>> FilterFileIdsBySmartTagsAsync(
        SmartTagFilter filter,
        int maximumCount,
        CancellationToken cancellationToken = default);
    /// <summary>Returns whether the bounded legacy import completed successfully.</summary>
    Task<bool> IsLegacySmartTagImportCompleteAsync(CancellationToken cancellationToken = default);
    /// <summary>Imports only safely resolvable legacy user authority and records successful completion atomically.</summary>
    Task<SmartTagOperationResult> ImportLegacySmartTagsAsync(
        IReadOnlyList<LegacySmartTagImport> imports,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>Coordinates durable Smart Tags and one-time bounded migration from legacy tag surfaces.</summary>
public interface ISmartTagService
{
    /// <summary>Imports safely resolvable legacy authority once after schema initialization.</summary>
    Task<SmartTagOperationResult> InitializeAsync(CancellationToken cancellationToken = default);
    /// <summary>Returns bounded canonical definitions available to typed filter controls.</summary>
    Task<IReadOnlyList<SmartTagDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default);
    /// <summary>Resolves one active durable file identity from its current path.</summary>
    Task<string?> ResolveActiveFileIdAsync(string fullPath, CancellationToken cancellationToken = default);
    /// <summary>Resolves bounded current paths to active durable identities in one provider operation.</summary>
    async Task<IReadOnlyDictionary<string, string>> ResolveActiveFileIdsAsync(
        IReadOnlyList<string> fullPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fullPaths);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in fullPaths.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileId = await ResolveActiveFileIdAsync(path, cancellationToken).ConfigureAwait(false);
            if (fileId is not null)
            {
                result[path] = fileId;
            }
        }

        return result;
    }
    /// <summary>Returns effective assignments for one stable file.</summary>
    Task<IReadOnlyList<FileSmartTag>> GetFileTagsAsync(string fileId, CancellationToken cancellationToken = default);
    /// <summary>Adds one explicit local user tag.</summary>
    Task<SmartTagOperationResult> AddUserTagAsync(string fileId, string displayName, CancellationToken cancellationToken = default);
    /// <summary>Accepts or rejects one generated classification.</summary>
    Task<SmartTagOperationResult> DecideAsync(string fileId, string tagId, SmartTagDecision decision, CancellationToken cancellationToken = default);
    /// <summary>Removes a user tag or rejects a generated tag.</summary>
    Task<SmartTagOperationResult> RemoveAsync(string fileId, string tagId, CancellationToken cancellationToken = default);
    /// <summary>Resets decisions for one file or all files.</summary>
    Task<SmartTagOperationResult> ResetDecisionsAsync(string? fileId, CancellationToken cancellationToken = default);
    /// <summary>Clears generated classifications while preserving user authority.</summary>
    Task<SmartTagOperationResult> ClearGeneratedAsync(string? fileId, CancellationToken cancellationToken = default);
    /// <summary>Returns exact canonical filtered file identities.</summary>
    Task<IReadOnlyList<string>> FilterAsync(SmartTagFilter filter, int maximumCount, CancellationToken cancellationToken = default);
}

/// <summary>Uses schema-6 SQLite as authority while importing older path-keyed stores once and conservatively.</summary>
public sealed class SmartTagService : ISmartTagService
{
    private const int MaximumLegacyImports = 20_000;
    private readonly ISmartTagStore _store;
    private readonly IContentStore _contentStore;
    private readonly IResultsCatalogStore _catalogStore;
    private readonly SmartTagTaxonomy _taxonomy;
    private readonly ISmartTagClassifier _classifier;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the authoritative service and bounded legacy sources.</summary>
    public SmartTagService(
        ISmartTagStore store,
        IContentStore contentStore,
        IResultsCatalogStore catalogStore,
        SmartTagTaxonomy taxonomy,
        ISmartTagClassifier classifier,
        TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
        _catalogStore = catalogStore ?? throw new ArgumentNullException(nameof(catalogStore));
        _taxonomy = taxonomy ?? throw new ArgumentNullException(nameof(taxonomy));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<SmartTagOperationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        SmartTagOperationResult importResult;
        if (await _store.IsLegacySmartTagImportCompleteAsync(cancellationToken).ConfigureAwait(false))
        {
            importResult = new SmartTagOperationResult(false, 0, "Legacy Smart Tag authority was already imported.");
        }
        else
        {
            var imports = new List<LegacySmartTagImport>();
            var contentRecords = await _contentStore.ListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var record in contentRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var tag in record.Tags.Where(IsImportable).Take(32))
                {
                    var builtIn = ResolveBuiltIn(tag);
                    if (tag.AcceptanceState == TagAcceptanceState.Rejected && builtIn is null)
                    {
                        continue;
                    }

                    imports.Add(ToImport(record.FullPath, tag, builtIn));
                    if (imports.Count >= MaximumLegacyImports)
                    {
                        break;
                    }
                }

                if (imports.Count >= MaximumLegacyImports)
                {
                    break;
                }
            }

            if (imports.Count < MaximumLegacyImports)
            {
                var summaries = await _catalogStore.ListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var summary in summaries)
                {
                    var entry = await _catalogStore.LoadAsync(summary.Id, cancellationToken).ConfigureAwait(false);
                    if (entry is null)
                    {
                        continue;
                    }

                    var paths = entry.Snapshot.Files.ToDictionary(file => file.Id, file => file.FullPath, StringComparer.Ordinal);
                    foreach (var tag in entry.AcceptedTags.Where(IsImportable))
                    {
                        if (!paths.TryGetValue(tag.FileId, out var path))
                        {
                            continue;
                        }

                        imports.Add(ToImport(path, tag, ResolveBuiltIn(tag)) with
                        {
                            Decision = SmartTagDecision.Accepted,
                            IsUserOwned = true,
                        });
                        if (imports.Count >= MaximumLegacyImports)
                        {
                            break;
                        }
                    }

                    if (imports.Count >= MaximumLegacyImports)
                    {
                        break;
                    }
                }

            }

            var distinct = imports
                .DistinctBy(item => (Path.GetFullPath(item.FullPath), item.NormalizedValue, item.Decision))
                .Take(MaximumLegacyImports)
                .ToArray();
            importResult = await _store.ImportLegacySmartTagsAsync(
                distinct,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
        }

        await _store.PrepareStaleClassificationsAsync(
            _classifier.Version,
            _taxonomy.Version,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        return importResult;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FileSmartTag>> GetFileTagsAsync(string fileId, CancellationToken cancellationToken = default) =>
        _store.GetFileSmartTagsAsync(fileId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<SmartTagDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default) =>
        _store.GetSmartTagDefinitionsAsync(cancellationToken);

    /// <inheritdoc />
    public Task<string?> ResolveActiveFileIdAsync(string fullPath, CancellationToken cancellationToken = default) =>
        _store.ResolveActiveFileIdAsync(fullPath, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> ResolveActiveFileIdsAsync(
        IReadOnlyList<string> fullPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fullPaths);
        if (fullPaths.Count > OpenSorSe.Executor.Models.ChangePlanSchema.MaximumActions)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fullPaths),
                $"At most {OpenSorSe.Executor.Models.ChangePlanSchema.MaximumActions} current paths can be resolved at once.");
        }

        return _store.ResolveActiveFileIdsAsync(fullPaths, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SmartTagOperationResult> AddUserTagAsync(string fileId, string displayName, CancellationToken cancellationToken = default) =>
        _store.AddUserTagAsync(fileId, displayName, _timeProvider.GetUtcNow(), cancellationToken);

    /// <inheritdoc />
    public Task<SmartTagOperationResult> DecideAsync(
        string fileId,
        string tagId,
        SmartTagDecision decision,
        CancellationToken cancellationToken = default) =>
        _store.SetTagDecisionAsync(fileId, tagId, decision, _timeProvider.GetUtcNow(), cancellationToken);

    /// <inheritdoc />
    public Task<SmartTagOperationResult> RemoveAsync(string fileId, string tagId, CancellationToken cancellationToken = default) =>
        _store.RemoveTagAsync(fileId, tagId, _timeProvider.GetUtcNow(), cancellationToken);

    /// <inheritdoc />
    public Task<SmartTagOperationResult> ResetDecisionsAsync(string? fileId, CancellationToken cancellationToken = default) =>
        _store.ResetTagDecisionsAsync(fileId, _timeProvider.GetUtcNow(), cancellationToken);

    /// <inheritdoc />
    public Task<SmartTagOperationResult> ClearGeneratedAsync(string? fileId, CancellationToken cancellationToken = default) =>
        _store.ClearGeneratedSmartTagsAsync(fileId, _timeProvider.GetUtcNow(), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> FilterAsync(
        SmartTagFilter filter,
        int maximumCount,
        CancellationToken cancellationToken = default) =>
        _store.FilterFileIdsBySmartTagsAsync(filter, maximumCount, cancellationToken);

    private SmartTagDefinition? ResolveBuiltIn(TagAssociation tag) =>
        _taxonomy.Definitions.FirstOrDefault(item =>
            string.Equals(item.CanonicalKey, tag.NormalizedValue, StringComparison.Ordinal) ||
            string.Equals(item.DisplayName, tag.DisplayName, StringComparison.OrdinalIgnoreCase) ||
            item.Aliases.Contains(tag.NormalizedValue, StringComparer.Ordinal));

    private static bool IsImportable(TagAssociation tag) =>
        tag.Source == TagSource.UserApproved ||
        (tag.AcceptanceState is TagAcceptanceState.Accepted or TagAcceptanceState.Rejected) && !tag.IsSystem;

    private static LegacySmartTagImport ToImport(
        string path,
        TagAssociation tag,
        SmartTagDefinition? builtIn) => new(
            path,
            tag.DisplayName,
            tag.NormalizedValue,
            tag.AcceptanceState == TagAcceptanceState.Rejected
                ? SmartTagDecision.Rejected
                : SmartTagDecision.Accepted,
            tag.Source == TagSource.UserApproved || builtIn is null,
            builtIn?.TagId);
}
