# Legacy manual coverage map

This is a definition/routing catalog, not an execution ledger. Use the
[manual release-testing guide](../MANUAL_RELEASE_TESTING.md) and the release's
single GitHub testing issue for results. The short core table does not waive
the older unresolved requirements below.

## Stable source IDs and dispositions

`OS-L2.10-001` means the first checkbox in
[`MANUAL_TESTING_v2.10.md`](../MANUAL_TESTING_v2.10.md), counting every checkbox
from top to bottom, including checked historical evidence. Use the same
`OS-L<version>-NNN` convention for the other checkbox files. Frozen source
checklists retain their wording and order. The source heading plus ordinal
locates the exact acceptance requirement; an ordinal is not a test result.

- **Core** links a reusable core definition. If a row also retains a
  conditional variant, completing the core does not complete that variant.
- **Conditional** retains the full original item under its own legacy ID.
  Select it when the changed feature, platform, support claim or unresolved
  risk applies. Add each selected item as a separate execution row, with its
  prerequisite, exact actions, expected result and safe reset clarified before
  testing. Split compound legacy items into suffixed rows (`-a`, `-b`, etc.)
  when they need separate attempts, environments or outcomes. Do not aggregate
  a source range into a single pass.
- **Automated** links the relevant existing check/evidence owner. It is not
  a newly observed run or proof of interactive success.
- **Owner decision** records scope, permissions or release evidence policy.
  It never passes a human test. Unselected applicable legacy items remain
  unresolved; record deferral/risk acceptance explicitly in the release issue.

The 130 v2.10–v2.13 items below were all unchecked in the retained sources.
They remain unresolved until separately observed or explicitly dispositioned
by the release owner. No test was executed to prepare this map. Historical
checked items later in this file retain only their original scope; no result
is transferred onto another candidate, package, environment or test.

