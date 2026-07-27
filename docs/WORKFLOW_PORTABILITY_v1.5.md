# Workflow Portability in v1.5

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
