# OpenSorSe 1.3 Troubleshooting

## Profile unavailable — review configuration

Open **Workflows** and **Watched Folders**. The assigned profile or recipe may be missing, disabled, archived, incompatible, not permitted by the profile, or the legacy recipe may be `current`. Choose an active persistent replacement and save. OpenSorSe deliberately does not fall back.

The v1.2 profile ID `default` is the only compatibility alias; it maps explicitly to General Documents and records a warning.

## Profile or recipe will not save

Read the validation message. Common causes are duplicate name/ID, missing recipe dependency, invalid/duplicate file types, OCR without required metadata, an AI policy contradiction, unsupported enum/capability, destructive imported rule action, malformed template, or bounds exceeded.

## Recipe preview is invalid

Check required/fallback fields and the conflicts list. Destination templates must be relative, remain below the selected organization root, contain no `.`/`..` segment, and produce a safe unoccupied path. Filename/destination segments cannot be empty, reserved Windows device names, or over the configured length.

Sanitization is reported. A sanitized preview is deterministic, but it remains only a proposal.

## Workflow library recovery message

OpenSorSe keeps the original `workflow-library.json`, attempts a timestamped `.corrupt-…json` diagnostic copy, and loads canonical built-ins. Do not overwrite the original while investigating. Use **Export workflow diagnostics**, then import validated items from a known-good export or recreate user items.

## Import failed

Check the content type and schema, size/depth limits, duplicate conflict choice, dependency IDs, and templates. Import cannot replace a canonical built-in or execute code. Use **Import as copy** when an ID/name already exists.

## OCR or AI is off despite the profile

Profiles can request capabilities but cannot bypass application settings. Enable/configure the global capability separately. OCR also requires the local engine/language. AI requires the global switch, compatible capability, selected local model/provider availability, profile/local permission, and an eligible item.

## No Change Plan was generated

The profile may disable Change Plans or that action type; no recipe may be attached; the file may not satisfy applicability; required fields may be unresolved; the destination may equal the source, collide, escape the root, or be unsafe; or uncertainty policy may skip it. Preview the recipe and inspect scan/watched warnings.

## A historical scan differs from the current profile

This is expected. History stores the resolved profile/recipe revision and effective configuration snapshot used at that time. Editing a profile does not retroactively change its explanation.
