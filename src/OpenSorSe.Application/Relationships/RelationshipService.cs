using System.Diagnostics;
using System.Globalization;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;

namespace OpenSorSe.Application.Relationships;

/// <summary>Coordinates provider-neutral relationship analysis, user control, Search, and privacy.</summary>
public sealed class RelationshipService : IRelationshipService, IRelationshipSearchSource
{
    private readonly IConfigurationService _configurationService;
    private readonly IDiagnosticsEventSink? _diagnostics;
    private readonly IRelationshipEngine _engine;
    private readonly IRelationshipStore _store;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the relationship service.</summary>
    public RelationshipService(
        IConfigurationService configurationService,
        IRelationshipStore store,
        IRelationshipEngine engine,
        IDiagnosticsEventSink? diagnostics = null,
        TimeProvider? timeProvider = null)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _diagnostics = DiagnosticsIsolation.Protect(diagnostics);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<RelationshipAnalysisResult> AnalyzeFileAsync(
        string fileId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(fileId, nameof(fileId));
        var settings = _configurationService.Current.DeepIndexing;
        if (!settings.RelationshipAnalysisEnabled)
        {
            return new RelationshipAnalysisResult(fileId, 0, 0, 0, TimeSpan.Zero, true, "Relationship analysis is disabled in Settings.");
        }

        var document = await _store.GetRelationshipFileAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return new RelationshipAnalysisResult(fileId, 0, 0, 0, TimeSpan.Zero, true, "The indexed file is no longer available for relationship analysis.");
        }

        if (document.RelationshipAnalysisSuppressed || IsExcludedExtension(document.Extension, settings.RelationshipExcludedExtensions))
        {
            return new RelationshipAnalysisResult(fileId, 0, 0, 0, TimeSpan.Zero, true, "Relationship analysis is excluded for this file.");
        }

