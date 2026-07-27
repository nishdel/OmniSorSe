# OpenSorSe 1.1.0 Version Notes

OpenSorSe 1.1 introduces a complete preview-first safety boundary for supported organization changes.

## Highlights

- Reviewable, editable, persisted Change Plans.
- Rename, move, and create-directory actions with no default overwrite.
- Validation at creation/review and immediately before Apply.
- Durable action-level Operation Journal across restarts.
- Verification, reverse-order rollback, safe-boundary cancellation, and Interrupted Operation inspection.
- Conflict-aware whole-operation and selected-operation Undo.
- Persistent Operation History and human-readable debugging report.
- Accepted AI rename/folder suggestions become plans; AI remains read-only and cannot execute.
- Existing deterministic folder apply is routed through the shared execution/journal service.

## Compatibility

Product version is `1.1.0`; file/assembly version is `1.1.0.0`. Existing v1.0 settings, saved catalogs, saved searches, tags, decision history, content cache, semantic index, and structure history remain readable. The new stores are optional when absent.

## Limitations

Permanent deletion, unattended organization, live monitoring, cloud AI/synchronization, learning from decisions, and collaborative catalogs remain outside this release. Filesystems are not perfectly transactional. Undo can be blocked by later external or OpenSorSe changes, and partial rollback/manual recovery remains possible.

No v1.1 packaged binary, signature, installer, or interactive platform validation is claimed by these source notes until the manual release checklist is completed.
