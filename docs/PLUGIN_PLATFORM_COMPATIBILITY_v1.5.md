# Plugin Platform Compatibility in v1.5

The v1.4 manifest schema remains readable. v1.5 adds optional
`supportedRuntimeIdentifiers` and `containsNativeDependencies` members.

```json
{
  "runtimeCompatibility": "net8.0",
  "supportedRuntimeIdentifiers": ["win-x64", "linux-x64"],
  "containsNativeDependencies": true
}
```

An empty RID list means a managed-only plugin makes no narrower binary-platform
claim. A plugin declaring native dependencies must supply a bounded, unique RID
list. The current RID must match exactly or discovery reports
`PlatformIncompatible`; the assembly is not loaded. Package paths remain
root-confined, traversal/reparse entries fail closed, and activation always
requires explicit user state. Integrity hashing is stable over controlled
content, but it is not publisher authentication. In-process load contexts are
not a security sandbox.
