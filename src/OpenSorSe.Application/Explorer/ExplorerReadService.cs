using OmniSorSe.ExplorerProtocol;
using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Application.Explorer;

internal sealed class ExplorerProtocolException : Exception
{
    public ExplorerProtocolException(ExplorerErrorCode code, string message, bool retryable = false)
        : base(message)
    {
        Code = code;
        Retryable = retryable;
    }

    public ExplorerErrorCode Code { get; }
    public bool Retryable { get; }
}

/// <summary>Supplies existing provider-neutral index, Search, and Related Files data to Explorer projection.</summary>
public interface IExplorerDataSource
{
    /// <summary>Gets currently configured indexed sources.</summary>
    Task<IReadOnlyList<IndexingSource>> GetSourcesAsync(CancellationToken cancellationToken);

    /// <summary>Gets a bounded set of current indexed files.</summary>
    Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsAsync(int maximumCount, CancellationToken cancellationToken);

    /// <summary>Gets exact current indexed files by stable identifier.</summary>
    Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsByIdsAsync(
        IReadOnlyList<string> fileIds,
        CancellationToken cancellationToken);

    /// <summary>Runs the existing unified Search pipeline.</summary>
    Task<SearchExecutionResult> SearchAsync(
        OpenSorSe.Application.Semantic.SearchRequest request,
        CancellationToken cancellationToken);

    /// <summary>Gets current bounded Related Files evidence.</summary>
    Task<IReadOnlyList<RelatedFile>> GetRelatedFilesAsync(
        string fileId,
        int maximumCount,
        CancellationToken cancellationToken);
}

/// <summary>Adapts existing application services without exposing SQLite or provider internals.</summary>
public sealed class ExplorerDataSource : IExplorerDataSource
{
    private readonly IBackgroundIndexingService _indexing;
    private readonly ISemanticSearchService _search;
    private readonly IRelationshipService _relationships;

