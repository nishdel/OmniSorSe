# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 012 |
| Component | Configuration Manager |
| Project | OpenSorSe.Core |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The Configuration Manager is responsible for loading, validating, providing, and saving application settings.

It acts as the single source of truth for configuration throughout the application.

---

# Why

Centralizing configuration ensures consistent behavior across all components and avoids duplicated configuration logic.

---

# Responsibilities

The Configuration Manager shall:

- Load application settings.
- Save application settings.
- Validate configuration values.
- Provide configuration to other components.
- Supply default values when necessary.
- Detect invalid or missing configuration.

---

# Does NOT

The Configuration Manager must NOT:

- Execute business logic.
- Move files.
- Read file contents.
- Rename files.
- Delete files.
- Apply organization rules.
- Perform filesystem scanning.

---

# Inputs

- Configuration file.
- User settings.
- Default configuration.

---

# Outputs

The component provides:

- Application configuration.
- Validation results.
- Configuration errors.

---

# Workflow

1. Load configuration.
2. Validate values.
3. Apply defaults where required.
4. Make configuration available.
5. Save changes when requested.

---

# Assumptions

- Configuration storage is accessible.
- Default configuration exists.

---

# Acceptance Criteria

The implementation is complete when:

- Configuration loads successfully.
- Invalid settings are detected.
- Default values are applied.
- Configuration can be saved.
- Unit tests pass.

---

# Future

Not part of v0.1:

- Multiple user profiles.
- Cloud synchronization.
- Encrypted settings.
- Import/export configuration.
- Live configuration reloading.

---

# Dependencies

Depends on:

- None

Required by:

- All configurable components

---

# Autonomous v0.1 Decisions

## Contract

`IConfigurationService` remains the public contract: `Current`, `InitializeAsync`, and `SaveAsync`. Settings are immutable-after-initialization object graphs for v0.1; user-edit APIs belong to Specification 019. JSON configuration is stored only at the absolute composition-root path.

Defaults are represented by a new `ApplicationSettings` instance. Load order is defaults, persisted JSON when present, then the existing `OPENSORSE_LOGGING__MINIMUMLEVEL` environment override. A malformed JSON document is reported as `ConfigurationValidationException` without exposing serializer internals. Missing configuration is normal.

## Persistence and safety

Saving creates only the configured application-settings parent directory. It writes a GUID-suffixed temporary file and atomically replaces the configuration file; this controlled overwrite is limited to the application's own configuration path. Cancellation is observed before file access and through asynchronous serialization. No user file, scan result, history, or database data is accessed.

## Errors, ordering, and tests

Invalid paths, invalid settings, invalid overrides, malformed JSON, and failed serialization are request failures; no partial `Current` replacement occurs. Tests cover defaults, precedence, malformed data, invalid settings, save/load round-trip, cancellation, temporary cleanup, and no mutation of unrelated files.
