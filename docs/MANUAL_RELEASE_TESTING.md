# Manual release testing

Repository files define tests. **One GitHub “Release manual testing” issue per release owns execution results.** [Release index and prepared issue bodies](release-testing/README.md) identify actual published releases; a draft is preparation, never a second live ledger. Keep the original versioned reports unchanged.

## Start testing here

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

## Core scenarios

These definitions apply to **v2.4.0 and v2.13.0-rc** unless specified. Each has one small goal. They do not certify optional features. The historical drafts link procedures from their own release.

<a id="os-m01"></a>
### OS-M01: Launch the exact package

**Prerequisite:** Verified ZIP and disposable account from Start testing here.

1. Extract all files and open `OmniSorSe.exe`.
2. Open **About** in the sidebar footer. Check release version against the issue. For v2.13 open extracted `OmniSorSe.build.json` in Notepad and match `sourceRevision`/`productVersion` with the issue. Do not require that newer manifest in v2.4.
3. Return **Home**; confirm the first-scan action is available. v2.13 also presents Scan → Review → Organize; do not require that newer hierarchy in v2.4.

**Expected:** Correct version opens and first-scan action is usable.

**Cleanup:** Close About; leave the test app open for M02.

<a id="os-m02"></a>
### OS-M02: Scan four disposable files

**Prerequisite:** Untouched four-file sample, AI off.

1. On Home select **Scan a folder** (or **Scan folder**), then **Browse...**. Select `ReleaseSample`; Browse adds it to the roots list automatically. Confirm only that root is listed, then select **Start scan**. If entering a path by hand instead, select **Add folder** first.
2. Wait for completion; open **Files** (v2.4) or **Review** (v2.13).
3. Confirm all four expected filenames, including `Notes/readme.txt`, are listed. In File Explorer confirm names and text remain unchanged.

**Expected:** Four sample files are found; source files stay unchanged.

**Cleanup:** Leave the scan open. Do not add any real folders.

<a id="os-m03"></a>
### OS-M03: Find a known filename

**Prerequisite:** M02; local Search/background coverage enabled as above. Wait for sample indexing to complete.

1. Open **Search**, enter `lunar-sample`, then select **Search**.
2. Select `lunar-sample.txt` and inspect its path; it must belong to this sample.
3. Clear the query. If indexing is incomplete, record that state instead of a false Pass.

**Expected:** The known file is found in the selected sample.

**Cleanup:** Clear query/filters. Saved searches are a separate conditional check.

<a id="os-m04"></a>
### OS-M04: Review a proposed move without applying

**Prerequisite:** M06 found the two-file duplicate group; neither file changed.

1. In **Duplicates**, open the group and tick only `copy-b.txt`; keep `copy-a.txt` unticked. Select **Review selected duplicates**.
2. Inspect the proposed recovery move, any required **CreateDirectory** actions and warnings. Every source/destination must be within the disposable root; a fresh sample needs the recovery parent folders created. v2.13 also shows origin/purpose/action counts.
3. Try **Approve all safe** then **Deselect all** (v2.4), or **Approve all eligible** then **Exclude all** (v2.13); then **Approve** the eligible sample move **and its required destination-folder actions**. Select each action to inspect it, or use the bulk approval after checking that every action belongs to this sample. Do **not** click Apply yet. Check File Explorer: both originals must still exist unchanged.

**Expected:** A scoped proposal is reviewable; approval alone changes no files.

**Cleanup:** Leave this approved sample plan for M05, or navigate away without applying. Unexplained conflicts mean Blocked, not a guessed workaround.

<a id="os-m05"></a>
### OS-M05: Apply one move and Undo

**Prerequisite:** M04; Windows filesystem supports the required safety checks. All plan actions stay under the disposable root.

1. Select **Validate Plan**, **Apply Plan**, then **Confirm Apply Plan** only for the reviewed recovery move and its required sample-folder actions.
2. Check `copy-a.txt` remains and `copy-b.txt` moved to the exact recovery destination shown in the plan (File Explorer → View → Hidden items if needed). Its text must be unchanged. This is recoverable movement, not permanent deletion or reclaimed space.
3. Select **Undo** in Review Changes, or **Operation History → Undo → Confirm Undo**. Verify both original filenames/text are restored in File Explorer. Re-scan to refresh any removed logical row; Undo need not recreate that row immediately.

**Expected:** Only the reviewed copy moves; Undo restores its name/content.

**Cleanup:** Verify the four-file baseline. On failure preserve state/evidence for the owner instead of manually overwriting files. Unsupported-platform refusal is conditional C09, with an explicit M05 N/A reason for that platform.

<a id="os-m06"></a>
### OS-M06: Inspect an exact-duplicate pair

**Prerequisite:** M02 on the untouched sample.

1. Open **Duplicates** and select the group containing `copy-a.txt` and `copy-b.txt`.
2. Check only these two files are in the group and both paths are in the sample.
3. Close/reopen the details and confirm neither file changed. Leave at least one keeper if proceeding to M04.

