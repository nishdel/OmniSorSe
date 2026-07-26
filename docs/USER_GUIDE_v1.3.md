# OpenSorSe 1.3 User Guide

> Workflow profiles automate configuration and analysis, not approval or file modification.

## Workflows

Open **Workflows** from primary navigation. The first tab lists profiles; the second lists recipes; the third transfers versioned JSON and exports workflow diagnostics.

Search matches names and descriptions. Filters cover file type, built-in/user-created origin, AI, OCR, duplicate detection, and archived state. Usage text reports watched folders, recent saved scans, and profiles that reference a recipe.

Canonical built-ins are read-only. Choose **Duplicate** before editing one. User items can be renamed by changing the name and saving, enabled/disabled, archived/restored, exported, or deleted when unreferenced.

## Built-in profiles

- **General Documents:** balanced document metadata/text, duplicates, classification/rules, and policy-gated optional AI. No recipe is attached by default.
- **Invoices and Receipts:** PDF/image processing, OCR when necessary, duplicates, and invoice recipe proposals. When no reviewed vendor value is available, the built-in recipe uses the visible `UnknownVendor` fallback rather than guessing.
- **Photos:** metadata-first photo handling, filesystem-creation-date grouping, duplicates, no OCR or AI by default.
- **Downloads Cleanup:** conservative completed-download classification and category recipe; no deletion.
- **Minimal Local Processing:** metadata-only, no OCR/AI, and low-cost analysis.

## Profiles

The editor separates Files, Extraction, Classification/Duplicates, AI, Sorting recipes, watched behavior, notifications, uncertainty, and Change Plan options.

A profile selects zero or more persistent recipes. A watched folder may use only recipes permitted by its profile. Saving validates dependencies and contradictions and increments the profile revision.

Effective precedence, from broadest to narrowest, is:

1. application safety and capability settings;
2. canonical or user profile;
3. watched-folder supported constraints;
4. one-time manual-scan constraints;
5. ordered recipe and deterministic-rule selection.

Later levels may narrow global/profile capabilities; they cannot turn on OCR, AI, duplicate analysis, or Change Plans that a broader safety gate disabled.

Incremental behavior is effective at runtime: profiles may reanalyse changed items only or all current items, preserve or clear unchanged analysis, and choose whether a full watcher reconciliation removes missing catalogue entries. Disabling full or incremental scanning makes that profile unavailable for the corresponding scan mode instead of selecting another profile.

## Recipes and template syntax

Recipes define applicability, priority, a filename template, a relative destination template, fields/fallbacks, normalization, date format, length, collision, and uncertainty behavior.

Examples:

```text
{date:yyyy-MM-dd}_{vendor}_{documentType}_{amount}
Invoices/{date:yyyy}/{vendor}
```

Supported fields are `originalName`, `extension`, `date`, `createdDate`, `modifiedDate`, `vendor`, `documentType`, `title`, `author`, `invoiceNumber`, `amount`, `currency`, `project`, `category`, `captureYear`, `captureMonth`, and `ruleGenerated`.

Only date fields accept a date format. No function calls, operators, nested expressions, scripts, environment expansion, or shell syntax are supported.

Deterministic scan output currently supplies original name, extension, filesystem dates, and classification/category fields. Persistent recipe rules run in explicit priority order through the existing rule engine; they do not execute expressions or inject arbitrary code-backed fields.

Choose **Preview recipe** with representative metadata. The preview reports original path, filename/destination, values, missing/fallback fields, sanitization, conflicts, warnings, and AI-derived status. Preview never creates a directory or changes a file.

## Manual scans

On **Scan**, select a profile and review its file types, OCR, duplicate, AI, recipe, and processing summary. One-time settings can only narrow the saved profile. **Save as new profile** duplicates and persists the adjusted settings; it does not mutate the original.

The completed scan stores the effective revision snapshot. A valid recipe may create a Change Plan and open Review Changes. It does not apply it.

## Watched folders

Choose one active profile and zero or more displayed recipes. Save the configuration to trigger reconciliation. A profile edit triggers reconciliation with the new revision.

If a dependency becomes missing, archived, disabled, incompatible, or still uses v1.2's `current` recipe, processing stops in **Profile unavailable — review configuration**. Choose a deliberate replacement; OpenSorSe will not substitute an unrelated profile.

## Import and export

Export places versioned JSON in the transfer area. It includes type, application/schema version, item identity, description, configuration, and dependency IDs. It excludes provider settings, endpoints, credentials, document contents, logs, and secrets.

Import validates size/depth/schema/content type, IDs/names, capabilities, dependencies, and templates. Choose import as copy, confirmed replacement of a user item, skip, or cancel. Canonical built-ins are never overwritten.

## Change Plans and AI

Profile/recipe proposals use Review Changes, live preflight, explicit approval, Apply confirmation, the Operation Journal, recovery, rollback, Operation History, and Undo from v1.1.

AI is never implied by choosing a profile. Global AI, an installed selected model, the profile policy, local watched/manual permission, capability compatibility, and eligible item are all required. AI-derived fields remain visibly AI-assisted.
