using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OpenSorSe.Core.Tests;

/// <summary>
/// Protects the repository-level documentation entry points and architectural
/// dependency rules that ordinary project compilation cannot validate.
/// </summary>
public sealed partial class RepositoryDocumentationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>Verifies every active relative Markdown/HTML link resolves to an existing repository path.</summary>
    [Fact]
    public void MarkdownRelativeLinks_ResolveToExistingPaths()
    {
        var issues = new List<string>();
        foreach (var file in MarkdownFiles())
        {
            var content = File.ReadAllText(file);
            var activeContent = FencedCodeRegex().Replace(
                HtmlCommentRegex().Replace(content, string.Empty),
                string.Empty);
            var targets = MarkdownLinkRegex().Matches(activeContent)
                .Select(match => match.Groups["target"].Value.Trim('<', '>'))
                .Concat(HtmlLinkRegex().Matches(activeContent)
                    .Select(match => match.Groups["target"].Value));

            foreach (var target in targets)
            {
                if (string.IsNullOrWhiteSpace(target) ||
                    target.StartsWith('#') ||
                    Uri.TryCreate(target, UriKind.Absolute, out _))
                {
                    continue;
                }

                var pathPart = Uri.UnescapeDataString(target.Split('#', 2)[0]);
                if (string.IsNullOrWhiteSpace(pathPart))
                {
                    continue;
                }

                var baseDirectory = Path.GetDirectoryName(file) ?? RepositoryRoot;
                var resolved = Path.GetFullPath(Path.Combine(
                    baseDirectory,
                    pathPart.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    issues.Add($"{Relative(file)} -> {target}");
                }
                else if (!HasExactRepositoryPathCasing(resolved))
                {
                    issues.Add($"{Relative(file)} -> {target} (path casing does not match the repository)");
                }
            }
        }

        Assert.True(issues.Count == 0, $"Broken documentation links:{Environment.NewLine}{string.Join(Environment.NewLine, issues)}");
    }

    /// <summary>Verifies Mermaid fences are balanced and declare a supported diagram grammar.</summary>
    [Fact]
    public void MermaidBlocks_AreStructurallyValid()
    {
        var issues = new List<string>();
        var blockCount = 0;
        foreach (var file in MarkdownFiles())
        {
            var content = File.ReadAllText(file);
            var openingCount = MermaidOpeningRegex().Matches(content).Count;
            var blocks = MermaidBlockRegex().Matches(content);
            if (openingCount != blocks.Count)
            {
                issues.Add($"{Relative(file)} has {openingCount} Mermaid openings but {blocks.Count} complete blocks.");
            }

            foreach (Match block in blocks)
            {
                blockCount++;
                var firstLine = block.Groups["body"].Value
                    .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();
                if (firstLine is null || !MermaidDirectiveRegex().IsMatch(firstLine))
                {
                    issues.Add($"{Relative(file)} starts a Mermaid block with unsupported syntax '{firstLine ?? "<empty>"}'.");
                }
            }
        }

        var systemMap = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "docs",
            "Architecture",
            "OpenSorSe_System_Map.md"));
        Assert.Equal(5, MermaidBlockRegex().Matches(systemMap).Count);
        Assert.True(blockCount > 0);
        Assert.True(issues.Count == 0, $"Malformed Mermaid blocks:{Environment.NewLine}{string.Join(Environment.NewLine, issues)}");
    }

    /// <summary>Verifies the central index links every required audience and architecture entry point.</summary>
    [Fact]
    public void DocumentationIndex_ContainsRequiredEntryPoints()
    {
        var indexPath = Path.Combine(RepositoryRoot, "docs", "README.md");
        var index = File.ReadAllText(indexPath).Replace('\\', '/');
        var required = new[]
        {
            "../README.md",
            "../CONTRIBUTING.md",
            "USER_GUIDE_v1.6.md",
            "TROUBLESHOOTING_v1.6.md",
            "MANUAL_TESTING_v1.6.md",
            "VERSION_NOTES_v1.6.md",
            "V1.6_IMPLEMENTATION_REPORT.md",
            "V1.6_VALIDATION_REPORT.md",
            "PLATFORM_COMPATIBILITY_MATRIX.md",
            "LINUX_BUILD_AND_LAUNCH.md",
            "SAFETY_AND_PRIVACY.md",
            "ARCHITECTURE_OVERVIEW.md",
            "REPOSITORY_STRUCTURE.md",
            "DEVELOPER_GUIDE.md",
            "MAINTAINER_GUIDE.md",
            "Architecture/OpenSorSe_System_Map.md",
            "EXTENSION_SDK_v1.4.md",
            "PLUGIN_AUTHOR_GUIDE_v1.4.md",
            "PLUGIN_PLATFORM_COMPATIBILITY_v1.5.md",
            "WORKFLOW_PORTABILITY_v1.5.md",
            "WATCHED_FOLDERS_LINUX_v1.5.md",
            "Architecture/00_System/09_v1.6_Reliability_Architecture.md",
            "Implementation_Spec/v1.6/058_Reliability_Performance_and_Production_Hardening.md",
            "Implementation_Spec/README.md",
        };

        foreach (var target in required)
        {
            Assert.Contains($"({target})", index, StringComparison.Ordinal);
        }
    }

    /// <summary>Verifies production project references remain the documented acyclic dependency graph.</summary>
    [Fact]
    public void ProductionProjects_FollowDocumentedDependencyPolicy()
    {
        var expected = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["OpenSorSe.Extensions.Abstractions"] = new HashSet<string>(StringComparer.Ordinal),
            ["OpenSorSe.Core"] = new HashSet<string>(StringComparer.Ordinal),
            ["OpenSorSe.Scanner"] = new HashSet<string>(["OpenSorSe.Core"], StringComparer.Ordinal),
            ["OpenSorSe.Rules"] = new HashSet<string>(["OpenSorSe.Core", "OpenSorSe.Scanner"], StringComparer.Ordinal),
            ["OpenSorSe.Executor"] = new HashSet<string>(["OpenSorSe.Core", "OpenSorSe.Rules"], StringComparer.Ordinal),
            ["OpenSorSe.Application"] = new HashSet<string>(
                ["OpenSorSe.Extensions.Abstractions", "OpenSorSe.Core", "OpenSorSe.Executor", "OpenSorSe.Scanner", "OpenSorSe.Rules"],
                StringComparer.Ordinal),
            ["OpenSorSe.AI"] = new HashSet<string>(["OpenSorSe.Application", "OpenSorSe.Core"], StringComparer.Ordinal),
            ["OpenSorSe.Desktop"] = new HashSet<string>(
                ["OpenSorSe.Extensions.Abstractions", "OpenSorSe.Core", "OpenSorSe.Scanner", "OpenSorSe.Rules", "OpenSorSe.Executor", "OpenSorSe.Application", "OpenSorSe.AI"],
                StringComparer.Ordinal),
        };

        foreach (var projectPath in Directory.EnumerateFiles(
                     Path.Combine(RepositoryRoot, "src"),
                     "*.csproj",
                     SearchOption.AllDirectories))
        {
            var project = Path.GetFileNameWithoutExtension(projectPath);
            Assert.True(expected.TryGetValue(project, out var allowed), $"Undocumented production project '{project}'.");
            var actual = XDocument.Load(projectPath)
                .Descendants("ProjectReference")
                .Select(element => GetProjectReferenceName((string?)element.Attribute("Include")))
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .ToHashSet(StringComparer.Ordinal);
            Assert.True(
                actual.SetEquals(allowed!),
                $"{project} references [{string.Join(", ", actual.Order())}] but policy expects [{string.Join(", ", allowed!.Order())}].");
        }
    }

    /// <summary>Verifies every public Extension SDK type remains discoverable through XML documentation.</summary>
    [Fact]
    public void ExtensionSdk_PublicTypesHaveXmlDocumentation()
    {
        var path = Path.Combine(
            RepositoryRoot,
            "src",
            "OpenSorSe.Extensions.Abstractions",
            "ExtensionContracts.cs");
        var source = File.ReadAllText(path);
        var lines = File.ReadAllLines(path);
        var publicTypes = lines
            .Select((line, index) => (line, index))
            .Where(item => PublicSdkTypeRegex().IsMatch(item.line))
            .ToArray();
        var undocumented = new List<string>();
        foreach (var (declaration, index) in publicTypes)
        {
            var documentation = new List<string>();
            for (var cursor = index - 1;
                 cursor >= 0 && lines[cursor].TrimStart().StartsWith("///", StringComparison.Ordinal);
                 cursor--)
            {
                documentation.Add(lines[cursor]);
            }

            if (!documentation.Any(line => line.Contains("<summary>", StringComparison.Ordinal)))
            {
                undocumented.Add(declaration.Trim());
            }
        }

        Assert.True(publicTypes.Length > 0);
        Assert.True(
            undocumented.Count == 0,
            $"Undocumented public SDK types:{Environment.NewLine}{string.Join(Environment.NewLine, undocumented)}");
        Assert.Contains("does not authorize direct user-file mutation", source, StringComparison.Ordinal);
        Assert.Contains("not sandboxing", source, StringComparison.Ordinal);
    }

    /// <summary>Verifies the automated suite contains no statically skipped xUnit tests.</summary>
    [Fact]
    public void AutomatedTests_ContainNoSkipDeclarations()
    {
        var skipped = Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "tests"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !IsExcluded(path))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Path: path, Line: index + 1, Text: line)))
            .Where(item =>
                item.Text.Contains(string.Concat("Sk", "ip ="), StringComparison.Ordinal) ||
                item.Text.Contains(string.Concat("Sk", "ip="), StringComparison.Ordinal))
            .Select(item => $"{Relative(item.Path)}:{item.Line}")
            .ToArray();

        Assert.True(
            skipped.Length == 0,
            $"Skipped test declarations:{Environment.NewLine}{string.Join(Environment.NewLine, skipped)}");
    }

    private static IEnumerable<string> MarkdownFiles() =>
        Directory.EnumerateFiles(RepositoryRoot, "*.md", SearchOption.AllDirectories)
            .Where(path => !IsExcluded(path))
            .Order(StringComparer.Ordinal);

    private static bool IsExcluded(string path)
    {
        var relative = Relative(path).Replace('\\', '/');
        return relative.StartsWith(".git/", StringComparison.Ordinal) ||
               relative.StartsWith(".artifacts/", StringComparison.Ordinal) ||
               relative.StartsWith("release/", StringComparison.Ordinal) ||
               relative.Contains("/bin/", StringComparison.Ordinal) ||
               relative.Contains("/obj/", StringComparison.Ordinal);
    }

    private static string Relative(string path) =>
        Path.GetRelativePath(RepositoryRoot, path);

    private static string? GetProjectReferenceName(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFileNameWithoutExtension(
                path.Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar));

    private static bool HasExactRepositoryPathCasing(string path)
    {
        var relative = Path.GetRelativePath(RepositoryRoot, path);
        if (relative == "." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return relative == ".";
        }

        var current = RepositoryRoot;
        foreach (var segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var exact = Directory.EnumerateFileSystemEntries(current)
                .Select(Path.GetFileName)
                .FirstOrDefault(name => string.Equals(name, segment, StringComparison.Ordinal));
            if (exact is null)
            {
                return false;
            }

            current = Path.Combine(current, exact);
        }

        return true;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenSorSe.sln")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The OpenSorSe repository root could not be located.");
    }

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlCommentRegex();

    [GeneratedRegex(@"```(?!mermaid).*?```", RegexOptions.Singleline)]
    private static partial Regex FencedCodeRegex();

    [GeneratedRegex(@"!?\[[^\]]*\]\((?<target><[^>]+>|[^\s\)]+)(?:\s+[""'][^""']*[""'])?\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"(?:src|href)=""(?<target>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlLinkRegex();

    [GeneratedRegex(@"^```mermaid\s*$", RegexOptions.Multiline)]
    private static partial Regex MermaidOpeningRegex();

    [GeneratedRegex(@"^```mermaid\s*\r?\n(?<body>.*?)^```\s*$", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex MermaidBlockRegex();

    [GeneratedRegex(@"^(flowchart|graph|sequenceDiagram|classDiagram|stateDiagram(?:-v2)?|erDiagram|journey|gantt|pie|mindmap|timeline|gitGraph|C4)\b")]
    private static partial Regex MermaidDirectiveRegex();

    [GeneratedRegex(@"^public\s+(?:sealed\s+record|static\s+class|interface|enum)\s+\w+")]
    private static partial Regex PublicSdkTypeRegex();
}
