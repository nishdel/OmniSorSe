# Tag Provenance

**Version scope:** Introduced in v1. This is the canonical subject document;
version-specific statements below retain that scope. Consult [Current State](../../CURRENT-STATE.md)
and source/tests for subsequent authority, schema and runtime changes.
Exact prior text: [Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/Architecture/06_Search/08_v1_Tag_Provenance.md).

Tags are the shared classification layer for Results filtering, catalog search, Semantic Search Beta, folder suggestions, rules, and structure summaries.

## Model

Every tag records file identity, normalized/display values, source, state, confidence, creation/update time, source fingerprint, and bounded provenance. Sources include User, ExistingAccepted, Rule, EmbeddedMetadata, OCR, Semantic, FileType, Date, FolderContext, and AI.

States are Confirmed, Suggested, Rejected, and System. A low-confidence generated value never becomes Confirmed automatically. User-confirmed tags receive the highest search weight. Rejected generated tags are suppressed while their source fingerprint remains unchanged.

Existing `TagAssociation` records migrate in memory: user-approved values become confirmed User tags; deterministic extension values become System/FileType tags. Catalog compatibility is preserved.

```mermaid
stateDiagram-v2
    [*] --> Suggested: generated candidate
    [*] --> Confirmed: user-created/existing accepted
    [*] --> System: deterministic
    Suggested --> Confirmed: accept
    Suggested --> Rejected: reject
    Rejected --> Suggested: material source change/reset
    Confirmed --> Rejected: explicit removal/rejection
```
