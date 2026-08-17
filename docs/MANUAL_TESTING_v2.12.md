# OmniSorSe v2.12 Manual Validation Addendum

This checklist adds only v2.12 relationship/context scenarios. All inherited
v2.10 and v2.11 release gates remain separate. Every item below is intentionally
unchecked until a maintainer performs it on the claimed host.

## Relationship quality and authority

- [ ] Review direct Related Files on a mixed real library with Knowledge Graph disabled.
- [ ] Repeat with Knowledge Graph enabled; direct results and corrections remain available if graph storage is disabled again.
- [ ] Mark a pair Related, restart, and confirm positive authority and explanation persist.
- [ ] Mark a pair Not Related, restart, confirm it is hidden from automatic results, then find it in corrections.
- [ ] Choose Use automatic result, restart, and confirm explicit authority remains cleared.
- [ ] Rename and move a file through the existing reviewed Change Plan; stable-ID pair and collection authority persists.
- [ ] Modify file content and reindex; inferred evidence refreshes while explicit authority persists.
- [ ] Review common-folder/common-topic and large duplicate-group cases for false positives and crowding.

## Backup and lifecycle

- [ ] Export format-2 `.oms-state`, restore collection rename/pin/manual membership/merge/split authority into a disposable profile.
- [ ] Import an exact format-1 `.oms-state` and confirm missing format-2 categories neither cause corruption nor clear existing Smart Collection authority.
- [ ] Restore with unresolved stable IDs and confirm no path/filename guessing occurs.
- [ ] Forget a high-degree file and confirm no Related, correction, collection, Search-context, graph-mirror, or Explorer ghost remains.
- [ ] Forget a source and repeat the ghost-reference check for every affected stable ID.
- [ ] Interrupt relationship-only refresh, restart, and confirm it resumes without rerunning extraction/OCR/transcription.
- [ ] Remove/remount a source while Related Files is open; state fails safely and refreshes after reconciliation.

## UX, accessibility, and companion

- [ ] At 100%, 125%, and 150% DPI, inspect long reasons, corrections, scrolling, selection, and async focus stability.
- [ ] Complete Related / Not Related / Use automatic using only the keyboard.
- [ ] With a screen reader, verify filenames, confidence bands, evidence class, authority state, and action names are announced without color-only meaning.
- [ ] Use Search and Files entry points and confirm the selected stable file opens in Related Files.
- [ ] With OmniBrille installed separately, request repeated bounded context expansion and verify one opaque target per pair.
- [ ] On claimed Windows/macOS/Linux support levels, exercise case, Unicode/NFC-NFD, missing-source, and symlink-sensitive identity scenarios.

## Release evidence

- [ ] About, diagnostics, `.oms-state` manifest, binaries, and local packages agree on 2.12.0 and the reviewed commit.
- [ ] Confirm schema remains 6 and Explorer Protocol reports 1.0.
- [ ] Confirm no automatic rename, move, delete, or Change Plan creation follows from relationship confidence.