Reusable definitions:
[M01 launch](../MANUAL_RELEASE_TESTING.md#os-m01),
[M02 scan](../MANUAL_RELEASE_TESTING.md#os-m02),
[M03 Search](../MANUAL_RELEASE_TESTING.md#os-m03),
[M04 review](../MANUAL_RELEASE_TESTING.md#os-m04),
[M05 Apply/Undo](../MANUAL_RELEASE_TESTING.md#os-m05),
[M06 duplicates](../MANUAL_RELEASE_TESTING.md#os-m06),
[M07 settings](../MANUAL_RELEASE_TESTING.md#os-m07),
[M08 keyboard/layout](../MANUAL_RELEASE_TESTING.md#os-m08).
`C01`–`C12` refer to the conditional definitions in the same guide. A reference
to one is a starting scenario; retained variations stated below still apply.

**A-Package** means the existing
[native packaging workflow](../../.github/workflows/release-packaging.yml)
and its exact-artifact evidence in [Release Status](../RELEASE_STATUS.md).
**A-Host** means the existing
[hosted validation workflow](../../.github/workflows/cross-platform-validation.yml)
and the same evidence owner. Neither link claims a run occurred in this task.

## v2.10 master matrix — 61 items

Source: [v2.10 full definitions](../MANUAL_TESTING_v2.10.md).
“Conditional: original” always means this exact source item, not an omitted
or replaced gate.

| Stable source ID | Original requirement | Disposition / retained destination |
| --- | --- | --- |
| <a id="os-l2.10-001"></a>OS-L2.10-001 | Fresh Windows install/profile | Core M01; conditional original for installer lifecycle. |
| <a id="os-l2.10-002"></a>OS-L2.10-002 | v2.4/schema-5 migration | Conditional C05; retain exact predecessor/schema case. |
| <a id="os-l2.10-003"></a>OS-L2.10-003 | Interrupted migration recovery | Conditional: original, disposable managed recovery copy. |
| <a id="os-l2.10-004"></a>OS-L2.10-004 | Two processes, one profile | Conditional: original ownership failure. |
| <a id="os-l2.10-005"></a>OS-L2.10-005 | Killed profile owner/restart | Conditional: original abnormal-shutdown recovery. |
| <a id="os-l2.10-006"></a>OS-L2.10-006 | Concurrent distinct profiles | Conditional: original on supported host. |
| <a id="os-l2.10-007"></a>OS-L2.10-007 | Install/upgrade/uninstall preservation | Conditional C05 plus original uninstall/reinstall variant. |
| <a id="os-l2.10-008"></a>OS-L2.10-008 | Version/source/hash agreement | Automated A-Package; conditional original for visible About/package agreement. |
| <a id="os-l2.10-009"></a>OS-L2.10-009 | Newer-state downgrade refusal | Conditional: original, copied disposable profile only. |
| <a id="os-l2.10-010"></a>OS-L2.10-010 | Empty and one-file libraries | Conditional: original two size cases; M02 is only the normal scan. |
| <a id="os-l2.10-011"></a>OS-L2.10-011 | Base Search before Deep work | Conditional: original progressive-indexing case. |
| <a id="os-l2.10-012"></a>OS-L2.10-012 | Deep cancel/restart/retry/reconcile | Conditional: original lifecycle cases, separate attempts. |
| <a id="os-l2.10-013"></a>OS-L2.10-013 | Search ranking, facets, counts, Saved Views and snapshot selection | Core M03 covers only filename lookup; C12 covers the v2.13 saved-query entry path. Conditional original retains ranking, facets, counts and snapshot selection. |
| <a id="os-l2.10-014"></a>OS-L2.10-014 | Search-to-Files return with query/facets/Saved View preserved | Conditional original; M03 and C12 do not cover the complete return/context workflow. |
| <a id="os-l2.10-015"></a>OS-L2.10-015 | Smart Tag bands/decisions/review | Conditional C10 plus original band/continuous-review variations. |
| <a id="os-l2.10-016"></a>OS-L2.10-016 | Real 20k library | Conditional: original scale observations; separate from M02. |
| <a id="os-l2.10-017"></a>OS-L2.10-017 | Real 100k library/bounds | Conditional: original, hardware/limits recorded. |
| <a id="os-l2.10-018"></a>OS-L2.10-018 | 10/100 selections, three entry points | Conditional: original selection matrix. |
| <a id="os-l2.10-019"></a>OS-L2.10-019 | Recipe modes/fallback/edit/re-preview | Conditional: original recipe variants; M04 covers basic review only. |
| <a id="os-l2.10-020"></a>OS-L2.10-020 | Evidence/name/path/collision failures | Conditional: original controlled edge cases. |
| <a id="os-l2.10-021"></a>OS-L2.10-021 | Apply/reconcile/return | Core M05; conditional original rename/move/directory-action variants. |
| <a id="os-l2.10-022"></a>OS-L2.10-022 | Stale source/new destination | Conditional: original preflight failures. |
| <a id="os-l2.10-023"></a>OS-L2.10-023 | Partial/rollback/restart/Undo conflict | Conditional: original recovery cases, separate attempts. |
| <a id="os-l2.10-024"></a>OS-L2.10-024 | No watched/Saved View auto-execution | Conditional: original no-mutation authority check. |
| <a id="os-l2.10-025"></a>OS-L2.10-025 | Export privacy/archive contents | Conditional C06 plus original archive inspection. |
| <a id="os-l2.10-026"></a>OS-L2.10-026 | Empty/existing restore conflicts | Conditional C06 plus original two target-profile cases. |
| <a id="os-l2.10-027"></a>OS-L2.10-027 | All authored state/unresolved IDs | Conditional C06 plus original category-by-category restore. |
| <a id="os-l2.10-028"></a>OS-L2.10-028 | Restore cancellation/failure | Conditional: original pre-restore recovery-point cases. |
| <a id="os-l2.10-029"></a>OS-L2.10-029 | Corrupt Change Plan/journal | Conditional: original maintainer failure injection. |
| <a id="os-l2.10-030"></a>OS-L2.10-030 | Corrupt settings/views/recipes/watchers | Conditional: original authority-specific recovery. |
| <a id="os-l2.10-031"></a>OS-L2.10-031 | Forget across all stores | Conditional: original file/source absence checks. |
| <a id="os-l2.10-032"></a>OS-L2.10-032 | Clear/Forget/Rebuild/Restore distinctions | Conditional: original data-scope check. |
| <a id="os-l2.10-033"></a>OS-L2.10-033 | Full disk/write failures | Conditional: original isolated storage-failure cases. |
| <a id="os-l2.10-034"></a>OS-L2.10-034 | Locks/permissions/read-only/temp | Conditional: original isolated permission cases. |
| <a id="os-l2.10-035"></a>OS-L2.10-035 | Removed/remounted USB source | Conditional: original scan/preview/Search/Apply cases. |
| <a id="os-l2.10-036"></a>OS-L2.10-036 | Hostile/large PDFs | Conditional: original non-sensitive bounded fixtures. |
| <a id="os-l2.10-037"></a>OS-L2.10-037 | PDF/OCR cancel/cleanup | Conditional: original native extraction cleanup. |
| <a id="os-l2.10-038"></a>OS-L2.10-038 | PDFium crash/hang behavior | Conditional: original isolated-host observation. |
| <a id="os-l2.10-039"></a>OS-L2.10-039 | Tesseract capability/failures | Conditional: original exact tool/language matrix. |
| <a id="os-l2.10-040"></a>OS-L2.10-040 | ffmpeg/ffprobe capability/failures | Conditional: original exact native-tool matrix. |
| <a id="os-l2.10-041"></a>OS-L2.10-041 | whisper capability/failures | Conditional: original runtime/model/cancel matrix. |
| <a id="os-l2.10-042"></a>OS-L2.10-042 | Ollama endpoint/capability states | Conditional C02 plus original endpoint/failure matrix; remote permission is an owner decision. |
| <a id="os-l2.10-043"></a>OS-L2.10-043 | Prompt injection/grounded suggestions | Conditional: original controlled hostile-document case. |
| <a id="os-l2.10-044"></a>OS-L2.10-044 | OmniBrille handoff/lifecycle | Conditional: original real companion cases. |
| <a id="os-l2.10-045"></a>OS-L2.10-045 | Healthy stores/ownership/startup | Conditional: original health and startup observation. |
| <a id="os-l2.10-046"></a>OS-L2.10-046 | Health degraded/recovery states | Conditional: original controlled failure states. |
| <a id="os-l2.10-047"></a>OS-L2.10-047 | Kill across five operations | Conditional: original restart cases, separate attempts. |
| <a id="os-l2.10-048"></a>OS-L2.10-048 | Slow-provider shutdown | Conditional: original bounded shutdown observation. |
| <a id="os-l2.10-049"></a>OS-L2.10-049 | Safe diagnostics privacy | Conditional: original redaction inspection. |
| <a id="os-l2.10-050"></a>OS-L2.10-050 | Operational runbooks | Conditional: select each relevant existing runbook as its own row. |
| <a id="os-l2.10-051"></a>OS-L2.10-051 | Keyboard across workflows | Core M08; conditional original Smart Tags/health/backup/restore paths. |
| <a id="os-l2.10-052"></a>OS-L2.10-052 | Screen-reader names/states/live updates | Conditional C07; retain original backup/conflict/recovery paths. |
| <a id="os-l2.10-053"></a>OS-L2.10-053 | Async/re-preview/restore focus | Core M08; conditional original asynchronous and restore focus cases. |
| <a id="os-l2.10-054"></a>OS-L2.10-054 | Compact scrolling/critical actions | Core M08. |
| <a id="os-l2.10-055"></a>OS-L2.10-055 | 100/125/150% and #29/#31 | Core M08; conditional original collection/Related layout reproductions. |
| <a id="os-l2.10-056"></a>OS-L2.10-056 | Native Windows and optional tools | Core M01 on Windows; conditional original native-tool checks. |
| <a id="os-l2.10-057"></a>OS-L2.10-057 | Native Intel macOS | Conditional C09; original filesystem/tool cases; signing scope is owner decision. |
| <a id="os-l2.10-058"></a>OS-L2.10-058 | Native ARM64 macOS | Conditional C09 on a separate host row; signing scope is owner decision. |
| <a id="os-l2.10-059"></a>OS-L2.10-059 | Native Linux source preview | Conditional: original Linux runtime/filesystem/tool cases. |
| <a id="os-l2.10-060"></a>OS-L2.10-060 | Compile is not native evidence | Owner decision: evidence classification; A-Host supports only recorded hosts. |
| <a id="os-l2.10-061"></a>OS-L2.10-061 | Signing/notarization truth | Owner decision: exact-artifact trust/signature evidence before claims. |

## v2.11 addendum — 20 items

Source: [v2.11 full definitions](../MANUAL_TESTING_v2.11.md).

| Stable source ID | Original requirement | Disposition / retained destination |
| --- | --- | --- |
| <a id="os-l2.11-001"></a>OS-L2.11-001 | Candidate/commit agreement | Automated A-Package; conditional original visible About/diagnostics agreement. |
| <a id="os-l2.11-002"></a>OS-L2.11-002 | ZIP without installed .NET | Core M01; conditional original clean-machine prerequisite. |
| <a id="os-l2.11-003"></a>OS-L2.11-003 | Per-user upgrade/uninstall/reinstall | Conditional C05 plus full original lifecycle/schema retention. |
| <a id="os-l2.11-004"></a>OS-L2.11-004 | Installer while app runs | Conditional: original Restart Manager and journal safety. |
| <a id="os-l2.11-005"></a>OS-L2.11-005 | Signature/SmartScreen | Conditional: observe exact package prompt; owner decision on trust/signing claim. |
| <a id="os-l2.11-006"></a>OS-L2.11-006 | Packaged .NET 10 desktop/AX/DPI | Core M08; conditional C07 and original #29/#31 cases. |
| <a id="os-l2.11-007"></a>OS-L2.11-007 | Packaged optional-tool readiness | Conditional: original present/absent tools, no automatic downloads. |
| <a id="os-l2.11-008"></a>OS-L2.11-008 | Intel DMG runtime/backup lifecycle | Conditional C09 on Intel; C06 and original health/exit checks. |
| <a id="os-l2.11-009"></a>OS-L2.11-009 | ARM64 DMG runtime/backup lifecycle | Conditional C09 on ARM64; C06 and original health/exit checks. |
| <a id="os-l2.11-010"></a>OS-L2.11-010 | Gatekeeper/signature/notarization | Conditional: record observed prompts; owner decision on exact trust claims. |
| <a id="os-l2.11-011"></a>OS-L2.11-011 | macOS Unicode/case/permissions/links | Conditional: original disposable filesystem matrix. |
| <a id="os-l2.11-012"></a>OS-L2.11-012 | macOS keyboard/VoiceOver/scaling | Conditional C07/C09; original native support-claim smoke. |
| <a id="os-l2.11-013"></a>OS-L2.11-013 | macOS native optional tools | Conditional: original per-architecture tool versions/errors. |
| <a id="os-l2.11-014"></a>OS-L2.11-014 | Linux SDK/source/native lifecycle | Conditional: original supported distro/source-preview setup. |
| <a id="os-l2.11-015"></a>OS-L2.11-015 | Linux filesystem/watcher/remount | Conditional: original native failure cases. |
| <a id="os-l2.11-016"></a>OS-L2.11-016 | Linux keyboard/desktop | Conditional: original named desktop environment. |
| <a id="os-l2.11-017"></a>OS-L2.11-017 | Linux preview/no installer claim | Owner decision: documentation/support claim inspection. |
| <a id="os-l2.11-018"></a>OS-L2.11-018 | Artifact SHA/RID/runtime/provenance | Automated A-Package; owner links exact run and published artifact identity. |
| <a id="os-l2.11-019"></a>OS-L2.11-019 | Artifact private/unintended contents | Automated A-Package; conditional original for any contents outside the linked check's scope. |
| <a id="os-l2.11-020"></a>OS-L2.11-020 | Trusted download/manual update | Conditional C05 plus original provenance/close/replace/health sequence. |

## v2.12 addendum — 24 items

Source: [v2.12 full definitions](../MANUAL_TESTING_v2.12.md).

| Stable source ID | Original requirement | Disposition / retained destination |
| --- | --- | --- |
| <a id="os-l2.12-001"></a>OS-L2.12-001 | Related with graph disabled | Conditional C11; original mixed-library quality observation. |
| <a id="os-l2.12-002"></a>OS-L2.12-002 | Graph enable/disable preserves direct data | Conditional: original authority boundary. |
| <a id="os-l2.12-003"></a>OS-L2.12-003 | Related choice persists | Conditional C11 positive decision/restart. |
| <a id="os-l2.12-004"></a>OS-L2.12-004 | Not Related/corrections persists | Conditional C11 plus original negative/corrections case. |
| <a id="os-l2.12-005"></a>OS-L2.12-005 | Use automatic persists | Conditional: original authority-clear/restart case. |
| <a id="os-l2.12-006"></a>OS-L2.12-006 | Pair/collection after rename/move | Conditional: original stable-ID authority after M05 operation. |
| <a id="os-l2.12-007"></a>OS-L2.12-007 | Content reindex preserves authority | Conditional: original inferred/authored separation. |
| <a id="os-l2.12-008"></a>OS-L2.12-008 | False positives/duplicate crowding | Conditional: original relationship-quality fixtures. |
| <a id="os-l2.12-009"></a>OS-L2.12-009 | Format-2 collection authority restore | Conditional C06 plus each original collection category. |
| <a id="os-l2.12-010"></a>OS-L2.12-010 | Format-1 compatibility | Conditional: original exact legacy archive import. |
| <a id="os-l2.12-011"></a>OS-L2.12-011 | Restore unresolved IDs | Conditional C06 plus original no-guessing case. |
| <a id="os-l2.12-012"></a>OS-L2.12-012 | Forget high-degree file, no ghosts | Conditional: original every-consumer absence checks. |
| <a id="os-l2.12-013"></a>OS-L2.12-013 | Forget source, no ghosts | Conditional: original every-affected-ID absence checks. |
| <a id="os-l2.12-014"></a>OS-L2.12-014 | Interrupted relationship-only refresh | Conditional: original resume/no-extraction replay case. |
| <a id="os-l2.12-015"></a>OS-L2.12-015 | Related while source remounts | Conditional: original missing-source reconciliation. |
| <a id="os-l2.12-016"></a>OS-L2.12-016 | Related long reasons/DPI/focus | Core M08; conditional original Related detail variations. |
| <a id="os-l2.12-017"></a>OS-L2.12-017 | Keyboard relationship decisions | Conditional C11 plus all three original actions. |
| <a id="os-l2.12-018"></a>OS-L2.12-018 | Screen-reader relationship semantics | Conditional C07 on original Related states/evidence. |
| <a id="os-l2.12-019"></a>OS-L2.12-019 | Search/Files stable-ID entry points | Conditional C11 plus original two entry points. |
| <a id="os-l2.12-020"></a>OS-L2.12-020 | OmniBrille bounded context expansion | Conditional: original installed companion/opaque-pair observation. |
| <a id="os-l2.12-021"></a>OS-L2.12-021 | Platform-sensitive identity | Conditional: original case/Unicode/missing-source/link cases per host. |
| <a id="os-l2.12-022"></a>OS-L2.12-022 | v2.12 version/commit agreement | Automated A-Package; conditional original About/diagnostics/archive manifest. |
| <a id="os-l2.12-023"></a>OS-L2.12-023 | Schema 6/protocol 1.0 | Automated A-Host; conditional original observed persisted/negotiated versions. |
| <a id="os-l2.12-024"></a>OS-L2.12-024 | Confidence never initiates mutation | Conditional: original relationship no-operation observation. |

## v2.13 addendum — 25 items

Source: [v2.13 full definitions](../MANUAL_TESTING_v2.13.md).

| Stable source ID | Original requirement | Disposition / retained destination |
| --- | --- | --- |
| <a id="os-l2.13-001"></a>OS-L2.13-001 | Fresh/existing Home primary path | Core M01; conditional original existing-profile first-view comparison. |
| <a id="os-l2.13-002"></a>OS-L2.13-002 | Scan → suggestions → review → Apply | Core M02/M04/M05; conditional C10/C02 for Smart Tags/AI stages. |
| <a id="os-l2.13-003"></a>OS-L2.13-003 | Secondary flows reachable/distinct | Core M03/M06/M07; conditional original Related/automation/graph navigation. |
| <a id="os-l2.13-004"></a>OS-L2.13-004 | Minimum size, 100/125/150/200% | Core M08, including all original scales/surfaces. |
| <a id="os-l2.13-005"></a>OS-L2.13-005 | Four Change Plan origins | Core M04; conditional original duplicate/recipe/AI rename/folder origin variants. |
| <a id="os-l2.13-006"></a>OS-L2.13-006 | Approve eligible without applying | Core M04; conditional original mixed Valid/Warning/blocking-conflict fixture. |
| <a id="os-l2.13-007"></a>OS-L2.13-007 | Reversible approvals/exclusions | Core M04. |
| <a id="os-l2.13-008"></a>OS-L2.13-008 | More than five duplicates/Apply/Undo | Conditional C01; retain per-group keeper and combined recovery. |
| <a id="os-l2.13-009"></a>OS-L2.13-009 | Separate five-item shell cap | Conditional: original Open file/Open folder boundary. |
| <a id="os-l2.13-010"></a>OS-L2.13-010 | Search resize/scroll/input | Core M03/M08; retain wheel, keyboard and scrollbar observations. |
| <a id="os-l2.13-011"></a>OS-L2.13-011 | Search expansion and saved searches | Conditional C12 covers saved-query reload; conditional original retains index-maintenance expansion and remaining focus behavior. |
| <a id="os-l2.13-012"></a>OS-L2.13-012 | Throughput/ETA/resource observations | Conditional C08, with original stage/resource evidence. |
| <a id="os-l2.13-013"></a>OS-L2.13-013 | Search readiness routes to Settings | Conditional: original disabled/unavailable and direct-link cases. |
| <a id="os-l2.13-014"></a>OS-L2.13-014 | Smart Tag lifecycle and Refresh | Conditional C10 plus original pending/insufficient/Refresh variants. |
| <a id="os-l2.13-015"></a>OS-L2.13-015 | AI disabled card/no request/link | Conditional C02 disabled branch. |
| <a id="os-l2.13-016"></a>OS-L2.13-016 | AI endpoints/proposals/errors/cancel | Conditional C02 plus original local/remote/failure/cancel/handoff cases. |
| <a id="os-l2.13-017"></a>OS-L2.13-017 | Six status meanings and announcements | Conditional C07; retain each original status, no color-only meaning. |
| <a id="os-l2.13-018"></a>OS-L2.13-018 | Related ordinary, graph derived | Conditional C11 plus original graph-presentation comparison. |
| <a id="os-l2.13-019"></a>OS-L2.13-019 | Exact relationship-data confirmations | Conditional C03 for merge; original unlink/split/forget/automatic variants remain separate. |
| <a id="os-l2.13-020"></a>OS-L2.13-020 | Cancel/confirm/restart each action | Conditional C03 for merge; original other actions require their own cancel/confirm/restart evidence. |
| <a id="os-l2.13-021"></a>OS-L2.13-021 | Repair/rebuild contextual, files unchanged | Conditional: original repair and rebuild observations. |
| <a id="os-l2.13-022"></a>OS-L2.13-022 | Full source/package/release identity | Automated A-Package; owner records tag/release identity; conditional original About/diagnostics agreement. |
| <a id="os-l2.13-023"></a>OS-L2.13-023 | Public download/hash/install/uninstall | Core M01 for launch; conditional original normal-user full lifecycle/preservation. |
| <a id="os-l2.13-024"></a>OS-L2.13-024 | Windows/macOS upgrade/trust prompts | Conditional C05/C09 per architecture; owner records unsigned/unnotarized claim. |
| <a id="os-l2.13-025"></a>OS-L2.13-025 | Schema/protocol/v2.12 state readable | Conditional C05 with exact v2.12 predecessor; automated A-Host for contract checks. |

## Earlier retained coverage

These tables are a catalog of independently selectable definitions, not
execution rows. An inclusive range names every source checkbox in that range;
the source section supplies its exact wording. **Conditional: original** keeps
each item independently open unless its original evidence explicitly says
otherwise. A checked harness/native-provider/package item is not desktop
usability evidence. See the relevant historical release draft for evidence
attribution and missing environment details.

The v2.10 matrix describes itself as deduplicating v2.5–v2.9. These links retain
the detailed acceptance cases behind that summary instead of assuming the
shorter master matrix silently completed them. Older implementation versions
are not thereby classified as published releases; the release index owns that
distinction.

### v1.1 — 34 source checkboxes

Source: [v1.1 definitions](../MANUAL_TESTING_v1.1.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L1.1-001–OS-L1.1-009 | Review Changes | Conditional: original, each item separate. |
| OS-L1.1-010–OS-L1.1-018 | Execution and journal | Conditional: original, each item separate. |
| OS-L1.1-019–OS-L1.1-025 | Undo | Conditional: original, each item separate. |
| OS-L1.1-026–OS-L1.1-029 | Interruption and errors | Conditional: original, each item separate. |
| OS-L1.1-030–OS-L1.1-034 | Regression | Conditional: original, each item separate. |

### v1.2 — 45 source checkboxes

Source: [v1.2 definitions](../MANUAL_TESTING_v1.2.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L1.2-001–OS-L1.2-009 | Configuration and persistence | Conditional: original, each item separate. |
| OS-L1.2-010–OS-L1.2-017 | Watcher hints, debounce, and stability | Conditional: original, each item separate. |
| OS-L1.2-018–OS-L1.2-026 | Incremental and offline reconciliation | Conditional: original, each item separate. |
| OS-L1.2-027–OS-L1.2-035 | Change Plans and self-generated events | Conditional: original, each item separate. |
| OS-L1.2-036–OS-L1.2-041 | Resource and presentation checks | Conditional: original, each item separate. |
| OS-L1.2-042–OS-L1.2-045 | Regression and completion | Conditional: original, each item separate. |

### v1.3 — 39 source checkboxes

Source: [v1.3 definitions](../MANUAL_TESTING_v1.3.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L1.3-001–OS-L1.3-003 | Release identity | Conditional: original, each item separate. |
| OS-L1.3-004–OS-L1.3-010 | Library and editor | Conditional: original, each item separate. |
| OS-L1.3-011–OS-L1.3-015 | Template safety | Conditional: original, each item separate. |
| OS-L1.3-016–OS-L1.3-022 | Manual and watched integration | Conditional: original, each item separate. |
| OS-L1.3-023–OS-L1.3-027 | Change Plan boundary | Conditional: original, each item separate. |
| OS-L1.3-028–OS-L1.3-032 | AI | Conditional: original, each item separate. |
| OS-L1.3-033–OS-L1.3-037 | Import, recovery, privacy | Conditional: original, each item separate. |
| OS-L1.3-038–OS-L1.3-039 | v1.2 regression | Conditional: original, each item separate. |

### v1.4 — 30 source checkboxes

Source: [v1.4 definitions](../MANUAL_TESTING_v1.4.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L1.4-001–OS-L1.4-004 | Release gate | Conditional: original, each item separate. |
| OS-L1.4-005–OS-L1.4-012 | Local package lifecycle | Conditional: original, each item separate. |
| OS-L1.4-013–OS-L1.4-016 | Adversarial packages | Conditional: original, each item separate. |
| OS-L1.4-017–OS-L1.4-020 | Runtime containment | Conditional: original, each item separate. |
| OS-L1.4-021–OS-L1.4-027 | Workflow and Change Plan safety | Conditional: original, each item separate. |
| OS-L1.4-028–OS-L1.4-030 | Inherited regression | Conditional: original, each item separate. |

### v1.5 — 23 source checkboxes

Source: [v1.5 definitions](../MANUAL_TESTING_v1.5.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L1.5-001–OS-L1.5-012 | Both supported targets | Conditional: original, each item separate. |
| OS-L1.5-013–OS-L1.5-015 | Windows | Conditional: original, each item separate. |
| OS-L1.5-016–OS-L1.5-023 | Linux preview | Conditional: original, each item separate. |

### v1.6 — 38 source checkboxes

Source: [v1.6 definitions](../MANUAL_TESTING_v1.6.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L1.6-001–OS-L1.6-005 | Windows, Linux, and macOS startup | Conditional: original, each item separate. |
| OS-L1.6-006–OS-L1.6-010 | Accessibility | Conditional: original, each item separate. |
| OS-L1.6-011–OS-L1.6-015 | Persistence and recovery | Conditional: original, each item separate. |
| OS-L1.6-016–OS-L1.6-019 | Scan, search, and cancellation | Conditional: original, each item separate. |
| OS-L1.6-020–OS-L1.6-024 | Watched folders | Conditional: original, each item separate. |
| OS-L1.6-025–OS-L1.6-029 | Change Plans, recovery, and Undo | Conditional: original, each item separate. |
| OS-L1.6-030–OS-L1.6-034 | AI, OCR, workflows, and plugins | Conditional: original, each item separate. |
| OS-L1.6-035–OS-L1.6-038 | Repository handoff | Conditional: original, each item separate. |

### v1.7 — 31 source checkboxes

Source: [v1.7 definitions](../MANUAL_TESTING_v1.7.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L1.7-001–OS-L1.7-004 | Search and accessibility | Conditional: original, each item separate. |
| OS-L1.7-005–OS-L1.7-009 | Initial indexing, progress, and persistence | Conditional: original, each item separate. |
| OS-L1.7-010–OS-L1.7-014 | Pause, cancellation, and interruption recovery | Conditional: original, each item separate. |
| OS-L1.7-015–OS-L1.7-018 | Dependencies and resource controls | Conditional: original, each item separate. |
| OS-L1.7-019–OS-L1.7-023 | Source lifecycle and incremental behaviour | Conditional: original, each item separate. |
| OS-L1.7-024–OS-L1.7-029 | Storage, failures, and diagnostics | Conditional: original, each item separate. |
| OS-L1.7-030–OS-L1.7-031 | Existing-feature regression smoke tests | Conditional: original, each item separate. |

### v1.8 — 48 source checkboxes

Source: [v1.8 definitions](../MANUAL_TESTING_v1.8.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L1.8-001–OS-L1.8-015 | Search quality and refinement | Conditional: original, each item separate. |
| OS-L1.8-016–OS-L1.8-019 | Accessibility and input | Conditional: original, each item separate. |
| OS-L1.8-020–OS-L1.8-029 | Indexed-data privacy | Conditional: original, each item separate. |
| OS-L1.8-030–OS-L1.8-039 | Repair, recovery, and concurrency | Conditional: original, each item separate. |
| OS-L1.8-040–OS-L1.8-045 | Security and diagnostics | Conditional: original, each item separate. |
| OS-L1.8-046–OS-L1.8-048 | Existing-feature regression | Conditional: original, each item separate. |

### v1.9 — 65 source checkboxes

Source: [v1.9 definitions](../MANUAL_TESTING_v1.9.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L1.9-001–OS-L1.9-006 | Installation, migration, and recovery | Conditional: original, each item separate. |
| OS-L1.9-007–OS-L1.9-017 | Relationship discovery and explanations | Conditional: original, each item separate. |
| OS-L1.9-018–OS-L1.9-027 | Smart Collections, context, and timeline | Conditional: original, each item separate. |
| OS-L1.9-028–OS-L1.9-036 | Related Files and manual control | Conditional: original, each item separate. |
| OS-L1.9-037–OS-L1.9-043 | Search integration | Conditional: original, each item separate. |
| OS-L1.9-044–OS-L1.9-053 | Privacy, forgetting, and repair | Conditional: original, each item separate. |
| OS-L1.9-054–OS-L1.9-060 | Dependency, performance, and resource behavior | Conditional: original, each item separate. |
| OS-L1.9-061–OS-L1.9-065 | Accessibility and regression | Conditional: original, each item separate. |

### v2.0 — 111 source checkboxes

Source: [v2.0 definitions](../MANUAL_TESTING_v2.0.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L2.0-001–OS-L2.0-007 | Installation, enablement, and compatibility | Conditional: original, each item separate. |
| OS-L2.0-008–OS-L2.0-018 | Lifecycle, interruption, and recovery | Conditional: original, each item separate. |
| OS-L2.0-019–OS-L2.0-031 | Graph quality, explanations, and control | Conditional: original, each item separate. |
| OS-L2.0-032–OS-L2.0-040 | Search and progressive coverage | Conditional: original, each item separate. |
| OS-L2.0-041–OS-L2.0-053 | File/index changes and selective repair | Conditional: original, each item separate. |
| OS-L2.0-054–OS-L2.0-071 | Privacy and original-file safety | Conditional: original, each item separate. |
| OS-L2.0-072–OS-L2.0-079 | Dependency and resource failure | Conditional: original, each item separate. |
| OS-L2.0-080–OS-L2.0-089 | Accessibility and responsiveness | Conditional: original, each item separate. |
| OS-L2.0-090–OS-L2.0-097 | Existing-feature smoke tests | Conditional: original, each item separate. |
| OS-L2.0-098–OS-L2.0-107 | Distribution and community-testing handoff | Conditional: original, each item separate. |
| OS-L2.0-108–OS-L2.0-111 | Completion record | Owner decision: evidence, defects, cleanup and completion policy. |

### v2.1 — 34 source checkboxes

Source: [v2.1 definitions](../MANUAL_TESTING_v2.1.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L2.1-001–OS-L2.1-007 | Search and results | Conditional: original, each item separate. |
| OS-L2.1-008–OS-L2.1-014 | Ollama and optional AI | Conditional: original, each item separate. |
| OS-L2.1-015–OS-L2.1-024 | Manual-validation fixes | Conditional: original, each item separate. |
| OS-L2.1-025–OS-L2.1-029 | Contextual Help and legacy workflows | Conditional: original, each item separate. |
| OS-L2.1-030–OS-L2.1-034 | Packaging and accessibility | Conditional: original, each item separate. |

### v2.2 — 67 source checkboxes

Source: [v2.2 definitions](../MANUAL_TESTING_v2.2.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L2.2-001–OS-L2.2-010 | Completed controlled native-host evidence | Conditional: original, each item separate. 8 historical checked item(s): retain source scope only. |
| OS-L2.2-011–OS-L2.2-017 | Settings and capability states | Conditional: original, each item separate. |
| OS-L2.2-018–OS-L2.2-026 | Images | Conditional: original, each item separate. |
| OS-L2.2-027–OS-L2.2-033 | Audio | Conditional: original, each item separate. 1 historical checked item(s): retain source scope only. |
| OS-L2.2-034–OS-L2.2-043 | Video | Conditional: original, each item separate. 2 historical checked item(s): retain source scope only. |
| OS-L2.2-044–OS-L2.2-052 | Search and Related Files | Conditional: original, each item separate. |
| OS-L2.2-053–OS-L2.2-062 | Privacy, diagnostics, and recovery | Conditional: original, each item separate. |
| OS-L2.2-063–OS-L2.2-067 | Cross-platform native follow-up | Conditional: original, each item separate. 1 historical checked item(s): retain source scope only. |

### v2.3 — 54 source checkboxes

Source: [v2.3 definitions](../MANUAL_TESTING_v2.3.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L2.3-001–OS-L2.3-007 | Automated evidence | Automated: linked source evidence, original commit/host only. |
| OS-L2.3-008–OS-L2.3-016 | Deterministic Content Intelligence | Conditional: original, each item separate. 4 historical checked item(s): retain source scope only. |
| OS-L2.3-017–OS-L2.3-026 | Local whisper.cpp provider | Conditional: original, each item separate. 6 historical checked item(s): retain source scope only. |
| OS-L2.3-027–OS-L2.3-029 | Native media-tool smoke | Conditional: original, each item separate. 2 historical checked item(s): retain source scope only. |
| OS-L2.3-030–OS-L2.3-032 | Visual understanding and semantic fallback | Conditional: original, each item separate. |
| OS-L2.3-033–OS-L2.3-036 | Cross-media Related Files | Conditional: original, each item separate. |
| OS-L2.3-037–OS-L2.3-042 | Schema 4 to 5 migration | Conditional: original, each item separate. 4 historical checked item(s): retain source scope only. |
| OS-L2.3-043–OS-L2.3-047 | Windows interactive desktop | Conditional: original, each item separate. |
| OS-L2.3-048–OS-L2.3-050 | Linux and macOS native validation | Conditional: original, each item separate. |
| OS-L2.3-051–OS-L2.3-054 | Safety and privacy | Conditional: original, each item separate. |

### v2.4 — 63 source checkboxes

Source: [v2.4 definitions](../MANUAL_TESTING_v2.4.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L2.4-001–OS-L2.4-017 | Automated and Windows-host evidence | Automated: linked source evidence, original commit/host only. |
| OS-L2.4-018–OS-L2.4-022 | Rename and desktop branding | Conditional: original, each item separate. 2 historical checked item(s): retain source scope only. |
| OS-L2.4-023–OS-L2.4-029 | Genuine v2.3 profile upgrade | Conditional: original, each item separate. 6 historical checked item(s): retain source scope only. |
| OS-L2.4-030–OS-L2.4-035 | Windows installer transition | Conditional: original, each item separate. 6 historical checked item(s): retain source scope only. |
| OS-L2.4-036–OS-L2.4-047 | Native protocol lifecycle and security | Conditional: original, each item separate. 11 historical checked item(s): retain source scope only. |
| OS-L2.4-048–OS-L2.4-054 | Search, Structure, and Context round trips | Conditional: original, each item separate. 5 historical checked item(s): retain source scope only. |
| OS-L2.4-055–OS-L2.4-063 | Native platform and packaging | Conditional: original, each item separate. 5 historical checked item(s): retain source scope only. |

### v2.5 — 59 source checkboxes

Source: [v2.5 definitions](../MANUAL_TESTING_v2.5.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L2.5-001–OS-L2.5-013 | Automated evidence | Automated: linked source evidence, original commit/host only. |
| OS-L2.5-014–OS-L2.5-020 | Controlled Change Plan workflow | Conditional: original, each item separate. |
| OS-L2.5-021–OS-L2.5-027 | Progressive indexing | Conditional: original, each item separate. |
| OS-L2.5-028–OS-L2.5-033 | Issue #29 — Virtual Collections scrolling | Conditional: original, each item separate. |
| OS-L2.5-034–OS-L2.5-039 | Issue #31 — Related Files scrolling | Conditional: original, each item separate. |
| OS-L2.5-040–OS-L2.5-049 | OmniBrille companion handoff | Conditional: original, each item separate. 6 historical checked item(s): retain source scope only. |
| OS-L2.5-050–OS-L2.5-055 | Accessibility and UX | Conditional: original, each item separate. |
| OS-L2.5-056–OS-L2.5-059 | Platform boundary | Conditional: original, each item separate. |

### v2.6 — 55 source checkboxes

Source: [v2.6 definitions](../MANUAL_TESTING_v2.6.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L2.6-001–OS-L2.6-007 | Automated evidence | Automated: linked source evidence, original commit/host only. |
| OS-L2.6-008–OS-L2.6-014 | Schema-5 profile upgrade | Conditional: original, each item separate. |
| OS-L2.6-015–OS-L2.6-022 | Classification quality | Conditional: original, each item separate. |
| OS-L2.6-023–OS-L2.6-029 | User authority | Conditional: original, each item separate. |
| OS-L2.6-030–OS-L2.6-036 | Search and filtering | Conditional: original, each item separate. |
| OS-L2.6-037–OS-L2.6-041 | Progressive indexing and recovery | Conditional: original, each item separate. |
| OS-L2.6-042–OS-L2.6-044 | Text extraction | Conditional: original, each item separate. |
| OS-L2.6-045–OS-L2.6-049 | Desktop accessibility and privacy | Conditional: original, each item separate. |
| OS-L2.6-050–OS-L2.6-055 | Performance and platforms | Conditional: original, each item separate. |

### v2.7 — 36 source checkboxes

Source: [v2.7 definitions](../MANUAL_TESTING_v2.7.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L2.7-001–OS-L2.7-007 | Automated release evidence | Automated: linked source evidence, original commit/host only. |
| OS-L2.7-008–OS-L2.7-012 | Complete-library retrieval | Conditional: original, each item separate. |
| OS-L2.7-013–OS-L2.7-018 | Faceted discovery | Conditional: original, each item separate. |
| OS-L2.7-019–OS-L2.7-022 | Saved Views and Smart Tag review | Conditional: original, each item separate. |
| OS-L2.7-023–OS-L2.7-026 | Native extraction | Conditional: original, each item separate. |
| OS-L2.7-027–OS-L2.7-030 | Windows desktop and accessibility | Conditional: original, each item separate. |
| OS-L2.7-031–OS-L2.7-036 | Privacy and platforms | Conditional: original, each item separate. |

### v2.8 — 38 source checkboxes

Source: [v2.8 definitions](../MANUAL_TESTING_v2.8.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L2.8-001–OS-L2.8-006 | Home | Conditional: original, each item separate. |
| OS-L2.8-007–OS-L2.8-012 | Find and Search to Files | Conditional: original, each item separate. |
| OS-L2.8-013–OS-L2.8-018 | Continuous Smart Tag review | Conditional: original, each item separate. |
| OS-L2.8-019–OS-L2.8-023 | Understand and Organize | Conditional: original, each item separate. |
| OS-L2.8-024–OS-L2.8-026 | Optional capabilities | Conditional: original, each item separate. |
| OS-L2.8-027–OS-L2.8-032 | Accessibility, DPI, and scrolling | Conditional: original, each item separate. |
| OS-L2.8-033–OS-L2.8-038 | Privacy, recovery, and platforms | Conditional: original, each item separate. |

### v2.9 — 36 source checkboxes

Source: [v2.9 definitions](../MANUAL_TESTING_v2.9.md).

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-L2.9-001–OS-L2.9-004 | Recipe lifecycle | Conditional: original, each item separate. |
| OS-L2.9-005–OS-L2.9-009 | Selection and discovery | Conditional: original, each item separate. |
| OS-L2.9-010–OS-L2.9-016 | Naming, destination, and evidence | Conditional: original, each item separate. |
| OS-L2.9-017–OS-L2.9-022 | Preview and safety | Conditional: original, each item separate. |
| OS-L2.9-023–OS-L2.9-027 | Change Plan, reconciliation, and Undo | Conditional: original, each item separate. |
| OS-L2.9-028–OS-L2.9-031 | Accessibility and layout | Conditional: original, each item separate. |
| OS-L2.9-032–OS-L2.9-036 | Platform and boundaries | Conditional: original, each item separate. |

## v1.0 and inherited v0.9.1 numbered instructions

The v1.0 list repeats printed numbers across sections. Use `OS-N1.0-NNN`
for numbered paragraphs counted once from top to bottom, `OS-B1.0-NNN` for
plain diagnostic bullet items counted from top to bottom, and `OS-T1.0-NNN`
for the 21 small-model table rows in order. These IDs prevent ambiguous
references to the repeated printed step 15. Keep each selected check in its
own issue row; do not treat these ranges as a compound test.

Source: [v1.0 definitions](../MANUAL_TESTING_v1.0.md): 182 numbered
paragraphs, 22 diagnostic bullets, and 21 model cases. All remain
conditional historical definitions; absent observations are Unknown / Not
recorded. These historical actions use v1.0 capabilities and wording, including
Meaning Search, legacy tags and restructuring; do not retrofit later recipes,
Smart Tags, graph or current Change Plan controls onto that release.

| Exact source ID range | Source section | Disposition |
| --- | --- | --- |
| OS-N1.0-001–OS-N1.0-008 | Release-candidate shell and Home | Conditional: original, each item separate. |
| OS-N1.0-009–OS-N1.0-033 | Files | Conditional: original, each item separate. |
| OS-N1.0-034–OS-N1.0-049 | Duplicates | Conditional: original, each item separate. |
| OS-N1.0-050–OS-N1.0-060 | Settings, navigation, and global toggles | Conditional: original, each item separate. |
| OS-N1.0-061–OS-N1.0-070 | OCR | Conditional: original, each item separate. |
| OS-N1.0-071–OS-N1.0-077 | Metadata | Conditional: original, each item separate. |
| OS-N1.0-078–OS-N1.0-084 | Tags | Conditional: original, each item separate. |
| OS-N1.0-085–OS-N1.0-100 | Meaning Search | Conditional: original, each item separate. |
| OS-N1.0-101–OS-N1.0-111 | Restructuring history | Conditional: original, each item separate. |
| OS-N1.0-112–OS-N1.0-123 | Structure History | Conditional: original, each item separate. |
| OS-N1.0-124–OS-N1.0-140 | Migration and regression | Conditional: original, each item separate. |
| OS-N1.0-141–OS-N1.0-159 | Final OCR and AI-text hardening | Conditional: original, each item separate. |
| OS-N1.0-160–OS-N1.0-163 | File Assistant reliability | Conditional: original, each item separate. |
| OS-N1.0-164–OS-N1.0-174 | Unified Advanced Diagnostics | Conditional: original, each item separate. |
| OS-N1.0-175–OS-N1.0-182 | Privacy, settings, and lifecycle | Conditional: original, each item separate. |
| OS-B1.0-001–OS-B1.0-006 | AI diagnostic flow | Conditional: original, each item separate. |
| OS-B1.0-007–OS-B1.0-011 | OCR and text-extraction diagnostic flow | Conditional: original, each item separate. |
| OS-B1.0-012–OS-B1.0-015 | Scanning diagnostic flow | Conditional: original, each item separate. |
| OS-B1.0-016–OS-B1.0-022 | Privacy, settings, and lifecycle | Conditional: original, each item separate. |
| OS-T1.0-001–OS-T1.0-007 | Small-model matrix, approximately 2B | Conditional: seven separate original model cases; exact installed model required. |
| OS-T1.0-008–OS-T1.0-014 | Small-model matrix, approximately 4B | Conditional: seven separate original model cases; exact installed model required. |
| OS-T1.0-015–OS-T1.0-021 | Small-model matrix, approximately 7B/8B | Conditional: seven separate original model cases; exact installed model required. |

Inherited [v0.9.1 definitions](../MANUAL_TESTING_v0.9.1.md) contain 55
numbered instructions: `OS-N0.9.1-001`–`OS-N0.9.1-055`, counted in source
order. Each remains a conditional original case, with the source section as
its action/acceptance reference. Setup and expected-result prose in both
documents still applies. Their historic completion/publication decisions are
owner decisions, not current permission to merge or publish.

## Evidence that remains missing

- The current 130 source items have no recorded manual pass in their source
  checklists. A smaller issue draft or linked CI result does not close them.
- v2.2/v2.3/v2.4 checked provider, migration, protocol and package observations
  retain their documented controlled scope. Broad desktop, screen-reader,
  provider/model and native-platform gaps stay open where the sources say so.
- The [v1.6 validation report](../V1.6_VALIDATION_REPORT.md) records maintainer
  interactive smoke at report scope. Its unchecked detailed checklist cannot
  be converted into 38 individual passes without matching observations.
- Where a historical observation lacks exact package/hash, person/date, host
  or item-level detail, retain that limitation. Later observation is a dated
  retrospective retest, never a backdated historical pass.
