#pragma warning disable CS1591

using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace OpenSorSe.Application.Plugins;

/// <summary>
/// Inspects, installs, upgrades, and removes local plugin ZIP packages inside the controlled plugin root.
/// </summary>
/// <remarks>
/// Package input is untrusted. Archive paths, duplicate entries, links,
/// reserved names, native declarations, counts, sizes, manifest shape, managed
/// entry assembly, and integrity are validated before and after extraction.
/// Extraction uses a controlled staging directory and an atomic exact-version
/// directory move; existing versions are never overwritten. Removal is
/// confirmed, dependency-aware, reparse-safe, and limited to the resolved
/// plugin/version directory. User files and execution history are outside this
/// service's authority.
/// </remarks>
public sealed class PluginPackageService : IPluginPackageService
{
    private readonly string _pluginRoot;
    private readonly IPluginManifestParser _parser;
    private readonly IPluginIntegrityService _integrity;
    private readonly IPluginUsageInspector _usage;
    private readonly IPluginDiagnostics _diagnostics;

    public PluginPackageService(
        string pluginRoot,
        IPluginManifestParser parser,
        IPluginIntegrityService integrity,
        IPluginUsageInspector usage,
        IPluginDiagnostics diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginRoot);
        _pluginRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pluginRoot));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _integrity = integrity ?? throw new ArgumentNullException(nameof(integrity));
        _usage = usage ?? throw new ArgumentNullException(nameof(usage));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public async Task<PluginPackageInspection> InspectAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        var issues = new List<string>();
        try
        {
            var fullPath = Path.GetFullPath(packagePath);
            var info = new FileInfo(fullPath);
            if (!info.Exists || info.Length <= 0 || info.Length > PluginLimits.MaximumPackageBytes)
            {
                return Invalid("The plugin package is missing, empty, or exceeds the package size limit.");
            }

            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count is < 2 or > PluginLimits.MaximumPackageEntries)
            {
                issues.Add("The package entry count is invalid or excessive.");
            }

            long totalBytes = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                totalBytes = checked(totalBytes + entry.Length);
                if (totalBytes > PluginLimits.MaximumPackageBytes)
                {
                    issues.Add("The package uncompressed size exceeds the limit.");
                    break;
                }

                var normalized = entry.FullName.Replace('\\', '/').TrimEnd('/');
                if (normalized.Length == 0)
                {
                    continue;
                }

                if (!PluginManifestParser.IsSafeRelativePath(normalized) ||
                    Path.IsPathFullyQualified(entry.FullName) ||
                    !names.Add(normalized) ||
                    HasInvalidFileName(normalized) ||
                    IsLink(entry))
                {
                    issues.Add($"Package entry \"{SafeEntryName(entry.FullName)}\" is unsafe, duplicated, rooted, traversing, invalid, or link-like.");
                }
            }

            var manifestEntries = archive.Entries
                .Where(entry =>
                    string.Equals(
                        entry.FullName.Replace('\\', '/'),
                        "plugin.json",
                        StringComparison.Ordinal))
                .ToArray();
            if (manifestEntries.Length != 1 ||
                manifestEntries[0].Length <= 0 ||
                manifestEntries[0].Length > PluginLimits.MaximumManifestBytes)
            {
                issues.Add("The package must contain exactly one bounded root plugin.json.");
                return new PluginPackageInspection(
                    false,
                    null,
                    totalBytes,
                    archive.Entries.Count,
                    Array.AsReadOnly(issues.Distinct(StringComparer.Ordinal).ToArray()));
            }

            byte[] manifestBytes;
            await using (var manifestStream = manifestEntries[0].Open())
            {
                using var memory = new MemoryStream((int)manifestEntries[0].Length);
                await manifestStream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
                manifestBytes = memory.ToArray();
            }

            var parsed = _parser.Parse(manifestBytes);
            issues.AddRange(parsed.Issues.Where(issue => issue.IsBlocking).Select(issue => issue.Message));
            var manifest = parsed.Manifest;
            if (manifest is not null)
            {
                var entryAssembly = archive.Entries.SingleOrDefault(entry =>
                    string.Equals(
                        entry.FullName.Replace('\\', '/'),
                        manifest.EntryAssembly.Replace('\\', '/'),
                        StringComparison.Ordinal));
                if (entryAssembly is null || entryAssembly.Length <= 0)
                {
                    issues.Add("The declared entry assembly is missing or empty.");
                }
                else
                {
                    await ValidateAssemblyAndIntegrityAsync(
                        entryAssembly,
                        manifest,
                        issues,
                        cancellationToken).ConfigureAwait(false);
                }

                foreach (var binary in archive.Entries.Where(entry =>
                             !string.IsNullOrEmpty(entry.Name) &&
                             IsPotentialNativeBinary(entry.Name)))
                {
                    if (string.Equals(
                            binary.FullName.Replace('\\', '/'),
                            manifest.EntryAssembly.Replace('\\', '/'),
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var managed = await IsManagedAssemblyAsync(binary, cancellationToken).ConfigureAwait(false);
                    if (!managed &&
                        !manifest.Capabilities.Contains(
                            OpenSorSe.Extensions.Abstractions.PluginCapability.UseNativeLibraries))
                    {
                        issues.Add(
                            $"Native or unrecognized binary \"{SafeEntryName(binary.FullName)}\" requires the use-native-libraries capability.");
                    }
                }
            }

            return new PluginPackageInspection(
                issues.Count == 0 && manifest is not null,
                issues.Count == 0 ? manifest : null,
                totalBytes,
                archive.Entries.Count,
                Array.AsReadOnly(issues.Distinct(StringComparer.Ordinal).ToArray()));
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                NotSupportedException or
                OverflowException)
        {
            return Invalid("The plugin package is unreadable or malformed.");
        }
    }

    public async Task<PluginOperationResult> InstallAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        var inspection = await InspectAsync(packagePath, cancellationToken).ConfigureAwait(false);
        if (!inspection.IsValid || inspection.Manifest is null)
        {
            return new PluginOperationResult(
                false,
                inspection.Issues.Count == 0
                    ? "The plugin package is invalid."
                    : string.Join(" ", inspection.Issues));
        }

        var manifest = inspection.Manifest;
        var pluginParent = SafeChild(_pluginRoot, manifest.PluginId);
        var destination = SafeChild(pluginParent, manifest.PluginVersion);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            return new PluginOperationResult(
                false,
                "That plugin version is already installed. Existing files were not overwritten.");
        }

        Directory.CreateDirectory(_pluginRoot);
        EnsureControlledPath(_pluginRoot, _pluginRoot);
        var stagingRoot = SafeChild(_pluginRoot, "_staging");
        Directory.CreateDirectory(stagingRoot);
        EnsureControlledPath(_pluginRoot, stagingRoot);
        var staging = SafeChild(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            await ExtractValidatedAsync(
                packagePath,
                staging,
                cancellationToken).ConfigureAwait(false);
            var reparsed = _parser.ParseFile(Path.Combine(staging, "plugin.json"));
            if (!reparsed.IsValid ||
                reparsed.Manifest is null ||
                !string.Equals(reparsed.Manifest.PluginId, manifest.PluginId, StringComparison.Ordinal) ||
                !string.Equals(reparsed.Manifest.PluginVersion, manifest.PluginVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The staged manifest differs from the validated package manifest.");
            }

            var stagedIssues = await ValidateStagedTreeAsync(
                staging,
                reparsed.Manifest,
                cancellationToken).ConfigureAwait(false);
            if (stagedIssues.Count > 0)
            {
                throw new InvalidDataException(string.Join(" ", stagedIssues));
            }

            _ = await _integrity.CalculateAsync(staging, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(pluginParent);
            EnsureControlledPath(_pluginRoot, pluginParent);
            Directory.Move(staging, destination);
            _diagnostics.Record(
                PluginDiagnosticKind.PackageInstallation,
                manifest.PluginId,
                $"Installed local plugin version {manifest.PluginVersion}. It remains disabled pending explicit review.");
            return new PluginOperationResult(
                true,
                $"Installed {manifest.DisplayName} {manifest.PluginVersion}. It is disabled until explicitly reviewed and enabled.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _diagnostics.Record(
                PluginDiagnosticKind.PackageInstallation,
                manifest.PluginId,
                "Local plugin installation failed; the validated destination was not replaced.",
                exception.GetType().Name);
            return new PluginOperationResult(
                false,
                "Plugin installation failed safely. No installed version was overwritten.");
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    public async Task<PluginOperationResult> UpgradeAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        var inspection = await InspectAsync(packagePath, cancellationToken).ConfigureAwait(false);
        if (!inspection.IsValid || inspection.Manifest is null)
        {
            return new PluginOperationResult(false, string.Join(" ", inspection.Issues));
        }

        var manifest = inspection.Manifest;
        var pluginParent = SafeChild(_pluginRoot, manifest.PluginId);
        var installed = Directory.Exists(pluginParent)
            ? Directory.EnumerateDirectories(pluginParent)
                .Where(path => File.Exists(Path.Combine(path, "plugin.json")))
                .Select(path => _parser.ParseFile(Path.Combine(path, "plugin.json")).Manifest)
                .Where(value => value is not null)
                .ToArray()
            : [];
        if (installed.Length == 0)
        {
            return new PluginOperationResult(
                false,
                "No installed version exists for this plugin; use Install instead.");
        }

        var newest = installed
            .OrderByDescending(value => ParseVersion(value!.PluginVersion))
            .First()!;
        if (ParseVersion(manifest.PluginVersion) <= ParseVersion(newest.PluginVersion))
        {
            return new PluginOperationResult(
                false,
                $"Upgrade version {manifest.PluginVersion} must be newer than installed version {newest.PluginVersion}.");
        }

        var result = await InstallAsync(packagePath, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
        {
            _diagnostics.Record(
                PluginDiagnosticKind.Upgrade,
                manifest.PluginId,
                $"Staged and installed version {manifest.PluginVersion}; version {newest.PluginVersion} was preserved for rollback.");
            return result with
            {
                Message =
                    $"Installed upgrade {manifest.PluginVersion}. Previous version {newest.PluginVersion} remains installed for rollback until separately removed.",
            };
        }

        _diagnostics.Record(
            PluginDiagnosticKind.Rollback,
            manifest.PluginId,
            $"Upgrade failed before replacement; installed version {newest.PluginVersion} remains intact.");
        return result;
    }

    public async Task<PluginOperationResult> RemoveAsync(
        string pluginId,
        string pluginVersion,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginVersion);
        if (!confirmed)
        {
            return new PluginOperationResult(
                false,
                "Removal requires explicit confirmation. User documents and historical records are never removed.");
        }

        var usage = await _usage.InspectAsync(pluginId, cancellationToken).ConfigureAwait(false);
        if (usage.HasActiveDependencies)
        {
            return new PluginOperationResult(
                false,
                $"Removal is blocked because active dependencies remain: {usage.WorkflowProfileIds.Count} profile(s), {usage.SortingRecipeIds.Count} recipe(s), and {usage.WatchedFolderIds.Count} watched folder(s).");
        }

        var pluginParent = SafeChild(_pluginRoot, pluginId);
        var target = SafeChild(pluginParent, pluginVersion);
        if (!Directory.Exists(target))
        {
            return new PluginOperationResult(false, "The selected external plugin version is not installed.");
        }

        if (!File.Exists(Path.Combine(target, "plugin.json")))
        {
            return new PluginOperationResult(
                false,
                "The selected directory is incomplete and was not removed automatically.");
        }

        try
        {
            EnsureControlledPath(_pluginRoot, target);
            _ = EnumerateSafeFiles(target);
            Directory.Delete(target, recursive: true);
            if (Directory.Exists(pluginParent) &&
                !Directory.EnumerateFileSystemEntries(pluginParent).Any())
            {
                Directory.Delete(pluginParent);
            }

            _diagnostics.Record(
                PluginDiagnosticKind.Removal,
                pluginId,
                $"Removed controlled installed files for version {pluginVersion}. Historical provenance was retained.");
            return new PluginOperationResult(
                true,
                "Plugin files were removed. User documents, workflows, scan history, Change Plans, and operation history were preserved.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new PluginOperationResult(
                false,
                "Plugin removal failed safely. No user content was targeted.");
        }
    }

    private static async Task ValidateAssemblyAndIntegrityAsync(
        ZipArchiveEntry entryAssembly,
        PluginManifest manifest,
        List<string> issues,
        CancellationToken cancellationToken)
    {
        if (!await IsManagedAssemblyAsync(entryAssembly, cancellationToken).ConfigureAwait(false))
        {
            issues.Add("The declared entry assembly is not a managed .NET assembly.");
            return;
        }

        if (manifest.Integrity is not null)
        {
            await using var stream = entryAssembly.Open();
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            var actual = Convert.ToHexString(hash).ToLowerInvariant();
            if (!string.Equals(actual, manifest.Integrity.Hash, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("The entry assembly does not match the manifest SHA-256 integrity value.");
            }
        }
    }

    private static async Task<IReadOnlyList<string>> ValidateStagedTreeAsync(
        string staging,
        PluginManifest manifest,
        CancellationToken cancellationToken)
    {
        var issues = new List<string>();
        IReadOnlyList<FileInfo> files;
        try
        {
            files = EnumerateSafeFiles(staging);
        }
        catch (InvalidDataException exception)
        {
            return [exception.Message];
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(staging));
        var entryPath = Path.GetFullPath(Path.Combine(root, manifest.EntryAssembly));
        if (!IsWithin(root, entryPath))
        {
            return ["The staged entry assembly escaped the controlled plugin directory."];
        }

        var entryAssembly = files.SingleOrDefault(file =>
            string.Equals(file.FullName, entryPath, PathComparison));
        if (entryAssembly is null ||
            entryAssembly.Length <= 0 ||
            !await IsManagedAssemblyAsync(entryAssembly, cancellationToken).ConfigureAwait(false))
        {
            issues.Add("The staged entry assembly is missing, empty, or not a managed .NET assembly.");
        }
        else if (manifest.Integrity is not null)
        {
            await using var stream = new FileStream(
                entryAssembly.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            var actual = Convert.ToHexString(hash).ToLowerInvariant();
            if (!string.Equals(actual, manifest.Integrity.Hash, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("The staged entry assembly does not match the manifest SHA-256 integrity value.");
            }
        }

        foreach (var binary in files.Where(file =>
                     IsPotentialNativeBinary(file.Name) &&
                     !string.Equals(file.FullName, entryPath, PathComparison)))
        {
            if (!await IsManagedAssemblyAsync(binary, cancellationToken).ConfigureAwait(false) &&
                !manifest.Capabilities.Contains(
                    OpenSorSe.Extensions.Abstractions.PluginCapability.UseNativeLibraries))
            {
                issues.Add(
                    $"Native or unrecognized binary \"{SafeEntryName(Path.GetRelativePath(root, binary.FullName))}\" requires the use-native-libraries capability.");
            }
        }

        return Array.AsReadOnly(issues.ToArray());
    }

    private static async Task<bool> IsManagedAssemblyAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var source = entry.Open();
            using var memory = new MemoryStream((int)Math.Min(entry.Length, 16L * 1024 * 1024));
            await source.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            memory.Position = 0;
            using var reader = new PEReader(memory, PEStreamOptions.LeaveOpen);
            return reader.HasMetadata && reader.PEHeaders.CorHeader is not null;
        }
        catch (Exception exception) when (
            exception is BadImageFormatException or IOException)
        {
            return false;
        }
    }

    private static async Task<bool> IsManagedAssemblyAsync(
        FileInfo entry,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var source = new FileStream(
                entry.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var memory = new MemoryStream((int)Math.Min(entry.Length, 16L * 1024 * 1024));
            await source.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            memory.Position = 0;
            using var reader = new PEReader(memory, PEStreamOptions.LeaveOpen);
            return reader.HasMetadata && reader.PEHeaders.CorHeader is not null;
        }
        catch (Exception exception) when (
            exception is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task ExtractValidatedAsync(
        string packagePath,
        string staging,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            packagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(staging));
        foreach (var entry in archive.Entries.OrderBy(value => value.FullName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = entry.FullName.Replace('\\', '/').TrimEnd('/');
            if (relative.Length == 0)
            {
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsWithin(root, target))
            {
                throw new InvalidDataException("An archive entry escaped the staging directory.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            var parent = Path.GetDirectoryName(target)
                ?? throw new InvalidDataException("An archive file has no parent directory.");
            Directory.CreateDirectory(parent);
            await using var input = entry.Open();
            await using var output = new FileStream(
                target,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsLink(ZipArchiveEntry entry)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
        var dosAttributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
        return unixMode == 0xA000 || dosAttributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private static bool HasInvalidFileName(string normalized)
    {
        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0 ||
                segment.Length > 255 ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                PluginManifestParser.IsReservedFileNameSegment(segment) ||
                segment.EndsWith(' ') ||
                segment.EndsWith('.') ||
                segment is "." or "..")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPotentialNativeBinary(string name) =>
        Path.GetExtension(name).ToLowerInvariant() is ".dll" or ".so" or ".dylib" or ".exe";

    private static IReadOnlyList<FileInfo> EnumerateSafeFiles(string directory)
    {
        var root = new DirectoryInfo(Path.GetFullPath(directory));
        if (!root.Exists || root.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException("The controlled plugin directory is unavailable or is a reparse point.");
        }

        var files = new List<FileInfo>();
        var pending = new Queue<DirectoryInfo>();
        pending.Enqueue(root);
        var directoryCount = 0;
        long totalBytes = 0;
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            directoryCount++;
            if (directoryCount > PluginLimits.MaximumInstalledFiles)
            {
                throw new InvalidDataException("The controlled plugin directory contains too many directories.");
            }

            foreach (var entry in current.EnumerateFileSystemInfos()
                         .OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidDataException("The controlled plugin directory contains a reparse point.");
                }

                switch (entry)
                {
                    case DirectoryInfo child:
                        pending.Enqueue(child);
                        break;
                    case FileInfo file:
                        files.Add(file);
                        totalBytes = checked(totalBytes + file.Length);
                        if (files.Count > PluginLimits.MaximumInstalledFiles ||
                            totalBytes > PluginLimits.MaximumInstalledPluginBytes)
                        {
                            throw new InvalidDataException("The controlled plugin directory exceeds installed size or file-count limits.");
                        }

                        break;
                    default:
                        throw new InvalidDataException("The controlled plugin directory contains an unsupported filesystem entry.");
                }
            }
        }

        return Array.AsReadOnly(files.ToArray());
    }

    private static void EnsureControlledPath(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        if (!IsWithin(normalizedRoot, normalizedCandidate))
        {
            throw new InvalidDataException("The controlled plugin path escaped its root.");
        }

        var current = Directory.Exists(normalizedCandidate)
            ? new DirectoryInfo(normalizedCandidate)
            : new DirectoryInfo(Path.GetDirectoryName(normalizedCandidate) ?? normalizedCandidate);
        while (true)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidDataException("The controlled plugin path contains a reparse point.");
            }

            if (string.Equals(current.FullName, normalizedRoot, PathComparison))
            {
                return;
            }

            current = current.Parent
                ?? throw new InvalidDataException("The controlled plugin path could not be traced to its root.");
        }
    }

    private static string SafeChild(string parent, string child)
    {
        if (!PluginManifestParser.IsSafeRelativePath(child) ||
            child.Contains('/') ||
            child.Contains('\\'))
        {
            throw new ArgumentException("A controlled plugin path segment is invalid.", nameof(child));
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var candidate = Path.GetFullPath(Path.Combine(root, child));
        if (!IsWithin(root, candidate) || string.Equals(root, candidate, PathComparison))
        {
            throw new InvalidOperationException("The controlled plugin path escaped its parent.");
        }

        return candidate;
    }

    private static bool IsWithin(string root, string candidate) =>
        string.Equals(root, candidate, PathComparison) ||
        candidate.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string SafeEntryName(string value) =>
        new(value.Where(character => !char.IsControl(character)).Take(256).ToArray());

    private static Version ParseVersion(string value)
    {
        _ = PluginManifestParser.TryVersion(value, out var version);
        return version;
    }

    private static PluginPackageInspection Invalid(string message) =>
        new(false, null, 0, 0, [message]);
}

public sealed class NullPluginUsageInspector : IPluginUsageInspector
{
    public Task<PluginUsage> InspectAsync(
        string pluginId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PluginUsage([], [], [], []));
    }
}
