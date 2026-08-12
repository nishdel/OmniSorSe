using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using OpenSorSe.Application.Features;
using OpenSorSe.Application.Models;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application.AI;

/// <summary>
/// Coordinates capability gates, bounded prompts, optional provider requests, complete response validation, and local review history.
/// </summary>
public sealed class AiSuggestionService : IAiSuggestionService
{
    private const string ProviderName = "Ollama";
    private readonly IDecisionHistoryStore _decisionHistoryStore;
    private readonly ILogger _logger;
    private readonly IAiPromptBuilder _promptBuilder;
    private readonly IAiSuggestionProvider _provider;
    private readonly IAiResponseParser _responseParser;
    private readonly IAiRequestDiagnosticsStore? _requestDiagnosticsStore;
    private readonly IAiDiagnosticsCollector? _diagnosticsCollector;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the coordinator with default application-owned prompt and response components.</summary>
    public AiSuggestionService(
        IAiSuggestionProvider provider,
        IDecisionHistoryStore decisionHistoryStore,
        ILoggingService loggingService,
        TimeProvider? timeProvider = null,
        IAiRequestDiagnosticsStore? requestDiagnosticsStore = null,
        IAiDiagnosticsCollector? diagnosticsCollector = null)
        : this(provider, decisionHistoryStore, new AiPromptBuilder(), new AiResponseParser(), loggingService, timeProvider, requestDiagnosticsStore, diagnosticsCollector)
    {
    }