        var started = Stopwatch.GetTimestamp();
        var session = _diagnostics?.BeginSession(
            DiagnosticCategory.SearchAndIndexing,
            "Relationship analysis",
            [
                new DiagnosticField("File identity", "indexed-file"),
                new DiagnosticField("Algorithm", _engine.Algorithm),
                new DiagnosticField("Algorithm version", _engine.Version),
            ]);
        try
        {
            var features = _engine.CreateFeatures(document);
            await _store
                .UpsertRelationshipFeaturesAsync(features, _timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            var candidateLimit = Math.Clamp(
                settings.MaximumRelationshipCandidates,
                1,
                RelationshipLimits.MaximumCandidates);
            var candidates = await _store
                .GetRelationshipCandidatesAsync(features, candidateLimit, cancellationToken)
                .ConfigureAwait(false);
            var proposals = _engine.Discover(
                document,
                candidates,
                Math.Clamp(
                    settings.MaximumRelationshipsPerFile,
                    1,
                    RelationshipLimits.MaximumRelationshipsPerFile),
                cancellationToken);
            var elapsed = Stopwatch.GetElapsedTime(started);
            var completedAt = _timeProvider.GetUtcNow();
            await _store.SaveRelationshipAnalysisAsync(
                    new RelationshipAnalysisBatch(
                        fileId,
                        features,
                        candidates.Count,
                        proposals,
                        _engine.Algorithm,
                        _engine.Version,
                        completedAt,
                        elapsed),
                    Math.Clamp(
                        settings.MaximumSmartCollectionMembers,
                        2,
                        RelationshipLimits.MaximumCollectionMembers),
                    cancellationToken)
                .ConfigureAwait(false);
            var collectionCount = proposals.Count(item => item.Collection is not null);
            _diagnostics?.Complete(
                session,
                DiagnosticStatus.Succeeded,
                elapsed,
                "Relationship analysis completed with bounded evidence.",
                fields:
                [
                    new DiagnosticField("Candidate count", candidates.Count.ToString(CultureInfo.InvariantCulture)),
                    new DiagnosticField("Relationship count", proposals.Count.ToString(CultureInfo.InvariantCulture)),
                    new DiagnosticField("Collection suggestion count", collectionCount.ToString(CultureInfo.InvariantCulture)),
                ]);
            return new RelationshipAnalysisResult(
                fileId,
                candidates.Count,
                proposals.Count,
                collectionCount,
                elapsed,
                false,
                $"Created or refreshed {proposals.Count} evidence-backed relationships.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _diagnostics?.Complete(
                session,
                DiagnosticStatus.Cancelled,
                Stopwatch.GetElapsedTime(started),
                "Relationship analysis was cancelled before publishing a partial batch.");
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or IOException)
        {
            _diagnostics?.Complete(
                session,
                DiagnosticStatus.Failed,
                Stopwatch.GetElapsedTime(started),
                "Relationship analysis failed safely without changing source files.",
                DiagnosticSeverity.Error,
                [new DiagnosticField("Failure category", exception.GetType().Name)]);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RelationshipFileDocument>> GetFilesAsync(
        int maximumCount = 1_000,
        CancellationToken cancellationToken = default) =>
        _store.GetRelationshipFilesAsync(Bound(maximumCount, 1, 10_000, nameof(maximumCount)), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<RelatedFile>> GetRelatedFilesAsync(
        string fileId,
        RelationshipType? type = null,
        RelationshipConfidence? minimumConfidence = null,
        RelatedFileSort sort = RelatedFileSort.Confidence,
        int maximumCount = 200,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(fileId, nameof(fileId));
        if (type.HasValue && !Enum.IsDefined(type.Value) ||
            minimumConfidence.HasValue && !Enum.IsDefined(minimumConfidence.Value) ||
            !Enum.IsDefined(sort))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        return _store.GetRelatedFilesAsync(
            fileId,
            type,
            minimumConfidence,
            sort,
            Bound(maximumCount, 1, 1_000, nameof(maximumCount)),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<FileRelationship?> GetRelationshipAsync(string relationshipId, CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(relationshipId, nameof(relationshipId));
        return _store.GetRelationshipAsync(relationshipId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SmartCollection>> GetCollectionsAsync(
        int maximumCount = 500,
        CancellationToken cancellationToken = default) =>
        _store.GetCollectionsAsync(Bound(maximumCount, 1, 2_000, nameof(maximumCount)), cancellationToken);

    /// <inheritdoc />
    public Task<SmartCollectionDetails?> GetCollectionAsync(
        string collectionId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(collectionId, nameof(collectionId));
        return _store.GetCollectionAsync(collectionId, RelationshipLimits.MaximumCollectionMembers, cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> LinkFilesAsync(
        string firstFileId,
        string secondFileId,
        RelationshipType type,
        string? customType = null,
        bool alwaysRelate = false,
        CancellationToken cancellationToken = default)
    {
        ValidatePair(firstFileId, secondFileId);
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        customType = ValidateCustomType(type, customType);
        return _store.LinkFilesAsync(
            firstFileId,
            secondFileId,
            type,
            customType,
            alwaysRelate,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> UnlinkAsync(
        string relationshipId,
        bool neverRelate = false,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(relationshipId, nameof(relationshipId));
        return _store.UnlinkFilesAsync(relationshipId, neverRelate, _timeProvider.GetUtcNow(), cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> SetDecisionAsync(
        string relationshipId,
        RelationshipDecision decision,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(relationshipId, nameof(relationshipId));
        if (!Enum.IsDefined(decision) || decision == RelationshipDecision.None)
        {
            throw new ArgumentOutOfRangeException(nameof(decision));
        }

        return _store.SetRelationshipDecisionAsync(relationshipId, decision, _timeProvider.GetUtcNow(), cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> RenameCollectionAsync(
        string collectionId,
        string title,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(collectionId, nameof(collectionId));
        title = ValidateTitle(title);
        return _store.RenameCollectionAsync(collectionId, title, _timeProvider.GetUtcNow(), cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> SetCollectionPinnedAsync(
        string collectionId,
        bool pinned,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(collectionId, nameof(collectionId));
        return _store.SetCollectionPinnedAsync(collectionId, pinned, _timeProvider.GetUtcNow(), cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> MergeCollectionsAsync(
        string targetCollectionId,
        string sourceCollectionId,
        CancellationToken cancellationToken = default)
    {
        ValidatePair(targetCollectionId, sourceCollectionId);
        return _store.MergeCollectionsAsync(targetCollectionId, sourceCollectionId, _timeProvider.GetUtcNow(), cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> SplitCollectionMemberAsync(
        string collectionId,
        string fileId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(collectionId, nameof(collectionId));
        ValidateIdentifier(fileId, nameof(fileId));
        return _store.SplitCollectionMemberAsync(collectionId, fileId, _timeProvider.GetUtcNow(), cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> ForgetCollectionAsync(
        string collectionId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(collectionId, nameof(collectionId));
        return _store.ForgetCollectionAsync(collectionId, _timeProvider.GetUtcNow(), cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> ForgetFileAsync(
        string fileId,
        bool excludeFutureAnalysis,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(fileId, nameof(fileId));
        return _store.ForgetFileRelationshipsAsync(
            fileId,
            excludeFutureAnalysis,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> ForgetSourceAsync(
        string sourceId,
        bool excludeFutureAnalysis,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(sourceId, nameof(sourceId));
        return _store.ForgetSourceRelationshipsAsync(
            sourceId,
            excludeFutureAnalysis,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RelationshipOperationResult> RebuildFileAsync(
        string fileId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(fileId, nameof(fileId));
        var prepared = await _store
            .PrepareRelationshipRebuildAsync(fileId, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        if (!prepared.Applied)
        {
            return prepared;
        }

        var analysis = await AnalyzeFileAsync(fileId, cancellationToken).ConfigureAwait(false);
        return new RelationshipOperationResult(
            true,
            analysis.RelationshipCount,
            analysis.CollectionSuggestionCount,
            "Relationship data was rebuilt. The original file was not changed.");
    }

    /// <inheritdoc />
    public Task<RelationshipOperationResult> RepairAsync(CancellationToken cancellationToken = default) =>
        _store.RepairRelationshipsAsync(_timeProvider.GetUtcNow(), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<RelationshipSearchExpansion>> ExpandSearchAsync(
        IReadOnlyList<string> seedFileIds,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seedFileIds);
        foreach (var id in seedFileIds.Take(32))
        {
            ValidateIdentifier(id, nameof(seedFileIds));
        }

        return _store.GetSearchExpansionsAsync(
            seedFileIds.Distinct(StringComparer.Ordinal).Take(32).ToArray(),
            Bound(maximumCount, 1, RelationshipLimits.MaximumSearchExpansions, nameof(maximumCount)),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RelationshipDiagnosticsSnapshot> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
        _store.GetRelationshipDiagnosticsAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<RelationshipSearchExpansion>> ExpandAsync(
        IReadOnlyList<string> seedFileIds,
        int maximumCount,
        CancellationToken cancellationToken = default) =>
        ExpandSearchAsync(seedFileIds, maximumCount, cancellationToken);

    private static bool IsExcludedExtension(string extension, IReadOnlyList<string> exclusions) =>
        exclusions.Any(value => string.Equals(NormalizeExtension(value), NormalizeExtension(extension), StringComparison.Ordinal));

    private static string NormalizeExtension(string extension) =>
        string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : "." + extension.Trim().TrimStart('.').ToLowerInvariant();

    private static string? ValidateCustomType(RelationshipType type, string? customType)
    {
        if (type != RelationshipType.Custom)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(customType) || customType.Length > 64 ||
            SearchTextNormalizer.ContainsMalformedUnicode(customType) || customType.Any(char.IsControl))
        {
            throw new ArgumentException("A custom relationship requires a valid name of 64 characters or fewer.", nameof(customType));
        }

        return string.Join(' ', customType.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ValidateTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (title.Length > RelationshipLimits.MaximumCollectionTitleCharacters ||
            SearchTextNormalizer.ContainsMalformedUnicode(title) || title.Any(char.IsControl))
        {
            throw new ArgumentException("The collection title is malformed or exceeds the supported bound.", nameof(title));
        }

        return string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static void ValidatePair(string first, string second)
    {
        ValidateIdentifier(first, nameof(first));
        ValidateIdentifier(second, nameof(second));
        if (string.Equals(first, second, StringComparison.Ordinal))
        {
            throw new ArgumentException("A relationship or merge requires two different identifiers.");
        }
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl))
        {
            throw new ArgumentException("The relationship identifier is malformed or exceeds the supported bound.", parameterName);
        }
    }

    private static int Bound(int value, int minimum, int maximum, string parameterName) =>
        value is >= 1 && value >= minimum && value <= maximum
            ? value
            : throw new ArgumentOutOfRangeException(parameterName);
}
