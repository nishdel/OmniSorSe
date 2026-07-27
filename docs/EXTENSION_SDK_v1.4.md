# OpenSorSe 1.4 Extension SDK

The SDK is the `OpenSorSe.Extensions.Abstractions` project. It has no reference
to OpenSorSe Application, Desktop, storage, execution, or dependency-injection
projects. Plugin projects should reference only this assembly for host
contracts.

## Design contract

- Implement `IOpenSorSePlugin`.
- Return immutable/bounded `ExtensionResult<T>` values.
- Honor every supplied `CancellationToken`.
- Treat file references and request collections as read-only inputs.
- Declare every contribution and requested capability in `plugin.json`.
- Use stable lowercase IDs; do not derive identity from display text.
- Return controlled failures for expected problems; uncaught exceptions are
  contained and diagnosed by the host.

`InitializeAsync` receives only `PluginInitializationContext`: plugin identity,
granted capabilities, and host version. The host service provider, filesystem
mutation gateway, Change Plan executor, settings store, credentials, and AI
global controls are not exposed.

## Call and lifetime contract

- Initialization, stop, and every extension call receive a linked cancellation
  token. It can represent caller cancellation, host timeout, plugin disable, or
  application shutdown.
- Check cancellation before I/O and repeatedly during parsing or other
  expensive work. Let `OperationCanceledException` propagate; do not translate
  it into success.
- Use `ExtensionResult<T>.Failure` for expected format, access, or unsupported-
  input outcomes. The host contains unexpected exceptions and discards partial
  output.
- Do not retain request models, paths, extracted text, import bytes, export
  rows, or host metadata after the call.
- Do not start untracked background threads/tasks. `StopAsync` must stop
  plugin-owned work as far as the plugin can guarantee.
- Initialization contributions must exactly match manifest IDs and extension
  points. The host rejects missing, extra, duplicate, conflicting, or
  capability-ineligible contributions.
- A source can disappear or change after its `PluginFileReference` was created.
  Handle ordinary filesystem races and return a controlled failure. The host
  may reject results whose source identity became stale.
- Output is accepted atomically only after host validation. A successful result
  is not a promise that it will be persisted, displayed, or used.

## Extension points

| Interface | Purpose |
| --- | --- |
| `IMetadataProvider` | Bounded metadata for a host-selected file |
| `IContentExtractor` | Bounded text/fields without executing embedded content |
| `IFileClassifier` | Labels with confidence, reason, and derivation |
| `IRecipeFieldProvider` | One typed declarative recipe value |
| `IDuplicateSignalProvider` | Evidence only; the host decides duplicate groups |
| `IWorkflowCapabilityProvider` | Named read-only workflow output |
| `IImportFormatProvider` | Non-mutating proposals that the host validates |
| `IExportFormatProvider` | Bounded bytes; the host chooses whether/where to write |

The host validates counts, lengths, confidence/similarity ranges, file names,
media types, typed serialization, and output sizes. Invalid output is discarded
atomically. Extension calls have host timeouts and cancellation.

Every `ExtensionValue`, classification, duplicate signal, workflow output, and
recipe value must report honest derivation. AI-assisted data uses
`ExtensionDerivationKind.AiAssisted` and must include a grounded reason,
evidence where available, and a finite normalized confidence where applicable.
The host copies exact plugin/version/contribution identity into downstream
workflow or Change Plan provenance.

## Capabilities

Capabilities are declared intent and an explicit user grant. They include
metadata/content reads, extracted-text processing, network, AI integration,
recipe/workflow contributions, import/export, and native libraries. External
plugins begin disabled. Never infer permission from installation.

The v1.4 host does not provide an AI adapter to plugins. A future adapter must
still honor the global AI switch, provider readiness, per-item policy, privacy
controls, and provenance.

`NetworkAccess` declares and gates intent inside the host. It does not provide a
network client, firewall rule, or process sandbox. A plugin granted network
access must still disclose destinations and data policy to users and must never
upload content unrelated to the explicit invocation. Native-library capability
is similarly a declaration, not proof that native code is safe.

## Safety invariant

An extension may analyze or propose. It may not directly mutate user files,
approve a Change Plan, call Apply, write the Operation Journal, or bypass
preflight/confirmation/recovery/Undo. Import output is a proposal; export output
is returned to the host; recipe values remain data in a constrained template.

External plugin assemblies run in the OpenSorSe process under the current
user's operating-system permissions. `AssemblyLoadContext` confines dependency
resolution and supports unload attempts; it does not prevent arbitrary .NET or
native code from using OS APIs. Install only trusted, reviewed packages.

See [Plugin Author Guide](PLUGIN_AUTHOR_GUIDE_v1.4.md) and
[Manifest Reference](PLUGIN_MANIFEST_REFERENCE_v1.4.md).
