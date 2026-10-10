# Manual testing

This is the canonical manual-testing procedure and historical evidence ledger.
Use disposable synthetic data, preserve original files, and record actual
observations in the release's testing issue. An automated check, model review,
publication, or unchecked procedure never establishes a human pass.

## Current release target

The published testing candidate is **v3.0.0-rc.1**. [Issue #53](https://github.com/nishdel/OmniSorSe/issues/53)
owns live observations; the [official prerelease](https://github.com/nishdel/OmniSorSe/releases/tag/v3.0.0-rc.1)
owns exact assets, checksums and publication state. [Release Status](RELEASE_STATUS.md)
owns readiness. The repository baseline has **24 human scenarios Not run**.
Use [the current release-specific checks](#manual-v3-0) below. Earlier packages
are historical fixtures; their observations never transfer to this candidate.

## Status definitions

| Status | Meaning |
| --- | --- |
| Pass | All expected results were observed on the recorded package and environment. |
| Fail | An observed result disagreed with an expectation; retain evidence and a defect reference. |
| Partial | Only part was observed; not a pass. The issue vocabulary is `Partially / Not tested` unless a failure or blocker was observed. |
| Blocked | Execution could not proceed; record the actual cause and environment. |
| Not run | Planned/unperformed; equivalent to `No / Not tested` in the shared issue template. |
| Not recorded | Historical evidence is absent or lacks detail; do not infer that execution occurred. |
| N/A | Explicitly excluded with a reason; never counts as a pass. |

Historical checked boxes retain their original **automated**, controlled-native,
or interactive scope. Unchecked historical boxes stay unchecked: some older
procedures do not distinguish Not run from unavailable or unsuccessful work.
The v1.6 maintainer attestation has no per-environment results and does not
convert its unchecked procedure into individual passes.

## Reusable core release checklist

Start every package with unrun rows. Use the detailed core definitions below:

| ID | Reusable check | Initial state |
| --- | --- | --- |
| OS-M01 | [Launch the exact identified package](#release-testing-procedure-os-m01) | Not run |
| OS-M02 | [Scan four disposable files](#release-testing-procedure-os-m02) | Not run |
| OS-M03 | [Find a known filename](#release-testing-procedure-os-m03) | Not run |
| OS-M04 | [Review a move without applying](#release-testing-procedure-os-m04) | Not run |
| OS-M05 | [Apply one move and Undo](#release-testing-procedure-os-m05) | Not run |
| OS-M06 | [Inspect an exact-duplicate pair](#release-testing-procedure-os-m06) | Not run |
| OS-M07 | [Keep a setting after restart](#release-testing-procedure-os-m07) | Not run |
| OS-M08 | [Keyboard and visible controls](#release-testing-procedure-os-m08) | Not run |

Select conditional provider, migration, scale, privacy, recovery and platform
checks explicitly. The detailed procedure retains its v2.4/v2.13 setup labels;
for v3 use its candidate identity and release-specific expectations below.

<a id="manual-v3-0"></a>
## Current release-specific checks — v3.0.0-rc.1

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v3.0.md). This prepared baseline belongs only to the current testing candidate; live observations remain in issue #53.

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

The core definitions in [Manual release testing](MANUAL_TESTING.md#release-testing-procedure)
remain applicable. Record expected versus observed behavior, failures and actual
environment in the live issue. A changed package starts a new table with all
rows Not run and retains the previous evidence.

Repeatable synthetic inputs and quality criteria live in the opt-in
[real-model Search benchmark](../eng/benchmarks/VectorSearch/README.md).
Its automated results are separate from all 24 human rows above.


<a id="release-testing-procedure"></a>
## Shared execution and recording procedure

Repository files define tests. **One GitHub “Release manual testing” issue per release owns execution results.** [Release index and prepared issue bodies](release-testing/README.md) identify actual published releases; a draft is preparation, never a second live ledger. Preserve historical results and their exact Git/tag snapshots; update living procedures in this canonical document.

<a id="release-testing-procedure-start-testing-here"></a>
## Start testing here

For **v3.0.0-rc.1**, use the [version-specific checklist](MANUAL_TESTING.md#manual-v3-0)
and [live issue #53](https://github.com/nishdel/OmniSorSe/issues/53). It uses the
per-user installer and covers progressive enrichment, Organize and storage in
addition to these existing core scenarios. Package identity is assigned only
after publication; human rows remain Not run. The procedures below retain their
stated v2.4/v2.13 package identities for historical testing.

1. Choose the exact record in the [index](release-testing/README.md). Latest stable and published prerelease are different records. The next-candidate draft has **no assigned package**; do not substitute another build. No old package was downloaded, installed or retested to prepare these files.
2. Use a disposable **Windows x64 OS account or VM**. Portable builds still use `%LOCALAPPDATA%\OpenSorSe`; a new extraction folder does not isolate your personal profile. Record OS/build, locale, filesystem, display scale, dependencies and a synthetic environment ID in the issue. The checklist owner is assigned before execution.
3. For stable v2.4.0 choose `OmniSorSe-v2.4.0-win-x64.zip` and its `OmniSorSe-v2.4.0-SHA256SUMS.txt` from the [stable release](https://github.com/nishdel/OmniSorSe/releases/tag/v2.4.0). For published v2.13.0-rc choose `OmniSorSe-v2.13.0-rc-win-x64.zip` and its matching SHA256SUMS from the [prerelease](https://github.com/nishdel/OmniSorSe/releases/tag/v2.13.0-rc). Verify the **complete** hash against that record and checksum file. In the ZIP's folder, type `powershell` in File Explorer's address bar and run the corresponding command:

   ```powershell
   Get-FileHash .\OmniSorSe-v2.4.0-win-x64.zip -Algorithm SHA256
   # OR, for the published prerelease:
   Get-FileHash .\OmniSorSe-v2.13.0-rc-win-x64.zip -Algorithm SHA256
   ```

4. Right-click the chosen ZIP → **Extract All**, keep the files together, double-click **OmniSorSe.exe**. Both ZIPs are self-contained; a separate .NET runtime is unnecessary. Record any OS warning and blocked launch. The owner decides on trust overrides separately; do not disable system security globally. Verified launch/setup references: [v2.4 installation at its tag](https://github.com/nishdel/OmniSorSe/blob/v2.4.0/docs/INSTALLATION.md) and [current Installation](INSTALLATION.md). v1.0–v2.3 retain the `OpenSorSe.exe` name.
5. In the disposable account create the four-file sample below, or have the owner prepare exactly that tree. Keep AI, OCR, media and watchers off for core checks. In Settings enable **Enable local Search** and **Extract searchable information and build Search coverage in the background**, then **Save** for v2.4/current. Do not enable optional provider stages.
6. Perform **OS-M01 → M02 → M06 → M04 → M05 → M03 → M07 → M08**. Read the short expected result before starting. If an expected control is absent, record the exact version/control as Blocked; do not guess another action. Historical v1.0 has its own smaller plan and labels.
7. Enter observations in the issue table: actual tester/date, expected versus observed behavior, and a small screenshot or note. A failure uses **Pending** or an existing linked defect. Redact private paths, content and credentials. A transcribing agent must preserve the reporter's uncertainty and never invent observations.

<a id="release-testing-procedure-disposable-sample-and-reset"></a>
### Disposable sample and reset

In File Explorer create a new empty folder `ReleaseSample` in the disposable account. In it create `lunar-sample.txt` containing `Lunar sample note`, `copy-a.txt` containing `Identical duplicate sample`, and an exact copy named `copy-b.txt`. Create subfolder `Notes` with `readme.txt` containing `Separate nested note`. Save as plain text; ensure Windows does not add a second `.txt` extension. There are **four files, one exact-duplicate pair**. Search uses the filename `lunar-sample`, so it does not depend on TXT content extraction.

Optional copy/paste creation in PowerShell (new path only; it fails if the folder exists):

```powershell
$sampleRoot = Join-Path ([Environment]::GetFolderPath('Desktop')) 'ReleaseSample'
if (Test-Path -LiteralPath $sampleRoot) { throw 'Use a new empty sample location.' }
New-Item -ItemType Directory -Path $sampleRoot | Out-Null
New-Item -ItemType Directory -Path (Join-Path $sampleRoot 'Notes') | Out-Null
Set-Content -LiteralPath (Join-Path $sampleRoot 'lunar-sample.txt') -Value 'Lunar sample note' -Encoding UTF8
Set-Content -LiteralPath (Join-Path $sampleRoot 'copy-a.txt') -Value 'Identical duplicate sample' -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $sampleRoot 'copy-a.txt') -Destination (Join-Path $sampleRoot 'copy-b.txt')
Set-Content -LiteralPath (Join-Path $sampleRoot 'Notes/readme.txt') -Value 'Separate nested note' -Encoding UTF8
```

After a run: preserve failed attempts/screenshots, close OmniSorSe, and revert the **test VM snapshot** or use a new disposable OS account and freshly created sample. Do not clear, downgrade or delete your personal OpenSorSe profile. For a repeat in the same test account, finish Undo first and verify the four original filenames/content; a fresh account is still required when checking first launch or another package. Do not delete a whole parent directory as cleanup.

<a id="release-testing-procedure-recording-results"></a>
## Recording results

Use exactly:

ID | Test | Expected result | Tested | Result | Issue registered | Tester / date | Evidence / notes

- Tested: **No / Partially / Yes / N/A**. Result: **Not tested / Pass / Fail / Blocked / N/A**. Historical absence of evidence alone uses **Unknown / Not recorded**.
- Issue registered: **— / Not needed / Pending / linked issue number**. Newly planned rows start **No / Not tested / —**. Pass requires **Yes** and observation of **all** required expected results. Partial observation is Partially / Not tested, or Fail/Blocked if observed.
- Blocked and N/A require a reason; N/A never counts as a pass. Fail requires a linked defect or Pending. Reuse existing defects. Closing a defect does not establish a successful manual retest.
- Record the actual test date and person. A report's creation date is not automatically the execution date. Historical missing artifact/hash/environment/person/date is **Unavailable / Not recorded**, never inferred from CI or publication. Any later historical test is labelled **Retrospective**, with its real date.
- Above the table identify release, candidate/package, source commit, SHA-256 (or Unavailable), definition revision, environment and checklist owner. Summary counts the Result cells: **Passed / Failed / Blocked / Not tested / N/A / Not recorded**. Count selected conditional/environment rows too.
- On a changed package, first preserve the previous identity, entire table, failures and evidence verbatim in a dated issue comment. List changed behavior and affected IDs. Start a new candidate table with **all rows No / Not tested**; designate impacted and package-launch tests for first repetition. Prior passes remain evidence for the prior package only. If source is unchanged but packaging changes, launch/install/platform checks still repeat. Never silently transfer passes.
- Before a retest, append the failed/blocked attempt and evidence to a dated comment; then update the current row. Separate OS/device rows use an ID plus environment suffix. Recompute counts after every change.
- Keep automated checks and owner decisions (scope, signing, provider permission, publication) in separate paragraphs. Neither can pass a human row. The table is a starting overview, not release approval.

<a id="release-testing-procedure-core-scenarios"></a>
## Core scenarios

These definitions apply to **v2.4.0 and v2.13.0-rc** unless specified. Each has one small goal. They do not certify optional features. The historical drafts link procedures from their own release.

<a id="release-testing-procedure-os-m01"></a>
<a id="release-testing-procedure-os-m01-launch-the-exact-package"></a>
### OS-M01: Launch the exact package

**Prerequisite:** Verified ZIP and disposable account from Start testing here.

1. Extract all files and open `OmniSorSe.exe`.
2. Open **About** in the sidebar footer. Check release version against the issue. For v2.13 open extracted `OmniSorSe.build.json` in Notepad and match `sourceRevision`/`productVersion` with the issue. Do not require that newer manifest in v2.4.
3. Return **Home**; confirm the first-scan action is available. v2.13 also presents Scan → Review → Organize; do not require that newer hierarchy in v2.4.

**Expected:** Correct version opens and first-scan action is usable.

**Cleanup:** Close About; leave the test app open for M02.

<a id="release-testing-procedure-os-m02"></a>
<a id="release-testing-procedure-os-m02-scan-four-disposable-files"></a>
### OS-M02: Scan four disposable files

**Prerequisite:** Untouched four-file sample, AI off.

1. On Home select **Scan a folder** (or **Scan folder**), then **Browse...**. Select `ReleaseSample`; Browse adds it to the roots list automatically. Confirm only that root is listed, then select **Start scan**. If entering a path by hand instead, select **Add folder** first.
2. Wait for completion; open **Files** (v2.4) or **Review** (v2.13).
3. Confirm all four expected filenames, including `Notes/readme.txt`, are listed. In File Explorer confirm names and text remain unchanged.

**Expected:** Four sample files are found; source files stay unchanged.

**Cleanup:** Leave the scan open. Do not add any real folders.

<a id="release-testing-procedure-os-m03"></a>
<a id="release-testing-procedure-os-m03-find-a-known-filename"></a>
### OS-M03: Find a known filename

**Prerequisite:** M02; local Search/background coverage enabled as above. Wait for sample indexing to complete.

1. Open **Search**, enter `lunar-sample`, then select **Search**.
2. Select `lunar-sample.txt` and inspect its path; it must belong to this sample.
3. Clear the query. If indexing is incomplete, record that state instead of a false Pass.

**Expected:** The known file is found in the selected sample.

**Cleanup:** Clear query/filters. Saved searches are a separate conditional check.

<a id="release-testing-procedure-os-m04"></a>
<a id="release-testing-procedure-os-m04-review-a-proposed-move-without-applying"></a>
### OS-M04: Review a proposed move without applying

**Prerequisite:** M06 found the two-file duplicate group; neither file changed.

1. In **Duplicates**, open the group and tick only `copy-b.txt`; keep `copy-a.txt` unticked. Select **Review selected duplicates**.
2. Inspect the proposed recovery move, any required **CreateDirectory** actions and warnings. Every source/destination must be within the disposable root; a fresh sample needs the recovery parent folders created. v2.13 also shows origin/purpose/action counts.
3. Try **Approve all safe** then **Deselect all** (v2.4), or **Approve all eligible** then **Exclude all** (v2.13); then **Approve** the eligible sample move **and its required destination-folder actions**. Select each action to inspect it, or use the bulk approval after checking that every action belongs to this sample. Do **not** click Apply yet. Check File Explorer: both originals must still exist unchanged.

**Expected:** A scoped proposal is reviewable; approval alone changes no files.

**Cleanup:** Leave this approved sample plan for M05, or navigate away without applying. Unexplained conflicts mean Blocked, not a guessed workaround.

<a id="release-testing-procedure-os-m05"></a>
<a id="release-testing-procedure-os-m05-apply-one-move-and-undo"></a>
### OS-M05: Apply one move and Undo

**Prerequisite:** M04; Windows filesystem supports the required safety checks. All plan actions stay under the disposable root.

1. Select **Validate Plan**, **Apply Plan**, then **Confirm Apply Plan** only for the reviewed recovery move and its required sample-folder actions.
2. Check `copy-a.txt` remains and `copy-b.txt` moved to the exact recovery destination shown in the plan (File Explorer → View → Hidden items if needed). Its text must be unchanged. This is recoverable movement, not permanent deletion or reclaimed space.
3. Select **Undo** in Review Changes, or **Operation History → Undo → Confirm Undo**. Verify both original filenames/text are restored in File Explorer. Re-scan to refresh any removed logical row; Undo need not recreate that row immediately.

**Expected:** Only the reviewed copy moves; Undo restores its name/content.

**Cleanup:** Verify the four-file baseline. On failure preserve state/evidence for the owner instead of manually overwriting files. Unsupported-platform refusal is conditional C09, with an explicit M05 N/A reason for that platform.

<a id="release-testing-procedure-os-m06"></a>
<a id="release-testing-procedure-os-m06-inspect-an-exact-duplicate-pair"></a>
### OS-M06: Inspect an exact-duplicate pair

**Prerequisite:** M02 on the untouched sample.

1. Open **Duplicates** and select the group containing `copy-a.txt` and `copy-b.txt`.
2. Check only these two files are in the group and both paths are in the sample.
3. Close/reopen the details and confirm neither file changed. Leave at least one keeper if proceeding to M04.

**Expected:** The identical pair is grouped without changing either file.

**Cleanup:** Clear selections unless continuing directly to M04. Multi-group selection is conditional C01.

<a id="release-testing-procedure-os-m07"></a>
<a id="release-testing-procedure-os-m07-keep-a-setting-after-restart"></a>
### OS-M07: Keep a setting after restart

**Prerequisite:** Disposable profile; note initial **Show advanced features** value.

1. Open **Settings**, change **Show advanced features**, then select **Save**.
2. Close the app normally and reopen the same extracted executable in the same account.
3. Open Settings and confirm the chosen value persists.

**Expected:** The saved setting survives a normal restart.

**Cleanup:** Restore the original value and Save. Do not use Reset on another person's profile.

<a id="release-testing-procedure-os-m08"></a>
<a id="release-testing-procedure-os-m08-keyboard-and-visible-controls"></a>
### OS-M08: Keyboard and visible controls

**Prerequisite:** Keyboard; record one tested display scale/window size per row. Owner selects additional supported scales when layout changed.

1. With Tab/Shift+Tab and Enter/Space, visit Home, Files/Review, Search and Settings; observe a visible focus indicator and reachable primary actions.
2. Resize to the smallest allowed window; scroll the sample result list and reach its controls.
3. Check that navigation, primary buttons and status text do not clip or overlap. Record an exact screen/size for a defect.

**Expected:** Focus and primary controls remain visible and usable.

**Cleanup:** Restore window size/scale. Screen-reader announcements are conditional C07.

<a id="release-testing-procedure-conditional-scenarios"></a>
## Conditional scenarios

Activate only for relevant changes, platform or release risk, using the [coverage map](release-testing/coverage-map.md). Missing a required environment becomes Blocked after assessment, not an automatic N/A. Keep each selected legacy case as its own row; retain unresolved scope explicitly. C01–C12 below use v2.13 controls unless a version-specific procedure is linked; do not impose them on older releases unchanged.


<a id="release-testing-procedure-os-c01"></a>
<a id="release-testing-procedure-os-c01-conditional-many-duplicates"></a>
### OS-C01: Conditional: many duplicates

**Prerequisite:** Disposable duplicate groups with at least seven extra copies and one keeper per group.

1. Select more than five duplicate copies across groups and retain a keeper in each.
2. Inspect the combined recovery Change Plan and the separately explained five-item shell-open limit.
3. On supported Windows, explicitly Apply and Undo only that reviewed sample plan.

**Expected:** More than five copies can be reviewed safely.

**Cleanup:** Close/cancel pending work, restore changed settings, and use a fresh disposable profile/sample before a different package. Preserve evidence before resetting.

<a id="release-testing-procedure-os-c02"></a>
<a id="release-testing-procedure-os-c02-conditional-optional-ai"></a>
### OS-C02: Conditional: optional AI

**Prerequisite:** Existing approved local provider/model only; owner-provided synthetic prompt.

1. With AI disabled, open the visible organization card and follow its Settings link.
2. With the already approved local provider enabled, request one rename/folder proposal; inspect provenance and Keep/Dismiss.
3. Cancel a request and verify it cannot later apply a change; accepting a proposal only hands it to Change Plan review.

**Expected:** Disabled AI is inert; proposals require review.

**Cleanup:** Close/cancel pending work, restore changed settings, and use a fresh disposable profile/sample before a different package. Preserve evidence before resetting.

<a id="release-testing-procedure-os-c03"></a>
<a id="release-testing-procedure-os-c03-conditional-relationship-merge"></a>
### OS-C03: Conditional: relationship merge

**Prerequisite:** Disposable indexed collections, explicit owner scope for one merge.

1. Read the exact target/consequence of a merge confirmation and cancel it.
2. Repeat and explicitly confirm the same synthetic merge.
3. Restart and inspect retained collection authority; source files must be unchanged. Use separate inherited-case rows for unlink, split, forget and reset-to-automatic.

**Expected:** Cancel preserves state; confirmed merge persists.

**Cleanup:** Close/cancel pending work, restore changed settings, and use a fresh disposable profile/sample before a different package. Preserve evidence before resetting.

<a id="release-testing-procedure-os-c04"></a>
<a id="release-testing-procedure-os-c04-conditional-catalog-cancel"></a>
### OS-C04: Conditional: catalog cancel

**Prerequisite:** Two explicit synthetic historical catalog snapshots; enough entries to observe cancellation.

1. Open Before & After / Compare scans, select Earlier scan and Later scan, then choose Compare selected scans.
2. Use Cancel comparison while active, then start another comparison.
3. Confirm responsive cancellation and that the first operation cannot replace the later result.

**Expected:** Cancelled comparison cannot publish stale results.

**Cleanup:** Close/cancel pending work, restore changed settings, and use a fresh disposable profile/sample before a different package. Preserve evidence before resetting.

<a id="release-testing-procedure-os-c05"></a>
<a id="release-testing-procedure-os-c05-conditional-upgrade"></a>
### OS-C05: Conditional: upgrade

**Prerequisite:** Disposable account with copied v2.12 state, or v2.4/schema-5 state for migration coverage.

1. Close the predecessor and install the exact current per-user package.
2. Launch and inspect saved searches, tags and relationship decisions from that prepared profile.
3. Record predecessor/schema separately; do not infer schema-5 migration from a v2.12-to-v2.13 run.

**Expected:** Prior supported profile remains readable.

**Cleanup:** Close/cancel pending work, restore changed settings, and use a fresh disposable profile/sample before a different package. Preserve evidence before resetting.

<a id="release-testing-procedure-os-c06"></a>
<a id="release-testing-procedure-os-c06-conditional-state-backup"></a>
### OS-C06: Conditional: state backup

**Prerequisite:** Disposable indexed state and another disposable profile; no personal backup.

1. Export a format-2 .oms-state archive and read its privacy/scope warning.
2. Preview/restore it in the disposable destination profile.
3. Verify one saved query and explicit relationship/collection decision; unresolved identities must be reported, not guessed by filename. Format-1 compatibility uses its own inherited case.

**Expected:** Explicit retained decisions restore without guessing.

**Cleanup:** Close/cancel pending work, restore changed settings, and use a fresh disposable profile/sample before a different package. Preserve evidence before resetting.

<a id="release-testing-procedure-os-c07"></a>
<a id="release-testing-procedure-os-c07-conditional-screen-reader"></a>
### OS-C07: Conditional: screen reader

**Prerequisite:** Actual screen reader and native supported platform.

1. Navigate Search, Smart Tag review and one target-specific relationship confirmation.
2. Observe information/ready/warning/error/disabled/unavailable states that the fixture can produce.
3. Record announced names/states/focus and unobserved states separately; color alone is insufficient.

**Expected:** Status and action meaning is announced.

**Cleanup:** Close/cancel pending work, restore changed settings, and use a fresh disposable profile/sample before a different package. Preserve evidence before resetting.

<a id="release-testing-procedure-os-c08"></a>
<a id="release-testing-procedure-os-c08-conditional-index-progress"></a>
### OS-C08: Conditional: index progress

**Prerequisite:** Owner-approved mixed synthetic library and declared hardware/resource policy.

1. Run a bounded index and observe terminal completions, recent throughput and ETA availability.
2. Record data shape, CPU/disk/queue/stage observations during the same interval.
3. Compare against predeclared criteria; do not infer real 20k/100k coverage from a small sample.

**Expected:** Throughput/ETA reflect observed completed work.

**Cleanup:** Close/cancel pending work, restore changed settings, and use a fresh disposable profile/sample before a different package. Preserve evidence before resetting.

<a id="release-testing-procedure-os-c09"></a>
<a id="release-testing-procedure-os-c09-conditional-native-macos"></a>
### OS-C09: Conditional: native macOS

**Prerequisite:** Native Intel or Apple Silicon host with matching approved DMG; disposable account.

1. Verify that architecture's hash, open the DMG and copy OmniSorSe.app to Applications.
2. Record Gatekeeper/signing status without disabling system security globally; launch, scan/Search the sample and close normally.
3. Observe the platform mutation boundary. Use separate rows for Intel and ARM64; cross-build evidence cannot pass either.

**Expected:** Matching package opens; unsupported mutation stays blocked.

**Cleanup:** Close/cancel pending work, restore changed settings, and use a fresh disposable profile/sample before a different package. Preserve evidence before resetting.

<a id="release-testing-procedure-os-c10"></a>
<a id="release-testing-procedure-os-c10-smart-tag-decision-v213"></a>
### OS-C10: Smart Tag decision (v2.13)

**Prerequisite:** Indexed synthetic document with a reviewable Smart Tag and recorded expected suggestion; fixture owner supplies it before testing.

1. In Review select the document, open Smart Tags and inspect evidence/status.
2. Select one suggestion and **Accept suggestion** or **Reject suggestion**; note the choice.
3. Refresh/restart and inspect the same file's retained decision.

**Expected:** The explicit choice persists; no source file is changed.

**Cleanup:** Use a fresh test profile for another decision. Unavailable/still-indexing states remain separate inherited cases.

<a id="release-testing-procedure-os-c11"></a>
<a id="release-testing-procedure-os-c11-related-files-decision-v213"></a>
### OS-C11: Related Files decision (v2.13)

**Prerequisite:** Two synthetic indexed files with an owner-prepared visible pair; record their IDs/labels.

1. In **Related Files**, select the pair and choose **Related** or **Not Related**.
2. Request **Use automatic result**, inspect the exact target, then **Cancel**.
3. Restart and confirm the explicit choice remains and source files are unchanged.

**Expected:** Cancellation preserves the explicit pair choice after restart.

**Cleanup:** Retain evidence and reset the disposable profile. Confirmed reset, unlink, split and Forget are individual inherited checks.

<a id="release-testing-procedure-os-c12"></a>
<a id="release-testing-procedure-os-c12-saved-search-v213"></a>
### OS-C12: Saved search (v2.13)

**Prerequisite:** M03 successful; synthetic query `lunar-sample`.

1. Run that query, expand **Saved searches and index maintenance**, give it the name `Sample lookup`, then **Save new**.
2. Navigate away/back, select `Sample lookup` and **Open**.
3. Confirm query and known result return; collapse/expand **Refine this search** without losing focus or selection.

**Expected:** The saved query reloads and controls remain usable.

**Cleanup:** Select only `Sample lookup` and **Delete selected**. Earlier versions use their own saved-view/catalog-search procedures.

<a id="release-testing-procedure-retained-coverage-and-issue-handoff"></a>
## Retained coverage and issue handoff

The [coverage map](release-testing/coverage-map.md) maps original requirements to core, conditional, automated evidence or owner decisions. Reclassification does not satisfy anything. For inherited checks use **OS-L<version>-NNN**, the one-based checkbox ordinal within the matching historical version section (or its original file in Git) (for example OS-L2.13-006); do not renumber frozen sources. Select one requirement per issue row, link its original heading, and state prerequisite/actions/expected/reset before execution. Split an oversized legacy item into suffixed cases when necessary; its parent remains unresolved until all required parts have evidence.

The [index](release-testing/README.md) explains how to create or reuse the release's issue and pin definition links. Only the issue's current table and preserved attempt comments hold ongoing results. Historical reports are frozen evidence; drafts are never synchronized back from an issue. Before publication assign source/artifact, environment, owner, conditional scope and acceptance criteria. Signing, external-provider permission and publication approval are owner decisions outside the test table.

## Release-history ledger

Counts below are source checkbox markers, **not manual-pass totals**. Numbered
procedures without result fields remain Not recorded. Evidence provenance,
failures and platform boundaries are retained in each version section.

| Version | Checked markers | Unchecked markers | Evidence boundary |
| --- | ---: | ---: | --- |
| [v3.0](#manual-v3-0) | 0 | 0 | 24 human rows Not run; live issue #53 |
| [v2.13](#manual-v2-13) | 0 | 25 | Unchecked procedure; no individual pass claimed |
| [v2.12](#manual-v2-12) | 0 | 24 | Unchecked procedure; no individual pass claimed |
| [v2.11](#manual-v2-11) | 0 | 20 | Unchecked procedure; no individual pass claimed |
| [v2.10](#manual-v2-10) | 0 | 61 | Unchecked procedure; no individual pass claimed |
| [v2.9](#manual-v2-9) | 0 | 36 | Unchecked procedure; no individual pass claimed |
| [v2.8](#manual-v2-8) | 0 | 38 | Unchecked procedure; no individual pass claimed |
| [v2.7](#manual-v2-7) | 7 | 29 | Automated evidence only; native and interactive checks unperformed |
| [v2.6](#manual-v2-6) | 7 | 48 | Automated evidence only; native and interactive checks unperformed |
| [v2.5](#manual-v2-5) | 19 | 40 | Mixed automated/controlled-native evidence; unperformed interaction stays unperformed |
| [v2.4](#manual-v2-4) | 52 | 11 | Mixed automated/controlled-native evidence; unperformed interaction stays unperformed |
| [v2.3](#manual-v2-3) | 23 | 31 | Mixed automated/controlled-native evidence; unperformed interaction stays unperformed |
| [v2.2](#manual-v2-2) | 12 | 55 | Mixed automated/controlled-native evidence; unperformed interaction stays unperformed |
| [v2.1](#manual-v2-1) | 0 | 34 | Unchecked procedure; no individual pass claimed |
| [v2.0](#manual-v2-0) | 0 | 111 | Unchecked procedure; no individual pass claimed |
| [v1.9](#manual-v1-9) | 0 | 65 | Unchecked procedure; no individual pass claimed |
| [v1.8](#manual-v1-8) | 0 | 48 | Unchecked procedure; no individual pass claimed |
| [v1.7](#manual-v1-7) | 0 | 31 | Unchecked procedure; no individual pass claimed |
| [v1.6](#manual-v1-6) | 0 | 38 | Maintainer attestation; detailed environment Not recorded |
| [v1.5](#manual-v1-5) | 0 | 23 | Unchecked procedure; no individual pass claimed |
| [v1.4](#manual-v1-4) | 0 | 30 | Unchecked procedure; no individual pass claimed |
| [v1.3](#manual-v1-3) | 0 | 39 | Unchecked procedure; no individual pass claimed |
| [v1.2](#manual-v1-2) | 0 | 45 | Unchecked procedure; no individual pass claimed |
| [v1.1](#manual-v1-1) | 0 | 34 | Unchecked procedure; no individual pass claimed |
| [v1.0](#manual-v1-0) | 0 | 0 | Numbered procedure; 21 model-matrix rows Not run; other results Not recorded |
| [v0.9.1](#manual-v0-9-1) | 0 | 0 | Numbered planned procedure; result Not recorded |

## Shared historical platform checks

These define repeated procedures once; their separate release statuses remain
in the historical sections. None implies current native validation.

<a id="common-45e132c48d"></a>
- Perform native Windows runtime validation.

<a id="common-7946934fcb"></a>
- Perform native Linux runtime validation.

<a id="common-e0d1fba209"></a>
- Perform native macOS x64 runtime validation.

<a id="common-da03243e67"></a>
- Perform native macOS arm64 runtime validation.




<a id="manual-v2-13"></a>
## v2.13

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v2.13.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

This checklist adds the Product Clarity & Workflow scenarios. The inherited
v2.10–v2.12 platform, provider, relationship, packaging, and upgrade gates
remain separate. Every item below stays unchecked until a maintainer performs
it on the claimed host.

<a id="manual-v2-13-primary-workflow-and-hierarchy"></a>
#### Primary workflow and hierarchy

- [ ] At first launch and with an existing profile, Home makes Scan → Review → Organize the obvious primary path.
- [ ] Complete a real Scan, inspect Smart Tag and optional AI suggestions, create a Change Plan, review it, and apply it only in a disposable folder.
- [ ] Verify Search, Duplicates, Related Files, library automation, graph diagnostics, and Settings remain reachable and have distinct roles.
- [ ] At minimum supported window size and 100%, 125%, 150%, and 200% scaling, verify cards, navigation, status banners, tables, and primary actions do not clip or overlap.

<a id="manual-v2-13-review-and-duplicates"></a>
#### Review and duplicates

- [ ] Open Change Plans from duplicates, recipes, AI rename, and AI folder suggestions; verify origin, purpose, warnings, and action counts are accurate.
- [ ] Verify Approve all eligible includes only Valid/Warning actions without blocking conflicts and does not apply the plan.
- [ ] Verify Exclude all and individual approvals remain reversible before Apply.
- [ ] Select more than five duplicate copies across groups, retain at least one keeper in each group, review the combined recovery plan, apply, and Undo in a disposable tree.
- [ ] Verify shell Open file/Open folder remains capped and explains the separate five-item limit.

<a id="manual-v2-13-search-indexing-and-smart-tags"></a>
#### Search, indexing, and Smart Tags

- [ ] Resize Search through supported dimensions; results retain useful height and normal wheel, keyboard, and scrollbar behavior.
- [ ] Expand/collapse Refine this search, Saved searches, and index maintenance; focus remains stable and existing saved searches still load.
- [ ] Observe recent throughput and ETA during a mixed real-library index; compare displayed values with terminal completions over time and record CPU, disk, queue depth, extraction/OCR/media stages, and resource policy.
- [ ] Verify Search enablement and disabled/unavailable explanations route directly to the relevant Settings controls.
- [ ] Verify Smart Tags for indexed files, still-indexing files, files with insufficient evidence, accepted/rejected tags, restart persistence, and Refresh Smart Tags.

<a id="manual-v2-13-ai-and-status-presentation"></a>
#### AI and status presentation

- [ ] With AI disabled, the optional AI organization card remains visible, makes no provider request, and opens the correct Settings section.
- [ ] With local and non-loopback test endpoints, verify the privacy label, model availability, rename/folder proposal provenance, validation failures, cancellation, Keep proposal, Dismiss, and Change Plan handoff.
- [ ] Verify information, ready, warning, error, disabled, and unavailable statuses are distinguishable without color alone and announced appropriately by a screen reader.

<a id="manual-v2-13-relationships-collections-and-graph-diagnostics"></a>
#### Relationships, collections, and graph diagnostics

- [ ] Use Related Files as the ordinary relationship flow and confirm Graph diagnostics reads as an advanced derived projection, not authority.
- [ ] For unlink, merge, split, forget file/source/collection, and Use automatic result, verify the exact target and consequence appear before service state changes.
- [ ] Cancel each relationship-data confirmation and verify no retained authority is changed; confirm each against disposable indexed state and restart to verify persistence.
- [ ] Verify repair/rebuild remains contextual and does not modify source files.

<a id="manual-v2-13-packaging-and-upgrade"></a>
#### Packaging and upgrade

- [ ] Confirm About, diagnostics, binaries, installer, portable build manifest, app bundle, SBOM, checksum manifest, tag, and release agree on `2.13.0-rc`, file version `2.13.0.0`, and one exact commit.
- [ ] Download packages from the public prerelease as a normal user, verify SHA-256, install/run/uninstall, and verify user-data preservation.
- [ ] Repeat normal-user launch/upgrade on supported Windows and macOS architectures; record SmartScreen/Gatekeeper behavior and do not claim publisher signing or notarization.
- [ ] Confirm schema remains 6, Explorer Protocol remains 1.0, and existing v2.12 profile/saved-search/relationship/tag state remains readable.

</details>

<a id="manual-v2-12"></a>
## v2.12

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v2.12.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

This checklist adds only v2.12 relationship/context scenarios. All inherited
v2.10 and v2.11 release gates remain separate. Every item below is intentionally
unchecked until a maintainer performs it on the claimed host.

<a id="manual-v2-12-relationship-quality-and-authority"></a>
#### Relationship quality and authority

- [ ] Review direct Related Files on a mixed real library with Knowledge Graph disabled.
- [ ] Repeat with Knowledge Graph enabled; direct results and corrections remain available if graph storage is disabled again.
- [ ] Mark a pair Related, restart, and confirm positive authority and explanation persist.
- [ ] Mark a pair Not Related, restart, confirm it is hidden from automatic results, then find it in corrections.
- [ ] Choose Use automatic result, restart, and confirm explicit authority remains cleared.
- [ ] Rename and move a file through the existing reviewed Change Plan; stable-ID pair and collection authority persists.
- [ ] Modify file content and reindex; inferred evidence refreshes while explicit authority persists.
- [ ] Review common-folder/common-topic and large duplicate-group cases for false positives and crowding.

<a id="manual-v2-12-backup-and-lifecycle"></a>
#### Backup and lifecycle

- [ ] Export format-2 `.oms-state`, restore collection rename/pin/manual membership/merge/split authority into a disposable profile.
- [ ] Import an exact format-1 `.oms-state` and confirm missing format-2 categories neither cause corruption nor clear existing Smart Collection authority.
- [ ] Restore with unresolved stable IDs and confirm no path/filename guessing occurs.
- [ ] Forget a high-degree file and confirm no Related, correction, collection, Search-context, graph-mirror, or Explorer ghost remains.
- [ ] Forget a source and repeat the ghost-reference check for every affected stable ID.
- [ ] Interrupt relationship-only refresh, restart, and confirm it resumes without rerunning extraction/OCR/transcription.
- [ ] Remove/remount a source while Related Files is open; state fails safely and refreshes after reconciliation.

<a id="manual-v2-12-ux-accessibility-and-companion"></a>
#### UX, accessibility, and companion

- [ ] At 100%, 125%, and 150% DPI, inspect long reasons, corrections, scrolling, selection, and async focus stability.
- [ ] Complete Related / Not Related / Use automatic using only the keyboard.
- [ ] With a screen reader, verify filenames, confidence bands, evidence class, authority state, and action names are announced without color-only meaning.
- [ ] Use Search and Files entry points and confirm the selected stable file opens in Related Files.
- [ ] With OmniBrille installed separately, request repeated bounded context expansion and verify one opaque target per pair.
- [ ] On claimed Windows/macOS/Linux support levels, exercise case, Unicode/NFC-NFD, missing-source, and symlink-sensitive identity scenarios.

<a id="manual-v2-12-release-evidence"></a>
#### Release evidence

- [ ] About, diagnostics, `.oms-state` manifest, binaries, and published packages agree on semantic version 2.12.0-rc, Windows file version 2.12.0.0, and the exact reviewed commit.
- [ ] Confirm schema remains 6 and Explorer Protocol reports 1.0.
- [ ] Confirm no automatic rename, move, delete, or Change Plan creation follows from relationship confidence.

</details>

<a id="manual-v2-11"></a>
## v2.11

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v2.11.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Status:** Unreleased; every unchecked item is genuinely unperformed.

Use the [v2.10 master matrix](MANUAL_TESTING.md#manual-v2-10) for inherited product,
recovery, scale, accessibility, and failure gates. This addendum contains only
runtime/platform/package work introduced by v2.11.

<a id="manual-v2-11-windows-x64"></a>
#### Windows x64

- [ ] Confirm About, diagnostics, package manifest, file/product version, and
  installer all show the same candidate version under test and exact commit.
- [ ] Launch the self-contained portable ZIP on a clean machine without a
  separately installed .NET runtime.
- [ ] Install per-user, first-launch, close, upgrade from the published v2.4
  installer/profile, confirm schema-5-to-6 migration and user-state retention,
  then uninstall/reinstall and confirm application data remains.
- [ ] Run the installer while OmniSorSe is open; confirm Restart Manager requests
  closure/refuses unsafe replacement and no journal/profile damage occurs.
- [ ] Record Windows signature and SmartScreen status; do not mark signed unless
  the exact executable and installer signatures verify.
- [ ] Repeat keyboard, screen-reader, 100/125/150% DPI, and issues #29/#31 smoke
  from the v2.10 matrix on the packaged .NET 10 build.
- [ ] Confirm configured/absent Tesseract, ffmpeg/ffprobe, whisper.cpp, Ollama,
  and OmniBrille readiness on the package without downloading them automatically.

<a id="manual-v2-11-macos-x64-and-arm64"></a>
#### macOS x64 and arm64

- [ ] On native Intel macOS, mount the x64 DMG, inspect architecture/version/SHA,
  launch, create a disposable profile, scan/Search, health-check, backup/restore,
  and exit cleanly.
- [ ] Repeat on native Apple Silicon with the arm64 DMG.
- [ ] Record signature, notarization, staple, and Gatekeeper status separately;
  an unsigned override is not notarization evidence.
- [ ] Exercise NFC/NFD-equivalent names, case behavior, permissions, and symlinks
  on disposable roots. Confirm unsupported mutation remains blocked.
- [ ] Perform keyboard, VoiceOver, and window-scaling smoke if normal macOS
  support is to be claimed.
- [ ] Check optional-tool discovery/version/error behavior for tools actually
  installed on each architecture.

<a id="manual-v2-11-linux-x64-preview"></a>
#### Linux x64 preview

- [ ] On a representative supported distribution, install SDK 10.0.400 (or the
  documented feature-band-compatible SDK), restore/build, launch, scan/Search,
  health-check, backup/restore, and exit cleanly.
- [ ] Exercise case-sensitive names, owner permissions, symlinks, watcher limits,
  and a missing/remounted source on disposable data.
- [ ] Perform keyboard/basic desktop smoke and record the desktop environment.
- [ ] Confirm documentation says source-build preview and offers no installer.

<a id="manual-v2-11-package-replacement-and-trust"></a>
#### Package replacement and trust

- [ ] Verify SHA-256 for every candidate artifact and compare package filenames,
  manifests, binary metadata, RID, bundled .NET 10 runtime, and exact commit.
- [ ] Inspect every artifact for profiles, `.oms-state` backups, logs, databases,
  test results, credentials, developer paths, models, and unintended executables.
- [ ] Follow the supported update path: download from the trusted release, verify
  provenance/checksum/signature status, close OmniSorSe, install/replace, launch,
  and inspect health/profile migration. No in-app updater exists.

</details>

<a id="manual-v2-10"></a>
## v2.10

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v2.10.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Status:** all items below are unperformed until a maintainer records the
exact host, build commit, inputs, and observed result. This checklist
deduplicates the outstanding v2.5-v2.9 gates; historical checklists remain as
release records.

<a id="manual-v2-10-install-profile-upgrade-and-provenance"></a>
#### Install, profile, upgrade, and provenance

- [ ] Fresh Windows install and fresh profile.
- [ ] Reuse a published v2.4/schema-5 profile and observe schema-6 migration.
- [ ] Interrupt upgrade/migration and recover from the managed copy.
- [ ] Launch two processes against one profile; second is read-only startup failure and first remains healthy.
- [ ] Kill the owner process; next launch acquires the profile and reports abnormal prior shutdown.
- [ ] Run distinct profiles concurrently where supported.
- [ ] Install, upgrade, uninstall, and verify profile preservation.
- [ ] Verify About, executable, installer/app bundle, package name, build manifest, commit, and checksum agree.
- [ ] Confirm downgrade of newer state fails clearly and does not write.

<a id="manual-v2-10-indexing-discovery-and-scale"></a>
#### Indexing, discovery, and scale

- [ ] Empty library and one-file library.
- [ ] Fast/base indexing reaches Search before Deep work.
- [ ] Deep indexing, cancellation, restart, retry, and source reconciliation.
- [ ] Search ranking, facets, counts, Saved Views, and Saved View snapshot selection.
- [ ] Search to Files and return with query/facets/Saved View preserved.
- [ ] Smart Tag Strong/Moderate/Limited, accept/reject, continuous next/previous review.
- [ ] Real 20k-file library with memory, startup, Search, and health observations.
- [ ] Real 100k-file library where hardware permits; record truthful truncation/bounds.

<a id="manual-v2-10-reviewed-organization-and-mutation"></a>
#### Reviewed organization and mutation

- [ ] Select 10 and 100 stable IDs from Files, Search, and a Saved View.
- [ ] Recipe naming-only, destination-only, combined, fallback, privacy warning, edit/re-preview.
- [ ] Missing/ambiguous evidence, Unicode, invalid characters, path limits, case/normalization collision.
- [ ] Review Changes, execute rename/move/directory actions, reconcile, return to discovery.
- [ ] Destination appears after preview; stale source moved/deleted/locked/read-only.
- [ ] Partial failure, rollback success, rollback failure reporting, restart recovery, and Undo conflict.
- [ ] Confirm watched folders and Saved Views never auto-execute a recipe.

<a id="manual-v2-10-backup-deletion-corruption-and-storage-failure"></a>
#### Backup, deletion, corruption, and storage failure

- [ ] Export state, inspect privacy warning and fixed archive contents.
- [ ] Restore into empty and existing profiles; review merge/replace conflicts.
- [ ] Restore recipes, Saved Views, sources, User Tags, accept/reject decisions, and manual relationship decisions; unresolved IDs/pairs are skipped without guessing.
- [ ] Cancel restore and inject/observe a mid-restore failure with pre-restore recovery point.
- [ ] Corrupt/truncate Change Plan and Operation Journal files; mutation blocks and evidence remains.
- [ ] Corrupt settings, Saved Views, recipes, and watched settings; verify documented authority policy.
- [ ] Forget File and Forget Source; inspect SQLite plus content/semantic/thumbnail compatibility stores for absence.
- [ ] Distinguish Clear Generated Intelligence, Forget, Clear Index/Rebuild, and Restore.
- [ ] Low application-data space, full export destination, SQLite write failure, and full Change Plan destination.
- [ ] Locked files, permission denied, read-only application data, and unavailable temp storage.
- [ ] Remove a USB/removable source during scan, preview, Search, and Change Plan; reconnect and reconcile.

<a id="manual-v2-10-hostile-documents-and-optional-tools"></a>
#### Hostile documents and optional tools

- [ ] Near/over-limit, malformed, high-page-count, and expansion-heavy PDFs.
- [ ] Cancel PDF extraction/OCR and inspect bounded cleanup.
- [ ] Record residual in-process PDFium crash/hang behavior with non-sensitive hostile fixtures.
- [ ] Tesseract installed, absent, disabled, missing language, timeout, and malformed output.
- [ ] ffprobe/ffmpeg installed, absent, disabled, timeout, crash, and oversized media.
- [ ] whisper.cpp installed/configured, absent, disabled, timeout, cancellation, and model mismatch.
- [ ] Ollama disabled/local/remote HTTPS/remote plain HTTP acknowledged/misconfigured/unavailable.
- [ ] Prompt-injection documents produce only bounded review suggestions and never execute or invent IDs.
- [ ] OmniBrille unavailable, launch, one-time handoff, expiry/replay, disconnect, and close lifecycle.

<a id="manual-v2-10-health-lifecycle-diagnostics-and-recovery"></a>
#### Health, lifecycle, diagnostics, and recovery

- [ ] Health shows healthy schema/stores/ownership with bounded startup cost.
- [ ] Health shows unreachable source, failed jobs, low space, corrupt journal, and recovery backup.
- [ ] Kill during indexing, migration, Change Plan, restore, and shutdown; verify honest restart state.
- [ ] Shutdown remains responsive while optional providers are slow/uncooperative.
- [ ] Review logs/diagnostics for absence of content, OCR, transcripts, tags, queries, prompts, tokens, and private paths.
- [ ] Follow every operational runbook on a disposable profile.

<a id="manual-v2-10-accessibility-and-layout"></a>
#### Accessibility and layout

- [ ] Keyboard-only Home, Search, Files, Smart Tag review, Organize, Review Changes, Settings health, export, and restore.
- [ ] Screen-reader names/states/live announcements for health, backup preview, conflicts, recovery, and existing workflows.
- [ ] Focus after Search-to-Files return, review decision, async refresh, recipe re-preview, and restore preview.
- [ ] Compact-window scrolling with no clipped critical action.
- [ ] Windows display scaling at 100%, 125%, and 150%, including tracked layout scenarios #29 and #31.

<a id="manual-v2-10-platforms-and-packaging"></a>
#### Platforms and packaging

- [ ] Native Windows x64 runtime smoke and optional tools.
- [ ] Native macOS Intel runtime, case/Unicode filesystem, package, Gatekeeper/signing state, and optional tools.
- [ ] Native macOS ARM64 runtime, package, Gatekeeper/signing state, and optional tools.
- [ ] Native Linux source build/runtime, permissions, case/symlink behavior, desktop integration, and optional tools.
- [ ] Confirm cross-target compilation is not recorded as native execution.
- [ ] Validate unsigned/notarized status accurately; validate signatures when maintainer credentials become available.

</details>

<a id="manual-v2-9"></a>
## v2.9

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v2.9.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Status:** checklist for maintainer sign-off; unchecked items are not claimed as passed

Use disposable indexed data. Confirm the source paths and target root before
approving any Change Plan.

<a id="manual-v2-9-recipe-lifecycle"></a>
#### Recipe lifecycle

- [ ] Create, edit, save, close, and reopen an Organization recipe.
- [ ] Duplicate a built-in recipe; verify the built-in remains immutable.
- [ ] Archive/delete an unreferenced user recipe and confirm referenced recipes remain protected.
- [ ] Restart OmniSorSe and verify the atomic workflow library reloads.

<a id="manual-v2-9-selection-and-discovery"></a>
#### Selection and discovery

- [ ] Select files in Files, preview, close, and verify no filesystem change.
- [ ] Select Search results, preview, return, and verify query/facets remain intact.
- [ ] Open a Saved View, explicitly select current results, preview, then add a new matching file and verify it was not silently added to the selection.
- [ ] Exercise 10 and 100 selected files; verify the count remains explicit.
- [ ] Verify over-bound selection and combined file/directory actions are rejected without truncation.

<a id="manual-v2-9-naming-destination-and-evidence"></a>
#### Naming, destination, and evidence

- [ ] Rename only; verify the original extension and its casing are preserved.
- [ ] Move only into an existing folder and into reviewed newly created nested folders.
- [ ] Combine rename and move without converting file formats.
- [ ] Exercise Unicode, invalid characters, reserved names, missing values, explicit fallbacks, and an invalid token.
- [ ] Verify Accepted and uniquely Strong deterministic Theme/Document Type resolve.
- [ ] Verify Moderate, Limited, rejected, ambiguous Theme, and missing Document Type do not resolve.
- [ ] Verify filesystem-created and filesystem-modified dates are labelled distinctly.

<a id="manual-v2-9-preview-and-safety"></a>
#### Preview and safety

- [ ] Edit a recipe after preview and confirm **Review Changes** remains unavailable until re-preview.
- [ ] Filter preview by Reliable, Needs review, Cannot propose, conflicts, and missing evidence.
- [ ] Verify duplicate, case-only, normalization, existing-target, traversal, absolute, drive, UNC, outside-root, read-only-root, and path-length cases block safely.
- [ ] Move/delete a source or create its target after preview; verify stale preview requires refresh.
- [ ] Verify classification-derived naming shows the externalization warning.
- [ ] Inspect ordinary diagnostics and confirm raw tag values, generated paths, and evidence are absent.

<a id="manual-v2-9-change-plan-reconciliation-and-undo"></a>
#### Change Plan, reconciliation, and Undo

- [ ] Choose **Review Changes** and verify no operation runs before approval and Apply confirmation.
- [ ] Execute rename-only, move-only, combined rename/move, and directory-creation plans.
- [ ] Verify Files, Search, index identity, details, and selection reconcile to filesystem truth.
- [ ] Exercise a safe partial-failure/rollback scenario and verify mixed state is reported truthfully.
- [ ] Undo and verify original paths/searchability return and created owned directories are cleaned only when safe.

<a id="manual-v2-9-accessibility-and-layout"></a>
#### Accessibility and layout

- [ ] Complete the workflow using keyboard multi-selection and Tab/Shift+Tab.
- [ ] Verify recipe selector, token picker, patterns, readiness, current/proposed paths, conflicts, preview filters, re-preview, and Review Changes with a screen reader.
- [ ] Verify focus remains stable after preview/re-preview and asynchronous status updates.
- [ ] Test compact and normal windows, scrolling, and 100%, 125%, and 150% Windows display scaling.

<a id="manual-v2-9-platform-and-boundaries"></a>
#### Platform and boundaries

- [ ] Perform native Windows execution and Undo.
- [ ] Perform native macOS execution, including case and Unicode-normalization collision scenarios, when supported.
- [ ] Perform Linux source-build runtime checks according to documented support.
- [ ] Confirm watched folders never auto-apply a recipe.
- [ ] Confirm closing OmniBrille and Explorer Protocol behavior are unaffected.

Automated Windows-host tests cover the deterministic contracts but do not
substitute for these desktop, DPI, screen-reader, permission, filesystem, or
native macOS/Linux checks.

</details>

<a id="manual-v2-8"></a>
## v2.8

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v2.8.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Status:** unreleased validation checklist

Automated coverage validates contracts and state transitions. The unchecked
desktop, screen-reader, DPI, optional-tool, and real file-operation scenarios
below are not claimed passes.

<a id="manual-v2-8-home"></a>
#### Home

- [ ] Start with a disposable profile containing no sources; confirm Home
  explains how to begin and Find remains available.
- [ ] Restart with an existing durable index; confirm known files, source count,
  base-Search readiness, deeper-analysis state, review count, and Saved View
  count appear without a new in-session scan.
- [ ] While Fast indexing continues, confirm Home distinguishes base Search
  ready from deeper analysis/classification still running.
- [ ] Confirm recent Saved View shortcuts do not execute until opened.
- [ ] Exercise Ready, Disabled, Not configured, Unavailable, and Needs attention
  optional-capability states where practical.
- [ ] Confirm Home refresh does not contact Ollama, execute tools, launch
  OmniBrille, or noticeably block startup.

<a id="manual-v2-8-find-and-search-to-files"></a>
#### Find and Search to Files

- [ ] Run a free-text query with multiple facets and a Saved View, select a
  result, and choose **Open in Files**.
- [ ] Confirm Files selects the same stable logical file and shows its current path.
- [ ] Choose **Return to discovery** and confirm query, facets, Saved View,
  unresolved-review mode, result context, and keyboard focus remain coherent.
- [ ] Rename/move the file through a disposable reviewed Change Plan between
  Search and Files; confirm stable identity follows the current path.
- [ ] Delete/move a disposable source file externally during the transition;
  confirm a bounded unavailable/missing warning and no stale-path launch.
- [ ] Confirm Search has only one Theme, Document Type, and User Tag filter
  surface and active chips/counts stay synchronized.

<a id="manual-v2-8-continuous-smart-tag-review"></a>
#### Continuous Smart Tag review

- [ ] Open unresolved Moderate suggestions from Home or Search.
- [ ] Inspect type, label, Moderate state, and no more than the bounded evidence reasons.
- [ ] Keep one suggestion; confirm it leaves unresolved review and the next
  current item opens.
- [ ] Dismiss one suggestion; confirm rejection persists and it does not return
  after ordinary reindex.
- [ ] Use previous/next and Return to discovery with keyboard only.
- [ ] Remove or reclassify an item while review is active; confirm stale items
  are skipped gracefully and no decision is applied to another file.

<a id="manual-v2-8-understand-and-organize"></a>
#### Understand and Organize

- [ ] Use Home **Understand** with and without an existing Files context; confirm
  the prerequisite is clear and no fake recent item is invented.
- [ ] Generate a rename proposal for a file with accepted and Strong
  classification evidence; confirm the allowed evidence and authority are shown.
- [ ] Confirm unresolved Moderate, Limited, and rejected evidence is not used.
- [ ] Edit/reject the proposal and confirm no source mutation occurs.
- [ ] Create and explicitly apply a disposable Change Plan, inspect reconciled
  Files/Search state, Undo it, and return to the original discovery context.

<a id="manual-v2-8-optional-capabilities"></a>
#### Optional capabilities

- [ ] Validate Ollama, Tesseract, ffprobe, ffmpeg, whisper.cpp, and OmniBrille
  when installed, absent, disabled, and intentionally misconfigured where safe.
- [ ] Confirm each compact explanation describes the missing capability's effect
  without describing an optional absence as an application failure.
- [ ] Confirm settings/help links reach the existing configuration surface and
  no auto-install or network action occurs.

<a id="manual-v2-8-accessibility-dpi-and-scrolling"></a>
#### Accessibility, DPI, and scrolling

- [ ] Complete Home tasks, Saved View shortcuts, Search to Files, return,
  review previous/next, Keep/Dismiss, and organization evidence using keyboard only.
- [ ] With a Windows screen reader, verify names, selected/current states,
  readiness updates, counts, capability states, and review-position changes.
- [ ] Repeat Home, Search facets, Files review, and evidence disclosure at 100%,
  125%, and 150% DPI in compact and normal windows.
- [ ] Exercise mouse wheel, scrollbar drag, Page Up/Page Down, Tab, and Shift+Tab.
- [ ] Confirm focus remains visible and stable after asynchronous Home/count updates.
- [ ] Recheck issue #29 Virtual Collections and #31 Related Files separately;
  this release does not claim those matrices complete when their code is unchanged.

<a id="manual-v2-8-privacy-recovery-and-platforms"></a>
#### Privacy, recovery, and platforms

- [ ] Inspect ordinary/exported diagnostics for absence of raw query text, User
  Tags, Saved View contents, evidence excerpts, optional-tool secrets, and private paths.
- [ ] Verify Clear Index, Forget, recovery guidance, Operation History, and Undo
  wording distinguish index data from source-file changes.
- [ ] [Shared perform native windows runtime validation](#common-45e132c48d).
- [ ] [Shared perform native linux runtime validation](#common-7946934fcb).
- [ ] [Shared perform native macos x64 runtime validation](#common-e0d1fba209).
- [ ] [Shared perform native macos arm64 runtime validation](#common-da03243e67).

</details>

<a id="manual-v2-7"></a>
## v2.7

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v2.7.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Status:** unreleased validation checklist

This document separates automated evidence from scenarios that still require a
real desktop, large disposable library, accessibility technology, or native
platform. An unchecked item is not a claimed pass.

<a id="manual-v2-7-automated-release-evidence"></a>
#### Automated release evidence

- [x] Fresh Debug and Release non-incremental builds finish with zero warnings/errors.
- [x] Complete Debug and Release suites pass: 1,753 passed, 0 failed, 0 skipped
  in each configuration (24 tests above the committed v2.6 baseline).
- [x] A 20,000-file regression retrieves exact filename, stem, and prefix
  matches beyond the former 10,000-row projection, including canonical Smart
  Tag and explicit filesystem-date filters.
- [x] A controlled 100,000-file regression considers the complete library while
  hydrating 512 relevance-selected candidates. On the validation host, candidate
  selection took 2.840 seconds and six-group facet aggregation took 3.357 seconds.
- [x] SQLite facet counts, OR-within/AND-across semantics, unresolved Moderate
  review, Saved View persistence, extraction, privacy, and accessibility tests pass.
- [x] Explorer Protocol v1, workflow safety, performance (23/23), policy (8/8),
  format, analyzer, vulnerability, diff, and repository-integrity gates pass.
- [x] Release compilation passes for win-x64, linux-x64, osx-x64, and osx-arm64.
  Compilation is not native runtime validation.

<a id="manual-v2-7-complete-library-retrieval"></a>
#### Complete-library retrieval

- [ ] Index a disposable library above 10,000 files and search for an exact
  filename deliberately sorted outside the first 10,000 paths.
- [ ] Repeat with the exact filename stem, a file-type facet, and a Smart Tag facet.
- [ ] Confirm exact filename/stem/prefix order remains above weaker content/tag matches.
- [ ] Inspect the separate indexed, candidate-coverage, and displayed-result facts.
- [ ] Cancel a large query and replace an in-flight query; confirm no stale result appears.

<a id="manual-v2-7-faceted-discovery"></a>
#### Faceted discovery

- [ ] Combine two Themes and confirm OR within Theme.
- [ ] Add Document Type, User Tag, file type, created year, and modified year;
  confirm AND across populated groups.
- [ ] Confirm created and modified years are labelled as filesystem dates.
- [ ] Confirm counts respect query text and filters in every other group.
- [ ] Remove individual chips and Clear all; confirm one coherent query state.
- [ ] Let classification finish in the background and confirm counts/results
  increase without duplicate file identity or a false Complete state.

<a id="manual-v2-7-saved-views-and-smart-tag-review"></a>
#### Saved Views and Smart Tag review

- [ ] Save a query/filter rule, reopen it, rename/update it, and delete it.
- [ ] Change the indexed library and confirm the view reevaluates current data;
  no result membership should be copied into the Saved View store.
- [ ] Confirm Saved scans remain historical catalog snapshots with distinct wording.
- [ ] Open unresolved Moderate suggestions, Keep one, Dismiss one, and confirm
  each leaves the unresolved result without weakening reindex authority.

<a id="manual-v2-7-native-extraction"></a>
#### Native extraction

- [ ] Index controlled UTF-8/BOM CSV and TSV files with quoted cells, embedded
  delimiters, malformed rows, and size/row bounds.
- [ ] Index controlled XLSX files with shared strings, inline strings, numeric
  values, dates, and formulas; verify formulas are never executed.
- [ ] Index controlled PPTX slides and speaker notes; verify embedded objects,
  macros, relationships, and external resources are not opened.
- [ ] Confirm extracted evidence can feed Search, Content Intelligence, and Smart
  Tags without modifying the source files.

<a id="manual-v2-7-windows-desktop-and-accessibility"></a>
#### Windows desktop and accessibility

- [ ] Exercise facet disclosure, scrolling, search-with-filters, and Saved View
  actions using mouse and keyboard at 100%, 125%, and 150% DPI.
- [ ] Repeat in compact and normal windows; verify focus survives asynchronous
  count refresh when the focused value remains present.
- [ ] Use a Windows screen reader to verify group, value/count, selected state,
  removable chip, Clear all, Saved View, and live coverage announcements.
- [ ] Confirm no state relies on colour alone.

<a id="manual-v2-7-privacy-and-platforms"></a>
#### Privacy and platforms

- [ ] Inspect ordinary and exported diagnostics for absence of raw query text,
  User Tags, facet labels, Saved View rules, and evidence excerpts.
- [ ] Confirm Saved Views remain local and no network request is introduced.
- [ ] [Shared perform native windows runtime validation](#common-45e132c48d).
- [ ] [Shared perform native linux runtime validation](#common-7946934fcb).
- [ ] [Shared perform native macos x64 runtime validation](#common-e0d1fba209).
- [ ] [Shared perform native macos arm64 runtime validation](#common-da03243e67).

</details>

<a id="manual-v2-6"></a>
## v2.6

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v2.6.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Status:** unreleased implementation evidence tracker

Only genuinely performed scenarios may be checked. Automated coverage does not
substitute for desktop keyboard, screen-reader, large-library, provider-native,
or platform-native validation.

<a id="manual-v2-6-automated-evidence"></a>
#### Automated evidence

- [x] Fresh Debug and Release builds finish with zero warnings/errors.
- [x] Complete Debug and Release suites pass with 1,729 tests in each
  configuration, zero failures, and zero skips.
- [x] Schema-5-to-6 migration/reopen/recovery tests pass.
- [x] Taxonomy, deterministic classifier, evidence-fusion, user-authority,
  Search/filter, progressive indexing, privacy, and extraction tests pass.
- [x] Explorer Protocol v1 regression suite passes without a protocol change
  (47/47 focused tests).
- [x] Formatting, analyzers, policy, vulnerability, diff, and repository
  integrity checks pass.
- [x] Release compilation passes for win-x64, linux-x64, osx-x64, and
  osx-arm64. This is not native runtime validation.

<a id="manual-v2-6-schema-5-profile-upgrade"></a>
#### Schema-5 profile upgrade

- [ ] Create a disposable schema-5 profile with indexed files, accepted and
  rejected legacy tags, user tags, Content/Media Intelligence, and Search data.
- [ ] Open it with v2.6 and confirm a managed pre-migration backup is created.
- [ ] Confirm schema 6 opens, Search data remains, and source files are unchanged.
- [ ] Confirm uniquely resolvable user/accepted/rejected authority imports.
- [ ] Confirm ambiguous path identities are not guessed or destructively erased.
- [ ] Reopen and confirm migration/import are not repeated.
- [ ] Exercise controlled interrupted/corrupt/newer-schema recovery paths.

<a id="manual-v2-6-classification-quality"></a>
#### Classification quality

- [ ] Use controlled invoice, receipt, contract, statement, report, manual,
  booking, itinerary, form, letter, meeting-notes, certificate, research-paper,
  finance, legal, travel, insurance, and technology fixtures.
- [ ] Confirm content can contradict a misleading filename.
- [ ] Confirm filename/path alone does not produce a semantic Theme.
- [ ] Confirm corroborating independent evidence can strengthen a result.
- [ ] Confirm derived topic/summary text is not visibly double-counted.
- [ ] Confirm ambiguous Document Types show an unresolved state instead of two
  Strong primary types.
- [ ] Confirm unsupported/no-evidence files are not labelled “Unknown”.
- [ ] Inspect at most a few clear evidence reasons and verify Strong/Moderate
  wording is not presented as statistical probability.

<a id="manual-v2-6-user-authority"></a>
#### User authority

- [ ] Add and remove a User Tag; reindex and rename/move the source file.
- [ ] Accept a Moderate suggestion and confirm it remains accepted after reindex.
- [ ] Reject/remove a generated tag and confirm it stays hidden after reindex.
- [ ] Change file content and taxonomy/classifier fingerprint; confirm decisions
  retain authority while generated evidence is refreshed.
- [ ] Reset tag decisions and confirm current generated evidence is reviewable.
- [ ] Clear Generated Smart Tags and confirm User Tags, accepted authority, and
  rejection decisions remain.
- [ ] Forget a file/source and Clear Index; confirm owned Smart Tag state is
  removed while source files remain untouched.

<a id="manual-v2-6-search-and-filtering"></a>
#### Search and filtering

- [ ] Confirm an exact filename match remains above a tag-only match.
- [ ] Confirm Strong generated, accepted, and User Tag matches have accurate
  explanations.
- [ ] Confirm Moderate unaccepted and rejected tags do not affect ordinary Search.
- [ ] Select multiple Theme values and confirm OR semantics.
- [ ] Combine Theme, Document Type, and User Tag filters and confirm AND semantics.
- [ ] Clear filters and use View files with this tag.
- [ ] Confirm progressively arriving tags enrich one existing file result without
  duplicate identity or stale explanation.

<a id="manual-v2-6-progressive-indexing-and-recovery"></a>
#### Progressive indexing and recovery

- [ ] In Fast/searchable-first mode, confirm base names/paths are searchable
  before classification completes.
- [ ] Restart, pause/resume, cancel/retry, and remove/move files while Smart Tag
  jobs are pending.
- [ ] Confirm a taxonomy-only change does not repeat OCR, transcription, media
  probing, topic/entity extraction, or summaries.
- [ ] Repeat in Deep initial analysis and confirm existing capability switches
  remain authoritative.
- [ ] Confirm missing optional media/OCR/transcription/Ollama providers do not
  block deterministic classification over available evidence.

<a id="manual-v2-6-text-extraction"></a>
#### Text extraction

- [ ] Index disposable UTF-8/BOM `.txt`, `.md`, `.markdown`, and `.text` files.
- [ ] Confirm bounded content contributes to Content Intelligence and Smart Tags.
- [ ] Confirm malformed/oversized/unreadable files fail per item without blocking
  indexing or modifying the source.

<a id="manual-v2-6-desktop-accessibility-and-privacy"></a>
#### Desktop accessibility and privacy

- [ ] Verify compact chips/rows, grouped Classifications/Suggestions/Your tags,
  and `+N` behavior at representative window sizes and DPI settings.
- [ ] Use keyboard-only navigation for Keep, Dismiss, Add, Remove, Reset, Clear,
  and View files with this tag.
- [ ] Use a Windows screen reader to verify type, state, confidence band, and
  action labels; confirm no meaning relies on color alone.
- [ ] Inspect ordinary diagnostics and exported safe diagnostics for absence of
  raw tag values, user labels, evidence excerpts, entities, and source content.
- [ ] Confirm no source-file metadata changes and no network access occurs.

<a id="manual-v2-6-performance-and-platforms"></a>
#### Performance and platforms

- [ ] Exercise 10,000-file and 100,000-file controlled indexes plus hundreds of
  thousands of assignments where practical.
- [ ] Record base-search time, classification throughput, typed-filter/Search
  latency, restart reuse, taxonomy reclassification time, storage growth, and
  memory behavior.
- [ ] [Shared perform native windows runtime validation](#common-45e132c48d).
- [ ] [Shared perform native linux runtime validation](#common-7946934fcb).
- [ ] [Shared perform native macos x64 runtime validation](#common-e0d1fba209).
- [ ] [Shared perform native macos arm64 runtime validation](#common-da03243e67).

</details>

<a id="manual-v2-5"></a>
## v2.5

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v2.5.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Status:** unreleased implementation evidence tracker

This checklist separates automated evidence from genuinely interactive work.
An unchecked item is not a failed automated test; it means that exact manual
scenario has not been performed on this branch.

<a id="manual-v2-5-automated-evidence"></a>
#### Automated evidence

- [x] Clean v2.4 baseline restored and built in Debug and Release with zero
  warnings/errors.
- [x] Baseline Debug and Release suites each passed 1,671 tests with zero
  failures/skips.
- [x] Outcome-driven reconciliation tests cover successful rename, move/folder
  restructure, rollback, partial rollback, missing results, and Undo.
- [x] Results projection test verifies path replacement and file-ID selection
  retention.
- [x] A partial execution result reaches the shell reconciliation event.
- [x] Progressive SQLite claim-order tests distinguish broad base-first from
  per-file deep-initial scheduling.
- [x] Search coverage becomes available after discovery while deeper jobs remain
  incomplete.
- [x] An affected-source refresh reuses compatible completed stages.
- [x] Scan-depth configuration default, persistence, manual-scan capture, Help,
  and accessibility labels have automated coverage.
- [x] Optional companion discovery leaves the Explorer host dormant when
  OmniBrille is absent and preserves enabled indexed-source scope when present.
- [x] A genuine separate child test process receives the established one-time
  current-user handoff, authenticates, negotiates Explorer Protocol v1, exits,
  and causes session revocation.
- [x] Bootstrap tests cover independent repeated grants, expiry/timeout,
  incompatible/rejected/invalid responses, strict bounded JSON, missing or
  misconfigured executable paths, process-start failure, and no-source denial.
- [x] Final Debug and Release suites each passed 1,702 tests with zero
  failures/skips. Focused validation passed for Explorer Protocol and companion
  handoff (47/47), workflow/file operations (77/77), Search/index/media
  (262/262), performance (15/15), and documentation/policy (8/8). The
  vulnerability audit reported no findings, and Release cross-target builds
  passed for win-x64, linux-x64, osx-x64, and osx-arm64.

<a id="manual-v2-5-controlled-change-plan-workflow"></a>
#### Controlled Change Plan workflow

- [ ] Rename a disposable file through Review Changes and confirm Files shows
  only the new path immediately.
- [ ] Search before/after the rename and confirm the old path stops appearing.
- [ ] Move disposable files through a folder-structure proposal and confirm
  Files, details, duplicates, and selection converge.
- [ ] Undo the operation and confirm restored Files/Search state.
- [ ] Force a safe controlled execution failure and inspect mixed-state warning
  and affected-source refresh.
- [ ] Force a controlled rollback failure; confirm the UI reports uncertainty
  rather than full success.
- [ ] Confirm Operation History and source files agree after each case.

<a id="manual-v2-5-progressive-indexing"></a>
#### Progressive indexing

- [ ] Select **Fast — searchable first** for a mixed document/media source.
- [ ] Confirm names/paths become searchable while deeper media analysis is still
  reported as continuing.
- [ ] Confirm later OCR/media/content evidence updates the existing result and
  does not add a duplicate.
- [ ] Exit after base coverage but before deeper completion; restart and confirm
  base Search remains available and pending jobs resume.
- [ ] Pause/resume and cancel/retry during deeper analysis.
- [ ] Repeat with **Deep initial analysis** and confirm per-file deeper progress
  is favored without changing enabled capabilities.
- [ ] Confirm missing optional tools produce waiting/unavailable behavior without
  blocking base Search.

<a id="manual-v2-5-issue-29--virtual-collections-scrolling"></a>
#### Issue #29 — Virtual Collections scrolling

- [ ] Normal Windows window: mouse wheel and scrollbar.
- [ ] Small/narrow window: all collections and details reachable.
- [ ] Keyboard focus, Tab/Shift+Tab, Page Up/Page Down.
- [ ] 100% DPI.
- [ ] 125% DPI.
- [ ] 150% DPI.

<a id="shared-7ba0487d3c26"></a>

No v2.5 layout change should be made unless this original scenario is genuinely
reproduced.

<a id="manual-v2-5-issue-31--related-files-scrolling"></a>
#### Issue #31 — Related Files scrolling

- [ ] Normal Windows window: mouse wheel and scrollbar.
- [ ] Small/narrow window: all controls reachable without competing scroll.
- [ ] Keyboard focus, Tab/Shift+Tab, Page Up/Page Down.
- [ ] 100% DPI.
- [ ] 125% DPI.
- [ ] 150% DPI.

Shared definition: [same retained text](#shared-7ba0487d3c26).

<a id="manual-v2-5-omnibrille-companion-handoff"></a>
#### OmniBrille companion handoff

- [x] Test process: actual separate-process bootstrap and Protocol v1
  negotiation over local pipes.
- [x] Automated: host remains dormant until an explicit launch with an
  available companion and enabled indexed source.
- [x] Automated: authorized scope contains enabled indexed sources only and
  omits raw paths.
- [x] Automated: launch material is delivered through a random one-time
  current-user named pipe, not a durable file, environment value, or
  bearer-token command line.
- [x] Automated: strict 4-KiB frames, 15-second acknowledgement bound,
  independent repeated launches, and session revocation on companion exit.
- [x] Automated: missing/misconfigured executable and bootstrap/auth/version
  failures are isolated from normal OmniSorSe operation.
- [ ] Maintainer desktop: use **Open in OmniBrille** with the real installed
  OmniBrille Stage 4 client and verify Search, Related, neighborhoods, and
  details after the normal-user handoff.
- [ ] Maintainer desktop: close OmniBrille normally and confirm OmniSorSe stays
  open; then close OmniSorSe while connected and confirm OmniBrille reports the
  ended session normally.
- [ ] Maintainer desktop: invoke the action twice and confirm two independent
  companion sessions or windows according to OmniBrille's current behavior.
- [ ] Native Linux and macOS companion bootstrap/runtime checks. Cross-target
  compilation is not native execution evidence.

<a id="manual-v2-5-accessibility-and-ux"></a>
#### Accessibility and UX

- [ ] Scan-depth options use plain language and expose an accessible name.
- [ ] Index status clearly distinguishes searchable base coverage from deeper
  analysis.
- [ ] Files organization card makes Suggest → Review Changes → execute clear.
- [ ] Folder suggestions disclose their bounded current-page scope.
- [ ] Review Changes mixed-outcome notification is visible and understandable.
- [ ] Screen-reader smoke test (not implied by keyboard/source checks).

<a id="manual-v2-5-platform-boundary"></a>
#### Platform boundary

- [ ] Native Windows interactive scenarios above.
- [ ] Native Linux interaction.
- [ ] Native macOS x64 interaction.
- [ ] Native macOS arm64 interaction.

Cross-target compilation does not mark native interaction complete.

</details>

<a id="manual-v2-4"></a>
## v2.4

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v2.4.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Status:** v2.4.0 release validation record

This checklist distinguishes automated evidence, Windows-native protocol
execution, maintainer-interactive work, and native packaging/platform work.
An unchecked item is not a failed test; it has not been genuinely exercised in
the stated environment.

<a id="manual-v2-4-automated-and-windows-host-evidence"></a>
#### Automated and Windows-host evidence

- [x] Baseline v2.3.0 commit, clean worktree, synchronized `main`, tag, and Git
  integrity were verified before branching.
- [x] Baseline Release build completed with zero warnings/errors.
- [x] Baseline Release suite passed 1,637 tests with no failures/skips.
- [x] Protocol contracts contain only read-only operations and the contract
  assembly has no SQLite/provider/UI/renderer dependency.
- [x] Strict JSON rejects unknown fields and runtime type metadata.
- [x] High-entropy session/token creation, fixed-time hashed validation, expiry,
  revocation, and session-bound opaque IDs were exercised automatically.
- [x] Authorized roots, stable/paged children, traversal rejection, bounded
  neighborhoods, grounded Search, Related Files scope, and private-detail
  omission were exercised automatically.
- [x] A real Windows named-pipe request/response round trip completed using the
  production frame format and authorization boundary.
- [x] Invalid-secret and malformed-payload connection failures were isolated.
- [x] Provider cancellation reached a controlled long-running request.
- [x] A 5,000-document synthetic structural projection remained bounded.
- [x] Legacy Windows/Linux/macOS profile path selection was exercised through
  deterministic platform tests.
- [x] Final forced/no-cache restore completed for all 17 solution projects.
- [x] Final Debug and Release non-incremental builds completed with zero
  warnings and zero errors.
- [x] Final Debug and Release suites each passed 1,671 tests with zero failures
  and zero skipped/not-executed tests; totals were independently parsed from
  the generated TRX counters.
- [x] Focused Explorer Protocol (33), performance-regression (19), and
  repository documentation/dependency policy (8) suites passed.
- [x] Whitespace, style, analyzer, patch-format, and live NuGet vulnerability
  checks passed; the advisory audit reported no vulnerable direct or transitive
  packages in any solution project.

<a id="manual-v2-4-rename-and-desktop-branding"></a>
#### Rename and desktop branding

- [ ] Launch the final desktop interactively and inspect title, navigation,
  About, Settings, Help, dialogs, notifications, diagnostics, and accessibility
  names for current OmniSorSe branding.
- [ ] Verify About reports OmniSorSe 2.4 and repository links still reach the
  not-yet-renamed repository.
- [x] Verify a fresh user profile starts without errors and creates data only in
  the established legacy compatibility directory.
- [x] Verify no duplicate OpenSorSe/OmniSorSe profiles appear.
- [ ] Exercise primary Scan, Search, Duplicates, Related Files, and Organize
  workflows without OmniExplorer installed.

<a id="manual-v2-4-genuine-v23-profile-upgrade"></a>
#### Genuine v2.3 profile upgrade

- [x] Create a controlled profile using the published OpenSorSe v2.3.0 Windows
  portable package and its real application services.
- [x] Record settings, sources, watched folders, schema-5 index/Search results,
  privacy/AI settings, and external-tool paths. The controlled text files were
  metadata-only in the published v2.3 pipeline, so this fixture did not contain
  Media/Content Intelligence, Change Plan, journal, or recovery rows; their
  schema-5 round trips remain covered by the existing migration/store suites.
- [x] Start the v2.4 candidate twice against that profile and verify indexed
  documents, deterministic Search, watched folders, diagnostics/privacy state,
  Ollama selection, and ffmpeg/ffprobe/Tesseract/whisper paths remain.
- [x] Confirm schema stays at 5 and migration/backup is not run for branding.
- [x] Confirm controlled source files are byte-for-byte untouched.
- [x] Exercise absent profile, invalid settings, and a pre-existing unrelated
  OmniSorSe directory without destructive overwrite. Invalid settings remained
  byte-for-byte intact and startup degraded to defaults.
- [ ] Exercise a genuine NTFS permission-denied legacy profile. This was not
  changed on the maintainer account because it risked leaving inaccessible
  test ACLs; deterministic path/configuration failure coverage remains green.

<a id="manual-v2-4-windows-installer-transition"></a>
#### Windows installer transition

- [x] Install the official v2.3.0 Windows setup package into a disposable
  controlled directory.
- [x] Upgrade with a native v2.4 candidate using the retained installer AppId.
- [x] Verify one Add/Remove Programs entry named OmniSorSe 2.4.0.
- [x] Verify the legacy-compatible install directory is reused,
  `OmniSorSe.exe` launches,
  and obsolete `OpenSorSe` entrypoint files are removed.
- [x] Verify the old Start Menu group/shortcut is removed and only the new
  OmniSorSe shortcut is visible.
- [x] Uninstall and confirm application data remains preserved.

<a id="manual-v2-4-native-protocol-lifecycle-and-security"></a>
#### Native protocol lifecycle and security

- [x] Windows current-user named-pipe round trip using a valid session.
- [x] Missing/invalid secret is rejected before operation/version processing.
- [x] Expired and revoked sessions are rejected in deterministic tests.
- [x] Paths are omitted by default and appear only with an explicit path grant.
- [x] Unavailable OmniExplorer state has no filesystem scan or broken UI action.
- [x] Bounded concurrency and queue saturation return a stable busy response.
- [x] Launch two real processes and verify a grant can be handed off through a
  current-user-only ACL-protected disposable channel without a
  command-line token, then revoked immediately after the companion exits.
- [x] Disconnect the client mid-Search and observe prompt provider cancellation
  and clean host recovery.
- [x] Saturate bounded concurrency with 24 controlled requests: 16 completed,
  eight returned the bounded busy response, none caused a transport failure,
  and host shutdown remained clean.
- [x] Shut down the host during an active request and verify the client receives
  a predictable terminated connection, provider work is cancelled, and host
  state reaches unavailable.
- [x] Exercise actual 15-second expiry, immediate revocation, wrong/missing
  tokens, strict unknown-property rejection, and oversized-frame rejection.
- [ ] Inspect Advanced Diagnostics and confirm no token, query, path, snippet,
  OCR, transcript, or request payload appears.

<a id="manual-v2-4-search-structure-and-context-round-trips"></a>
#### Search, Structure, and Context round trips

- [x] Root scope and out-of-scope filtering are automated.
- [x] Stable folders-first structure, paging, missing/traversal paths, and bounded
  neighborhood behavior are automated.
- [x] Unified deterministic Search ordering, known-ID grounding, AI-disabled
  fallback, explanations, and cancellation are automated.
- [x] Related Files filtering, reason/provenance projection, and bounded details
  without complete OCR/transcript/GPS exposure are automated.
- [ ] Exercise protocol reads while indexing updates/deletes controlled files and
  verify removed nodes fail safely without stale content.
- [x] Exercise Unicode, spaces, and punctuation through roots, children,
  neighborhood, Search, Related Files, and details over the native transport.
- [ ] Exercise a safe near-maximum Windows long path and controlled UNC indexed
  source. No controlled UNC environment was available.

<a id="manual-v2-4-native-platform-and-packaging"></a>
#### Native platform and packaging

- [x] Windows x64 Release runtime output launched through the production
  non-interactive package-smoke entry point and exited successfully using an
  isolated profile. File metadata reported OmniSorSe 2.4.0 / 2.4.0.0, while the
  profile was correctly created under the retained `OpenSorSe` storage name.
- [x] Release cross-target compilation passed with zero warnings/errors for
  `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64`.
- [x] Every cross-target output contained the OmniSorSe app host, Explorer
  Protocol contract assembly, one target-appropriate SQLite native asset, and
  one target-appropriate SkiaSharp native asset.
- [x] Windows x64 v2.4 portable package inspection and package-smoke launch.
- [x] Windows x64 v2.3-to-v2.4 installer upgrade/uninstall test.
- [ ] Native Linux x64 protocol round trip and source-profile compatibility.
- [ ] Native macOS x64 protocol round trip, visible bundle transition, and
  Application Support continuity.
- [ ] Native macOS arm64 protocol round trip, visible bundle transition, and
  Application Support continuity.
- [ ] Screen-reader and full keyboard traversal of affected branded surfaces.

<a id="manual-v2-4-evidence-boundary"></a>
#### Evidence boundary

Automated and controlled native success does not prove the unchecked broad
interactive, permission-denied, screen-reader, long-path/UNC, or native
Linux/macOS scenarios. Cross-target compilation is not native execution.
No OmniExplorer UI, renderer, launcher, package, or external listener is part of
this checklist.

<a id="manual-v2-4-defects-found-during-final-native-validation"></a>
#### Defects found during final native validation

- The original disconnect monitor polled `NamedPipeServerStream.IsConnected`,
  which did not promptly observe a disappeared peer during active provider
  work. It now uses a cancellable asynchronous read probe; native and automated
  regression tests observe provider cancellation promptly.
- The accept loop originally created the next named-pipe instance outside its
  failure boundary. Twenty-four simultaneous clients could exhaust the Windows
  instance limit and fault host disposal. Creation is now guarded with bounded
  retry/backoff and native saturation completes with deterministic busy
  responses.
- The original 54-character random pipe name exceeded macOS's 104-character
  full Unix-domain socket path limit after .NET added the host temp-directory
  prefix. The compact endpoint retains 128 bits of randomness in 36 characters.
- Inno Setup reused the v2.3 Start Menu group name during an AppId-compatible
  upgrade. `UsePreviousGroup=no` now preserves the installer identity while
  replacing the old group with a single visible OmniSorSe group.

</details>

<a id="manual-v2-3"></a>
## v2.3

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v2.3.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Status:** v2.3.0 release evidence tracker. Unchecked items remain explicit
post-release/manual/community work rather than implied validation.

This checklist separates automated evidence from tests that require a native
provider, interactive desktop session, or another operating system. Do not
mark a scenario complete because a fake provider or cross-target build passed.

<a id="manual-v2-3-automated-evidence"></a>
#### Automated evidence

The final totals and gate results must be filled only after the fresh final
validation pass.

- [x] Clean/no-cache restore completed.
- [x] Non-incremental Debug build completed with zero warnings/errors.
- [x] Complete Debug suite passed: 1,637 passed, zero failures/skips.
- [x] Non-incremental Release build completed with zero warnings/errors.
- [x] Complete Release suite passed: 1,637 passed, zero failures/skips.
- [x] Search relevance, performance, media/indexing, SQLite migration/recovery,
  privacy, accessibility, Ollama, file-operation safety, and policy subsets
  passed.
- [x] Release cross-target compilation passed for win-x64, linux-x64, osx-x64,
  and osx-arm64 with expected application-owned native assets.

These checked items are automated Windows-host evidence from 12 August 2026.
Controlled native validation used explicit temporary paths for official
whisper.cpp 1.9.2, a local tiny English model, and ffmpeg/ffprobe 9.0; none is
bundled with OpenSorSe. Tesseract was not installed: a reviewed installer was
downloaded and verified by Windows Package Manager, but host installation was
cancelled with `0x800704c7`, so no fresh native v2.3 OCR claim is made.
Cross-target compilation does not represent native Linux or macOS execution.

<a id="manual-v2-3-deterministic-content-intelligence"></a>
#### Deterministic Content Intelligence

- [x] Index a synthetic document about Raspberry Pi, Docker, Prometheus, and
  Grafana; inspect bounded topics, textual entities, provenance, and extractive
  summary.
- [x] Confirm bounded topic/entity/keyword counts and generic-term suppression
  through the controlled provider harness and automated stop-topic coverage.
- [ ] Confirm deterministic ordering is stable after repeated unchanged runs.
- [ ] Disable topics, entities, and summaries independently and confirm the
  corresponding work is absent after applicable reprocessing.
- [ ] Change one relevant bound and confirm Content Intelligence is invalidated;
  change an unrelated UI setting and confirm it is reused.
- [x] Search topic/entity/summary-only phrases and inspect their exact explanation
  and bounded snippet source.
- [x] Confirm an exact filename remains above a weak derived-only match.
- [ ] Clear Content Intelligence for one file and confirm the original file is
  byte-for-byte unchanged.
- [ ] Forget a source and clear the index; confirm no stale derived Search or
  Related Files evidence remains.

<a id="manual-v2-3-local-whispercpp-provider"></a>
#### Local whisper.cpp provider

- [x] Configure the reviewed official whisper.cpp 1.9.2 Windows x64 executable
  and a controlled local tiny English GGML model; confirm capability state is
  Available. The runtime archive's published SHA-256 was independently matched.
- [x] Transcribe a short synthetic WAV containing known words and find it through
  normal Search.
- [x] Inspect two bounded timestamp segments and a transcript-specific Search reason.
- [ ] Re-index the unchanged recording and confirm transcript cache reuse.
- [ ] Change model/runtime metadata and confirm relevant transcript invalidation.
- [x] Cancel an active transcription; confirm process-tree termination, no
  false-complete cache, retry availability, and temporary-workspace cleanup.
- [ ] Trigger the configured timeout and verify the same consistency guarantees.
- [ ] Configure a missing runtime, missing model, too-small/invalid model, and
  inaccessible path; confirm actionable unavailable state and ordinary Search.
- [x] Transcribe video audio through an owned temporary WAV and verify cleanup
  after success. Failure/cancellation cleanup remains covered deterministically,
  not claimed as a separate native video run.
- [x] Confirm the OpenSorSe adapter performs no runtime/model download and does
  not route transcription through Ollama; runtime/model acquisition for this
  test was an explicit release-engineering action.

<a id="manual-v2-3-native-media-tool-smoke"></a>
#### Native media-tool smoke

- [x] Run real ffprobe 9.0 through the OpenSorSe provider for controlled WAV and
  MP4 fixtures; verify duration, codecs, sample rate/channels, resolution, and
  frame rate.
- [x] Run real ffmpeg 9.0 through the bounded frame sampler; verify one sampled
  frame for the short fixture, byte bounds, configured cap, and workspace cleanup.
- [ ] Run real Tesseract through the final v2.3 OCR path. Installation was
  cancelled by the Windows host; automated provider tests and prior v2.2 native
  evidence are retained without being relabelled as a v2.3 native pass.

<a id="manual-v2-3-visual-understanding-and-semantic-fallback"></a>
#### Visual understanding and semantic fallback

- [ ] Confirm visual descriptions report unavailable/not configured and image,
  video, Search, and Related Files behavior remains usable.
- [ ] Confirm enabling the unavailable switch does not send an image/frame to
  Ollama.
- [ ] Confirm existing bounded local related-concept Search works with Ollama,
  transcription, and visual descriptions all unavailable.

<a id="manual-v2-3-cross-media-related-files"></a>
#### Cross-media Related Files

- [ ] Create a PDF and audio transcript sharing two specific topics; confirm an
  explainable relationship without matching filenames.
- [ ] Confirm one generic topic or same camera/device alone does not create a
  relationship.
- [ ] Inspect shared-topic and textual-entity evidence and confidence.
- [ ] Clear/forget one member's intelligence and confirm stale relationship
  evidence is removed or rebuilt through the existing relationship controls.

<a id="manual-v2-3-schema-4-to-5-migration"></a>
#### Schema 4 to 5 migration

- [x] Open a controlled genuine v2.2 schema-4 index, generated by exact v2.2.0
  source in a detached worktree, through the current store.
- [x] Confirm the schema advances to 5 and `content_intelligence_json` exists.
- [x] Confirm v2.2 document/media Search data remains searchable and source
  fixtures remain untouched.
- [x] Populate Content Intelligence and relationship terms, reopen, and confirm
  the migration is not repeated and the recovery-backup count remains stable.
- [ ] Exercise interrupted/mismatched migration recovery and unsupported-newer
  schema refusal.
- [ ] Corrupt one intelligence record and confirm Search returns a valid fallback
  document with a visible failure, not a corrupt partial object.

<a id="manual-v2-3-windows-interactive-desktop"></a>
#### Windows interactive desktop

- [ ] Inspect Content Intelligence settings at normal and narrow window sizes.
- [ ] Use keyboard-only navigation and verify visible focus, meaningful labels,
  switch states, help, indexed-data inspection, clear, and Search result details.
- [ ] Confirm status wording distinguishes Disabled, Not configured,
  Unavailable, Available, Processing, Error, and cancellation where applicable.
- [ ] Confirm background indexing makes filename/text Search available before
  expensive optional transcription finishes.
- [ ] Inspect Advanced Diagnostics and confirm full document/OCR/transcript/
  summary content is absent by default.

<a id="manual-v2-3-linux-and-macos-native-validation"></a>
#### Linux and macOS native validation

- [ ] Linux x64 native launch, deterministic extraction, Search, and optional
  whisper.cpp capability/process validation.
- [ ] Intel macOS native launch, deterministic extraction, Search, and optional
  whisper.cpp capability/process validation.
- [ ] Apple Silicon macOS native launch, deterministic extraction, Search, and
  optional whisper.cpp capability/process validation.

Cross-target compilation alone must not check these native scenarios.

<a id="manual-v2-3-safety-and-privacy"></a>
#### Safety and privacy

- [ ] Verify no source document/media file changes during extraction,
  transcription, Search, relationships, clear, forget, migration, or repair.
- [ ] Verify no telemetry, hidden web lookup, silent upload, model download,
  facial recognition, person identification, or autonomous file operation.
- [ ] Configure a non-loopback Ollama-compatible endpoint and confirm its remote
  privacy warning remains separate from deterministic Content Intelligence.
- [ ] Review an exported diagnostic bundle for private content and paths before
  sharing.

</details>

<a id="manual-v2-2"></a>
## v2.2

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v2.2.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Status:** controlled Windows native-provider, real local Tesseract, and
published-v2.1 migration checks completed on 2026-08-11 are checked below.
Interactive desktop and native Linux/macOS scenarios remain unchecked. A
checked item is not a claim that a broader UI or platform scenario was
exercised.

Use only synthetic or intentionally shareable test media. Before and after the
session, verify that no source media file was changed.

<a id="manual-v2-2-completed-controlled-native-host-evidence"></a>
#### Completed controlled native-host evidence

These checks ran OpenSorSe provider code from a disposable ignored harness;
they were not mocked and did not inspect personal media.

- [x] Published v2.1.0 binaries created a genuine schema-3 index with one
  searchable record; the current store migrated it to schema 4, created one
  recovery backup and the media table, retained Search data, reopened cleanly,
  did not rerun migration, and preserved the source fixture hash.
- [x] A generated valid JPEG APP1/EXIF fixture returned dimensions,
  orientation, make/model, capture text, and intentionally embedded GPS through
  the real image metadata provider. Missing/corrupt EXIF isolation remains
  covered by the automated suite.
- [x] A real decodable generated PNG produced a lazy application-owned Skia
  preview; a 4×2 orientation-6 source produced a 2×4 preview, reused the same
  cache entry, cleared the cache, and preserved the source hash.
- [x] Gyan FFmpeg/ffprobe 9.0 (temporary unbundled validation copy) supplied
  real metadata for generated MP3, WAV, FLAC, M4A, and MP4 fixtures through
  `FfprobeMediaMetadataProvider`.
- [x] The real 4-second 640×360, 25-fps H.264/AAC MP4 produced one bounded
  interior frame at 2 seconds through `FfmpegVideoFrameSampler`; the encoded
  frame was 82,943 bytes and the owned workspace was removed.
- [x] The real process runner cancelled and timed out controlled long-running
  ffmpeg operations, requested process-tree termination, and left no completed
  provider result.
- [x] Invalid ffprobe/ffmpeg executable paths returned `Unavailable` without a
  crash or false success.
- [x] A checksum-verified Tesseract 5.5.3 validation copy and the official
  English `tessdata_fast` model were extracted into temporary storage without
  system registration. OpenSorSe's real CLI engine recognized `QUARTZ NEBULA
  DOCKER VALIDATION 74291` from a generated `capture-2026.png`; the filename
  and path contained none of the query words. Unified Search returned the
  image with a `media OCR` component and `MediaOcr` snippet, the source hash
  was unchanged, an invalid executable path stayed unavailable, and disabled
  OCR stopped before provider execution. All temporary runtime/model/image
  material was removed after validation.
- [ ] Real speech transcription. No concrete provider/runtime/model is shipped
  or configured, so transcript Search is not claimed.
- [ ] Interactive window-size, keyboard, screen-reader, wheel, and Windows
  125%/150% scaling checks. XAML/accessibility regression tests pass, but they
  are not a substitute for maintainer interaction.

<a id="manual-v2-2-issue-readiness-recommendation-issues-remain-open"></a>
#### Issue-readiness recommendation (issues remain open)

This table separates implementation/automated evidence from still-unperformed
interactive checks. It is a release-review recommendation, not an issue-closure
record.

| Issue | Recommendation | Evidence and remaining interaction |
| --- | --- | --- |
| #27 Scan ETA | Fixed and automated | A monotonic, stage-aware smoothed estimate appears only after enough homogeneous work, resets when the workload changes, and terminates on success, failure, or cancellation. Visual observation during a long heterogeneous scan remains unchecked. |
| #28 Multi-group duplicate removal | Fixed and automated | Selections persist across groups, every group must retain a keeper, one reviewable Change Plan is created, cancellation emits no plan, and the duplicate projection changes only after completely successful execution. Interactive Change Plan execution and partial-failure recovery remain unchecked. |
| #29 Virtual Collections scrolling | Fixed and automated | The collections list owns a bounded grid row and vertical scroll surface; adjacent detail content scrolls independently. Mouse-wheel, high-DPI, resize, and focus-following observation remain unchecked. |
| #30 Related Files destination | Fixed and automated | Related Files is one primary destination. Collections retains a hidden compatibility tab/route only, so old internal navigation does not break. Interactive navigation remains unchecked. |
| #31 Related Files scrolling | Fixed and automated | The status region is bounded and the tab pages own vertical scroll surfaces with stretched content. The requested window-size/scaling matrix remains unchecked interactively. |
| #32 local/privacy wording | Fixed and automated | File analysis is described as local indexing; local and non-loopback AI endpoints are distinguished, and remote endpoints carry an explicit disclosure. Interactive screen-reader review remains unchecked. |
| #33 Search discoverability | Fixed and automated | Search is a primary destination with a unified media-aware search box and Hybrid/Hybrid + AI explanation. Interactive first-use and keyboard-navigation observation remains unchecked. |

No issue was closed during this implementation-only pass.

<a id="manual-v2-2-settings-and-capability-states"></a>
#### Settings and capability states

- [ ] Open Settings with a fresh profile and confirm metadata is enabled while
  image OCR, transcription, frame analysis, and visual descriptions are off.
- [ ] Use keyboard-only navigation through every Media Intelligence switch,
  bound, path field, capability button, status, and Help route.
- [ ] Confirm screen-reader names describe each switch and capability state.
- [ ] Check capabilities with no ffprobe, ffmpeg, transcription provider, or
  visual provider configured; unavailable features are explained as optional.
- [ ] Configure a valid local ffprobe/ffmpeg path and recheck capability.
- [ ] Enter invalid/relative executable paths and confirm Save fails safely.
- [ ] Disable Media Intelligence and confirm ordinary document/filename Search
  remains available.

<a id="manual-v2-2-images"></a>
#### Images

- [ ] Index synthetic JPEG, PNG, WebP, BMP, and TIFF images.
- [ ] Verify dimensions and available EXIF make/model/orientation/capture date.
- [ ] Verify missing EXIF is normal and malformed EXIF fails only that evidence.
- [ ] Index a synthetic GPS-tagged image; inspect local stored categories and
  confirm no external geocoding/network request occurs.
- [ ] Enable image OCR with a configured local engine and find a screenshot by
  visible command text.
- [ ] Stop/remove OCR and confirm the item waits or reports capability without
  blocking filename Search.
- [ ] Inspect an indexed image and confirm its bounded preview loads lazily.
- [ ] Clear media-derived data and confirm the preview/index data is removed but
  the original image is byte-for-byte unchanged.
- [ ] Test a corrupt, truncated, oversized, permission-denied, and disappearing
  image; the indexing run continues.

<a id="manual-v2-2-audio"></a>
#### Audio

- [x] With local ffprobe available, index synthetic MP3, WAV, FLAC, and M4A and
  inspect duration/codec/sample/channel/title metadata where present.
- [ ] Test an unsupported codec in a recognized container and inspect the
  actionable per-file failure.
- [ ] Enable transcription with no provider and confirm the truthful
  not-configured/waiting state.
- [ ] If a reviewed local transcription provider is later configured, verify a
  known phrase and timestamped segments through normal Search.
- [ ] Cancel transcription and verify prompt cooperative cancellation.
- [ ] Re-index an unchanged recording and verify completed compatible work is
  reused.
- [ ] Exceed audio duration and file-size limits and verify intentional skips.

<a id="manual-v2-2-video"></a>
#### Video

- [ ] With local ffprobe available, index synthetic MP4, MOV, MKV, and AVI and
  inspect available duration/resolution/frame-rate/codec/device metadata.
- [x] Enable frame analysis with local ffmpeg and verify at most the configured
  representative-frame count is produced.
- [x] Verify a short clip uses one interior frame.
- [ ] Verify with a real long clip that sampling uses one frame per started five
  minutes up to the configured cap. The deterministic algorithm is automated,
  but no large native fixture was generated for this pass.
- [ ] Enable frame OCR and find a synthetic screen recording by sampled text.
- [ ] Confirm at most the configured OCR frame count is processed.
- [ ] Remove/stop ffmpeg during processing and confirm the file fails/waits
  safely without aborting the run.
- [ ] Cancel frame extraction and confirm the managed temporary workspace is
  removed.
- [ ] Exceed the video duration/file-size limits and verify no unbounded work.
- [ ] Confirm video playback, editing, whole-frame analysis, and video previews
  are not presented as implemented.

<a id="manual-v2-2-search-and-related-files"></a>
#### Search and Related Files

- [ ] Find media by exact filename and confirm it outranks weak derived evidence.
- [ ] Find an image by camera/device metadata.
- [ ] Find an image/video by OCR and inspect the source-labelled snippet.
- [ ] If transcription is configured, find audio/video by transcript and inspect
  the source-labelled snippet.
- [ ] Confirm optional visual descriptions are unavailable unless a compatible
  explicitly configured local provider exists.
- [ ] Confirm explanations distinguish media metadata, transcript, media OCR,
  and optional visual description.
- [ ] Search while media indexing is running, paused, cancelled, and waiting for
  a dependency; incomplete coverage remains clear.
- [ ] Confirm a same-camera match alone does not create a Related Files link.
- [ ] Confirm exact matching transcript/OCR evidence produces an explainable
  relationship without overwhelming deterministic filename matches.

<a id="manual-v2-2-privacy-diagnostics-and-recovery"></a>
#### Privacy, diagnostics, and recovery

- [ ] Inspect media-derived data categories for an image, audio file, and video.
- [ ] Clear OCR-derived data, media-derived data, and all generated Search data;
  verify original files remain unchanged.
- [ ] Forget a media file and a media source; verify watched/manual source
  ownership and immediate re-index suppression remain correct.
- [ ] Restart during media processing and verify no stale running job remains.
- [ ] Retry a requested dependency after it becomes available.
- [ ] Review Advanced Diagnostics for provider, status, duration, cache, frame
  count, evidence sizes, warnings, failure, and cancellation.
- [ ] Confirm ordinary diagnostic summaries do not expose media bytes, complete
  transcripts/OCR, descriptions, or precise GPS.
- [ ] Review an exported diagnostics bundle before sharing.
- [ ] Monitor CPU, memory, temporary disk, and index growth using a deliberately
  bounded mixed-media folder under Eco, Balanced, and Fast modes.
- [ ] Verify pause, resume, cancel, quota cleanup, compaction, shutdown, and
  restart recovery with mixed document/media indexing.

<a id="manual-v2-2-cross-platform-native-follow-up"></a>
#### Cross-platform native follow-up

- [x] Windows x64: run image metadata/preview, a contained local Tesseract
  validation copy, and locally configured ffprobe/ffmpeg tools. Transcription
  was not available because no concrete provider ships.
- [ ] Linux x64: build/run from source and test only installed local tools.
- [ ] macOS Intel: run image metadata/preview and any locally configured tools.
- [ ] macOS Apple Silicon: run image metadata/preview and any locally configured
  tools.
- [ ] Record exact external-tool build, license, version, architecture, and codec
  availability for every native claim.

</details>

<a id="manual-v2-1"></a>
## v2.1

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v2.1.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

This is an evidence tracker, not a claim of completed testing. Every scenario
is intentionally unchecked. Use only synthetic/disposable files and record the
host OS, filesystem, Ollama version/model, OCR version, and application commit
when a maintainer performs a scenario.

<a id="manual-v2-1-search-and-results"></a>
#### Search and results

- [ ] Exact complete filename ranks first over weak content or related evidence.
- [ ] Exact filename stem, prefix, substring, separator, casing, punctuation,
      diacritic, and adjacent-transposition behavior is relevant and bounded.
- [ ] **Why this result?** agrees with the actual filename/path/metadata/text/OCR
      evidence and the snippet is bounded and source-labelled.
- [ ] Hybrid Search remains responsive during active and incomplete indexing.
- [ ] Incomplete coverage is visible and no-result wording is non-definitive.
- [ ] Copy full path, Open file, Open folder, details, and Change Plan handoff are
      keyboard accessible and work on the host platform.
- [ ] Search cancellation and rapid overlapping queries leave one coherent result.

<a id="manual-v2-1-ollama-and-optional-ai"></a>
#### Ollama and optional AI

- [ ] AI settings, endpoint, model discovery, runtime state, timeout, and Search
      assistance are discoverable without unrelated Advanced settings.
- [ ] Localhost/IPv4-loopback/IPv6-loopback endpoints display **Local endpoint**.
- [ ] A non-loopback endpoint displays the remote privacy warning before use.
- [ ] Installed models appear deterministically and a missing saved model falls
      back with an explicit Save instruction.
- [ ] Search works normally with Ollama stopped or not installed.
- [ ] Ollama stopped mid-request, timeout, malformed output, missing model, and
      cancellation preserve deterministic results without freezing the UI.
- [ ] AI-assisted Search cannot introduce an unknown file or promote weak evidence
      above an exact filename.

<a id="manual-v2-1-manual-validation-fixes"></a>
#### Manual-validation fixes

- [ ] Scan elapsed time updates throughout a long scan and freezes at the truthful
      completion duration.
- [ ] Failed and cancelled scans freeze their duration; a pre-start validation
      failure does not reuse a previous scan's duration.
- [ ] Duplicate review shows every relevant path before removal planning.
- [ ] Selecting all known copies disables safe removal; leaving at least one copy
      enables a reviewable Change Plan.
- [ ] Applying duplicate safe removal moves only selected copies into
      `.opensorse/duplicate-recovery`, updates the duplicate review, and records
      Operation History.
- [ ] Undo restores a safely removed copy when conflict checks still pass.
- [ ] The notification badge opens/closes by click and keyboard; Escape closes it;
      individual dismissal and Clear all do not erase Advanced Diagnostics.
- [ ] File analysis wording states local indexing needs no AI; AI assistance and
      endpoint privacy are separate.
- [ ] Related Files explains relationships in ordinary language and every visible
      relationship still has inspectable evidence.
- [ ] Search shows Hybrid / Hybrid + AI assistance without implying Ollama performs
      the file search.

<a id="manual-v2-1-contextual-help-and-legacy-workflows"></a>
#### Contextual Help and legacy workflows

- [ ] Contextual `?` opens the correct section for Search, Duplicates, Related
      Files, Watched Folders, Workflows, Settings, and AI/Ollama.
- [ ] Help covers Getting Started, Scan, Results, Organize/Change Plans,
      Diagnostics, Privacy, and Troubleshooting with current labels.
- [ ] Select a synthetic file, request an AI rename, review/edit the suggestion,
      create and validate the Change Plan, execute it, and confirm Results refresh.
- [ ] Preview a disposable folder restructure, review every move, explicitly
      confirm, execute, and verify history/repeat protection.
- [ ] Stop Ollama during an AI folder suggestion and verify actionable failure
      without any file change.

<a id="manual-v2-1-packaging-and-accessibility"></a>
#### Packaging and accessibility

- [ ] Windows installer install/start/stop/uninstall behavior matches policy.
- [ ] Windows portable package starts without a separately installed .NET runtime.
- [ ] Intel and Apple Silicon DMGs expose correct app metadata/architecture on
      their native hosts.
- [ ] Keyboard focus order, accessible names/live regions, high contrast, scaling,
      and screen-reader output are usable on a representative host.
- [ ] No screenshot, diagnostic export, or bug report includes private filenames,
      paths, content, prompts, tokens, or credentials without explicit review.

</details>

<a id="manual-v2-0"></a>
## v2.0

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v2.0.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Status:** Implementation-candidate maintainer checklist. Every scenario is
intentionally unchecked and no manual outcome is claimed.

Record the exact commit, OS/runtime, filesystem, application-data location,
graph/index settings, dependencies, test source, observed result, diagnostics,
and cleanup for each scenario. Use synthetic/reviewed data; never infer an
Ollama, OCR, accessibility, battery, lock, low-disk, or performance result from
automation.

<a id="manual-v2-0-installation-enablement-and-compatibility"></a>
#### Installation, enablement, and compatibility

- [ ] Upgrade a representative v1.9 profile and confirm existing Search,
      Relationships, Collections, sources, and settings before enabling graph.
- [ ] Review the graph privacy/storage explanation and decline enablement;
      confirm v1.9 behavior remains usable.
- [ ] Enable graph and observe bootstrap state, stage, progress, coverage,
      current safe item, counts, storage, and estimated time where meaningful.
- [ ] Close/reopen with graph disabled, partially built, and complete.
- [ ] Disable an already-built graph and confirm graph work/query stops while
      disclosed retained data and ordinary v1.9 features remain available.
- [ ] Run the reviewed v1.9 rollback procedure and confirm it ignores/preserves
      graph sidecars.
- [ ] While rolled back, change and remove representative v1.9 relationship and
      Smart Collection decisions; return to v2 and confirm the non-authoritative
      graph projection reconciles without resurrecting or duplicating them.

<a id="manual-v2-0-lifecycle-interruption-and-recovery"></a>
#### Lifecycle, interruption, and recovery

- [ ] Pause and resume bootstrap/projection.
- [ ] Restart while paused and confirm it remains paused.
- [ ] Cancel and explicitly retry.
- [ ] Force application termination during observation capture.
- [ ] Force application termination during candidate extraction.
- [ ] Force application termination during identity resolution.
- [ ] Force application termination during edge publication.
- [ ] Force application termination during maintenance/cleanup.
- [ ] Restart after each interruption and inspect resume/recovery facts.
- [ ] Repeatedly pause/resume and cancel/retry without duplicate or stale work.
- [ ] Exit during active graph work and confirm shutdown remains responsive.

<a id="manual-v2-0-graph-quality-explanations-and-control"></a>
#### Graph quality, explanations, and control

- [ ] Inspect File, Source, Folder, Collection, Document Set, and manual entity
      list/detail behavior where applicable; confirm accepted Tag nodes are not
      presented as stable without the separately approved tag contract.
- [ ] Confirm direct edges show relationship, evidence, confidence level,
      algorithm/version, freshness, and origin.
- [ ] Confirm an ambiguous similar-name case remains separate.
- [ ] Create and rename a manual entity.
- [ ] Link and unlink a manual edge.
- [ ] Merge compatible manual/experimental entity candidates and verify the
      explanation/control state.
- [ ] Split entities and verify they remain separate after restart/rebuild.
- [ ] Attempt to merge incompatible mechanical node kinds and confirm the action
      is unavailable or rejected without changing either identity.
- [ ] Confirm existing Same Project/Purchase/Trip/Topic relationships and
      Collection context are not presented as resolved real-world entities.
- [ ] Reject/ignore a suggestion and confirm it does not immediately return.
- [ ] Sort/filter/page related items and inspect bounded limit messages.
- [ ] Exercise a cyclic/high-degree synthetic graph and confirm navigation is
      bounded and responsive.
- [ ] Enable any experimental two-hop/suggestion feature only after reviewing
      its label and opt-in warning; confirm it can be disabled.

<a id="manual-v2-0-search-and-progressive-coverage"></a>
#### Search and progressive coverage

- [ ] Search exact filenames with graph disabled, building, paused, stale,
      under maintenance, complete, and unavailable.
- [ ] Confirm exact/literal results remain above graph-only context.
- [ ] Inspect “Why this result?” for a graph-expanded result and verify the
      actual edge/evidence supports it.
- [ ] Disable graph Search expansion and confirm ordinary v1.9 results remain.
- [ ] Toggle direct v1.9 relationship context and graph context independently;
      confirm disabling graph context does not disable or alter the existing
      relationship-context behavior.
- [ ] Use a result reachable through both v1.9 relationship context and its
      graph projection; confirm one result, one contextual score contribution,
      one truthful explanation, and the combined expansion limit.
- [ ] Search during graph publication/repair/cleanup and confirm no malformed
      partial result appears.
- [ ] Inspect indexing coverage and graph-projection coverage independently for
      incomplete/stale/failed/excluded/dependency cases; confirm neither layer
      is presented as the other.
- [ ] Confirm a no-result state does not imply exhaustive graph coverage while
      projection is incomplete.

<a id="manual-v2-0-fileindex-changes-and-selective-repair"></a>
#### File/index changes and selective repair

- [ ] Rename a synthetic file and confirm compatible identity/decisions follow.
- [ ] Move a synthetic file within a source.
- [ ] Move a synthetic file across sources and inspect ambiguity/ownership.
- [ ] Modify metadata only and inspect targeted invalidation.
- [ ] Modify content and inspect targeted graph invalidation/reprojection.
- [ ] Delete a file and confirm stale graph/Search visibility is removed.
- [ ] Remove a source and confirm completed reconciliation cleans its graph.
- [ ] Verify/repair one edge.
- [ ] Verify/repair one file/entity/collection/source.
- [ ] Rebuild a selected component while prior valid graph remains usable.
- [ ] Perform the reviewed full derived-graph rebuild last-resort scenario and
      confirm all manual decisions survive.
- [ ] Unlink a projected v1.9 Related File edge and confirm the existing
      relationship/never-relate state changes first, no duplicate graph-native
      decision is created, and the graph refreshes from the completed manifest.
- [ ] Split a projected v1.9 Smart Collection membership from the graph and
      confirm the existing collection-member override persists; verify source,
      folder, and exact-document-set structural edges expose no unlink action.

<a id="manual-v2-0-privacy-and-original-file-safety"></a>
#### Privacy and original-file safety

- [ ] Inspect stored graph data, evidence references, decisions, history, and
      storage use for a selected item/source.
- [ ] Forget graph data for a file and confirm the original remains unchanged.
- [ ] Forget graph data for a source and confirm original files/source
      registration remain unchanged.
- [ ] Exclude a folder/file type and confirm it cannot leak through stale graph
      Search while cleanup is pending.
- [ ] Review decision/graph database locations, retention, backup, and recovery
      explanations, including that sidecars, decisions, aliases, labels,
      evidence references, backups, and quarantine copies are sensitive local
      metadata and are not claimed to be encrypted.
- [ ] Clear derived graph data while retaining graph-native decisions, then
      rebuild and confirm those decisions are reapplied.
- [ ] On a disposable corrupt derived graph sidecar, use the maintainer recovery
      integration with exact confirmation `REBUILD DERIVED GRAPH STORE`; confirm
      the corrupt database family is quarantined, a fresh graph store validates,
      and decision, `deep-index.db`, and source-file hashes remain unchanged.
- [ ] Interrupt disposable derived-store recovery during quarantine/promotion,
      restart, re-enter the reviewed recovery path, and confirm the journal
      resumes while the quarantine remains available for inspection.
- [ ] Confirm derived-store recovery rejects a healthy store, a corrupt decision
      authority store, and an unsupported newer graph schema without replacing
      any of them.
- [ ] Clear graph-native decisions and their backups through the separate
      irreversible confirmation; confirm original files and v1.9 decisions are
      unchanged and graph processing does not restart until explicitly enabled.
- [ ] Complete a forget action, exercise the reviewed backup-restore path, and
      confirm an older backup cannot resurrect forgotten active graph data;
      inspect the disclosed minimum tombstone retention.
- [ ] Through the maintainer recovery service/integration path (not an end-user
      restore button), list recovery points and confirm that only bounded IDs,
      sequences, generations, times, and status codes appear, not paths or
      indexed content.
- [ ] Confirm that a wrong confirmation, missing point, corrupt or foreign
      point, unsupported newer schema, and point below the retained privacy
      floor are each rejected without replacing the decision store.
- [ ] Interrupt a disposable restore after the promotion journal reaches its
      promoting state, restart/reinitialize, and confirm the operation either
      finishes or rolls back before normal graph access.
- [ ] Restore a verified disposable recovery point with the exact confirmation
      `RESTORE GRAPH DECISIONS`; compare source-file and `deep-index.db` hashes
      before and after to confirm they remain unchanged.
- [ ] On a disposable profile, make the decision store unavailable/corrupt and
      confirm graph reads and Search expansion fail closed while ordinary v1.9
      Search remains usable.
- [ ] Review a diagnostics export before sharing and confirm private content,
      aliases, full paths, queries, OCR, summaries, prompts, vectors, and secrets
      are absent by default.
- [ ] Compare checksums/timestamps of source fixtures before/after all graph,
      forget, merge/split, repair, and rebuild actions.

<a id="manual-v2-0-dependency-and-resource-failure"></a>
#### Dependency and resource failure

- [ ] Use stable graph/Search with Ollama unavailable.
- [ ] Restore Ollama and confirm only eligible optional work resumes.
- [ ] Use stable graph/Search with OCR unavailable.
- [ ] Restore OCR and inspect partial coverage recovery.
- [ ] Hold the graph database with a second process and inspect bounded
      busy/locked behavior and fallback.
- [ ] Exercise the reviewed low-disk/quota scenario and inspect
      Waiting-for-resources behavior without silent deletion.
- [ ] Restore disk/resources and resume safely.
- [ ] Run Search while graph maintenance/compaction is active.

<a id="manual-v2-0-accessibility-and-responsiveness"></a>
#### Accessibility and responsiveness

- [ ] Navigate Graph overview/list/detail/evidence/privacy/repair using keyboard
      only with logical focus and visible focus state.
- [ ] Activate every explanation/help/control without hover.
- [ ] Verify meaningful screen-reader names, roles, states, progress, errors,
      stale/partial coverage, and confirmation dialogs on a supported host.
- [ ] Verify mouse and touch/click targets where supported.
- [ ] Verify progress/live announcements are useful and not repetitive.
- [ ] After paging, async refresh, merge, split, forget, repair, and removal,
      confirm selection/focus remains on the valid item or moves to the
      documented logical target without an older response replacing it.
- [ ] Verify high-contrast themes and supported text scaling preserve all
      content, controls, focus indicators, and bounded layouts.
- [ ] Confirm confidence, freshness, failures, and limits remain understandable
      without color, and every destructive index-only confirmation states that
      original files are unaffected.
- [ ] Page/filter a large synthetic graph without UI/accessibility-tree hang.
- [ ] Cancel long work and repeated Search promptly while the UI remains usable.

<a id="manual-v2-0-existing-feature-smoke-tests"></a>
#### Existing-feature smoke tests

- [ ] Existing indexing pause/resume/cancel/recovery still works.
- [ ] Existing Search and v1.9 contextual expansion still work.
- [ ] Existing Relationships/Collections/manual overrides still work.
- [ ] Watched-folder ownership/isolation still works.
- [ ] Catalogs, saved scans, and duplicate detection still work.
- [ ] Workflows and plugins still operate within existing authority.
- [ ] Create/review a Change Plan without graph side effects.
- [ ] Execute the approved synthetic Change Plan through the journal and Undo;
      confirm graph only observes the committed result.

<a id="manual-v2-0-distribution-and-community-testing-handoff"></a>
#### Distribution and community-testing handoff

- [ ] Install the official Windows x64 setup package on a representative
      non-development machine; record SmartScreen/signing behavior without
      treating a checksum as publisher authentication.
- [ ] Launch, close, uninstall, and confirm application data is preserved until
      the user explicitly removes it.
- [ ] Extract and launch the official Windows x64 portable ZIP from a clean
      directory.
- [ ] Mount, copy, launch, close, and remove the official Intel macOS app on a
      native Intel host; record Gatekeeper behavior.
- [ ] Mount, copy, launch, close, and remove the official Apple Silicon macOS app
      on a native Apple Silicon host; record Gatekeeper behavior.
- [ ] Build and launch the documented Linux x64 source preview on a native
      graphical host; do not infer an installer that is not published.
- [ ] Verify every downloaded artifact against
      `OpenSorSe-v2.0.0-SHA256SUMS.txt` from the same official release.
- [ ] Capture privacy-reviewed real application screenshots with synthetic data
      by completing [the screenshot checklist](images/README.md#historical-v2-0-screenshots).
- [ ] Begin friend/community testing only after publication; record tester host,
      version, exact scenario, expected/observed behavior, reviewed diagnostics,
      and whether a v2.0.x fix is required.
- [ ] Confirm no distribution, screenshot, bug report, or shared diagnostics
      contains personal filenames, paths, contents, prompts, secrets, tokens,
      databases, indexes, or unreviewed logs.

<a id="manual-v2-0-completion-record"></a>
#### Completion record

- [ ] Every completed item includes exact host/dependency evidence.
- [ ] All failures are classified and release blockers are resolved/retested.
- [ ] Test data, graph/index copies, diagnostics, and temporary backups are
      reviewed and safely cleaned up.
- [ ] No scenario is marked complete based solely on automated validation.

</details>

<a id="manual-v1-9"></a>
## v1.9

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v1.9.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

This checklist is intentionally an unexecuted evidence template. Every scenario
must remain unchecked until a maintainer performs and records the observation
on the intended host. Do not infer outcomes from automated tests.

Record before testing: branch and commit; operating system/version; filesystem;
desktop environment; power state; indexing policy; OCR executable/version;
Ollama endpoint/model; accessibility technology; synthetic test root; and
application-data backup location.

<a id="manual-v1-9-installation-migration-and-recovery"></a>
#### Installation, migration, and recovery

- [ ] Start from a backed-up v1.8 schema-2 index and verify transactional v1.9 migration preserves Search, sources, files, jobs, privacy rules, and retained content.
- [ ] Interrupt a disposable migration attempt and verify the original/recovery copy remains actionable.
- [ ] Present an unsupported newer schema and verify OpenSorSe fails safely without rewriting it.
- [ ] Present a deliberately corrupt disposable relationship record and verify inspection/repair is actionable and source files remain unchanged.
- [ ] Force application termination during relationship analysis, restart, and verify no stage remains permanently Running.
- [ ] Restart after completed relationship work and verify unchanged files are not unnecessarily reanalysed.

<a id="manual-v1-9-relationship-discovery-and-explanations"></a>
#### Relationship discovery and explanations

- [ ] Index a synthetic same-project set and verify only evidence-backed relationships appear.
- [ ] Index a synthetic trip set containing booking, ticket, photo metadata, GPX, expense, and packing records; verify conservative context and no invented events.
- [ ] Index a synthetic purchase set containing invoice, receipt, payment, warranty, and manual records; verify retained evidence matches the source signal.
- [ ] Verify identical content produces a Same Document Set relationship without duplicate source-file storage.
- [ ] Rename and move an unchanged file and verify identity reuse avoids unnecessary relationship analysis.
- [ ] Modify content and verify only affected relationship work is invalidated.
- [ ] Add unrelated distractors and inspect false positives.
- [ ] Add deliberately related but weak-evidence files and inspect false negatives.
- [ ] Repeat identical analysis and verify deterministic relationship type, confidence, ordering, and explanation.
- [ ] Verify malformed OCR, summary, metadata, Unicode, timestamps, and semantic data fail safely without an invented relationship.
- [ ] Verify confidence uses Low, Medium, High, or Confirmed and never presents an unexplained percentage.

<a id="manual-v1-9-smart-collections-context-and-timeline"></a>
#### Smart Collections, context, and timeline

- [ ] Open Collections and verify title, description, relationship summary, confidence, creation source, update time, and member count.
- [ ] Verify automatic membership updates incrementally when a synthetic member is added, changed, moved, or deleted.
- [ ] Inspect collection context and verify every displayed claim is supported by stored evidence.
- [ ] Inspect the timeline and verify it orders only available indexed timestamps and identifies each timestamp source.
- [ ] Rename and pin a collection; restart and verify both choices persist.
- [ ] Merge two collections and verify virtual membership changes without moving source files.
- [ ] Split a member, rerun analysis, and verify the member is not immediately re-added.
- [ ] Forget a collection, rerun analysis, and verify its automatic context is not immediately recreated.
- [ ] Exercise a cyclic set of manual relationships and verify the UI remains responsive and bounded.
- [ ] Exercise the configured maximum collection membership and verify excess derived membership is bounded and reported safely.

<a id="manual-v1-9-related-files-and-manual-control"></a>
#### Related Files and manual control

- [ ] Select an indexed file and verify Related Files shows relationship, evidence, confidence, origin, and validation time.
- [ ] Sort Related Files by confidence, relationship, filename, and last validation.
- [ ] Filter by relationship type and minimum confidence; clear filters and verify all eligible direct relationships return.
- [ ] Link two files manually using a standard category and verify the relationship persists across restart.
- [ ] Create a bounded Custom relationship and verify its label and evidence are inspectable.
- [ ] Unlink a relationship and verify the original files remain unchanged.
- [ ] Confirm and reject automatic relationships; restart and verify the decisions persist.
- [ ] Apply Always relate and Never relate, rerun analysis, and verify each override is respected.
- [ ] Verify relationship deletion and duplicate analysis do not create duplicate edges or memberships.

<a id="manual-v1-9-search-integration"></a>
#### Search integration

- [ ] Search an exact filename and verify it remains above a loosely related contextual result.
- [ ] Search for a synthetic invoice and verify a directly related warranty/receipt can appear with actual relationship evidence.
- [ ] Open Why this result? and verify relationship context appears only when it contributed to ranking.
- [ ] Disable Include related file context and verify contextual-only results disappear while ordinary Search remains functional.
- [ ] Search during active indexing, paused indexing, cleanup, and compaction; verify responsiveness and honest coverage.
- [ ] Search after relationship analysis is disabled or temporarily unavailable; verify filename, metadata, text, OCR, filters, and v1.8 explanations still work.
- [ ] Cancel and rapidly repeat contextual searches; verify prompt cancellation, deterministic results, and no partial/corrupt result objects.

<a id="manual-v1-9-privacy-forgetting-and-repair"></a>
#### Privacy, forgetting, and repair

- [ ] Disable relationship analysis globally and verify existing ordinary Search remains available.
- [ ] Exclude a configured file type and verify it is not analysed for relationships.
- [ ] Forget a selected file's relationship data and verify the source file bytes/timestamps remain unchanged.
- [ ] Forget and exclude a selected file; verify it is absent from Related Files, collection members/timeline, and contextual Search.
- [ ] Forget a manually managed source and verify source ownership/configuration remains intact.
- [ ] Forget a watched-folder source and verify watched-folder ownership remains intact.
- [ ] Rebuild a selected file and verify manual links/corrections remain while automatic data refreshes.
- [ ] Run derived-data repair and verify stale memberships, orphan evidence, and corrupt derived rows are handled without a full index rebuild.
- [ ] Inspect storage and privacy information and verify relationship counts are accurate after forget/rebuild/repair.
- [ ] Review relationship diagnostics and verify counts, timing, algorithm version, and repair operations do not expose document text, OCR text, summaries, vectors, secrets, or unnecessary paths.

<a id="manual-v1-9-dependency-performance-and-resource-behavior"></a>
#### Dependency, performance, and resource behavior

- [ ] Remove Ollama availability and verify relationship analysis and ordinary Search remain useful.
- [ ] Restore Ollama and verify optional v1.8 enrichment can resume without duplicating unchanged relationship work.
- [ ] Remove OCR availability and verify metadata/text-based relationships continue and coverage remains honest.
- [ ] Restore OCR and verify affected durable stages can retry before relationship refresh.
- [ ] Exercise Eco, Balanced, and Fast indexing modes and verify the Collections UI stays responsive.
- [ ] Use increasing synthetic datasets and observe candidate bounds, incremental update cost, Search latency, memory, and cancellation without claiming unmeasured scale.
- [ ] Trigger database busy/maintenance behavior and verify a recoverable message plus ordinary Search fallback.

<a id="manual-v1-9-accessibility-and-regression"></a>
#### Accessibility and regression

- [ ] Navigate Collections, Smart Collections, Related Files, inspectors, filters, and actions using keyboard only with logical focus order.
- [ ] Activate relationship explanation and privacy/help controls with mouse, keyboard, and touch/click where supported.
- [ ] Verify meaningful screen-reader names, selected states, evidence text, confidence labels, and live status announcements with the recorded accessibility technology.
- [ ] Verify high contrast, focus visibility, text scaling, narrow layout, and long bounded filenames/titles.
- [ ] Smoke-test Scan, Watched Folders, Search filters/snippets, duplicate review, workflows/plugins, Change Plan, Apply/recovery, and Undo without changing their established behavior.

<a id="manual-v1-9-completion-record"></a>
#### Completion record

Leave this section empty until every applicable observation has been performed.
Record failures and environment limitations rather than marking an unobserved
scenario complete. Interactive validation is required separately from the
automated v1.9 validation report.

</details>

<a id="manual-v1-8"></a>
## v1.8

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v1.8.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

Use disposable synthetic folders only. Record the operating system, exact
commit, dependencies, locale, data shape, and observation for every exercised
scenario. Every item intentionally remains unchecked; automated evidence is not
an interactive observation.

<a id="manual-v1-8-search-quality-and-refinement"></a>
#### Search quality and refinement

- [ ] Exact filename Search keeps the exact file above loosely related results.
- [ ] Full-text Search finds a file whose filename does not contain the query.
- [ ] OCR Search finds applicable retained OCR text and labels the evidence.
- [ ] Related-concept Search improves recall without overwhelming precise
  filename or literal text matches.
- [ ] Search remains useful with Ollama stopped or disabled.
- [ ] Search remains responsive while background indexing is active.
- [ ] Search with incomplete coverage clearly says some files may not appear.
- [ ] A natural-language absolute, relative, year, and locale month date filter
  is interpreted correctly for the recorded locale/clock.
- [ ] File-type and explicit extension filters are visible and correct.
- [ ] Remove one active filter and confirm the remaining filters/topic rerun.
- [ ] Clear all filters and confirm no hidden filter remains.
- [ ] A minor filename typo finds the intended file without unrelated flooding.
- [ ] **Why this result?** lists only evidence that actually contributed.
- [ ] A snippet is bounded, identifies its source, and highlights the matched
  term accessibly without showing a complete document.
- [ ] No-result states distinguish full coverage, incomplete indexing,
  exclusions, OCR wait, local-AI wait, failure, and maintenance/unavailability.

<a id="manual-v1-8-accessibility-and-input"></a>
#### Accessibility and input

- [ ] Complete Search, filter removal, explanation expansion, result opening,
  inspection, confirmation, and cancellation with keyboard only.
- [ ] Verify meaningful screen-reader names, result explanation reading order,
  snippet source, active-filter controls, live status, and focus order.
- [ ] Activate Search help, filters, explanations, inspection, and confirmation
  through mouse and hover where supported.
- [ ] Activate Search help, filters, explanations, inspection, and confirmation
  through click or touch; confirm no required information depends on hover.

<a id="manual-v1-8-indexed-data-privacy"></a>
#### Indexed-data privacy

- [ ] Inspect indexed data for Basic, Standard, and Deep files and compare the
  displayed category/count information with the configured policy.
- [ ] Confirm raw related-concept vectors and complete extracted/OCR text are
  not shown in the ordinary inspection UI.
- [ ] Forget one indexed file after confirmation and confirm it leaves Search
  while the original source file remains byte-for-byte unchanged.
- [ ] Forget one manually managed source and confirm source registration and
  original files remain unchanged.
- [ ] Forget one watched-folder source's index data and confirm watched-folder
  ownership remains intact without an immediate re-index loop.
- [ ] Clear OCR-derived data and confirm coverage/policy update accurately.
- [ ] Clear related-concept data/chunks and confirm ordinary literal Search
  remains available.
- [ ] Downgrade a file to metadata-only and confirm deeper content is removed
  and not regenerated automatically.
- [ ] Exclude a file/folder from future deep indexing and confirm exclusion
  impact appears in coverage.
- [ ] Disable OCR, summaries/keywords, and related-concept processing in
  Settings and confirm each choice persists after restart.

<a id="manual-v1-8-repair-recovery-and-concurrency"></a>
#### Repair, recovery, and concurrency

- [ ] Re-index a selected file and confirm only its applicable stages rerun.
- [ ] Rebuild a selected source and confirm other sources remain isolated.
- [ ] Retry a selected failed stage and confirm completed unrelated work is
  reused.
- [ ] Cancel a selected repair and confirm a safe durable boundary and accurate
  coverage.
- [ ] Terminate OpenSorSe during a selected repair, restart, and confirm durable
  recovery leaves no stale running stage.
- [ ] Search during paused indexing, cancelled indexing, restart recovery,
  cleanup, and compaction.
- [ ] Search during file deletion, rename, move, source removal, and dependency
  loss/restoration; confirm stale records behave predictably.
- [ ] Exercise rapid repeated and overlapping Search; confirm the UI remains
  responsive and stale query results do not replace newer results.
- [ ] Cancel an active Search and confirm no corrupt partial result list appears.
- [ ] Exercise Search while the index is busy or maintenance is active.

<a id="manual-v1-8-security-and-diagnostics"></a>
#### Security and diagnostics

- [ ] Enter a very long query and confirm a bounded actionable error.
- [ ] Enter malformed/control-containing text through an appropriate test input
  method and confirm safe rejection without a crash.
- [ ] Try wildcard/SQL/FTS-like punctuation and confirm it is treated as bounded
  ordinary text rather than executable syntax.
- [ ] Search hostile but valid filenames and malformed retained snippet text
  from a disposable fixture; confirm safe rendering.
- [ ] Review Search diagnostics and confirm duration, result/filter counts,
  ranking stages, coverage, availability, cancellation/timeout/failure category
  appear where applicable.
- [ ] Confirm default diagnostics and reviewed exports omit complete queries,
  snippets, extracted/OCR paragraphs, summaries, prompts, tokens, secrets, and
  unnecessary absolute paths.

<a id="manual-v1-8-existing-feature-regression"></a>
#### Existing-feature regression

- [ ] Load existing saved scans/catalogs and run a saved catalog Search.
- [ ] Exercise watched folders, duplicate detection, workflows, and a compatible
  plugin without changing their persisted public behavior.
- [ ] On disposable files, create/review/apply a Change Plan, inspect Operation
  History, and Undo it successfully.

</details>

<a id="manual-v1-7"></a>
## v1.7

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v1.7.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

Use disposable synthetic folders only. This checklist is not marked complete
until a maintainer records observed results in the validation report.

Record the host operating system, OpenSorSe commit, enabled dependencies, test
data shape, and the observation for each exercised item. Leave an item unchecked
when it was not exercised, was unavailable on the host, or did not pass.

<a id="manual-v1-7-search-and-accessibility"></a>
#### Search and accessibility

- [ ] The feature is named Search in Files navigation, page headings, controls,
  empty states, Help, accessibility output, and current user documentation.
- [ ] The Search help affordance is available by mouse hover where supported,
  keyboard focus and activation, click or touch, and a screen reader. Confirm
  its accessible name and help text do not depend on hover alone.
- [ ] During partial indexing, Search remains responsive, returns currently
  available filename and metadata matches, and states that coverage is still
  being built rather than presenting an empty result as definitive.
- [ ] Coverage reporting distinguishes filename and metadata, extracted text,
  OCR, semantic, and fully indexed coverage without claiming unavailable
  processing is complete.

<a id="manual-v1-7-initial-indexing-progress-and-persistence"></a>
#### Initial indexing, progress, and persistence

- [ ] Add a disposable synthetic folder and run initial indexing. Confirm each
  applicable file progresses from discovery through its selected Basic,
  Standard, or Deep stages without indexing outside the selected source.
- [ ] During indexing, confirm the UI shows the current stage and filename,
  processed, total, remaining, completed, skipped, failed, waiting, and retry
  counts, coverage, speed, current and maximum storage, and overall progress.
  Confirm the estimated remaining time is labelled as an estimate and remains
  hidden until sufficient samples exist.
- [ ] Select and save Basic, Standard, and Deep as the default indexing level
  in turn, and confirm each setting reloads without changing an existing source
  unexpectedly.
- [ ] Restart normally during active indexing and confirm recovery resumes the
  durable run, completed stages are reused, and no item remains stale in the
  running state.
- [ ] Restart after indexing work has completed and confirm the configured
  sources, settings, indexed progress, coverage, and searchable results persist.

<a id="manual-v1-7-pause-cancellation-and-interruption-recovery"></a>
#### Pause, cancellation, and interruption recovery

- [ ] Pause an active run and confirm no later work is claimed while paused;
  Resume and confirm processing continues from durable state.
- [ ] Exit OpenSorSe while a run is paused, relaunch it, and confirm the run
  remains paused until Resume is requested.
- [ ] Cancel active indexing and confirm cancellation is safe and prompt, its
  state persists across restart, and completed stages are not repeated when the
  run is explicitly retried or refreshed.
- [ ] Force-terminate OpenSorSe during active processing, then relaunch it.
  Confirm interrupted running work is recovered or requeued, completed work is
  reused, and no job remains permanently marked as running.
- [ ] Repeat interruption recovery at representative durable stages:
  metadata or fingerprinting, text extraction, OCR or dependency wait, and
  search-index update.

<a id="manual-v1-7-dependencies-and-resource-controls"></a>
#### Dependencies and resource controls

- [ ] With local AI explicitly enabled, make Ollama unavailable during
  applicable processing. Confirm a truthful waiting or failure state, then
  restore Ollama and verify retry or resume continues without repeating
  unrelated completed work.
- [ ] With OCR explicitly enabled, make the configured OCR tool unavailable
  during an applicable file. Confirm a truthful waiting, skipped, or failure
  state, then restore it and verify retry or resume continues safely.
- [ ] Select Eco, Balanced, and Fast modes in turn. Confirm each selection
  saves and reloads. With sufficient synthetic work, use run diagnostics to
  confirm observed worker concurrency follows the documented bounded mode
  policy and the UI remains responsive.
- [ ] Where the host reports support for idle, power, battery, or schedule
  controls, exercise the enabled constraint. Where support is unavailable,
  confirm the application degrades clearly and continues safely; do not infer
  platform behaviour that was not observed.

<a id="manual-v1-7-source-lifecycle-and-incremental-behaviour"></a>
#### Source lifecycle and incremental behaviour

- [ ] Complete a manual scan so its root becomes an indexing source, prioritise
  it, run the background-index rebuild, then remove it. Confirm the source
  remains configured through the rebuild and removing it does not alter the
  source files.
- [ ] Configure separate manual and watched-folder sources. Change or remove
  one watched-folder source and confirm unrelated watched and manual sources,
  their progress, and their indexed data remain isolated.
- [ ] Rename and move an unchanged indexed file within the disposable data.
  Where stable identity is supported, confirm its path metadata changes while
  reusable content analysis is not repeated.
- [ ] Modify an indexed file's content without changing its path. Confirm only
  affected stages are invalidated and rerun, and Search reflects the new
  content after indexing completes.
- [ ] Copy duplicate content, change metadata only, delete a file, and restore
  it where practical. Confirm shared work, metadata updates, and retention
  follow the documented policies without breaking watched-folder activity.

<a id="manual-v1-7-storage-failures-and-diagnostics"></a>
#### Storage, failures, and diagnostics

- [ ] Confirm current index size, configured maximum size, and the available
  metadata, extracted text, OCR, summary or keyword, semantic, relationship,
  job-history, and diagnostics breakdowns are truthful for the exercised data.
- [ ] With a safely low quota and synthetic data, approach the limit and
  confirm maintenance cleans eligible stale, orphaned, and expired operational
  data before blocking further work with a clear message. Confirm no source
  file or important user-visible metadata is silently deleted.
- [ ] Run the storage maintenance or compaction action and confirm its reported
  cleanup actions and resulting storage values are coherent. Reopen the
  application and confirm retained indexed data remains usable.
- [ ] Create a controlled locked, inaccessible, or otherwise failing synthetic
  item. Confirm failure counts and categories, failure inspection, maximum
  retry behaviour, and Retry failed items remain accurate.
- [ ] Open diagnostics for the current indexing run and confirm run ID, stage
  durations, queue lengths, retry counts, failure categories, dependency
  availability, database size, throughput, cancellation reason, resume
  information, and cleanup actions are present where applicable.
- [ ] Review any exported diagnostics before sharing and confirm no extracted
  text, OCR text, prompt body, token, or unnecessary absolute path is present.

<a id="manual-v1-7-existing-feature-regression-smoke-tests"></a>
#### Existing-feature regression smoke tests

- [ ] Load existing saved scans and exercise watched folders, duplicate
  detection, workflows, and a compatible plugin without changing their
  persisted public behaviour.
- [ ] On disposable files, create and review a Change Plan, apply it, inspect
  Operation History and recovery information, then Undo it successfully.

</details>

<a id="manual-v1-6"></a>
## v1.6

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v1.6.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="manual-v1-6-completion-record"></a>
#### Completion record

The project maintainer confirmed completion of the required interactive manual
smoke testing on 2026-07-28 and reported no release-blocking issues. Detailed
host, architecture, filesystem, assistive-technology, Tesseract, Ollama, and
plugin-version observations were not supplied for this repository record. The
checkboxes below remain the reusable validation procedure rather than a
fabricated per-environment evidence log.

Use disposable folders and copies of files. Do not use irreplaceable data.
Record the operating system, architecture, filesystem, .NET runtime, external
tool versions, exact result, and any diagnostic correlation identifier.

<a id="manual-v1-6-windows-linux-and-macos-startup"></a>
#### Windows, Linux, and macOS startup

- [ ] Build and launch the Desktop natively on the target host.
- [ ] Confirm the application icon, theme, scaling, resizing, keyboard focus,
      and text rendering.
- [ ] Confirm About displays `1.6` and assembly/product metadata displays
      `1.6.0`.
- [ ] Confirm the System check reports the correct host, application-data
      locations, path case policy, identity strength, watcher capability,
      desktop integration, packaging state, OCR tools, and limitations.
- [ ] Confirm existing v1.5 application data opens without reset or migration
      prompts.

<a id="manual-v1-6-accessibility"></a>
#### Accessibility

- [ ] Navigate primary, Advanced, and footer destinations using only the
      keyboard.
- [ ] With the host screen reader, verify navigation items announce their
      labels.
- [ ] Verify scan, global-operation, watched-folder, Change Plan, Operation
      History, plugin, and notification status changes are announced without
      stealing focus.
- [ ] Verify Scan cancellation and global cancellation expose distinct names.
- [ ] Verify visible focus, high contrast, 200% scaling, and resized layouts on
      critical workflows.

<a id="manual-v1-6-persistence-and-recovery"></a>
#### Persistence and recovery

- [ ] Save Settings, workflows, watched folders, Saved scans, saved searches,
      plugin state, and AI preferences; restart and verify exact reload.
- [ ] Interrupt the process while repeatedly saving application-owned state.
      Confirm the previous or new complete JSON document loads and no partial
      document replaces it.
- [ ] Make an application-data directory temporarily unavailable or read-only.
      Confirm the operation fails visibly, unrelated workflows continue, and
      the previous document remains intact.
- [ ] Supply malformed and oversized copies of each owned store in a disposable
      profile. Confirm documented recovery behavior preserves the invalid file.
- [ ] Confirm no `*.tmp` sibling remains after successful operations.

<a id="manual-v1-6-scan-search-and-cancellation"></a>
#### Scan, search, and cancellation

- [ ] Scan a large mixed tree and cancel during enumeration, metadata, hashing,
      duplicate detection, and result projection.
- [ ] Confirm cancellation returns promptly, does not publish partial results,
      and the next scan succeeds.
- [ ] Search and re-sort a large completed scan while typing rapidly. Confirm
      the UI remains responsive and stable pages contain at most the selected
      page size.
- [ ] Confirm duplicate groups, counts, ordering, and reclaimable sizes match
      v1.5 behavior.

<a id="manual-v1-6-watched-folders"></a>
#### Watched folders

- [ ] Start with an existing watched root, then create/modify/rename/delete
      files in bursts. Confirm one normalized batch after the quiet period.
- [ ] Test case-only names on the target filesystem. Confirm behavior follows
      the reported host/filesystem case policy.
- [ ] Pause, modify offline, resume, overflow/reconnect, and run manual full
      reconciliation.
- [ ] Close the application while a debounce and reconciliation are active.
      Confirm shutdown completes without a crash or orphaned watcher.
- [ ] Confirm failures create visible activity/reconciliation state and never
      apply file changes automatically.

<a id="manual-v1-6-change-plans-recovery-and-undo"></a>
#### Change Plans, recovery, and Undo

- [ ] Create, edit, approve, validate, and explicitly apply a disposable plan.
- [ ] Cancel before and between safe action boundaries.
- [ ] Simulate stale source, occupied destination, permission failure,
      interruption, rollback, and restart recovery.
- [ ] Confirm Operation Journal records remain complete and human-readable.
- [ ] Confirm Undo succeeds only while identity and dependency checks pass and
      blocks rather than overwriting external changes.

<a id="manual-v1-6-ai-ocr-workflows-and-plugins"></a>
#### AI, OCR, workflows, and plugins

- [ ] Confirm AI is disabled by default and each capability remains separately
      gated.
- [ ] Test configured local and intentionally remote Ollama endpoints, timeout,
      cancellation, missing model, malformed response, and recovery.
- [ ] Test native document extraction and optional Tesseract OCR cancellation,
      tool discovery, language availability, temporary cleanup, and bounds.
- [ ] Export/import v1.6 workflows and confirm v1.5 exports remain readable.
- [ ] Inspect, enable, disable, fail, recover, upgrade, and remove a disposable
      plugin. Confirm capability grants and dependency blocks remain enforced.

<a id="manual-v1-6-repository-handoff"></a>
#### Repository handoff

- [ ] Confirm Debug and Release automated reports contain zero failed and zero
      skipped tests.
- [ ] Confirm analyzer and formatting gates are clean.
- [ ] Confirm no generated build, test-result, package, or temporary artifact is
      tracked.
- [ ] Record native Windows, Linux, and macOS results in
      `VALIDATION.md#validation-v1-6` before making a release claim.

</details>

<a id="manual-v1-5"></a>
## v1.5

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v1.5.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

Use disposable data. Do not publish a binary based only on this checklist.
Record distribution, desktop, filesystem, mount type, architecture, Tesseract
version, and the exact commit tested.

<a id="manual-v1-5-both-supported-targets"></a>
#### Both supported targets

- [ ] About shows `1.5`; product is `1.5.0`; assembly/file version is `1.5.0.0`.
- [ ] Settings opens and Platform diagnostics lists every capability and owned
      location without secrets.
- [ ] Copy platform report and open diagnostics folder succeed or show a
      non-fatal, specific unavailable explanation.
- [ ] Scan a Unicode/hidden-file tree. Confirm symlink/reparse targets outside
      the root are not traversed and cycles terminate.
- [ ] Preview a portable recipe containing `:`, `?`, trailing dot/space, and a
      Windows device name. Confirm sanitization and no root escape.
- [ ] Rename and same-filesystem move through review, final confirmation,
      journal, verification, Undo, restart, and recovery.
- [ ] Confirm an occupied destination, linked destination, unwritable
      directory, changed source, and cross-filesystem move fail closed.
- [ ] Exercise watched create/modify/rename/delete, event bursts, root loss,
      startup reconciliation, manual reconciliation, and overflow reporting.
- [ ] Discover a managed plugin. Confirm it is not activated automatically.
- [ ] Confirm a native plugin with a mismatched/missing runtime identifier is
      incompatible and cannot load.
- [ ] Check Tesseract through `PATH`, then through an absolute configured path;
      verify missing executable and missing language-pack messages.
- [ ] Connect to an already running configured Ollama HTTP endpoint. Confirm no
      provider executable is auto-started.

<a id="manual-v1-5-windows"></a>
#### Windows

- [ ] Existing `%LocalAppData%\OpenSorSe` settings and state remain readable;
      no duplicate migration or deletion occurs.
- [ ] Case-only rename uses the safe temporary rename path and verifies result.
- [ ] Junctions and reparse points remain excluded.

<a id="manual-v1-5-linux-preview"></a>
#### Linux preview

- [ ] Follow [Linux build and launch](LINUX_BUILD_AND_LAUNCH.md) on a supported
      x64 distribution with a graphical session.
- [ ] Verify XDG overrides and fallback directories separately.
- [ ] Verify executable-bit failure for configured Tesseract.
- [ ] Verify case-sensitive files with names differing only by case remain
      distinct.
- [ ] Verify device/inode identity survives rename and distinguishes a copy.
- [ ] Verify same-mount rename/Undo and cross-mount rejection using disposable
      mounts.
- [ ] Inspect inotify descriptor/queue pressure and reconcile after overflow.
- [ ] Confirm file picker, clipboard, theme/fonts, and default file manager.

macOS is not a v1.5 manual release target. Do not convert an incidental launch
into a support claim.

</details>

<a id="manual-v1-4"></a>
## v1.4

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v1.4.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

Use disposable folders and disposable plugin packages. Do not use production
data. Record the application version, OS, package hashes, and results.

<a id="manual-v1-4-release-gate"></a>
#### Release gate

- [ ] About shows `1.4`; executable metadata reports `1.4.0.0`.
- [ ] A clean start with no plugin directory succeeds.
- [ ] Settings > Plugins lists all four built-in reference plugins.
- [ ] Plugin diagnostics can be exported and contain no file content,
      credential, token, or AI prompt payload.

<a id="manual-v1-4-local-package-lifecycle"></a>
#### Local package lifecycle

- [ ] A valid local ZIP installs but remains disabled.
- [ ] The UI accurately shows publisher, license, source, requested
      capabilities, dependencies, contributions, compatibility, and integrity.
- [ ] Enabling requires an explicit capability grant.
- [ ] Disabling removes contributions and accurately reports restart state.
- [ ] A compatible upgrade is fully validated before activation and leaves the
      old version available for rollback.
- [ ] A corrupt/incompatible upgrade leaves the previous version usable.
- [ ] Removal requires confirmation and deletes only the controlled version
      directory.
- [ ] Removal is blocked when a profile, recipe, or watched folder depends on
      the plugin.

<a id="manual-v1-4-adversarial-packages"></a>
#### Adversarial packages

- [ ] Reject missing, duplicate, oversized, deeply nested, malformed, or
      unknown-field `plugin.json` files.
- [ ] Reject path traversal, rooted paths, alternate separators, duplicate ZIP
      entries, link/reparse content, excessive entry count/size, missing entry
      assembly, and undeclared native binaries.
- [ ] Reject invalid IDs, versions, entry paths/types, dependency ranges,
      contribution conflicts, cycles, missing dependencies, and incompatible
      runtimes.
- [ ] Changing an installed file after acceptance disables the plugin and
      produces an integrity diagnostic.

<a id="manual-v1-4-runtime-containment"></a>
#### Runtime containment

- [ ] Initialization success, exception, timeout, and cancellation are reported
      without crashing startup.
- [ ] A plugin failing startup three times becomes quarantined.
- [ ] Duplicate contribution registration is rejected without replacing the
      first registration.
- [ ] Cancellation and invalid/oversized output at every extension point fails
      closed and does not leave partial state.

<a id="manual-v1-4-workflow-and-change-plan-safety"></a>
#### Workflow and Change Plan safety

- [ ] A matching enabled plugin contribution resolves at its exact version and
      appears in the immutable workflow snapshot.
- [ ] Missing, disabled, quarantined, changed, incompatible, and wrong-version
      references show “Plugin capability unavailable — review workflow profile.”
- [ ] Watched-folder processing does not silently fall back.
- [ ] Recipe fields preserve plugin/version/contribution/value/reason/evidence
      provenance on all generated actions, including inferred directories.
- [ ] Plugin field text cannot escape the approved root, create reserved names,
      execute syntax, overwrite a destination, or bypass collision handling.
- [ ] Generated actions remain Pending until normal review and explicit Apply.
- [ ] Apply, journal verification, recovery, rollback, history, and Undo still
      use the existing v1.1 execution boundary.

<a id="manual-v1-4-inherited-regression"></a>
#### Inherited regression

- [ ] Complete the [v1.1](MANUAL_TESTING.md#manual-v1-1),
      [v1.2](MANUAL_TESTING.md#manual-v1-2), and
      [v1.3](MANUAL_TESTING.md#manual-v1-3) checklists.
- [ ] Verify optional OCR/Ollama flows with plugins disabled and with one
      unrelated plugin enabled.
- [ ] Verify startup, shutdown, and restart after enable, disable, upgrade,
      quarantine, integrity change, and removal.

Do not publish a v1.4 binary until this checklist and the automated release
checks pass.

</details>

<a id="manual-v1-3"></a>
## v1.3

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v1.3.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

Run on a disposable test tree. Never use irreplaceable files for mutation/Undo checks.

<a id="manual-v1-3-release-identity"></a>
#### Release identity

- [ ] Branch is `v1.3-workflow-profiles`.
- [ ] About displays `1.3`; binary product/informational version is `1.3.0`; file/assembly version is `1.3.0.0`.
- [ ] Workflows appears in primary navigation.

<a id="manual-v1-3-library-and-editor"></a>
#### Library and editor

- [ ] All five profiles and four recipes appear after first start.
- [ ] Built-ins can be exported/duplicated but not edited, disabled, archived, or deleted.
- [ ] Create, rename, edit, disable/enable, archive/restore, and safely delete an unreferenced user profile.
- [ ] Create/duplicate/edit/archive/restore a recipe.
- [ ] Search and each origin/capability/file/archive filter produce understandable lists.
- [ ] Usage counts update for watched assignments and profile-recipe dependencies.
- [ ] Deletion of a referenced recipe/profile is blocked with a dependency message.

<a id="manual-v1-3-template-safety"></a>
#### Template safety

- [ ] Preview the invoice date/vendor example and inspect every explanation field.
- [ ] Try missing required values and fallback values.
- [ ] Try `../Outside`, a rooted destination, invalid characters, `CON`, `NUL.txt`, a long name/path, Unicode, and an occupied destination.
- [ ] Confirm invalid previews never create files/directories.
- [ ] Put braces and path separators in an AI-marked representative value; confirm they remain data and cannot become template syntax.

<a id="manual-v1-3-manual-and-watched-integration"></a>
#### Manual and watched integration

- [ ] Select each profile on Scan and verify its summary.
- [ ] Apply one-time narrowing; confirm the saved profile revision is unchanged.
- [ ] Save adjusted settings as a new profile.
- [ ] Assign one profile and multiple permitted recipes to a watched folder and restart.
- [ ] Edit the assigned profile; verify a reconciliation records the new revision.
- [ ] Archive/disable the assigned profile and recipe in turn; verify **Profile unavailable — review configuration** and no fallback.
- [ ] Test a migrated `default` profile and deliberately replace any legacy `current` recipe.

<a id="manual-v1-3-change-plan-boundary"></a>
#### Change Plan boundary

- [ ] Generate a recipe proposal and inspect source profile/recipe/revisions, values, evidence, deterministic/AI status, warnings, and unresolved fields.
- [ ] Confirm source files and destination directories are unchanged before Apply.
- [ ] Reject an action and confirm it is not executed.
- [ ] Approve, validate, explicitly Apply on disposable files, verify Operation History, then Undo.
- [ ] Retest occupied destination, stale source, locked source, cancellation, interruption recovery, rollback, and non-empty directory Undo protection from the v1.1 checklist.

<a id="manual-v1-3-ai"></a>
#### AI

- [ ] Global-off blocks a profile that permits AI.
- [ ] Profile-off blocks globally enabled AI.
- [ ] Test missing-classification, after-extraction, selected-file-type, and explicit-retry policies.
- [ ] Missing model/provider and failed/cancelled requests remain safe and retryable.
- [ ] Deterministic failures do not automatically invoke AI.

<a id="manual-v1-3-import-recovery-privacy"></a>
#### Import, recovery, privacy

- [ ] Export/import profile and recipe JSON as copy.
- [ ] Exercise skip, cancel, and confirmed user-item replacement conflicts.
- [ ] Confirm built-in replacement, missing dependencies, future schema, malicious traversal, deep, large, and destructive-rule inputs are rejected.
- [ ] Corrupt a disposable workflow store; verify the original and diagnostic copy remain and built-ins load.
- [ ] Export diagnostics and confirm no document contents, provider endpoint/model, credentials, or secrets appear.

<a id="manual-v1-3-v12-regression"></a>
#### v1.2 regression

- [ ] Run create/modify/rename/move/delete, pause/resume, offline restart, reconnect, overflow, daily/manual reconciliation, ignore, stability, and OpenSorSe self-event scenarios.
- [ ] Confirm no watched cycle calls Apply automatically.

</details>

<a id="manual-v1-2"></a>
## v1.2

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v1.2.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

Use only disposable temporary folders and copied test files. Do not use personal originals, the repository, a production synchronized folder, or the root of a drive.

<a id="manual-v1-2-configuration-and-persistence"></a>
#### Configuration and persistence

- [ ] About reports `1.2.0`; executable properties report `1.2.0.0`.
- [ ] Add one watched folder and confirm a startup reconciliation summary.
- [ ] Restart OpenSorSe and confirm the configuration, catalogue identity, timestamps, settings, and activity remain.
- [ ] Pause and resume; verify files and history remain and resume queues reconciliation.
- [ ] Remove from watch list after confirmation; verify the real folder and files remain.
- [ ] Add the same path with separator/case differences; verify it is rejected.
- [ ] Add parent and child roots; verify the overlap explanation prevents duplicate ownership.
- [ ] Disconnect removable storage; verify **Unavailable** without catalogue deletion. Reconnect and verify reconciliation.
- [ ] Rename the watched root externally; verify it remains unavailable and OpenSorSe does not guess another path.

<a id="manual-v1-2-watcher-hints-debounce-and-stability"></a>
#### Watcher hints, debounce, and stability

- [ ] Create, modify, rename, move, and delete files and directories.
- [ ] Copy a large file slowly; verify analysis waits until stable or reports deferred/incomplete.
- [ ] Save from an application that uses temporary-name-and-replace; verify one final catalogue entry.
- [ ] Generate a burst of changes; verify one grouped summary rather than one notification per raw event.
- [ ] Lock a file temporarily; verify deferral and successful later reconciliation.
- [ ] Create `*.tmp`, `*.part`, `*.crdownload`, hidden, oversized, exact-ignored, directory-ignored, and pattern-ignored items; verify they are absent from analysis and AI.
- [ ] Change an ignored pattern and run full reconciliation.
- [ ] Use a debug/fake-watcher harness to report overflow; verify the visible warning and required reconciliation.

<a id="manual-v1-2-incremental-and-offline-reconciliation"></a>
#### Incremental and offline reconciliation

- [ ] After a baseline, change one file in a large disposable folder; verify unchanged files retain analysis timestamps.
- [ ] Rename and move one unchanged file; verify identity/path update without duplicate entries or repeated OCR/AI.
- [ ] Modify content; verify metadata/content extraction, hash, classification, duplicate group, and affected rules update.
- [ ] Delete a file externally; verify the current catalogue reports removal but historical saved data is not erased.
- [ ] Close OpenSorSe, make additions/removals/renames, restart, and verify the offline summary.
- [ ] Make changes while paused, resume, and verify the same.
- [ ] Select **Scan changes now** and **Full reconciliation** and compare precise status wording.
- [ ] Run a no-change reconciliation; verify no content reanalysis or AI request.
- [ ] Cancel a processing operation at a safe boundary and reconcile again.

<a id="manual-v1-2-change-plans-and-self-generated-events"></a>
#### Change Plans and self-generated events

- [ ] Enable a deterministic sorting recipe and make one matching change; verify a Change Plan appears without filesystem mutation.
- [ ] Enable watched-folder AI plus global AI/capability/model settings; verify suggestions are batched and review-only.
- [ ] Disable watched-folder AI; verify no model request.
- [ ] Make Ollama/model unavailable; verify deterministic catalogue updates succeed and only AI remains retryable.
- [ ] Complete AI analysis for one file and fail it for another; verify **Retry failed AI analysis** sends only the pending/failed item.
- [ ] Review, approve, validate, and explicitly apply a disposable plan through **Review Changes**.
- [ ] Verify watcher events from Apply update the catalogue but do not create a recursive plan, repeated suggestion, or repeated AI request.
- [ ] Verify Operation History, rollback facts, restart recovery, and Undo remain functional.
- [ ] Modify a resulting file or occupy its original path; verify existing v1.1 Undo blocking still applies.

<a id="manual-v1-2-resource-and-presentation-checks"></a>
#### Resource and presentation checks

- [ ] Add/edit/pause/resume repeatedly and observe that old watcher instances are disposed.
- [ ] Stress a fake watcher beyond the 256-batch bound; verify busy/backpressure and reconciliation-required states.
- [ ] Verify availability, state, queued count, processing status, last change/scan/reconciliation, summary, plans, warning, and error presentation.
- [ ] Verify notifications group meaningful changes and never say files were organized before Apply.
- [ ] Verify **None**, **Errors only**, plan-ready, and unavailable notification preferences are saved and honored.
- [ ] Inspect ordinary logs: they may contain policy decisions and redacted paths but no file contents, extracted text, AI prompt, credentials, or raw model response.

<a id="manual-v1-2-regression-and-completion"></a>
#### Regression and completion

- [ ] Complete the [v1.1 checklist](MANUAL_TESTING.md#manual-v1-1) on disposable data.
- [ ] Scan/cancel, Files, duplicates, OCR, Meaning Search, Saved scans/search/comparison, tags, Settings, Help, diagnostics, Folder plans, Review Changes, Operation History, recovery, rollback, and Undo.
- [ ] Run complete Debug and Release tests with no skip.
- [ ] Run `git diff --check` and inspect generated/untracked output.

</details>

<a id="manual-v1-1"></a>
## v1.1

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v1.1.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

Use only a disposable temporary directory containing copied test files. Never test against personal originals, the repository, or a synchronized production folder.

<a id="manual-v1-1-review-changes"></a>
#### Review Changes

- [ ] Scan the disposable root and generate one rename suggestion.
- [ ] Accepting it opens a Change Plan and does not rename the file.
- [ ] Approve/reject one action and verify counts.
- [ ] Edit filename and destination; verify prior validation becomes invalid.
- [ ] Filter rename/move/folder and warning/conflict rows.
- [ ] Approve all safe and deselect all.
- [ ] Validate and confirm Apply remains disabled for an occupied destination.
- [ ] Verify the final summary counts and explicit confirmation.
- [ ] Return to Files without Apply and verify no mutation.

<a id="manual-v1-1-execution-and-journal"></a>
#### Execution and journal

- [ ] Apply one rename and verify old/new paths and Operation History.
- [ ] Apply a move into a newly created folder and verify all three supported action types.
- [ ] Test spaces, German characters, and other Unicode filenames.
- [ ] Test a case-only rename on Windows.
- [ ] Introduce a destination collision after approval; Apply must fail pre-execution without overwrite.
- [ ] Modify/delete/rename a source after approval; revalidation must mark it stale.
- [ ] Request cancellation during a multi-action disposable plan; verify a safe terminal/rollback state.
- [ ] Close/restart the app and confirm history and Undo availability remain visible.
- [ ] Copy a report and verify it includes paths/results but no file contents or AI prompt.

<a id="manual-v1-1-undo"></a>
#### Undo

- [ ] Undo a rename, move, and complete combined plan.
- [ ] Undo an OpenSorSe-created empty directory.
- [ ] Put a file in an OpenSorSe-created directory; Undo must preserve it and report a conflict.
- [ ] Modify a resulting file; Undo must be blocked.
- [ ] Occupy the original path; Undo must not overwrite it.
- [ ] Apply a later dependent operation; earlier Undo must be blocked.
- [ ] Verify partial Undo is labelled partial and every action detail is journalled.

<a id="manual-v1-1-interruption-and-errors"></a>
#### Interruption and errors

- [ ] Using a disposable copy/debug harness, leave a running journal record and restart; it must become Interrupted after path inspection.
- [ ] Simulate access denied/file lock where supported; verify safe category and retry after release.
- [ ] Verify rollback-partial wording never claims complete rollback.
- [ ] Inspect Change Plan and Operation Journal corrupt-data behavior with backed-up application data.

<a id="manual-v1-1-regression"></a>
#### Regression

- [ ] Scan/cancel, duplicates, Files filters/details, OCR availability, Meaning Search, Saved scans/search/comparison, tags, Settings, Help, diagnostics, and Structure history.
- [ ] AI disabled: no provider request.
- [ ] AI malformed response: no plan mutation and no filesystem work.
- [ ] Duplicate review offers no deletion.
- [ ] Check About reports 1.1.0 and executable properties report 1.1.0.0.

</details>

<a id="manual-v1-0"></a>
## v1.0

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v1.0.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

This checklist validates the OpenSorSe 1.0 release candidate using disposable test data. Unless a step explicitly tests an apply operation, OpenSorSe must not rename, move, delete, overwrite, or modify source files. Record the application version, operating system, .NET runtime, test-data root, OCR capability state, and result for every section.

<a id="manual-v1-0-release-candidate-shell-and-home"></a>
#### Release-candidate shell and Home

1. Start with default settings and confirm the sidebar shows Home, Scan, Files, Duplicates, Saved scans, and Settings.
2. Confirm Advanced pages are absent, and Help/About appear in the sidebar footer.
3. Confirm Home shows a friendly first-scan state and one primary **Scan a folder** action.
4. Complete a scan and confirm the single Latest scan card shows files, folders, copies, and warnings without duplicate summary wording.
5. Navigate every primary page and confirm the bottom status bar remains visible.
6. Resize to the minimum supported window size and confirm navigation, cards, drawers, and status text remain usable.
7. Confirm the official OpenSorSe mark is crisp and unclipped in the window chrome/sidebar at 100%, 125%, 150%, and 200% scaling where available.
8. Confirm the sidebar reads **OpenSorSe**, **OPEN SORT AND SEARCH**, and **Find clarity in your files**, with comfortable separation from navigation and footer actions.

<a id="manual-v1-0-files"></a>
#### Files

1. Scan a folder with more files than fit vertically.
2. Open Files.
3. Scroll the file list.
4. Confirm the search/filter toolbar remains fixed.
5. Confirm result rows scroll independently.
6. Resize the window.
7. Confirm controls reflow without clipping.
8. Open **Filters**, filter by type, and confirm the drawer can close without resetting values.
9. Filter by duplicates.
10. Filter by tag.
11. Change sorting.
12. Change page size.
13. Clear filters.
14. Confirm the result count remains correct.

15. Select a file and confirm the right details panel appears; clear selection and confirm it disappears.
16. Confirm File Assistant controls appear only for a selected file and every disabled rename action shows an exact reason.
17. Enable Meaning Search in Settings and confirm the **Meaning Search Beta** action appears in Files.
18. With a file selected, drag the table/details divider left and right; confirm the cursor changes, resizing remains smooth, and neither pane collapses below 450/320 device-independent pixels.
19. Focus the table/details divider and use the arrow keys. Open its context menu and test narrow, widen, and reset.
20. Resize the window after changing the divider and confirm the proportion remains coherent.
21. Restart OpenSorSe and confirm the preferred details proportion is restored.
22. Restore default Settings, save, return to Files, and confirm the default 32% details proportion is restored.
23. Clear the selected row and confirm the table reclaims the full width with no empty reserved panel.
24. Verify subtle alternating rows, hover feedback, selected-row contrast, long filename/path ellipsis, and vertical/horizontal scrolling.
25. Drag each file-column divider and then focus it and use the arrow keys; confirm headings and rows remain aligned.

<a id="manual-v1-0-duplicates"></a>
#### Duplicates

15. Open Duplicates.
16. Select a duplicate group.
17. Confirm the right-side drawer opens.
18. Select another group.
19. Confirm the drawer updates.
20. Close the drawer.
21. Reopen it.
22. Press Escape and confirm the drawer closes.
23. Test long filenames.
24. Test long paths.
25. Test **Open both files** for a two-file group.
26. Test **Open selected files** for a larger group.
27. Open containing folders.
28. Test a missing file.
29. Confirm partial failure is reported.
30. Confirm no file is deleted, moved, renamed, or modified.

<a id="manual-v1-0-settings-navigation-and-global-toggles"></a>
#### Settings, navigation, and global toggles

31. Navigate through every page.
32. Confirm **Enable AI features** is in Settings and AI capability/model controls remain hidden while it is off.
33. Confirm **Show advanced features** is in Settings.
34. Toggle AI.
35. Verify Settings updates.
36. Toggle Advanced.
37. Verify Settings updates.
38. Disable Advanced while on an advanced page.
39. Confirm safe navigation fallback to Home and that hidden values remain saved.
40. Restart and confirm persistence.
41. Confirm enabling AI alone does not contact Ollama.

<a id="manual-v1-0-ocr"></a>
#### OCR

42. Scan an image containing text.
43. Scan a scanned PDF.
44. Scan a PDF with native text.
45. Confirm native text prevents unnecessary OCR.
46. Disable OCR.
47. Confirm OCR is skipped.
48. Cancel OCR.
49. Test an oversized file.
50. Inspect OCR status.
51. Confirm source documents remain unchanged.

If no compatible local Tesseract engine is detected, confirm the interface reports **Unavailable** rather than claiming OCR succeeded. PDF rendering is built in; recognition still requires the externally installed Tesseract executable and all configured language data.

<a id="manual-v1-0-metadata"></a>
#### Metadata

52. Inspect PDF metadata.
53. Inspect DOCX metadata.
54. Inspect XLSX metadata.
55. Inspect image metadata.
56. Confirm provenance.
57. Test malformed files.
58. Confirm the scan continues.

<a id="manual-v1-0-tags"></a>
#### Tags

59. View user tags.
60. View generated tag suggestions.
61. Accept a tag.
62. Reject a tag.
63. Restart and verify persistence.
64. Confirm rejected tags do not immediately reappear.
65. Filter Results by tag.

<a id="manual-v1-0-meaning-search"></a>
#### Meaning Search

66. Build the semantic index.
67. Observe progress.
68. Cancel indexing.
69. Resume or rebuild.
70. Search by exact filename.
71. Search by user tag.
72. Search by metadata.
73. Search by OCR text.
74. Search with a natural-language phrase.
75. Inspect result explanations.
76. Delete or move a test file externally.
77. Refresh the index.
78. Confirm stale results are removed.
79. Clear the index.
80. Confirm files remain untouched.
81. Rebuild the index.

<a id="manual-v1-0-restructuring-history"></a>
#### Restructuring history

Use only disposable files for the apply test.

82. Preview a folder restructuring.
83. Confirm preview alone does not mark the folder organized.
84. Apply a restructuring using disposable files.
85. Rescan the same folder.
86. Confirm full restructuring is not proposed again.
87. Add new files.
88. Confirm incremental organization is offered.
89. Change the structure manually.
90. Confirm change detection.
91. Use **Propose restructuring again**.
92. Confirm explicit override.

<a id="manual-v1-0-structure-history"></a>
#### Structure History

93. Open Structure History.
94. Inspect previous runs.
95. Filter by root.
96. Filter by status.
97. Open the source structure.
98. Open the proposed structure.
99. Open the applied structure.
100. Compare previous and newer structures.
101. Inspect added, removed, moved, renamed, and unchanged nodes.
102. Test a large structure.
103. Confirm accessible textual summaries.
104. Confirm diagrams are read-only.

<a id="manual-v1-0-migration-and-regression"></a>
#### Migration and regression

105. Start with existing v0.9.1 settings.
106. Confirm settings load.
107. Open an existing catalog.
108. Open existing saved searches.
109. Open existing tags.
110. Confirm no data is lost.
111. Test scanning.
112. Test cancellation.
113. Test Rules.
114. Test snapshots.
115. Test Catalog Search.
116. Test Help.
117. Test Diagnostics.
118. Test Operation History.
119. Test AI rename suggestions.
120. Test AI folder suggestions.
121. Confirm all suggestion workflows remain preview-only.

<a id="manual-v1-0-final-ocr-and-ai-text-hardening"></a>
#### Final OCR and AI-text hardening

122. On a system without Tesseract, use **Recheck OCR capability** and confirm the UI names Tesseract as unavailable while reporting the built-in PDF renderer separately.
123. Install or configure a supported Tesseract 5 executable with `eng`, recheck, and confirm its version and detected languages appear.
124. Select `deu` without German language data and confirm recognition is blocked with an actionable language message.
125. Install `deu`, select `deu` or `deu+eng`, recheck, and recognize a representative German image.
126. OCR a scanned multi-page PDF and confirm page boundaries/provenance are retained.
127. OCR a mixed PDF containing reliable native-text pages and scanned pages; confirm only insufficient pages require Tesseract.
128. Cancel mixed-PDF OCR and confirm the operation stops, normal scanning remains usable, and no `OpenSorSe/ocr/job-*` temporary workspace remains.
129. Exercise the page, file, time, raster-edge, extracted-text, and temporary-storage bounds and confirm each returns a controlled result.
130. Restart after changing OCR language/DPI/bounds and confirm settings persist and affected cached records are reprocessed.
131. Confirm accepted/user tags survive reprocessing and a rejected generated tag remains suppressed for an unchanged source fingerprint.
132. Enable AI but leave **Allow local AI to analyze extracted document text** off; confirm no document-text action appears or reaches the provider.
133. Enable document-text interpretation and inspect the non-local endpoint warning before using a custom remote endpoint.
134. Select one indexed document and explicitly generate a document interpretation proposal.
135. Confirm the preview is labelled AI-generated/unverified and contains only bounded type/title/tag/date/issuer/folder suggestions, confidence, and explanation.
136. Reject or dismiss the proposal and confirm no source file, folder, embedded metadata, or tag is changed automatically.
137. Return malformed, fenced, unknown-source, unsafe-folder, overlong, excessive-count, and out-of-range-confidence provider fixtures and confirm whole-response rejection.
138. Disable the global AI switch while a request is active and confirm cancellation, preview clearing, and no later provider-driven UI update.
139. Point Ollama at an unavailable endpoint/model and confirm OCR, scanning, Results, Duplicate View, catalog, tags, saved searches, semantic search, and Structure history continue to work.
140. Compare source-file hashes/timestamps before and after all OCR and AI interpretation checks and confirm no automatic filesystem modification occurred.

<a id="manual-v1-0-file-assistant-reliability"></a>
#### File Assistant reliability

141. With AI off, select a file and confirm no File Assistant controls or Ollama communication occurs.
142. Enable AI and rename suggestions, leave the model empty, and confirm the action explains that a model must be selected.
143. Stop Ollama, select a file, choose **Retry connection**, and confirm the concise unavailable state.
144. Start Ollama, retry, and confirm server/model readiness refreshes without restarting OpenSorSe.

<a id="manual-v1-0-unified-advanced-diagnostics"></a>
##### Unified Advanced Diagnostics

145. Enable AI, Advanced mode, **Enable diagnostics**, **AI diagnostics**, and **Show unredacted diagnostic content**; save.
146. Start a rename suggestion and confirm the unified non-modal diagnostics window opens immediately and stays usable.
147. Observe stages update through request serialization, connection, response receipt, extraction, parsing, validation, and completion.
148. Copy the exact system prompt, user prompt, and request JSON; confirm request JSON names the selected model, uses `stream: false`, includes the capability JSON Schema in `format`, temperature `0.0`, and `keep_alive: 5m`.
149. Compare the raw Ollama envelope, extracted assistant `response`, and pretty-printed parsed JSON in their shared sections.
150. Trigger or reproduce a validation failure and confirm the Validation tab reports the property, required status, expected/actual type and value, and a precise failure—for example, an object-valued `reason` is identified as an object.
151. Exercise category/status filters, section copy, complete-report copy, selected JSON/text export, all-session export, word-wrap, auto-scroll, selected clear, and clear-all.
152. Close the diagnostics window during a request and confirm the AI operation continues.
153. Disable diagnostics and save; confirm retained records are cleared, then start another request and confirm no diagnostics window opens.
154. Repeat with Ollama unavailable, a missing model, timeout, cancellation, malformed JSON, Markdown-fenced JSON, and a non-success HTTP response.
155. Confirm no prompt or response body appears in ordinary application log files and no file or folder is changed by any suggestion.

<a id="manual-v1-0-ai-diagnostic-flow"></a>
###### AI diagnostic flow

- Run a successful rename request and confirm exact system prompt, user prompt, serialized Ollama request, endpoint/model, raw HTTP response, extracted assistant content, parsed response, validation detail, and timing remain separate.
- Exercise invalid structured output, Ollama unavailable, timeout, and cancellation. Confirm terminal states and warnings/errors are accurate and each later request remains usable.
- Run connection test and model discovery and confirm each has its own bounded session.
- Generate AI interpretation from indexed document text and confirm its AI session shows the related OCR/extraction session ID.
- Return one repairable malformed/schema-invalid response and then valid corrected JSON. Confirm exactly two related AI sessions retain the original and repair prompts/responses separately.
- Repeat with timeout, cancellation, unknown source ID, absolute/traversal path, invented metadata, oversized response, and a second invalid repair. Confirm timeout/cancellation/safety failures are not repaired and no operation makes more than two generation requests.

<a id="manual-v1-0-ocr-and-text-extraction-diagnostic-flow"></a>
###### OCR and text-extraction diagnostic flow

- Test a native-text PDF, scanned PDF, mixed native/scanned PDF, image containing text, blank page, low-quality or rotated scan, and corrupt file.
- Confirm the native-text quality decision and OCR fallback reason are explicit; mixed PDFs preserve separate per-page native and OCR sources.
- Confirm raw native text, raw OCR text, normalized text, and text supplied downstream remain distinct and include character counts/truncation.
- Confirm engine/version/language, page count/status, render DPI/dimensions, preprocessing, duration, cancellation, warnings, errors, and partial success are accurate. Do not expect invented confidence.
- Confirm rendered-page previews state that they were not retained and temporary OCR images are removed.

<a id="manual-v1-0-scanning-diagnostic-flow"></a>
###### Scanning diagnostic flow

- Test a normal folder, ordinary files unsupported by downstream extraction, an inaccessible folder, cancellation, a missing/changed file, a metadata failure, a reparse-point/junction, and a folder exceeding 500 discovered entries.
- Confirm accepted files remain accepted regardless of downstream extraction support; skipped entries state their actual reason.
- Confirm root/options, start/end, discovery, progress, counts, elapsed time, access/reparse decisions, cancellation, and metadata issues are visible.
- Confirm a large scan aggregates and reports omitted detail rather than growing without bound.

<a id="manual-v1-0-privacy-settings-and-lifecycle"></a>
###### Privacy, settings, and lifecycle

- Repeat AI, OCR, and scanning in redacted mode and verify paths, filenames, document/OCR text, metadata, tags, terms, prompts, and responses are not exposed.
- Explicitly enable unredacted content, repeat with disposable data, and confirm exact content is visible while credentials, secrets, and authorization headers remain absent.
- Toggle each category independently and confirm only enabled, instrumented categories create detailed sessions. Confirm planned categories say **not yet instrumented** and do not claim collection.
- Turn the master switch off and confirm subordinate controls are inactive; save and confirm retained sessions are cleared and new operations produce no detailed sessions.
- Close the diagnostics window during AI, OCR, and scanning operations and confirm no operation is cancelled.
- Verify all seven shared tabs, reverse chronology, live updates, filters, correlation IDs, clear actions, JSON/text exports, auto-scroll, and word wrap.
- Exit and restart OpenSorSe and confirm retained diagnostic history is gone. Inspect normal logs and confirm no sensitive detailed content appears.
156. Select an unavailable model and confirm the distinct model-missing state.
157. Select an installed model, save, retry, and generate a rename suggestion.
158. Confirm the actual model used matches the newly selected model.
159. Cancel a running rename request, then retry it and confirm the command is usable.
160. Repeat after timeout, connection failure, and malformed response; confirm each next request can run.
161. Switch files after cancellation and confirm a later proposal belongs only to the new selected file.
162. Confirm rename, folder, and document proposals remain labelled unverified and never change a file automatically.
163. Select 13 files for a folder suggestion. Confirm the UI reports all 13, states that none were sent, creates no partial plan, and makes no Ollama discovery or generation request.

<a id="manual-v1-0-small-model-manual-matrix"></a>
#### Small-model manual matrix

Do not claim model compatibility until every row below is run against disposable data, the exact Ollama model ID/tag and quantization/build are recorded, and failures are retained with diagnostics exports. Use one installed model in each size band; a blank exact ID means the row is not run.

| Model band | Exact Ollama model ID / build | Case | Expected safety result | Status | Diagnostic/export reference |
| --- | --- | --- | --- | --- | --- |
| Approximately 2B |  | Clear filename | One grounded extension-free stem; original extension appended by OpenSorSe | Not run |  |
| Approximately 2B |  | Ambiguous filename | `no_suggestion` | Not run |  |
| Approximately 2B |  | Scanned document with OCR noise | Supported values only or `no_suggestion`; no invented facts | Not run |  |
| Approximately 2B |  | Multilingual metadata | Valid Unicode grounded output or `no_suggestion` | Not run |  |
| Approximately 2B |  | Obvious folder categories | Every opaque ID assigned exactly once | Not run |  |
| Approximately 2B |  | Ambiguous folder files | Uncertain IDs assigned to `Other` | Not run |  |
| Approximately 2B |  | Malformed-response recovery | At most one related repair; corrected JSON or controlled rejection | Not run |  |
| Approximately 4B |  | Clear filename | One grounded extension-free stem; original extension appended by OpenSorSe | Not run |  |
| Approximately 4B |  | Ambiguous filename | `no_suggestion` | Not run |  |
| Approximately 4B |  | Scanned document with OCR noise | Supported values only or `no_suggestion`; no invented facts | Not run |  |
| Approximately 4B |  | Multilingual metadata | Valid Unicode grounded output or `no_suggestion` | Not run |  |
| Approximately 4B |  | Obvious folder categories | Every opaque ID assigned exactly once | Not run |  |
| Approximately 4B |  | Ambiguous folder files | Uncertain IDs assigned to `Other` | Not run |  |
| Approximately 4B |  | Malformed-response recovery | At most one related repair; corrected JSON or controlled rejection | Not run |  |
| Approximately 7B or 8B |  | Clear filename | One grounded extension-free stem; original extension appended by OpenSorSe | Not run |  |
| Approximately 7B or 8B |  | Ambiguous filename | `no_suggestion` | Not run |  |
| Approximately 7B or 8B |  | Scanned document with OCR noise | Supported values only or `no_suggestion`; no invented facts | Not run |  |
| Approximately 7B or 8B |  | Multilingual metadata | Valid Unicode grounded output or `no_suggestion` | Not run |  |
| Approximately 7B or 8B |  | Obvious folder categories | Every opaque ID assigned exactly once | Not run |  |
| Approximately 7B or 8B |  | Ambiguous folder files | Uncertain IDs assigned to `Other` | Not run |  |
| Approximately 7B or 8B |  | Malformed-response recovery | At most one related repair; corrected JSON or controlled rejection | Not run |  |

For each row, verify redacted and explicitly unredacted diagnostics, exact schema transport, property ordering, validation message clarity, normal-log privacy, and unchanged source files. Record model-specific limitations rather than treating a repairable response as proof of compatibility.

<a id="manual-v1-0-completion-record"></a>
#### Completion record

Manual testing is complete only when all failures are recorded with reproduction steps and either fixed or explicitly accepted as release limitations. Do not merge the v1.0 branch into `main` until both the inherited v0.9.1 checklist and this checklist have been completed.

</details>

<a id="manual-v0-9-1"></a>
## v0.9.1

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/MANUAL_TESTING_v0.9.1.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

Use a small disposable folder and record a before/after manifest. Ollama is optional for the non-AI checks and is externally managed; never use valuable files to test a pre-release build.

<a id="manual-v0-9-1-preparation"></a>
#### Preparation

1. Build and launch the `v0.9.1` branch.
2. If an older `settings.json` is present, preserve a copy and verify it loads without being deleted or manually migrated.
3. Prepare a disposable folder containing a few documents, images, duplicate files, and safely named subfolders.
4. Record filenames, paths, sizes, and timestamps before testing.

<a id="manual-v0-9-1-settings-and-visibility"></a>
#### Settings and visibility

1. Start with default settings. Verify Dashboard, Scan, Results, Duplicate View, Rules, Saved catalog, Catalog search, Settings, Help, and About are available.
2. Verify **Enable AI features** and **Show advanced features** are off.
3. Verify AI capability/provider controls and the Results AI panel are hidden. Verify Compare snapshots, Diagnostics, and Operation history are hidden.
4. Turn on **Enable AI features**. Verify the two capability switches appear in Settings immediately; save and verify the Results AI panel remains absent until a capability is enabled.
5. Enable only **Enable file rename suggestions**, save, and verify only the rename proposal controls appear for an eligible selected result.
6. Disable rename, enable only **Enable folder structure suggestions**, save, and verify only folder-structure controls appear when results exist.
7. Enable both capabilities and verify neither disables or resets the other.
8. Without advanced mode, verify the endpoint, **Check connection**, model discovery/selection, timeout, and capability controls remain usable. Enter `5`, `300`, `4`, `301`, and non-numeric timeout text; verify only 5 through 300 can be saved.
9. Turn on **Show advanced features**, save, and verify Compare snapshots, Diagnostics, Operation history, detailed logging, and the opt-in AI request diagnostic control appear.
10. Verify the four combinations independently: neither flag; advanced only; AI only; both AI and advanced.
11. While an advanced page is selected, turn advanced mode off and save. Verify navigation safely returns to Dashboard and the hidden page cannot be reached by keyboard navigation.
12. Scroll midway down Settings, toggle sections that change visibility, and run connection/model actions. Verify the same Settings page remains active and its scroll position does not jump unexpectedly.
13. Restart and verify all saved flags and provider values persist. Disable AI/advanced again and confirm hidden dependent values are preserved when re-enabled.

<a id="manual-v0-9-1-provider-failures"></a>
#### Provider failures

1. With AI and advanced mode enabled, stop Ollama or use an unreachable test endpoint. Test the connection and generate a suggestion. Verify concise unavailable messages and continued scanning/search use.
2. Start Ollama with no installed model, or clear the selected model. Verify a suggestion is blocked before generation and the message directs the user to advanced Settings.
3. Select a nonexistent model and verify a missing-model failure is controlled.
4. Cancel an in-flight connection or generation request and verify a cancellation message with no stale suggestion.
5. If practical with a controlled stub endpoint, return HTTP errors, an empty response, oversized response, malformed JSON, Markdown-fenced JSON, and an unsafe but well-formed response. Verify each is rejected without raw exception text.
6. Configure endpoint variants ending in `/api`, `/api/tags`, and `/api/generate`; verify connection/model/generation requests do not contain doubled API path segments.

<a id="manual-v0-9-1-suggestion-workflow"></a>
#### Suggestion workflow

1. Select one known result and request a rename. Verify the proposal repeats that source, preserves its extension, has no path component, and is labelled AI-generated, unverified, and review-only.
2. Edit the proposed base filename to another safe value, accept it as a review decision, and verify the actual file remains unchanged.
3. Generate again and reject the proposal. Verify dismissal does not change the file.
4. Select result metadata and request a folder structure. Verify it contains only logical relative folders and assignments to known selected files.
5. Accept or reject the folder proposal and verify no folder is created and no file is moved.
6. Disable the active capability while a proposal is visible, save Settings, and verify the proposal and action controls are cleared/hidden.
7. Disable global AI, save, and verify all AI UI disappears and no provider request occurs.
8. Observe a request in progress and verify the current stage and elapsed time update, cancellation ends cleanly, and changing the Results context clears stale progress and proposals.
9. With AI and advanced mode enabled, opt into **Enable AI request diagnostics**, complete a controlled request, and inspect the newest record in Diagnostics. Verify stage history, model, endpoint, sizes, included/omitted counts, validation outcome, prompt, and response are available.
10. Verify the diagnostic warning explains that filenames and relative folder metadata may be present. Exercise copy and clear, then disable advanced mode, global AI, or the diagnostic switch in turn; verify raw records clear and no new raw record is retained.

<a id="manual-v0-9-1-diagnostics-help-catalog-search-and-status"></a>
#### Diagnostics, Help, Catalog Search, and status

1. With advanced mode enabled, open Diagnostics and generate Information, Warning, and Error events through ordinary controlled actions. Refresh, filter by severity/category, select an event, and copy its safe details.
2. Verify the event list is newest first, long details wrap, empty filters show one clear empty state, and no raw stack trace or credential appears in the normal view.
3. Verify status feedback on Settings, AI Suggestions, Diagnostics, Catalog Search, and Duplicate View always includes a textual severity label and remains readable when narrow.
4. Use **Help** from Dashboard, Scan, Results, Duplicate View, AI Suggestions, Rules, Saved catalog, Catalog Search, Compare snapshots, Settings, Diagnostics, Operation history, and About. Verify the relevant topic opens, related topics work, and Back returns safely.
5. Open Help from an advanced page, disable advanced mode, and use Back. Verify Help falls back to Dashboard rather than restoring a hidden page.
6. In Catalog Search, run a query with hits and one without hits. Verify there is one status and one count/empty result, and the empty message appears only after a completed search.
7. Clear the query, create/run/rename/remove a saved search, and use the two-step clear-all action. Restart and verify renamed definitions preserve compatibility while hits, snapshots, tags, and selected files are unaffected.

<a id="manual-v0-9-1-duplicate-view-and-safe-opening"></a>
#### Duplicate View and safe opening

1. Open **Duplicate View** from Results and verify a two-file group reads **2 identical files**, shows both filenames, shortened parent paths with full-path tooltips, per-file size, and **Possible space saved by keeping one copy**.
2. Resize the window narrower and wider. Verify group cards wrap without page-wide horizontal scrolling or clipped primary actions.
3. For a controlled disposable pair, use **Open file**, **Open containing folder**, and **Open both files**. Verify only the requested known paths are opened.
4. For a larger group, select more than five members and use **Open selected files** and **Open selected folders**. Verify one action opens at most five targets and reports the cap.
5. Include one deliberately unavailable known path in a controlled snapshot. Verify remaining valid targets still open and the partial failure is reported.
6. Verify **Open both files** is absent for groups larger than two, unknown/stale rows cannot be opened, cancellation is controlled, and selection clears when the Results snapshot changes.
7. Use **Show group files in results** and **Back to all results**. Verify navigation and selection remain coherent.

<a id="manual-v0-9-1-non-ai-regression-smoke-test"></a>
#### Non-AI regression smoke test

1. Scan the disposable folder; verify progress, cancellation, warnings, and result totals.
2. Filter, sort, page, use ranked search, and inspect result details.
3. Review exact duplicate groups in Duplicate View, safely open controlled comparison targets, and return to the corresponding result rows.
4. Add/remove a manual tag and verify deterministic and accepted tags remain searchable without AI.
5. Enable the catalog, save a bounded snapshot, reopen it, name it, and inspect captured source scope.
6. Search the catalog, save/rerun/remove a named search, and verify hits are not persisted.
7. With advanced mode enabled, compare two snapshots and verify filters, scope status, cancellation, and historical opening.
8. Recheck the disposable-folder manifest. No filename, path, content, size, or timestamp should have changed because of an OpenSorSe suggestion or review action.

<a id="manual-v0-9-1-expected-result"></a>
#### Expected result

All UI visibility combinations are coherent, settings survive restart, disabled commands cannot communicate with Ollama, failures remain isolated, and no suggestion or review action modifies the selected filesystem.

</details>
