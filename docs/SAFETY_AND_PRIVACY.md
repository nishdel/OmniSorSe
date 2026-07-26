# OpenSorSe 1.3 Safety and Privacy

OpenSorSe is local-first and non-destructive by default. Scanning, watched-folder detection and reconciliation, duplicate review, metadata extraction, OCR, tagging, semantic indexing/search, structure previews/diagrams, catalog comparison, and AI suggestions do not modify selected files.

> OpenSorSe does not apply AI-generated or bulk file changes without a user-reviewed Change Plan. Supported file operations are recorded in the Operation Journal and are reversible unless later external changes make automatic restoration unsafe.

OpenSorSe 1.3 continues to authorize only rename file, move file, and create directory through the v1.1 Change Plan execution boundary.

## Workflow profiles and recipes

> Workflow profiles automate configuration and analysis, not approval or file modification.

Profiles and recipes are bounded local declarative data. Canonical built-ins are read-only; user items use an atomic versioned local JSON store. A corrupt original is preserved and a diagnostic copy is attempted before built-ins-only recovery.

Templates accept only documented fields and date formatting. They do not execute code, commands, environment substitutions, or unrestricted expressions. Values are appended as data once, sanitized and normalized, and checked for traversal, rooted injection, root escape, reserved Windows names, length, unresolved requirements, and occupied destinations. Invalid previews cannot become proposals. Import additionally rejects excessive/deep data, unsupported policies, missing dependencies, destructive recipe rules, and absolute move-rule destinations. Organization moves come only from the root-confined destination template.

Resolved settings are conjunctive: application safety/capability gates are ceilings; profile, watched-folder, and one-time manual settings can only narrow them. Missing/archived/disabled/incompatible assignments stop watched processing with an explicit state. Only the named legacy `default` mapping is migrated; no unrelated silent fallback exists.

Recipe output is stored as a pending v1.1 Change Plan with profile/recipe revision, field values, evidence, deterministic/AI status, warnings, and unresolved fields. No workflow service approves or applies it. Existing preflight, explicit Apply, non-overwrite, journal, recovery, rollback, history, and Undo rules remain the sole mutation path.

Workflow export excludes document contents, AI endpoint/model/provider configuration, credentials, and secrets. Diagnostic export contains library lifecycle/status data and may include the local path of a preserved corrupt workflow file; inspect it before sharing.

The mutation workflow requires:

1. An explicit absolute root.
2. A proposal captured with stable action IDs and source identity/size/last-modified data.
3. User review, editing, and action-level approval or rejection.
4. Explicit **Validate Plan** and a concise final summary.
5. A separate explicit **Apply Plan** confirmation.
6. Immediate full-plan revalidation before the first mutation.
7. A durable pending/running Operation Journal record before mutation.
8. Action-level persistence, result verification, and inverse-operation preparation.

The service rejects out-of-root or linked destinations, unknown/missing/renamed/changed/locked sources, invalid filenames, duplicate sources or destinations, execution-order conflicts, stale scans, occupied destinations, and unsupported action types. It never overwrites or deletes a file. Case-only renames use a verified temporary sibling. Cancellation is observed at safe action boundaries. On a blocking failure, completed reversible actions are rolled back in reverse order; rollback failure remains explicit.

Filesystem operations are transaction-like, not perfectly transactional. Power loss, storage failure, permission changes, or another process can prevent complete rollback. OpenSorSe never claims success, rollback, or Undo without verifying the corresponding paths and identities.

## Watched-folder boundary

> Watched folders automate detection and analysis, not file modification.

Watched roots are explicit opt-in configurations. Operating-system watcher events are untrusted hints: they are bounded, debounced, root-checked, grouped, and verified against the real filesystem before an application-owned catalogue changes. Unknown or directory hints, queue pressure, watcher errors, startup, resume, reconnect, and the daily interval conservatively request reconciliation.

Canonical ignore rules exclude configured paths/patterns, temporary and incomplete-download names, hidden or oversized files when configured, OpenSorSe internal data, and reparse points. Overlapping roots are rejected. New or content-changing files must be readable and stable across observations before content extraction, OCR cache checks, hashing, classification, rules, or AI. Deferred files retain an unresolved state and are retried by later reconciliation.

Pause, disconnection, access failure, and removal from the watch list never delete user files, the dedicated watched catalogue, or grouped activity. Watched updates do not consume or evict entries from the separate opt-in Saved scans catalogue. A missing root remains unavailable until the exact path returns. v1.3 does not search other drives or guess that a root moved.

Deterministic and optional AI suggestions become ordinary reviewable Change Plans. The watched-folder coordinator never invokes the execution service. Verified Operation Journal results correlate expected OpenSorSe-generated watcher events and suppress recursive suggestions without suspending observation for an arbitrary time.

## AI boundary

AI and Advanced mode are independent and disabled by default. While AI is disabled, provider detection, discovery, requests, and background communication are rejected at the application boundary.

