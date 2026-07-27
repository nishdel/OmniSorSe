using System.Collections.ObjectModel;

namespace OpenSorSe.Extensions.Abstractions;

/// <summary>Identifies the v1.4 extension points supported by the OpenSorSe host.</summary>
/// <remarks>
/// Extension points exchange bounded data with the host. None grants access to
/// Change Plan approval, the executor, the host service provider, credentials,
/// or a direct file-mutation API.
/// </remarks>
public enum ExtensionPointKind
{
    /// <summary>Contributes bounded file metadata.</summary>
    MetadataProvider,
    /// <summary>Extracts bounded text or fields from a known file.</summary>
    ContentExtractor,
    /// <summary>Classifies a known file without changing it.</summary>
    FileClassifier,
    /// <summary>Supplies a typed value to a declarative sorting recipe.</summary>
    RecipeFieldProvider,
    /// <summary>Supplies additional evidence for duplicate analysis.</summary>
    DuplicateSignalProvider,
    /// <summary>Supplies a named, read-only workflow capability.</summary>
    WorkflowCapabilityProvider,
    /// <summary>Parses a bounded import payload into a host-validated proposal.</summary>
    ImportFormatProvider,
    /// <summary>Produces a bounded export payload from an explicit read-only model.</summary>
    ExportFormatProvider,
}

/// <summary>Declares sensitive or host-mediated operations an extension may request.</summary>
/// <remarks>
/// A manifest declaration expresses intent, and an explicit user grant allows
/// host registration or invocation. Neither is an operating-system sandbox:
/// external plugin code runs in-process with the current user's permissions.
/// Plugins must still confine themselves to each host request. Network and AI
/// capabilities may be unavailable even when declared; the v1.4 host does not
/// provide a general AI or network client to plugins.
/// </remarks>
public enum PluginCapability
{
    /// <summary>Reads ordinary filesystem metadata for host-selected files.</summary>
    ReadFileMetadata,
    /// <summary>Reads file contents for host-selected files.</summary>
    ReadFileContents,
    /// <summary>Processes text that the host has already extracted.</summary>
    ProcessExtractedText,
    /// <summary>Connects to a network endpoint. The v1.4 host does not grant this by default.</summary>
    NetworkAccess,
    /// <summary>Participates in an explicitly approved AI-provider integration.</summary>
    AiProviderIntegration,
    /// <summary>Contributes fields to declarative recipes.</summary>
    ContributeRecipeFields,
    /// <summary>Contributes capabilities referenced by workflow profiles.</summary>
    ContributeWorkflowCapabilities,
    /// <summary>Returns configuration import proposals.</summary>
    ImportConfiguration,
    /// <summary>Exports explicit read-only report data.</summary>
    ExportReports,
    /// <summary>Loads native libraries. The v1.4 host treats this as sensitive.</summary>
    UseNativeLibraries,
}

/// <summary>Identifies the bounded scalar type of an extension-supplied value.</summary>
public enum ExtensionValueKind
{
    /// <summary>A UTF-8 text value.</summary>
    Text,
    /// <summary>A signed 64-bit integer.</summary>
    Integer,
    /// <summary>A finite decimal number.</summary>
    Decimal,
    /// <summary>A boolean value.</summary>
    Boolean,
    /// <summary>An ISO-8601 UTC date and time.</summary>
    DateTime,
}

/// <summary>Identifies whether a value was deterministic or AI-assisted.</summary>
public enum ExtensionDerivationKind
{
    /// <summary>The same bounded input produces the same value.</summary>
    Deterministic,
    /// <summary>The value was produced with optional AI assistance and requires that provenance.</summary>
    AiAssisted,
}

