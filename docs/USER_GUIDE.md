# User guide

Start with [How OmniSorSe Works](HOW_OMNISORSE_WORKS.md), [Installation](INSTALLATION.md), [Organize](ORGANIZE.md), [Hybrid Search](HYBRID_SEARCH.md) and [Storage management](STORAGE_MANAGEMENT.md). The inherited chapters below preserve workflow-specific instructions and UI labels at their introduction; current source and [Current State](CURRENT-STATE.md) resolve later changes.

## Chapter index

- [v1.9](#guide-v1-9): Background relationship analysis; Browse Smart Collections; Inspect Related Files; Use relationship-aware Search; Forget or rebuild derived data; Diagnostics.
- [v1.8](#guide-v1-8): What Search uses; Natural-language filters; Results, snippets, and explanations; Progressive coverage; Inspect and forget indexed data; Selective repair.
- [v1.7](#guide-v1-7): Search; Indexing levels; Progress and control; Recovery; Storage and privacy.
- [v1.6](#guide-v1-6): Core workflow; What is more reliable in 1.6; Failure recovery.
- [v1.5](#guide-v1-5): Start and storage; Platform diagnostics; Safe file operations; Workflows; Plugins and external tools; Watchers and links.
- [v1.4](#guide-v1-4): Inspect plugins; Install a local package; Upgrade, disable, and remove; Workflows and recipes; Diagnostics and privacy.
- [v1.3](#guide-v1-3): Workflows; Built-in profiles; Profiles; Recipes and template syntax; Manual scans; Watched folders.
- [v1.2](#guide-v1-2): Safety boundary; Add and manage a watched folder; Incremental scanning; Full reconciliation, restart, pause, and offline storage; Ignore rules; Quiet period and unstable files.
- [v1.1](#guide-v1-1): Preview-first organization; Supported actions; Conflicts and stale plans; Undo guarantees and limits; Operation History and reports; Interrupted Operations.

## Version history


<a id="guide-v1-9"></a>
## v1.9

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/USER_GUIDE_v1.9.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

This guide adds v1.9 relationship and collection behavior to the v1.8 Search
and v1.7 background-indexing guides. Existing Scan, Watched Folder, duplicate,
workflow, Change Plan, recovery, and Undo instructions remain unchanged.

<a id="guide-v1-9-background-relationship-analysis"></a>
#### Background relationship analysis

Relationship analysis runs as a durable background-indexing stage. It uses
metadata and derived data already retained at the selected indexing level, so
available evidence improves as indexing progresses. Completed unchanged work
is reused across pause, restart, dependency loss, and ordinary rescans.

In **Settings → Background indexing**, you can enable or disable relationship
analysis, set bounded candidate/relationship/collection limits, and exclude
file extensions. Disabling it does not disable ordinary Search or remove source
files. Use index privacy controls when you also want derived relationship data
forgotten.

<a id="guide-v1-9-browse-smart-collections"></a>
#### Browse Smart Collections

Open **Collections** from the main navigation. The **Smart Collections** tab
shows evidence-backed virtual groups. Select a collection to inspect:

- title, description, confidence, creation source, and update time;
- indexed member files;
- relationships and their retained evidence;
- a deterministic timeline from available creation/modification timestamps.

Rename or pin a collection to keep your preferred presentation. Merge combines
virtual membership; Split removes the selected member and records that choice.
None of these operations moves or changes a source file.

<a id="guide-v1-9-inspect-related-files"></a>
#### Inspect Related Files

Open **Related Files**, choose an indexed file, and filter by relationship type
or minimum confidence. Sort by confidence, relationship, filename, or last
validation. Select an edge to inspect the evidence, algorithm/version, and user
decision.

Use **Confirm**, **Reject**, **Always relate**, **Never relate**, or **Unlink**
to correct suggestions. Use the two file selectors and relationship category to
create a manual link. A Custom relationship requires a short valid name.

<a id="guide-v1-9-use-relationship-aware-search"></a>
#### Use relationship-aware Search

The Search option **Include related file context** is enabled by default. Search
first performs its ordinary exact/literal-first ranking, then may add bounded
direct neighbors from already-ranked results. Explanations identify relationship
context when it actually contributed. Disable the option per query when you do
not want context expansion.

Search does not require Ollama. If the relationship index is unavailable,
filename, folder, metadata, text, OCR, filters, snippets, explanations, and the
other v1.8 ranking signals continue through their existing fallback behavior.

<a id="guide-v1-9-forget-or-rebuild-derived-data"></a>
#### Forget or rebuild derived data

The Collections privacy and repair area offers index-only actions:

- **Forget file relationships** removes automatic relationship data for the
  selected file;
- **Forget and exclude** also suppresses future relationship analysis;
- **Forget source relationships** applies the same boundary to the selected
  source without removing its indexing ownership;
- **Forget collection** removes only the virtual collection and records a
  tombstone to prevent immediate recreation;
- **Rebuild selected file** clears automatic edges, preserves manual choices,
  and queues the existing durable relationship stage for refresh;
- **Repair derived data** removes orphan/corrupt relationship records and stale
  memberships.

Every action states that original files remain unchanged. Review the selected
file or collection before applying an operation.

<a id="guide-v1-9-diagnostics"></a>
#### Diagnostics

The Collections status includes relationship, collection, evidence, correction,
exclusion, candidate, timing, algorithm-version, and repair counts. It avoids
document text and unnecessary paths. Advanced Diagnostics keeps the existing
Search/index privacy boundary; exports remain explicit and reviewable.

<a id="guide-v1-9-accessibility"></a>
#### Accessibility

Collections, Related Files, inspectors, filters, explanations, privacy actions,
and maintenance controls provide meaningful accessible names. Tabs, lists,
selectors, and buttons support keyboard focus and activation as well as pointer
and touch/click use. Status text is exposed as a live region. Perform the
unchecked v1.9 manual checklist with the maintainer's target screen reader and
desktop platform before a release claim.

<a id="guide-v1-9-known-limitations"></a>
#### Known limitations

Relationships are conservative and evidence-based, not exhaustive. Some
categories are primarily available for manual links until a future algorithm
has trustworthy evidence. Timelines expose indexed timestamps rather than
invented event narratives. Collections are local virtual data. v1.9 does not
implement a conversational assistant or the future Knowledge Graph.

</details>

<a id="guide-v1-8"></a>
## v1.8

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/USER_GUIDE_v1.8.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

OpenSorSe 1.8 keeps the v1.7 durable background index and makes Search easier to
refine, explain, measure, and control. It remains local and useful without
Ollama.

<a id="guide-v1-8-what-search-uses"></a>
#### What Search uses

Search can use filenames, folder names, paths, extensions, file type, metadata,
accepted tags, extracted document text, OCR text, summaries, generated
keywords, selected bounded text, and related concepts. Exact filename and
literal text evidence remain stronger than related-concept similarity.

The `?` help control beside Search works through hover where supported,
keyboard focus/activation, and click or touch. It has a screen-reader name and
help text.

<a id="guide-v1-8-natural-language-filters"></a>
#### Natural-language filters

Queries such as these are interpreted locally:

- `PDF invoices from 2026 mentioning Mercedes`
- `large videos modified this month`
- `documents tagged tax`
- `files in the Raspberry Pi folder about monitoring`
- `metadata only household records`

Recognized filters appear below the query. Select a filter chip to remove it,
or use **Clear all filters**. Uncertain phrases remain topic words. Changing the
query clears the previous filter set.

Search accepts bounded file types, explicit `extension:`, `tag:`, and `source:`
forms, named folders, sizes, dates/months/years, indexing level/completeness,
OCR or related-concept availability, and failed indexing. A query is limited
to 512 characters, 32 topic terms, and 16 filters.

<a id="guide-v1-8-results-snippets-and-explanations"></a>
#### Results, snippets, and explanations

A result may include a short snippet from retained index data. Its label says
whether the source was a filename, path, metadata, document text, OCR, summary,
tag/keyword, or selected text. A snippet is at most 240 characters and Search
does not reopen or re-extract the file to create it.

Open **Why this result?** to see only reasons that actually affected ranking,
such as an exact filename, folder, tag, document/OCR text, or related-concept
match. Ordinary users see reasons, not internal scoring mathematics or numeric
vectors.

<a id="guide-v1-8-progressive-coverage"></a>
#### Progressive coverage

Search remains available during active, paused, cancelled, recovering, or
partially failed indexing. Coverage reports names/metadata, document text, OCR,
related concepts, and fully indexed files. Messages also identify exclusions,
OCR or local-AI dependency waits, failed stages, or temporary index
unavailability.

An empty result is not described as exhaustive while material coverage is
missing. Filename and metadata Search can continue when deeper data is
unavailable.

<a id="guide-v1-8-inspect-and-forget-indexed-data"></a>
#### Inspect and forget indexed data

Choose **Inspect indexed data** on a result to review:

- indexing level and last indexed date;
- metadata size;
- extracted/OCR character counts;
- whether a summary or related-concept data exists;
- keyword, selected-chunk, failure, and stage-history counts;
- source ownership and per-file privacy policy.

OpenSorSe does not show raw related-concept vectors or complete document text in
this panel.

Index-only actions can clear OCR data, clear related-concept data, use
metadata-only indexing, exclude future deep indexing, retry a failed stage, or
re-index a file. Forgetting a file or source requires confirmation. Every
action states that original files are unaffected.

Because summaries, keywords, selected text, and related-concept data can be
derived from OCR, clearing OCR also clears and suppresses those dependent
representations. This avoids retaining an indirect copy of text the user chose
to clear. An explicit selective repair can generate permitted data again.

Forgetting a watched source preserves watched-folder ownership. Forgetting
derived data stores a minimal suppression/exclusion rule so the same data is
not immediately generated again. Clearing shared duplicate-content data
reports how many records were affected.

<a id="guide-v1-8-selective-repair"></a>
#### Selective repair

Use record verification, metadata refresh, document-text refresh, OCR refresh,
summary/keyword regeneration, related-concept regeneration, file
re-index/retry, or **Rebuild selected source** for isolated problems. The
selected repair invalidates only the requested stage and its dependants, then
uses normal persistent progress, pause, cancellation, retry, and restart
recovery. The repair description identifies optional OCR/local-AI dependencies
and confirms the source file remains untouched. A full database rebuild is not
required for one inconsistent record.

<a id="guide-v1-8-privacy-settings"></a>
#### Privacy settings

Background Indexing settings allow metadata-only Basic indexing, OCR off,
local-AI enrichment off, generated summaries/keywords off, related-concept
data/chunks off, generated-folder exclusions, binary/executable metadata-only
defaults, retention, quota, and source exclusions. The index can contain
searchable representations of selected document contents; protect application
data like the source documents.

**Clear generated Search data** requires confirmation, clears the compatible
Search store, and forgets generated data for every registered deep-index source.
The source registrations and original files remain unchanged. Durable
exclusions prevent immediate regeneration until the user explicitly repairs or
re-indexes the affected source.

The SQLite index is not advertised as encrypted. OpenSorSe does not implement a
custom encryption scheme. Operating-system account, disk, and backup
protections remain the supported at-rest controls.

<a id="guide-v1-8-ai-optional-behavior"></a>
#### AI-optional behavior

Filename, folder, metadata, exact text, OCR, filters, date parsing, ranking,
snippets, and explanations are deterministic local features. Ollama is
optional. If enabled, local AI may improve indexing summaries/keywords, but its
absence does not block ordinary Search. OpenSorSe does not silently transmit
queries or document data to a remote service.

<a id="guide-v1-8-known-limitations"></a>
#### Known limitations

- The grammar is deliberately conservative and is not conversational Search.
- Typo tolerance is filename-oriented, bounded, and language-neutral; it is not
  a full multilingual spell checker.
- Related-concept quality depends on retained local representations and
  coverage; it is not a statement of meaning or certainty.
- Inspection reports the embedded provider and processor contract version.
  v1.8 does not persist a per-file Ollama model identity for older generated
  rows, so it does not invent one in the inspection panel.
- v1.8 does not recursively Search archive contents beyond data supplied by
  existing bounded extractors.
- Relevance metrics describe the repository's synthetic regression corpus, not
  universal human judgement or million-file performance.

</details>

<a id="guide-v1-7"></a>
## v1.7

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/USER_GUIDE_v1.7.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="guide-v1-7-search"></a>
#### Search

The feature previously named **Meaning Search** is now **Search**. Existing
indexes and settings remain compatible. Search can use names, folder names,
metadata, document text, OCR, tags, summaries, and related concepts. Select or
focus the `?` help button beside the Search heading for the concise explanation;
the same action works with pointer, keyboard, touch/click, and screen readers.

Search remains usable while background indexing runs. A coverage message means
some files currently have only name/metadata coverage while others also have
document text, OCR, or related-concept data. When coverage is incomplete, no
result set should be treated as definitive.

<a id="guide-v1-7-indexing-levels"></a>
#### Indexing levels

| Level | Retained local information |
| --- | --- |
| Basic | Path, filename, extension, size, timestamps, metadata, fingerprint, and state |
| Standard | Basic plus bounded extracted text, normalized keywords, and one document-level related-concept representation |
| Deep | Standard plus applicable OCR, bounded summary, selected chunks, and richer staged processing |

Basic is the default. Executables, binary files, generated folders, and archive
contents use conservative defaults. Higher levels can consume more storage and
may depend on Tesseract or an optional local enrichment provider.

<a id="guide-v1-7-progress-and-control"></a>
#### Progress and control

The Search page shows:

- run state and active stage;
- the current filename;
- processed, discovered, and remaining counts;
- complete, skipped, failed, waiting, and retry counts;
- measured processing speed;
- an estimated remaining time only after enough samples exist;
- current and maximum index storage;
- coverage and storage-category breakdown.

Expand **Indexing failures** to inspect the bounded privacy-minimized failure
list. **Open run diagnostics** opens Advanced Diagnostics for the active run;
diagnostic export remains a separate reviewable action.

Pause stops new stages after current safe work finishes. Resume continues the
durable queue. Cancel safely asks discovery and workers to stop, retains
completed work, and records cancellation. Paused and cancelled states survive
restart: Resume is required for paused work, and Retry is required for
cancelled work. Retry failed items requeues eligible transient,
dependency-waiting, or cancelled work within the retry policy.
Prioritize changes which source is claimed first. Remove source deletes only
OpenSorSe-owned index records. Rebuild clears derived index data, keeps source
configuration, and indexes again.

<a id="guide-v1-7-recovery"></a>
#### Recovery

OpenSorSe stores each stage durably. After application exit, operating-system
shutdown, power loss, or process interruption, startup requeues work that had
been running and resumes incomplete discovery in the same run. Completed
compatible stages are reused. If OCR or optional local AI is unavailable,
affected work waits and automatically becomes eligible again after the durable
retry time; other applicable work remains usable.

If the index is malformed, newer than this OpenSorSe version, or cannot be
opened, background indexing fails closed with recovery guidance while the
compatible existing Search path remains available. Pre-migration, manual, and
explicit recovery copies are kept in the managed backup directory, with at
most three retained. **Rebuild background index** preserves the unreadable
database and SQLite sidecars before creating a fresh schema; a newer schema is
never silently downgraded. Watched-folder configurations register their
sources again. A manually added source that existed only in an unreadable
database must be added again. Rebuild is safe because the index contains
derived application data, not copies of source files.

<a id="guide-v1-7-storage-and-privacy"></a>
#### Storage and privacy

Settings control maximum index size, text/OCR bounds, chunks, retention,
retries, concurrency, resource mode, time window, dependencies, generated
folders, binaries, and archives. Maintenance first removes stale/orphaned
derived records and expired operational history, then compacts storage. If the
quota remains exceeded, further work is blocked visibly.

The local index may contain filenames, paths, metadata, extracted text, OCR
text, tags, summaries, and related-concept data. It stays in the current user's
OpenSorSe application-data directory. No full source-file copies are stored.
Diagnostics omit document contents and minimize paths. A remotely configured
Ollama-compatible endpoint can send explicitly enabled AI input off-device;
the default background indexing path does not require it.

All organization behavior remains unchanged: suggestions enter a reviewed
Change Plan, Apply revalidates and journals, and Undo remains conflict-aware.
Indexing never grants file-mutation authority.

</details>

<a id="guide-v1-6"></a>
## v1.6

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/USER_GUIDE_v1.6.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

OpenSorSe 1.6 keeps the complete 1.5 workflow and safety model. The release is
focused on reliability, responsiveness, recovery, accessibility, and
cross-platform verification; it does not add an unattended organizer.

<a id="guide-v1-6-core-workflow"></a>
#### Core workflow

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
[v1.5 User Guide](USER_GUIDE.md#guide-v1-5); every described feature remains
available in v1.6.

<a id="guide-v1-6-what-is-more-reliable-in-16"></a>
#### What is more reliable in 1.6

- Application-owned state is replaced only after a complete, bounded, flushed
  JSON document is ready.
- Concurrent in-process users of the same store are serialized.
- Large duplicate analysis, result projection, and local search use less
  transient memory and observe cancellation throughout the work.
- Watched-folder startup and shutdown tolerate repeated/concurrent lifecycle
  calls and isolate observer failures.
- Critical status and progress surfaces expose screen-reader automation
  metadata.

<a id="guide-v1-6-failure-recovery"></a>
#### Failure recovery

If a save fails, OpenSorSe keeps the previous complete owned document whenever
replacement did not occur. Invalid settings and workflow files are preserved
under their established recovery contracts. Optional rebuildable caches fail
closed and can be rebuilt. Change Plans and Operation Journals remain separate;
an interrupted file operation is inspected against real paths and identities on
recovery.

See [v1.6 Troubleshooting](TROUBLESHOOTING.md#support-v1-6), [Safety and
Privacy](SAFETY_AND_PRIVACY.md), and [the manual checklist](MANUAL_TESTING.md#manual-v1-6).

</details>

<a id="guide-v1-5"></a>
## v1.5

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/USER_GUIDE_v1.5.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

OpenSorSe 1.5 is a local-first desktop application for inspecting selected
folders and applying only explicitly reviewed Change Plans. Linux support is a
preview, not a claim of uniform behavior on every filesystem or desktop.

<a id="guide-v1-5-start-and-storage"></a>
#### Start and storage

Build and launch from source using the [Linux build guide](LINUX_BUILD_AND_LAUNCH.md)
or existing Windows instructions in [Installation](INSTALLATION.md). Windows
continues to use `%LocalAppData%\OpenSorSe`. Linux uses:

- `${XDG_CONFIG_HOME:-~/.config}/opensorse` for settings;
- `${XDG_DATA_HOME:-~/.local/share}/opensorse` for durable data and plugins;
- `${XDG_STATE_HOME:-~/.local/state}/opensorse` for journals and logs;
- `${XDG_CACHE_HOME:-~/.cache}/opensorse` for reproducible caches.

OpenSorSe creates only its owned directories and never migrates or deletes an
existing Windows data directory silently.

<a id="guide-v1-5-platform-diagnostics"></a>
#### Platform diagnostics

Settings contains **Platform diagnostics**. It lists OS/runtime/architecture,
owned locations, OCR, identity, watcher, plugin, execution, desktop, and
packaging states. **Copy platform report** creates a human-readable bug report
without credentials or document contents. Limited and unavailable states
include their reason.

<a id="guide-v1-5-safe-file-operations"></a>
#### Safe file operations

Scanning, watchers, workflows, plugins, OCR, and AI may analyze or propose.
They cannot approve or apply changes. Rename, move, and create-directory actions
still require a reviewed Change Plan, validation, separate confirmation,
journal write, execution, and resulting-state verification. Destinations are
never silently overwritten. Moves are limited to a verified same filesystem;
links and unverified mount boundaries fail closed.

<a id="guide-v1-5-workflows"></a>
#### Workflows

Recipes carry one filename policy:

- `Portable` (default) uses conservative Windows/Linux interchange rules.
- `WindowsCompatible` retains Windows filename rules on every host.
- `CurrentPlatform` permits names valid on the active host, so a Linux export
  may later be invalid on Windows.

Import preserves the declared policy and does not silently rewrite it. Preview
the recipe after importing on another platform.

<a id="guide-v1-5-plugins-and-external-tools"></a>
#### Plugins and external tools

Managed plugins remain in-process and are not a security sandbox. A plugin with
native dependencies must declare supported runtime identifiers such as
`win-x64` or `linux-x64`; a mismatch prevents loading. Tesseract remains an
external installation. Configure an absolute path or leave it blank for safe
`PATH` discovery. OpenSorSe never invokes a shell command string or installs
language data.

<a id="guide-v1-5-watchers-and-links"></a>
#### Watchers and links

Watcher events are hints. Debounce, stability checks, bounded queues, startup
and periodic reconciliation remain authoritative. Linux inotify descriptor
limits, overflow, mount loss, and editor replacement patterns can cause delay
or a visible reconciliation warning. Symbolic links/reparse points are not
traversed outside the approved root and are not used to mutate their targets.

See [Troubleshooting](TROUBLESHOOTING.md#support-v1-5) and the
[platform matrix](PLATFORM_COMPATIBILITY_MATRIX.md).

</details>

<a id="guide-v1-4"></a>
## v1.4

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/USER_GUIDE_v1.4.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

This guide covers the v1.4 Plugin Foundation and Extension SDK. Existing scan,
catalogue, watched-folder, workflow, Change Plan, review, Apply, Operation
History, recovery, and Undo behavior remains unchanged.

<a id="guide-v1-4-inspect-plugins"></a>
#### Inspect plugins

Open **Settings > Plugins**. Each row identifies the plugin, version, publisher,
license, origin, lifecycle state, compatibility, integrity, dependencies,
contributions, requested capabilities, granted capabilities, errors,
quarantine, and whether restart is required.

Built-in reference plugins are active by default. External plugins are disabled
after discovery or installation until you review them and explicitly enable
their requested capabilities.

<a id="guide-v1-4-install-a-local-package"></a>
#### Install a local package

1. Obtain a local `.zip` package from a publisher you trust.
2. In **Settings > Plugins**, enter the package path and choose **Install**.
3. Review the discovered publisher, source, version, requested capabilities,
   integrity state, dependencies, and contributions.
4. Choose **Enable and grant requested capabilities** only if you accept them.

Installation is local only. OpenSorSe does not download packages, contact a
marketplace, or automatically update plugins. Packages with invalid manifests,
unsafe archive paths, unexpected native binaries, missing entry assemblies, or
conflicting versions are rejected without replacing the active installation.

<a id="guide-v1-4-upgrade-disable-and-remove"></a>
#### Upgrade, disable, and remove

- **Upgrade** validates and stages the new local ZIP before switching versions;
  the previous version is preserved for rollback.
- **Disable** stops contribution use. A restart may be required to release an
  in-process assembly.
- **Remove** requires confirmation and is blocked while a workflow profile,
  recipe, or watched configuration depends on that plugin version.

OpenSorSe removes only its controlled installed-version directory. It does not
remove unrelated files, user data, workflow history, or operation history.

<a id="guide-v1-4-workflows-and-recipes"></a>
#### Workflows and recipes

Profiles and recipes reference a plugin, exact version, and contribution ID.
Resolution snapshots preserve that identity in history. If a required plugin
is disabled, missing, incompatible, changed, quarantined, or the wrong version,
the workflow fails closed with:

> Plugin capability unavailable — review workflow profile

There is no silent fallback to a similarly named contribution. Watched folders
also stop that workflow instead of applying a different recipe.

Plugin recipe fields use the form `plugin.<plugin-id>.<field-id>`. Values remain
subject to the normal template sanitization, root confinement, collision,
preview, and Change Plan validation rules. Plugin outputs do not authorize a
file operation.

<a id="guide-v1-4-diagnostics-and-privacy"></a>
#### Diagnostics and privacy

Use **Export diagnostics** to save bounded, redacted plugin lifecycle and
resolution events. Diagnostics omit file contents, credentials, and AI prompt
payloads. Review the file before sharing it.

A capability grant describes intended host-mediated access; it is not an OS
sandbox. An in-process plugin runs with the permissions of OpenSorSe. Install
only trusted code, grant the smallest needed set of capabilities, and disable a
plugin whose installed hash changes unexpectedly.

For problems, see [Troubleshooting](TROUBLESHOOTING.md#support-v1-4).

</details>

<a id="guide-v1-3"></a>
## v1.3

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/USER_GUIDE_v1.3.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

> Workflow profiles automate configuration and analysis, not approval or file modification.

<a id="guide-v1-3-workflows"></a>
#### Workflows

Open **Workflows** from primary navigation. The first tab lists profiles; the second lists recipes; the third transfers versioned JSON and exports workflow diagnostics.

Search matches names and descriptions. Filters cover file type, built-in/user-created origin, AI, OCR, duplicate detection, and archived state. Usage text reports watched folders, recent saved scans, and profiles that reference a recipe.

Canonical built-ins are read-only. Choose **Duplicate** before editing one. User items can be renamed by changing the name and saving, enabled/disabled, archived/restored, exported, or deleted when unreferenced.

<a id="guide-v1-3-built-in-profiles"></a>
#### Built-in profiles

- **General Documents:** balanced document metadata/text, duplicates, classification/rules, and policy-gated optional AI. No recipe is attached by default.
- **Invoices and Receipts:** PDF/image processing, OCR when necessary, duplicates, and invoice recipe proposals. When no reviewed vendor value is available, the built-in recipe uses the visible `UnknownVendor` fallback rather than guessing.
- **Photos:** metadata-first photo handling, filesystem-creation-date grouping, duplicates, no OCR or AI by default.
- **Downloads Cleanup:** conservative completed-download classification and category recipe; no deletion.
- **Minimal Local Processing:** metadata-only, no OCR/AI, and low-cost analysis.

<a id="guide-v1-3-profiles"></a>
#### Profiles

The editor separates Files, Extraction, Classification/Duplicates, AI, Sorting recipes, watched behavior, notifications, uncertainty, and Change Plan options.

A profile selects zero or more persistent recipes. A watched folder may use only recipes permitted by its profile. Saving validates dependencies and contradictions and increments the profile revision.

Effective precedence, from broadest to narrowest, is:

1. application safety and capability settings;
2. canonical or user profile;
3. watched-folder supported constraints;
4. one-time manual-scan constraints;
5. ordered recipe and deterministic-rule selection.

Later levels may narrow global/profile capabilities; they cannot turn on OCR, AI, duplicate analysis, or Change Plans that a broader safety gate disabled.

Incremental behavior is effective at runtime: profiles may reanalyse changed items only or all current items, preserve or clear unchanged analysis, and choose whether a full watcher reconciliation removes missing catalogue entries. Disabling full or incremental scanning makes that profile unavailable for the corresponding scan mode instead of selecting another profile.

<a id="guide-v1-3-recipes-and-template-syntax"></a>
#### Recipes and template syntax

Recipes define applicability, priority, a filename template, a relative destination template, fields/fallbacks, normalization, date format, length, collision, and uncertainty behavior.

Examples:

```text
{date:yyyy-MM-dd}_{vendor}_{documentType}_{amount}
Invoices/{date:yyyy}/{vendor}
```

Supported fields are `originalName`, `extension`, `date`, `createdDate`, `modifiedDate`, `vendor`, `documentType`, `title`, `author`, `invoiceNumber`, `amount`, `currency`, `project`, `category`, `captureYear`, `captureMonth`, and `ruleGenerated`.

Only date fields accept a date format. No function calls, operators, nested expressions, scripts, environment expansion, or shell syntax are supported.

Deterministic scan output currently supplies original name, extension, filesystem dates, and classification/category fields. Persistent recipe rules run in explicit priority order through the existing rule engine; they do not execute expressions or inject arbitrary code-backed fields.

Choose **Preview recipe** with representative metadata. The preview reports original path, filename/destination, values, missing/fallback fields, sanitization, conflicts, warnings, and AI-derived status. Preview never creates a directory or changes a file.

<a id="guide-v1-3-manual-scans"></a>
#### Manual scans

On **Scan**, select a profile and review its file types, OCR, duplicate, AI, recipe, and processing summary. One-time settings can only narrow the saved profile. **Save as new profile** duplicates and persists the adjusted settings; it does not mutate the original.

The completed scan stores the effective revision snapshot. A valid recipe may create a Change Plan and open Review Changes. It does not apply it.

<a id="guide-v1-3-watched-folders"></a>
#### Watched folders

Choose one active profile and zero or more displayed recipes. Save the configuration to trigger reconciliation. A profile edit triggers reconciliation with the new revision.

If a dependency becomes missing, archived, disabled, incompatible, or still uses v1.2's `current` recipe, processing stops in **Profile unavailable — review configuration**. Choose a deliberate replacement; OpenSorSe will not substitute an unrelated profile.

<a id="guide-v1-3-import-and-export"></a>
#### Import and export

Export places versioned JSON in the transfer area. It includes type, application/schema version, item identity, description, configuration, and dependency IDs. It excludes provider settings, endpoints, credentials, document contents, logs, and secrets.

Import validates size/depth/schema/content type, IDs/names, capabilities, dependencies, and templates. Choose import as copy, confirmed replacement of a user item, skip, or cancel. Canonical built-ins are never overwritten.

<a id="guide-v1-3-change-plans-and-ai"></a>
#### Change Plans and AI

Profile/recipe proposals use Review Changes, live preflight, explicit approval, Apply confirmation, the Operation Journal, recovery, rollback, Operation History, and Undo from v1.1.

AI is never implied by choosing a profile. Global AI, an installed selected model, the profile policy, local watched/manual permission, capability compatibility, and eligible item are all required. AI-derived fields remain visibly AI-assisted.

</details>

<a id="guide-v1-2"></a>
## v1.2

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/USER_GUIDE_v1.2.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="guide-v1-2-safety-boundary"></a>
#### Safety boundary

> Watched folders automate detection and analysis, not file modification.

Watching follows:

`automatic detection and analysis → manual review → explicit approval → safe execution`

No watched-folder event can automatically rename, move, delete, deduplicate, or restructure a real folder. Suggestions become ordinary v1.1 Change Plans and must be reviewed, approved, revalidated, and explicitly applied. Existing Operation History, rollback, recovery, and Undo remain responsible for attempted file changes.

<a id="guide-v1-2-add-and-manage-a-watched-folder"></a>
#### Add and manage a watched folder

1. Open **Watched Folders**.
2. Enter an absolute folder path and optional display name.
3. Select **Add watched folder**.
4. Open the configuration and review subfolder, analysis, AI, notification, quiet-period, size-limit, scan-profile, sorting-recipe, ignored-path, and ignore-pattern settings.
5. Select **Save watched-folder settings** after editing.

AI analysis is disabled for a new watched folder unless explicitly enabled there. The global AI switch, compatible capability, endpoint, and selected available Ollama model must also be ready before any request can run.

Folder-structure AI uses requests of at most 12 files and processes at most 120 affected items in one scan/retry cycle. A larger backlog remains explicitly pending for a later retry, keeping startup and offline reconciliation cancellable and bounded.

The sorting recipe value `current` means the rules most recently saved in the current session's **Rule Editor**. v1.2 does not yet include a persistent named recipe library; an unrecognized recipe ID safely resolves to no rules, and the current-session rules must be saved again after restart.

The scan-profile field is persisted, but the shipped processing behavior is the existing `default` profile. v1.2 does not include a separate scan-profile library/editor.

Use:

- **Pause** to stop watching while preserving the configuration and catalogue.
- **Resume** to restart watching and reconcile changes made while paused.
- **Scan changes now** to enumerate current metadata and incrementally analyse only differences.
- **Full reconciliation** to compare the saved catalogue with the real folder after suspected missed events.
- **Review suggestions** to open the newest non-mutating watched-folder Change Plan.
- **Retry failed AI analysis** to retry only catalogue items marked pending or failed, without repeating completed AI or successful deterministic catalogue work.
- **Open folder** to ask the operating system to show the selected root.
- **Remove from watch list** to remove only the configuration. The real folder, its files, dedicated watched catalogue, and grouped activity history are not deleted. The separate opt-in Saved scans catalogue is not used or evicted by watcher updates.

<a id="guide-v1-2-incremental-scanning"></a>
#### Incremental scanning

Watcher events are hints. OpenSorSe waits for the configured quiet period, groups duplicates and bursts, rejects paths outside the root, applies ignore rules, and probes the actual filesystem.

For a changed file, OpenSorSe compares stable identity, path, size, timestamps, and attributes. New or content-changing files wait for two stable observations. Unchanged files retain previous analysis. Metadata-only changes do not trigger hashing, OCR, or content extraction. Rename and move changes retain analysis when identity and content evidence are unchanged. Deleted files are removed from the current watched catalogue only after reconciliation evidence; the history is preserved.

Exact-duplicate groups are recalculated from retained and new hashes without re-reading unchanged file content. Deterministic rules run only for affected files. Suggestions are recorded with deterministic or AI provenance.

<a id="guide-v1-2-full-reconciliation-restart-pause-and-offline-storage"></a>
#### Full reconciliation, restart, pause, and offline storage

OpenSorSe reconciles enabled watched folders at startup before treating the saved catalogue as current. This detects additions, edits, removals, moves, and renames that occurred while the application was closed.

Resume and reconnect also queue reconciliation. Watcher overflow is visible and requires reconciliation. While OpenSorSe remains running it requests a full metadata reconciliation at least every 24 hours. Manual reconciliation is always available.

A missing, disconnected, or inaccessible root remains configured. OpenSorSe does not erase its catalogue or history. When the exact root path returns, watching restarts and a reconnect reconciliation is queued. If the root itself was renamed, edit by removing the old configuration and adding the new root; v1.2 does not search the drive and guess.

<a id="guide-v1-2-ignore-rules"></a>
#### Ignore rules

Exact ignored paths can be absolute or relative to the watched root. A directory ignore excludes that directory and its descendants. Patterns use `*`, `?`, and `**`; examples are `*.bak` and `Archive/**`.

Built-in visible policy excludes:

- `~$*`, `*.tmp`, `*.temp`, `*.part`, `*.partial`;
- `*.crdownload`, `*.download`, `*.opdownload`;
- `*.swp`, `*.swo`, `.~lock.*`;
- `.DS_Store`, `Thumbs.db`, and `desktop.ini`;
- `.opensorse` and OpenSorSe internal store filenames;
- reparse points;
- hidden files when **Ignore hidden files** is selected;
- files above the configured maximum size.

Canonical root checks prevent an ignore rule from affecting unrelated sibling paths. Ignored items are never passed to optional AI.

<a id="guide-v1-2-quiet-period-and-unstable-files"></a>
#### Quiet period and unstable files

The default quiet period is two seconds and may be set from 0.25 to 300 seconds. It groups multi-stage saves and copy bursts.

Before metadata extraction, OCR, hashing, or AI, a changed file must remain the same size and modification time across two observations and be readable. OpenSorSe retries three observations in the immediate batch. A still-changing or locked file is marked deferred; the scan summary remains incomplete and reconciliation remains required. A later reconciliation retries deferred analysis even if the final metadata now matches the deferred observation.

<a id="guide-v1-2-activity-and-notifications"></a>
#### Activity and notifications

The page reports availability, watcher state, queue count, processing state, last detected change, last successful scan, last reconciliation, latest grouped summary, pending Change Plans, and errors.

Activity history stores meaningful batches such as watcher start/pause/resume, availability, overflow, batch detection, scan/reconciliation start and completion, deferral, AI attempt, Change Plan creation, manual scan, and configuration changes. Raw operating-system events are not written one by one.

Notification level can be **None**, **Errors only**, or **Summaries**, with separate choices for plan-ready and unavailable-folder notices. Notices are grouped and deduplicated. They say that suggestions or a plan are ready and never claim that files were organized when only analysis occurred.

<a id="guide-v1-2-review-and-apply-suggestions"></a>
#### Review and apply suggestions

1. Select **Review suggestions**.
2. Inspect every proposed rename, move, or directory action.
3. Approve or reject actions individually; edit when appropriate.
4. Select **Validate Plan**.
5. Select **Apply Plan** only after reviewing the final summary and explicit confirmation.
6. Inspect **Operation History**.
7. Use existing conflict-aware **Undo** only when offered.

OpenSorSe-generated file events are matched to journal actions and verified resulting identities. They update the watched catalogue without generating a recursive plan or repeated AI analysis.

</details>

<a id="guide-v1-1"></a>
## v1.1

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/USER_GUIDE_v1.1.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="guide-v1-1-preview-first-organization"></a>
#### Preview-first organization

1. Scan a selected folder or open a current result set.
2. Generate a rename or folder-structure suggestion, or prepare a deterministic folder plan.
3. Accepting a suggestion creates a **Change Plan**. No source file changes at this point.
4. Open **Review Changes** and inspect current path, proposed path, action type, source, reason, warnings, conflicts, and approval state.
5. Use **Approve all safe**, **Deselect all**, or approve/reject one action. Edit a proposed filename or destination when needed.
6. Filter by action type or warning/conflict state. Review approved, rejected, invalid, and conflicting counts.
7. Select **Validate Plan**. Fix or reject blocking conflicts.
8. Select **Apply Plan**, inspect the final rename/move/folder/excluded/warning/overwrite summary, then confirm.
9. Open **Operation History** to inspect the verified result, rollback information, or debugging report.
10. Use **Undo** only when offered and confirm it. OpenSorSe revalidates the current files before restoring anything.

The Apply button stays disabled until at least one approved action has passed the latest validation and no approved blocking conflict remains. Editing or changing approval invalidates the previous validation.

<a id="guide-v1-1-supported-actions"></a>
#### Supported actions

- Rename a regular file within its current directory.
- Move a regular file to another path under the selected root.
- Create a required directory under the selected root.

OpenSorSe never overwrites an existing file. It does not append numeric suffixes or replace a destination automatically. Permanent deletion is not supported in v1.1.

<a id="guide-v1-1-conflicts-and-stale-plans"></a>
#### Conflicts and stale plans

Validation reports user-safe categories such as source missing, source renamed externally, source changed, source locked, invalid filename, destination occupied, duplicate destination, conflicting source actions, missing parent, linked/out-of-root destination, execution-order conflict, and stale scan.

Revalidate without rerunning AI analysis. If the filesystem changed after approval, OpenSorSe blocks Apply until the revised state is reviewed. AI failures or retries do not alter an existing plan or any file.

<a id="guide-v1-1-undo-guarantees-and-limits"></a>
#### Undo guarantees and limits

A successful rename or move is undoable while the resulting file still exists, remains the same file, was not materially modified, and its original path is free. A directory is removed only when OpenSorSe created it and it is still empty.

Undo is blocked when restoration could overwrite newer data, when another OpenSorSe operation depends on the current path, or when identity/current-state checks fail. Safe actions in the same Undo request may still complete; the journal then reports a partial Undo.

OpenSorSe provides transaction-like safeguards, not a true filesystem transaction. Storage, hardware, permission, power, or external-process failures can require manual recovery.

<a id="guide-v1-1-operation-history-and-reports"></a>
#### Operation History and reports

**Operation History** persists across restarts. Select an operation to see timestamps, status, root, succeeded/failed/skipped/rolled-back counts, paths, suggestion source, validation/execution state, errors, rollback, Undo, and optional AI model/request correlation IDs.

Use **Copy operation report** to copy a bounded human-readable report. Inspect it before sharing because it contains file paths. Reports do not include file contents, extracted text, AI prompts, credentials, or raw model responses.

By default the two v1.1 stores are:

- `%LOCALAPPDATA%\OpenSorSe\change-plans.json`
- `%LOCALAPPDATA%\OpenSorSe\operation-journal.json`

<a id="guide-v1-1-interrupted-operations"></a>
#### Interrupted Operations

On startup, OpenSorSe inspects operations left Pending or Running. It marks them **Interrupted** and records only states supported by actual path and identity evidence. Ambiguous actions require manual review; OpenSorSe does not guess that a move or directory creation succeeded.

> OpenSorSe does not apply AI-generated or bulk file changes without a user-reviewed Change Plan. Supported file operations are recorded in the Operation Journal and are reversible unless later external changes make automatic restoration unsafe.

</details>
