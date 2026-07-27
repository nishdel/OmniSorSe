# OpenSorSe 1.1 Troubleshooting

## Apply Plan is disabled

Confirm that at least one action is approved, select **Validate Plan**, and review invalid/conflict counts. Editing a destination or changing approval deliberately clears the prior validation. Reject or correct every approved blocking conflict, then validate again.

## A source is stale or missing

The file may have been deleted, renamed, resized, or modified after the Change Plan was created. Use **Validate Plan** to refresh its state without rerunning AI. Return to Files and create a revised plan when the proposed source is no longer current.

## Destination occupied

OpenSorSe never overwrites by default and does not silently append a number. Choose a different filename/destination, reject the action, or move the existing destination yourself and revalidate.

## Source locked or permission denied

Close applications using the file and confirm access to the source and destination parent. Revalidate. OpenSorSe reports a category rather than relying on a raw exception message. Platform/filesystem permissions can still change between validation and the atomic filesystem call.

## Apply failed or rollback was partial

Open **Operation History**, select the operation, and inspect each action's actual path, error category, rollback state, and warning. Copy the operation report before manual repair. Do not retry blindly: first verify both original and intended paths.

**Rollback partially failed** means OpenSorSe could not verify a complete restoration. The filesystem is not fully transactional; external changes, permissions, storage failure, or interruption may require manual recovery.

## Undo is unavailable or blocked

Undo is blocked if the result is missing, replaced, or materially changed; the original path is occupied; a later successful OpenSorSe operation depends on the path; or a created directory is non-empty. OpenSorSe will not overwrite newer data. Review the recorded conflict and restore manually only after confirming identities and backups.

## Interrupted Operation after restart

OpenSorSe found a journal entry that did not reach a terminal state. Operation Details shows what path/identity inspection established. A directory that exists after interruption is treated as ambiguous because ownership cannot safely be guessed. Preserve the report and inspect all listed paths before recovery.

## Operation History is empty or corrupt

The journal normally lives at `%LOCALAPPDATA%\OpenSorSe\operation-journal.json`. A missing file means no v1.1 operations were persisted. Corrupt or unsupported data fails gracefully and cannot trigger file work. Preserve the file for debugging before replacing it. Existing v1.0 stores are independent and remain readable.

## Report privacy

The copied report includes source/destination paths, timestamps, identities, safe error details, and optional model/request correlation IDs. It excludes contents, extracted text, AI prompt bodies, raw responses, and credentials. Inspect paths before sharing.