The AI capabilities are separately enabled file-rename, logical folder-structure, and document-text interpretation suggestions. Rename/folder requests use bounded filenames, extensions, deterministic categories, existing logical folder names, request-local identities, and optional concise preferences; they exclude absolute paths and file contents.

Document-text interpretation has its own default-off switch. It requires global AI, the capability, a valid endpoint/model, one explicitly selected known content record, and a direct **Generate** command. Its prompt contains bounded normalized extracted text with page/native/OCR provenance, never file bytes or an absolute path. A custom endpoint may be remote, so Settings warns before this switch is enabled. The prompt forbids exact/legal/financial transcription claims and any filesystem action.

AI output is untrusted strict JSON. Rename models return only an extension-free stem; OpenSorSe preserves and appends the known extension. Folder models receive only opaque request-local IDs and prevalidated folder-name choices. Whole-response validation independently checks exact schema/casing, evidence grounding, identities, counts, assignments, filenames, path components, confidence, parent relationships, cycles, and hierarchy safety. Any safety or identity failure rejects the complete suggestion. Accepted valid rename/folder output is converted into a non-mutating Change Plan. The approved persisted plan—not a later model response—is the only possible execution input.

At most one structured-output repair request may follow malformed JSON or a schema-shape failure. It uses the same task/schema, the bounded prior response, and one concise validation error. Timeouts, cancellations, unsafe or unknown identities, path/traversal attempts, hard bounds, provider failures, and model misuse are never retried. Original and repair attempts remain separate related in-memory diagnostics sessions. AI requests and retries remain read-only. The normal Operation Journal stores model/request correlation metadata where relevant, not prompt text or file contents.

Watched-folder AI is separately disabled by default for every root and still requires the global AI switch, compatible capability, valid endpoint, and exact selected available model. It receives only affected, non-ignored result metadata in batches of at most 12, with at most 120 items attempted in one cycle. Per-file completion/failure state prevents successful work from being repeated by **Retry failed AI analysis**; pending or failed items alone are retried. AI unavailability never rolls back a deterministic catalogue update.

## Advanced diagnostics

Advanced Diagnostics is independent of ordinary logging and disabled by default. Its master switch controls all detailed collection; separate category switches currently instrument AI, OCR/text extraction, and scanning. Duplicate detection, search/indexing, rules/organisation, file operations, and standalone performance diagnostics are clearly marked not yet instrumented.

Redacted mode is the default. The separate unredacted opt-in may retain filenames, complete paths, document/OCR text, metadata, tags, search terms, prompts, and responses in process memory. Secrets, credentials, authorization headers, passwords, and API keys are removed in every mode. Detailed diagnostic content is never written to normal logs.

The common store retains at most 50 sessions overall, 20 per category, 750 events per session, 100 OCR page records, 500 scan-entry records, 1,048,576 characters per field/event field set, approximately 8 MiB per session, and approximately 32 MiB total. Rendered OCR images are not retained. History is cleared when diagnostics are disabled and saved, when unredacted retention is turned back off, when explicitly cleared, or when OpenSorSe exits. Data leaves memory only through an explicit JSON or text export chosen by the user.

## OCR, metadata, tags, and semantic search

- Filesystem, PDF, Open XML, and image extractors open supported files read-only, apply byte/page/text bounds, do not execute macros, and do not fetch external resources.
- OCR Beta is separately enabled and capability-detected. PdfPig reads native PDF pages; the built-in PDFtoImage/PDFium renderer creates bounded page images only where native text is insufficient; the optional external Tesseract CLI recognizes those images.
- Reliable native page text skips rasterization/OCR by default. Mixed PDFs preserve native/OCR provenance per page.
- Tesseract version and configured `eng`/`deu` language data are detected before recognition. Process output, page count, duration, image dimensions, retained text, and temporary storage are bounded.
- Temporary page images live only in validated OpenSorSe-owned `job-*` directories and are deleted per page and again on success, error, timeout, or cancellation.
- OCR and extraction failures are isolated per file and cannot stop normal scanning/search/catalog workflows.
- Provenance tags distinguish confirmed system/user evidence from unverified generated candidates.
- Semantic Search Beta is separately enabled, local, deterministic, bounded to configured document/result limits, incremental, cancellable, and rebuildable.
- Search vectors are not shown as meaning or certainty. Results explain concrete match signals.
- Clearing content or semantic stores never changes source files.

## OpenSorSe-owned storage

By default, runtime files are below `Environment.SpecialFolder.LocalApplicationData/OpenSorSe`.

