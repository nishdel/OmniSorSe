# OmniSorSe v2.9 — Reviewed Intelligent Organization

**Status:** implemented on `v2.9-reviewed-intelligent-organization` for maintainer review; not released

v2.9 connects the existing persistent `SortingRecipe` authority to Files,
Search, Saved Views, and the existing Change Plan safety boundary. The product
calls these definitions **Organization recipes**. It does not add a second
recipe store or an execution engine.

## User workflow

1. Select an explicit set of current indexed results in Files or Search. A
   Saved View contributes only its currently selected results; future matches
   are never included automatically.
2. Choose an existing Organization recipe and, if needed, edit its naming and
   destination patterns.
3. Preview trusted values, readiness, missing evidence, fallbacks, collisions,
   privacy implications, and the combined file/directory action budget.
4. Choose **Review Changes**. OmniSorSe re-resolves stable file IDs and repeats
   collision and action-budget checks before creating the existing Change Plan.
5. Review and explicitly approve the Change Plan. The existing journal,
   preflight, rollback, reconciliation, and Undo behavior remains authoritative.

Preview is ephemeral. It neither persists mutation intent nor changes files or
directories. A recipe edit, changed indexed identity/evidence, missing file, or
new filesystem target invalidates the prior preview.

## Selection and bounds

Selections contain stable indexed file IDs rather than paths. At most 1,000
files may be selected, and the final total of file actions plus deduplicated
directory-creation actions must also be at most 1,000. Nothing is silently
truncated. A Saved View remains a live discovery rule, not an automatic file
rule.

Search supplies durable IDs directly. The Files projection retains scan-local
row IDs for presentation, so its selected current paths are resolved to active
schema-6 identities in one bounded SQLite operation before preview. Any row
that no longer resolves blocks the handoff instead of substituting a path ID.

One reviewed proposal is restricted to one current registered indexed source.
Destination patterns remain relative to that explicit source root. Cross-root,
absolute, UNC, drive-switching, traversal, and token-generated root changes are
outside v2.9.

## Trusted modern tokens

| Token | Meaning |
| --- | --- |
| `{originalName}` | Current filename without its original extension. |
| `{theme}` | One accepted or uniquely usable Strong deterministic Theme. |
| `{documentType}` | One accepted or uniquely usable Strong deterministic Document Type. |
| `{filesystemCreatedDate:<format>}` | Explicit filesystem-created timestamp. |
| `{filesystemModifiedDate:<format>}` | Explicit filesystem-modified timestamp. |
| `{category}` | Coarse normalized file category. |

The token picker promotes only this closed set. Historical recipe fields remain
parser-compatible but are not presented as trusted modern organization facts.
In particular, generic date/capture aliases do not acquire new semantics;
entity and User Tag path tokens are not added.

An empty naming pattern preserves the current filename. An empty destination
pattern preserves the current containing folder. The original extension is
always preserved exactly in this workflow; recipes do not convert formats.

## Evidence and readiness

Accepted classifications reflect explicit user authority. Automatic
classifications are usable only when they are deterministic and Strong.
Moderate, Limited, and rejected classifications cannot resolve recipe tokens.
A singular token with multiple eligible values is ambiguous and cannot be
proposed.

- **Reliable:** every required value resolves from trusted evidence and no
  blocking warning or conflict exists.
- **Needs review:** an explicit configured fallback or nonblocking
  normalization warning was used.
- **Cannot propose:** a required value is missing/ambiguous/unsafe, the source
  is stale, the target conflicts, the scope is invalid, or action bounds fail.

Coverage such as `Document Type: 92 / 100` is a literal count, not confidence.
Evidence explanations are bounded to the token, resolved value, and authority
source. AI never fills a missing deterministic token.

## Safety and privacy

Every expanded value is untrusted path input. The established template and
Change Plan validation enforce portable invalid-character handling, reserved
names, root confinement, path limits, collision checks, symlink/reparse
restrictions, no overwrite, and immediate pre-execution validation. Preview
uses one proposal-wide normalized collision index and deduplicates inferred
directories.

Theme and Document Type values written into names can reveal sensitive meaning
outside OmniSorSe through shared folders, backups, sync, or attachments. The UI
shows a concise externalization warning. Normal diagnostics do not record raw
selected tags, expanded names, generated destinations, or evidence values.

## Architecture boundaries

- Recipe definitions remain in the bounded, atomic, versioned workflow-library
  JSON store. Schema remains 6.
- Organization proposals are ephemeral; Change Plans retain durable mutation
  intent.
- OCR, transcription, extraction, and classification are reused, never rerun
  to build a preview.
- Existing AI Rename and AI Folder Suggestion remain optional one-off proposal
  sources. They do not resolve recipe tokens or execute changes.
- Watched folders do not apply recipes automatically.
- Explorer Protocol remains read-only v1; OmniBrille is unchanged.
- No production dependency, cloud service, schema migration, or metadata
  writeback is introduced.

See [the v2.9 manual checklist](MANUAL_TESTING_v2.9.md) for the remaining
interactive and native-platform release gates.