**Expected:** The identical pair is grouped without changing either file.

**Cleanup:** Clear selections unless continuing directly to M04. Multi-group selection is conditional C01.

<a id="os-m07"></a>
### OS-M07: Keep a setting after restart

**Prerequisite:** Disposable profile; note initial **Show advanced features** value.

1. Open **Settings**, change **Show advanced features**, then select **Save**.
2. Close the app normally and reopen the same extracted executable in the same account.
3. Open Settings and confirm the chosen value persists.

**Expected:** The saved setting survives a normal restart.

**Cleanup:** Restore the original value and Save. Do not use Reset on another person's profile.

<a id="os-m08"></a>
### OS-M08: Keyboard and visible controls

**Prerequisite:** Keyboard; record one tested display scale/window size per row. Owner selects additional supported scales when layout changed.

1. With Tab/Shift+Tab and Enter/Space, visit Home, Files/Review, Search and Settings; observe a visible focus indicator and reachable primary actions.
2. Resize to the smallest allowed window; scroll the sample result list and reach its controls.
3. Check that navigation, primary buttons and status text do not clip or overlap. Record an exact screen/size for a defect.

**Expected:** Focus and primary controls remain visible and usable.

**Cleanup:** Restore window size/scale. Screen-reader announcements are conditional C07.

## Conditional scenarios

Activate only for relevant changes, platform or release risk, using the [coverage map](release-testing/coverage-map.md). Missing a required environment becomes Blocked after assessment, not an automatic N/A. Keep each selected legacy case as its own row; retain unresolved scope explicitly. C01–C12 below use v2.13 controls unless a version-specific procedure is linked; do not impose them on older releases unchanged.


<a id="os-c01"></a>
### OS-C01: Conditional: many duplicates

**Prerequisite:** Disposable duplicate groups with at least seven extra copies and one keeper per group.

1. Select more than five duplicate copies across groups and retain a keeper in each.
2. Inspect the combined recovery Change Plan and the separately explained five-item shell-open limit.
3. On supported Windows, explicitly Apply and Undo only that reviewed sample plan.

**Expected:** More than five copies can be reviewed safely.

**Cleanup:** Close/cancel pending work, restore changed settings, and use a fresh disposable profile/sample before a different package. Preserve evidence before resetting.

<a id="os-c02"></a>
### OS-C02: Conditional: optional AI

**Prerequisite:** Existing approved local provider/model only; owner-provided synthetic prompt.

1. With AI disabled, open the visible organization card and follow its Settings link.
2. With the already approved local provider enabled, request one rename/folder proposal; inspect provenance and Keep/Dismiss.
3. Cancel a request and verify it cannot later apply a change; accepting a proposal only hands it to Change Plan review.

**Expected:** Disabled AI is inert; proposals require review.

**Cleanup:** Close/cancel pending work, restore changed settings, and use a fresh disposable profile/sample before a different package. Preserve evidence before resetting.

<a id="os-c03"></a>
### OS-C03: Conditional: relationship merge

**Prerequisite:** Disposable indexed collections, explicit owner scope for one merge.

1. Read the exact target/consequence of a merge confirmation and cancel it.
2. Repeat and explicitly confirm the same synthetic merge.
3. Restart and inspect retained collection authority; source files must be unchanged. Use separate inherited-case rows for unlink, split, forget and reset-to-automatic.

**Expected:** Cancel preserves state; confirmed merge persists.

**Cleanup:** Close/cancel pending work, restore changed settings, and use a fresh disposable profile/sample before a different package. Preserve evidence before resetting.

<a id="os-c04"></a>
### OS-C04: Conditional: catalog cancel

**Prerequisite:** Two explicit synthetic historical catalog snapshots; enough entries to observe cancellation.

1. Open Before & After / Compare scans, select Earlier scan and Later scan, then choose Compare selected scans.
2. Use Cancel comparison while active, then start another comparison.
3. Confirm responsive cancellation and that the first operation cannot replace the later result.

**Expected:** Cancelled comparison cannot publish stale results.

**Cleanup:** Close/cancel pending work, restore changed settings, and use a fresh disposable profile/sample before a different package. Preserve evidence before resetting.

<a id="os-c05"></a>
### OS-C05: Conditional: upgrade

**Prerequisite:** Disposable account with copied v2.12 state, or v2.4/schema-5 state for migration coverage.

1. Close the predecessor and install the exact current per-user package.
2. Launch and inspect saved searches, tags and relationship decisions from that prepared profile.
3. Record predecessor/schema separately; do not infer schema-5 migration from a v2.12-to-v2.13 run.

**Expected:** Prior supported profile remains readable.

**Cleanup:** Close/cancel pending work, restore changed settings, and use a fresh disposable profile/sample before a different package. Preserve evidence before resetting.

<a id="os-c06"></a>
### OS-C06: Conditional: state backup

**Prerequisite:** Disposable indexed state and another disposable profile; no personal backup.

