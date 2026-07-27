#pragma warning disable CS1591

using System.IO.Compression;
using OpenSorSe.Application.Plugins;

namespace OpenSorSe.Application.Tests;

public sealed class PluginPackageTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        "OpenSorSe.PluginPackage.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly string _pluginRoot;

    public PluginPackageTests()
    {
        Directory.CreateDirectory(_workspace);
        _pluginRoot = Path.Combine(_workspace, "installed");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }

        DeleteEmptyParent(_workspace);
    }

    [Fact]
    public async Task InspectAndInstall_ValidPackage_StagesInsideControlledRootAndStaysDisabledByDesign()
    {
        var package = CreatePackage(PluginManifestTests.Manifest());
        var service = Service();

        var inspection = await service.InspectAsync(package, CancellationToken.None);
        var install = await service.InstallAsync(package, CancellationToken.None);

        Assert.True(inspection.IsValid);
        Assert.True(install.Succeeded);
        Assert.True(File.Exists(Path.Combine(
            _pluginRoot,
            "example.plugin",
            "1.0.0",
            "plugin.json")));
        Assert.Contains("disabled", install.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inspect_TraversalAbsoluteDuplicateAndLinkEntries_AreRejected()
    {
        var traversal = CreatePackage(
            PluginManifestTests.Manifest(),
            archive => AddText(archive, "../escape.txt", "escape"));
        var absolute = CreatePackage(
            PluginManifestTests.Manifest("absolute.plugin"),
            archive => AddText(archive, "/rooted.txt", "rooted"));
        var duplicate = CreatePackage(
            PluginManifestTests.Manifest("duplicate.plugin"),
            archive =>
            {
                AddText(archive, "duplicate.txt", "one");
                AddText(archive, "duplicate.txt", "two");
            });
        var link = CreatePackage(
            PluginManifestTests.Manifest("link.plugin"),
            archive =>
            {
                var entry = archive.CreateEntry("linked");
                entry.ExternalAttributes = 0xA000 << 16;
            });
        var service = Service();

        Assert.False((await service.InspectAsync(traversal, CancellationToken.None)).IsValid);
        Assert.False((await service.InspectAsync(absolute, CancellationToken.None)).IsValid);
        Assert.False((await service.InspectAsync(duplicate, CancellationToken.None)).IsValid);
        Assert.False((await service.InspectAsync(link, CancellationToken.None)).IsValid);
        Assert.False(File.Exists(Path.Combine(_workspace, "escape.txt")));
    }

    [Fact]
    public async Task Inspect_MissingEntryAssemblyAndUnexpectedNativeBinary_AreRejected()
    {
        var missing = CreatePackage(
            PluginManifestTests.Manifest("missing.plugin"),
            includeAssembly: false);
        var native = CreatePackage(
            PluginManifestTests.Manifest("native.plugin"),
            archive => AddText(archive, "native.dll", "not managed"));
        var service = Service();

        var missingResult = await service.InspectAsync(missing, CancellationToken.None);
        var nativeResult = await service.InspectAsync(native, CancellationToken.None);

        Assert.False(missingResult.IsValid);
        Assert.Contains(missingResult.Issues, issue =>
            issue.Contains("entry assembly", StringComparison.OrdinalIgnoreCase));
        Assert.False(nativeResult.IsValid);
        Assert.Contains(nativeResult.Issues, issue =>
            issue.Contains("native", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Install_ExistingVersion_IsNeverOverwritten()
    {
        var package = CreatePackage(PluginManifestTests.Manifest());
        var service = Service();
        Assert.True((await service.InstallAsync(package, CancellationToken.None)).Succeeded);
        var manifestPath = Path.Combine(
            _pluginRoot,
            "example.plugin",
            "1.0.0",
            "plugin.json");
        var before = await File.ReadAllBytesAsync(manifestPath);

        var second = await service.InstallAsync(package, CancellationToken.None);

        Assert.False(second.Succeeded);
        Assert.Equal(before, await File.ReadAllBytesAsync(manifestPath));
    }

    [Fact]
    public async Task Upgrade_ValidatesNewVersionAndPreservesOldForRollback()
    {
        var service = Service();
        var oldPackage = CreatePackage(PluginManifestTests.Manifest(version: "1.0.0"));
        var newPackage = CreatePackage(PluginManifestTests.Manifest(version: "1.1.0"));
        Assert.True((await service.InstallAsync(oldPackage, CancellationToken.None)).Succeeded);

        var upgraded = await service.UpgradeAsync(newPackage, CancellationToken.None);

        Assert.True(upgraded.Succeeded);
        Assert.True(Directory.Exists(Path.Combine(_pluginRoot, "example.plugin", "1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(_pluginRoot, "example.plugin", "1.1.0")));
        Assert.Contains("rollback", upgraded.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upgrade_InvalidNewPackage_LeavesWorkingOldVersionUntouched()
    {
        var service = Service();
        var oldPackage = CreatePackage(PluginManifestTests.Manifest(version: "1.0.0"));
        Assert.True((await service.InstallAsync(oldPackage, CancellationToken.None)).Succeeded);
        var invalid = CreatePackage(
            PluginManifestTests.Manifest(version: "1.1.0"),
            includeAssembly: false);

        var upgraded = await service.UpgradeAsync(invalid, CancellationToken.None);

        Assert.False(upgraded.Succeeded);
        Assert.True(Directory.Exists(Path.Combine(_pluginRoot, "example.plugin", "1.0.0")));
        Assert.False(Directory.Exists(Path.Combine(_pluginRoot, "example.plugin", "1.1.0")));
    }

    [Fact]
    public async Task Remove_RequiresConfirmationBlocksDependenciesAndPreservesUnrelatedFiles()
    {
        var package = CreatePackage(PluginManifestTests.Manifest());
        var unrelated = Path.Combine(_pluginRoot, "unrelated.keep");
        Directory.CreateDirectory(_pluginRoot);
        await File.WriteAllTextAsync(unrelated, "keep");
        var service = Service();
        Assert.True((await service.InstallAsync(package, CancellationToken.None)).Succeeded);

        var notConfirmed = await service.RemoveAsync(
            "example.plugin",
            "1.0.0",
            false,
            CancellationToken.None);
        var blocked = new PluginPackageService(
            _pluginRoot,
            new PluginManifestParser(),
            new PluginIntegrityService(),
            new FixedUsageInspector(new PluginUsage(["profile:1"], [], [], [])),
            new PluginDiagnostics());
        var dependency = await blocked.RemoveAsync(
            "example.plugin",
            "1.0.0",
            true,
            CancellationToken.None);
        var removed = await service.RemoveAsync(
            "example.plugin",
            "1.0.0",
            true,
            CancellationToken.None);

        Assert.False(notConfirmed.Succeeded);
        Assert.False(dependency.Succeeded);
        Assert.True(removed.Succeeded);
        Assert.True(File.Exists(unrelated));
        Assert.False(Directory.Exists(Path.Combine(_pluginRoot, "example.plugin", "1.0.0")));
    }

    [Fact]
    public async Task Inspect_EntryAssemblyIntegrityMismatch_IsRejectedWithoutPublisherClaims()
    {
        var manifest = PluginManifestTests.Manifest() with
        {
            Integrity = new PluginManifestIntegrity("SHA-256", new string('0', 64)),
        };
        var package = CreatePackage(manifest);

        var result = await Service().InspectAsync(package, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue =>
            issue.Contains("SHA-256", StringComparison.OrdinalIgnoreCase));
    }

    private PluginPackageService Service() =>
        new(
            _pluginRoot,
            new PluginManifestParser(),
            new PluginIntegrityService(),
            new FixedUsageInspector(new PluginUsage([], [], [], [])),
            new PluginDiagnostics());

    private string CreatePackage(
        PluginManifest manifest,
        Action<ZipArchive>? customize = null,
        bool includeAssembly = true)
    {
        var path = Path.Combine(
            _workspace,
            $"{manifest.PluginId}-{manifest.PluginVersion}-{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddBytes(archive, "plugin.json", PluginManifestTests.Serialize(manifest));
        if (includeAssembly)
        {
            AddBytes(
                archive,
                manifest.EntryAssembly,
                File.ReadAllBytes(typeof(PluginPackageTests).Assembly.Location));
        }

        customize?.Invoke(archive);
        return path;
    }

    private static void AddText(ZipArchive archive, string path, string text) =>
        AddBytes(archive, path, System.Text.Encoding.UTF8.GetBytes(text));

    private static void AddBytes(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static void DeleteEmptyParent(string path)
    {
        try
        {
            var parent = Directory.GetParent(path);
            if (parent?.Exists == true &&
                !parent.EnumerateFileSystemInfos().Any())
            {
                parent.Delete();
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Parallel test cleanup may create another child after the empty check.
        }
    }

    private sealed class FixedUsageInspector(PluginUsage usage) : IPluginUsageInspector
    {
        public Task<PluginUsage> InspectAsync(
            string pluginId,
            CancellationToken cancellationToken) =>
            Task.FromResult(usage);
    }
}
