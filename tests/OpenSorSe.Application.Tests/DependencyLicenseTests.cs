using System.Text.Json;

namespace OpenSorSe.Application.Tests;

/// <summary>Guards the resolved dependency set against the committed FOSS inventory.</summary>
public sealed class DependencyLicenseTests
{
    private static readonly HashSet<string> AllowedLicenses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Apache-2.0",
            "BSD-3-Clause",
            "MIT",
        };

    /// <summary>Verifies every current resolved package/version is known and permissively licensed.</summary>
    [Fact]
    public void Inventory_CoversCurrentResolvedPackages_AndRejectsForbiddenLicenses()
    {
        var repositoryRoot = FindRepositoryRoot();
        var inventoryPath = Path.Combine(repositoryRoot, "docs", "dependency-licenses.json");
        using var inventory = JsonDocument.Parse(File.ReadAllText(inventoryPath));
        var packages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in inventory.RootElement.GetProperty("groups").EnumerateArray())
        {
            var license = RequiredString(group, "license");
            Assert.Contains(license, AllowedLicenses);
            Assert.DoesNotContain("UNKNOWN", license, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("AGPL", license, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("GPL", license, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PROPRIETARY", license, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("NONCOMMERCIAL", license, StringComparison.OrdinalIgnoreCase);
            _ = RequiredString(group, "purpose");
            _ = RequiredString(group, "upstream");
            _ = RequiredString(group, "notice");
            _ = RequiredString(group, "distributionStatus");
            _ = RequiredString(group, "redistribution");

            foreach (var package in group.GetProperty("packages").EnumerateArray())
            {
                var identity = package.GetString();
                Assert.False(string.IsNullOrWhiteSpace(identity));
                Assert.True(
                    packages.TryAdd(identity!, license),
                    $"Dependency inventory contains duplicate identity '{identity}'.");
            }
        }

        foreach (var component in inventory.RootElement.GetProperty("externalComponents").EnumerateArray())
        {
            var license = RequiredString(component, "license");
            Assert.Contains(license, AllowedLicenses);
            Assert.Equal("external-not-bundled", RequiredString(component, "distributionStatus"));
            Assert.True(component.GetProperty("optional").GetBoolean());
        }

        var resolved = ReadLatestResolvedPackages(repositoryRoot);
        Assert.NotEmpty(resolved);
        Assert.Equal(
            resolved.Order(StringComparer.OrdinalIgnoreCase),
            packages.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            resolved,
            identity => identity.StartsWith(
                "AvaloniaUI.DiagnosticsSupport@",
                StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<string> ReadLatestResolvedPackages(string repositoryRoot)
    {
        var latestAssetsByProject = Directory
            .EnumerateFiles(repositoryRoot, "project.assets.json", SearchOption.AllDirectories)
            .Where(path =>
                path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = path,
                LastWrite = File.GetLastWriteTimeUtc(path),
                ProjectName = ReadProjectName(path),
            })
            .Where(candidate => candidate.ProjectName is not null)
            .GroupBy(candidate => candidate.ProjectName!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.LastWrite).First())
            .ToArray();

        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assets in latestAssetsByProject)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(assets.Path));
            foreach (var library in document.RootElement.GetProperty("libraries").EnumerateObject())
            {
                if (library.Value.GetProperty("type").GetString() == "package")
                {
                    var separator = library.Name.LastIndexOf('/');
                    Assert.True(separator > 0, $"Unexpected NuGet asset identity '{library.Name}'.");
                    resolved.Add($"{library.Name[..separator]}@{library.Name[(separator + 1)..]}");
                }
            }
        }

        return resolved;
    }

    private static string? ReadProjectName(string assetsPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        return document.RootElement
            .GetProperty("project")
            .GetProperty("restore")
            .TryGetProperty("projectName", out var projectName)
            ? projectName.GetString()
            : null;
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName).GetString();
        Assert.False(string.IsNullOrWhiteSpace(value), $"Inventory property '{propertyName}' is required.");
        return value!;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenSorSe.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the OpenSorSe repository root.");
    }
}
