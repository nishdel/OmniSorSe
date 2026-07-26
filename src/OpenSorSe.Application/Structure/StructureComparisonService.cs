namespace OpenSorSe.Application.Structure;

/// <summary>Produces deterministic, bounded structure changes with move and rename recognition.</summary>
public sealed class StructureComparisonService : IStructureComparisonService
{
    /// <inheritdoc />
    public IReadOnlyList<StructureChange> Compare(
        FolderStructureSnapshot before,
        FolderStructureSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var beforeRemaining = before.Nodes.ToList();
        var afterRemaining = after.Nodes.ToList();
        var changes = new List<StructureChange>();

        foreach (var beforeNode in before.Nodes)
        {
            var exact = afterRemaining.FirstOrDefault(node =>
                node.IsDirectory == beforeNode.IsDirectory &&
                FolderStructureSnapshotService.PathComparer.Equals(node.RelativePath, beforeNode.RelativePath));
            if (exact is null)
            {
                continue;
            }

            changes.Add(new StructureChange(
                StructureChangeKind.Unchanged,
                beforeNode.RelativePath,
                exact.RelativePath,
                beforeNode.IsDirectory,
                beforeNode.IdentityFingerprint));
            beforeRemaining.Remove(beforeNode);
            afterRemaining.Remove(exact);
        }

        foreach (var beforeNode in beforeRemaining.Where(node => !node.IsDirectory).ToArray())
        {
            var identityMatch = afterRemaining.FirstOrDefault(node =>
                !node.IsDirectory &&
                string.Equals(node.IdentityFingerprint, beforeNode.IdentityFingerprint, StringComparison.Ordinal));
            if (identityMatch is null)
            {
                continue;
            }

            var sameParent = FolderStructureSnapshotService.PathComparer.Equals(
                Path.GetDirectoryName(beforeNode.RelativePath) ?? string.Empty,
                Path.GetDirectoryName(identityMatch.RelativePath) ?? string.Empty);
            changes.Add(new StructureChange(
                sameParent ? StructureChangeKind.Renamed : StructureChangeKind.Moved,
                beforeNode.RelativePath,
                identityMatch.RelativePath,
                false,
                beforeNode.IdentityFingerprint));
            beforeRemaining.Remove(beforeNode);
            afterRemaining.Remove(identityMatch);
        }

        changes.AddRange(beforeRemaining.Select(node => new StructureChange(
            StructureChangeKind.Removed,
            node.RelativePath,
            null,
            node.IsDirectory,
            node.IdentityFingerprint)));
        changes.AddRange(afterRemaining.Select(node => new StructureChange(
            StructureChangeKind.Added,
            null,
            node.RelativePath,
            node.IsDirectory,
            node.IdentityFingerprint)));

        return Array.AsReadOnly(changes
            .OrderBy(change => change.Kind)
            .ThenBy(change => change.AfterRelativePath ?? change.BeforeRelativePath, FolderStructureSnapshotService.PathComparer)
            .Take(StructureLimits.MaximumSnapshotNodes)
            .ToArray());
    }
}
