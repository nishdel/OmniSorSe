#pragma warning disable CS1591

using OpenSorSe.Application.Models;
using OpenSorSe.Extensions.Abstractions;

namespace OpenSorSe.Application.Plugins;

/// <summary>
/// Invokes active plugin contributions with host timeouts, cancellation, exception containment, and output validation.
/// </summary>
/// <remarks>
/// The registry has already enforced manifest/capability ownership. This host
/// adds per-call bounds and rejects an invalid response atomically; it does not
/// trust a successful plugin result merely because the call returned. Requests
/// expose no host services or mutation authority.
/// </remarks>
public sealed class PluginExtensionHost : IPluginExtensionHost
{
    private const int MaximumFields = 128;
    private const int MaximumLabels = 32;
    private const int MaximumTextCharacters = 1_000_000;
    private const int MaximumValueCharacters = 8_192;
    private const int MaximumOutputBytes = 16 * 1024 * 1024;
    private readonly IPluginContributionRegistry _registry;
    private readonly IPluginDiagnostics _diagnostics;

    public PluginExtensionHost(
        IPluginContributionRegistry registry,
        IPluginDiagnostics diagnostics)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public Task<ExtensionResult<MetadataResponse>> GetMetadataAsync(
        string pluginId,
        string contributionId,
        MetadataRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync<IMetadataProvider, MetadataResponse>(
            pluginId,
            contributionId,
            ExtensionPointKind.MetadataProvider,
            (provider, invocationCancellation) =>
                provider.GetMetadataAsync(request, invocationCancellation),
            value => value.Fields.Count <= Math.Min(MaximumFields, request.MaximumFields) &&
                     value.Fields.All(ValidValue),
            cancellationToken);

    public Task<ExtensionResult<ContentExtractionResponse>> ExtractContentAsync(
        string pluginId,
        string contributionId,
        ContentExtractionRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync<IContentExtractor, ContentExtractionResponse>(
            pluginId,
            contributionId,
            ExtensionPointKind.ContentExtractor,
            (provider, invocationCancellation) =>
                provider.ExtractAsync(request, invocationCancellation),
            value =>
                value.Fields.Count <= Math.Min(MaximumFields, request.MaximumFields) &&
                value.Fields.All(ValidValue) &&
                value.Text is null or { Length: <= MaximumTextCharacters } &&
                (value.Text?.Length ?? 0) <= request.MaximumTextCharacters,
            cancellationToken);

    public Task<ExtensionResult<ClassificationResponse>> ClassifyAsync(
        string pluginId,
        string contributionId,
        ClassificationRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync<IFileClassifier, ClassificationResponse>(
            pluginId,
            contributionId,
            ExtensionPointKind.FileClassifier,
            (provider, invocationCancellation) =>
                provider.ClassifyAsync(request, invocationCancellation),
            value =>
                value.Labels.Count <= Math.Min(MaximumLabels, request.MaximumLabels) &&
                value.Labels.All(label =>
                    Bounded(label.Label, 256) &&
                    Bounded(label.Reason, 2_048) &&
                    double.IsFinite(label.Confidence) &&
                    label.Confidence is >= 0 and <= 1 &&
                    Enum.IsDefined(label.Derivation)),
            cancellationToken);

    public Task<ExtensionResult<RecipeFieldResponse>> ResolveRecipeFieldAsync(
        string pluginId,
        string contributionId,
        RecipeFieldRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync<IRecipeFieldProvider, RecipeFieldResponse>(
            pluginId,
            contributionId,
            ExtensionPointKind.RecipeFieldProvider,
            (provider, invocationCancellation) =>
                provider.ResolveFieldAsync(request, invocationCancellation),
            value =>
                value.Field is null ||
                ValidValue(value.Field) &&
                string.Equals(value.Field.Name, request.FieldName, StringComparison.Ordinal),
            cancellationToken);

    public Task<ExtensionResult<DuplicateSignalResponse>> AnalyzeDuplicateAsync(
        string pluginId,
        string contributionId,
        DuplicateSignalRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync<IDuplicateSignalProvider, DuplicateSignalResponse>(
            pluginId,
            contributionId,
            ExtensionPointKind.DuplicateSignalProvider,
            (provider, invocationCancellation) =>
                provider.AnalyzeAsync(request, invocationCancellation),
            value =>
                double.IsFinite(value.Similarity) &&
                value.Similarity is >= 0 and <= 1 &&
                Bounded(value.SignalKind, 128) &&
                Bounded(value.Reason, 2_048) &&
                Enum.IsDefined(value.Derivation),
            cancellationToken);

    public Task<ExtensionResult<WorkflowCapabilityResponse>> ResolveWorkflowCapabilityAsync(
        string pluginId,
        string contributionId,
        WorkflowCapabilityRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync<IWorkflowCapabilityProvider, WorkflowCapabilityResponse>(
            pluginId,
            contributionId,
            ExtensionPointKind.WorkflowCapabilityProvider,
            (provider, invocationCancellation) =>
                provider.ResolveAsync(request, invocationCancellation),
            value =>
                value.Outputs.Count <= MaximumFields &&
                value.Outputs.All(pair => Bounded(pair.Key, 128) && Bounded(pair.Value, MaximumValueCharacters)) &&
                Bounded(value.Reason, 2_048) &&
                Enum.IsDefined(value.Derivation),
            cancellationToken);

    public Task<ExtensionResult<ImportResponse>> ImportAsync(
        string pluginId,
        string contributionId,
        ImportRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync<IImportFormatProvider, ImportResponse>(
            pluginId,
            contributionId,
            ExtensionPointKind.ImportFormatProvider,
            (provider, invocationCancellation) =>
                provider.ImportAsync(request, invocationCancellation),
            value =>
                value.Proposals.Count <= request.MaximumProposalCount &&
                value.Proposals.Count <= 1_000 &&
                value.Proposals.All(proposal =>
                    Bounded(proposal.ProposalType, 128) &&
                    Bounded(proposal.Reason, 2_048) &&
                    proposal.Values.Count <= MaximumFields &&
                    proposal.Values.All(pair =>
                        Bounded(pair.Key, 128) &&
                        Bounded(pair.Value, MaximumValueCharacters))),
            cancellationToken);

    public Task<ExtensionResult<ExportResponse>> ExportAsync(
        string pluginId,
        string contributionId,
        ExportRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync<IExportFormatProvider, ExportResponse>(
            pluginId,
            contributionId,
            ExtensionPointKind.ExportFormatProvider,
            (provider, invocationCancellation) =>
                provider.ExportAsync(request, invocationCancellation),
            value =>
                value.Payload.Length <= Math.Min(MaximumOutputBytes, request.MaximumOutputBytes) &&
                Bounded(value.SuggestedFileName, 256) &&
                Path.GetFileName(value.SuggestedFileName) == value.SuggestedFileName &&
                value.SuggestedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                Bounded(value.MediaType, 128),
            cancellationToken);

    private async Task<ExtensionResult<TResponse>> InvokeAsync<TProvider, TResponse>(
        string pluginId,
        string contributionId,
        ExtensionPointKind kind,
        Func<TProvider, CancellationToken, Task<ExtensionResult<TResponse>>> invoke,
        Func<TResponse, bool> validate,
        CancellationToken cancellationToken)
        where TProvider : class, IExtensionContribution
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contributionId);
        ArgumentNullException.ThrowIfNull(invoke);
        ArgumentNullException.ThrowIfNull(validate);
        var registration = _registry.Find(pluginId, contributionId, kind);
        if (registration?.Instance is not TProvider provider)
        {
            return ExtensionResult<TResponse>.Failure(
                "plugin.contribution-unavailable",
                "The requested plugin contribution is unavailable.");
        }

        using var invocationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        invocationCancellation.CancelAfter(PluginLimits.InitializationTimeout);
        try
        {
            var result = await invoke(provider, invocationCancellation.Token)
                .WaitAsync(PluginLimits.InitializationTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return ExtensionResult<TResponse>.Failure(
                    SafeCode(result.ErrorCode),
                    SafeText(result.Message, 2_048),
                    SafeWarnings(result.Warnings));
            }

            if (result.Value is null || !validate(result.Value))
            {
                _diagnostics.Record(
                    PluginDiagnosticKind.ContributionRegistration,
                    pluginId,
                    $"Contribution \"{contributionId}\" returned output rejected by host validation.",
                    "plugin.output-invalid");
                return ExtensionResult<TResponse>.Failure(
                    "plugin.output-invalid",
                    "The plugin returned invalid or excessive output.");
            }

            return ExtensionResult<TResponse>.Success(
                result.Value,
                SafeText(result.Message, 2_048),
                SafeWarnings(result.Warnings));
        }
        catch (TimeoutException)
        {
            invocationCancellation.Cancel();
            _diagnostics.Record(
                PluginDiagnosticKind.Timeout,
                pluginId,
                $"Contribution \"{contributionId}\" timed out.",
                "plugin.contribution-timeout");
            return ExtensionResult<TResponse>.Failure(
                "plugin.contribution-timeout",
                "The plugin contribution timed out.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _diagnostics.Record(
                PluginDiagnosticKind.Cancellation,
                pluginId,
                $"Contribution \"{contributionId}\" was cancelled.");
            throw;
        }
        catch (OperationCanceledException)
        {
            _diagnostics.Record(
                PluginDiagnosticKind.Timeout,
                pluginId,
                $"Contribution \"{contributionId}\" timed out.",
                "plugin.contribution-timeout");
            return ExtensionResult<TResponse>.Failure(
                "plugin.contribution-timeout",
                "The plugin contribution timed out.");
        }
        catch (Exception exception)
        {
            _diagnostics.Record(
                PluginDiagnosticKind.ContributionRegistration,
                pluginId,
                $"Contribution \"{contributionId}\" failed without escaping the host boundary.",
                exception.GetType().Name);
            return ExtensionResult<TResponse>.Failure(
                "plugin.exception",
                "The plugin contribution failed safely.");
        }
    }

    private static bool ValidValue(ExtensionValue value) =>
        Bounded(value.Name, 256) &&
        Bounded(value.SerializedValue, MaximumValueCharacters) &&
        Bounded(value.Reason, 2_048) &&
        value.Evidence is null or { Length: <= 2_048 } &&
        (value.Confidence is null ||
         double.IsFinite(value.Confidence.Value) &&
         value.Confidence.Value is >= 0 and <= 1) &&
        Enum.IsDefined(value.Kind) &&
        Enum.IsDefined(value.Derivation);

    private static bool Bounded(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum &&
        !value.Any(character => char.IsControl(character) && character is not '\t');

    private static string SafeCode(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "plugin.failure"
            : SafeText(value, 128);

    private static string SafeText(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value)
            ? "Plugin operation completed."
            : new(value
                .Where(character => !char.IsControl(character) || character is '\t')
                .Take(maximum)
                .ToArray());

    private static IReadOnlyList<string> SafeWarnings(IReadOnlyList<string>? warnings) =>
        Array.AsReadOnly((warnings ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => SafeText(value, 1_024))
            .Take(64)
            .ToArray());
}

/// <summary>
/// Resolves exact plugin recipe fields and converts validated SDK provenance into workflow provenance.
/// </summary>
/// <remarks>
/// Missing fields remain explicit and prevent an unsafe recipe fallback.
/// Values are returned as data to the constrained template engine; this service
/// cannot apply the generated destination.
/// </remarks>
public sealed class PluginRecipeFieldService : IPluginRecipeFieldService
{
    private readonly IPluginExtensionHost _host;
    private readonly IPluginContributionRegistry _registry;
    private readonly IPluginDiagnostics _diagnostics;

    public PluginRecipeFieldService(
        IPluginExtensionHost host,
        IPluginContributionRegistry registry,
        IPluginDiagnostics diagnostics)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public async Task<IReadOnlyList<PluginRecipeFieldValue>> ResolveAsync(
        IReadOnlyList<PluginContributionReference> references,
        ResultFile file,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(file);
        var values = new List<PluginRecipeFieldValue>();
        foreach (var reference in references
                     .Where(value => value.ExtensionPoint == ExtensionPointKind.RecipeFieldProvider)
                     .OrderBy(value => value.PluginId, StringComparer.Ordinal)
                     .ThenBy(value => value.ContributionId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var registration = _registry.Find(
                reference.PluginId,
                reference.ContributionId,
                ExtensionPointKind.RecipeFieldProvider);
            if (registration is null ||
                reference.PluginVersion is not null &&
                !string.Equals(
                    reference.PluginVersion,
                    registration.PluginVersion,
                    StringComparison.Ordinal))
            {
                if (reference.Required)
                {
                    _diagnostics.Record(
                        PluginDiagnosticKind.RecipeFieldResolution,
                        reference.PluginId,
                        $"Required recipe-field contribution \"{reference.ContributionId}\" is unavailable.");
                }

                continue;
            }

            var fieldName = FieldName(reference.PluginId, reference.ContributionId);
            var result = await _host.ResolveRecipeFieldAsync(
                reference.PluginId,
                reference.ContributionId,
                new RecipeFieldRequest(
                    ToFile(file),
                    fieldName,
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    null),
                cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded || result.Value?.Field is not { } field)
            {
                continue;
            }

            values.Add(new PluginRecipeFieldValue(
                fieldName,
                field.SerializedValue,
                field.Kind,
                registration.PluginId,
                registration.PluginVersion,
                registration.ContributionId,
                field.Derivation,
                field.Reason,
                field.Evidence,
                field.Confidence));
        }

        return Array.AsReadOnly(values.ToArray());
    }

    public static string FieldName(string pluginId, string contributionId) =>
        $"plugin.{pluginId}.{contributionId}";

    private static PluginFileReference ToFile(ResultFile file) =>
        new(
            file.Id,
            file.FullPath,
            file.SizeInBytes ?? 0,
            file.LastWriteTimeUtc ?? DateTimeOffset.UnixEpoch,
            file.NormalizedExtension);
}
