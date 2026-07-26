# OpenSorSe v1.0 Manual Testing Guide

This checklist validates the OpenSorSe 1.0 release candidate using disposable test data. Unless a step explicitly tests an apply operation, OpenSorSe must not rename, move, delete, overwrite, or modify source files. Record the application version, operating system, .NET runtime, test-data root, OCR capability state, and result for every section.

## Release-candidate shell and Home

1. Start with default settings and confirm the sidebar shows Home, Scan, Files, Duplicates, Saved scans, and Settings.
2. Confirm Advanced pages are absent, and Help/About appear in the sidebar footer.
3. Confirm Home shows a friendly first-scan state and one primary **Scan a folder** action.
4. Complete a scan and confirm the single Latest scan card shows files, folders, copies, and warnings without duplicate summary wording.
5. Navigate every primary page and confirm the bottom status bar remains visible.
6. Resize to the minimum supported window size and confirm navigation, cards, drawers, and status text remain usable.
7. Confirm the official OpenSorSe mark is crisp and unclipped in the window chrome/sidebar at 100%, 125%, 150%, and 200% scaling where available.
8. Confirm the sidebar reads **OpenSorSe**, **OPEN SORT AND SEARCH**, and **Find clarity in your files**, with comfortable separation from navigation and footer actions.

## Files

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

## Duplicates

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

## Settings, navigation, and global toggles

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

## OCR

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

## Metadata

52. Inspect PDF metadata.
53. Inspect DOCX metadata.
54. Inspect XLSX metadata.
55. Inspect image metadata.
56. Confirm provenance.
57. Test malformed files.
58. Confirm the scan continues.

## Tags

59. View user tags.
60. View generated tag suggestions.
61. Accept a tag.
62. Reject a tag.
63. Restart and verify persistence.
64. Confirm rejected tags do not immediately reappear.
65. Filter Results by tag.

## Meaning Search

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

## Restructuring history

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

## Structure History

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

## Migration and regression

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

## Final OCR and AI-text hardening

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

## File Assistant reliability

141. With AI off, select a file and confirm no File Assistant controls or Ollama communication occurs.
142. Enable AI and rename suggestions, leave the model empty, and confirm the action explains that a model must be selected.
143. Stop Ollama, select a file, choose **Retry connection**, and confirm the concise unavailable state.
144. Start Ollama, retry, and confirm server/model readiness refreshes without restarting OpenSorSe.

### Unified Advanced Diagnostics

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

#### AI diagnostic flow

- Run a successful rename request and confirm exact system prompt, user prompt, serialized Ollama request, endpoint/model, raw HTTP response, extracted assistant content, parsed response, validation detail, and timing remain separate.
- Exercise invalid structured output, Ollama unavailable, timeout, and cancellation. Confirm terminal states and warnings/errors are accurate and each later request remains usable.
- Run connection test and model discovery and confirm each has its own bounded session.
- Generate AI interpretation from indexed document text and confirm its AI session shows the related OCR/extraction session ID.
- Return one repairable malformed/schema-invalid response and then valid corrected JSON. Confirm exactly two related AI sessions retain the original and repair prompts/responses separately.
- Repeat with timeout, cancellation, unknown source ID, absolute/traversal path, invented metadata, oversized response, and a second invalid repair. Confirm timeout/cancellation/safety failures are not repaired and no operation makes more than two generation requests.

#### OCR and text-extraction diagnostic flow

- Test a native-text PDF, scanned PDF, mixed native/scanned PDF, image containing text, blank page, low-quality or rotated scan, and corrupt file.
- Confirm the native-text quality decision and OCR fallback reason are explicit; mixed PDFs preserve separate per-page native and OCR sources.
- Confirm raw native text, raw OCR text, normalized text, and text supplied downstream remain distinct and include character counts/truncation.
- Confirm engine/version/language, page count/status, render DPI/dimensions, preprocessing, duration, cancellation, warnings, errors, and partial success are accurate. Do not expect invented confidence.
- Confirm rendered-page previews state that they were not retained and temporary OCR images are removed.

#### Scanning diagnostic flow

- Test a normal folder, ordinary files unsupported by downstream extraction, an inaccessible folder, cancellation, a missing/changed file, a metadata failure, a reparse-point/junction, and a folder exceeding 500 discovered entries.
- Confirm accepted files remain accepted regardless of downstream extraction support; skipped entries state their actual reason.
- Confirm root/options, start/end, discovery, progress, counts, elapsed time, access/reparse decisions, cancellation, and metadata issues are visible.
- Confirm a large scan aggregates and reports omitted detail rather than growing without bound.

#### Privacy, settings, and lifecycle

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

## Small-model manual matrix

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

## Completion record

Manual testing is complete only when all failures are recorded with reproduction steps and either fixed or explicitly accepted as release limitations. Do not merge the v1.0 branch into `main` until both the inherited v0.9.1 checklist and this checklist have been completed.
