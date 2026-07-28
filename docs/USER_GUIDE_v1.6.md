# OpenSorSe 1.6 User Guide

OpenSorSe 1.6 keeps the complete 1.5 workflow and safety model. The release is
focused on reliability, responsiveness, recovery, accessibility, and
cross-platform verification; it does not add an unattended organizer.

## Core workflow

1. Select explicit folders in **Scan folders**.
2. Review progress and cancel at any time.
3. Explore bounded pages in **Files**, exact copies in **Duplicate Detective**,
   optional local tags/content/OCR, and optional **Meaning Search**.
4. Use workflow profiles and sorting recipes to produce deterministic analysis
   and proposals.
5. Treat watched-folder activity as detection and analysis only.
6. Treat optional AI and plugin output as untrusted suggestions.
7. Review each suggestion in **Review Changes**. Approve or reject actions,
   validate the plan, inspect the final summary, and explicitly confirm Apply.
8. Use **Operation History** for journal facts, reports, recovery state, and
   conflict-aware Undo.

The full feature walkthrough remains in the
[v1.5 User Guide](USER_GUIDE_v1.5.md); every described feature remains
available in v1.6.

## What is more reliable in 1.6

- Application-owned state is replaced only after a complete, bounded, flushed
  JSON document is ready.
- Concurrent in-process users of the same store are serialized.
- Large duplicate analysis, result projection, and local search use less
  transient memory and observe cancellation throughout the work.
- Watched-folder startup and shutdown tolerate repeated/concurrent lifecycle
  calls and isolate observer failures.
- Critical status and progress surfaces expose screen-reader automation
  metadata.

## Failure recovery

If a save fails, OpenSorSe keeps the previous complete owned document whenever
replacement did not occur. Invalid settings and workflow files are preserved
under their established recovery contracts. Optional rebuildable caches fail
closed and can be rebuilt. Change Plans and Operation Journals remain separate;
an interrupted file operation is inspected against real paths and identities on
recovery.

See [v1.6 Troubleshooting](TROUBLESHOOTING_v1.6.md), [Safety and
Privacy](SAFETY_AND_PRIVACY.md), and [the manual checklist](MANUAL_TESTING_v1.6.md).
