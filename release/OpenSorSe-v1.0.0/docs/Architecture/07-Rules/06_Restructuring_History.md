# v1.0 Restructuring History and Protection

## Boundary

Folder restructuring is preview-first. A plan is relative to one explicit root and contains only known source identities and safe relative destinations. Previewing or accepting an AI decision does not apply it.

An application operation may be applied only after explicit confirmation. It validates root containment, unchanged source fingerprint, current files, destination conflicts, traversal, bounds, and cancellation. It creates required in-root folders and moves listed files without overwrite or deletion. Each item and final outcome are recorded.

## Repeat protection

Only a successful applied history record activates protection. The current snapshot is compared with the last applied snapshot by root identity, structure hash, file identities, and relative paths. An unchanged root yields AlreadyOrganized. New files yield IncrementalAvailable. Manual changes yield MateriallyChanged. Preview, failed, partial, and unrelated records never suppress a full proposal.

## History

Runs retain bounded source, proposed, and applied structure snapshots, counts, source, status, application/schema version, optional label, and current-match state. Clearing history never changes or undoes files.
