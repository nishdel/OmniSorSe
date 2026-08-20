# OmniSorSe v2.10 operational runbooks

These are concise maintainer procedures. Preserve evidence before resetting
state. Never edit a user's source files as part of profile recovery.

## Corrupt SQLite index

1. Stop every OmniSorSe instance and retain the profile.
2. Run Data & Index Health and collect privacy-reviewed diagnostics.
3. Preserve the provider recovery/corruption copy and note version/commit.
4. Prefer the existing explicit Reset/Rebuild derived-index action.
5. Confirm source registrations and logical user-state backup first.
6. Reopen, verify schema 6, source reachability, Search coverage, and failed jobs.

## Corrupt Change Plan or Operation Journal

1. Do not bypass the recovery-required mutation fence.
2. Preserve the original and generated `.corrupt-*` copy.
3. Determine whether an operation was partially applied by comparing journal
   facts and current filesystem identity; never infer from missing JSON alone.
4. Restore a reviewed known-good profile state or use a purpose-built reviewed
   recovery path. Do not replace the journal with `[]`.
5. Re-run health and Change Plan preflight before permitting mutation.

## Failed migration

1. Stop the app and preserve the database plus managed migration backup.
2. Record source application version/commit, schema, free space, and exception
   category.
3. Correct storage/permission pressure before retry.
4. Reopen with the same or newer compatible binary; do not downgrade-write a
   schema-6 profile.
5. Validate quick check, schema, sources, Search, tags, and decisions.

## Stuck indexing or unavailable storage

1. Check whether the source/removable drive is reachable and unchanged.
2. Review resource policy, optional provider status, and failed-job category.
3. Cancel or pause through the UI; do not kill the process during mutation.
4. Reconnect storage, then explicitly Resume/Retry/Reconcile.
5. Rebuild only derived state and only after a logical state export.

## Partial Change Plan, rollback, or Undo

1. Stop new mutations and preserve the journal.
2. Use Operation History and per-action actual paths/identities.
3. Run recovery/preflight; external changes may legitimately block Undo.
4. Never overwrite an occupied target to force recovery.
5. Escalate a rollback failure with the journal/corruption copy and a redacted
   filesystem-state description.

## Backup or restore failure

1. Keep the selected archive, current profile, and generated pre-restore
   recovery archive.
2. Check free space, permissions, archive format, digest, and version.
3. Re-preview the exact unchanged archive.
4. Retry only the explicitly selected categories. Exact file IDs are required
   for Smart Tag authority; skipped identities are not errors to guess around.

## Optional provider failure

1. Verify enabled state, resolved executable/endpoint, version, and timeout.
2. For remote plain HTTP, confirm the user understands the unencrypted boundary.
3. Disable the provider and verify base local behavior remains available.
4. Never add provider binaries/models to the repository or a support bundle.

## Support evidence

Record app version, source commit, build configuration, OS/runtime, schema,
health issue codes, aggregate counts, and sanitized errors. Exclude extracted
content, OCR, transcripts, User Tags, Saved View queries, prompts/responses,
tokens, and private paths unless the user explicitly reviews and supplies them.

## Dependency vulnerability or release mismatch

1. Reproduce the audit from a clean forced restore and record direct/transitive
   package identity.
2. Assess reachability before changing a dependency.
3. For a release mismatch, stop packaging; compare requested ref, resolved
   commit, binary product/file version, `OmniSorSe.build.json`, package name,
   release notes, and checksums.
4. Never publish from a mismatched or dirty tree.
