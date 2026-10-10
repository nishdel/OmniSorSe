# Troubleshooting

Use [Current State](CURRENT-STATE.md) for current capabilities and [Operational runbooks](OPERATIONAL_RUNBOOKS.md) for profile recovery. Find the affected subsystem in the chapter index below. Older error text and controls retain their version scope.

## Chapter index

- [v1.8](#support-v1-8): Search says coverage is incomplete; A natural-language filter is wrong; A precise match ranks too low; A result has no snippet; Search is rejected; The deep index is unavailable or busy.
- [v1.7](#support-v1-7): Indexing says waiting; Indexing is paused or never starts; Storage limit reached; A file is failed or skipped; The application or computer stopped unexpectedly; The index is corrupt or from a newer version.
- [v1.6](#support-v1-6): A setting, catalog, workflow, or history change did not save; An invalid owned file was preserved; Cancellation appears delayed; Watched-folder state says reconciliation is required; The application closed while watching or scanning; A Change Plan or Undo is blocked.
- [v1.5](#support-v1-5): A platform feature is limited or disabled; Linux application data is not where expected; A move or Undo is rejected; A watched folder missed activity; Tesseract is unavailable; Folder opening does nothing.
- [v1.4](#support-v1-4): A plugin is visible but unavailable; “Plugin capability unavailable — review workflow profile”; Installation is rejected; Integrity changed; Plugin is quarantined; Disable or upgrade says restart required.
- [v1.3](#support-v1-3): Profile unavailable — review configuration; Profile or recipe will not save; Recipe preview is invalid; Workflow library recovery message; Import failed; OCR or AI is off despite the profile.
- [v1.2](#support-v1-2): Watched folder unavailable; Watcher overflow or missed-event warning; Processing remains deferred; A file does not appear; AI did not run; A suggestion did not organize files.
- [v1.1](#support-v1-1): Apply Plan is disabled; A source is stale or missing; Destination occupied; Source locked or permission denied; Apply failed or rollback was partial; Undo is unavailable or blocked.

## Version history


<a id="support-v1-8"></a>
## v1.8

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/TROUBLESHOOTING_v1.8.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="support-v1-8-search-says-coverage-is-incomplete"></a>
#### Search says coverage is incomplete

Open Background indexing and review names/metadata, text, OCR, related-concept,
and fully indexed coverage. The message identifies exclusions, OCR/local-AI
waits, failed stages, or unavailable storage. Search still returns currently
known filename/metadata results. Resume paused work, restore an optional
dependency, or retry failed items as appropriate.

<a id="support-v1-8-a-natural-language-filter-is-wrong"></a>
#### A natural-language filter is wrong

Every interpreted filter is visible. Remove the individual chip or use **Clear
all filters**. Uncertain language should remain a topic term; report a
reproducible phrase if OpenSorSe applies an uncertain filter. Changing the query
clears old filters.

<a id="support-v1-8-a-precise-match-ranks-too-low"></a>
#### A precise match ranks too low

Open **Why this result?** and compare actual evidence. Exact filename and
literal tiers should remain above related-concept-only results. Record a small
synthetic corpus and query for a relevance regression report; do not share
private document snippets.

<a id="support-v1-8-a-result-has-no-snippet"></a>
#### A result has no snippet

Snippets are optional. Search uses only bounded already-indexed content and will
not extract a file at query time. A metadata-only, excluded, malformed, or
partially indexed file may have no safe snippet.

<a id="support-v1-8-search-is-rejected"></a>
#### Search is rejected

Queries are limited to 512 characters, 32 topic terms, and 16 filters. Embedded
null/control characters and malformed Unicode are rejected. Short typo queries
are intentionally not expanded into many weak matches.

<a id="support-v1-8-the-deep-index-is-unavailable-or-busy"></a>
#### The deep index is unavailable or busy

Compatible filename/metadata Search remains available where possible. Wait for
maintenance or another short transaction to finish, then refresh. Inspect
redacted diagnostics for a failure category. For corruption or an unsupported
newer schema, preserve the recovery copy and use the explicit rebuild action;
never edit a live database manually.

<a id="support-v1-8-forget-or-clear-data-reappears"></a>
#### Forget or clear data reappears

v1.8 stores a durable source-relative suppression/exclusion policy. Ensure the
action completed and inspect the file policy. A later explicit re-index/repair
can intentionally clear the suppression. Watched-folder configuration may own
the source but does not override a file exclusion.

<a id="support-v1-8-selective-repair-is-waiting"></a>
#### Selective repair is waiting

The repair uses the durable v1.7 pipeline. Check pause state, resource mode,
quota, file accessibility, OCR/local-AI availability, retry count, and
processing window. Cancellation and restart recovery apply normally.

<a id="support-v1-8-diagnostics-and-privacy"></a>
#### Diagnostics and privacy

Default Search diagnostics contain timing, counts, stages, coverage,
availability, and failure categories—not full queries, snippets, extracted/OCR
paragraphs, summaries, prompts, tokens, or unnecessary paths. Review every
explicit export before sharing.

</details>

<a id="support-v1-7"></a>
## v1.7

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/TROUBLESHOOTING_v1.7.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="support-v1-7-indexing-says-waiting"></a>
#### Indexing says waiting

Check the active stage and dependency. OCR work waits when Tesseract or required
language data is unavailable. Optional local enrichment waits when its provider
is unavailable. OpenSorSe automatically makes the run eligible again at its
durable retry time after the dependency returns. Choose **Retry failed items**
to request an immediate retry, or **Resume** if the run was explicitly paused.
Name/metadata Search remains available.

<a id="support-v1-7-indexing-is-paused-or-never-starts"></a>
#### Indexing is paused or never starts

Check Background indexing settings: enabled state, Eco/Balanced/Fast mode,
processing time window, and source availability. Idle/power/battery policies
degrade gracefully when the current platform cannot report those signals.

<a id="support-v1-7-storage-limit-reached"></a>
#### Storage limit reached

Choose **Maintain storage**. OpenSorSe removes expired deleted/history records,
orphans, and low-value selected chunks, then compacts the database. If the
limit remains reached, raise the explicit quota, lower the indexing level or
retention, remove an indexing source, or rebuild. Source files are not deleted.

<a id="support-v1-7-a-file-is-failed-or-skipped"></a>
#### A file is failed or skipped

Open indexing failures/diagnostics and review the category. Locked or transient
I/O failures can be retried. Permission denied, missing files, links, or
unsupported input may require changing access/source policy. Skipped items are
counted and are not presented as fully indexed.

<a id="support-v1-7-the-application-or-computer-stopped-unexpectedly"></a>
#### The application or computer stopped unexpectedly

Restart OpenSorSe. Running stages are requeued and compatible completed stages
are reused; interrupted discovery resumes the same run without resetting
completed jobs. If a run remains paused, choose Resume. Cancellation remains
cancelled until explicitly retried.

<a id="support-v1-7-the-index-is-corrupt-or-from-a-newer-version"></a>
#### The index is corrupt or from a newer version

Do not delete unrelated application data. Review the actionable error and
provider-managed `deep-index-*.db` backups in the OpenSorSe index directory.
Existing compatible Search remains available while background indexing is
degraded. Prefer opening a newer-schema index with a compatible newer
OpenSorSe version. Choosing **Rebuild background index** explicitly preserves
the original database and sidecars as a bounded recovery copy before creating
a fresh derived index; a newer schema is never silently downgraded.

<a id="support-v1-7-search-appears-incomplete"></a>
#### Search appears incomplete

Read the coverage line. Some files may only be searchable by name and metadata
until text, OCR, or related-concept stages complete. The UI states:
“Search coverage is still being built. Some files may not appear yet.”

</details>

<a id="support-v1-6"></a>
## v1.6

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/TROUBLESHOOTING_v1.6.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="support-v1-6-a-setting-catalog-workflow-or-history-change-did-not-save"></a>
#### A setting, catalog, workflow, or history change did not save

Check the visible status and redacted diagnostics for permission, disk-space,
capacity, or unavailable-directory errors. OpenSorSe writes a complete sibling
before replacing application-owned JSON, so a failed write should leave the
previous complete document intact. Do not delete an unexpected `*.tmp` file
while another OpenSorSe process is running. OpenSorSe's coordination is
process-local, not a cross-process lock.

<a id="support-v1-6-an-invalid-owned-file-was-preserved"></a>
#### An invalid owned file was preserved

This is intentional. Settings and workflow recovery keep malformed input for
diagnosis rather than silently overwriting it. Back up the file, inspect only
for non-sensitive configuration data, then use the documented explicit save or
clear action. Optional content and semantic indexes can be rebuilt.

<a id="support-v1-6-cancellation-appears-delayed"></a>
#### Cancellation appears delayed

Cancellation is cooperative and is observed between safe units of work.
Filesystem APIs, an external OCR process, a provider request, or a currently
executing safe file-operation boundary may need to return before cancellation
completes. OpenSorSe does not interrupt a mutation midway through an unsafe
boundary.

<a id="support-v1-6-watched-folder-state-says-reconciliation-is-required"></a>
#### Watched-folder state says reconciliation is required

Watcher events are hints and can be duplicated, reordered, omitted, or
overflowed. Use **Full reconciliation**. Restore access if the root is
unavailable, then refresh. A case-only name can represent one path or two paths
depending on the host/filesystem case policy reported by System check.

<a id="support-v1-6-the-application-closed-while-watching-or-scanning"></a>
#### The application closed while watching or scanning

On a normal close, v1.6 cancels and awaits the owned watcher loops. The next
startup still performs offline reconciliation. If the process was forcibly
terminated, inspect watched activity and Operation History; do not infer success
from the absence of a notification.

<a id="support-v1-6-a-change-plan-or-undo-is-blocked"></a>
#### A Change Plan or Undo is blocked

Revalidate the plan. OpenSorSe blocks stale sources, occupied destinations,
changed identities, later-operation dependencies, and unsafe Undo rather than
overwriting. The Operation Journal and exported operation report contain the
safe failure category and recovery facts.

<a id="support-v1-6-screen-reader-status-is-missing"></a>
#### Screen-reader status is missing

Verify the native v1.6 Desktop is running, the host accessibility service is
enabled, and focus is within the application. Critical status surfaces use
polite live announcements; the screen reader can intentionally defer them
while speaking. Record the host, reader/version, page, control, and exact action
for a reproducible issue.

For feature-specific guidance, also see the
[v1.5 troubleshooting guide](TROUBLESHOOTING.md#support-v1-5) and
[v1.6 manual checklist](MANUAL_TESTING.md#manual-v1-6).

</details>

<a id="support-v1-5"></a>
## v1.5

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/TROUBLESHOOTING_v1.5.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="support-v1-5-a-platform-feature-is-limited-or-disabled"></a>
#### A platform feature is limited or disabled

Open Settings → Platform diagnostics and read the capability explanation. Copy
the report when filing an issue. An unavailable state is deliberate when
identity, permission, mount, link, desktop, or external-tool safety cannot be
established.

<a id="support-v1-5-linux-application-data-is-not-where-expected"></a>
#### Linux application data is not where expected

OpenSorSe accepts only absolute `XDG_CONFIG_HOME`, `XDG_DATA_HOME`,
`XDG_STATE_HOME`, and `XDG_CACHE_HOME` values. A missing or relative value uses
the standard home-directory fallback documented in the
[user guide](USER_GUIDE.md#guide-v1-5). It does not merge or delete another location.

<a id="support-v1-5-a-move-or-undo-is-rejected"></a>
#### A move or Undo is rejected

The destination may be occupied, unwritable, linked, on another filesystem, or
the source identity/metadata may have changed. Restore the mount or permissions,
remove the external collision yourself if appropriate, then revalidate. OpenSorSe
does not overwrite, elevate, change ownership, or broadly change permissions.

<a id="support-v1-5-a-watched-folder-missed-activity"></a>
#### A watched folder missed activity

Linux inotify and Windows watcher delivery can overflow, duplicate, coalesce, or
lose events. Use manual reconciliation and inspect grouped watcher activity.
Large Linux trees may require an administrator-managed inotify limit change;
OpenSorSe never runs `sudo` or changes system settings.

<a id="support-v1-5-tesseract-is-unavailable"></a>
#### Tesseract is unavailable

Install Tesseract and the requested language data through the operating system,
then use **Check Text Recognition**. A configured path must be absolute, exist,
resolve safely, and be executable. Blank configuration searches `PATH` without
a shell. Review the [Linux guide](LINUX_BUILD_AND_LAUNCH.md).

<a id="support-v1-5-folder-opening-does-nothing"></a>
#### Folder opening does nothing

Linux requires a graphical session and configured default opener. The desktop
adapter failure is non-fatal; copy the exact application-owned location from
Platform diagnostics and open it manually.

<a id="support-v1-5-a-plugin-is-incompatible"></a>
#### A plugin is incompatible

Check `runtimeCompatibility`, OpenSorSe version range, and
`supportedRuntimeIdentifiers`. Native dependencies require at least one exact
RID matching the host. Managed `AssemblyLoadContext` isolation is not a sandbox.

For safety guarantees see [Safety and Privacy](SAFETY_AND_PRIVACY.md).

</details>

<a id="support-v1-4"></a>
## v1.4

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/TROUBLESHOOTING_v1.4.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="support-v1-4-a-plugin-is-visible-but-unavailable"></a>
#### A plugin is visible but unavailable

Inspect its state in **Settings > Plugins**. Common causes are disabled state,
host/runtime incompatibility, a missing or wrong-version dependency,
contribution conflict, integrity change, repeated-failure quarantine, or a
pending restart. Resolve the displayed diagnostic; do not edit plugin state
JSON by hand.

<a id="support-v1-4-plugin-capability-unavailable--review-workflow-profile"></a>
#### “Plugin capability unavailable — review workflow profile”

The profile or recipe requires an exact plugin/version/contribution that is not
active. Enable the expected version with accepted capabilities, repair its
dependency or integrity problem, or edit the profile/recipe to use an available
contribution. OpenSorSe intentionally does not choose a fallback.

<a id="support-v1-4-installation-is-rejected"></a>
#### Installation is rejected

The package must be a bounded local ZIP containing exactly one root
`plugin.json`, a managed entry assembly at the declared relative path, and no
unsafe/archive-traversal content. Native libraries require the declared
`UseNativeLibraries` capability. Review the manifest reference and ask the
publisher for a corrected package.

<a id="support-v1-4-integrity-changed"></a>
#### Integrity changed

OpenSorSe hashes controlled installed content. Any unexpected change after
acceptance locks the plugin out. Disable it, preserve diagnostics, and reinstall
a verified package from a trusted publisher. A matching hash detects content
change; it does not prove who published the package.

<a id="support-v1-4-plugin-is-quarantined"></a>
#### Plugin is quarantined

Three repeated startup failures trigger quarantine. Export diagnostics, disable
the plugin, correct or upgrade it, and restart if requested. Do not repeatedly
re-enable an unknown or crashing binary.

<a id="support-v1-4-disable-or-upgrade-says-restart-required"></a>
#### Disable or upgrade says restart required

External plugins run in collectible .NET assembly load contexts, but references
held by plugin code or runtime internals can delay unloading. Close OpenSorSe,
restart it, and inspect the resulting state. This is an in-process limitation.

<a id="support-v1-4-removal-is-blocked"></a>
#### Removal is blocked

Open **Workflows** and **Watched Folders** and remove or replace references to
the exact plugin version. Historical snapshots and operation history are
preserved and do not reactivate a plugin.

<a id="support-v1-4-opensorse-fails-during-startup"></a>
#### OpenSorSe fails during startup

Plugin failures should be contained and diagnosed while the application starts
without that contribution. If startup remains blocked, preserve the plugin
root and diagnostic export, then move only the suspect plugin version out of
the controlled root while OpenSorSe is closed. Do not delete workflow, Change
Plan, journal, or history stores.

<a id="support-v1-4-security-concern"></a>
#### Security concern

Disable the plugin, disconnect sensitive resources if appropriate, preserve
the local ZIP/hash/diagnostics, and restart. Plugins are in-process code running
as the current user; v1.4 does not claim a sandbox, signature authority, or
publisher authentication.

</details>

<a id="support-v1-3"></a>
## v1.3

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/TROUBLESHOOTING_v1.3.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="support-v1-3-profile-unavailable--review-configuration"></a>
#### Profile unavailable — review configuration

Open **Workflows** and **Watched Folders**. The assigned profile or recipe may be missing, disabled, archived, incompatible, not permitted by the profile, or the legacy recipe may be `current`. Choose an active persistent replacement and save. OpenSorSe deliberately does not fall back.

The v1.2 profile ID `default` is the only compatibility alias; it maps explicitly to General Documents and records a warning.

<a id="support-v1-3-profile-or-recipe-will-not-save"></a>
#### Profile or recipe will not save

Read the validation message. Common causes are duplicate name/ID, missing recipe dependency, invalid/duplicate file types, OCR without required metadata, an AI policy contradiction, unsupported enum/capability, destructive imported rule action, malformed template, or bounds exceeded.

<a id="support-v1-3-recipe-preview-is-invalid"></a>
#### Recipe preview is invalid

Check required/fallback fields and the conflicts list. Destination templates must be relative, remain below the selected organization root, contain no `.`/`..` segment, and produce a safe unoccupied path. Filename/destination segments cannot be empty, reserved Windows device names, or over the configured length.

Sanitization is reported. A sanitized preview is deterministic, but it remains only a proposal.

<a id="support-v1-3-workflow-library-recovery-message"></a>
#### Workflow library recovery message

OpenSorSe keeps the original `workflow-library.json`, attempts a timestamped `.corrupt-…json` diagnostic copy, and loads canonical built-ins. Do not overwrite the original while investigating. Use **Export workflow diagnostics**, then import validated items from a known-good export or recreate user items.

<a id="support-v1-3-import-failed"></a>
#### Import failed

Check the content type and schema, size/depth limits, duplicate conflict choice, dependency IDs, and templates. Import cannot replace a canonical built-in or execute code. Use **Import as copy** when an ID/name already exists.

<a id="support-v1-3-ocr-or-ai-is-off-despite-the-profile"></a>
#### OCR or AI is off despite the profile

Profiles can request capabilities but cannot bypass application settings. Enable/configure the global capability separately. OCR also requires the local engine/language. AI requires the global switch, compatible capability, selected local model/provider availability, profile/local permission, and an eligible item.

<a id="support-v1-3-no-change-plan-was-generated"></a>
#### No Change Plan was generated

The profile may disable Change Plans or that action type; no recipe may be attached; the file may not satisfy applicability; required fields may be unresolved; the destination may equal the source, collide, escape the root, or be unsafe; or uncertainty policy may skip it. Preview the recipe and inspect scan/watched warnings.

<a id="support-v1-3-a-historical-scan-differs-from-the-current-profile"></a>
#### A historical scan differs from the current profile

This is expected. History stores the resolved profile/recipe revision and effective configuration snapshot used at that time. Editing a profile does not retroactively change its explanation.

</details>

<a id="support-v1-2"></a>
## v1.2

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/TROUBLESHOOTING_v1.2.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="support-v1-2-watched-folder-unavailable"></a>
#### Watched folder unavailable

OpenSorSe retains the configuration, watched catalogue, results catalogue, and activity history. Reconnect the storage or restore access to the exact configured path, then select **Full reconciliation**. If the root was renamed, remove only the old watch configuration and add the new absolute root.

<a id="support-v1-2-watcher-overflow-or-missed-event-warning"></a>
#### Watcher overflow or missed-event warning

Operating-system buffers can overflow during large bursts. The warning is not hidden. OpenSorSe marks reconciliation required and queues or requests a full metadata comparison. Unchanged file content is not automatically reanalysed.

<a id="support-v1-2-processing-remains-deferred"></a>
#### Processing remains deferred

The file may still be copied, locked, repeatedly resized, or saved in stages. Close the writing application or wait for copying to finish, then select **Scan changes now**. A deferred batch is not reported as fully successful.

<a id="support-v1-2-a-file-does-not-appear"></a>
#### A file does not appear

Check exact ignored paths, directory exclusions, filename/extension patterns, hidden-file choice, built-in temporary/incomplete-download patterns, reparse-point status, and maximum size. Ignored files are intentionally excluded from AI.

<a id="support-v1-2-ai-did-not-run"></a>
#### AI did not run

All gates must be enabled: the watched-folder AI option, global AI, a compatible suggestion capability, valid endpoint, and exact installed selected model. AI runs only for affected or explicitly retried items. A no-change reconciliation does not call AI. Use **Retry failed AI analysis** after fixing readiness; it selects only pending/failed catalogue entries and does not repeat completed AI work.

<a id="support-v1-2-a-suggestion-did-not-organize-files"></a>
#### A suggestion did not organize files

This is expected. A watched-folder suggestion is a non-mutating v1.1 Change Plan. Open it with **Review suggestions**, approve actions, validate, and explicitly select **Apply Plan**. Until then no file has been organized.

<a id="support-v1-2-a-plan-appeared-after-opensorse-apply"></a>
#### A plan appeared after OpenSorSe Apply

This should not occur. Preserve the Operation Journal and watched activity store, inspect redacted diagnostics, and run full reconciliation. OpenSorSe correlates journal action paths and verified file identity, suppresses the resulting recursive suggestions, and reconciles the catalogue. Do not repeatedly apply the duplicate plan.

<a id="support-v1-2-parent-or-child-root-cannot-be-added"></a>
#### Parent or child root cannot be added

v1.2 rejects overlapping watched roots. Remove the broader/narrower configuration or choose one ownership boundary. This avoids processing `Documents/Invoices` twice when `Documents` is already watched.

<a id="support-v1-2-removing-a-watch-did-not-delete-its-catalogue"></a>
#### Removing a watch did not delete its catalogue

This is intentional. **Remove from watch list** never deletes user files, saved scan history, watched catalogue history, or grouped activity. Those application-owned records have separate explicit maintenance boundaries.

</details>

<a id="support-v1-1"></a>
## v1.1

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/TROUBLESHOOTING_v1.1.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="support-v1-1-apply-plan-is-disabled"></a>
#### Apply Plan is disabled

Confirm that at least one action is approved, select **Validate Plan**, and review invalid/conflict counts. Editing a destination or changing approval deliberately clears the prior validation. Reject or correct every approved blocking conflict, then validate again.

<a id="support-v1-1-a-source-is-stale-or-missing"></a>
#### A source is stale or missing

The file may have been deleted, renamed, resized, or modified after the Change Plan was created. Use **Validate Plan** to refresh its state without rerunning AI. Return to Files and create a revised plan when the proposed source is no longer current.

<a id="support-v1-1-destination-occupied"></a>
#### Destination occupied

OpenSorSe never overwrites by default and does not silently append a number. Choose a different filename/destination, reject the action, or move the existing destination yourself and revalidate.

<a id="support-v1-1-source-locked-or-permission-denied"></a>
#### Source locked or permission denied

Close applications using the file and confirm access to the source and destination parent. Revalidate. OpenSorSe reports a category rather than relying on a raw exception message. Platform/filesystem permissions can still change between validation and the atomic filesystem call.

<a id="support-v1-1-apply-failed-or-rollback-was-partial"></a>
#### Apply failed or rollback was partial

Open **Operation History**, select the operation, and inspect each action's actual path, error category, rollback state, and warning. Copy the operation report before manual repair. Do not retry blindly: first verify both original and intended paths.

**Rollback partially failed** means OpenSorSe could not verify a complete restoration. The filesystem is not fully transactional; external changes, permissions, storage failure, or interruption may require manual recovery.

<a id="support-v1-1-undo-is-unavailable-or-blocked"></a>
#### Undo is unavailable or blocked

Undo is blocked if the result is missing, replaced, or materially changed; the original path is occupied; a later successful OpenSorSe operation depends on the path; or a created directory is non-empty. OpenSorSe will not overwrite newer data. Review the recorded conflict and restore manually only after confirming identities and backups.

<a id="support-v1-1-interrupted-operation-after-restart"></a>
#### Interrupted Operation after restart

OpenSorSe found a journal entry that did not reach a terminal state. Operation Details shows what path/identity inspection established. A directory that exists after interruption is treated as ambiguous because ownership cannot safely be guessed. Preserve the report and inspect all listed paths before recovery.

<a id="support-v1-1-operation-history-is-empty-or-corrupt"></a>
#### Operation History is empty or corrupt

The journal normally lives at `%LOCALAPPDATA%\OpenSorSe\operation-journal.json`. A missing file means no v1.1 operations were persisted. Corrupt or unsupported data fails gracefully and cannot trigger file work. Preserve the file for debugging before replacing it. Existing v1.0 stores are independent and remain readable.

<a id="support-v1-1-report-privacy"></a>
#### Report privacy

The copied report includes source/destination paths, timestamps, identities, safe error details, and optional model/request correlation IDs. It excludes contents, extracted text, AI prompt bodies, raw responses, and credentials. Inspect paths before sharing.

</details>
