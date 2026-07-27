#pragma warning disable CS1591

using System.Text.Json;
using OpenSorSe.Extensions.Abstractions;

namespace OpenSorSe.Application.Plugins;

public static class BuiltInPluginCatalog
{
    public static IReadOnlyList<BuiltInPluginDefinition> Definitions { get; } =
        Array.AsReadOnly(
        [
            Definition(
                "opensorse.reference.metadata",
                "Reference Filesystem Metadata",
                "reference.filesystem-metadata",
                "Filesystem Metadata",
                ExtensionPointKind.MetadataProvider,
                [PluginCapability.ReadFileMetadata],
                typeof(ReferenceMetadataPlugin),
                static () => new ReferenceMetadataPlugin()),
            Definition(
                "opensorse.reference.classifier",
                "Reference Extension Classifier",
                "reference.extension-classifier",
                "Extension Classifier",
                ExtensionPointKind.FileClassifier,
                [PluginCapability.ReadFileMetadata],
                typeof(ReferenceClassifierPlugin),
                static () => new ReferenceClassifierPlugin()),
            Definition(
                "opensorse.reference.recipe-fields",
                "Reference Recipe Fields",
                "reference.standard-extension-group",
                "Standard Extension Group",
                ExtensionPointKind.RecipeFieldProvider,
                [PluginCapability.ReadFileMetadata, PluginCapability.ContributeRecipeFields],
                typeof(ReferenceRecipeFieldPlugin),
                static () => new ReferenceRecipeFieldPlugin()),
            Definition(
                "opensorse.reference.json-export",
                "Reference JSON Report Export",
                "reference.json-report",
                "JSON Report",
                ExtensionPointKind.ExportFormatProvider,
                [PluginCapability.ExportReports],
                typeof(ReferenceJsonExportPlugin),
                static () => new ReferenceJsonExportPlugin()),
        ]);

    private static BuiltInPluginDefinition Definition(
        string id,
        string name,
        string contributionId,
        string contributionName,
        ExtensionPointKind extensionPoint,
        IReadOnlyList<PluginCapability> capabilities,
        Type entryType,
        Func<IOpenSorSePlugin> factory) =>
        new(
            new PluginManifest(
                PluginLimits.CurrentManifestSchemaVersion,
                id,
                name,
                "A low-risk built-in v1.4 reference plugin that proves the public extension SDK and host boundary.",
                "1.4.0",
                "OpenSorSe contributors",
                "MIT",
                "1.4.0",
                "1.4.99",
                "net8.0",
                "OpenSorSe.Application.dll",
                entryType.FullName!,
                [new PluginManifestContribution(contributionId, extensionPoint, contributionName)],
                capabilities,
                [],
                "https://github.com/nishdel/OpenSorSe",
                "https://github.com/nishdel/OpenSorSe",
                true,
                null),
            factory);
}

public abstract class SingleContributionPlugin : IOpenSorSePlugin
{
    protected abstract IExtensionContribution Contribution { get; }

    public Task<ExtensionResult<IReadOnlyList<IExtensionContribution>>> InitializeAsync(
        PluginInitializationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<IExtensionContribution> contributions = [Contribution];
        return Task.FromResult(ExtensionResult<IReadOnlyList<IExtensionContribution>>.Success(contributions));
    }

    public Task<ExtensionResult<bool>> StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ExtensionResult<bool>.Success(true));
    }
}

public sealed class ReferenceMetadataPlugin : SingleContributionPlugin
{
    private readonly ReferenceFilesystemMetadataProvider _contribution = new();
    protected override IExtensionContribution Contribution => _contribution;
}

public sealed class ReferenceClassifierPlugin : SingleContributionPlugin
{
    private readonly ReferenceExtensionClassifier _contribution = new();
    protected override IExtensionContribution Contribution => _contribution;
}

public sealed class ReferenceRecipeFieldPlugin : SingleContributionPlugin
{
    private readonly ReferenceRecipeFieldProvider _contribution = new();
    protected override IExtensionContribution Contribution => _contribution;
}

public sealed class ReferenceJsonExportPlugin : SingleContributionPlugin
{
    private readonly ReferenceJsonReportExporter _contribution = new();
    protected override IExtensionContribution Contribution => _contribution;
}

public sealed class ReferenceFilesystemMetadataProvider : IMetadataProvider
{
    public string Id => "reference.filesystem-metadata";
    public string DisplayName => "Filesystem Metadata";
    public int Priority => 0;