    /// <summary>Initializes the coordinator with explicit testable prompt and response components.</summary>
    public AiSuggestionService(
        IAiSuggestionProvider provider,
        IDecisionHistoryStore decisionHistoryStore,
        IAiPromptBuilder promptBuilder,
        IAiResponseParser responseParser,
        ILoggingService loggingService,
        TimeProvider? timeProvider = null,
        IAiRequestDiagnosticsStore? requestDiagnosticsStore = null,
        IAiDiagnosticsCollector? diagnosticsCollector = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _decisionHistoryStore = decisionHistoryStore ?? throw new ArgumentNullException(nameof(decisionHistoryStore));
        _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
        _responseParser = responseParser ?? throw new ArgumentNullException(nameof(responseParser));
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService))).CreateLogger(nameof(AiSuggestionService));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _requestDiagnosticsStore = requestDiagnosticsStore;
        _diagnosticsCollector = AiDiagnosticsIsolation.Protect(diagnosticsCollector);
    }

    /// <inheritdoc />
    public Task<AiConnectionResult> TestConnectionAsync(ApplicationSettings settings, CancellationToken cancellationToken) =>
        GetConnectionAsync(settings, discoverModels: false, cancellationToken);

    /// <inheritdoc />
    public Task<AiConnectionResult> DiscoverModelsAsync(ApplicationSettings settings, CancellationToken cancellationToken) =>
        GetConnectionAsync(settings, discoverModels: true, cancellationToken);

    /// <inheritdoc />
    public async Task<AiFileRenameResult> GenerateFileRenameAsync(
        AiFileRenameRequest request,
        AiSettings settings,
        CancellationToken cancellationToken) =>
        await GenerateFileRenameAsync(request, settings, null, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<AiFileRenameResult> GenerateFileRenameAsync(
        AiFileRenameRequest request,
        AiSettings settings,
        IProgress<AiRequestProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var diagnostic = new RequestDiagnosticScope(
            _requestDiagnosticsStore,
            settings,
            AiSuggestionKind.FileRename,
            request.File is null ? 0 : 1,
            progress,
            _timeProvider,
            _diagnosticsCollector);
        diagnostic.Report(AiRequestStage.CheckingSettings, "Checking AI settings and rename capability.");
        if (!TryValidateReadySettings(settings, AiCapability.FileRenameSuggestions, out var state, out var message))
        {
            diagnostic.Complete(message, null, "Not started", [message]);
            return new AiFileRenameResult(state, message, null);
        }

        if (!IsValidRenameContext(request))
        {
            const string invalidContext = "Select one valid known result file before requesting a rename suggestion.";
            diagnostic.Complete(invalidContext, null, "Context rejected", [invalidContext]);
            return new AiFileRenameResult(
                AiAvailabilityState.InvalidContext,
                invalidContext,
                null);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            diagnostic.Report(AiRequestStage.RequestCancelled, "The AI rename request was cancelled.");
            diagnostic.Complete("Cancelled", null, "Not validated", ["Caller cancellation was already requested."]);
            return new AiFileRenameResult(AiAvailabilityState.RequestCancelled, "The AI rename request was cancelled.", null);
        }

        diagnostic.Report(AiRequestStage.Connecting, "Connecting to Ollama using the configured endpoint.");
        var modelCheck = await GetSelectedModelAvailabilityAsync(settings, diagnostic.RequestId, cancellationToken).ConfigureAwait(false);
        diagnostic.SetConnection(modelCheck);
        diagnostic.Report(AiRequestStage.ValidatingModel, "Validating the exact selected Ollama model identifier.");
        if (modelCheck.State != AiAvailabilityState.ModelSelected)
        {
            diagnostic.Report(modelCheck.State == AiAvailabilityState.RequestCancelled ? AiRequestStage.RequestCancelled : AiRequestStage.RequestFailed, modelCheck.Message);
            diagnostic.Complete(modelCheck.Message, null, "Not validated", [modelCheck.Message]);
            return new AiFileRenameResult(modelCheck.State, modelCheck.Message, null);
        }

        diagnostic.Report(AiRequestStage.PreparingMetadata, "Preparing bounded filename metadata.");
        var preferences = await LoadPreferencesAsync(settings.PreferenceAdaptationEnabled, cancellationToken).ConfigureAwait(false);
        var prompt = _promptBuilder.BuildFileRenamePrompt(request, preferences);
        diagnostic.SetPrompt(prompt);
        var providerResult = await GenerateSafelyAsync(
            new AiProviderGenerationRequest(
                AiSuggestionKind.FileRename,
                settings.Endpoint,
                settings.SelectedModel!,
                prompt.Prompt,
                TimeSpan.FromSeconds(settings.RequestTimeoutSeconds))
            {
                SystemPrompt = prompt.SystemPrompt,
                Progress = diagnostic,
                DiagnosticRequestId = diagnostic.RequestId,
            },
            cancellationToken).ConfigureAwait(false);
        diagnostic.SetProviderResult(providerResult);
        if (!providerResult.IsSuccess)
        {
            var terminalStage = providerResult.FailureKind switch
            {
                AiProviderFailureKind.Cancelled => AiRequestStage.RequestCancelled,
                AiProviderFailureKind.Timeout => AiRequestStage.RequestTimedOut,
                _ => AiRequestStage.RequestFailed,
            };
            diagnostic.Report(terminalStage, providerResult.Message);
            diagnostic.Complete(providerResult.Message, providerResult, "Not validated", [providerResult.Message]);
            return new AiFileRenameResult(MapFailure(providerResult.FailureKind), providerResult.Message, null, prompt.WasInputBounded);
        }

        diagnostic.Report(AiRequestStage.ValidatingSuggestion, "Validating the complete untrusted rename response.");
        var parsed = _responseParser.ParseFileRename(providerResult.StructuredJson!, request, prompt.SourceMappings);
        if (parsed.CanRepair)
        {
            var repair = await TryRepairAsync(
                prompt,
                settings,
                providerResult.StructuredJson!,
                parsed.Message,
                response => _responseParser.ParseFileRename(response, request, prompt.SourceMappings),
                diagnostic,
                cancellationToken).ConfigureAwait(false);
            if (repair.ProviderResult is not null)
            {
                providerResult = repair.ProviderResult;
                diagnostic.SetProviderResult(providerResult);
            }

            if (repair.ParsedResult is not null)
            {
                parsed = repair.ParsedResult;
            }

            if (!providerResult.IsSuccess)
            {
                diagnostic.Complete(providerResult.Message, providerResult, "Not validated", [providerResult.Message]);
                return new AiFileRenameResult(
                    MapFailure(providerResult.FailureKind),
                    providerResult.Message,
                    null,
                    prompt.WasInputBounded);
            }
        }

        if (!parsed.IsValid)
        {
            _logger.LogWarning("An AI file-rename response was rejected during deterministic validation.");
            diagnostic.Report(AiRequestStage.RequestFailed, "The rename response failed validation.");
            diagnostic.Complete(parsed.Message, providerResult, "Rejected", [parsed.Message]);
            return new AiFileRenameResult(AiAvailabilityState.ResponseInvalid, parsed.Message, null, prompt.WasInputBounded);
        }

        if (parsed.IsNoSuggestion)
        {
            diagnostic.Complete(parsed.Message, providerResult, "Valid no-suggestion", Array.Empty<string>());
            return new AiFileRenameResult(AiAvailabilityState.NoSuggestion, parsed.Message, null, prompt.WasInputBounded);
        }

        var value = parsed.Value!;
        var suggestion = new AiFileRenameSuggestion(
            $"rename-suggestion:{Guid.NewGuid():N}",
            value.SourceFileId,
            value.SuggestedFileName,
            value.Reason,
            value.Confidence,
            ProviderName,
            settings.SelectedModel!,
            _timeProvider.GetUtcNow());
        var boundedSuffix = prompt.WasInputBounded ? " Some nearby-name context was deterministically bounded." : string.Empty;
        diagnostic.Report(AiRequestStage.SuggestionReady, "The validated rename suggestion is ready for review.");
        diagnostic.Complete("Suggestion ready", providerResult, "Accepted", Array.Empty<string>());
        return new AiFileRenameResult(
            AiAvailabilityState.ModelSelected,
            $"AI-generated rename suggestion available for review. It is unverified and no file was changed.{boundedSuffix}",
            suggestion,
            prompt.WasInputBounded);
    }

    /// <inheritdoc />
    public async Task<AiFolderStructureResult> GenerateFolderStructureAsync(
        AiFolderStructureRequest request,
        AiSettings settings,
        CancellationToken cancellationToken) =>
        await GenerateFolderStructureAsync(request, settings, null, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<AiFolderStructureResult> GenerateFolderStructureAsync(
        AiFolderStructureRequest request,
        AiSettings settings,
        IProgress<AiRequestProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var diagnostic = new RequestDiagnosticScope(
            _requestDiagnosticsStore,
            settings,
            AiSuggestionKind.FolderStructure,
            request.Files.Count,
            progress,
            _timeProvider,
            _diagnosticsCollector);
        diagnostic.Report(AiRequestStage.CheckingSettings, "Checking AI settings and folder-structure capability.");
        if (!TryValidateReadySettings(settings, AiCapability.FolderStructureSuggestions, out var state, out var message))
        {
            diagnostic.Complete(message, null, "Not started", [message]);
            return new AiFolderStructureResult(state, message, null);
        }

        if (!IsValidFolderContext(request))
        {
            const string invalidContext = "Select at least one valid known result file before requesting a folder-structure suggestion.";
            diagnostic.Complete(invalidContext, null, "Context rejected", [invalidContext]);
            return new AiFolderStructureResult(
                AiAvailabilityState.InvalidContext,
                invalidContext,
                null);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            diagnostic.Report(AiRequestStage.RequestCancelled, "The AI folder-structure request was cancelled.");
            diagnostic.Complete("Cancelled", null, "Not validated", ["Caller cancellation was already requested."]);
            return new AiFolderStructureResult(AiAvailabilityState.RequestCancelled, "The AI folder-structure request was cancelled.", null);
        }

        if (request.Files.Count > AiPromptLimits.MaximumFolderStructureFiles)
        {
            var boundedContext =
                $"Folder suggestions support at most {AiPromptLimits.MaximumFolderStructureFiles} files per request. " +
                $"{request.Files.Count} files are in the current selection; none were sent to the model. " +
                $"Reduce the selection to {AiPromptLimits.MaximumFolderStructureFiles} or fewer and try again.";
            diagnostic.Complete(boundedContext, null, "Context rejected", [boundedContext]);
            return new AiFolderStructureResult(
                AiAvailabilityState.InvalidContext,
                boundedContext,
                null);
        }

        diagnostic.Report(AiRequestStage.Connecting, "Connecting to Ollama using the configured endpoint.");
        var modelCheck = await GetSelectedModelAvailabilityAsync(settings, diagnostic.RequestId, cancellationToken).ConfigureAwait(false);
        diagnostic.SetConnection(modelCheck);
        diagnostic.Report(AiRequestStage.ValidatingModel, "Validating the exact selected Ollama model identifier.");
        if (modelCheck.State != AiAvailabilityState.ModelSelected)
        {
            diagnostic.Report(modelCheck.State == AiAvailabilityState.RequestCancelled ? AiRequestStage.RequestCancelled : AiRequestStage.RequestFailed, modelCheck.Message);
            diagnostic.Complete(modelCheck.Message, null, "Not validated", [modelCheck.Message]);
            return new AiFolderStructureResult(modelCheck.State, modelCheck.Message, null);
        }

        diagnostic.Report(AiRequestStage.PreparingMetadata, "Preparing the exact bounded filename list.");
        var preferences = await LoadPreferencesAsync(settings.PreferenceAdaptationEnabled, cancellationToken).ConfigureAwait(false);
        var prompt = _promptBuilder.BuildFolderStructurePrompt(request, preferences);
        diagnostic.SetPrompt(prompt);
        var providerResult = await GenerateSafelyAsync(
            new AiProviderGenerationRequest(
                AiSuggestionKind.FolderStructure,
                settings.Endpoint,
                settings.SelectedModel!,
                prompt.Prompt,
                TimeSpan.FromSeconds(settings.RequestTimeoutSeconds))
            {
                SystemPrompt = prompt.SystemPrompt,
                Progress = diagnostic,
                DiagnosticRequestId = diagnostic.RequestId,
            },
            cancellationToken).ConfigureAwait(false);
        diagnostic.SetProviderResult(providerResult);
        if (!providerResult.IsSuccess)
        {
            var terminalStage = providerResult.FailureKind switch
            {
                AiProviderFailureKind.Cancelled => AiRequestStage.RequestCancelled,
                AiProviderFailureKind.Timeout => AiRequestStage.RequestTimedOut,
                _ => AiRequestStage.RequestFailed,
            };
            diagnostic.Report(terminalStage, providerResult.Message);
            diagnostic.Complete(providerResult.Message, providerResult, "Not validated", [providerResult.Message]);
            return new AiFolderStructureResult(MapFailure(providerResult.FailureKind), providerResult.Message, null, prompt.WasInputBounded);
        }

        var includedIds = prompt.IncludedSourceIds.ToHashSet(StringComparer.Ordinal);
        var includedFiles = Array.AsReadOnly(request.Files.Where(file => includedIds.Contains(file.Id)).ToArray());
        diagnostic.Report(AiRequestStage.ValidatingSuggestion, "Validating every folder and exact request-local assignment.");
        var parsed = _responseParser.ParseFolderStructure(
            providerResult.StructuredJson!,
            includedFiles,
            prompt.SourceMappings,
            prompt.AllowedFolderNames);
        if (parsed.CanRepair)
        {
            var repair = await TryRepairAsync(
                prompt,
                settings,
                providerResult.StructuredJson!,
                parsed.Message,
                response => _responseParser.ParseFolderStructure(
                    response,
                    includedFiles,
                    prompt.SourceMappings,
                    prompt.AllowedFolderNames),
                diagnostic,
                cancellationToken).ConfigureAwait(false);
            if (repair.ProviderResult is not null)
            {
                providerResult = repair.ProviderResult;
                diagnostic.SetProviderResult(providerResult);
            }

            if (repair.ParsedResult is not null)
            {
                parsed = repair.ParsedResult;
            }

            if (!providerResult.IsSuccess)
            {
                diagnostic.Complete(providerResult.Message, providerResult, "Not validated", [providerResult.Message]);
                return new AiFolderStructureResult(
                    MapFailure(providerResult.FailureKind),
                    providerResult.Message,
                    null,
                    prompt.WasInputBounded);
            }
        }

        if (!parsed.IsValid)
        {
            _logger.LogWarning("An AI folder-structure response was rejected during deterministic validation.");
            diagnostic.Report(AiRequestStage.RequestFailed, "The folder-structure response failed validation.");
            diagnostic.Complete(parsed.Message, providerResult, "Rejected", [parsed.Message]);
            return new AiFolderStructureResult(AiAvailabilityState.ResponseInvalid, parsed.Message, null, prompt.WasInputBounded);
        }

        if (parsed.IsNoSuggestion)
        {
            diagnostic.Complete(parsed.Message, providerResult, "Valid no-suggestion", Array.Empty<string>());
            return new AiFolderStructureResult(AiAvailabilityState.NoSuggestion, parsed.Message, null, prompt.WasInputBounded);
        }

        var value = parsed.Value!;
        var plan = new AiFolderStructurePlan(
            $"folder-plan:{Guid.NewGuid():N}",
            value.Folders,
            value.Items,
            value.Reason,
            ProviderName,
            settings.SelectedModel!,
            _timeProvider.GetUtcNow());
        var boundedSuffix = prompt.WasInputBounded ? " Allowed folder-name context was deterministically bounded." : string.Empty;
        diagnostic.Report(AiRequestStage.SuggestionReady, "The validated folder-structure suggestion is ready for review.");
        diagnostic.Complete("Suggestion ready", providerResult, "Accepted", Array.Empty<string>());
        return new AiFolderStructureResult(
            AiAvailabilityState.ModelSelected,
            $"AI-generated folder-structure suggestion available for review. All {prompt.IncludedInputCount} selected files were included exactly once. It is unverified and cannot create folders or move files.{boundedSuffix}",
            plan,
            prompt.WasInputBounded);
    }

    /// <inheritdoc />
    public async Task<AiDocumentInterpretationResult> GenerateDocumentInterpretationAsync(
        AiDocumentTextRequest request,
        AiSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var diagnostic = new RequestDiagnosticScope(
            _requestDiagnosticsStore,
            settings,
            AiSuggestionKind.DocumentTextInterpretation,
            1,
            null,
            _timeProvider,
            _diagnosticsCollector,
            request.RelatedDiagnosticSessionId);
        diagnostic.Report(AiRequestStage.CheckingSettings, "Checking AI and extracted-text capability settings.");
        if (!TryValidateReadySettings(
                settings,
                AiCapability.DocumentTextInterpretation,
                out var state,
                out var message))
        {
            diagnostic.Complete(message, null, "Not started", [message]);
            return new AiDocumentInterpretationResult(state, message, null);
        }

        if (!IsValidDocumentContext(request))
        {
            const string invalidContext = "Select one indexed document with bounded extracted text before requesting interpretation.";
            diagnostic.Complete(invalidContext, null, "Context rejected", [invalidContext]);
            return new AiDocumentInterpretationResult(
                AiAvailabilityState.InvalidContext,
                invalidContext,
                null);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            diagnostic.Report(AiRequestStage.RequestCancelled, "The AI document interpretation request was cancelled.");
            diagnostic.Complete(
                "Cancelled",
                null,
                "Not validated",
                ["Caller cancellation was already requested."]);
            return new AiDocumentInterpretationResult(
                AiAvailabilityState.RequestCancelled,
                "The AI document interpretation request was cancelled.",
                null);
        }

        diagnostic.Report(AiRequestStage.Connecting, "Connecting to the configured Ollama-compatible endpoint.");
        var modelCheck = await GetSelectedModelAvailabilityAsync(settings, diagnostic.RequestId, cancellationToken).ConfigureAwait(false);
        diagnostic.SetConnection(modelCheck);
        if (modelCheck.State != AiAvailabilityState.ModelSelected)
        {
            diagnostic.Complete(modelCheck.Message, null, "Not validated", [modelCheck.Message]);
            return new AiDocumentInterpretationResult(modelCheck.State, modelCheck.Message, null);
        }

        diagnostic.Report(AiRequestStage.PreparingMetadata, "Preparing bounded extracted text with provenance.");
        var prompt = _promptBuilder.BuildDocumentInterpretationPrompt(request);
        diagnostic.SetPrompt(prompt);
        var providerResult = await GenerateSafelyAsync(
            new AiProviderGenerationRequest(
                AiSuggestionKind.DocumentTextInterpretation,
                settings.Endpoint,
                settings.SelectedModel!,
                prompt.Prompt,
                TimeSpan.FromSeconds(settings.RequestTimeoutSeconds))
            {
                SystemPrompt = prompt.SystemPrompt,
                Progress = diagnostic,
                DiagnosticRequestId = diagnostic.RequestId,
            },
            cancellationToken).ConfigureAwait(false);
        diagnostic.SetProviderResult(providerResult);
        if (!providerResult.IsSuccess)
        {
            diagnostic.Complete(providerResult.Message, providerResult, "Not validated", [providerResult.Message]);
            return new AiDocumentInterpretationResult(
                MapFailure(providerResult.FailureKind),
                providerResult.Message,
                null,
                prompt.WasInputBounded);
        }

        diagnostic.Report(AiRequestStage.ValidatingSuggestion, "Validating the complete untrusted interpretation response.");
        var parsed = _responseParser.ParseDocumentInterpretation(
            providerResult.StructuredJson!,
            request,
            prompt.SourceMappings);
        if (parsed.CanRepair)
        {
            var repair = await TryRepairAsync(
                prompt,
                settings,
                providerResult.StructuredJson!,
                parsed.Message,
                response => _responseParser.ParseDocumentInterpretation(
                    response,
                    request,
                    prompt.SourceMappings),
                diagnostic,
                cancellationToken).ConfigureAwait(false);
            if (repair.ProviderResult is not null)
            {
                providerResult = repair.ProviderResult;
                diagnostic.SetProviderResult(providerResult);
            }

            if (repair.ParsedResult is not null)
            {
                parsed = repair.ParsedResult;
            }

            if (!providerResult.IsSuccess)
            {
                diagnostic.Complete(
                    providerResult.Message,
                    providerResult,
                    "Not validated",
                    [providerResult.Message]);
                return new AiDocumentInterpretationResult(
                    MapFailure(providerResult.FailureKind),
                    providerResult.Message,
                    null,
                    prompt.WasInputBounded);
            }
        }

        if (!parsed.IsValid)
        {
            _logger.LogWarning("An AI document interpretation response was rejected during deterministic validation.");
            diagnostic.Complete(parsed.Message, providerResult, "Rejected", [parsed.Message]);
            return new AiDocumentInterpretationResult(
                AiAvailabilityState.ResponseInvalid,
                parsed.Message,
                null,
                prompt.WasInputBounded);
        }

        if (parsed.IsNoSuggestion)
        {
            diagnostic.Complete(parsed.Message, providerResult, "Valid no-suggestion", []);
            return new AiDocumentInterpretationResult(
                AiAvailabilityState.NoSuggestion,
                parsed.Message,
                null,
                prompt.WasInputBounded);
        }

        var value = parsed.Value!;
        var suggestion = new AiDocumentInterpretationSuggestion(
            $"document-interpretation:{Guid.NewGuid():N}",
            value.SourceFileId,
            value.DocumentType,
            value.Title,
            value.Tags,
            value.Dates,
            value.Issuer,
            value.SuggestedFolder,
            value.Reason,
            value.Confidence,
            ProviderName,
            settings.SelectedModel!,
            _timeProvider.GetUtcNow());
        diagnostic.Report(AiRequestStage.SuggestionReady, "The unverified interpretation is ready for review.");
        diagnostic.Complete("Suggestion ready", providerResult, "Accepted", []);
        return new AiDocumentInterpretationResult(
            AiAvailabilityState.ModelSelected,
            "AI-generated document interpretation is available for review. It is unverified and no source file was changed.",
            suggestion,
            prompt.WasInputBounded);
    }

    /// <inheritdoc />
    public async Task<AiDecisionResult> RecordDecisionAsync(
        AiSuggestionDecision decision,
        AiSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var capability = decision.Kind switch
        {
            AiSuggestionDecisionKind.Rename => AiCapability.FileRenameSuggestions,
            AiSuggestionDecisionKind.FolderStructure => AiCapability.FolderStructureSuggestions,
            _ => (AiCapability?)null,
        };
        if (capability is null)
        {
            return new AiDecisionResult(AiAvailabilityState.InvalidContext, "That AI decision type is not supported by this OmniSorSe version.");
        }

        if (!TryValidateEnabled(settings, capability.Value, out var state, out var message))
        {
            return new AiDecisionResult(state, message);
        }

        if (string.IsNullOrWhiteSpace(decision.SuggestedValue) || decision.SuggestedValue.Length > 4096 ||
            string.IsNullOrWhiteSpace(decision.Provider) || string.IsNullOrWhiteSpace(decision.Model))
        {
            return new AiDecisionResult(AiAvailabilityState.InvalidContext, "The AI review decision is incomplete and was not saved.");
        }

        try
        {
            await _decisionHistoryStore.AppendAsync(decision, cancellationToken).ConfigureAwait(false);
            return new AiDecisionResult(AiAvailabilityState.ModelSelected, "The local review decision was saved. No file or folder was changed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AiDecisionResult(AiAvailabilityState.RequestCancelled, "Saving the local AI review decision was cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            _logger.LogWarning(exception, "The local AI review decision could not be saved.");
            return new AiDecisionResult(AiAvailabilityState.Unavailable, "The local AI review decision could not be saved. No file or folder was changed.");
        }
    }

    /// <inheritdoc />
    public async Task<AiDecisionResult> ResetDecisionHistoryAsync(ApplicationSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!FeatureAccess.IsEnabled(settings, FeatureRequirement.ForAdvancedAi()))
        {
            return new AiDecisionResult(
                settings.Ai.Enabled ? AiAvailabilityState.CapabilityDisabled : AiAvailabilityState.Disabled,
                "Enable AI and advanced features before resetting local AI review history.");
        }

        try
        {
            await _decisionHistoryStore.ClearAsync(cancellationToken).ConfigureAwait(false);
            return new AiDecisionResult(AiAvailabilityState.ModelSelected, "Local AI review history was reset. No scanned file or other OmniSorSe store changed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AiDecisionResult(AiAvailabilityState.RequestCancelled, "Resetting local AI review history was cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            _logger.LogWarning(exception, "Local AI review history could not be reset.");
            return new AiDecisionResult(AiAvailabilityState.Unavailable, "Local AI review history could not be reset. Existing application data was preserved.");
        }
    }

    private async Task<AiConnectionResult> GetConnectionAsync(
        ApplicationSettings settings,
        bool discoverModels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Ai.Enabled)
        {
            return new AiConnectionResult(
                AiAvailabilityState.Disabled,
                "Enable AI features before contacting Ollama.",
                Array.Empty<AiModel>());
        }

        try
        {
            settings.Validate();
        }
        catch (ConfigurationValidationException)
        {
            return new AiConnectionResult(AiAvailabilityState.Unavailable, "AI settings are invalid.", Array.Empty<AiModel>());
        }

        var diagnosticsStarted = _timeProvider.GetUtcNow();
        var diagnosticId = _diagnosticsCollector?.IsEnabled == true
            ? _diagnosticsCollector.Begin(
                discoverModels ? AiSuggestionKind.ModelDiscovery : AiSuggestionKind.ConnectionTest,
                settings.Ai.SelectedModel ?? string.Empty,
                settings.Ai.Endpoint)
            : null;
        _diagnosticsCollector?.ReportStage(diagnosticId, "Connecting to Ollama", AiDiagnosticState.Active, TimeSpan.Zero);
        AiConnectionResult connection;
        try
        {
            connection = discoverModels
                ? await _provider.GetConnectionAsync(settings.Ai, diagnosticId, cancellationToken).ConfigureAwait(false)
                : await _provider.CheckConnectionAsync(settings.Ai, diagnosticId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _diagnosticsCollector?.Complete(diagnosticId, AiDiagnosticState.Cancelled, true, _timeProvider.GetUtcNow() - diagnosticsStarted, "The connection operation was cancelled.");
            return new AiConnectionResult(AiAvailabilityState.RequestCancelled, "The Ollama connection test was cancelled.", Array.Empty<AiModel>());
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or InvalidDataException or NotSupportedException)
        {
            _logger.LogWarning(exception, "The AI provider failed during a connection test.");
            _diagnosticsCollector?.Complete(diagnosticId, AiDiagnosticState.Failed, false, _timeProvider.GetUtcNow() - diagnosticsStarted, $"{exception.GetType().Name}: {exception.Message}");
            return new AiConnectionResult(AiAvailabilityState.Unavailable, "Ollama is unavailable at the configured endpoint.", Array.Empty<AiModel>());
        }

        _diagnosticsCollector?.Complete(
            diagnosticId,
            connection.State is AiAvailabilityState.Connected or AiAvailabilityState.ModelSelected or AiAvailabilityState.NoModelsAvailable
                ? AiDiagnosticState.Succeeded
                : AiDiagnosticState.Failed,
            connection.State == AiAvailabilityState.RequestCancelled,
            _timeProvider.GetUtcNow() - diagnosticsStarted,
            connection.State is AiAvailabilityState.Connected or AiAvailabilityState.ModelSelected or AiAvailabilityState.NoModelsAvailable ? null : connection.Message);

        if (connection.State is not (AiAvailabilityState.Connected or AiAvailabilityState.ModelSelected or AiAvailabilityState.NoModelsAvailable))
        {
            return connection;
        }

        if (!discoverModels)
        {
            return connection;
        }

        if (connection.Models.Count == 0)
        {
            return connection with { State = AiAvailabilityState.NoModelsAvailable, Message = "Ollama is connected, but no installed models were found." };
        }

        if (!string.IsNullOrWhiteSpace(settings.Ai.SelectedModel) &&
            connection.Models.Any(model => string.Equals(model.Id, settings.Ai.SelectedModel, StringComparison.Ordinal)))
        {
            return connection with { State = AiAvailabilityState.ModelSelected, Message = $"Ollama is connected and '{settings.Ai.SelectedModel}' is selected." };
        }

        return connection with { State = AiAvailabilityState.Connected, Message = "Ollama is connected. Select one discovered model before requesting suggestions." };
    }

    private async Task<AiConnectionResult> GetSelectedModelAvailabilityAsync(
        AiSettings settings,
        string? diagnosticRequestId,
        CancellationToken cancellationToken)
    {
        AiConnectionResult connection;
        try
        {
            connection = await _provider.GetConnectionAsync(settings, diagnosticRequestId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AiConnectionResult(AiAvailabilityState.RequestCancelled, "The AI request was cancelled while checking the selected model.", Array.Empty<AiModel>());
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or InvalidDataException or NotSupportedException)
        {
            _logger.LogWarning(exception, "The AI provider failed while validating the selected model.");
            return new AiConnectionResult(AiAvailabilityState.Unavailable, "Ollama could not be reached while validating the selected model.", Array.Empty<AiModel>());
        }

        if (connection.State is not (AiAvailabilityState.Connected or AiAvailabilityState.ModelSelected))
        {
            return connection;
        }

        return connection.Models.Any(model => string.Equals(model.Id, settings.SelectedModel, StringComparison.Ordinal))
            ? connection with
            {
                State = AiAvailabilityState.ModelSelected,
                Message = $"Selected model '{settings.SelectedModel}' is installed.",
            }
            : connection with
            {
                State = AiAvailabilityState.ModelUnavailable,
                Message = $"The selected model '{settings.SelectedModel}' is not installed. Refresh models in Settings and select an available model.",
            };
    }

    private async Task<AiProviderGenerationResult> GenerateSafelyAsync(
        AiProviderGenerationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _provider.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AiProviderGenerationResult(null, AiProviderFailureKind.Cancelled, "The AI suggestion request was cancelled.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or InvalidDataException or NotSupportedException)
        {
            _logger.LogWarning(exception, "The AI provider failed before a response could be validated.");
            return new AiProviderGenerationResult(null, AiProviderFailureKind.Unavailable, "The AI provider failed safely. Check Ollama and the configured model.");
        }
    }

    private async Task<RepairAttempt<T>> TryRepairAsync<T>(
        AiPromptPackage originalPrompt,
        AiSettings settings,
        string priorResponse,
        string validationError,
        Func<string, AiResponseParseResult<T>> parse,
        RequestDiagnosticScope diagnostic,
        CancellationToken cancellationToken)
        where T : class
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new RepairAttempt<T>(
                new AiProviderGenerationResult(
                    null,
                    AiProviderFailureKind.Cancelled,
                    "The AI suggestion repair request was cancelled."),
                null);
        }

        var repairPrompt = _promptBuilder.BuildRepairPrompt(
            originalPrompt,
            priorResponse,
            validationError);
        var repairDiagnosticId = diagnostic.BeginRepairAttempt(
            repairPrompt,
            priorResponse,
            validationError);
        var providerResult = await GenerateSafelyAsync(
            new AiProviderGenerationRequest(
                originalPrompt.Kind,
                settings.Endpoint,
                settings.SelectedModel!,
                repairPrompt.Prompt,
                TimeSpan.FromSeconds(settings.RequestTimeoutSeconds))
            {
                SystemPrompt = repairPrompt.SystemPrompt,
                Progress = diagnostic,
                DiagnosticRequestId = repairDiagnosticId,
            },
            cancellationToken).ConfigureAwait(false);

        if (!providerResult.IsSuccess)
        {
            diagnostic.CompleteRepairAttempt<T>(
                repairDiagnosticId,
                repairPrompt,
                providerResult,
                null,
                providerResult.Message);
            return new RepairAttempt<T>(providerResult, null);
        }

        var parsed = parse(providerResult.StructuredJson!);
        diagnostic.CompleteRepairAttempt(
            repairDiagnosticId,
            repairPrompt,
            providerResult,
            parsed,
            parsed.IsValid ? null : parsed.Message);
        return new RepairAttempt<T>(providerResult, parsed);
    }

    private async Task<AiPreferenceSummary> LoadPreferencesAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (!enabled)
        {
            return EmptyPreferences();
        }

        try
        {
            return AiPreferenceAggregator.Build(await _decisionHistoryStore.LoadAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Local AI decision history could not be loaded. Suggestions will continue without preference context.");
            return EmptyPreferences();
        }
    }

    private static AiPreferenceSummary EmptyPreferences() =>
        new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    private static bool TryValidateReadySettings(
        AiSettings settings,
        AiCapability capability,
        out AiAvailabilityState state,
        out string message)
    {
        if (!TryValidateEnabled(settings, capability, out state, out message))
        {
            return false;
        }

        try
        {
            settings.Validate();
        }
        catch (ConfigurationValidationException)
        {
            state = AiAvailabilityState.Unavailable;
            message = "AI settings are invalid. Check the endpoint and request timeout.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.SelectedModel))
        {
            state = AiAvailabilityState.NoModelsAvailable;
            message = "Select an installed Ollama model in Settings before requesting suggestions.";
            return false;
        }

        state = AiAvailabilityState.ModelSelected;
        message = string.Empty;
        return true;
    }

    private static bool TryValidateEnabled(
        AiSettings settings,
        AiCapability capability,
        out AiAvailabilityState state,
        out string message)
    {
        if (settings is null || !settings.Enabled)
        {
            state = AiAvailabilityState.Disabled;
            message = "AI features are disabled in Settings.";
            return false;
        }

        if (!settings.IsCapabilityEnabled(capability))
        {
            state = AiAvailabilityState.CapabilityDisabled;
            message = capability switch
            {
                AiCapability.FileRenameSuggestions => "File rename suggestions are disabled in Settings.",
                AiCapability.FolderStructureSuggestions => "Folder structure suggestions are disabled in Settings.",
                AiCapability.DocumentTextInterpretation => "AI analysis of extracted document text is disabled in Settings.",
                _ => "The requested AI capability is disabled in Settings.",
            };
            return false;
        }

        state = AiAvailabilityState.ModelSelected;
        message = string.Empty;
        return true;
    }

    private static bool IsValidRenameContext(AiFileRenameRequest? request) =>
        request?.File is { } file &&
        request.SiblingFileNames is not null &&
        !string.IsNullOrWhiteSpace(file.Id) && file.Id.Length <= 128 &&
        !string.IsNullOrWhiteSpace(file.DisplayFileName) && file.DisplayFileName.Length <= 255 &&
        IsSafeOriginalExtension(file.DisplayFileName, file.NormalizedExtension);

    private static bool IsSafeOriginalExtension(string displayFileName, string? normalizedExtension)
    {
        if (string.IsNullOrWhiteSpace(normalizedExtension) ||
            normalizedExtension.Length > 32 ||
            normalizedExtension[0] != '.' ||
            normalizedExtension.Any(character =>
                char.IsControl(character) ||
                character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*'))
        {
            return false;
        }

        return string.Equals(
            Path.GetExtension(displayFileName),
            normalizedExtension,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidFolderContext(AiFolderStructureRequest? request)
    {
        if (request?.Files is null || request.ExistingFolderNames is null || request.Files.Count == 0)
        {
            return false;
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in request.Files)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.Id) || file.Id.Length > 128 ||
                string.IsNullOrWhiteSpace(file.DisplayFileName) || file.DisplayFileName.Length > 255 ||
                !identities.Add(file.Id))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidDocumentContext(AiDocumentTextRequest? request) =>
        request is not null &&
        !string.IsNullOrWhiteSpace(request.SourceFileId) &&
        request.SourceFileId.Length <= 128 &&
        !string.IsNullOrWhiteSpace(request.DisplayFileName) &&
        request.DisplayFileName.Length <= 255 &&
        request.Pages is not null &&
        (!string.IsNullOrWhiteSpace(request.NativeText) ||
         !string.IsNullOrWhiteSpace(request.OcrText) ||
         request.Pages.Any(page => page is not null && !string.IsNullOrWhiteSpace(page.Text)));

    private static AiAvailabilityState MapFailure(AiProviderFailureKind failure) => failure switch
    {
        AiProviderFailureKind.Cancelled => AiAvailabilityState.RequestCancelled,
        AiProviderFailureKind.ModelUnavailable => AiAvailabilityState.ModelUnavailable,
        AiProviderFailureKind.InvalidResponse or AiProviderFailureKind.UnsupportedResponse => AiAvailabilityState.ResponseInvalid,
        _ => AiAvailabilityState.Unavailable,
    };

    private sealed record RepairAttempt<T>(
        AiProviderGenerationResult? ProviderResult,
        AiResponseParseResult<T>? ParsedResult)
        where T : class;

    private sealed class RequestDiagnosticScope : IProgress<AiRequestProgress>
    {
        private readonly IAiRequestDiagnosticsStore? _store;
        private readonly IProgress<AiRequestProgress>? _outerProgress;
        private readonly TimeProvider _timeProvider;
        private readonly AiSettings _settings;
        private readonly AiSuggestionKind _kind;
        private readonly DateTimeOffset _startedAtUtc;
        private readonly List<AiRequestStageEntry> _stages = [];
        private readonly int _totalInputCount;
        private readonly IAiDiagnosticsCollector? _collector;
        private AiPromptPackage? _prompt;
        private AiConnectionResult? _connection;
        private AiProviderGenerationResult? _providerResult;
        private bool _completed;

        public RequestDiagnosticScope(
            IAiRequestDiagnosticsStore? store,
            AiSettings settings,
            AiSuggestionKind kind,
            int totalInputCount,
            IProgress<AiRequestProgress>? outerProgress,
            TimeProvider timeProvider,
            IAiDiagnosticsCollector? collector,
            string? relatedSessionId = null)
        {
            _store = store;
            _settings = settings;
            _kind = kind;
            _totalInputCount = totalInputCount;
            _outerProgress = outerProgress;
            _timeProvider = timeProvider;
            _startedAtUtc = timeProvider.GetUtcNow();
            _collector = collector;
            RequestId = collector?.IsEnabled == true
                ? collector.Begin(kind, settings.SelectedModel ?? string.Empty, settings.Endpoint)
                : null;
            collector?.Relate(RequestId, relatedSessionId);
        }

        public string? RequestId { get; }

        public void Report(AiRequestProgress value)
        {
            ArgumentNullException.ThrowIfNull(value);
            _stages.Add(new AiRequestStageEntry(value.Stage, _timeProvider.GetUtcNow(), Bound(value.Message, 500)));
            _collector?.ReportStage(
                RequestId,
                StageName(value.Stage),
                StageState(value.Stage),
                value.Elapsed,
                value.Stage is AiRequestStage.RequestFailed or AiRequestStage.RequestTimedOut ? value.Message : null);
            _outerProgress?.Report(value);
        }

        public void Report(AiRequestStage stage, string message)
        {
            var elapsed = _timeProvider.GetUtcNow() - _startedAtUtc;
            Report(new AiRequestProgress(stage, message, elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed));
        }

        public void SetPrompt(AiPromptPackage prompt)
        {
            _prompt = prompt;
            _collector?.SetContract(
                RequestId,
                prompt.TaskId,
                prompt.PromptVersion,
                AiStructuredOutputContracts.GetSchemaSha256(prompt.Kind));
            _collector?.ReportStage(RequestId, "Building system prompt", AiDiagnosticState.Succeeded, Elapsed());
            _collector?.Capture(RequestId, AiDiagnosticContentKind.SystemPrompt, prompt.SystemPrompt);
            _collector?.ReportStage(RequestId, "Building user prompt", AiDiagnosticState.Succeeded, Elapsed());
            _collector?.Capture(RequestId, AiDiagnosticContentKind.UserPrompt, prompt.Prompt);
        }

        public void SetConnection(AiConnectionResult connection) => _connection = connection;

        public void SetProviderResult(AiProviderGenerationResult result) => _providerResult = result;

        public string? BeginRepairAttempt(
            AiPromptPackage repairPrompt,
            string priorResponse,
            string validationError)
        {
            _collector?.SetValidation(
                RequestId,
                priorResponse,
                AiDiagnosticValidationInspector.Inspect(priorResponse, repairPrompt.TaskId).Checks,
                [validationError]);
            _collector?.ReportStage(
                RequestId,
                "Structured-output repair",
                AiDiagnosticState.Active,
                Elapsed());
            var repairRequestId = _collector?.IsEnabled == true
                ? _collector.Begin(
                    _kind,
                    _settings.SelectedModel ?? string.Empty,
                    _settings.Endpoint,
                    retryAttempt: 2)
                : null;
            _collector?.Relate(RequestId, repairRequestId);
            _collector?.SetContract(
                repairRequestId,
                repairPrompt.TaskId,
                repairPrompt.PromptVersion,
                AiStructuredOutputContracts.GetSchemaSha256(repairPrompt.Kind));
            _collector?.Capture(
                repairRequestId,
                AiDiagnosticContentKind.SystemPrompt,
                repairPrompt.SystemPrompt);
            _collector?.Capture(
                repairRequestId,
                AiDiagnosticContentKind.UserPrompt,
                repairPrompt.Prompt);
            _collector?.SetValidation(
                repairRequestId,
                priorResponse,
                AiDiagnosticValidationInspector.Inspect(priorResponse, repairPrompt.TaskId).Checks,
                [validationError]);
            return repairRequestId;
        }

        public void CompleteRepairAttempt<T>(
            string? repairRequestId,
            AiPromptPackage repairPrompt,
            AiProviderGenerationResult providerResult,
            AiResponseParseResult<T>? parsed,
            string? error)
            where T : class
        {
            var inspected = AiDiagnosticValidationInspector.Inspect(
                providerResult.StructuredJson,
                repairPrompt.TaskId);
            var issues = string.IsNullOrWhiteSpace(error)
                ? inspected.Checks.Where(check => !check.Passed).Select(check => check.Message).ToArray()
                : inspected.Checks
                    .Where(check => !check.Passed)
                    .Select(check => check.Message)
                    .Append(error)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            _collector?.SetValidation(
                repairRequestId,
                inspected.ParsedJson,
                inspected.Checks,
                issues);
            var state = providerResult.FailureKind switch
            {
                AiProviderFailureKind.Cancelled => AiDiagnosticState.Cancelled,
                AiProviderFailureKind.None when parsed?.IsValid == true => AiDiagnosticState.Succeeded,
                AiProviderFailureKind.None => AiDiagnosticState.Rejected,
                _ => AiDiagnosticState.Failed,
            };
            _collector?.Complete(
                repairRequestId,
                state,
                state == AiDiagnosticState.Cancelled,
                providerResult.Diagnostics?.Elapsed ?? Elapsed(),
                error);
            _collector?.ReportStage(
                RequestId,
                "Structured-output repair",
                state,
                Elapsed(),
                error);
        }

        public void Complete(
            string outcome,
            AiProviderGenerationResult? providerResult,
            string validationOutcome,
            IReadOnlyList<string> validationIssues)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _providerResult = providerResult ?? _providerResult;
            var inspected = AiDiagnosticValidationInspector.Inspect(
                _providerResult?.StructuredJson,
                _prompt?.TaskId ?? string.Empty);
            var detailedIssues = validationIssues
                .Concat(inspected.Checks.Where(check => !check.Passed).Select(check => check.Message))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            _collector?.SetValidation(RequestId, inspected.ParsedJson, inspected.Checks, detailedIssues);
            var finalState = _providerResult?.FailureKind switch
            {
                AiProviderFailureKind.Cancelled => AiDiagnosticState.Cancelled,
                AiProviderFailureKind.None when validationOutcome is "Accepted" or "Valid no-suggestion" => AiDiagnosticState.Succeeded,
                AiProviderFailureKind.None => AiDiagnosticState.Rejected,
                null when validationOutcome == "Context rejected" => AiDiagnosticState.Rejected,
                _ => AiDiagnosticState.Failed,
            };
            _collector?.Complete(RequestId, finalState, finalState == AiDiagnosticState.Cancelled, Elapsed(), detailedIssues.FirstOrDefault());
            if (!_settings.RequestDiagnosticsEnabled || _store?.IsEnabled != true)
            {
                return;
            }

            var completedAt = _timeProvider.GetUtcNow();
            var elapsed = _providerResult?.Diagnostics?.Elapsed ?? completedAt - _startedAtUtc;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            var response = _providerResult?.Diagnostics?.RawResponse ?? _providerResult?.StructuredJson ?? string.Empty;
            var normalizedEndpoint = _providerResult?.Diagnostics?.NormalizedEndpoint
                ?? _connection?.NormalizedEndpoint
                ?? _settings.Endpoint.Trim();
            var promptText = _prompt?.Prompt ?? string.Empty;
            var includedCount = _prompt?.IncludedInputCount ?? 0;
            var totalCount = _prompt?.TotalInputCount ?? _totalInputCount;
            _store.Record(new AiRequestDiagnostic(
                $"ai-request:{Guid.NewGuid():N}",
                _startedAtUtc,
                _kind,
                normalizedEndpoint,
                _settings.SelectedModel ?? string.Empty,
                _settings.RequestTimeoutSeconds,
                Array.AsReadOnly(_stages.Take(30).ToArray()),
                _startedAtUtc,
                completedAt,
                elapsed,
                Bound(outcome, 500),
                _providerResult?.Diagnostics?.HttpStatusCode ?? _connection?.HttpStatusCode,
                _providerResult?.FailureKind ?? AiProviderFailureKind.None,
                promptText.Length,
                Encoding.UTF8.GetByteCount(promptText),
                response.Length,
                Encoding.UTF8.GetByteCount(response),
                Bound(validationOutcome, 200),
                Array.AsReadOnly(validationIssues.Select(issue => Bound(issue, 500)).Take(50).ToArray()),
                totalCount,
                includedCount,
                Math.Max(0, totalCount - includedCount),
                promptText,
                response));
        }

        private static string Bound(string value, int maximumLength) =>
            value.Length <= maximumLength ? value : value[..maximumLength];

        private TimeSpan Elapsed()
        {
            var value = _timeProvider.GetUtcNow() - _startedAtUtc;
            return value < TimeSpan.Zero ? TimeSpan.Zero : value;
        }

        private static string StageName(AiRequestStage stage) => stage switch
        {
            AiRequestStage.CheckingSettings or AiRequestStage.PreparingMetadata => "Preparing file context",
            AiRequestStage.Connecting => "Connecting to Ollama",
            AiRequestStage.ValidatingModel => "Validating model",
            AiRequestStage.SendingRequest => "Sending request",
            AiRequestStage.WaitingForModel => "Waiting for model",
            AiRequestStage.ReceivingResponse => "Response body received",
            AiRequestStage.ValidatingSuggestion => "Validating response",
            AiRequestStage.SuggestionReady => "Completed",
            AiRequestStage.RequestCancelled => "Cancelled",
            AiRequestStage.RequestTimedOut => "Timed out",
            _ => "Failed",
        };

        private static AiDiagnosticState StageState(AiRequestStage stage) => stage switch
        {
            AiRequestStage.RequestCancelled => AiDiagnosticState.Cancelled,
            AiRequestStage.RequestFailed or AiRequestStage.RequestTimedOut => AiDiagnosticState.Failed,
            AiRequestStage.Connecting or AiRequestStage.SendingRequest or
                AiRequestStage.WaitingForModel or AiRequestStage.ReceivingResponse or
                AiRequestStage.ValidatingSuggestion => AiDiagnosticState.Active,
            _ => AiDiagnosticState.Succeeded,
        };
    }
}
