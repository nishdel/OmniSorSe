# OpenSorSe 1.0 Safety and Privacy

OpenSorSe is local-first and non-destructive by default. Scanning, duplicate review, metadata extraction, OCR, tagging, semantic indexing/search, structure previews/diagrams, catalog comparison, and AI suggestions do not modify selected files.

The only 1.0 workflow authorized to change source locations is deterministic folder restructuring. It is separate from AI and requires:

1. An explicit absolute root.
2. A bounded metadata-only source snapshot.
3. A reviewable relative-path proposal.
4. A second confirmation tied to the exact preview identity.
5. Revalidation that the root is unchanged.
6. Validation of every source and destination before the first move.

The service rejects traversal, absolute destinations, unknown/missing sources, duplicate sources or destinations, reparse-point destinations, stale previews, overwrites, and more than 500 moves. It never deletes or overwrites a file. If a move fails or the operation is cancelled after work begins, completed moves are rolled back where possible; any rollback failure is recorded as partial and does not activate repeat protection.

## AI boundary

AI and Advanced mode are independent and disabled by default. While AI is disabled, provider detection, discovery, requests, and background communication are rejected at the application boundary.

The AI capabilities are separately enabled file-rename, logical folder-structure, and document-text interpretation suggestions. Rename/folder requests use bounded filenames, extensions, deterministic categories, existing logical folder names, request-local identities, and optional concise preferences; they exclude absolute paths and file contents.

Document-text interpretation has its own default-off switch. It requires global AI, the capability, a valid endpoint/model, one explicitly selected known content record, and a direct **Generate** command. Its prompt contains bounded normalized extracted text with page/native/OCR provenance, never file bytes or an absolute path. A custom endpoint may be remote, so Settings warns before this switch is enabled. The prompt forbids exact/legal/financial transcription claims and any filesystem action.

AI output is untrusted strict JSON. Rename models return only an extension-free stem; OpenSorSe preserves and appends the known extension. Folder models receive only opaque request-local IDs and prevalidated folder-name choices. Whole-response validation independently checks exact schema/casing, evidence grounding, identities, counts, assignments, filenames, path components, confidence, parent relationships, cycles, and hierarchy safety. Any safety or identity failure rejects the complete suggestion.

At most one structured-output repair request may follow malformed JSON or a schema-shape failure. It uses the same task/schema, the bounded prior response, and one concise validation error. Timeouts, cancellations, unsafe or unknown identities, path/traversal attempts, hard bounds, provider failures, and model misuse are never retried. Original and repair attempts remain separate related in-memory diagnostics sessions. Accept/edit/reject records a local review decision only. No AI output can invoke restructuring, the historical Executor library, or another file operation.

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

## Dormant generic executor

The repository retains historical `OpenSorSe.Executor` move/copy/rename/undo components and their tests. The Desktop does not register or expose that generic executor. The 1.0 restructuring service is a separate narrow application boundary with exact preview confirmation and its own history; AI services do not reference either executor.

## Recovery

Malformed or invalid settings are preserved while safe defaults are loaded. Existing v0.9.1 settings, catalog schemas 1/2, accepted tags, saved searches, and AI decisions remain readable. Missing 1.0 fields and stores are valid and receive conservative defaults.

Deleting or clearing OpenSorSe-owned indexes/cache/history is not an undo operation and cannot restore source files. Use disposable data for manual restructuring verification and complete the documented checklist before release integration.
