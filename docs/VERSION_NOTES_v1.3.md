# OpenSorSe 1.3 Version Notes

Release name: **Workflow Profiles and Recipe Library**

Development branch: `v1.3-workflow-profiles`

Version metadata: product `1.3.0`, assembly/file `1.3.0.0`, About `1.3`

> Workflow profiles automate configuration and analysis, not approval or file modification.

## Highlights

- A durable, versioned workflow library replaces v1.2's runtime fallback with inspectable profiles and recipes.
- Five canonical profiles ship in source: General Documents, Invoices and Receipts, Photos, Downloads Cleanup, and Minimal Local Processing.
- Four canonical recipes demonstrate conservative document, invoice, photo, and download organization. Built-ins are visible and duplicable but never mutated in place.
- The **Workflows** destination supports search, capability/file-type/origin/archive filters, structured profile editing, recipe design and deterministic previews, lifecycle actions, usage information, import/export, and diagnostic export.
- Manual scans select a profile and may apply session-only constraints or save an adjusted copy.
- Watched folders persist one profile and zero or more permitted recipes. Missing, archived, disabled, or incompatible dependencies enter **Profile unavailable — review configuration** instead of silently using another profile.
- Resolved profile/recipe revisions and effective settings are stored with scan and watched-catalogue snapshots so later edits do not rewrite history.

## Safety

Templates are a field whitelist, not an expression language. Evaluation normalizes and sanitizes values, blocks rooted/traversing/out-of-root paths, reserved Windows names, overlong output, unresolved required fields, and occupied destinations. Imported recipes cannot contain executable code, destructive rule actions, or absolute move-rule destinations; organization moves come from the root-confined destination template.

Recipe output enters the existing v1.1 Change Plan factory. Profile/recipe revision, values, evidence, AI-assisted state, warnings, and unresolved fields are retained per proposal. Required directory proposals retain the same provenance. No workflow service calls the executor.

AI remains optional and must pass the global switch, configured model/capability, profile policy, watched/manual constraint, and item policy. AI-derived values are labeled and are never reparsed as template syntax.

## Compatibility

- The v1.2 profile ID `default` is explicitly mapped to General Documents with a migration warning.
- The session-only recipe ID `current` is not silently persisted. A watched folder using it becomes unavailable until a persistent recipe is chosen.
- Existing v1.1 Change Plans and journals retain schema 1; v1.3 provenance is an additive optional field.
- Existing saved scans without workflow snapshots remain readable.

## Known limitations

- Profile/recipe transfer is human-inspectable JSON through the Workflows text area; no online library or synchronization exists.
- Recipe fields are limited to the documented whitelist. Existing deterministic processing supplies names, extensions, filesystem timestamps, and category/document type; recipes do not run arbitrary expressions or scripts. The photo recipe uses filesystem creation time rather than EXIF capture time, and the invoice recipe visibly falls back to `UnknownVendor` when no reviewed vendor field is available.
- Built-in General Documents intentionally has no attached move/rename recipe, so explicit migration from v1.2 `default` cannot unexpectedly create organization proposals.
- Live OCR/provider availability still depends on separately installed/configured local components.
- Interactive GUI, live watcher, OCR, provider, packaging, and Windows permission matrices remain release-checklist work and are not replaced by automated tests.
