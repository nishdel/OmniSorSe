using System.Security.Cryptography;
using System.Text;

namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>Projects authoritative graph-native decisions into deterministic visible mutations.</summary>
public sealed class DeterministicGraphDecisionProjectionBuilder : IGraphDecisionProjectionBuilder
{
    private const string Algorithm = "graph-native-decision-overlay";
    private const string AlgorithmVersion = "1.0.0";
    private readonly IGraphIdentityResolver _identityResolver;

    /// <summary>Initializes the graph-native decision projector.</summary>
    public DeterministicGraphDecisionProjectionBuilder(IGraphIdentityResolver identityResolver) =>
        _identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));

    /// <inheritdoc />
    public GraphDecisionProjection Build(
        GraphDecisionEntry decision,
        GraphDecisionSnapshot snapshot,
        DateTimeOffset validatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsValid || decision.Sequence is <= 0 || decision.Sequence > snapshot.Sequence ||
            string.IsNullOrWhiteSpace(decision.DecisionId) || string.IsNullOrWhiteSpace(decision.CanonicalHash))
        {
            throw new InvalidDataException("A graph-native decision cannot be projected from an invalid checkpoint.");
        }

        ValidateCommand(decision.Command);
        return decision.Command.Kind switch
        {
            GraphDecisionKind.CreateManualEntity => CreateEntity(decision, snapshot, validatedAtUtc),
            GraphDecisionKind.RenameManualEntity => Base(decision, replacementLabel: decision.Command.Label),
            GraphDecisionKind.AddAlias => AddAlias(decision),
            GraphDecisionKind.LinkNodes => Link(decision, validatedAtUtc),
            GraphDecisionKind.RemoveAlias or
            GraphDecisionKind.MergeEntities or
            GraphDecisionKind.SplitEntities or
            GraphDecisionKind.UnlinkNodes or
            GraphDecisionKind.RejectSuggestion or
            GraphDecisionKind.NeverMerge or
            GraphDecisionKind.Forget or
            GraphDecisionKind.Exclude or
            GraphDecisionKind.Include => Base(decision),
            _ => throw new InvalidDataException("Unknown graph-native decision kind."),
        };
    }

    private GraphDecisionProjection CreateEntity(
        GraphDecisionEntry decision,
        GraphDecisionSnapshot snapshot,
        DateTimeOffset validatedAtUtc)
    {
        var resolution = _identityResolver.Resolve(new GraphIdentityInput
        {
            Kind = GraphNodeKind.ManualEntity,
            Scope = "manual",
            CanonicalKey = decision.Command.SubjectId,
            ExistingStableId = decision.Command.SubjectId,
            NormalizationVersion = "manual-id-v1",
        });
        if (resolution.Status != GraphIdentityResolutionStatus.Resolved || resolution.NodeId is null)
        {
            throw new InvalidDataException("A manual entity decision contains an invalid stable identity.");
        }

        var node = new GraphNode
        {
            Identity = new GraphIdentity(
                resolution.NodeId,
                GraphNodeKind.ManualEntity,
                "manual",
                decision.Command.SubjectId,
                "manual-id-v1",
                resolution.CanonicalInputs),
            DisplayLabel = decision.Command.Label!,
            Origin = GraphOrigin.Manual,
            SourceManifestId = string.Concat("decision:", snapshot.CheckpointId),
            ObservationHash = decision.CanonicalHash,
            Algorithm = Algorithm,
            AlgorithmVersion = AlgorithmVersion,
            CreatedAtUtc = decision.CreatedAtUtc,
            LastValidatedAtUtc = validatedAtUtc,
        };
        return Base(decision) with { Node = node };
    }

    private static GraphDecisionProjection AddAlias(GraphDecisionEntry decision)
    {
        var normalized = decision.Command.Label!.Normalize(NormalizationForm.FormC).Trim();
        var alias = new GraphAlias(
            StableId("alias", decision.Command.SubjectId, normalized),
            decision.Command.SubjectId,
            decision.Command.Label,
            normalized.ToUpperInvariant(),
            GraphOrigin.Manual,
            decision.DecisionId,
            decision.CreatedAtUtc);
        return Base(decision) with { Alias = alias };
    }

    private static GraphDecisionProjection Link(GraphDecisionEntry decision, DateTimeOffset validatedAtUtc)
    {
        var edge = new GraphEdge
        {
            Id = StableId("manual-edge", decision.Command.SubjectId, decision.Command.TargetId!, decision.DecisionId),
            Kind = GraphEdgeKind.Manual,
            SourceNodeId = decision.Command.SubjectId,
            TargetNodeId = decision.Command.TargetId!,
            Confidence = GraphConfidenceLevel.Confirmed,
            Origin = GraphOrigin.Manual,
            EvidenceIds = [],
            Algorithm = Algorithm,
            AlgorithmVersion = AlgorithmVersion,
            InputFingerprint = decision.CanonicalHash,
            CreatedAtUtc = decision.CreatedAtUtc,
            LastValidatedAtUtc = validatedAtUtc,
            IsManual = true,
        };
        return Base(decision) with { Edge = edge };
    }

    private static GraphDecisionProjection Base(GraphDecisionEntry decision, string? replacementLabel = null) => new()
    {
        Decision = decision,
        SubjectId = decision.Command.SubjectId,
        TargetId = decision.Command.TargetId,
        ReplacementLabel = replacementLabel,
    };

    private static void ValidateCommand(GraphDecisionCommand command)
    {
        if (!Enum.IsDefined(command.Kind))
        {
            throw new InvalidDataException("A persisted graph-native decision has an unknown kind.");
        }

        Validate(command.SubjectId, GraphLimits.MaximumStableIdCharacters, "subject");
        if (command.TargetId is { } target)
        {
            Validate(target, GraphLimits.MaximumStableIdCharacters, "target");
            if (string.Equals(command.SubjectId, target, StringComparison.Ordinal))
            {
                throw new InvalidDataException("A graph-native decision cannot target itself.");
            }
        }

        if (command.Label is { } label)
        {
            Validate(label, GraphLimits.MaximumLabelCharacters, "label");
        }

        if (command.Kind == GraphDecisionKind.CreateManualEntity &&
            (command.NodeKind != GraphNodeKind.ManualEntity || command.Label is null))
        {
            throw new InvalidDataException("Manual entity creation requires its stable kind and a bounded label.");
        }

        if (command.Kind is GraphDecisionKind.RenameManualEntity or GraphDecisionKind.AddAlias or GraphDecisionKind.RemoveAlias &&
            command.Label is null)
        {
            throw new InvalidDataException("The persisted graph-native decision requires a bounded label.");
        }

        if (command.Kind is GraphDecisionKind.LinkNodes or GraphDecisionKind.MergeEntities or GraphDecisionKind.SplitEntities &&
            command.TargetId is null)
        {
            throw new InvalidDataException("The persisted graph-native decision requires a target.");
        }

        if (command.RelationshipSourceNodeId is { } relationshipSource)
        {
            Validate(relationshipSource, GraphLimits.MaximumStableIdCharacters, "relationship source");
        }
        if (command.RelationshipTargetNodeId is { } relationshipTarget)
        {
            Validate(relationshipTarget, GraphLimits.MaximumStableIdCharacters, "relationship target");
        }
        if (command.RelationshipKind is { } relationshipKind && !relationshipKind.IsStable)
        {
            throw new InvalidDataException("The persisted relationship kind is not stable.");
        }
        if (command.RelationshipScope is { } relationshipScope)
        {
            Validate(relationshipScope, GraphLimits.MaximumStableIdCharacters, "relationship scope");
        }

        var relationshipRemoval = command.Kind is GraphDecisionKind.UnlinkNodes or GraphDecisionKind.NeverMerge;
        var relationshipIdentityFieldCount = new object?[]
        {
            command.RelationshipSourceNodeId,
            command.RelationshipTargetNodeId,
            command.RelationshipKind,
            command.RelationshipScope,
        }.Count(item => item is not null);
        if (relationshipRemoval && relationshipIdentityFieldCount is > 0 and < 4)
        {
            throw new InvalidDataException("A persisted relationship removal contains a partial endpoint-pair identity.");
        }
    }

    private static void Validate(string value, int maximumLength, string field)
    {
        if (value.Length > maximumLength || string.IsNullOrWhiteSpace(value) ||
            ConservativeGraphIdentityResolver.ContainsInvalidUnicodeOrControl(value))
        {
            throw new InvalidDataException(string.Concat("Invalid graph-native decision ", field, "."));
        }
    }

    private static string StableId(string prefix, params string[] values)
    {
        var canonical = string.Join('|', values.Select(value => string.Concat(value.Length, ":", value)));
        return string.Concat(
            "kg:",
            prefix,
            ":",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant());
    }
}
