# Run retrospective — canonical documentation consolidation

**Date:** 2026-10-11  
**Task category:** Documentation and validation tooling only  
**Risk domains:** Historical evidence, current/readiness authority, link compatibility  
**Implementation/review basis:** main `783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f`  
**Author:** Codex lead; independent Documentation/adversarial reviewer

## Outcome

127 versioned or duplicate procedure paths consolidate into 55 canonical
subject destinations. The main index shrinks from 300 lines to 68. The complete
617-path migration register explains deletions, distinct living subjects and
archival retention. No application behavior or recorded test outcome changes.
See the [run report](../reports/2026-10-11-documentation-consolidation.md).

## Assumptions and rediscovery

- The supplied parent checkout was old and dirty; the actual OmniSorSe worktree
  was clean. Branching from fetched main preserved both existing branches/work.
- Versioned manual records are not uniformly empty templates: v1.6 has an
  attestation, v2.2–v2.7 mix different evidence classes, and v3 has 24 unrun rows.
- Thirty published absolute old links are pinned snapshots; nine relative links
  need correction. Neither case needs precautionary compatibility stubs.
- Original architecture/specification archives retain distinct planned intent;
  their existence is not current implementation evidence.

## Rework and specialist effectiveness

OmniLAB received inventory, grouping, repetition, reference and review packets.
Generic output and bounded-input/model failures required explicit Codex fallback.
A separate Codex documentation reviewer found duplicate changelog version
sections and two misleading automated/native ledger summaries; both were fixed.
The lead independently checked the findings, all source parity, status sequences,
links, inline path references and render structure. Runtime/config/models stayed
unchanged. Details and request IDs are in the report.

## Review and validation gaps

The existing link test checks paths, not fragments; a separate exhaustive
fragment check supplemented it. Shared scenario definitions preserve each
version's checkbox status and ordering. Local Markdown/Chromium rendering
verified tables and collapsible sections after remote rendering was rejected.
Ten inherited broken links remain in the unchanged frozen package, explicitly
listed in the report. Product/native/human suites were not run or claimed.

## Candidate lessons

| ID | Observation | Proposed follow-up | Status |
| --- | --- | --- | --- |
| C1 | Small-model review confused excerpt truncation with source truncation. | Use complete bounded sections and explicit review scope; independently validate every finding. | Candidate, not promoted |
| C2 | Per-version marker counts revealed automated/native summary overstatement despite exact body parity. | Pair mechanical content parity with independent semantic evidence review. | Candidate, not promoted |
| C3 | Clickable-link checks missed duplicated directories in inline code paths. | Consider a narrow repository-relative documentation-reference check if future examples justify it. | Candidate, not promoted |

## Independent lesson evaluation

No lesson was promoted by its author. The independent review approved this
change, not a new general rule. Candidate lessons remain for slow-loop review.

## Compact metrics and handoff

- Specialists: OmniLAB bounded analysis/review; Codex independent documentation review.
- Validation: 15 focused repository documentation tests passed; parity, paths,
  fragments, status sequences, script syntax, formatting and local layout passed.
- Durable output: canonical subject docs, migration register, explicit policy,
  lightweight existing-tooling guard and this evidence report.
- Remaining uncertainty: historical unrecorded environments and unrun human checks
  remain unrecorded/unrun. No release readiness was inferred.
- Follow-up: PR review and normal integration; no merge or release publication
  belongs to this task.
