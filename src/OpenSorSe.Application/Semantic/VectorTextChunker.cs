using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OpenSorSe.Application.Semantic;

/// <summary>Creates deterministic chunks without combining evidence with different provenance.</summary>
public static class VectorTextChunker
{
    /// <summary>Gets the maximum learned chunks retained per file.</summary>
    public const int MaximumChunks = 16;
    /// <summary>Gets the maximum UTF-16 characters in one embedding input.</summary>
    public const int MaximumChunkCharacters = 1600;
    /// <summary>Gets the overlap retained between adjacent chunks of the same evidence field.</summary>
    public const int OverlapCharacters = 200;

    /// <summary>Chunks each evidence field independently with stable identities, exact offsets and bounded overlap.</summary>
    public static IReadOnlyList<VectorTextChunk> Create(VectorIndexDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var chunks = new List<VectorTextChunk>();
        var fields = document.Fields.Where(field => !string.IsNullOrWhiteSpace(field.Text)).Take(MaximumChunks).ToArray();
        var offsets = new int[fields.Length];
        // Round-robin retains native, OCR, transcript and AI evidence even when one field is long.
        while (chunks.Count < MaximumChunks)
        {
            var added = false;
            for (var index = 0; index < fields.Length && chunks.Count < MaximumChunks; index++)
            {
                var field = fields[index];
                var start = offsets[index];
                while (start < field.Text.Length && char.IsWhiteSpace(field.Text[start])) start++;
                if (start >= field.Text.Length) continue;
                var end = Math.Min(field.Text.Length, start + MaximumChunkCharacters);
                if (end < field.Text.Length)
                {
                    // Prefer a word boundary, and never divide a UTF-16 surrogate pair.
                    var boundary = end;
                    while (boundary > start + MaximumChunkCharacters / 2 && !char.IsWhiteSpace(field.Text[boundary - 1])) boundary--;
                    if (boundary > start + MaximumChunkCharacters / 2) end = boundary;
                    if (char.IsHighSurrogate(field.Text[end - 1]) && char.IsLowSurrogate(field.Text[end])) end--;
                }
                var length = end - start;
                var text = field.Text.Substring(start, length);
                var identity = string.Join('\n', document.FileId, field.Name,
                    start.ToString(CultureInfo.InvariantCulture), text);
                var chunkId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
                chunks.Add(new VectorTextChunk(chunkId, chunks.Count, field.Name, start, length, text));
                var next = end >= field.Text.Length ? end : Math.Max(start + 1, end - OverlapCharacters);
                if (next < field.Text.Length && next > 0 && char.IsLowSurrogate(field.Text[next]) && char.IsHighSurrogate(field.Text[next - 1])) next++;
                offsets[index] = next;
                added = true;
            }
            if (!added) break;
        }
        return chunks;
    }
}
