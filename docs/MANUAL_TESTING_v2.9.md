# OmniSorSe v2.9 manual testing

**Status:** checklist for maintainer sign-off; unchecked items are not claimed as passed

Use disposable indexed data. Confirm the source paths and target root before
approving any Change Plan.

## Recipe lifecycle

- [ ] Create, edit, save, close, and reopen an Organization recipe.
- [ ] Duplicate a built-in recipe; verify the built-in remains immutable.
- [ ] Archive/delete an unreferenced user recipe and confirm referenced recipes remain protected.
- [ ] Restart OmniSorSe and verify the atomic workflow library reloads.

## Selection and discovery

- [ ] Select files in Files, preview, close, and verify no filesystem change.
- [ ] Select Search results, preview, return, and verify query/facets remain intact.
- [ ] Open a Saved View, explicitly select current results, preview, then add a new matching file and verify it was not silently added to the selection.
- [ ] Exercise 10 and 100 selected files; verify the count remains explicit.
- [ ] Verify over-bound selection and combined file/directory actions are rejected without truncation.

## Naming, destination, and evidence

- [ ] Rename only; verify the original extension and its casing are preserved.
- [ ] Move only into an existing folder and into reviewed newly created nested folders.
- [ ] Combine rename and move without converting file formats.
- [ ] Exercise Unicode, invalid characters, reserved names, missing values, explicit fallbacks, and an invalid token.
- [ ] Verify Accepted and uniquely Strong deterministic Theme/Document Type resolve.
- [ ] Verify Moderate, Limited, rejected, ambiguous Theme, and missing Document Type do not resolve.
- [ ] Verify filesystem-created and filesystem-modified dates are labelled distinctly.

## Preview and safety

- [ ] Edit a recipe after preview and confirm **Review Changes** remains unavailable until re-preview.
- [ ] Filter preview by Reliable, Needs review, Cannot propose, conflicts, and missing evidence.
- [ ] Verify duplicate, case-only, normalization, existing-target, traversal, absolute, drive, UNC, outside-root, read-only-root, and path-length cases block safely.
- [ ] Move/delete a source or create its target after preview; verify stale preview requires refresh.
- [ ] Verify classification-derived naming shows the externalization warning.
- [ ] Inspect ordinary diagnostics and confirm raw tag values, generated paths, and evidence are absent.

## Change Plan, reconciliation, and Undo

- [ ] Choose **Review Changes** and verify no operation runs before approval and Apply confirmation.
- [ ] Execute rename-only, move-only, combined rename/move, and directory-creation plans.
- [ ] Verify Files, Search, index identity, details, and selection reconcile to filesystem truth.
- [ ] Exercise a safe partial-failure/rollback scenario and verify mixed state is reported truthfully.
- [ ] Undo and verify original paths/searchability return and created owned directories are cleaned only when safe.

## Accessibility and layout

- [ ] Complete the workflow using keyboard multi-selection and Tab/Shift+Tab.
- [ ] Verify recipe selector, token picker, patterns, readiness, current/proposed paths, conflicts, preview filters, re-preview, and Review Changes with a screen reader.
- [ ] Verify focus remains stable after preview/re-preview and asynchronous status updates.
- [ ] Test compact and normal windows, scrolling, and 100%, 125%, and 150% Windows display scaling.

## Platform and boundaries

- [ ] Perform native Windows execution and Undo.
- [ ] Perform native macOS execution, including case and Unicode-normalization collision scenarios, when supported.
- [ ] Perform Linux source-build runtime checks according to documented support.
- [ ] Confirm watched folders never auto-apply a recipe.
- [ ] Confirm closing OmniBrille and Explorer Protocol behavior are unaffected.

Automated Windows-host tests cover the deterministic contracts but do not
substitute for these desktop, DPI, screen-reader, permission, filesystem, or
native macOS/Linux checks.
