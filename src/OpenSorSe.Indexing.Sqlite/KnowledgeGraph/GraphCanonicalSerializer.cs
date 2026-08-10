using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSorSe.Application.KnowledgeGraph;

namespace OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

/// <summary>Provides one deterministic envelope and hash representation for provider observations.</summary>
internal static class GraphCanonicalSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    internal static string SerializeObservation(GraphProjectionObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var payload = observation switch
        {
            GraphSourceObservation value => JsonSerializer.Serialize(value, JsonOptions),
            GraphFileObservation value => JsonSerializer.Serialize(value, JsonOptions),
            GraphRelationshipObservation value => JsonSerializer.Serialize(value, JsonOptions),
            GraphCollectionObservation value => JsonSerializer.Serialize(value, JsonOptions),
            GraphCollectionMembershipObservation value => JsonSerializer.Serialize(value, JsonOptions),
            GraphLegacyDecisionObservation value => JsonSerializer.Serialize(value, JsonOptions),
            GraphDeletionObservation value => JsonSerializer.Serialize(value, JsonOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(observation), "Unsupported graph observation type."),
        };
        return JsonSerializer.Serialize(new ObservationEnvelope(observation.Kind, payload), JsonOptions);
    }

    internal static GraphProjectionObservation DeserializeObservation(string envelopeJson)
    {
        var envelope = JsonSerializer.Deserialize<ObservationEnvelope>(envelopeJson, JsonOptions)
            ?? throw SqliteKnowledgeInfrastructure.Corrupt("A durable graph observation envelope is malformed.");
        return envelope.Kind switch
        {
            GraphProjectionObservationKind.Source => Deserialize<GraphSourceObservation>(envelope.Payload),
            GraphProjectionObservationKind.File => Deserialize<GraphFileObservation>(envelope.Payload),
            GraphProjectionObservationKind.Relationship => Deserialize<GraphRelationshipObservation>(envelope.Payload),
            GraphProjectionObservationKind.Collection => Deserialize<GraphCollectionObservation>(envelope.Payload),
            GraphProjectionObservationKind.CollectionMembership => Deserialize<GraphCollectionMembershipObservation>(envelope.Payload),
            GraphProjectionObservationKind.LegacyDecision => Deserialize<GraphLegacyDecisionObservation>(envelope.Payload),
            GraphProjectionObservationKind.Deletion => Deserialize<GraphDeletionObservation>(envelope.Payload),
            _ => throw SqliteKnowledgeInfrastructure.Corrupt("A durable graph observation kind is unsupported."),
        };
    }

    internal static string SerializeDecisionProjection(GraphDecisionProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return JsonSerializer.Serialize(projection, JsonOptions);
    }

    internal static GraphDecisionProjection DeserializeDecisionProjection(string json) =>
        JsonSerializer.Deserialize<GraphDecisionProjection>(json, JsonOptions)
        ?? throw SqliteKnowledgeInfrastructure.Corrupt("A staged graph-native decision projection is malformed.");

    internal static string CalculatePageHash(IEnumerable<GraphProjectionObservation> observations) =>
        HashLines(observations.Select(CanonicalRow));

    internal static string CalculateManifestHash(IEnumerable<GraphProjectionObservation> observations) =>
        HashLines(observations
            .OrderBy(item => item.Kind.ToString(), StringComparer.Ordinal)
            .ThenBy(item => item.StableKey, StringComparer.Ordinal)
            .Select(CanonicalRow));

    internal static string CalculateManifestHash(
        IEnumerable<(string Kind, string StableKey, string RowHash)> rows) =>
        HashLines(rows
            .OrderBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.StableKey, StringComparer.Ordinal)
            .Select(item => $"{item.Kind}|{item.StableKey}|{item.RowHash}"));

    internal static string CalculateOrderedManifestHash(
        IEnumerable<(string Kind, string StableKey, string RowHash)> rows) =>
        HashLines(rows.Select(item => $"{item.Kind}|{item.StableKey}|{item.RowHash}"));

    internal static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string HashLines(IEnumerable<string> lines)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var first = true;
        foreach (var line in lines)
        {
            if (!first)
            {
                hash.AppendData("\n"u8);
            }

            hash.AppendData(Encoding.UTF8.GetBytes(line));
            first = false;
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string CanonicalRow(GraphProjectionObservation item) =>
        $"{item.Kind}|{item.StableKey}|{item.CanonicalRowHash}";

    private static T Deserialize<T>(string json) where T : GraphProjectionObservation =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw SqliteKnowledgeInfrastructure.Corrupt("A durable graph observation payload is malformed.");

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new GraphNodeKindConverter());
        options.Converters.Add(new GraphEdgeKindConverter());
        options.Converters.Add(new GraphEvidenceKindConverter());
        return options;
    }

    private static string ReadCode(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() ?? throw new JsonException("A graph code cannot be null.");
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A graph code must be a string or a value object.");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        if (!document.RootElement.TryGetProperty("value", out var value) &&
            !document.RootElement.TryGetProperty("Value", out value))
        {
            throw new JsonException("A graph code value is missing.");
        }

        return value.GetString() ?? throw new JsonException("A graph code cannot be null.");
    }

    private sealed class GraphNodeKindConverter : JsonConverter<GraphNodeKind>
    {
        public override GraphNodeKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(ReadCode(ref reader));

        public override void Write(Utf8JsonWriter writer, GraphNodeKind value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }

    private sealed class GraphEdgeKindConverter : JsonConverter<GraphEdgeKind>
    {
        public override GraphEdgeKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(ReadCode(ref reader));

        public override void Write(Utf8JsonWriter writer, GraphEdgeKind value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }

    private sealed class GraphEvidenceKindConverter : JsonConverter<GraphEvidenceKind>
    {
        public override GraphEvidenceKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(ReadCode(ref reader));

        public override void Write(Utf8JsonWriter writer, GraphEvidenceKind value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }

    private sealed record ObservationEnvelope(GraphProjectionObservationKind Kind, string Payload);
}