    public Task<ExtensionResult<MetadataResponse>> GetMetadataAsync(
        MetadataRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var info = new FileInfo(request.File.FullPath);
            if (!info.Exists)
            {
                return Task.FromResult(ExtensionResult<MetadataResponse>.Failure(
                    "metadata.file-missing",
                    "The host-selected file is unavailable."));
            }

            IReadOnlyList<ExtensionValue> fields =
            [
                Value("fileName", info.Name),
                Value("extension", info.Extension.TrimStart('.').ToLowerInvariant()),
                new ExtensionValue(
                    "sizeBytes",
                    ExtensionValueKind.Integer,
                    info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ExtensionDerivationKind.Deterministic,
                    "Read from filesystem metadata."),
                new ExtensionValue(
                    "modifiedUtc",
                    ExtensionValueKind.DateTime,
                    info.LastWriteTimeUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                    ExtensionDerivationKind.Deterministic,
                    "Read from filesystem metadata."),
            ];
            return Task.FromResult(ExtensionResult<MetadataResponse>.Success(
                new MetadataResponse(Array.AsReadOnly(fields.Take(request.MaximumFields).ToArray()))));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Task.FromResult(ExtensionResult<MetadataResponse>.Failure(
                "metadata.read-failed",
                "Filesystem metadata could not be read."));
        }
    }

    private static ExtensionValue Value(string name, string value) =>
        new(
            name,
            ExtensionValueKind.Text,
            value,
            ExtensionDerivationKind.Deterministic,
            "Read from filesystem metadata.");
}

public sealed class ReferenceExtensionClassifier : IFileClassifier
{
    private static readonly IReadOnlyDictionary<string, string> Categories =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "document",
            [".doc"] = "document",
            [".docx"] = "document",
            [".txt"] = "document",
            [".jpg"] = "image",
            [".jpeg"] = "image",
            [".png"] = "image",
            [".gif"] = "image",
            [".mp3"] = "audio",
            [".wav"] = "audio",
            [".mp4"] = "video",
            [".mkv"] = "video",
            [".zip"] = "archive",
        };

    public string Id => "reference.extension-classifier";
    public string DisplayName => "Extension Classifier";
    public int Priority => 0;

    public Task<ExtensionResult<ClassificationResponse>> ClassifyAsync(
        ClassificationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var label = Categories.TryGetValue(request.File.NormalizedExtension, out var category)
            ? category
            : "other";
        IReadOnlyList<ClassificationLabel> labels =
        [
            new(
                label,
                label == "other" ? 0.5 : 1,
                "Deterministic mapping from the normalized extension.",
                ExtensionDerivationKind.Deterministic),
        ];
        return Task.FromResult(ExtensionResult<ClassificationResponse>.Success(
            new ClassificationResponse(labels)));
    }
}

public sealed class ReferenceRecipeFieldProvider : IRecipeFieldProvider
{
    public string Id => "reference.standard-extension-group";
    public string DisplayName => "Standard Extension Group";
    public int Priority => 0;

    public Task<ExtensionResult<RecipeFieldResponse>> ResolveFieldAsync(
        RecipeFieldRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var extension = request.File.NormalizedExtension.TrimStart('.').ToLowerInvariant();
        var value = string.IsNullOrWhiteSpace(extension) ? "no-extension" : extension;
        return Task.FromResult(ExtensionResult<RecipeFieldResponse>.Success(
            new RecipeFieldResponse(
                new ExtensionValue(
                    request.FieldName,
                    ExtensionValueKind.Text,
                    value,
                    ExtensionDerivationKind.Deterministic,
                    "Derived from the host-normalized file extension.",
                    request.File.NormalizedExtension,
                    1))));
    }
}

public sealed class ReferenceJsonReportExporter : IExportFormatProvider
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string Id => "reference.json-report";
    public string DisplayName => "JSON Report";
    public int Priority => 0;

    public Task<ExtensionResult<ExportResponse>> ExportAsync(
        ExportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var payload = JsonSerializer.SerializeToUtf8Bytes(request.Rows, Options);
        if (payload.Length > request.MaximumOutputBytes)
        {
            return Task.FromResult(ExtensionResult<ExportResponse>.Failure(
                "export.size",
                "The JSON report exceeds the host-provided output bound."));
        }

        return Task.FromResult(ExtensionResult<ExportResponse>.Success(
            new ExportResponse(
                "opensorse-report.json",
                "application/json",
                payload)));
    }
}
