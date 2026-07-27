# 056 — Plugin Foundation and Extension SDK

## Release identity

- Version: `v1.4`
- Name: **Plugin Foundation and Extension SDK**
- Branch: `v1.4-plugin-foundation`
- Base: final `v1.3-workflow-profiles` commit

## Objective

Provide a stable, local, inspectable extension foundation without weakening the
v1.1–v1.3 safety, privacy, workflow, watcher, Change Plan, journal, recovery,
or Undo invariants.

## Implemented scope

- Standalone abstractions SDK and eight read-only/proposal extension points.
- Strict manifest schema, compatibility, controlled discovery, dependency
  graph, deterministic activation order, contribution conflict handling,
  integrity state, diagnostics, quarantine, and lifecycle management.
- One collectible load context per external plugin and shared SDK contracts.
- Explicit external enable/capability grants; built-in reference plugins.
- Transactional local ZIP install, validated upgrade/rollback preservation,
  dependency-aware confirmed removal, and restart-required state.
- Plugin references in profiles/recipes/watched resolution, exact immutable
  snapshots, typed recipe field provenance, import/export host boundaries, and
  pending Change Plan integration.
- Settings > Plugins inspection and lifecycle controls.

## Non-goals

No online marketplace, downloads, automatic update, remote dependency
resolution, plugin scripts, arbitrary UI injection, out-of-process execution,
OS sandbox, publisher/signature authority, credential access, direct user-file
mutation, approval, Apply, journal write, or safety-policy bypass.

## Required invariants

1. Discovery never loads code.
2. External code is disabled until an explicit grant.
3. Invalid, incompatible, changed, conflicting, missing-dependency, cyclic,
   failed, and quarantined plugins fail closed.
4. A contribution cannot replace another contribution silently.
5. All calls are cancellable, bounded, exception-contained, and output-validated.
6. Workflow resolution records exact versions and never falls back silently.
7. Plugin-generated organization remains a Pending proposal.
8. The existing review/preflight/confirmation/execution/journal/recovery/Undo
   path is the only supported mutation path.
9. Package operations stay within a validated controlled root and preserve
   unrelated data/history.
10. Documentation describes in-process risk and integrity limits honestly.

## Acceptance evidence

Automated coverage includes manifest, discovery, compatibility, dependency,
cycle, integrity, package traversal/root/link/native/rollback/removal,
lifecycle exception/timeout/cancellation/quarantine, registration conflicts,
all extension points, workflow fail-closed behavior, immutable provenance,
template confinement, Change Plan state, and Plugins ViewModel operations.

Release validation requires in-place restore, zero-warning Debug and Release
builds, the full test suite in both configurations with no failures or skips,
format verification, `git diff --check`, generated-artifact inspection, and a
clean staging area. Manual GUI/filesystem/runtime testing remains required
before binary publication.

## Documentation

- [Architecture](../../Architecture/10_Plugins/06_v1.4_Plugin_Foundation.md)
- [SDK](../../EXTENSION_SDK_v1.4.md)
- [Author guide](../../PLUGIN_AUTHOR_GUIDE_v1.4.md)
- [Manifest](../../PLUGIN_MANIFEST_REFERENCE_v1.4.md)
- [Local packages](../../LOCAL_PLUGIN_PACKAGES_v1.4.md)
- [User guide](../../USER_GUIDE_v1.4.md)
- [Manual testing](../../MANUAL_TESTING_v1.4.md)
