---
name: Release manual testing
about: Record human observations for one release and its exact candidates
title: "Release manual testing — VERSION"
labels: ""
assignees: ""
---

Release/version and classification (stable/prerelease/unpublished): OWNER TO FILL

Candidate/package filename, source commit and artifact SHA-256 (or Unavailable with reason): OWNER TO FILL

Definition revision: OWNER TO FILL; pin the new-definition links below to the reviewed documentation commit.

Environment ID, OS/build/architecture, locale/filesystem, display scale/devices, dependencies and disposable profile/sample: OWNER TO FILL

Checklist owner: OWNER TO FILL; actual performers/dates belong in rows.

Summary — Passed: 0 / Failed: 0 / Blocked: 0 / Not tested: 8 / N/A: 0 / Not recorded: 0

Use this issue as the sole live results record. New tests start No / Not tested; historical missing evidence uses Unknown / Not recorded. Tested: No / Partially / Yes / N/A / Unknown (historical only). Result: Not tested / Pass / Fail / Blocked / N/A / Not recorded (historical only). Issue registered: — / Not needed / Pending / linked issue number. Pass requires all expected results observed and Tested=Yes. Fail needs Pending or a linked defect; Blocked/N/A need a reason; N/A is never a pass. Record the actual performer/date; an agent may only transcribe real observations. Search existing issues before creating a defect; closed defects still need retesting.

Before a retest preserve the previous attempt in an identity-labelled comment. Before changing package preserve the whole table, failures and evidence, record affected IDs, and initialize the new package's rows No / Not tested. Prior passes belong only to their original package. Separate environments use suffixed rows. Recount summary cells after every edit.

**Definition links:** these drafts currently link `blob/main` for new definitions. Before publishing the issue, replace those links with the reviewed documentation commit containing this change and check the anchors. Historical source links are already pinned to released commits. Documentation identity is separate from package identity. [Start testing here](https://github.com/nishdel/OmniSorSe/blob/main/docs/MANUAL_TESTING.md#release-testing-procedure-start-testing-here).

| ID | Test | Expected result | Tested | Result | Issue registered | Tester / date | Evidence / notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| [OS-M01](https://github.com/nishdel/OmniSorSe/blob/main/docs/MANUAL_TESTING.md#release-testing-procedure-os-m01) | Portable launch | Correct version opens; first scan is usable. | No | Not tested | — | — | — |
| [OS-M02](https://github.com/nishdel/OmniSorSe/blob/main/docs/MANUAL_TESTING.md#release-testing-procedure-os-m02) | Scan sample | Four files found; sources unchanged. | No | Not tested | — | — | — |
| [OS-M03](https://github.com/nishdel/OmniSorSe/blob/main/docs/MANUAL_TESTING.md#release-testing-procedure-os-m03) | Search filename | Known file found in sample. | No | Not tested | — | — | — |
| [OS-M04](https://github.com/nishdel/OmniSorSe/blob/main/docs/MANUAL_TESTING.md#release-testing-procedure-os-m04) | Review proposal | Approval alone changes no files. | No | Not tested | — | — | — |
| [OS-M05](https://github.com/nishdel/OmniSorSe/blob/main/docs/MANUAL_TESTING.md#release-testing-procedure-os-m05) | Apply and Undo | Reviewed copy moves; Undo restores it. | No | Not tested | — | — | — |
| [OS-M06](https://github.com/nishdel/OmniSorSe/blob/main/docs/MANUAL_TESTING.md#release-testing-procedure-os-m06) | Duplicate review | Identical pair grouped; files unchanged. | No | Not tested | — | — | — |
| [OS-M07](https://github.com/nishdel/OmniSorSe/blob/main/docs/MANUAL_TESTING.md#release-testing-procedure-os-m07) | Settings persistence | Saved value survives restart. | No | Not tested | — | — | — |
| [OS-M08](https://github.com/nishdel/OmniSorSe/blob/main/docs/MANUAL_TESTING.md#release-testing-procedure-os-m08) | Keyboard and layout | Focus and controls remain usable. | No | Not tested | — | — | — |

Historical releases: use their prepared draft and release-specific behavior; do not impose this current core on v1.0. Missing historical evidence is Unknown / Not recorded. A later retest is explicitly Retrospective.

Automated evidence (separate): retained release/source reports are references, not human passes. No builds, tests, package smoke or manual runs were executed to prepare this draft.

Owner decisions (separate): assign checklist owner and environment, select applicable conditional/legacy IDs from the coverage map, and set their acceptance criteria before execution. Unselected inherited safety/platform/upgrade/feature requirements remain unresolved. Signing, remote-provider permission and publication are separate decisions. Add selected cases as individual rows and update counts.

Candidate/attempt history: initial preparation only. Once posted, update the GitHub issue and its dated evidence comments; do not copy results back into this draft. A later test of an old release must say “Retrospective” and use its actual execution date.
