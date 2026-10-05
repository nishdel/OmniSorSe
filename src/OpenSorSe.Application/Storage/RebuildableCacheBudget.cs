using System.Text.Json;
using OpenSorSe.Application.Models;

namespace OpenSorSe.Application.Storage;

internal static class RebuildableCacheBudget
{
    internal static bool HasUserAuthority(IEnumerable<TagAssociation> tags) => tags.Any(tag =>
        tag.Source == TagSource.UserApproved || tag.AcceptanceState == TagAcceptanceState.Rejected ||
        tag.AcceptanceState == TagAcceptanceState.Accepted && !tag.IsSystem);

    // The envelope and separators also consume space; keeping this reserve makes the
    // final atomic writer's exact encoded-size check the final guard.
    internal static IReadOnlyList<T> RetainNewest<T>(IEnumerable<T> records, Func<T, DateTimeOffset> timestamp,
        long maximumBytes, JsonSerializerOptions jsonOptions, CancellationToken cancellationToken, Func<T, bool> hasUserAuthority)
    {
        var retained = new List<T>();
        long used = 256;
        foreach (var record in records.OrderByDescending(hasUserAuthority).ThenByDescending(timestamp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(record, jsonOptions).LongLength + 1;
            if (bytes <= maximumBytes - used)
            {
                retained.Add(record);
                used += bytes;
            }
            else if (hasUserAuthority(record))
            {
                throw new InvalidDataException("This cache contains retained user decisions above the configured budget. Increase the cache limit; no accepted or rejected tag was removed.");
            }
        }

        return retained;
    }
}
