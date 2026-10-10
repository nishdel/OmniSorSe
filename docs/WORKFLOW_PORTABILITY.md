# Workflow Portability in

**Version scope:** Introduced in v1.5. This is the canonical subject document;
version-specific statements below retain that scope. Consult [Current State](CURRENT-STATE.md)
and source/tests for subsequent authority, schema and runtime changes.
Exact prior text: [Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/WORKFLOW_PORTABILITY_v1.5.md).

Every sorting recipe carries `fileNamePortability`:

- `portable` (default): conservative Windows/Linux interchange;
- `windowsCompatible`: Windows device-name, invalid-character, and trailing
  dot/space rules on any host;
- `currentPlatform`: active-platform rules, allowing Linux names such as
  `report:final` that Windows cannot create.

Existing JSON without the member receives the portable default, preserving the
prior conservative behavior. Export and import preserve the selected policy;
import never silently rewrites it. Preview imported recipes before applying a
Change Plan on a different operating system. Root confinement, collision
blocking, non-overwrite, and explicit review apply in every mode.
