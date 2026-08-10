using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.KnowledgeGraph;

namespace OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

public sealed partial class SqliteGraphStore
{
    private const string ActiveNodesCte =
        """
        WITH active_node_rows AS (
            SELECT n.*,
                   c.freshness_state AS component_freshness_state,
                   c.integrity_state AS component_integrity_state,
                   ROW_NUMBER() OVER (
                       PARTITION BY n.node_id
                       ORDER BY n.last_validated_utc_ticks DESC, n.component_key, n.generation DESC) AS row_rank
            FROM graph_nodes n
            JOIN graph_components c
              ON c.component_key = n.component_key AND c.active_generation = n.generation
            WHERE n.is_visible = 1
              AND c.freshness_state = 'Current' AND c.integrity_state = 'Valid'
              AND NOT EXISTS (
                  SELECT 1 FROM graph_decision_suppressions s
                  WHERE (s.suppression_kind = 'node' AND s.stable_id = n.node_id)
                     OR (s.suppression_kind = 'canonical' AND s.stable_id = n.canonical_key)
                     OR (s.suppression_kind = 'source' AND s.stable_id = n.source_id)
                     OR s.suppression_kind = 'all')
              AND NOT EXISTS (
                  SELECT 1
                  FROM graph_privacy_exclusions x
                  WHERE x.scope_kind = 'All'
                     OR (x.scope_kind = 'Node' AND x.stable_id = n.node_id)
                     OR (x.scope_kind = 'Source' AND x.stable_id = n.source_id)
                     OR (x.scope_kind IN ('File', 'Collection') AND EXISTS (
                         SELECT 1
                         FROM graph_nodes scoped
                         WHERE scoped.component_key = n.component_key
                           AND scoped.generation = n.generation
                           AND scoped.node_type = lower(x.scope_kind)
                           AND (scoped.node_id = x.stable_id OR scoped.canonical_key = x.stable_id))))
        ),
        active_nodes AS (SELECT * FROM active_node_rows WHERE row_rank = 1)
        """;

