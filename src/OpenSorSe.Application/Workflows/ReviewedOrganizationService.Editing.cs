using OpenSorSe.Core.Platform;

#pragma warning disable CS1591

namespace OpenSorSe.Application.Workflows;

public sealed partial class ReviewedOrganizationService
{
    private OrganizationProposalRow ApplyOrganizationChoices(
        OrganizationProposalRow row,
        string root,
        OrganizationPreviewRequest request,
        OrganizationProposalEdit? edit,
        IReadOnlyDictionary<string, string> preferences)
    {
        if (edit?.IsRejected == true)
        {
            return row with
            {
                IsRejected = true,
                TargetPath = row.CurrentPath,
                Reasons = ["User rejected this move; the file stays in its current location."],
            };
        }

        if (row.TargetPath is null && edit?.RelativeTargetPath is null)
        {
            return row;
        }

        var reasons = row.Reasons.ToList();
        var target = row.TargetPath;
        var currentFolder = Path.GetDirectoryName(row.CurrentPath)!;
        if (target is not null)
        {
            switch (request.Strategy)
            {
                case OrganizationStrategy.Preserve:
                    target = Path.Combine(currentFolder, Path.GetFileName(target));
                    reasons.Add("Preserve my structure: keep the current containing folder.");
                    break;
                case OrganizationStrategy.Improve:
                    if (!_paths.PathsEqual(currentFolder, root))
                    {
                        var leaf = Path.GetFileName(Path.GetDirectoryName(target));
                        target = string.IsNullOrEmpty(leaf) ||
                                 _paths.Comparer.Equals(Path.GetFileName(currentFolder), leaf)
                            ? Path.Combine(currentFolder, Path.GetFileName(target))
                            : Path.Combine(currentFolder, leaf, Path.GetFileName(target));
                    }

                    reasons.Add("Improve my structure: retain the existing hierarchy and group by the recommended folder.");
                    break;
                case OrganizationStrategy.Fresh:
                    reasons.Add("Reorganize from scratch: use the recipe hierarchy from the library root.");
                    break;
            }
        }

        var recommendedFolder = target is null ? null : RelativeFolder(root, target);
        if (target is not null && request.Strategy != OrganizationStrategy.Preserve &&
            recommendedFolder is not null && preferences.TryGetValue(recommendedFolder, out var preferred))
        {
            target = Path.Combine(root, preferred == "." ? string.Empty : preferred, Path.GetFileName(target));
            reasons.Add("User preference remembered from earlier reviewed folder edits.");
        }

        var relativeTarget = edit?.RelativeTargetPath ?? (target is null ? null : Path.GetRelativePath(root, target));
        if (edit?.RelativeTargetPath is not null)
        {
            reasons.Add("User edited the proposed filename or folder hierarchy.");
        }

        // Legacy recipe-only requests retain their original missing/unchanged semantics.
        if (request.Strategy is null && edit is null && target == row.TargetPath)
        {
            return row with { RecommendedRelativeDestination = recommendedFolder };
        }

        var conflicts = new List<string>();
        target = ValidateEditedTarget(root, row.CurrentPath, relativeTarget, request.Recipe, conflicts);
        if (!File.Exists(row.CurrentPath))
        {
            conflicts.Add("The indexed source file is no longer available.");
        }

        var unchanged = target is not null && _paths.PathsEqual(target, row.CurrentPath);
        if (target is not null && !unchanged && (File.Exists(target) || Directory.Exists(target)))
        {
            conflicts.Add("The proposed target is already occupied; overwrite is never allowed.");
        }

        if (edit?.RelativeTargetPath is null && row.MissingEvidence.Count > 0)
        {
            conflicts.Add("Required recipe values are unresolved.");
        }

        return row with
        {
            TargetPath = target,
            ProposedFileName = target is null ? null : Path.GetFileName(target),
            ProposedRelativeDestination = target is null ? null : DisplayFolder(RelativeFolder(root, target)),
            RecommendedRelativeDestination = recommendedFolder,
            IsUnchanged = unchanged && conflicts.Count == 0,
            Readiness = conflicts.Count > 0 ? OrganizationProposalReadiness.CannotPropose
                : row.Warnings.Count > 0 || edit is not null ? OrganizationProposalReadiness.NeedsReview
                : OrganizationProposalReadiness.Reliable,
            Conflicts = Array.AsReadOnly(conflicts.ToArray()),
            MissingEvidence = edit?.RelativeTargetPath is null ? row.MissingEvidence : [],
            Reasons = Array.AsReadOnly(reasons.ToArray()),
        };
    }

    private string? ValidateEditedTarget(
        string root, string source, string? relative, SortingRecipe recipe, List<string> conflicts)
    {
        if (string.IsNullOrWhiteSpace(relative) || CrossPlatformPath.IsRootedOnAnyPlatform(relative))
        {
            conflicts.Add("The edited target must be a non-empty path relative to the selected library.");
            return null;
        }

        var segments = relative.Split(['/', '\\']);
        if (segments.Any(segment => segment is "." or ".." ||
            !_paths.IsValidFileName(segment, recipe.FileNamePortability, out _)))
        {
            conflicts.Add("The edited target contains traversal, an empty segment, or an invalid filename.");
            return null;
        }

        if (segments[^1].Length > recipe.MaximumFileNameLength ||
            !string.Equals(Path.GetExtension(segments[^1]), Path.GetExtension(source), StringComparison.Ordinal))
        {
            conflicts.Add("Keep the original extension exactly and use a filename within the recipe's length limit.");
            return null;
        }

        try
        {
            var target = Path.GetFullPath(Path.Combine([root, .. segments]));
            if (target.Length > WorkflowLibraryLimits.MaximumPathLength || !_paths.IsWithinRoot(root, target))
            {
                conflicts.Add("The edited target exceeds the supported path length or library boundary.");
                return null;
            }

            for (var parent = Path.GetDirectoryName(target); parent is not null && _paths.IsWithinRoot(root, parent);
                 parent = Path.GetDirectoryName(parent))
            {
                if (File.Exists(parent) || Directory.Exists(parent) &&
                    (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                {
                    conflicts.Add("A proposed parent folder is occupied by a file or is a symbolic link/reparse point.");
                    return null;
                }

                if (_paths.PathsEqual(parent, root))
                {
                    break;
                }
            }

            return target;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            conflicts.Add("The edited target is not a valid filesystem path.");
            return null;
        }
    }

    private static string RelativeFolder(string root, string target) =>
        Path.GetRelativePath(root, Path.GetDirectoryName(target) ?? root);

    private static string DisplayFolder(string relative) => relative == "." ? string.Empty : relative;
}