| Data | File/location | Bound and behavior |
| --- | --- | --- |
| Settings | `settings.json` | At most 1 MiB, validated, backward compatible, atomically replaced. |
| Diagnostic logs | `Logs/opensorse-owned-YYYY-MM-DD.log` | Bounded daily files with ownership markers and retention. |
| AI decisions | `decision-history.json` | Up to 1,000 bounded metadata-only review records. |
| Saved catalog | `catalog.json` | Opt-in bounded historical display metadata, names, source roots, and accepted tags. |
| Saved searches | `saved-catalog-searches.json` | Up to 25 name/query definitions; hits are not stored. |
| Content cache | `content-index.json` | Bounded extracted metadata, native/OCR text, page provenance, and extraction fingerprint used locally; source and component/settings fingerprints enable reuse/invalidation. |
| Semantic index | `semantic-index.json` | Up to 10,000 bounded entries with normalized terms, accepted tag evidence, and deterministic vectors. |
| Structure history | `structure-history.json` | Up to 250 records and 4,000 nodes per snapshot with relative paths, fingerprints, previews, outcomes, and applied state. |
| Change Plans | `change-plans.json` | Up to 100 versioned review plans with at most 1,000 actions each; contains paths, identities, reasons, provenance, decisions, validation, and conflicts, but no file contents. |
| Operation Journal | `operation-journal.json` | Up to 500 durable operation records and 128 MiB total; contains attempted paths, identities, results, rollback/Undo facts, safe errors, and optional AI correlation metadata. |
| Watched-folder configurations | `watched-folders.json` | Up to 64 schema-versioned roots with scope, ignore, analysis, notification, quiet-period, lifecycle, and catalogue settings; atomically replaced. |
| Watched catalogues | `watched-catalogues.json` | Up to 250,000 files per catalogue and 256 MiB total; stores metadata, stable/best-effort identity, derived results, AI retry state, directories, and reconciliation facts without file contents. |
| Watched activity | `watched-activity.json` | Up to 1,000 grouped lifecycle/batch/scan/error/plan activities and 16 MiB total; raw watcher events are not persisted individually. |

Content and semantic stores can contain sensitive words extracted from selected documents. They remain local but should be protected like other application data. Raw OCR/native text, semantic vectors, credentials, and detailed Advanced Diagnostics content are never written to ordinary logs.

Atomic stores use temporary sibling files and replace only their own target. Corrupt optional content/semantic/history stores fail closed to an empty or rebuildable state; they never trigger source-file operations.

## Repeat protection and history

Previewed, rejected, cancelled, failed, and partial records never mark a root organized. Only a successful applied record activates protection.

- Exact current/applied match: redundant full proposal is suppressed.
- Existing applied files unchanged plus new files: only new root-level files are proposed.
- Existing file/path change: material change is reported and a fresh review is required.
- Different or moved root: no unrelated history is inherited.
- Explicit **Propose restructuring again**: bypasses suppression but still produces only a preview and can honestly return no safe changes.

Clearing Structure history changes no user file, but removes the local record used for repeat protection.

## Execution boundary

The Desktop registers `IChangePlanExecutionService` as the production user-file mutation boundary. `ChangePlanExecutionService` delegates low-level mutations only to `IFileSystemGateway`; ViewModels, watched-folder services, scanners, rules, and AI services perform no raw file operations. The v1.0 deterministic restructuring compatibility workflow converts its exact confirmed moves into a Change Plan and calls the same execution service.

The repository still contains the pre-v1.1 `IActionExecutor`/`IUndoEngine` compatibility library and its regression tests. It is not registered or exposed by the Desktop and is not used by v1.1/v1.2/v1.3 suggestions or organization workflows.

## Undo

Successful rename/move actions record the original/result paths and pre/post identities. Created directories are undoable only when OpenSorSe created them and they remain empty. Before an inverse action, OpenSorSe checks that the result still exists and is the same file, has not been materially modified, the original is unoccupied, and no later successful OpenSorSe operation depends on the path.

Unsafe actions are marked blocked; they do not overwrite or destroy newer data. Other requested inverse actions may continue, but the operation is explicitly **Undo partially completed**. Whole-operation and selected-action Undo use the same validation and durable journal updates.

## Recovery

Malformed or invalid settings are preserved while safe defaults are loaded. Existing v1.0 settings, catalog schemas 1/2, accepted tags, saved searches, AI decisions, content, semantic, and structure history remain readable. Missing v1.1 plan/journal, v1.2 watched-folder, and v1.3 workflow-library stores are valid empty states. Corrupt workflow data preserves the original and attempts a diagnostic copy before built-ins-only recovery. Corrupt watched configuration/catalogue/activity data is preserved and fails closed rather than silently replacing evidence or starting a watcher. Legacy raw-array journal data is normalized to the current schema; corrupt or unsupported journal data fails gracefully to an empty history and cannot trigger a mutation.

At startup, journal records left Pending or Running are inspected against actual paths and marked **Interrupted**. Completed actions are inferred only when path and identity evidence agree. Directory ownership and ambiguous states are never guessed; Operation Details explains the conflict and any manual recovery requirement.

Deleting or clearing OpenSorSe-owned indexes/cache/history is not an undo operation and cannot restore source files. Use disposable data for manual restructuring verification and complete the documented checklist before release integration.
