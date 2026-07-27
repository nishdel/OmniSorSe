#pragma warning disable CS1591

using OpenSorSe.Extensions.Abstractions;

namespace OpenSorSe.Application.Plugins;

public static class PluginLimits
{
    public const int CurrentManifestSchemaVersion = 1;
    public const int MaximumManifestBytes = 256 * 1024;
    public const int MaximumManifestDepth = 32;
    public const int MaximumPlugins = 256;
    public const int MaximumContributionsPerPlugin = 64;
    public const int MaximumDependenciesPerPlugin = 32;
    public const int MaximumRuntimeIdentifiersPerPlugin = 32;
    public const int MaximumStringCharacters = 2_048;
    public const int MaximumIdentifierCharacters = 128;
    public const int MaximumPackageEntries = 1_024;
    public const long MaximumPackageBytes = 128L * 1024 * 1024;
    public const long MaximumInstalledPluginBytes = 256L * 1024 * 1024;
    public const int MaximumInstalledFiles = 4_096;
    public const int MaximumFailuresBeforeQuarantine = 3;
    public const int MaximumDiagnostics = 1_000;
    public static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(10);
    public static readonly Version HostVersion = new(1, 5, 0);
}

public enum PluginOriginKind
{
    BuiltIn,
    LocalPackage,
    ControlledFolder,
}

public enum PluginLifecycleState
{
    Discovered,
    Invalid,
    Incompatible,
    DependencyBlocked,
    Disabled,
    Ready,
    Loading,
    Initialized,
    Active,
    Failed,
    Quarantined,
    Stopping,
    Stopped,
    RestartRequired,
}

public enum PluginCompatibilityState
{
    Compatible,
    HostVersionTooOld,
    HostVersionTooNew,
    RuntimeIncompatible,
    PlatformIncompatible,
    UnsupportedManifest,
}

public enum PluginIntegrityStatus
{
    NotApplicable,
    NotCalculated,
    Verified,
    Changed,
    Failed,
}

public enum PluginDiagnosticKind
{
    Discovery,
    ManifestParsing,
    Validation,
    Compatibility,
    DependencyResolution,
    IntegrityHashing,
    IntegrityChanged,
    Loading,
    Initialization,
    Timeout,
    Cancellation,
    Activation,
    Deactivation,
    Quarantine,
    ContributionRegistration,
    PackageInstallation,
    Upgrade,
    Rollback,
    Removal,
    WorkflowResolution,
    WatchedFolderResolution,
    RecipeFieldResolution,
}

public sealed record PluginManifest(
    int ManifestSchemaVersion,
    string PluginId,
    string DisplayName,
    string Description,
    string PluginVersion,
    string Publisher,
    string LicenseIdentifier,
    string MinimumOpenSorSeVersion,
    string? MaximumOpenSorSeVersion,
    string RuntimeCompatibility,
    string EntryAssembly,
    string EntryType,
    IReadOnlyList<PluginManifestContribution> Contributions,
    IReadOnlyList<PluginCapability> Capabilities,
    IReadOnlyList<PluginDependency> Dependencies,
    string? Homepage,
    string? SourceRepository,
    bool BuiltIn,
    PluginManifestIntegrity? Integrity)
{
    /// <summary>
    /// Gets the runtime identifiers on which this plugin may be loaded. An empty collection
    /// means that the managed plugin is portable across host-supported platforms.
    /// </summary>
    public IReadOnlyList<string> SupportedRuntimeIdentifiers { get; init; } = [];

    /// <summary>Gets whether the plugin payload contains platform-specific native dependencies.</summary>
    public bool ContainsNativeDependencies { get; init; }
}

public sealed record PluginManifestContribution(
    string ContributionId,
    ExtensionPointKind ExtensionPoint,
    string DisplayName,
    int Priority = 0);

public sealed record PluginDependency(
    string PluginId,
    string MinimumVersion,
    string? MaximumVersion = null,
    bool Optional = false);

public sealed record PluginManifestIntegrity(
    string Algorithm,
    string Hash);

public sealed record PluginManifestIssue(
    string Code,
    string Message,
    bool IsBlocking = true);

public sealed record PluginManifestParseResult(
    PluginManifest? Manifest,
    IReadOnlyList<PluginManifestIssue> Issues)
{
    public bool IsValid => Manifest is not null && Issues.All(issue => !issue.IsBlocking);
}

public sealed record PluginProvenance(
    PluginOriginKind Origin,
    string Source,
    DateTimeOffset DiscoveredAtUtc);

public sealed record PluginStateEntry(
    string PluginId,
    string PluginVersion,
    bool Enabled,
    IReadOnlySet<PluginCapability> GrantedCapabilities,
    string? AcceptedIntegrityHash,
    int ConsecutiveFailureCount,
    bool Quarantined,
    DateTimeOffset? ReviewedAtUtc,
    string? LastError);