    /// <summary>Creates the adapter over existing provider-neutral services.</summary>
    public ExplorerDataSource(
        IBackgroundIndexingService indexing,
        ISemanticSearchService search,
        IRelationshipService relationships)
    {
        _indexing = indexing ?? throw new ArgumentNullException(nameof(indexing));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IndexingSource>> GetSourcesAsync(CancellationToken cancellationToken) =>
        _indexing.GetSourcesAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsAsync(
        int maximumCount,
        CancellationToken cancellationToken) =>
        _indexing.GetDocumentsAsync(maximumCount, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsByIdsAsync(
        IReadOnlyList<string> fileIds,
        CancellationToken cancellationToken) =>
        _indexing.GetDocumentsByIdsAsync(fileIds, cancellationToken);

    /// <inheritdoc />
    public Task<SearchExecutionResult> SearchAsync(
        OpenSorSe.Application.Semantic.SearchRequest request,
        CancellationToken cancellationToken) =>
        _search.SearchAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<RelatedFile>> GetRelatedFilesAsync(
        string fileId,
        int maximumCount,
        CancellationToken cancellationToken) =>
        _relationships.GetRelatedFilesAsync(
            fileId,
            minimumConfidence: RelationshipConfidence.Medium,
            maximumCount: maximumCount,
            cancellationToken: cancellationToken);
}

internal sealed class ExplorerReadService
{
    private sealed record AuthorizedDocumentBatch(
        IReadOnlyList<ProgressiveSearchDocument> Documents,
        bool IsProjectionTruncated);

    private readonly IExplorerDataSource _source;
    private readonly IPathSemantics _paths;

    public ExplorerReadService(IExplorerDataSource source, IPathSemantics paths)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public ExplorerProtocolInfo GetProtocolInfo() => new(
        ExplorerProtocolVersion.Major,
        ExplorerProtocolVersion.Minor,
        "OmniSorSe",
        OpenSorSe.Core.ApplicationVersionInfo.Current,
        ExplorerCapability.Structure |
        ExplorerCapability.Search |
        ExplorerCapability.Context |
        ExplorerCapability.RelatedFiles |
        ExplorerCapability.MediaIntelligence |
        ExplorerCapability.ContentIntelligence |
        ExplorerCapability.Ocr |
        ExplorerCapability.Transcripts |
        ExplorerCapability.Topics |
        ExplorerCapability.Entities |
        ExplorerCapability.Summaries,
        ExplorerProtocolDefaults.CreateLimits(),
        IsReadOnly: true,
        Transport: "Local named pipe (Unix-domain-backed on Unix hosts)");

    public async Task ValidateScopeAsync(
        ExplorerSessionScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var configured = (await _source.GetSourcesAsync(cancellationToken).ConfigureAwait(false))
            .Select(source => source.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (scope.AuthorizedSourceIds.Count is 0 or > ExplorerProtocolDefaults.MaximumAuthorizedSources ||
            scope.AuthorizedSourceIds.Any(sourceId => !configured.Contains(sourceId)))
        {
            throw new ArgumentException(
                "Explorer sessions may authorize only currently configured indexed sources.",
                nameof(scope));
        }
    }

    public async Task<ExplorerNodePage> GetAccessibleRootsAsync(
        ExplorerSessionContext session,
        CancellationToken cancellationToken)
    {
        var sources = await _source.GetSourcesAsync(cancellationToken).ConfigureAwait(false);
        var nodes = sources
            .Where(source => session.IsAuthorizedSource(source.Id))
            .OrderBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.Id, StringComparer.Ordinal)
            .Take(ExplorerProtocolDefaults.MaximumAuthorizedSources)
            .Select(source => CreateSourceNode(session, source))
            .ToArray();
        return new ExplorerNodePage(nodes, nodes.Length, false, null);
    }

    public async Task<ExplorerNodePage> GetChildrenAsync(
        ExplorerSessionContext session,
        ExplorerChildrenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var identity = Resolve(session, request.ParentNodeId);
        if (identity.Kind == ExplorerNodeKind.File)
        {
            return new ExplorerNodePage([], 0, false, null);
        }

        var pageSize = BoundCount(request.MaximumResults, ExplorerProtocolDefaults.DefaultPageSize, ExplorerProtocolDefaults.MaximumNodes);
        var offset = ParseContinuation(request.ContinuationToken);
        var source = await GetAuthorizedSourceAsync(session, identity.SourceId, cancellationToken).ConfigureAwait(false);
        var documentBatch = await GetAuthorizedDocumentsAsync(session, identity.SourceId, cancellationToken).ConfigureAwait(false);
        var children = BuildImmediateChildren(session, source, identity, documentBatch.Documents);
        if (offset > children.Count)
        {
            throw new ExplorerProtocolException(ExplorerErrorCode.LimitExceeded, "The continuation token exceeds the available bounded result set.");
        }

        var page = children.Skip(offset).Take(pageSize).ToArray();
        var nextOffset = offset + page.Length;
        var hasAnotherPage = nextOffset < children.Count;
        var truncated = hasAnotherPage || documentBatch.IsProjectionTruncated;
        return new ExplorerNodePage(
            page,
            children.Count,
            truncated,
            hasAnotherPage ? FormatContinuation(nextOffset) : null);
    }

    public async Task<ExplorerSearchResult> SearchAsync(
        ExplorerSessionContext session,
        ExplorerSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = request.Query?.Trim() ?? string.Empty;
        if (query.Length is 0 or > SearchLimits.MaximumQueryCharacters || query.Any(character => character == '\0'))
        {
            throw new ExplorerProtocolException(
                query.Length > SearchLimits.MaximumQueryCharacters
                    ? ExplorerErrorCode.RequestTooLarge
                    : ExplorerErrorCode.MalformedRequest,
                "Search requires a non-empty bounded query.");
        }

        var maximum = BoundCount(request.MaximumResults, 50, ExplorerProtocolDefaults.MaximumSearchResults);
        var execution = await _source.SearchAsync(
            new OpenSorSe.Application.Semantic.SearchRequest(
                query,
                IncludeRelationshipContext: request.IncludeContext)
            {
                UseAiAssistance = false,
                IncludeGraphContext = request.IncludeContext,
            },
            cancellationToken).ConfigureAwait(false);
        var fileIds = execution.Hits
            .Select(hit => hit.FileId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(SearchLimits.MaximumRankedResults)
            .ToArray();
        var documents = await _source.GetDocumentsByIdsAsync(fileIds, cancellationToken).ConfigureAwait(false);
        var authorized = documents
            .Where(document => session.IsAuthorizedSource(document.SourceId))
            .ToDictionary(document => document.FileId, StringComparer.Ordinal);
        var results = new List<ExplorerSearchHit>(maximum);
        for (var index = 0; index < execution.Hits.Count && results.Count < maximum; index++)
        {
            var hit = execution.Hits[index];
            if (hit.FileId is null || !authorized.TryGetValue(hit.FileId, out var document))
            {
                continue;
            }

            var node = CreateFileNode(session, document);
            results.Add(new ExplorerSearchHit(
                node,
                results.Count + 1,
                hit.Score,
                Bound(hit.Explanation, ExplorerProtocolDefaults.MaximumReasonCharacters),
                hit.Snippet is null ? null : Bound(hit.Snippet.Text, SearchLimits.MaximumSnippetCharacters),
                hit.Snippet?.SourceLabel ?? hit.RankingComponents?.FirstOrDefault()?.Field));
        }

        var authorizedHitCount = execution.Hits.Count(hit =>
            hit.FileId is not null && authorized.ContainsKey(hit.FileId));
        return new ExplorerSearchResult(
            results,
            authorizedHitCount > results.Count,
            "Unified local Search completed over the authorized indexed scope.",
            UsedAiAssistance: false);
    }

    public async Task<ExplorerRelatedResult> GetRelatedAsync(
        ExplorerSessionContext session,
        ExplorerRelatedRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var identity = Resolve(session, request.NodeId);
        if (identity.Kind != ExplorerNodeKind.File || string.IsNullOrWhiteSpace(identity.FileId))
        {
            throw new ExplorerProtocolException(ExplorerErrorCode.CapabilityUnavailable, "Related Files is available for indexed file nodes.");
        }

        var maximum = BoundCount(request.MaximumResults, 50, ExplorerProtocolDefaults.MaximumRelatedResults);
        var requestedRows = Math.Min((maximum + 1) * 4, RelationshipLimits.MaximumRelationshipsPerFile);
        var relatedRows = await _source.GetRelatedFilesAsync(identity.FileId, requestedRows, cancellationToken).ConfigureAwait(false);
        var related = RelationshipPairAggregator.ToRelatedFiles(RelationshipPairAggregator.Aggregate(
            relatedRows
                .Where(item => item.Relationship.Decision is not RelationshipDecision.Rejected and not RelationshipDecision.NeverRelate)
                .ToArray(),
            Math.Min(maximum + 1, ExplorerProtocolDefaults.MaximumRelatedResults + 1)));
        var relatedIds = related.Select(item => item.FileId).Distinct(StringComparer.Ordinal).ToArray();
        var documents = await _source.GetDocumentsByIdsAsync(relatedIds, cancellationToken).ConfigureAwait(false);
        var authorized = documents
            .Where(document => session.IsAuthorizedSource(document.SourceId))
            .ToDictionary(document => document.FileId, StringComparer.Ordinal);
        var eligible = related
            .Where(item => authorized.ContainsKey(item.FileId))
            .Take(maximum + 1)
            .ToArray();
        var nodes = new List<ExplorerNode>(Math.Min(maximum, eligible.Length));
        var edges = new List<ExplorerEdge>(Math.Min(maximum, eligible.Length));
        foreach (var item in eligible.Take(maximum))
        {
            var document = authorized[item.FileId];
            var node = CreateFileNode(session, document);
            nodes.Add(node);
            edges.Add(CreateRelationshipEdge(request.NodeId, node.Id, item.Relationship));
        }

        return new ExplorerRelatedResult(nodes, edges, eligible.Length > maximum);
    }

    public async Task<ExplorerNodeDetails> GetNodeDetailsAsync(
        ExplorerSessionContext session,
        ExplorerNodeDetailsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var identity = Resolve(session, request.NodeId);
        var source = await GetAuthorizedSourceAsync(session, identity.SourceId, cancellationToken).ConfigureAwait(false);
        if (identity.Kind is ExplorerNodeKind.Source or ExplorerNodeKind.Folder)
        {
            var documentBatch = await GetAuthorizedDocumentsAsync(session, identity.SourceId, cancellationToken).ConfigureAwait(false);
            var documents = documentBatch.Documents;
            var node = identity.Kind == ExplorerNodeKind.Source
                ? CreateSourceNode(session, source)
                : CreateFolderNode(
                    session,
                    source,
                    identity.RelativePath!,
                    RegisterParent(session, identity),
                    CountImmediateChildren(identity.RelativePath!, documents));
            return new ExplorerNodeDetails(
                node,
                null,
                null,
                null,
                [],
                [],
                null,
                [],
                !documentBatch.IsProjectionTruncated && documents.All(item => item.IsFullyIndexed));
        }

        var document = await GetAuthorizedDocumentAsync(session, identity.FileId!, cancellationToken).ConfigureAwait(false);
        var intelligence = document.ContentIntelligence;
        var related = await _source.GetRelatedFilesAsync(document.FileId, 8, cancellationToken).ConfigureAwait(false);
        var relatedDocuments = await _source.GetDocumentsByIdsAsync(
            related.Select(item => item.FileId).Distinct(StringComparer.Ordinal).ToArray(),
            cancellationToken).ConfigureAwait(false);
        var authorizedRelatedIds = relatedDocuments
            .Where(item => session.IsAuthorizedSource(item.SourceId))
            .Select(item => item.FileId)
            .ToHashSet(StringComparer.Ordinal);
        return new ExplorerNodeDetails(
            CreateFileNode(session, document),
            document.CreationTimeUtc,
            document.ModifiedTimeUtc,
            BoundOrNull(intelligence?.Summary?.Text ?? document.Summary, 512),
            ToConcepts(intelligence?.Topics, ExplorerProtocolDefaults.CreateLimits().MaximumTopics),
            ToConcepts(intelligence?.Entities, ExplorerProtocolDefaults.CreateLimits().MaximumEntities),
            ToMedia(document),
            related.Where(item => authorizedRelatedIds.Contains(item.FileId))
                .Select(item => Bound(item.Relationship.Explanation, ExplorerProtocolDefaults.MaximumReasonCharacters))
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Take(8)
                .ToArray(),
            document.IsFullyIndexed);
    }

    public async Task<ExplorerNeighborhood> GetNeighborhoodAsync(
        ExplorerSessionContext session,
        ExplorerNeighborhoodRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = Resolve(session, request.NodeId);
        var depth = BoundCount(request.Depth, 1, ExplorerProtocolDefaults.MaximumDepth);
        var maximumNodes = BoundCount(request.MaximumNodes, 100, ExplorerProtocolDefaults.MaximumNodes);
        var maximumEdges = BoundCount(request.MaximumEdges, 200, ExplorerProtocolDefaults.MaximumEdges);
        var nodes = new Dictionary<string, ExplorerNode>(StringComparer.Ordinal);
        var edges = new List<ExplorerEdge>(maximumEdges);
        var queue = new Queue<(string NodeId, int Depth)>();
        queue.Enqueue((request.NodeId, 0));
        var truncated = false;

        while (queue.Count > 0 && nodes.Count < maximumNodes && edges.Count < maximumEdges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (nodeId, currentDepth) = queue.Dequeue();
            var details = await GetNodeDetailsAsync(
                session,
                new ExplorerNodeDetailsRequest(nodeId),
                cancellationToken).ConfigureAwait(false);
            nodes.TryAdd(nodeId, details.Node);
            if (currentDepth >= depth || details.Node.Kind == ExplorerNodeKind.File)
            {
                continue;
            }

            var remainingNodeCapacity = maximumNodes - nodes.Count;
            if (remainingNodeCapacity <= 0)
            {
                truncated = true;
                continue;
            }

            var page = await GetChildrenAsync(
                session,
                new ExplorerChildrenRequest(nodeId, remainingNodeCapacity),
                cancellationToken).ConfigureAwait(false);
            truncated |= page.IsTruncated;
            foreach (var child in page.Nodes)
            {
                if (nodes.Count >= maximumNodes || edges.Count >= maximumEdges)
                {
                    truncated = true;
                    break;
                }

                nodes.TryAdd(child.Id, child);
                edges.Add(new ExplorerEdge(
                    nodeId,
                    child.Id,
                    ExplorerEdgeKind.Contains,
                    100,
                    "Indexed structural containment",
                    ExplorerEvidenceClass.Structural,
                    "OmniSorSe indexed structure"));
                queue.Enqueue((child.Id, currentDepth + 1));
            }
        }

        if (request.IncludeContext && edges.Count < maximumEdges)
        {
            var focus = Resolve(session, request.NodeId);
            if (focus.Kind == ExplorerNodeKind.File)
            {
                var remainingNodeCapacity = maximumNodes - nodes.Count;
                var remainingEdgeCapacity = maximumEdges - edges.Count;
                if (remainingNodeCapacity <= 0 || remainingEdgeCapacity <= 0)
                {
                    truncated = true;
                    return new ExplorerNeighborhood(request.NodeId, nodes.Values.ToArray(), edges.ToArray(), truncated, null);
                }

                var related = await GetRelatedAsync(
                    session,
                    new ExplorerRelatedRequest(request.NodeId, Math.Min(remainingNodeCapacity, remainingEdgeCapacity)),
                    cancellationToken).ConfigureAwait(false);
                foreach (var node in related.Nodes)
                {
                    nodes.TryAdd(node.Id, node);
                }

                edges.AddRange(related.Edges.Take(maximumEdges - edges.Count));
                truncated |= related.IsTruncated;
            }
        }

        return new ExplorerNeighborhood(request.NodeId, nodes.Values.ToArray(), edges.ToArray(), truncated, null);
    }

    private async Task<IndexingSource> GetAuthorizedSourceAsync(
        ExplorerSessionContext session,
        string sourceId,
        CancellationToken cancellationToken)
    {
        if (!session.IsAuthorizedSource(sourceId))
        {
            throw new ExplorerProtocolException(ExplorerErrorCode.OutOfScope, "The requested node is outside the authorized indexed scope.");
        }

        var sources = await _source.GetSourcesAsync(cancellationToken).ConfigureAwait(false);
        return sources.FirstOrDefault(source => string.Equals(source.Id, sourceId, StringComparison.Ordinal)) ??
            throw new ExplorerProtocolException(ExplorerErrorCode.NodeNotFound, "The requested indexed source is no longer available.");
    }

    private async Task<AuthorizedDocumentBatch> GetAuthorizedDocumentsAsync(
        ExplorerSessionContext session,
        string sourceId,
        CancellationToken cancellationToken)
    {
        if (!session.IsAuthorizedSource(sourceId))
        {
            throw new ExplorerProtocolException(ExplorerErrorCode.OutOfScope, "The requested node is outside the authorized indexed scope.");
        }

        var documents = await _source.GetDocumentsAsync(
            ExplorerProtocolDefaults.MaximumDocumentsExamined,
            cancellationToken).ConfigureAwait(false);
        return new AuthorizedDocumentBatch(
            documents.Where(document =>
                    string.Equals(document.SourceId, sourceId, StringComparison.Ordinal) &&
                    !document.IsExcluded)
                .ToArray(),
            documents.Count >= ExplorerProtocolDefaults.MaximumDocumentsExamined);
    }

    private async Task<ProgressiveSearchDocument> GetAuthorizedDocumentAsync(
        ExplorerSessionContext session,
        string fileId,
        CancellationToken cancellationToken)
    {
        var documents = await _source.GetDocumentsByIdsAsync([fileId], cancellationToken).ConfigureAwait(false);
        var document = documents.FirstOrDefault(item => string.Equals(item.FileId, fileId, StringComparison.Ordinal));
        if (document is null)
        {
            throw new ExplorerProtocolException(ExplorerErrorCode.NodeNotFound, "The requested indexed file is no longer available.");
        }

        if (!session.IsAuthorizedSource(document.SourceId))
        {
            throw new ExplorerProtocolException(ExplorerErrorCode.OutOfScope, "The requested node is outside the authorized indexed scope.");
        }

        return document;
    }

    private IReadOnlyList<ExplorerNode> BuildImmediateChildren(
        ExplorerSessionContext session,
        IndexingSource source,
        ExplorerNodeIdentity parent,
        IReadOnlyList<ProgressiveSearchDocument> documents)
    {
        var parentRelative = NormalizeRelative(parent.RelativePath);
        var folders = new SortedDictionary<string, (string Name, HashSet<string> Children)>(StringComparer.OrdinalIgnoreCase);
        var files = new List<ProgressiveSearchDocument>();
        foreach (var document in documents)
        {
            var relative = NormalizeRelative(document.RelativePath);
            if (relative is null || !TryGetRemainder(parentRelative, relative, out var remainder))
            {
                continue;
            }

            var separator = remainder.IndexOf('/');
            if (separator >= 0)
            {
                var name = remainder[..separator];
                var folderRelative = string.IsNullOrEmpty(parentRelative) ? name : parentRelative + "/" + name;
                if (!folders.TryGetValue(folderRelative, out var folder))
                {
                    folder = (name, new HashSet<string>(_paths.Comparer));
                    folders.Add(folderRelative, folder);
                }

                var descendant = remainder[(separator + 1)..];
                if (descendant.Length > 0)
                {
                    var nextSeparator = descendant.IndexOf('/');
                    folder.Children.Add(nextSeparator < 0 ? descendant : descendant[..nextSeparator]);
                }
            }
            else if (remainder.Length > 0)
            {
                files.Add(document);
            }
        }

        var parentId = session.RegisterNode(parent);
        return folders.Select(pair => CreateFolderNode(session, source, pair.Key, parentId, pair.Value.Children.Count))
            .Concat(files.OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(file => file.FileId, StringComparer.Ordinal)
                .Select(file => CreateFileNode(session, file, parentId)))
            .ToArray();
    }

    private ExplorerNode CreateSourceNode(ExplorerSessionContext session, IndexingSource source)
    {
        var identity = new ExplorerNodeIdentity(ExplorerNodeKind.Source, source.Id);
        var id = session.RegisterNode(identity);
        return new ExplorerNode(
            id,
            Bound(source.DisplayName, 256),
            ExplorerNodeKind.Source,
            null,
            null,
            null,
            session.IncludeAuthorizedPaths ? source.RootPath : null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Indexing level"] = source.Level.ToString(),
                ["Enabled"] = source.Enabled ? "Yes" : "No",
            },
            0,
            0);
    }

    private static string RegisterParent(ExplorerSessionContext session, ExplorerNodeIdentity identity)
    {
        var separator = identity.RelativePath!.LastIndexOf('/');
        var parent = separator < 0
            ? new ExplorerNodeIdentity(ExplorerNodeKind.Source, identity.SourceId)
            : new ExplorerNodeIdentity(ExplorerNodeKind.Folder, identity.SourceId, identity.RelativePath[..separator]);
        return session.RegisterNode(parent);
    }

    private ExplorerNode CreateFolderNode(
        ExplorerSessionContext session,
        IndexingSource source,
        string relativePath,
        string parentId,
        int childCount)
    {
        var normalized = NormalizeRelative(relativePath) ??
            throw new ExplorerProtocolException(ExplorerErrorCode.InternalFailure, "An indexed folder path could not be projected safely.");
        var identity = new ExplorerNodeIdentity(ExplorerNodeKind.Folder, source.Id, normalized);
        var id = session.RegisterNode(identity);
        var name = normalized.Split('/').Last();
        return new ExplorerNode(
            id,
            Bound(name, 256),
            ExplorerNodeKind.Folder,
            parentId,
            null,
            null,
            session.IncludeAuthorizedPaths ? SafeFolderPath(source.RootPath, normalized) : null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            childCount,
            0);
    }

    private int CountImmediateChildren(
        string relativePath,
        IReadOnlyList<ProgressiveSearchDocument> documents)
    {
        var normalized = NormalizeRelative(relativePath) ??
            throw new ExplorerProtocolException(ExplorerErrorCode.InternalFailure, "An indexed folder path could not be projected safely.");
        var prefix = normalized + "/";
        var children = new HashSet<string>(_paths.Comparer);
        foreach (var document in documents)
        {
            var item = NormalizeRelative(document.RelativePath);
            if (item is null || !item.StartsWith(prefix, _paths.Comparison))
            {
                continue;
            }

            var remainder = item[prefix.Length..];
            if (remainder.Length == 0)
            {
                continue;
            }

            var separator = remainder.IndexOf('/');
            children.Add(separator < 0 ? remainder : remainder[..separator]);
        }

        return children.Count;
    }

    private ExplorerNode CreateFileNode(
        ExplorerSessionContext session,
        ProgressiveSearchDocument document,
        string? knownParentId = null)
    {
        if (!session.IsAuthorizedSource(document.SourceId) || string.IsNullOrWhiteSpace(document.SourceId))
        {
            throw new ExplorerProtocolException(ExplorerErrorCode.OutOfScope, "The requested node is outside the authorized indexed scope.");
        }

        var relative = NormalizeRelative(document.RelativePath) ?? document.FileName;
        var parentRelative = relative.Contains('/') ? relative[..relative.LastIndexOf('/')] : null;
        var parentId = knownParentId ?? (parentRelative is null
            ? session.RegisterNode(new ExplorerNodeIdentity(ExplorerNodeKind.Source, document.SourceId))
            : session.RegisterNode(new ExplorerNodeIdentity(ExplorerNodeKind.Folder, document.SourceId, parentRelative)));
        var id = session.RegisterNode(new ExplorerNodeIdentity(
            ExplorerNodeKind.File,
            document.SourceId,
            relative,
            document.FileId));
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(document.FileType))
        {
            metadata["Type"] = Bound(document.FileType, 80);
        }

        metadata["Index state"] = document.IsFullyIndexed ? "Complete" : "Incomplete";
        return new ExplorerNode(
            id,
            Bound(document.FileName, 256),
            ExplorerNodeKind.File,
            parentId,
            BoundOrNull(document.Extension, 32),
            document.Length,
            session.IncludeAuthorizedPaths ? document.FullPath : null,
            metadata,
            0,
            0);
    }

    private ExplorerNodeIdentity Resolve(ExplorerSessionContext session, string nodeId) =>
        session.TryResolveNode(nodeId, out var identity) && identity is not null
            ? identity
            : throw new ExplorerProtocolException(
                ExplorerErrorCode.OutOfScope,
                "The opaque node identifier was not issued for this authorized session.");

    private static ExplorerEdge CreateRelationshipEdge(
        string sourceNodeId,
        string targetNodeId,
        FileRelationship relationship)
    {
        var firstEvidence = relationship.Evidence.FirstOrDefault();
        var hasUserAuthority = relationship.IsManual ||
            relationship.Decision is RelationshipDecision.Confirmed or RelationshipDecision.AlwaysRelate;
        var kind = firstEvidence?.Kind switch
        {
            RelationshipEvidenceKind.OcrText or RelationshipEvidenceKind.MediaOcr => ExplorerEdgeKind.Ocr,
            RelationshipEvidenceKind.MediaTranscript => ExplorerEdgeKind.Transcript,
            RelationshipEvidenceKind.ContentTopic => ExplorerEdgeKind.Topic,
            RelationshipEvidenceKind.ContentEntity => ExplorerEdgeKind.Entity,
            RelationshipEvidenceKind.Timestamp => ExplorerEdgeKind.Temporal,
            _ => ExplorerEdgeKind.Related,
        };
        var evidenceClass = firstEvidence?.Origin is RelationshipEvidenceOrigin.Derived or
            RelationshipEvidenceOrigin.AiDerived
                ? ExplorerEvidenceClass.Derived
                : ExplorerEvidenceClass.Deterministic;
        var strength = relationship.Confidence switch
        {
            RelationshipConfidence.Confirmed => 100,
            RelationshipConfidence.High => 80,
            RelationshipConfidence.Medium => 60,
            _ => 30,
        };
        return new ExplorerEdge(
            sourceNodeId,
            targetNodeId,
            kind,
            strength,
            Bound(
                hasUserAuthority
                    ? $"Related by explicit user authority. {relationship.Explanation}"
                    : relationship.Explanation,
                ExplorerProtocolDefaults.MaximumReasonCharacters),
            evidenceClass,
            hasUserAuthority
                ? "OmniSorSe user relationship authority"
                : Bound($"{relationship.Algorithm} {relationship.AlgorithmVersion}", 128));
    }

    private static IReadOnlyList<ExplorerConcept> ToConcepts(
        IReadOnlyList<ContentConcept>? concepts,
        int maximum) =>
        concepts?.Take(maximum).Select(item => new ExplorerConcept(
            Bound(item.DisplayName, 128),
            item.Kind.ToString(),
            item.Confidence.ToString(),
            item.Origin == ContentIntelligenceOrigin.AiDerived,
            Bound(item.Provider, 80))).ToArray() ?? [];

    private static ExplorerMediaDetails? ToMedia(ProgressiveSearchDocument document)
    {
        var evidence = document.MediaEvidence;
        if (evidence is null)
        {
            return null;
        }

        var metadata = evidence.Metadata;
        var device = string.Join(' ', new[] { metadata.DeviceMake, metadata.DeviceModel }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return new ExplorerMediaDetails(
            evidence.Kind.ToString(),
            BoundOrNull(metadata.Container, 64),
            metadata.Width,
            metadata.Height,
            metadata.Duration?.TotalSeconds,
            BoundOrNull(device, 128),
            metadata.CapturedAtUtc,
            BoundOrNull(metadata.VideoCodec, 64),
            BoundOrNull(metadata.AudioCodec, 64),
            !string.IsNullOrWhiteSpace(evidence.OcrText),
            !string.IsNullOrWhiteSpace(evidence.Transcript));
    }

    private string? SafeFolderPath(string rootPath, string relativePath)
    {
        try
        {
            var combined = Path.GetFullPath(Path.Combine(
                rootPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            return _paths.IsWithinRoot(rootPath, combined) ? combined : null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    private string? NormalizeRelative(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (normalized.Length > 4096 ||
            normalized.Split('/').Any(segment =>
                segment.Length is 0 or > 255 ||
                segment is "." or ".." ||
                segment.Any(character => character == '\0' || char.IsControl(character))))
        {
            return null;
        }

        return normalized;
    }

    private bool TryGetRemainder(string? parent, string relative, out string remainder)
    {
        parent ??= string.Empty;
        if (parent.Length == 0)
        {
            remainder = relative;
            return true;
        }

        var prefix = parent + "/";
        if (relative.StartsWith(prefix, _paths.Comparison))
        {
            remainder = relative[prefix.Length..];
            return true;
        }

        remainder = string.Empty;
        return false;
    }

    private static int BoundCount(int? requested, int fallback, int maximum)
    {
        var value = requested ?? fallback;
        if (value <= 0)
        {
            throw new ExplorerProtocolException(ExplorerErrorCode.LimitExceeded, "The requested limit must be positive.");
        }

        return Math.Min(value, maximum);
    }

    private static int ParseContinuation(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return 0;
        }

        if (token.Length > 24 ||
            !token.StartsWith("o:", StringComparison.Ordinal) ||
            !int.TryParse(token.AsSpan(2), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var offset) ||
            offset < 0 || offset > ExplorerProtocolDefaults.MaximumDocumentsExamined)
        {
            throw new ExplorerProtocolException(ExplorerErrorCode.MalformedRequest, "The continuation token is invalid.");
        }

        return offset;
    }

    private static string FormatContinuation(int offset) =>
        "o:" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Bound(string? value, int maximum) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= maximum ? value : value[..maximum];

    private static string? BoundOrNull(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? null : Bound(value.Trim(), maximum);
}
