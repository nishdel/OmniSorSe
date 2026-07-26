# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 018 |
| Component | Rule Editor |
| Project | OpenSorSe.Desktop |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The Rule Editor allows users to create, modify, enable, disable, and delete organization rules.

It provides a simple interface for customizing how OpenSorSe organizes files.

---

# Why

Every user organizes files differently.

The Rule Editor gives users full control over the organization process without modifying the application's code.

---

# Responsibilities

The Rule Editor shall:

- Display existing rules.
- Create new rules.
- Edit existing rules.
- Delete rules.
- Enable or disable rules.
- Validate rule configuration.
- Save rule changes.

---

# Does NOT

The Rule Editor must NOT:

- Execute rules.
- Scan folders.
- Move files.
- Modify the filesystem.
- Calculate hashes.
- Detect duplicates.
- Perform AI analysis.

---

# Inputs

- Existing rules.
- User interactions.
- Application configuration.

---

# Outputs

The component provides:

- Updated rule configuration.
- Validation results.
- Save requests.

---

# Workflow

1. Load existing rules.
2. Display rule list.
3. User creates or edits a rule.
4. Validate rule configuration.
5. Save changes.
6. Return to the previous view.

---

# Assumptions

- Configuration has been loaded.
- Rule storage is available.

---

# Acceptance Criteria

The implementation is complete when:

- Rules can be created.
- Rules can be edited.
- Rules can be deleted.
- Rules can be enabled or disabled.
- Invalid rules are detected.
- Rule changes are saved.
- UI tests pass.

---

# Layout

+------------------------------------------------------------+
| Rule Editor                                                |
+------------------------------------------------------------+

Current Rules

------------------------------------------------------------

✓ Move PDFs → Documents

✓ Move MP3 → Music

✓ Move JPG → Pictures

------------------------------------------------------------

Selected Rule

Name:

Move PDFs

Condition:

Extension == ".pdf"

Destination:

Documents/

------------------------------------------------------------

[ New ]

[ Save ]

[ Delete ]

[ Cancel ]

------------------------------------------------------------

Status

Ready

---

# Future

Not part of v0.1:

- Rule templates.
- Drag-and-drop rule builder.
- Natural language rules.
- AI-generated rules.
- Rule testing.
- Rule priority visualization.

---

# Dependencies

Depends on:

- 006 - Rule Engine
- 012 - Configuration Manager

Required by:

- None

---

# Autonomous v0.1 Decisions

The draft does not define rule persistence, an editable field-level draft model, or a save target. v0.1 edits immutable existing `FileRule` values in an ordered in-memory collection. `AddOrUpdate` creates or replaces a whole rule; validation covers the deterministic Rule Engine's supported action and condition shapes. A save operation emits a read-only snapshot event only, because no rule repository exists in the approved project set.

The editor never evaluates a rule, resolves a path, persists a change, or invokes file execution. A detailed rule-builder form, priority visualization, templates, import/export, storage, and configuration integration remain deferred.

When no in-memory rules exist, the v0.1 page explicitly says so and explains that rules are optional and cannot change files in this release. The selected-rule pane and its actions are hidden until a rule is selected. The shell does not display an inert Save control because v0.1 has no rule repository or shell save handler.