public sealed record PluginDescriptor(
    PluginManifest? Manifest,
    string InstallationPath,
    PluginProvenance Provenance,
    PluginLifecycleState LifecycleState,
    PluginCompatibilityState Compatibility,
    PluginIntegrityStatus IntegrityStatus,
    string? CalculatedIntegrityHash,
    bool IsSelectedVersion,
    bool IsEnabled,
    IReadOnlySet<PluginCapability> GrantedCapabilities,
    IReadOnlyList<string> DependencyErrors,
    string? LastError,
    bool RestartRequired)
{
    public string PluginId => Manifest?.PluginId ?? Path.GetFileName(InstallationPath);
    public string PluginVersion => Manifest?.PluginVersion ?? "unknown";
    public string DisplayName => Manifest?.DisplayName ?? "Invalid plugin";
    public bool IsBuiltIn => Provenance.Origin == PluginOriginKind.BuiltIn;
}

public sealed record PluginContributionRegistration(
    string ContributionId,
    string PluginId,
    string PluginVersion,
    ExtensionPointKind ExtensionPoint,
    string DisplayName,
    int Priority,
    PluginLifecycleState LifecycleAvailability,
    PluginProvenance Provenance,
    IExtensionContribution Instance);

public sealed record PluginDiagnostic(
    DateTimeOffset TimestampUtc,
    PluginDiagnosticKind Kind,
    string PluginId,
    string Summary,
    string? ErrorCode = null);

public sealed record PluginDiscoveryResult(
    IReadOnlyList<PluginDescriptor> Plugins,
    IReadOnlyList<PluginDiagnostic> Diagnostics);

public sealed record PluginOperationResult(
    bool Succeeded,
    string Message,
    PluginDescriptor? Plugin = null,
    IReadOnlyList<string>? Warnings = null)
{
    public IReadOnlyList<string> SafeWarnings =>
        Array.AsReadOnly((Warnings ?? []).ToArray());
}

public sealed record PluginPackageInspection(
    bool IsValid,
    PluginManifest? Manifest,
    long TotalUncompressedBytes,
    int EntryCount,
    IReadOnlyList<string> Issues);

public sealed record PluginUsage(
    IReadOnlyList<string> WorkflowProfileIds,
    IReadOnlyList<string> SortingRecipeIds,
    IReadOnlyList<string> WatchedFolderIds,
    IReadOnlyList<string> UnresolvedImportedConfiguration)
{
    public bool HasActiveDependencies =>
        WorkflowProfileIds.Count > 0 || SortingRecipeIds.Count > 0 || WatchedFolderIds.Count > 0;
}

public sealed record PluginContributionReference(
    string PluginId,
    string? PluginVersion,
    string ContributionId,
    ExtensionPointKind ExtensionPoint,
    bool Required = true);

public sealed record ResolvedPluginContributionSnapshot(
    string PluginId,
    string PluginVersion,
    string ContributionId,
    ExtensionPointKind ExtensionPoint);

public sealed record PluginRecipeFieldValue(
    string FieldName,
    string SerializedValue,
    ExtensionValueKind ValueKind,
    string PluginId,
    string PluginVersion,
    string ContributionId,
    ExtensionDerivationKind Derivation,
    string Reason,
    string? Evidence,
    double? Confidence);

public sealed record PluginSuggestionProvenance(
    string PluginId,
    string PluginVersion,
    string ContributionId,
    ExtensionPointKind ExtensionPoint,
    ExtensionDerivationKind Derivation,
    string Reason,
    string? Evidence,
    double? Confidence);

/// <summary>Strictly parses untrusted manifests without loading plugin code.</summary>
public interface IPluginManifestParser
{
    PluginManifestParseResult Parse(ReadOnlySpan<byte> utf8Json, bool expectedBuiltIn = false);
    PluginManifestParseResult ParseFile(string manifestPath, bool expectedBuiltIn = false);
}

/// <summary>Owns bounded durable host state for installed plugin versions.</summary>
public interface IPluginStateStore
{
    Task<IReadOnlyList<PluginStateEntry>> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<PluginStateEntry> entries, CancellationToken cancellationToken);
}

/// <summary>Calculates a deterministic bounded integrity hash for a controlled plugin tree.</summary>
public interface IPluginIntegrityService
{
    Task<string> CalculateAsync(string pluginDirectory, CancellationToken cancellationToken);
}

/// <summary>Discovers inspectable built-in and external descriptors without activating them.</summary>
public interface IPluginDiscoveryService
{
    Task<PluginDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken);
}

/// <summary>Applies deterministic exact-version dependency and conflict state to discovered descriptors.</summary>
public interface IPluginDependencyResolver
{
    IReadOnlyList<PluginDescriptor> Resolve(IReadOnlyList<PluginDescriptor> discovered);
}

/// <summary>Owns the active capability-authorized contribution set.</summary>
public interface IPluginContributionRegistry
{
    PluginOperationResult Register(
        PluginDescriptor owner,
        IReadOnlyList<IExtensionContribution> contributions);
    void RemovePlugin(string pluginId, string pluginVersion);
    IReadOnlyList<PluginContributionRegistration> List();
    PluginContributionRegistration? Find(
        string pluginId,
        string contributionId,
        ExtensionPointKind extensionPoint);
}