1. Export a format-2 .oms-state archive and read its privacy/scope warning.
2. Preview/restore it in the disposable destination profile.
3. Verify one saved query and explicit relationship/collection decision; unresolved identities must be reported, not guessed by filename. Format-1 compatibility uses its own inherited case.

**Expected:** Explicit retained decisions restore without guessing.

**Cleanup:** Close/cancel pending work, restore changed settings, and use a fresh disposable profile/sample before a different package. Preserve evidence before resetting.

<a id="os-c07"></a>
### OS-C07: Conditional: screen reader

**Prerequisite:** Actual screen reader and native supported platform.

1. Navigate Search, Smart Tag review and one target-specific relationship confirmation.
2. Observe information/ready/warning/error/disabled/unavailable states that the fixture can produce.
3. Record announced names/states/focus and unobserved states separately; color alone is insufficient.

**Expected:** Status and action meaning is announced.

**Cleanup:** Close/cancel pending work, restore changed settings, and use a fresh disposable profile/sample before a different package. Preserve evidence before resetting.

<a id="os-c08"></a>
### OS-C08: Conditional: index progress

**Prerequisite:** Owner-approved mixed synthetic library and declared hardware/resource policy.

1. Run a bounded index and observe terminal completions, recent throughput and ETA availability.
2. Record data shape, CPU/disk/queue/stage observations during the same interval.
3. Compare against predeclared criteria; do not infer real 20k/100k coverage from a small sample.

**Expected:** Throughput/ETA reflect observed completed work.

**Cleanup:** Close/cancel pending work, restore changed settings, and use a fresh disposable profile/sample before a different package. Preserve evidence before resetting.

<a id="os-c09"></a>
### OS-C09: Conditional: native macOS

**Prerequisite:** Native Intel or Apple Silicon host with matching approved DMG; disposable account.

1. Verify that architecture's hash, open the DMG and copy OmniSorSe.app to Applications.
2. Record Gatekeeper/signing status without disabling system security globally; launch, scan/Search the sample and close normally.
3. Observe the platform mutation boundary. Use separate rows for Intel and ARM64; cross-build evidence cannot pass either.

**Expected:** Matching package opens; unsupported mutation stays blocked.

**Cleanup:** Close/cancel pending work, restore changed settings, and use a fresh disposable profile/sample before a different package. Preserve evidence before resetting.

<a id="os-c10"></a>
### OS-C10: Smart Tag decision (v2.13)

**Prerequisite:** Indexed synthetic document with a reviewable Smart Tag and recorded expected suggestion; fixture owner supplies it before testing.

1. In Review select the document, open Smart Tags and inspect evidence/status.
2. Select one suggestion and **Accept suggestion** or **Reject suggestion**; note the choice.
3. Refresh/restart and inspect the same file's retained decision.

**Expected:** The explicit choice persists; no source file is changed.

**Cleanup:** Use a fresh test profile for another decision. Unavailable/still-indexing states remain separate inherited cases.

<a id="os-c11"></a>
### OS-C11: Related Files decision (v2.13)

**Prerequisite:** Two synthetic indexed files with an owner-prepared visible pair; record their IDs/labels.

1. In **Related Files**, select the pair and choose **Related** or **Not Related**.
2. Request **Use automatic result**, inspect the exact target, then **Cancel**.
3. Restart and confirm the explicit choice remains and source files are unchanged.

**Expected:** Cancellation preserves the explicit pair choice after restart.

**Cleanup:** Retain evidence and reset the disposable profile. Confirmed reset, unlink, split and Forget are individual inherited checks.

<a id="os-c12"></a>
### OS-C12: Saved search (v2.13)

**Prerequisite:** M03 successful; synthetic query `lunar-sample`.

1. Run that query, expand **Saved searches and index maintenance**, give it the name `Sample lookup`, then **Save new**.
2. Navigate away/back, select `Sample lookup` and **Open**.
3. Confirm query and known result return; collapse/expand **Refine this search** without losing focus or selection.

**Expected:** The saved query reloads and controls remain usable.

**Cleanup:** Select only `Sample lookup` and **Delete selected**. Earlier versions use their own saved-view/catalog-search procedures.

## Retained coverage and issue handoff

The [coverage map](release-testing/coverage-map.md) maps original requirements to core, conditional, automated evidence or owner decisions. Reclassification does not satisfy anything. For inherited checks use **OS-L<version>-NNN**, the one-based checkbox ordinal across the original file (for example OS-L2.13-006); do not renumber frozen sources. Select one requirement per issue row, link its original heading, and state prerequisite/actions/expected/reset before execution. Split an oversized legacy item into suffixed cases when necessary; its parent remains unresolved until all required parts have evidence.

The [index](release-testing/README.md) explains how to create or reuse the release's issue and pin definition links. Only the issue's current table and preserved attempt comments hold ongoing results. Historical reports are frozen evidence; drafts are never synchronized back from an issue. Before publication assign source/artifact, environment, owner, conditional scope and acceptance criteria. Signing, external-provider permission and publication approval are owner decisions outside the test table.