/// <summary>Represents one controlled extension outcome without using exceptions for expected failures.</summary>
/// <typeparam name="T">The bounded payload type.</typeparam>
/// <param name="Succeeded">Whether the extension completed with a host-validatable payload.</param>
/// <param name="Value">The payload on success; expected failures return no value.</param>
/// <param name="ErrorCode">A stable, non-sensitive plugin error code on failure.</param>
/// <param name="Message">A bounded, user-safe summary that must not contain secrets or document content.</param>
/// <param name="Warnings">Bounded non-fatal warnings associated with a successful or failed call.</param>
public sealed record ExtensionResult<T>(
    bool Succeeded,
    T? Value,
    string? ErrorCode,
    string Message,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Creates a successful result.</summary>
    /// <param name="value">The complete bounded value to return to the host.</param>
    /// <param name="message">A non-sensitive completion summary.</param>
    /// <param name="warnings">Optional non-fatal warnings.</param>
    /// <returns>An immutable successful result.</returns>
    public static ExtensionResult<T> Success(
        T value,
        string message = "Completed.",
        IReadOnlyList<string>? warnings = null) =>
        new(true, value, null, message, Copy(warnings));

    /// <summary>Creates a controlled unsuccessful result.</summary>
    /// <param name="errorCode">A stable error category suitable for diagnostics.</param>
    /// <param name="message">A non-sensitive failure summary.</param>
    /// <param name="warnings">Optional additional warnings.</param>
    /// <returns>An immutable result with no value.</returns>
    public static ExtensionResult<T> Failure(
        string errorCode,
        string message,
        IReadOnlyList<string>? warnings = null) =>
        new(false, default, errorCode, message, Copy(warnings));

    private static IReadOnlyList<string> Copy(IReadOnlyList<string>? values) =>
        Array.AsReadOnly((values ?? []).ToArray());
}

/// <summary>Identifies an installed plugin without exposing host services.</summary>
/// <param name="PluginId">The stable manifest plugin identifier.</param>
/// <param name="PluginVersion">The exact installed numeric version.</param>
/// <param name="DisplayName">The bounded manifest display name.</param>
/// <param name="IsBuiltIn">Whether the contribution is shipped and instantiated by the host.</param>
public sealed record PluginIdentity(
    string PluginId,
    string PluginVersion,
    string DisplayName,
    bool IsBuiltIn);

/// <summary>Provides the immutable, capability-filtered initialization context for one plugin.</summary>
/// <param name="Identity">The exact plugin instance being initialized.</param>
/// <param name="GrantedCapabilities">A read-only snapshot of user-effective grants.</param>
/// <param name="HostVersion">The OpenSorSe host version used for compatibility decisions.</param>
public sealed record PluginInitializationContext(
    PluginIdentity Identity,
    IReadOnlySet<PluginCapability> GrantedCapabilities,
    string HostVersion);

/// <summary>Describes one host-selected file supplied to a read-only extension point.</summary>
/// <param name="FileId">An opaque request identity; it is not a filesystem capability.</param>
/// <param name="FullPath">The normalized host-selected path for this invocation.</param>
/// <param name="SizeInBytes">The observed source length used to enforce request bounds.</param>
/// <param name="LastWriteTimeUtc">The observed source timestamp; the host may later reject stale output.</param>
/// <param name="NormalizedExtension">The normalized file extension, including the leading period when present.</param>
/// <remarks>
/// The host guarantees a syntactically valid request model, not that the file
/// will remain present or unchanged for the duration of an external call.
/// Plugins must handle ordinary access races and must not inspect other paths.
/// </remarks>
public sealed record PluginFileReference(
    string FileId,
    string FullPath,
    long SizeInBytes,
    DateTimeOffset LastWriteTimeUtc,
    string NormalizedExtension);

/// <summary>Provides a bounded, immutable contribution identity.</summary>
/// <remarks>
/// Identity values must exactly match the plugin manifest. Registration is
/// deterministic and rejects conflicts rather than replacing another plugin.
/// Implementations should expose immutable identity state.
/// </remarks>
public interface IExtensionContribution
{
    /// <summary>Gets the stable contribution ID declared in the plugin manifest.</summary>
    string Id { get; }

    /// <summary>Gets the human-readable contribution name.</summary>
    string DisplayName { get; }

    /// <summary>Gets the deterministic ordering priority.</summary>
    int Priority { get; }
}

