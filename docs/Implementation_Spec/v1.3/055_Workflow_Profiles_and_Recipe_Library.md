# 055 — Workflow Profiles and Recipe Library

Version: v1.3
Release: **Workflow Profiles and Recipe Library**
Branch: `v1.3-workflow-profiles`

> Workflow profiles automate configuration and analysis, not approval or file modification.

## Implemented boundary

v1.3 adds typed, versioned `WorkflowProfile` and `SortingRecipe` aggregates; canonical built-ins; an atomic bounded JSON library with migration/corruption recovery; validation; constrained templates; immutable resolution snapshots; watched/manual integration; Change Plan provenance; import/export; diagnostics; and Avalonia management/editor state.

The branch convention is `v<version>-<primary-feature>` (for example `v1.1-safe-file-operations`, `v1.2-watched-folders`, and `v1.3-workflow-profiles`).

## Components

- `BuiltInWorkflowLibrary`: canonical profiles/recipes, never persisted or edited.
- `JsonWorkflowLibraryStore`: schema-2 envelope, schema-1 migration, 8 MiB/depth bounds, atomic write-through sibling temporary file and replace, corrupt original preservation.
- `WorkflowLibraryService`: lifecycle, revision/timestamp ownership, dependency/usage checks, and bounded diagnostics.
- `WorkflowValidator`: identity, collection, enum/capability, dependency, contradiction, non-destructive rule, and template validation.
- `WorkflowTemplateEngine`: whitelist parser, deterministic field/date resolution, normalization/sanitization, root/collision/length/reserved-name enforcement, and explanation result.
- `WorkflowConfigurationResolver`: explicit compatibility alias, global → profile → folder/manual narrowing, capability gates, permitted ordered recipes, and historical snapshot.
- `WorkflowRecipePlanService`: applicability and explicit priority/stable-ID selection; existing Change Plan factory only.
- `WorkflowImportExportService`: bounded human-readable versioned JSON and explicit conflict policy.
- `WorkflowsViewModel`: presentation-only orchestration; parsing, persistence, resolution, and templates remain application services.

## Safety invariants

1. Recipes are data. No C#, PowerShell, shell, JavaScript, environment expansion, reflection, or arbitrary expression evaluation exists.
2. Generated paths must be below the approved organization root. No overwrite or automatic suffixing exists.
3. Missing/invalid profiles stop processing; only legacy `default` has an explicit named mapping.
4. AI is conjunctive with global and local gates, and AI values are labeled and never reparsed.
5. Proposals remain pending in the v1.1 Change Plan. Review, approval, preflight, explicit Apply, journal, recovery, rollback, history, and Undo are unchanged.
6. Historical settings are snapshots, not live references.

## Persistence and compatibility

Canonical built-ins are merged at read time. User items alone are stored. Meaningful edits increment revision and UTC modification time. Watched configuration schema 3 persists multiple recipe IDs and constrained overrides; watched catalogue schema 2 and saved scan snapshots retain workflow revisions.

Change Plan schema remains 1 because workflow provenance is an additive optional property. Existing records deserialize with null provenance.

## Explicit limitations

No unattended execution, deletion, overwrite, cloud sync/provider, marketplace/plugin execution, scripting, collaboration, distributed catalogue, or mobile client is introduced. Transfer uses a JSON text surface rather than an online library. Live external-component/platform verification remains manual.
