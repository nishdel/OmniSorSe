namespace OpenSorSe.Core.Platform;

/// <summary>Enumerates explicitly owned storage without following links or unbounded directory trees.</summary>
public static class ApplicationStorageFiles
{
    /// <summary>Gets the durable data entries registered by the production composition root.</summary>
    public static IReadOnlyList<string> DataEntries { get; } = Array.AsReadOnly(new[]
    {
        "index", "catalog.json", "watched-catalogues.json", "structure-history.json",
        "decision-history.json", "workflow-library.json", "saved-discovery-views.json",
        "saved-catalog-searches.json",
    });

    /// <summary>Gets the reproducible cache entries registered by the production composition root.</summary>
    public static IReadOnlyList<string> CacheEntries { get; } = Array.AsReadOnly(new[]
    {
        "content-index.json", "semantic-index.json", "media-thumbnails", "media-temporary",
    });

    /// <summary>Rejects links at the path and every existing ancestor, including linked storage roots.</summary>
    public static void RequireUnlinkedPath(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Application storage cannot traverse a symbolic link or reparse point.");
            }

            current = Path.GetDirectoryName(current);
        }
    }

    /// <summary>Returns bounded regular files under an exact owned entry, rejecting linked descendants.</summary>
    public static IReadOnlyList<FileInfo> Enumerate(string ownedEntry, CancellationToken cancellationToken = default)
    {
        RequireUnlinkedPath(ownedEntry);
        var result = new List<FileInfo>();
        if (File.Exists(ownedEntry))
        {
            result.Add(new FileInfo(ownedEntry));
            return result;
        }

        if (!Directory.Exists(ownedEntry))
        {
            return result;
        }

        var pending = new Stack<(string Directory, int Depth)>();
        pending.Push((ownedEntry, 0));
        var visited = 0;
        while (pending.TryPop(out var next))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (next.Depth > 32)
            {
                throw new IOException("Application storage exceeds the supported directory depth.");
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(next.Directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++visited > 100_000)
                {
                    throw new IOException("Application storage exceeds the supported inventory size.");
                }

                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Application storage contains a symbolic link or reparse point.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push((entry, next.Depth + 1));
                }
                else
                {
                    result.Add(new FileInfo(entry));
                }
            }
        }

        return result;
    }
}