/// <summary>Defines a plugin entry point loaded by the host without exposing its dependency-injection container.</summary>
/// <remarks>
/// The host calls <see cref="InitializeAsync"/> once for an activation attempt
/// and calls <see cref="StopAsync"/> during disable or shutdown when possible.
/// Both calls have host timeouts and linked cancellation. Expected problems
/// should be returned as <see cref="ExtensionResult{T}"/> failures; cancellation
/// should propagate as <see cref="OperationCanceledException"/>. Other
/// exceptions are contained, diagnosed, and may contribute to quarantine.
///
/// Do not start untracked background work or retain request objects. The SDK
/// does not authorize direct user-file mutation, Change Plan approval,
/// invocation of Apply/Undo, journal access, credential discovery, shell or
/// script execution, or use of internal OpenSorSe services. Capability grants
/// are host policy, not sandboxing.
/// </remarks>
public interface IOpenSorSePlugin
{
    /// <summary>Initializes the plugin within a host-enforced timeout.</summary>
    /// <param name="context">The immutable identity and effective capability snapshot.</param>
    /// <param name="cancellationToken">Linked caller, host-timeout, and shutdown cancellation.</param>
    /// <returns>Exactly the bounded contributions declared by the manifest, or a controlled failure.</returns>
    Task<ExtensionResult<IReadOnlyList<IExtensionContribution>>> InitializeAsync(
        PluginInitializationContext context,
        CancellationToken cancellationToken);

    /// <summary>Stops the plugin. In-process unload may still require an application restart.</summary>
    /// <param name="cancellationToken">Linked host-timeout and shutdown cancellation.</param>
    /// <returns>Success only after plugin-owned work has stopped as far as the plugin can guarantee.</returns>
    Task<ExtensionResult<bool>> StopAsync(CancellationToken cancellationToken);
}

/// <summary>Represents one typed extension value with provenance.</summary>
/// <param name="Name">The stable bounded field name.</param>
/// <param name="Kind">The scalar type represented by <paramref name="SerializedValue"/>.</param>
/// <param name="SerializedValue">The culture-independent bounded value representation.</param>
/// <param name="Derivation">Whether the value is deterministic or AI-assisted.</param>
/// <param name="Reason">A concise explanation of how the value was derived.</param>
/// <param name="Evidence">Optional bounded evidence; it must not invent certainty or disclose unrelated content.</param>
/// <param name="Confidence">Optional finite normalized confidence from zero through one.</param>
public sealed record ExtensionValue(
    string Name,
    ExtensionValueKind Kind,
    string SerializedValue,
    ExtensionDerivationKind Derivation,
    string Reason,
    string? Evidence = null,
    double? Confidence = null);

/// <summary>Requests bounded metadata for one host-selected file.</summary>
/// <param name="File">The only file selected for this invocation.</param>
/// <param name="MaximumFields">The maximum number of fields the host will accept.</param>
/// <param name="MaximumValueCharacters">The maximum serialized size of one value.</param>
public sealed record MetadataRequest(
    PluginFileReference File,
    int MaximumFields,
    int MaximumValueCharacters);

/// <summary>Returns bounded metadata values.</summary>
/// <param name="Fields">Typed values with complete derivation, reason, and optional evidence/confidence.</param>
public sealed record MetadataResponse(IReadOnlyList<ExtensionValue> Fields);

