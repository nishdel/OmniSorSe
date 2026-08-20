using System.Diagnostics;
using System.Text;
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

    /// <summary>Verifies active Markdown is valid UTF-8 text without embedded binary control data.</summary>
    [Fact]
    public void MarkdownFiles_AreStrictUtf8Text()
    {
        var issues = new List<string>();
        var strictUtf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

        foreach (var file in MarkdownFiles())
        {
            var bytes = File.ReadAllBytes(file);
            try
            {
                var content = strictUtf8.GetString(bytes);
                if (content.Any(character =>
                        character is '\0' or '\uFFFE' or '\uFFFF' ||
                        (char.IsControl(character) && character is not ('\r' or '\n' or '\t'))))
                {
                    issues.Add($"{Relative(file)} contains binary or unsupported control characters.");
                }
            }
            catch (DecoderFallbackException)
            {
                issues.Add($"{Relative(file)} is not valid UTF-8 text.");
            }
        }

        Assert.True(
            issues.Count == 0,
            $"Invalid documentation text:{Environment.NewLine}{string.Join(Environment.NewLine, issues)}");
    }

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
            "CURRENT-STATE.md",
            "engineering/README.md",
            "engineering/ARCHITECTURE_AUTHORITY.md",
            "engineering/DEVELOPMENT_SYSTEM.md",
            "engineering/RISK_VALIDATION_MATRIX.md",
            "engineering/LEARNING_SYSTEM.md",
            "USER_GUIDE_v1.9.md",
            "TROUBLESHOOTING_v1.8.md",
            "MANUAL_TESTING_v1.9.md",
            "VERSION_NOTES_v1.9.md",
            "RELATIONSHIPS_AND_COLLECTIONS_v1.9.md",
            "V1.9_IMPLEMENTATION_REPORT.md",
            "V1.9_VALIDATION_REPORT.md",
            "MANUAL_TESTING_v2.0.md",
            "RELEASE_READINESS_v2.0.md",
            "RELEASE_NOTES_v2.0.0.md",
            "RELEASE_NOTES_v2.1.0.md",
            "RELEASE_NOTES_v2.2.0.md",
            "CONTENT_INTELLIGENCE_v2.3.md",
            "MANUAL_TESTING_v2.3.md",
            "RELEASE_NOTES_v2.3.0.md",
            "OMNISORSE_TRANSITION_AND_EXPLORER_PROTOCOL_v2.4.md",
            "MANUAL_TESTING_v2.4.md",
            "PRODUCTION_HARDENING_v2.10.md",
            "OPERATIONAL_RUNBOOKS_v2.10.md",
            "MANUAL_TESTING_v2.10.md",
            "RELEASE_NOTES_v2.10.0.md",
            "SUPPORTED_RUNTIME_PLATFORM_READINESS_v2.11.md",
            "MANUAL_TESTING_v2.11.md",
            "RELEASE_NOTES_v2.11.0.md",
            "TRUSTED_RELATIONSHIPS_CONTEXT_v2.12.md",
            "MANUAL_TESTING_v2.12.md",
            "RELEASE_NOTES_v2.12.0.md",
            "SEARCH_AND_AI_QUALITY_v2.1.md",
            "MANUAL_TESTING_v2.1.md",
            "RELEASE_PACKAGING_v2.0.md",
            "SCREENSHOT_CHECKLIST_v2.0.md",
            "V2.0_COMPATIBILITY_MATRIX.md",
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
            "Architecture/00_System/10_v1.7_Deep_Indexing_Architecture.md",
            "Implementation_Spec/v1.7/059_Deep_Indexing_Foundation.md",
            "Architecture/06_Search/09_v1.8_Search_Intelligence_Privacy.md",
            "Implementation_Spec/v1.8/060_Search_Intelligence_Quality_and_Privacy.md",
            "Architecture/06_Search/10_v1.9_Relationships_Context.md",
            "Implementation_Spec/v1.9/061_Relationships_Context_and_Smart_Collections.md",
            "Architecture/06_Search/11_v2.0_Knowledge_Graph_Stability_Design.md",
            "Implementation_Spec/v2.0/00_v2.0_Knowledge_Graph_Stability_Proposal.md",
            "Implementation_Spec/README.md",
        };

        foreach (var target in required)
        {
            Assert.Contains($"({target})", index, StringComparison.Ordinal);
        }

        foreach (var route in new[]
                 {
                     "Current project state",
                     "Use the latest published release",
                     "Develop or contribute",
                     "Understand the current candidate",
                     "Validate or assess readiness",
                     "Research releases or history",
                 })
        {
            Assert.Contains(route, index, StringComparison.Ordinal);
        }

        Assert.Contains("## Current project authorities", index, StringComparison.Ordinal);
        Assert.Contains("## Latest published release", index, StringComparison.Ordinal);
        Assert.Contains("## Current unreleased candidate lineage", index, StringComparison.Ordinal);
        Assert.Contains("## Release and implementation records", index, StringComparison.Ordinal);
    }

    /// <summary>Verifies native release automation names every supported artifact and no Linux installer.</summary>
    [Fact]
    public void ReleasePackaging_UsesNativeRunnersAndExactPublicArtifactNames()
    {
        var validationWorkflow = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            ".github",
            "workflows",
            "cross-platform-validation.yml"));
        var releaseWorkflow = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            ".github",
            "workflows",
            "release-packaging.yml"));

        Assert.Contains("actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1", validationWorkflow, StringComparison.Ordinal);
        Assert.Contains("actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68", validationWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/checkout@v", validationWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/setup-dotnet@v", validationWorkflow, StringComparison.Ordinal);
        Assert.Contains("- \"v*\"", validationWorkflow, StringComparison.Ordinal);
        Assert.Contains("[System.IO.Path]::GetFullPath", validationWorkflow, StringComparison.Ordinal);
        Assert.Contains("[System.Diagnostics.ProcessStartInfo]::new()", validationWorkflow, StringComparison.Ordinal);
        Assert.Contains("ArgumentList.Add($dataRoot)", validationWorkflow, StringComparison.Ordinal);
        Assert.Contains("WaitForExit(60000)", validationWorkflow, StringComparison.Ordinal);
        Assert.Contains("Kill($true)", validationWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("WaitForExit()", validationWorkflow, StringComparison.Ordinal);
        Assert.Contains("$process.ExitCode", validationWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("& $hostPath --package-smoke-test $dataRoot", validationWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/checkout@v", releaseWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/setup-dotnet@v", releaseWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/upload-artifact@v", releaseWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/download-artifact@v", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("windows-latest", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("macos-15-intel", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("osx-x64", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("osx-arm64", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("OmniSorSe-v${{ inputs.version }}-win-x64.zip", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("OmniSorSe-v${{ inputs.version }}-win-x64-setup.exe", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("OmniSorSe-v${{ inputs.version }}-macos-x64.dmg", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("OmniSorSe-v${{ inputs.version }}-macos-arm64.dmg", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("OmniSorSe-v${{ inputs.version }}-sbom.cdx.json", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("OmniSorSe-v${{ inputs.version }}-SHA256SUMS.txt", releaseWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain(".deb", releaseWorkflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".rpm", releaseWorkflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AppImage", releaseWorkflow, StringComparison.OrdinalIgnoreCase);

        foreach (var script in new[]
                 {
                     "Build-WindowsArtifacts.ps1",
                     "Validate-WindowsArtifacts.ps1",
                     "Build-MacArtifacts.sh",
                     "Validate-MacArtifact.sh",
                     "New-ReleaseChecksums.ps1",
                     "New-ReleaseSbom.ps1",
                     "OpenSorSe.iss",
                 })
        {
            Assert.True(
                File.Exists(Path.Combine(RepositoryRoot, "eng", "release", script)),
                $"Missing native release script: {script}");
        }

        var windowsPackaging = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "eng",
            "release",
            "Build-WindowsArtifacts.ps1"));
        var macPackaging = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "eng",
            "release",
            "Build-MacArtifacts.sh"));
        var installer = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "eng",
            "release",
            "OpenSorSe.iss"));
        Assert.Contains("RELEASE_NOTES_v$Version.md", windowsPackaging, StringComparison.Ordinal);
        Assert.Contains("RELEASE_NOTES_v$version.md", macPackaging, StringComparison.Ordinal);
        Assert.DoesNotContain("RELEASE_NOTES_v2.0.0.md", windowsPackaging, StringComparison.Ordinal);
        Assert.Contains("OmniSorSe.exe", windowsPackaging, StringComparison.Ordinal);
        Assert.Contains("targetFramework = 'net10.0'", windowsPackaging, StringComparison.Ordinal);
        Assert.Contains("runtimeIdentifier = 'win-x64'", windowsPackaging, StringComparison.Ordinal);
        Assert.Contains("OmniSorSe.app", macPackaging, StringComparison.Ordinal);
        Assert.Contains("\"targetFramework\":\"net10.0\"", macPackaging, StringComparison.Ordinal);
        Assert.Contains("io.github.nishdel.OpenSorSe", macPackaging, StringComparison.Ordinal);
        Assert.Contains("AppId={{3F3BCA7E-38A1-45D3-B068-B22D25BCECF4}", installer, StringComparison.Ordinal);
        Assert.Contains("DefaultDirName={localappdata}\\Programs\\OpenSorSe", installer, StringComparison.Ordinal);
        Assert.Contains("AppName=OmniSorSe", installer, StringComparison.Ordinal);
        Assert.Contains("DefaultGroupName=OmniSorSe", installer, StringComparison.Ordinal);
        Assert.Contains("UsePreviousGroup=no", installer, StringComparison.Ordinal);
        Assert.Contains("{app}\\OpenSorSe.exe", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("RELEASE_NOTES_v2.0.0.md", macPackaging, StringComparison.Ordinal);
    }

    /// <summary>Verifies one repository authority selects .NET 10 for every project and release path.</summary>
    [Fact]
    public void RuntimeAuthority_TargetsNet10Everywhere()
    {
        var projects = Directory.EnumerateFiles(RepositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsExcluded(path))
            .ToArray();
        Assert.NotEmpty(projects);
        foreach (var project in projects)
        {
            var document = XDocument.Load(project);
            Assert.Empty(document.Descendants("TargetFramework"));
            Assert.Empty(document.Descendants("TargetFrameworks"));
        }

        var globalJson = File.ReadAllText(Path.Combine(RepositoryRoot, "global.json"));
        Assert.Contains("\"version\": \"10.0.400\"", globalJson, StringComparison.Ordinal);
        var buildProperties = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));
        Assert.Equal("net10.0", buildProperties.Descendants("TargetFramework").Single().Value);
        Assert.Equal("2.12.0-rc", buildProperties.Descendants("OmniSorSeVersion").Single().Value);
        Assert.Equal("2.12.0.0", buildProperties.Descendants("OmniSorSeFileVersion").Single().Value);

        var releaseWorkflow = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            ".github",
            "workflows",
            "release-packaging.yml"));
        Assert.Contains("default: 2.12.0", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("global-json-file: global.json", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("Build-WindowsArtifacts.ps1", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("Build-MacArtifacts.sh", releaseWorkflow, StringComparison.Ordinal);
    }

    /// <summary>Verifies production project references remain the documented acyclic dependency graph.</summary>
    [Fact]
    public void ProductionProjects_FollowDocumentedDependencyPolicy()
    {
        var expected = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["OmniSorSe.ExplorerProtocol"] = new HashSet<string>(StringComparer.Ordinal),
            ["OpenSorSe.Extensions.Abstractions"] = new HashSet<string>(StringComparer.Ordinal),
            ["OpenSorSe.Core"] = new HashSet<string>(StringComparer.Ordinal),
            ["OpenSorSe.Scanner"] = new HashSet<string>(["OpenSorSe.Core"], StringComparer.Ordinal),
            ["OpenSorSe.Rules"] = new HashSet<string>(["OpenSorSe.Core", "OpenSorSe.Scanner"], StringComparer.Ordinal),
            ["OpenSorSe.Executor"] = new HashSet<string>(["OpenSorSe.Core", "OpenSorSe.Rules"], StringComparer.Ordinal),
            ["OpenSorSe.Application"] = new HashSet<string>(
                ["OmniSorSe.ExplorerProtocol", "OpenSorSe.Extensions.Abstractions", "OpenSorSe.Core", "OpenSorSe.Executor", "OpenSorSe.Scanner", "OpenSorSe.Rules"],
                StringComparer.Ordinal),
            ["OpenSorSe.AI"] = new HashSet<string>(["OpenSorSe.Application", "OpenSorSe.Core"], StringComparer.Ordinal),
            ["OpenSorSe.Indexing.Sqlite"] = new HashSet<string>(
                ["OpenSorSe.Application", "OpenSorSe.Core"],
                StringComparer.Ordinal),
            ["OpenSorSe.Desktop"] = new HashSet<string>(
                ["OpenSorSe.Extensions.Abstractions", "OpenSorSe.Core", "OpenSorSe.Scanner", "OpenSorSe.Rules", "OpenSorSe.Executor", "OpenSorSe.Application", "OpenSorSe.AI", "OpenSorSe.Indexing.Sqlite"],
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

    /// <summary>Guards high-value hardening boundaries against accidental architectural expansion.</summary>
    [Fact]
    public void ProductionHardeningBoundaries_RemainExplicit()
    {
        var productionSources = Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !IsExcluded(path))
            .Select(File.ReadAllText)
            .ToArray();
        var combined = string.Join('\n', productionSources);
        Assert.DoesNotContain("new TcpListener", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("new HttpListener", combined, StringComparison.Ordinal);

        var desktopSources = Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "OpenSorSe.Desktop"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !IsExcluded(path))
            .Select(File.ReadAllText);
        Assert.DoesNotContain(desktopSources, source =>
            source.Contains("Microsoft.Data.Sqlite", StringComparison.Ordinal));

        foreach (var relativePath in new[]
                 {
                     "src/OpenSorSe.Application/Explorer/ExplorerCompanionLaunch.cs",
                     "src/OpenSorSe.Application/Content/TesseractProcessRunner.cs",
                     "src/OpenSorSe.Application/Media/ExternalMediaProcessRunner.cs",
                 })
        {
            var source = File.ReadAllText(Path.Combine(
                RepositoryRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains("UseShellExecute = false", source, StringComparison.Ordinal);
            Assert.Contains("ArgumentList.Add", source, StringComparison.Ordinal);
        }

        var buildProperties = File.ReadAllText(Path.Combine(RepositoryRoot, "Directory.Build.props"));
        Assert.Contains("<OmniSorSeVersion", buildProperties, StringComparison.Ordinal);
        Assert.Contains("<SourceRevisionId", buildProperties, StringComparison.Ordinal);
        Assert.DoesNotContain(">2.4.0<", buildProperties, StringComparison.Ordinal);

        var indexModels = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "OpenSorSe.Application",
            "Indexing",
            "DeepIndexingModels.cs"));
        Assert.Contains("public const int SchemaVersion = 6;", indexModels, StringComparison.Ordinal);
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

    /// <summary>Verifies volatile version, runtime, schema, and protocol facts have one checked current-state entry point.</summary>
    [Fact]
    public void CurrentState_MatchesRepositoryRuntimeAndContractAuthorities()
    {
        var currentState = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "CURRENT-STATE.md"));
        var buildProperties = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));
        var targetFramework = buildProperties.Descendants("TargetFramework").Single().Value;
        var version = buildProperties.Descendants("OmniSorSeVersion").Single().Value;
        var indexModels = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "OpenSorSe.Application",
            "Indexing",
            "DeepIndexingModels.cs"));
        var protocol = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "OmniSorSe.ExplorerProtocol",
            "ExplorerProtocolContracts.cs"));
        var schema = Regex.Match(indexModels, @"SchemaVersion\s*=\s*(?<value>\d+)")
            .Groups["value"].Value;
        var protocolDisplay = Regex.Match(protocol, "Display\\s*=\\s*\"(?<value>[^\"]+)\"")
            .Groups["value"].Value;

        Assert.NotEmpty(schema);
        Assert.NotEmpty(protocolDisplay);
        Assert.Contains(version, currentState, StringComparison.Ordinal);
        var runtimeMajor = targetFramework.StartsWith("net", StringComparison.Ordinal)
            ? targetFramework[3..].Split('.', 2)[0]
            : targetFramework;
        Assert.Contains(
            $".NET {runtimeMajor}",
            currentState,
            StringComparison.Ordinal);
        Assert.Contains($"schema {schema}", currentState, StringComparison.Ordinal);
        Assert.Contains($"Protocol is **{protocolDisplay}**", currentState, StringComparison.Ordinal);
    }

    /// <summary>Verifies project agents and the substantial-run Skill stay discoverable and structurally complete.</summary>
    [Fact]
    public void CodexConfiguration_DefinesScopedAgentsAndOneProjectRunSkill()
    {
        var agentDirectory = Path.Combine(RepositoryRoot, ".codex", "agents");
        var expectedAgents = new[]
        {
            "adversarial-reviewer.toml",
            "architecture.toml",
            "ax.toml",
            "documentation.toml",
            "dx-maintainability.toml",
            "implementation.toml",
            "performance.toml",
            "product-strategy.toml",
            "ux.toml",
        };
        var actualAgents = Directory.EnumerateFiles(agentDirectory, "*.toml")
            .Select(path => Path.GetFileName(path)!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedAgents.Order(StringComparer.Ordinal), actualAgents);
        var writableAgents = new HashSet<string>(StringComparer.Ordinal)
        {
            "documentation.toml",
            "implementation.toml",
        };

        foreach (var agent in actualAgents)
        {
            var content = File.ReadAllText(Path.Combine(agentDirectory, agent));
            var normalized = content.Replace("\r\n", "\n").TrimEnd('\n');
            var lines = normalized.Split('\n');
            Assert.StartsWith("name = \"", lines[0], StringComparison.Ordinal);
            Assert.StartsWith("description = \"Use ", lines[1], StringComparison.Ordinal);
            Assert.Equal(
                writableAgents.Contains(agent)
                    ? "sandbox_mode = \"workspace-write\""
                    : "sandbox_mode = \"read-only\"",
                lines[2]);
            Assert.Equal("developer_instructions = \"\"\"", lines[3]);
            Assert.Equal("\"\"\"", lines[^1]);
            Assert.Equal(1, lines.Count(line => line.StartsWith("name = ", StringComparison.Ordinal)));
            Assert.Equal(1, lines.Count(line => line.StartsWith("description = ", StringComparison.Ordinal)));
            Assert.Equal(1, lines.Count(line => line.StartsWith("sandbox_mode = ", StringComparison.Ordinal)));
            Assert.Equal(1, lines.Count(line => line.StartsWith("developer_instructions = ", StringComparison.Ordinal)));
        }

        var skillRoot = Path.Combine(RepositoryRoot, ".agents", "skills");
        var skillPath = Assert.Single(Directory.EnumerateFiles(
            skillRoot,
            "SKILL.md",
            SearchOption.AllDirectories));
        Assert.Equal(Path.Combine(
            RepositoryRoot,
            ".agents",
            "skills",
            "omnisorse-engineering-run",
            "SKILL.md"), skillPath);
        var skill = File.ReadAllText(skillPath);
        var normalizedSkill = skill.Replace("\r\n", "\n");
        Assert.Matches(
            "^---\\nname: omnisorse-engineering-run\\ndescription: [^\\n]+\\n---\\n",
            normalizedSkill);
        Assert.Contains("docs/engineering/RISK_VALIDATION_MATRIX.md", skill, StringComparison.Ordinal);
        Assert.Contains("docs/engineering/templates/OWNER_REPORT.md", skill, StringComparison.Ordinal);
    }

    /// <summary>Verifies ADR identifiers are unique, indexed, and preserve the required decision evidence.</summary>
    [Fact]
    public void ArchitectureDecisionRecords_HaveUniqueIdsAndRequiredSections()
    {
        var directory = Path.Combine(RepositoryRoot, "docs", "Architecture", "99_Appendix");
        var records = Directory.EnumerateFiles(directory, "ADR-*.md")
            .Order(StringComparer.Ordinal)
            .ToArray();
        var identifiers = records
            .Select(path => Regex.Match(Path.GetFileName(path), @"^ADR-(?<id>\d{3})_").Groups["id"].Value)
            .ToArray();
        Assert.All(identifiers, identifier => Assert.False(string.IsNullOrWhiteSpace(identifier)));
        Assert.Equal(identifiers.Length, identifiers.Distinct(StringComparer.Ordinal).Count());

        var index = File.ReadAllText(Path.Combine(directory, "ADR.md"));
        foreach (var record in records)
        {
            var identifier = Regex.Match(Path.GetFileName(record), @"^(ADR-\d{3})_").Groups[1].Value;
            var content = File.ReadAllText(record);
            Assert.StartsWith($"# {identifier}", content, StringComparison.Ordinal);
            Assert.Contains("| Status |", content, StringComparison.Ordinal);
            Assert.Contains("## Context", content, StringComparison.Ordinal);
            Assert.Contains("## Decision", content, StringComparison.Ordinal);
            Assert.Contains("## Consequences", content, StringComparison.Ordinal);
            Assert.Contains("## Alternatives considered", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"[{identifier}]", index, StringComparison.Ordinal);
        }
    }

    /// <summary>Prevents generated validation clones and artifacts from becoming tracked parallel authorities.</summary>
    [Fact]
    public void GeneratedValidationRoots_AreIgnoredAndUntracked()
    {
        var ignoreLines = File.ReadAllLines(Path.Combine(RepositoryRoot, ".gitignore"))
            .Select(line => line.Trim())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(".audit-validation/", ignoreLines);
        Assert.Contains(".artifacts/", ignoreLines);

        var gitMetadata = Path.Combine(RepositoryRoot, ".git");
        if (!Directory.Exists(gitMetadata) && !File.Exists(gitMetadata))
        {
            return;
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        process.StartInfo.ArgumentList.Add("ls-files");
        process.StartInfo.ArgumentList.Add("--");
        process.StartInfo.ArgumentList.Add(".audit-validation");
        process.StartInfo.ArgumentList.Add(".artifacts");
        Assert.True(process.Start(), "Unable to start git for tracked generated-root validation.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10_000), "git ls-files did not finish within ten seconds.");
        Assert.True(process.ExitCode == 0, $"git ls-files failed: {error}");
        Assert.True(string.IsNullOrWhiteSpace(output), $"Generated validation files are tracked:{Environment.NewLine}{output}");
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
               relative.StartsWith(".audit-validation/", StringComparison.Ordinal) ||
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

        throw new InvalidOperationException("The OmniSorSe repository root could not be located.");
    }

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlCommentRegex();

    [GeneratedRegex(@"```(?!mermaid).*?```", RegexOptions.Singleline)]
    private static partial Regex FencedCodeRegex();

    [GeneratedRegex(@"!?\[[^\]]*\]\((?<target><[^>]+>|[^\s\)]+)(?:\s+[""'][^""']*[""'])?\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"(?:src|href)=""(?<target>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlLinkRegex();

    [GeneratedRegex(@"^```mermaid(?:[ \t]+[^\r\n`]*)?[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex MermaidOpeningRegex();

    [GeneratedRegex(@"^```mermaid(?:[ \t]+[^\r\n`]*)?[ \t]*\r?\n(?<body>.*?)^```[ \t]*$", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex MermaidBlockRegex();

    [GeneratedRegex(@"^(flowchart|graph|sequenceDiagram|classDiagram|stateDiagram(?:-v2)?|erDiagram|journey|gantt|pie|mindmap|timeline|gitGraph|C4)\b")]
    private static partial Regex MermaidDirectiveRegex();

    [GeneratedRegex(@"^public\s+(?:sealed\s+record|static\s+class|interface|enum)\s+\w+")]
    private static partial Regex PublicSdkTypeRegex();
}