    /// <inheritdoc />
    public Task<GraphPage<GraphNode>> GetNodesAsync(
        GraphNodeQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var pageSize = Math.Clamp(query.PageSize, 1, GraphLimits.MaximumPageSize);
        var cursor = ValidateCursor(query.Cursor);
        var prefix = query.NormalizedLabelPrefix is null
            ? null
            : NormalizeLabel(Bound(query.NormalizedLabelPrefix, GraphLimits.MaximumLabelCharacters));
        return RunReadAsync(
            () =>
            {
                using var connection = OpenReadConnection();
                if (!ReadQueriesAllowed(connection))
                {
                    return new GraphPage<GraphNode>([], null, 0);
                }

                var where =
                    "WHERE ($kind IS NULL OR node_type = $kind) " +
                    "AND ($prefix IS NULL OR (normalized_label >= $prefix AND normalized_label < $prefixUpper)) " +
                    "AND ($freshness IS NULL OR component_freshness_state = $freshness) " +
                    "AND ($integrity IS NULL OR component_integrity_state = $integrity) " +
                    "AND ($cursor IS NULL OR node_id > $cursor)";
                var total = Convert.ToInt64(
                    ExecuteScalarWithActiveCte(
                        connection,
                        $"SELECT COUNT(*) FROM active_nodes {where};",
                        QueryParameters(query, prefix, cursor: null)),
                    CultureInfo.InvariantCulture);
                using var command = connection.CreateCommand();
                command.CommandText = ActiveNodesCte +
                    $" SELECT * FROM active_nodes {where} ORDER BY node_id LIMIT $limit;";
                AddParameters(command, QueryParameters(query, prefix, cursor));
                command.Parameters.AddWithValue("$limit", pageSize + 1);
                var nodes = new List<GraphNode>(pageSize + 1);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    nodes.Add(ReadNode(reader));
                }

                var hasMore = nodes.Count > pageSize;
                if (hasMore)
                {
                    nodes.RemoveAt(nodes.Count - 1);
                }

                return new GraphPage<GraphNode>(
                    nodes,
                    hasMore ? new GraphPageCursor(nodes[^1].Identity.NodeId) : null,
                    total);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphNodeDetails?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        ValidateStableId(nodeId, nameof(nodeId));
        return RunReadAsync(
            () =>
            {
                using var connection = OpenReadConnection();
                if (!ReadQueriesAllowed(connection))
                {
                    return null;
                }

                GraphNode? node = null;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = ActiveNodesCte + " SELECT * FROM active_nodes WHERE node_id = $node;";
                    command.Parameters.AddWithValue("$node", nodeId);
                    using var reader = command.ExecuteReader();
                    if (reader.Read())
                    {
                        node = ReadNode(reader);
                    }
                }

                if (node is null)
                {
                    return null;
                }

                var aliases = new List<string>();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = ActiveNodesCte +
                        """
                        SELECT DISTINCT a.display_alias
                        FROM graph_aliases a
                        JOIN graph_components c ON c.component_key = a.component_key AND c.active_generation = a.generation
                        JOIN active_nodes visible
                          ON visible.node_id = a.node_id
                         AND visible.component_key = a.component_key
                         AND visible.generation = a.generation
                        WHERE a.node_id = $node
                          AND c.freshness_state = 'Current' AND c.integrity_state = 'Valid'
                        ORDER BY a.normalized_alias, a.display_alias
                        LIMIT $maximum;
                        """;
                    command.Parameters.AddWithValue("$node", nodeId);
                    command.Parameters.AddWithValue("$maximum", GraphLimits.MaximumAliasesPerNode);
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        aliases.Add(reader.GetString(0));
                    }
                }

                using var count = connection.CreateCommand();
                count.CommandText = ActiveNodesCte +
                    """
                    SELECT
                        COUNT(DISTINCT CASE WHEN e.target_node_id = $node THEN e.edge_id END),
                        COUNT(DISTINCT CASE WHEN e.source_node_id = $node THEN e.edge_id END)
                    FROM graph_edges e
                    JOIN graph_components c ON c.component_key = e.component_key AND c.active_generation = e.generation
                    JOIN active_nodes source_node ON source_node.node_id = e.source_node_id
                    JOIN active_nodes target_node ON target_node.node_id = e.target_node_id
                    WHERE (e.source_node_id = $node OR e.target_node_id = $node)
                      AND c.freshness_state = 'Current' AND c.integrity_state = 'Valid'
                      AND NOT EXISTS (
                          SELECT 1 FROM graph_decision_suppressions s
                          WHERE s.suppression_kind = 'edge' AND s.stable_id = e.edge_id)
                      AND NOT EXISTS (
                          SELECT 1 FROM graph_decision_suppressions s
                          WHERE s.suppression_kind = 'relationship'
                            AND s.stable_id = graph_relationship_key(
                                e.source_node_id, e.target_node_id, e.edge_type,
                                CASE WHEN source_node.source_id IS NOT NULL AND source_node.source_id = target_node.source_id
                                     THEN 'source:' || source_node.source_id ELSE 'cross-source' END));
                    """;
                count.Parameters.AddWithValue("$node", nodeId);
                using var counts = count.ExecuteReader();
                counts.Read();
                return new GraphNodeDetails(node, aliases, counts.GetInt32(0), counts.GetInt32(1));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphEdge?> GetEdgeAsync(string edgeId, CancellationToken cancellationToken = default)
    {
        ValidateStableId(edgeId, nameof(edgeId));
        return RunReadAsync(
            () =>
            {
                using var connection = OpenReadConnection();
                if (!ReadQueriesAllowed(connection))
                {
                    return null;
                }

                using var command = connection.CreateCommand();
                command.CommandText = ActiveNodesCte +
                    """
                    SELECT e.edge_id, e.source_node_id, e.target_node_id, e.edge_type, e.confidence,
                           e.origin, e.algorithm_name, e.algorithm_version, e.input_fingerprint,
                           e.created_utc_ticks, e.last_validated_utc_ticks,
                           c.freshness_state, c.integrity_state, e.is_manual
                    FROM graph_edges e
                    JOIN graph_components c
                      ON c.component_key = e.component_key AND c.active_generation = e.generation
                    JOIN active_nodes source_node ON source_node.node_id = e.source_node_id
                    JOIN active_nodes target_node ON target_node.node_id = e.target_node_id
                    WHERE e.edge_id = $edge
                      AND c.freshness_state = $current AND c.integrity_state = $valid
                      AND e.freshness_state = $current AND e.integrity_state = $valid
                      AND NOT EXISTS (
                          SELECT 1 FROM graph_decision_suppressions s
                          WHERE s.suppression_kind = 'edge' AND s.stable_id = e.edge_id)
                      AND NOT EXISTS (
                          SELECT 1 FROM graph_decision_suppressions s
                          WHERE s.suppression_kind = 'relationship'
                            AND s.stable_id = graph_relationship_key(
                                e.source_node_id, e.target_node_id, e.edge_type,
                                CASE WHEN source_node.source_id IS NOT NULL AND source_node.source_id = target_node.source_id
                                     THEN 'source:' || source_node.source_id ELSE 'cross-source' END))
                    ORDER BY e.last_validated_utc_ticks DESC, e.component_key, e.generation DESC
                    LIMIT 1;
                    """;
                AddParameters(command, ("$edge", edgeId), ("$current", State(GraphFreshnessState.Current)),
                    ("$valid", State(GraphIntegrityState.Valid)));
                GraphEdge edge;
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    edge = new GraphEdge
                    {
                        Id = reader.GetString(0),
                        SourceNodeId = reader.GetString(1),
                        TargetNodeId = reader.GetString(2),
                        Kind = new GraphEdgeKind(reader.GetString(3)),
                        Confidence = Parse<GraphConfidenceLevel>(reader.GetString(4)),
                        Origin = Parse<GraphOrigin>(reader.GetString(5)),
                        Algorithm = reader.GetString(6),
                        AlgorithmVersion = reader.GetString(7),
                        InputFingerprint = reader.GetString(8),
                        CreatedAtUtc = new DateTimeOffset(reader.GetInt64(9), TimeSpan.Zero),
                        LastValidatedAtUtc = new DateTimeOffset(reader.GetInt64(10), TimeSpan.Zero),
                        Freshness = Parse<GraphFreshnessState>(reader.GetString(11)),
                        Integrity = Parse<GraphIntegrityState>(reader.GetString(12)),
                        IsManual = reader.GetBoolean(13),
                        EvidenceIds = [],
                    };
                }

                return edge with
                {
                    EvidenceIds = ReadEvidence(connection, edgeId, GraphLimits.MaximumEvidencePerEdge)
                        .Select(item => item.Id)
                        .ToArray(),
                };
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphMention?> GetMentionAsync(string mentionId, CancellationToken cancellationToken = default)
    {
        ValidateStableId(mentionId, nameof(mentionId));
        return RunReadAsync(
            () =>
            {
                using var connection = OpenReadConnection();
                if (!ReadQueriesAllowed(connection))
                {
                    return null;
                }

                using var command = connection.CreateCommand();
                command.CommandText = ActiveNodesCte +
                    """
                    SELECT m.mention_id, m.suggestion_kind, m.source_stable_key, m.identity_scope,
                           m.bounded_label, m.normalized_key, m.extractor_version,
                           m.evidence_ids_json, m.is_confirmed
                    FROM graph_mentions m
                    JOIN graph_components c
                      ON c.component_key = m.component_key AND c.active_generation = m.generation
                    WHERE m.mention_id = $mention
                      AND c.freshness_state = $current AND c.integrity_state = $valid
                      AND EXISTS (
                          SELECT 1 FROM active_nodes visible
                          WHERE visible.component_key = m.component_key AND visible.generation = m.generation)
                      AND NOT EXISTS (
                          SELECT 1 FROM graph_decision_suppressions s
                          WHERE s.suppression_kind = 'mention' AND s.stable_id = m.mention_id)
                    ORDER BY m.component_key, m.generation DESC
                    LIMIT 1;
                    """;
                AddParameters(command, ("$mention", mentionId), ("$current", State(GraphFreshnessState.Current)),
                    ("$valid", State(GraphIntegrityState.Valid)));
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    return null;
                }

                var evidence = JsonSerializer.Deserialize<string[]>(reader.GetString(7), ProjectionJsonOptions) ?? [];
                return new GraphMention
                {
                    Id = reader.GetString(0),
                    Kind = Parse<GraphSuggestionKind>(reader.GetString(1)),
                    SourceStableKey = reader.GetString(2),
                    Scope = reader.GetString(3),
                    Label = reader.GetString(4),
                    NormalizedKey = reader.GetString(5),
                    ExtractorVersion = reader.GetString(6),
                    EvidenceIds = evidence,
                    IsConfirmed = reader.GetBoolean(8),
                };
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphPage<GraphFact>> GetFactsAsync(
        GraphFactQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateStableId(query.NodeId, nameof(query));
        var cursor = ValidateCursor(query.Cursor);
        var pageSize = Math.Clamp(query.PageSize, 1, GraphLimits.MaximumPageSize);
        return RunReadAsync(
            () =>
            {
                using var connection = OpenReadConnection();
                if (!ReadQueriesAllowed(connection))
                {
                    return new GraphPage<GraphFact>([], null, 0);
                }

                var parameters = new (string Name, object? Value)[]
                {
                    ("$node", query.NodeId),
                    ("$kind", query.Kind?.Value),
                    ("$cursor", cursor),
                };
                var where =
                    "WHERE f.node_id = $node AND ($kind IS NULL OR f.fact_kind = $kind) " +
                    "AND ($cursor IS NULL OR f.fact_id > $cursor)";
                var total = Convert.ToInt64(
                    ExecuteScalarWithActiveCte(
                        connection,
                        $"SELECT COUNT(*) FROM graph_facts f JOIN active_nodes n ON n.node_id = f.node_id AND n.component_key = f.component_key AND n.generation = f.generation {where};",
                        parameters),
                    CultureInfo.InvariantCulture);
                using var command = connection.CreateCommand();
                command.CommandText = ActiveNodesCte +
                    $" SELECT f.fact_id, f.node_id, f.fact_kind, f.canonical_value, f.evidence_ids_json, f.algorithm_version " +
                    $"FROM graph_facts f JOIN active_nodes n ON n.node_id = f.node_id AND n.component_key = f.component_key AND n.generation = f.generation {where} " +
                    "ORDER BY f.fact_id LIMIT $limit;";
                AddParameters(command, parameters);
                command.Parameters.AddWithValue("$limit", pageSize + 1);
                var facts = new List<GraphFact>(pageSize + 1);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    facts.Add(ReadFact(reader));
                }

                var hasMore = facts.Count > pageSize;
                if (hasMore)
                {
                    facts.RemoveAt(facts.Count - 1);
                }

                return new GraphPage<GraphFact>(
                    facts,
                    hasMore ? new GraphPageCursor(facts[^1].Id) : null,
                    total);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphPage<GraphTimelineEntry>> GetTimelineAsync(
        GraphTimelineQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateStableId(query.NodeId, nameof(query));
        if (query.FromUtc > query.ToUtc)
        {
            throw new ArgumentException("The graph timeline start cannot be later than its end.", nameof(query));
        }

        var cursor = ValidateCursor(query.Cursor);
        var pageSize = Math.Clamp(query.PageSize, 1, GraphLimits.MaximumPageSize);
        return RunReadAsync(
            () =>
            {
                using var connection = OpenReadConnection();
                if (!ReadQueriesAllowed(connection))
                {
                    return new GraphPage<GraphTimelineEntry>([], null, 0);
                }

                var from = query.FromUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
                var to = query.ToUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
                var parameters = new (string Name, object? Value)[]
                {
                    ("$node", query.NodeId),
                    ("$created", GraphFactKind.CreatedTimestamp.Value),
                    ("$modified", GraphFactKind.ModifiedTimestamp.Value),
                    ("$from", from),
                    ("$to", to),
                    ("$cursor", cursor),
                };
                var where =
                    "WHERE f.node_id = $node AND f.fact_kind IN ($created, $modified) " +
                    "AND ($from IS NULL OR f.canonical_value >= $from) AND ($to IS NULL OR f.canonical_value <= $to) " +
                    "AND ($cursor IS NULL OR f.fact_id > $cursor)";
                var total = Convert.ToInt64(
                    ExecuteScalarWithActiveCte(
                        connection,
                        $"SELECT COUNT(*) FROM graph_facts f JOIN active_nodes n ON n.node_id = f.node_id AND n.component_key = f.component_key AND n.generation = f.generation {where};",
                        parameters),
                    CultureInfo.InvariantCulture);
                using var command = connection.CreateCommand();
                command.CommandText = ActiveNodesCte +
                    $" SELECT f.fact_id, f.node_id, f.fact_kind, f.canonical_value, f.evidence_ids_json, f.algorithm_version " +
                    $"FROM graph_facts f JOIN active_nodes n ON n.node_id = f.node_id AND n.component_key = f.component_key AND n.generation = f.generation {where} " +
                    "ORDER BY f.fact_id LIMIT $limit;";
                AddParameters(command, parameters);
                command.Parameters.AddWithValue("$limit", pageSize + 1);
                var entries = new List<GraphTimelineEntry>(pageSize + 1);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fact = ReadFact(reader);
                    if (!DateTimeOffset.TryParseExact(
                            fact.CanonicalValue,
                            "O",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind,
                            out var occurredAt))
                    {
                        throw SqliteKnowledgeInfrastructure.Corrupt("A retained graph timeline timestamp is malformed.");
                    }

                    entries.Add(new GraphTimelineEntry(
                        fact.Id,
                        fact.SubjectNodeId,
                        fact.Kind,
                        occurredAt.ToUniversalTime(),
                        fact.EvidenceIds,
                        fact.AlgorithmVersion));
                }

                var hasMore = entries.Count > pageSize;
                if (hasMore)
                {
                    entries.RemoveAt(entries.Count - 1);
                }

                return new GraphPage<GraphTimelineEntry>(
                    entries,
                    hasMore ? new GraphPageCursor(entries[^1].FactId) : null,
                    total);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphPage<GraphNeighbor>> GetNeighborsAsync(
        GraphNeighborQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateStableId(query.NodeId, nameof(query));
        if (query.Depth < 1 || query.Depth > GraphLimits.MaximumExperimentalTraversalDepth ||
            query.Depth > GraphLimits.StableTraversalDepth && !query.ExperimentalTraversal)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Traversal depth requires explicit experimental mode and is bounded to two hops.");
        }

        var visitLimit = query.ExperimentalTraversal
            ? GraphLimits.MaximumExperimentalTraversalNodes
            : GraphLimits.MaximumStableTraversalNodes;
        var pageSize = Math.Min(Math.Clamp(query.PageSize, 1, GraphLimits.MaximumPageSize), visitLimit);
        var cursor = ValidateCursor(query.Cursor);
        return RunReadAsync(
            () =>
            {
                using var connection = OpenReadConnection();
                if (!ReadQueriesAllowed(connection))
                {
                    return new GraphPage<GraphNeighbor>([], null, 0);
                }

                using var command = connection.CreateCommand();
                command.CommandText = ActiveNodesCte +
                    """
                    , active_edge_rows AS (
                        SELECT e.*,
                               ROW_NUMBER() OVER (
                                   PARTITION BY e.edge_id
                                   ORDER BY e.last_validated_utc_ticks DESC, e.component_key, e.generation DESC) AS edge_rank
                        FROM graph_edges e
                        JOIN graph_components c
                          ON c.component_key = e.component_key AND c.active_generation = e.generation
                        JOIN active_nodes source_node ON source_node.node_id = e.source_node_id
                        JOIN active_nodes target_node ON target_node.node_id = e.target_node_id
                        WHERE ($edgeKind IS NULL OR e.edge_type = $edgeKind)
                          AND c.freshness_state = 'Current' AND c.integrity_state = 'Valid'
                          AND e.freshness_state = 'Current' AND e.integrity_state = 'Valid'
                          AND NOT EXISTS (
                              SELECT 1 FROM graph_decision_suppressions s
                              WHERE s.suppression_kind = 'edge' AND s.stable_id = e.edge_id)
                          AND NOT EXISTS (
                              SELECT 1 FROM graph_decision_suppressions s
                              WHERE s.suppression_kind = 'relationship'
                                AND s.stable_id = graph_relationship_key(
                                    e.source_node_id, e.target_node_id, e.edge_type,
                                    CASE WHEN source_node.source_id IS NOT NULL AND source_node.source_id = target_node.source_id
                                         THEN 'source:' || source_node.source_id ELSE 'cross-source' END))
                    ),
                    active_edges AS (SELECT * FROM active_edge_rows WHERE edge_rank = 1),
                    walk(node_id, via_edge_id, depth, visited) AS (
                        SELECT CASE WHEN e.source_node_id = $seed THEN e.target_node_id ELSE e.source_node_id END,
                               e.edge_id,
                               1,
                               '|' || $seed || '|' || CASE WHEN e.source_node_id = $seed THEN e.target_node_id ELSE e.source_node_id END || '|'
                        FROM active_edges e
                        WHERE e.source_node_id = $seed OR e.target_node_id = $seed
                        UNION ALL
                        SELECT CASE WHEN e.source_node_id = w.node_id THEN e.target_node_id ELSE e.source_node_id END,
                               e.edge_id,
                               w.depth + 1,
                               w.visited || CASE WHEN e.source_node_id = w.node_id THEN e.target_node_id ELSE e.source_node_id END || '|'
                        FROM walk w
                        JOIN active_edges e ON e.source_node_id = w.node_id OR e.target_node_id = w.node_id
                        WHERE w.depth < $depth
                          AND instr(w.visited, '|' || CASE WHEN e.source_node_id = w.node_id THEN e.target_node_id ELSE e.source_node_id END || '|') = 0
                        LIMIT $visitLimit
                    ),
                    selected AS (
                        SELECT node_id, via_edge_id, depth,
                               ROW_NUMBER() OVER (PARTITION BY node_id ORDER BY depth, via_edge_id) AS selected_rank
                        FROM walk
                    )
                    SELECT n.*, e.component_key AS edge_component_key, e.generation AS edge_generation,
                           e.edge_id, e.source_node_id, e.target_node_id, e.edge_type, e.confidence,
                           e.origin AS edge_origin, e.algorithm_name AS edge_algorithm_name,
                           e.algorithm_version AS edge_algorithm_version, e.input_fingerprint,
                           e.created_utc_ticks AS edge_created_utc_ticks,
                           e.last_validated_utc_ticks AS edge_last_validated_utc_ticks,
                           e.freshness_state AS edge_freshness_state,
                           e.integrity_state AS edge_integrity_state, e.is_manual
                    FROM selected s
                    JOIN active_nodes n ON n.node_id = s.node_id
                    JOIN active_edges e ON e.edge_id = s.via_edge_id
                    WHERE s.selected_rank = 1
                      AND ($neighborKind IS NULL OR n.node_type = $neighborKind)
                      AND ($cursor IS NULL OR (n.node_id || '|' || e.edge_id) > $cursor)
                    ORDER BY n.node_id, e.edge_id
                    LIMIT $limit;
                    """;
                AddParameters(
                    command,
                    ("$edgeKind", query.EdgeKind?.Value), ("$seed", query.NodeId), ("$depth", query.Depth),
                    ("$neighborKind", query.NeighborKind?.Value), ("$cursor", cursor),
                    ("$visitLimit", visitLimit), ("$limit", pageSize + 1));
                var rows = new List<(GraphNode Node, GraphEdge Edge)>(pageSize + 1);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        rows.Add((ReadNode(reader), ReadEdge(reader, NodeColumnCount)));
                    }
                }

                var neighbors = new List<GraphNeighbor>(rows.Count);
                foreach (var row in rows)
                {
                    var evidence = ReadEvidence(connection, row.Edge.Id, GraphLimits.MaximumEvidencePerEdge);
                    neighbors.Add(new GraphNeighbor(
                        row.Node,
                        row.Edge with { EvidenceIds = evidence.Select(item => item.Id).ToArray() },
                        evidence));
                }

                var hasMore = neighbors.Count > pageSize;
                if (hasMore)
                {
                    neighbors.RemoveAt(neighbors.Count - 1);
                }

                var next = hasMore
                    ? new GraphPageCursor($"{neighbors[^1].Node.Identity.NodeId}|{neighbors[^1].Edge.Id}")
                    : null;
                return new GraphPage<GraphNeighbor>(neighbors, next, null);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphEvidenceReference>> GetEvidenceAsync(
        string edgeId,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ValidateStableId(edgeId, nameof(edgeId));
        maximumCount = Math.Clamp(maximumCount, 1, GraphLimits.MaximumEvidencePerEdge);
        return RunReadAsync<IReadOnlyList<GraphEvidenceReference>>(
            () =>
            {
                using var connection = OpenReadConnection();
                return ReadQueriesAllowed(connection) ? ReadEvidence(connection, edgeId, maximumCount) : [];
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphSearchExpansion>> GetSearchExpansionsAsync(
        IReadOnlyList<string> seedFileIds,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seedFileIds);
        if (seedFileIds.Count > GraphLimits.MaximumSearchSeeds)
        {
            throw new ArgumentOutOfRangeException(nameof(seedFileIds));
        }

        foreach (var seed in seedFileIds)
        {
            ValidateStableId(seed, nameof(seedFileIds));
        }

        maximumCount = Math.Clamp(maximumCount, 1, GraphLimits.MaximumGraphSearchExpansions);
        return RunReadAsync<IReadOnlyList<GraphSearchExpansion>>(
            () =>
            {
                using var connection = OpenReadConnection();
                if (!ReadQueriesAllowed(connection) || seedFileIds.Count == 0)
                {
                    return [];
                }

                using var command = connection.CreateCommand();
                var values = new List<string>();
                for (var index = 0; index < seedFileIds.Count; index++)
                {
                    var name = $"$seed{index.ToString(CultureInfo.InvariantCulture)}";
                    values.Add($"({name})");
                    command.Parameters.AddWithValue(name, seedFileIds[index]);
                }

                command.CommandText = ActiveNodesCte +
                    $"""
                    , seeds(file_id) AS (VALUES {string.Join(",", values)}),
                    seed_nodes AS (
                        SELECT s.file_id, n.node_id
                        FROM seeds s JOIN active_nodes n
                          ON n.node_type = 'file' AND n.canonical_key = s.file_id
                         AND n.component_freshness_state = 'Current' AND n.component_integrity_state = 'Valid'
                    ),
                    active_edge_rows AS (
                        SELECT e.*, ROW_NUMBER() OVER (PARTITION BY e.edge_id ORDER BY e.last_validated_utc_ticks DESC, e.component_key, e.generation DESC) AS edge_rank
                        FROM graph_edges e
                        JOIN graph_components c ON c.component_key = e.component_key AND c.active_generation = e.generation
                        JOIN active_nodes source_node ON source_node.node_id = e.source_node_id
                        JOIN active_nodes target_node ON target_node.node_id = e.target_node_id
                        WHERE c.freshness_state = 'Current' AND c.integrity_state = 'Valid'
                          AND NOT EXISTS (
                              SELECT 1 FROM graph_decision_suppressions s
                              WHERE s.suppression_kind = 'edge' AND s.stable_id = e.edge_id)
                          AND NOT EXISTS (
                              SELECT 1 FROM graph_decision_suppressions s
                              WHERE s.suppression_kind = 'relationship'
                                AND s.stable_id = graph_relationship_key(
                                    e.source_node_id, e.target_node_id, e.edge_type,
                                    CASE WHEN source_node.source_id IS NOT NULL AND source_node.source_id = target_node.source_id
                                         THEN 'source:' || source_node.source_id ELSE 'cross-source' END))
                    ),
                    active_edges AS (SELECT * FROM active_edge_rows WHERE edge_rank = 1),
                    candidates AS (
                        SELECT sn.file_id AS seed_file_id,
                               related.canonical_key AS related_file_id,
                               e.*,
                               ROW_NUMBER() OVER (
                                   PARTITION BY sn.file_id, related.canonical_key
                                   ORDER BY CASE e.confidence WHEN 'Confirmed' THEN 0 WHEN 'High' THEN 1 WHEN 'Medium' THEN 2 ELSE 3 END,
                                            e.edge_type, e.edge_id) AS candidate_rank
                        FROM seed_nodes sn
                        JOIN active_edges e ON e.source_node_id = sn.node_id OR e.target_node_id = sn.node_id
                        JOIN active_nodes related
                          ON related.node_id = CASE WHEN e.source_node_id = sn.node_id THEN e.target_node_id ELSE e.source_node_id END
                         AND related.node_type = 'file'
                          AND related.component_freshness_state = 'Current' AND related.component_integrity_state = 'Valid'
                        WHERE related.canonical_key <> sn.file_id
                    )
                    SELECT c.seed_file_id, c.related_file_id, c.edge_id, c.edge_type, c.confidence,
                           (
                               SELECT explanation FROM graph_evidence ev
                               WHERE ev.edge_id = c.edge_id AND ev.component_key = c.component_key AND ev.generation = c.generation
                               ORDER BY ev.evidence_id LIMIT 1),
                           COALESCE((SELECT MAX(snapshot_revision) FROM graph_runs), 0),
                           c.freshness_state
                    FROM candidates c
                    WHERE c.candidate_rank = 1
                      AND EXISTS (
                          SELECT 1 FROM graph_evidence ev
                          WHERE ev.edge_id = c.edge_id AND ev.component_key = c.component_key AND ev.generation = c.generation)
                    ORDER BY CASE c.confidence WHEN 'Confirmed' THEN 0 WHEN 'High' THEN 1 WHEN 'Medium' THEN 2 ELSE 3 END,
                             c.seed_file_id, c.related_file_id, c.edge_id
                    LIMIT $maximum;
                    """;
                command.Parameters.AddWithValue("$maximum", maximumCount);
                var results = new List<GraphSearchExpansion>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    results.Add(new GraphSearchExpansion(
                        reader.GetString(0), reader.GetString(1), reader.GetString(2),
                        new GraphEdgeKind(reader.GetString(3)), Parse<GraphConfidenceLevel>(reader.GetString(4)),
                        reader.GetString(5), reader.GetInt64(6), Parse<GraphFreshnessState>(reader.GetString(7))));
                }

                return results;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<GraphProjectionCoverage> GetCoverageAsync(CancellationToken cancellationToken = default) =>
        RunReadAsync(
            () =>
            {
                using var connection = OpenReadConnection();
                return ReadCoverage(connection);
            },
            cancellationToken);

    private static GraphNode ReadNode(SqliteDataReader reader)
    {
        var identity = new GraphIdentity(
            reader.GetString(reader.GetOrdinal("node_id")),
            new GraphNodeKind(reader.GetString(reader.GetOrdinal("node_type"))),
            reader.GetString(reader.GetOrdinal("identity_scope")),
            reader.GetString(reader.GetOrdinal("canonical_key")),
            reader.GetString(reader.GetOrdinal("normalization_version")),
            reader.GetString(reader.GetOrdinal("canonical_inputs")));
        return new GraphNode
        {
            Identity = identity,
            OwningSourceId = reader.IsDBNull(reader.GetOrdinal("source_id"))
                ? null
                : reader.GetString(reader.GetOrdinal("source_id")),
            DisplayLabel = reader.GetString(reader.GetOrdinal("display_label")),
            Origin = Parse<GraphOrigin>(reader.GetString(reader.GetOrdinal("origin"))),
            SourceManifestId = reader.GetString(reader.GetOrdinal("source_manifest_id")),
            ObservationHash = reader.GetString(reader.GetOrdinal("observation_hash")),
            Algorithm = reader.GetString(reader.GetOrdinal("algorithm_name")),
            AlgorithmVersion = reader.GetString(reader.GetOrdinal("algorithm_version")),
            CreatedAtUtc = new DateTimeOffset(reader.GetInt64(reader.GetOrdinal("created_utc_ticks")), TimeSpan.Zero),
            LastValidatedAtUtc = new DateTimeOffset(reader.GetInt64(reader.GetOrdinal("last_validated_utc_ticks")), TimeSpan.Zero),
            Freshness = Parse<GraphFreshnessState>(reader.GetString(reader.GetOrdinal("component_freshness_state"))),
            Integrity = Parse<GraphIntegrityState>(reader.GetString(reader.GetOrdinal("component_integrity_state"))),
            IsVisible = reader.GetBoolean(reader.GetOrdinal("is_visible")),
        };
    }

    private static GraphEdge ReadEdge(SqliteDataReader reader, int offset) => new()
    {
        Id = reader.GetString(offset + 2),
        SourceNodeId = reader.GetString(offset + 3),
        TargetNodeId = reader.GetString(offset + 4),
        Kind = new GraphEdgeKind(reader.GetString(offset + 5)),
        Confidence = Parse<GraphConfidenceLevel>(reader.GetString(offset + 6)),
        Origin = Parse<GraphOrigin>(reader.GetString(offset + 7)),
        Algorithm = reader.GetString(offset + 8),
        AlgorithmVersion = reader.GetString(offset + 9),
        InputFingerprint = reader.GetString(offset + 10),
        CreatedAtUtc = new DateTimeOffset(reader.GetInt64(offset + 11), TimeSpan.Zero),
        LastValidatedAtUtc = new DateTimeOffset(reader.GetInt64(offset + 12), TimeSpan.Zero),
        Freshness = Parse<GraphFreshnessState>(reader.GetString(offset + 13)),
        Integrity = Parse<GraphIntegrityState>(reader.GetString(offset + 14)),
        IsManual = reader.GetBoolean(offset + 15),
        EvidenceIds = [],
    };

    private static GraphFact ReadFact(SqliteDataReader reader)
    {
        string[] evidenceIds;
        try
        {
            evidenceIds = JsonSerializer.Deserialize<string[]>(reader.GetString(4), ProjectionJsonOptions) ?? [];
        }
        catch (JsonException exception)
        {
            throw new SqliteKnowledgeStoreException(
                SqliteKnowledgeFailureKind.Corrupt,
                "A retained graph fact has malformed evidence references.",
                exception);
        }

        if (evidenceIds.Length > GraphLimits.MaximumEvidencePerEdge ||
            evidenceIds.Any(item => string.IsNullOrWhiteSpace(item) || item.Length > GraphLimits.MaximumStableIdCharacters))
        {
            throw SqliteKnowledgeInfrastructure.Corrupt("A retained graph fact exceeds its bounded evidence-reference contract.");
        }

        return new GraphFact(
            reader.GetString(0),
            reader.GetString(1),
            new GraphFactKind(reader.GetString(2)),
            reader.GetString(3),
            evidenceIds,
            reader.GetString(5));
    }

    private static IReadOnlyList<GraphEvidenceReference> ReadEvidence(
        SqliteConnection connection,
        string edgeId,
        int maximumCount)
    {
        var evidence = new List<GraphEvidenceReference>();
        using var command = connection.CreateCommand();
        command.CommandText = ActiveNodesCte +
            """
            SELECT e.evidence_id, e.evidence_kind, e.source_evidence_key,
                   e.explanation_template_code, e.explanation, e.source_manifest_id, e.observation_hash
            FROM graph_evidence e
            JOIN graph_components c ON c.component_key = e.component_key AND c.active_generation = e.generation
            JOIN graph_edges g ON g.component_key = e.component_key AND g.generation = e.generation AND g.edge_id = e.edge_id
            JOIN active_nodes source_node ON source_node.node_id = g.source_node_id
            JOIN active_nodes target_node ON target_node.node_id = g.target_node_id
            WHERE e.edge_id = $edge
              AND c.freshness_state = 'Current' AND c.integrity_state = 'Valid'
              AND g.freshness_state = 'Current' AND g.integrity_state = 'Valid'
              AND NOT EXISTS (
                  SELECT 1 FROM graph_decision_suppressions s
                  WHERE s.suppression_kind = 'edge' AND s.stable_id = e.edge_id)
              AND NOT EXISTS (
                  SELECT 1 FROM graph_decision_suppressions s
                  WHERE s.suppression_kind = 'relationship'
                    AND s.stable_id = graph_relationship_key(
                        g.source_node_id, g.target_node_id, g.edge_type,
                        CASE WHEN source_node.source_id IS NOT NULL AND source_node.source_id = target_node.source_id
                             THEN 'source:' || source_node.source_id ELSE 'cross-source' END))
            ORDER BY e.evidence_id
            LIMIT $maximum;
            """;
        AddParameters(command, ("$edge", edgeId), ("$maximum", maximumCount));
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            evidence.Add(new GraphEvidenceReference
            {
                Id = reader.GetString(0),
                Kind = new GraphEvidenceKind(reader.GetString(1)),
                SourceEvidenceKey = reader.GetString(2),
                ExplanationTemplateCode = reader.GetString(3),
                Explanation = reader.GetString(4),
                SourceManifestId = reader.GetString(5),
                ObservationHash = reader.GetString(6),
            });
        }

        return evidence;
    }

    private static (string Name, object? Value)[] QueryParameters(GraphNodeQuery query, string? prefix, string? cursor) =>
    [
        ("$kind", query.Kind?.Value),
        ("$prefix", prefix),
        ("$prefixUpper", prefix is null ? null : prefix + '\uffff'),
        ("$freshness", query.Freshness?.ToString()),
        ("$integrity", query.Integrity?.ToString()),
        ("$cursor", cursor),
    ];

    private static object? ExecuteScalarWithActiveCte(
        SqliteConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = ActiveNodesCte + sql;
        AddParameters(command, parameters);
        return command.ExecuteScalar();
    }

    private static string? ValidateCursor(GraphPageCursor? cursor)
    {
        if (cursor is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(cursor.Value) || cursor.Value.Length > GraphLimits.MaximumStableIdCharacters * 2 + 1)
        {
            throw new ArgumentException("The graph continuation cursor is invalid.", nameof(cursor));
        }

        return cursor.Value;
    }

    private const int NodeColumnCount = 25;
}