/// <summary>Contributes bounded read-only metadata.</summary>
/// <remarks>
/// The host invokes this point only when metadata access is effectively
/// granted. Output is validated atomically; an excessive or invalid collection
/// is discarded. The plugin must not change the file or follow data to another
/// path.
/// </remarks>
public interface IMetadataProvider : IExtensionContribution
{
    /// <summary>Reads metadata for one host-selected file.</summary>
    /// <param name="request">A validated request containing explicit host bounds.</param>
    /// <param name="cancellationToken">Cancellation that must be observed during I/O or expensive parsing.</param>
    /// <returns>Bounded typed metadata, or a controlled failure.</returns>
    Task<ExtensionResult<MetadataResponse>> GetMetadataAsync(
        MetadataRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Requests bounded content extraction for one host-selected file.</summary>
/// <param name="File">The only file selected for extraction.</param>
/// <param name="MaximumInputBytes">The maximum source bytes the plugin may read.</param>
/// <param name="MaximumTextCharacters">The maximum normalized text the host accepts.</param>
/// <param name="MaximumFields">The maximum number of typed fields the host accepts.</param>
public sealed record ContentExtractionRequest(
    PluginFileReference File,
    long MaximumInputBytes,
    int MaximumTextCharacters,
    int MaximumFields);

/// <summary>Returns bounded extracted text and fields.</summary>
/// <param name="Text">Optional normalized extracted text within the requested bound.</param>
/// <param name="Fields">Optional typed metadata with provenance.</param>
/// <param name="WasTruncated">Whether the plugin deliberately stopped at a declared bound.</param>
public sealed record ContentExtractionResponse(
    string? Text,
    IReadOnlyList<ExtensionValue> Fields,
    bool WasTruncated);

/// <summary>Contributes bounded content extraction.</summary>
/// <remarks>
/// Embedded macros, scripts, external references, and active content must not
/// be executed. Network access is not implied by content access. Output may
/// contain sensitive text and must not be logged or retained beyond the call.
/// </remarks>
public interface IContentExtractor : IExtensionContribution
{
    /// <summary>Extracts content without executing embedded content.</summary>
    /// <param name="request">A validated file and explicit input/output bounds.</param>
    /// <param name="cancellationToken">Cancellation for all reads and parsing stages.</param>
    /// <returns>Bounded text/fields with honest truncation, or a controlled failure.</returns>
    Task<ExtensionResult<ContentExtractionResponse>> ExtractAsync(
        ContentExtractionRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Requests classification of one host-selected file and optional bounded text.</summary>
/// <param name="File">The host-selected file identity.</param>
/// <param name="ExtractedText">Optional bounded text already selected by the host.</param>
/// <param name="MaximumLabels">The maximum number of labels the host accepts.</param>
public sealed record ClassificationRequest(
    PluginFileReference File,
    string? ExtractedText,
    int MaximumLabels);

/// <summary>Represents one classification label.</summary>
/// <param name="Label">A bounded display label.</param>
/// <param name="Confidence">A finite normalized confidence from zero through one.</param>
/// <param name="Reason">A grounded explanation for the label.</param>
/// <param name="Derivation">Whether deterministic processing or AI assistance produced the label.</param>
public sealed record ClassificationLabel(
    string Label,
    double Confidence,
    string Reason,
    ExtensionDerivationKind Derivation);

/// <summary>Returns bounded classification labels.</summary>
/// <param name="Labels">The complete bounded label set for this invocation.</param>
public sealed record ClassificationResponse(IReadOnlyList<ClassificationLabel> Labels);

/// <summary>Contributes file classification.</summary>
/// <remarks>
/// Classification is evidence for later host policy; it does not authorize a
/// file operation. AI-assisted labels must report AI provenance, and plugins
/// must not contact a network endpoint without an effective network grant.
/// </remarks>
public interface IFileClassifier : IExtensionContribution
{
    /// <summary>Classifies a file without modifying it.</summary>
    /// <param name="request">Validated file context and optional host-selected text.</param>
    /// <param name="cancellationToken">Cancellation for the entire classification operation.</param>
    /// <returns>Bounded validated labels, or a controlled failure.</returns>
    Task<ExtensionResult<ClassificationResponse>> ClassifyAsync(
        ClassificationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Requests one named recipe-field value.</summary>
/// <param name="File">The host-selected file identity.</param>
/// <param name="FieldName">The exact manifest/profile field requested.</param>
/// <param name="HostMetadata">A bounded read-only snapshot selected by the host.</param>
/// <param name="ExtractedText">Optional bounded text when the effective capability permits it.</param>
public sealed record RecipeFieldRequest(
    PluginFileReference File,
    string FieldName,
    IReadOnlyDictionary<string, string> HostMetadata,
    string? ExtractedText);

/// <summary>Returns one typed recipe-field value.</summary>
/// <param name="Field">The resolved field with provenance, or <see langword="null"/> when unavailable.</param>
public sealed record RecipeFieldResponse(ExtensionValue? Field);

/// <summary>Contributes typed values to declarative sorting recipes.</summary>
/// <remarks>
/// The host treats the value as data in a constrained template, sanitizes the
/// resulting name/path, records plugin provenance, and creates a proposal.
/// Plugins do not interpret recipe syntax and cannot apply the proposed path.
/// </remarks>
public interface IRecipeFieldProvider : IExtensionContribution
{
    /// <summary>Resolves one field without interpreting recipe syntax.</summary>
    /// <param name="request">The exact requested field and bounded host context.</param>
    /// <param name="cancellationToken">Cancellation for any parsing or analysis.</param>
    /// <returns>One typed value with accurate provenance, no value, or a controlled failure.</returns>
    Task<ExtensionResult<RecipeFieldResponse>> ResolveFieldAsync(
        RecipeFieldRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Requests a duplicate-analysis signal for two host-selected files.</summary>
/// <param name="First">The first host-selected file.</param>
/// <param name="Second">The second host-selected file.</param>
/// <param name="FirstMetadata">Bounded metadata for the first file.</param>
/// <param name="SecondMetadata">Bounded metadata for the second file.</param>
public sealed record DuplicateSignalRequest(
    PluginFileReference First,
    PluginFileReference Second,
    IReadOnlyDictionary<string, string> FirstMetadata,
    IReadOnlyDictionary<string, string> SecondMetadata);

/// <summary>Returns a normalized duplicate signal.</summary>
/// <param name="Similarity">A finite normalized evidence score from zero through one.</param>
/// <param name="SignalKind">A stable bounded description of the evidence type.</param>
/// <param name="Reason">A grounded explanation; it must not claim host duplicate authority.</param>
/// <param name="Derivation">Whether deterministic processing or AI assistance produced the signal.</param>
public sealed record DuplicateSignalResponse(
    double Similarity,
    string SignalKind,
    string Reason,
    ExtensionDerivationKind Derivation);

/// <summary>Contributes evidence to duplicate analysis without declaring files duplicates itself.</summary>
/// <remarks>
/// The signal is advisory. The host retains ownership of duplicate grouping and
/// review. This extension point does not authorize deletion, consolidation, or
/// movement of either file.
/// </remarks>
public interface IDuplicateSignalProvider : IExtensionContribution
{
    /// <summary>Computes a bounded similarity signal.</summary>
    /// <param name="request">Two explicit files and bounded metadata selected by the host.</param>
    /// <param name="cancellationToken">Cancellation for the complete comparison.</param>
    /// <returns>One normalized evidence signal, or a controlled failure.</returns>
    Task<ExtensionResult<DuplicateSignalResponse>> AnalyzeAsync(
        DuplicateSignalRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Requests a named workflow capability.</summary>
/// <param name="CapabilityId">The exact capability identifier declared by the contribution.</param>
/// <param name="Parameters">Bounded, read-only profile parameters validated by the host.</param>
public sealed record WorkflowCapabilityRequest(
    string CapabilityId,
    IReadOnlyDictionary<string, string> Parameters);

/// <summary>Returns the availability and read-only output of a workflow capability.</summary>
/// <param name="IsAvailable">Whether the capability can satisfy this request now.</param>
/// <param name="Outputs">Bounded read-only outputs for host interpretation.</param>
/// <param name="Derivation">Whether deterministic processing or AI assistance produced the output.</param>
/// <param name="Reason">A concise explanation of availability and derivation.</param>
public sealed record WorkflowCapabilityResponse(
    bool IsAvailable,
    IReadOnlyDictionary<string, string> Outputs,
    ExtensionDerivationKind Derivation,
    string Reason);

/// <summary>Contributes a capability that profiles may reference by stable ID.</summary>
/// <remarks>
/// A capability can narrow or enrich analysis but cannot approve a Change Plan
/// or broaden application safety settings. Missing or unavailable exact
/// plugin-version capabilities fail closed; the host does not silently
/// substitute another contribution.
/// </remarks>
public interface IWorkflowCapabilityProvider : IExtensionContribution
{
    /// <summary>Resolves the declared capability.</summary>
    /// <param name="request">The exact capability and validated bounded parameters.</param>
    /// <param name="cancellationToken">Cancellation for the complete resolution.</param>
    /// <returns>Availability plus bounded outputs, or a controlled failure.</returns>
    Task<ExtensionResult<WorkflowCapabilityResponse>> ResolveAsync(
        WorkflowCapabilityRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Requests parsing of an explicit bounded import payload.</summary>
/// <param name="FormatId">The exact manifest-declared format identifier.</param>
/// <param name="Payload">Caller-owned bounded bytes supplied explicitly by the user/host.</param>
/// <param name="MaximumProposalCount">The maximum proposals the host accepts.</param>
/// <remarks>The payload memory is valid only for the duration of the call and must not be retained.</remarks>
public sealed record ImportRequest(
    string FormatId,
    ReadOnlyMemory<byte> Payload,
    int MaximumProposalCount);

/// <summary>Represents one host-validated key/value import proposal.</summary>
/// <param name="ProposalType">A stable bounded proposal category.</param>
/// <param name="Values">Bounded data to be independently validated by the host.</param>
/// <param name="Reason">A concise explanation of the proposed import.</param>
public sealed record ImportProposal(
    string ProposalType,
    IReadOnlyDictionary<string, string> Values,
    string Reason);

/// <summary>Returns proposals that the host must validate before persistence.</summary>
/// <param name="Proposals">The complete bounded proposal set; no item is persisted automatically.</param>
public sealed record ImportResponse(IReadOnlyList<ImportProposal> Proposals);

/// <summary>Contributes a bounded import format without direct storage access.</summary>
/// <remarks>
/// Parsing does not grant persistence or execution. The host validates every
/// proposal against its own schema, dependency, safety, and conflict rules
/// before presenting or storing anything. Import content must never be
/// executed as code, script, command, or template language.
/// </remarks>
public interface IImportFormatProvider : IExtensionContribution
{
    /// <summary>Parses a payload into non-mutating proposals.</summary>
    /// <param name="request">The explicit format, bounded bytes, and output count limit.</param>
    /// <param name="cancellationToken">Cancellation for all parsing work.</param>
    /// <returns>Non-mutating proposals, or a controlled failure.</returns>
    Task<ExtensionResult<ImportResponse>> ImportAsync(
        ImportRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Requests an export from an explicit read-only data model.</summary>
/// <param name="FormatId">The exact manifest-declared format identifier.</param>
/// <param name="Rows">A bounded read-only projection selected by the host.</param>
/// <param name="MaximumOutputBytes">The maximum payload size the host accepts.</param>
public sealed record ExportRequest(
    string FormatId,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows,
    int MaximumOutputBytes);

/// <summary>Returns one bounded export payload.</summary>
/// <param name="SuggestedFileName">A safe relative filename suggestion subject to host validation.</param>
/// <param name="MediaType">A bounded media type describing the payload.</param>
/// <param name="Payload">The complete payload within the requested byte bound.</param>
public sealed record ExportResponse(
    string SuggestedFileName,
    string MediaType,
    ReadOnlyMemory<byte> Payload);

/// <summary>Contributes a bounded export format without storage or file-mutation access.</summary>
/// <remarks>
/// The plugin returns bytes only. The host validates the filename, media type,
/// and bound, then the user/host decides whether and where to create a new
/// export. The plugin does not receive an output path and must not write one.
/// </remarks>
public interface IExportFormatProvider : IExtensionContribution
{
    /// <summary>Creates a bounded payload; the host decides whether and where it is written.</summary>
    /// <param name="request">The explicit format, read-only rows, and byte limit.</param>
    /// <param name="cancellationToken">Cancellation for serialization.</param>
    /// <returns>A bounded payload and safe filename/media-type suggestion, or a controlled failure.</returns>
    Task<ExtensionResult<ExportResponse>> ExportAsync(
        ExportRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Provides helpers for plugin authors creating immutable collections.</summary>
public static class ExtensionCollections
{
    /// <summary>Copies a dictionary into a read-only ordinal dictionary.</summary>
    /// <param name="values">Values to copy; subsequent caller changes are not observed.</param>
    /// <returns>An ordinal-keyed read-only dictionary.</returns>
    public static IReadOnlyDictionary<string, string> ReadOnly(
        IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(values, StringComparer.Ordinal));
    }
}
