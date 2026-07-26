# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 019 |
| Component | Settings |
| Project | OpenSorSe.Desktop |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The Settings view allows users to configure OpenSorSe's behavior and application preferences.

It provides a centralized location for managing all configurable options.

---

# Why

Users should be able to customize how OpenSorSe behaves without modifying configuration files manually.

The Settings view makes configuration accessible, consistent, and easy to understand.

---

# Responsibilities

The Settings view shall:

- Display current application settings.
- Allow users to modify settings.
- Validate user input.
- Save configuration changes.
- Restore default settings.
- Notify the user when changes require an application restart.

---

# Does NOT

The Settings view must NOT:

- Scan folders.
- Move files.
- Execute organization rules.
- Modify the filesystem.
- Calculate hashes.
- Detect duplicates.
- Execute AI analysis.

---

# Inputs

- Current application configuration.
- User interaction.

---

# Outputs

The component provides:

- Updated configuration.
- Validation results.
- Save requests.
- Restore default requests.

---

# Workflow

1. Load current settings.
2. Display configuration options.
3. User modifies settings.
4. Validate changes.
5. Save configuration.
6. Notify user if required.
7. Return to previous view.

---

# Assumptions

- Configuration Manager is available.
- Configuration can be saved.

---

# Acceptance Criteria

The implementation is complete when:

- Settings load successfully.
- Settings can be modified.
- Invalid values are detected.
- Changes are saved correctly.
- Default settings can be restored.
- UI tests pass.

---

# Layout

+------------------------------------------------------------+
| Settings                                                   |
+------------------------------------------------------------+

General

☐ Start on system startup

☐ Check for updates

☐ Remember recently scanned folders

------------------------------------------------------------

Scanning

☐ Scan subfolders

☐ Follow symbolic links

☐ Skip hidden files

------------------------------------------------------------

Organization

Default Destination:

[____________________]

Conflict Strategy:

( ) Skip

( ) Rename

( ) Ask

------------------------------------------------------------

[ Restore Defaults ]

[ Cancel ]

[ Save ]

------------------------------------------------------------

Status

Ready

---

# Future

Not part of v0.1:

- Theme selection.
- Language selection.
- Plugin configuration.
- AI provider configuration.
- Cloud synchronization.
- User profiles.

---

# Dependencies

Depends on:

- 012 - Configuration Manager

Required by:

- All configurable components.

---

# Autonomous v0.1 Decisions

The draft lists many settings that have no backing models or subsystems. v0.1 exposes only the implemented logging settings: minimum level, local-file enablement, optional absolute log directory, and retained daily-file count. `IConfigurationService` gains a compatible explicit replacement-save overload so a validated immutable `ApplicationSettings` object can be persisted without a UI writing files directly.

The editor holds an independent `SettingsDraft`, validates through `ApplicationSettings.Validate`, saves through configuration, and declares restart required because active logging services are initialized at application startup. The current configuration file is the only file that may be changed. Startup, updates, scanning preferences, conflict strategies, themes, languages, AI, plugin configuration, profiles, and live reconfiguration are deferred.

Each exposed setting has a permanent visible label and short user-facing explanation. In particular, **Daily diagnostic log files to retain** controls how many OpenSorSe application diagnostic log files are kept; it requires a whole number of at least one and never affects scanned user files. It is separate from the scanner and does not alter scan, metadata, duplicate, or results behaviour.
