using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Persistence;

namespace OpenSorSe.Core.Platform;

/// <summary>Relocates closed application data by verified copy and one atomic active-location receipt.</summary>
public static class ApplicationStorageLocation
{
    private const string ReceiptName = "storage-location.json";
    private const string RequiredReceiptName = "storage-location-required.json";
    private const string GenerationMarkerName = "storage-generation.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Resolves or migrates data/cache before any stores are opened. The caller must hold the default profile lease.
    /// A failure leaves the previous receipt and all source data intact, and must stop startup rather than open an empty library.
    /// </summary>
    public static async Task<IApplicationPathProvider> ResolveAsync(
        IApplicationPathProvider defaultPaths,
        StorageSettings settings,
        IProfileOwnershipState ownership,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(defaultPaths);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ownership);
        cancellationToken.ThrowIfCancellationRequested();
        settings.Validate();
        if (ownership.Status != ProfileOwnershipStatus.Owned)
        {
            throw new InvalidOperationException("Storage relocation requires exclusive ownership of the original profile.");
        }

        var receiptPath = Path.Combine(defaultPaths.Paths.ConfigurationDirectory, ReceiptName);
        var profileKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            OperatingSystem.IsWindows() ? defaultPaths.SettingsFilePath.ToUpperInvariant() : defaultPaths.SettingsFilePath)))[..24];
        var receipt = await ReadReceiptAsync(receiptPath, cancellationToken).ConfigureAwait(false);
        var requested = settings.DirectoryPath is null ? null : Path.GetFullPath(settings.DirectoryPath);
        if (receipt is null)
        {
            // The original profile remains as a recovery copy, so absence of a receipt
            // after relocation must never be mistaken for a brand-new default library.
            var guard = await ReadReceiptAsync(Path.Combine(defaultPaths.Paths.ConfigurationDirectory, RequiredReceiptName), cancellationToken).ConfigureAwait(false);
            var generationParent = Path.Combine(requested ?? defaultPaths.Paths.DataDirectory, $".omnisorse-{profileKey}");
            var completedGeneration = ApplicationStorageFiles.Enumerate(generationParent, cancellationToken)
                .Any(file => file.Name == GenerationMarkerName);
            if (guard is not null || completedGeneration)
            {
                throw new IOException("The active storage-location receipt is missing from a relocated profile. Restore the matching receipt before starting; original data may be a stale recovery copy.");
            }
        }

        var active = defaultPaths;
        if (receipt is not null)
        {
            ValidateReceipt(receipt, profileKey);
            var generationRoot = GenerationRoot(receipt);
            ApplicationStorageFiles.RequireUnlinkedPath(generationRoot);
            var marker = await ReadReceiptAsync(Path.Combine(generationRoot, GenerationMarkerName), cancellationToken).ConfigureAwait(false);
            if (marker != receipt || !Directory.Exists(Path.Combine(generationRoot, "data")) ||
                !Directory.Exists(Path.Combine(generationRoot, "cache")))
            {
                throw new IOException("The configured application storage is unavailable or its migration receipt is invalid. Restore the drive or recovery copy before starting OmniSorSe.");
            }

            active = new LocatedPaths(defaultPaths, generationRoot);
        }

        if (receipt is null && requested is null || receipt is not null && SameRequestedRoot(receipt.RequestedRoot, requested))
        {
            return active;
        }

        var destinationRoot = requested ?? defaultPaths.Paths.DataDirectory;
        ApplicationStorageFiles.RequireUnlinkedPath(destinationRoot);
        var next = new LocationReceipt(1, profileKey, destinationRoot, requested, Guid.NewGuid().ToString("N"));
        var destination = GenerationRoot(next);
        var ownedEntries = ApplicationStorageFiles.DataEntries.Select(entry => Path.Combine(active.Paths.DataDirectory, entry))
            .Concat(ApplicationStorageFiles.CacheEntries.Select(entry => Path.Combine(active.Paths.CacheDirectory, entry)))
            .Append(defaultPaths.Paths.DiagnosticsDirectory).Append(defaultPaths.Paths.PluginDirectory)
            .Append(Path.Combine(defaultPaths.Paths.StateDirectory, "state-backups"));
        if (ownedEntries.Any(entry => PlatformServices.CurrentPathSemantics.IsWithinRoot(entry, destination)))
        {
            throw new IOException("Choose a storage folder outside existing index, cache, diagnostics, plugin and recovery subdirectories.");
        }

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException("The new storage generation already exists; no existing data was overwritten.");
        }

        var target = new LocatedPaths(defaultPaths, destination);
        try
        {
            // Inventory first so links, bounds and inaccessible source entries fail before any copy.
            var copies = CollectCopies(active.Paths.DataDirectory, target.Paths.DataDirectory, ApplicationStorageFiles.DataEntries, cancellationToken)
                .Concat(CollectCopies(active.Paths.CacheDirectory, target.Paths.CacheDirectory, ApplicationStorageFiles.CacheEntries, cancellationToken))
                .ToArray();
            Directory.CreateDirectory(target.Paths.DataDirectory);
            Directory.CreateDirectory(target.Paths.CacheDirectory);
            foreach (var copy in copies)
            {
                await CopyAndVerifyAsync(copy.Source, copy.Destination, cancellationToken).ConfigureAwait(false);
            }

            await AtomicJsonFile.WriteAsync(Path.Combine(destination, GenerationMarkerName), next, JsonOptions, 16_384, cancellationToken).ConfigureAwait(false);
            if (receipt is null)
            {
                // Once verified relocation data exists, preserve evidence that the old
                // default files can no longer safely be assumed to be current authority.
                await AtomicJsonFile.WriteAsync(Path.Combine(defaultPaths.Paths.ConfigurationDirectory, RequiredReceiptName),
                    next, JsonOptions, 16_384, cancellationToken).ConfigureAwait(false);
            }
            // This receipt is the only publication point. Prior generations remain untouched for recovery.
            await AtomicJsonFile.WriteAsync(receiptPath, next, JsonOptions, 16_384, cancellationToken).ConfigureAwait(false);
            return target;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Storage relocation did not complete. The previous library is preserved. Check free space and permissions, or restore the previous location in settings.json; incomplete copies may be retained in the selected folder.", exception);
        }
    }

    private static IEnumerable<(string Source, string Destination)> CollectCopies(
        string sourceRoot, string destinationRoot, IEnumerable<string> entries, CancellationToken cancellationToken)
    {
        foreach (var entry in entries)
        {
            foreach (var file in ApplicationStorageFiles.Enumerate(Path.Combine(sourceRoot, entry), cancellationToken))
            {
                yield return (file.FullName, Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, file.FullName)));
            }
        }
    }

    private static async Task CopyAndVerifyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplicationStorageFiles.RequireUnlinkedPath(sourcePath);
        ApplicationStorageFiles.RequireUnlinkedPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            65_536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         65_536, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Flush(flushToDisk: true);
        }

        source.Position = 0;
        var sourceHash = await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false);
        await using var verification = new FileStream(destinationPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            65_536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var targetHash = await SHA256.HashDataAsync(verification, cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(sourceHash, targetHash))
        {
            throw new IOException("Application storage verification failed; the active location was not changed.");
        }
    }

    private static async Task<LocationReceipt?> ReadReceiptAsync(string path, CancellationToken cancellationToken)
    {
        ApplicationStorageFiles.RequireUnlinkedPath(path);
        try
        {
            await using var stream = File.OpenRead(path);
            if (stream.Length > 16_384)
            {
                throw new IOException("The storage-location receipt exceeds its supported size.");
            }
            return await JsonSerializer.DeserializeAsync<LocationReceipt>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                   ?? throw new IOException("The storage-location receipt is empty.");
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (JsonException exception)
        {
            throw new IOException("The storage-location receipt is unreadable. It was preserved; restore it before opening this profile.", exception);
        }
    }

    private static void ValidateReceipt(LocationReceipt receipt, string profileKey)
    {
        if (receipt.Version != 1 || receipt.ProfileKey != profileKey ||
            string.IsNullOrWhiteSpace(receipt.StorageRoot) || !Path.IsPathFullyQualified(receipt.StorageRoot) ||
            !Guid.TryParseExact(receipt.Generation, "N", out _) ||
            receipt.RequestedRoot is not null && !SameRequestedRoot(receipt.RequestedRoot, receipt.StorageRoot))
        {
            throw new IOException("The storage-location receipt does not belong to this profile or uses an unsupported format.");
        }
    }

    private static bool SameRequestedRoot(string? first, string? second) =>
        first is null || second is null ? first is null && second is null :
            PlatformServices.CurrentPathSemantics.PathsEqual(first, second);

    private static string GenerationRoot(LocationReceipt receipt) => Path.Combine(
        receipt.StorageRoot, $".omnisorse-{receipt.ProfileKey}", receipt.Generation);

    private sealed record LocationReceipt(int Version, string ProfileKey, string StorageRoot, string? RequestedRoot, string Generation);

    private sealed class LocatedPaths : IApplicationPathProvider
    {
        private readonly IApplicationPathProvider _defaults;

        public LocatedPaths(IApplicationPathProvider defaults, string generationRoot)
        {
            _defaults = defaults;
            Paths = defaults.Paths with
            {
                DataDirectory = Path.Combine(generationRoot, "data"),
                CacheDirectory = Path.Combine(generationRoot, "cache"),
            };
        }

        public ApplicationPathSet Paths { get; }

        public string SettingsFilePath => _defaults.SettingsFilePath;

        public void EnsureOwnedDirectories()
        {
            _defaults.EnsureOwnedDirectories();
            ApplicationStorageFiles.RequireUnlinkedPath(Paths.DataDirectory);
            ApplicationStorageFiles.RequireUnlinkedPath(Paths.CacheDirectory);
            Directory.CreateDirectory(Paths.DataDirectory);
            Directory.CreateDirectory(Paths.CacheDirectory);
        }
    }
}
