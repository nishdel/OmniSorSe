# 034 — Desktop Integration, Product Polish, and Test Strategy

| Property | Value |
| --- | --- |
| Component | Avalonia integration and release validation |
| Target release | v0.3 |
| Status | Implemented |

## Purpose

This specification defines how optional AI assistance and ranked search integrate with the existing Avalonia MVVM workflow without moving business logic into views or weakening v0.1/v0.2 safety.

## ViewModel and UI impact

- `SettingsViewModel` owns an editable AI settings draft, connection test, model discovery, status feedback, and preference-history reset through application services.
- `ResultsViewModel` owns in-memory tags and forwards selected snapshot context to `AiSuggestionsViewModel`.
- `AiSuggestionsViewModel` owns request-running, suggestion/preview, editable review fields, decision commands, and disabled states. It has no HTTP, persistence, or filesystem code.
- `ResultsView` states the read-only boundary, shows tags and match explanations, exposes clear review buttons, shows running status, and labels folder structure as a preview.
- `MainWindow` has minimum dimensions of 960×640. Existing resize-friendly layouts, text trimming, scroll viewers, status surfaces, and duplicate navigation are retained.

Every new visible command works when its preconditions are met and is disabled otherwise. Controls do not look like file execution: acceptance records a decision, and all explanatory text states that no file or folder is changed.

## Accessibility and interaction requirements

- Text labels identify every primary action; ambiguous metadata is accompanied by descriptive copy or tooltip.
- Long paths, tags, explanations, and structure destinations wrap or trim rather than forcing layout overflow.
- Command availability follows selection, model, provider, and busy state.
- Status text distinguishes disabled, unavailable, no-model, model-selected, request-running, cancelled, invalid response, and preview-ready states.
- Results retain established selection/page behavior, duplicate review routing, cancellation semantics, empty states, warnings, and recoverable error presentation.

## Test strategy

| Area | Required coverage |
| --- | --- |
| Provider | Health, model discovery, no models, HTTP failures, malformed response, timeout, cancellation, and typed transport isolation with mock HTTP. |
| Suggestion safety | Valid rename, extension preservation, reserved names, traversal, absolute paths, conflict, tag normalization, category validation, destination validation, and folder-plan validation. |
| History | Accept/reject/edit aggregation, deterministic preference order, JSON round trip, malformed data, and reset. |
| Search | Tokens, filename/path/extension/category/tag matching, scoring, stable ties, filters, sorting, paging, empty/no-match state, and cancellation path. |
| Desktop | Existing Settings, Main, Results, Duplicate Review, scan progress, notifications, diagnostics, history, and new generated XAML compile validation. |

Automated tests use temporary paths only for application-owned JSON fixtures. They never invoke a real Ollama process or modify a selected user file. Manual acceptance is listed in the release proposal and README.

## Acceptance criteria

- OpenSorSe starts and scans normally without Ollama.
- Connection test and discovery communicate only with the configured endpoint and persist selected model only after Settings save.
- Selecting a result enables a suggestion request only when optional AI is ready; malformed/unsafe output never reaches an actionable preview.
- Accept, edit, and reject decisions are visible local actions and do not change the filesystem.
- Accepted tags appear in the current result session and can influence deterministic ranked search.
- Folder structure is always a bounded preview with no execution command.
- The complete solution builds with zero warnings and all automated tests pass.

## Risks and deferred work

Manual runtime UI testing remains recommended on a host with a local Ollama installation and an intentionally non-sensitive fixture folder. Native cross-platform accessibility inspection, persistent result/tag storage, and operation execution are outside this release.