/// <summary>Records bounded non-sensitive plugin lifecycle and validation decisions.</summary>
public interface IPluginDiagnostics
{
    void Record(
        PluginDiagnosticKind kind,
        string pluginId,
        string summary,
        string? errorCode = null);
    IReadOnlyList<PluginDiagnostic> List();
    string Export();
}

/// <summary>Activates and deactivates exact plugin versions behind the contribution registry.</summary>
public interface IPluginRuntime : IAsyncDisposable, IDisposable
{
    Task<PluginOperationResult> ActivateAsync(
        PluginDescriptor plugin,
        CancellationToken cancellationToken);
    Task<PluginOperationResult> DeactivateAsync(
        string pluginId,
        string pluginVersion,
        CancellationToken cancellationToken);
}

/// <summary>Owns validated local-package transactions within the controlled plugin root.</summary>
public interface IPluginPackageService
{
    Task<PluginPackageInspection> InspectAsync(
        string packagePath,
        CancellationToken cancellationToken);
    Task<PluginOperationResult> InstallAsync(
        string packagePath,
        CancellationToken cancellationToken);
    Task<PluginOperationResult> UpgradeAsync(
        string packagePath,
        CancellationToken cancellationToken);
    Task<PluginOperationResult> RemoveAsync(
        string pluginId,
        string pluginVersion,
        bool confirmed,
        CancellationToken cancellationToken);
}

/// <summary>Finds known configuration dependencies that can block exact-version removal.</summary>
public interface IPluginUsageInspector
{
    Task<PluginUsage> InspectAsync(string pluginId, CancellationToken cancellationToken);
}

/// <summary>Resolves exact workflow references without version/provider fallback.</summary>
public interface IPluginContributionResolver
{
    PluginOperationResult Resolve(IReadOnlyList<PluginContributionReference> references);
}

/// <summary>Resolves bounded recipe data with exact plugin provenance.</summary>
public interface IPluginRecipeFieldService
{
    Task<IReadOnlyList<PluginRecipeFieldValue>> ResolveAsync(
        IReadOnlyList<PluginContributionReference> references,
        OpenSorSe.Application.Models.ResultFile file,
        CancellationToken cancellationToken);
}

/// <summary>Provides validated, timed, cancellable invocation for every active SDK extension point.</summary>
public interface IPluginExtensionHost
{
    Task<ExtensionResult<MetadataResponse>> GetMetadataAsync(
        string pluginId,
        string contributionId,
        MetadataRequest request,
        CancellationToken cancellationToken);
    Task<ExtensionResult<ContentExtractionResponse>> ExtractContentAsync(
        string pluginId,
        string contributionId,
        ContentExtractionRequest request,
        CancellationToken cancellationToken);
    Task<ExtensionResult<ClassificationResponse>> ClassifyAsync(
        string pluginId,
        string contributionId,
        ClassificationRequest request,
        CancellationToken cancellationToken);
    Task<ExtensionResult<RecipeFieldResponse>> ResolveRecipeFieldAsync(
        string pluginId,
        string contributionId,
        RecipeFieldRequest request,
        CancellationToken cancellationToken);
    Task<ExtensionResult<DuplicateSignalResponse>> AnalyzeDuplicateAsync(
        string pluginId,
        string contributionId,
        DuplicateSignalRequest request,
        CancellationToken cancellationToken);
    Task<ExtensionResult<WorkflowCapabilityResponse>> ResolveWorkflowCapabilityAsync(
        string pluginId,
        string contributionId,
        WorkflowCapabilityRequest request,
        CancellationToken cancellationToken);
    Task<ExtensionResult<ImportResponse>> ImportAsync(
        string pluginId,
        string contributionId,
        ImportRequest request,
        CancellationToken cancellationToken);
    Task<ExtensionResult<ExportResponse>> ExportAsync(
        string pluginId,
        string contributionId,
        ExportRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Coordinates the complete serialized plugin lifecycle exposed to the Desktop.</summary>
public interface IPluginManager : IAsyncDisposable, IDisposable
{
    string PluginRoot { get; }
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PluginDescriptor>> RefreshAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PluginDescriptor>> ListAsync(CancellationToken cancellationToken);
    Task<PluginOperationResult> EnableAsync(
        string pluginId,
        string pluginVersion,
        IReadOnlySet<PluginCapability> grantedCapabilities,
        CancellationToken cancellationToken);
    Task<PluginOperationResult> DisableAsync(
        string pluginId,
        string pluginVersion,
        CancellationToken cancellationToken);
    Task<PluginOperationResult> InstallAsync(string packagePath, CancellationToken cancellationToken);
    Task<PluginOperationResult> UpgradeAsync(string packagePath, CancellationToken cancellationToken);
    Task<PluginOperationResult> RemoveAsync(
        string pluginId,
        string pluginVersion,
        bool confirmed,
        CancellationToken cancellationToken);
    string ExportDiagnostics();
}

public sealed record BuiltInPluginDefinition(
    PluginManifest Manifest,
    Func<IOpenSorSePlugin> Factory);
