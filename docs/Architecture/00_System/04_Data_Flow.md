# OpenSorSe 1.1 Data Flow

```mermaid
flowchart LR
    Roots["Explicit selected roots"] --> Scan["Read-only scan"]
    Scan --> Results["Immutable Results"]
    Scan --> Extract["Bounded metadata extraction"]
    Extract --> Native["Native text/metadata"]
    Native --> Need{"OCR enabled and needed?"}
    Need -- Yes --> OCR["Local OCR Beta"]
    Need -- No --> Cache["Content cache"]
    OCR --> Cache
    Cache --> Tags["Provenance tags"]
    Tags --> Index["Local semantic index"]
    Index --> Search["Explained Semantic Search Beta"]
    Results --> Catalog["Optional saved catalog"]
    Results --> Duplicate["Duplicate drawer"]
    Results --> Suggestions["AI/rule suggestions (read-only)"]
    Suggestions --> Plan["Persisted Change Plan"]
    Plan --> Review["Review/edit/approve/reject"]
    Review --> Validate["Validate Plan"]
    Validate --> Confirm{"Explicit Apply Plan?"}
    Confirm -- No --> Plan
    Confirm -- Yes --> Guard["Immediate immutable revalidation"]
    Guard --> Journal["Persist pending/running journal"]
    Journal --> Apply["Rename/move/create through gateway"]
    Apply --> Verify["Verify + action journal"]
    Verify --> History["Operation History / Undo"]
```

## Ownership and lifetime

| Data | Lifetime |
| --- | --- |
| Scan entries, hashes, duplicates, rule plans, current Results filters | In memory for the current processing/results context. |
| Saved snapshots, accepted catalog tags, names/source scope | Optional bounded `catalog.json`. |
| Saved query definitions | Bounded `saved-catalog-searches.json`; hits remain in memory. |
| Extracted metadata/native/OCR text and generated tag state | Bounded local `content-index.json`, invalidated by source fingerprint. |
| Semantic terms/vectors | Bounded rebuildable `semantic-index.json`. |
| AI review decisions | Bounded metadata-only `decision-history.json`. |
| Structure source/proposed/applied snapshots and outcomes | Bounded `structure-history.json`; file contents are not stored. |
| Reviewable Change Plans | Bounded versioned `change-plans.json`; paths, portable identities, reasons, decisions, and validation only. |
| Attempted operations, rollback, interruption, and Undo | Durable bounded `operation-journal.json`; no file contents or AI prompt bodies. |
| Comparison rows, diagram filters, current structure capture | In memory and capped for presentation. |

## Failure isolation

Malformed documents, OCR unavailability, semantic-index corruption, AI provider failure, catalog I/O, and optional-store corruption return controlled states and do not break scanning or unrelated workflows. Only a validated, separately confirmed Change Plan can reach `IFileSystemGateway`; every AI generation/parsing flow is read-only with respect to source files.
