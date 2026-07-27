# OpenSorSe 1.2 Watched Folders Troubleshooting

## Watched folder unavailable

OpenSorSe retains the configuration, watched catalogue, results catalogue, and activity history. Reconnect the storage or restore access to the exact configured path, then select **Full reconciliation**. If the root was renamed, remove only the old watch configuration and add the new absolute root.

## Watcher overflow or missed-event warning

Operating-system buffers can overflow during large bursts. The warning is not hidden. OpenSorSe marks reconciliation required and queues or requests a full metadata comparison. Unchanged file content is not automatically reanalysed.

## Processing remains deferred

The file may still be copied, locked, repeatedly resized, or saved in stages. Close the writing application or wait for copying to finish, then select **Scan changes now**. A deferred batch is not reported as fully successful.

## A file does not appear

Check exact ignored paths, directory exclusions, filename/extension patterns, hidden-file choice, built-in temporary/incomplete-download patterns, reparse-point status, and maximum size. Ignored files are intentionally excluded from AI.

## AI did not run

All gates must be enabled: the watched-folder AI option, global AI, a compatible suggestion capability, valid endpoint, and exact installed selected model. AI runs only for affected or explicitly retried items. A no-change reconciliation does not call AI. Use **Retry failed AI analysis** after fixing readiness; it selects only pending/failed catalogue entries and does not repeat completed AI work.

## A suggestion did not organize files

This is expected. A watched-folder suggestion is a non-mutating v1.1 Change Plan. Open it with **Review suggestions**, approve actions, validate, and explicitly select **Apply Plan**. Until then no file has been organized.

## A plan appeared after OpenSorSe Apply

This should not occur. Preserve the Operation Journal and watched activity store, inspect redacted diagnostics, and run full reconciliation. OpenSorSe correlates journal action paths and verified file identity, suppresses the resulting recursive suggestions, and reconciles the catalogue. Do not repeatedly apply the duplicate plan.

## Parent or child root cannot be added

v1.2 rejects overlapping watched roots. Remove the broader/narrower configuration or choose one ownership boundary. This avoids processing `Documents/Invoices` twice when `Documents` is already watched.

## Removing a watch did not delete its catalogue

This is intentional. **Remove from watch list** never deletes user files, saved scan history, watched catalogue history, or grouped activity. Those application-owned records have separate explicit maintenance boundaries.
