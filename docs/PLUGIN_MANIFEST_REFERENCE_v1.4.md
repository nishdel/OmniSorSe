# OpenSorSe 1.4 Plugin Manifest Reference

`plugin.json` is strict camel-case JSON. Comments, trailing commas, duplicate or
unknown properties, excessive nesting, and files over 256 KiB are rejected.

```json
{
  "manifestSchemaVersion": 1,
  "pluginId": "example.metadata",
  "displayName": "Example Metadata",
  "description": "Adds bounded example metadata.",
  "pluginVersion": "1.0.0",
  "publisher": "Example Publisher",
  "licenseIdentifier": "MIT",
  "minimumOpenSorSeVersion": "1.4.0",
  "maximumOpenSorSeVersion": "1.4.99",
  "runtimeCompatibility": "net8.0",
  "entryAssembly": "Example.Plugin.dll",
  "entryType": "Example.Plugin.ExamplePlugin",
  "contributions": [
    {
      "contributionId": "metadata",
      "extensionPoint": "metadataProvider",
      "displayName": "Example metadata",
      "priority": 0
    }
  ],
  "capabilities": ["readFileMetadata"],
  "dependencies": [],
  "homepage": "https://example.invalid/plugin",
  "sourceRepository": "https://example.invalid/source",
  "builtIn": false,
  "integrity": {
    "algorithm": "SHA-256",
    "hash": "64-lowercase-hex-characters"
  }
}
```

## Fields

| Field | Rule |
| --- | --- |
| `manifestSchemaVersion` | Required; v1.4 accepts `1` |
| `pluginId` | Required stable identifier, maximum 128 characters |
| `displayName`, `description`, `publisher`, `licenseIdentifier` | Required bounded text |
| `pluginVersion` | Required numeric version |
| `minimumOpenSorSeVersion`, `maximumOpenSorSeVersion` | Required minimum, optional maximum |
| `runtimeCompatibility` | Required runtime identifier compatible with the host |
| `entryAssembly` | Required normalized relative managed assembly path |
| `entryType` | Required fully qualified `IOpenSorSePlugin` type |
| `contributions` | 1–64 unique declarations |
| `capabilities` | Unique `PluginCapability` names |
| `dependencies` | 0–32 unique plugin IDs with valid min/max ranges |
| `homepage`, `sourceRepository` | Optional absolute HTTP/HTTPS URI |
| `builtIn` | External packages must use `false` |
| `integrity` | Optional SHA-256 declaration |

Contribution `extensionPoint` values are `metadataProvider`,
`contentExtractor`, `fileClassifier`, `recipeFieldProvider`,
`duplicateSignalProvider`, `workflowCapabilityProvider`,
`importFormatProvider`, and `exportFormatProvider`.

Capability values are the lower-camel-case `PluginCapability` names:
`readFileMetadata`, `readFileContents`, `processExtractedText`,
`networkAccess`, `aiProviderIntegration`, `contributeRecipeFields`,
`contributeWorkflowCapabilities`, `importConfiguration`, `exportReports`, and
`useNativeLibraries`.

Dependency objects contain `pluginId`, `minimumVersion`, optional
`maximumVersion`, and optional `optional`. Dependency resolution is
deterministic. Missing required dependencies, incompatible ranges, cycles,
duplicates, and contribution conflicts block activation.

All paths must remain below the package/install root. Rooted, traversal,
alternate-root, link/reparse, duplicate-entry, excessive-size/count, missing
entry assembly, and undeclared native-library packages are rejected.
