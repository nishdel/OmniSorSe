# OpenSorSe 1.7 Manual Testing

Use disposable synthetic folders only. This checklist is not marked complete
until a maintainer records observed results in the validation report.

- [ ] Search is named Search in Files, page headings, controls, empty states,
  Help, accessibility output, and current user documentation.
- [ ] The Search help button works by hover, keyboard focus, activation, touch
  or click, and screen reader.
- [ ] Basic, Standard, and Deep settings save and reload.
- [ ] A disposable folder displays stage, current filename, all counts,
  coverage, speed, storage, and ETA only after sufficient work.
- [ ] Pause stops later claims; Resume completes them.
- [ ] Cancel safely stops active work and Retry continues without repeating
  completed stages.
- [ ] Close/restart during metadata, extraction, OCR/dependency wait, and index
  update; confirm no stale running state and completed work is reused.
- [ ] Disconnect/reconnect Ollama and Tesseract where explicitly enabled;
  confirm truthful waiting and recovery.
- [ ] Rename/move/copy/modify/delete disposable files and confirm incremental
  behavior without breaking watched-folder activity.
- [ ] Exercise quota maintenance and confirm no source files or important
  metadata are silently deleted.
- [ ] Search remains responsive and warns about partial coverage during work.
- [ ] Existing saved scans, watched folders, duplicates, workflows, plugins,
  Change Plan review/Apply, Operation History, recovery, and Undo still work.
- [ ] Review exported diagnostics before sharing and confirm no extracted text,
  OCR text, prompt body, token, or unnecessary absolute path is present.
