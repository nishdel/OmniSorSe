# OpenSorSe 1.4 Plugin Author Guide

## Create a plugin

1. Create a .NET class-library project compatible with the v1.4 desktop
   runtime.
2. Reference `OpenSorSe.Extensions.Abstractions` only.
3. Implement `IOpenSorSePlugin` and one or more extension interfaces.
4. Keep contribution IDs stable across compatible releases.
5. Add a strict root `plugin.json` matching the entry assembly, entry type,
   contributions, capabilities, dependencies, and compatibility.
6. Put the manifest and all required local files into a ZIP with relative,
   normalized paths.
7. Test install, explicit enable/grant, timeout, cancellation, invalid inputs,
   upgrade, disable/unload/restart, dependency loss, and removal.

## Minimal entry point

```csharp
using OpenSorSe.Extensions.Abstractions;

public sealed class ExamplePlugin : IOpenSorSePlugin
{
    public Task<ExtensionResult<IReadOnlyList<IExtensionContribution>>> InitializeAsync(
        PluginInitializationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<IExtensionContribution> contributions =
            Array.AsReadOnly<IExtensionContribution>([new ExampleClassifier()]);
        return Task.FromResult(ExtensionResult<IReadOnlyList<IExtensionContribution>>
            .Success(contributions));
    }

    public Task<ExtensionResult<bool>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ExtensionResult<bool>.Success(true));
}

public sealed class ExampleClassifier : IFileClassifier
{
    public string Id => "example-classifier";
    public string DisplayName => "Example classifier";
    public int Priority => 0;

    public Task<ExtensionResult<ClassificationResponse>> ClassifyAsync(
        ClassificationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ClassificationLabel> labels =
            string.Equals(
                request.File.NormalizedExtension,
                ".example",
                StringComparison.Ordinal)
                ? Array.AsReadOnly<ClassificationLabel>(
                [
                    new(
                        "example",
                        1.0,
                        "The file has the deterministic .example extension.",
                        ExtensionDerivationKind.Deterministic),
                ])
                : Array.AsReadOnly<ClassificationLabel>([]);
        return Task.FromResult(
            ExtensionResult<ClassificationResponse>.Success(new(labels)));
    }
}
```

The contribution returned at initialization must exactly match a manifest
declaration. Registration conflicts fail; the host never silently replaces an
active contribution. The example manifest therefore declares contribution ID
`example-classifier`, extension point `fileClassifier`, and the capability
needed for the data it reads.

## Reliability rules

- Check cancellation before and during expensive work.
- Do not start untracked background tasks.
- Do not retain request objects, host-selected paths, or extracted content.
- Bound allocations before reading or producing data.
- Do not execute embedded documents, recipe text, import text, or package
  content.
- Preserve derivation, reason, evidence, and confidence accurately.
- Make initialization and stop idempotent where possible.
- Expect initialization exceptions/timeouts to count toward quarantine.
- Expect disable/unload to require restart.
- Return `ExtensionResult<T>.Failure` for expected problems and allow
  cancellation to propagate. Do not report cancellation as success.
- Treat `PluginFileReference` timestamps/sizes as observations, not locks; a
  source can change during the call.
- Expect the host to discard the whole result if any field, label, confidence,
  filename, media type, count, or byte bound is invalid.

## Security and privacy

Request the least capability set and explain it in publisher documentation.
Never assume a capability grant creates sandboxing. Do not look for credentials,
application stores, or files outside an explicit request. Network and native
code materially increase risk and must be declared.

A network grant does not supply a host HTTP client or prevent other OS access.
Document endpoints, content handling, retention, and failure behavior. The v1.4
host exposes no general AI adapter; do not infer access to the user's configured
provider or credentials.

Plugins are suggestion/analysis components. A direct file mutation, shell/script
execution feature, unreviewed network upload, approval simulation, or attempt
to access internal execution services is unsupported.

External code runs in-process as the current user. A collectible
`AssemblyLoadContext` is dependency isolation, not sandboxing. SHA-256 detects
changed bytes, not publisher identity. Users must establish trust out of band.

## Versioning

Use semantic numeric versions accepted by `System.Version`. Declare the minimum
and optional maximum OpenSorSe version plus dependency ranges. Profiles resolve
exact installed versions, so breaking contribution behavior requires a new
plugin version and deliberate profile update.
