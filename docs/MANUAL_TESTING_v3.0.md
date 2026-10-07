# v3.0.0-rc.1 manual acceptance

Baseline status: prepared with every human scenario **Not run**. Publication of
the testing candidate precedes maintainer acceptance. Automated checks never pass these rows.
[Live GitHub release-testing issue #53](https://github.com/nishdel/OmniSorSe/issues/53)
owns observations; this checked-in checklist defines the scenarios.

Use a disposable Windows x64 account/VM and a new synthetic library. Record the
tester, date, OS, filesystem, display scale, selected model/OCR version and exact
installer SHA-256. The intended installer is
`OmniSorSe-v3.0.0-rc.1-win-x64-setup.exe`; use it only after it appears in the
non-draft [official v3 release](https://github.com/nishdel/OmniSorSe/releases/tag/v3.0.0-rc.1)
with matching checksums and a source commit integrated into `main`. Take the
exact tag target, installer asset/hash and source identity from that release and
[issue #53](https://github.com/nishdel/OmniSorSe/issues/53), which also owns live
execution status. If those assets are unavailable, installation testing has not
opened. v2.13 is a historical upgrade/comparison fixture, never the testing target.

| Test | Tested? | Success/Failure/Not run | Issue registered/reference | Notes |
| --- | --- | --- | --- | --- |
| Fresh installation and first launch | No | Not run | — | Install candidate, verify About/manifest version and source commit. Record unsigned installer warnings. |
| Standard folder indexing | No | Not run | — | Disable both enrichment and embeddings, then add DOCX/PDF/TXT fixtures with Standard indexing. Search becomes usable; no model call occurs. |
| OCR extraction | No | Not run | — | Enable configured Tesseract and scan image-only electricity-bill PDF plus text JPEG. Observe dependency reporting or recognized text. |
| AI-enriched indexing | No | Not run | — | Configure installed local model, enable document interpretation, add AI-enriched folder. Search while enrichment runs. |
| Incremental concepts and provenance | No | Not run | — | Bill fixture lacking literal word “bill” becomes discoverable by inferred concepts; inspect AI origin and summary. Check retained user tags. |
| Enrich existing library | No | Not run | — | Enable AI on Standard-indexed source. Retained text is queued; previously extracted source files do not need full rescan. Metadata-only records first need deeper extraction. |
| Newly discovered files | No | Not run | — | Add file to watched enriched source; deterministic extraction precedes inherited AI processing. |
| Pause, resume, cancel, restart | No | Not run | — | Pause/cancel enrichment; Search stays responsive. Resume/retry and restart preserve durable progress and user decisions. |
| Read-only enrichment | No | Not run | — | Compare source byte hashes and modification times before/after extraction+AI. No embedded metadata changes. |
| Related Files and decisions | No | Not run | — | Inspect same-company/topic connections; reject/confirm and rerun analysis. User decisions remain authoritative. |
| Organize strategies and explanations | No | Not run | — | Select files, open Organize, compare both trees for all three strategies and inspect reasons. |
| Edit and reject proposal | No | Not run | — | Edit a file destination, rename a proposed folder, reject a move. Disk remains untouched. Remember a preference, reopen and check future proposal adaptation. |
| Safety, Apply and Undo | No | Not run | — | Test missing source, stale preview, duplicate/occupied destination and traversal rejection; apply a valid plan after approval; Undo restores original fixture path. |
| Storage location and restart | No | Not run | — | Save another directory and restart. Library, decisions, preferences and history remain available; original recovery copies remain. |
| Storage usage, limits and cleanup | No | Not run | — | Measure usage, lower cache quota, reclaim. Temporary/cache data goes first; accepted tags, relationship decisions and operation history survive. |
| Upgrade existing v2.13 library | No | Not run | — | Copy a disposable old profile, upgrade, verify sources/tags/relationships/preferences/history and schema migration. Preserve original backup for recovery. |
| Keyboard, resizing and accessibility | No | Not run | — | Use only keyboard through folder choice, search controls, Organize editor and storage Settings; inspect focus/live status at 100/150/200% scale. |
| Installer lifecycle | No | Not run | — | Close app, uninstall/reinstall in test account; confirm expected profile preservation and exact candidate package identity. |
| Dedicated embedding model and existing library | No | Not run | — | Enable embeddings with an installed embedding model independent of chat; retained files progress without re-adding folders. Model and indexed/pending counts are visible. |
| Keyword, semantic and hybrid relevance | No | Not run | — | Run exact filename, literal and paraphrase examples from the benchmark corpus; inspect RRF ranks, cosine, model and source-field offsets. Exact filename stays first. Try an unrelated query and assess weak suggestions; similarity never claims a fact. |
| Vector fallback and incomplete coverage | No | Not run | — | Disable embeddings or select a missing model; keyword results remain available with truthful status. Search also works while vectors are still being produced. |
| Vector lifecycle and restart | No | Not run | — | Change content, move/delete fixtures and update enrichment, then reconcile. Stale matches disappear. Restart resumes missing work; changing model/digest rebuilds compatible vectors. |
| Similarity and relationship decisions | No | Not run | — | Related Files lists semantic suggestions separately. Reject/Never relate and privacy suppression remove those pairs from semantic suggestions. No relationship is silently confirmed. |
| Vector storage, rebuild and cleanup | No | Not run | — | Check logical vector bytes, lower budget, relocate and restart. Cleanup pauses embeddings and clears vectors without losing catalog/enrichment/decisions; Resume or rebuild regenerates only derived data. |

The core definitions in [Manual release testing](MANUAL_RELEASE_TESTING.md)
remain applicable. Record expected versus observed behavior, failures and actual
environment in the live issue. A changed package starts a new table with all
rows Not run and retains the previous evidence.

Repeatable synthetic inputs and quality criteria live in the opt-in
[real-model Search benchmark](../eng/benchmarks/VectorSearch/README.md).
Its automated results are separate from all 24 human rows above.
