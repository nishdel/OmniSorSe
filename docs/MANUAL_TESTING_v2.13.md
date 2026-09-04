# OmniSorSe v2.13 Manual Validation Addendum

This checklist adds the Product Clarity & Workflow scenarios. The inherited
v2.10–v2.12 platform, provider, relationship, packaging, and upgrade gates
remain separate. Every item below stays unchecked until a maintainer performs
it on the claimed host.

## Primary workflow and hierarchy

- [ ] At first launch and with an existing profile, Home makes Scan → Review → Organize the obvious primary path.
- [ ] Complete a real Scan, inspect Smart Tag and optional AI suggestions, create a Change Plan, review it, and apply it only in a disposable folder.
- [ ] Verify Search, Duplicates, Related Files, library automation, graph diagnostics, and Settings remain reachable and have distinct roles.
- [ ] At minimum supported window size and 100%, 125%, 150%, and 200% scaling, verify cards, navigation, status banners, tables, and primary actions do not clip or overlap.

## Review and duplicates

- [ ] Open Change Plans from duplicates, recipes, AI rename, and AI folder suggestions; verify origin, purpose, warnings, and action counts are accurate.
- [ ] Verify Approve all eligible includes only Valid/Warning actions without blocking conflicts and does not apply the plan.
- [ ] Verify Exclude all and individual approvals remain reversible before Apply.
- [ ] Select more than five duplicate copies across groups, retain at least one keeper in each group, review the combined recovery plan, apply, and Undo in a disposable tree.
- [ ] Verify shell Open file/Open folder remains capped and explains the separate five-item limit.

## Search, indexing, and Smart Tags

- [ ] Resize Search through supported dimensions; results retain useful height and normal wheel, keyboard, and scrollbar behavior.
- [ ] Expand/collapse Refine this search, Saved searches, and index maintenance; focus remains stable and existing saved searches still load.
- [ ] Observe recent throughput and ETA during a mixed real-library index; compare displayed values with terminal completions over time and record CPU, disk, queue depth, extraction/OCR/media stages, and resource policy.
- [ ] Verify Search enablement and disabled/unavailable explanations route directly to the relevant Settings controls.
- [ ] Verify Smart Tags for indexed files, still-indexing files, files with insufficient evidence, accepted/rejected tags, restart persistence, and Refresh Smart Tags.

## AI and status presentation

- [ ] With AI disabled, the optional AI organization card remains visible, makes no provider request, and opens the correct Settings section.
- [ ] With local and non-loopback test endpoints, verify the privacy label, model availability, rename/folder proposal provenance, validation failures, cancellation, Keep proposal, Dismiss, and Change Plan handoff.
- [ ] Verify information, ready, warning, error, disabled, and unavailable statuses are distinguishable without color alone and announced appropriately by a screen reader.

## Relationships, collections, and graph diagnostics

- [ ] Use Related Files as the ordinary relationship flow and confirm Graph diagnostics reads as an advanced derived projection, not authority.
- [ ] For unlink, merge, split, forget file/source/collection, and Use automatic result, verify the exact target and consequence appear before service state changes.
- [ ] Cancel each relationship-data confirmation and verify no retained authority is changed; confirm each against disposable indexed state and restart to verify persistence.
- [ ] Verify repair/rebuild remains contextual and does not modify source files.

## Packaging and upgrade

- [ ] Confirm About, diagnostics, binaries, installer, portable build manifest, app bundle, SBOM, checksum manifest, tag, and release agree on `2.13.0-rc`, file version `2.13.0.0`, and one exact commit.
- [ ] Download packages from the public prerelease as a normal user, verify SHA-256, install/run/uninstall, and verify user-data preservation.
- [ ] Repeat normal-user launch/upgrade on supported Windows and macOS architectures; record SmartScreen/Gatekeeper behavior and do not claim publisher signing or notarization.
- [ ] Confirm schema remains 6, Explorer Protocol remains 1.0, and existing v2.12 profile/saved-search/relationship/tag state remains readable.
