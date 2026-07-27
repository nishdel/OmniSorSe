# OpenSorSe 1.1 User Flow

```mermaid
flowchart LR
    Launch --> Scan["Select and scan folders"]
    Scan --> Results["Review Results"]
    Results --> Duplicate["Open duplicate drawer"]
    Results --> Tags["Review/manage tags"]
    Results --> Catalog["Optionally save/search/compare snapshots"]
    Scan --> Content["Local metadata/OCR indexing"]
    Content --> Semantic["Build/search Semantic Search Beta"]
    Results --> Suggest["Generate/accept rename or folder suggestion"]
    Suggest --> Preview["Create Change Plan"]
    Preview --> Review["Review/edit/approve actions"]
    Review --> Validate["Validate Plan"]
    Validate --> Decision{"Apply exact reviewed plan?"}
    Decision -- No --> Dismiss["Dismiss; no source change"]
    Decision -- Yes --> Apply["Revalidate + journal + apply + verify"]
    Apply --> History["Operation History / result / Undo"]
```

## Primary actions

| Action | Effect |
| --- | --- |
| Scan/cancel | Reads selected roots asynchronously; never modifies them. |
| Results/search/filter/tags | Changes view state or OpenSorSe-owned tag metadata only. |
| Duplicate drawer/open | Reviews exact groups and asks the OS to open capped known paths; never deletes. |
| Catalog/search/comparison | Reads/writes bounded OpenSorSe-owned historical metadata only. |
| OCR/metadata | Locally reads supported files under bounds; source files remain unchanged. |
| Semantic build/search/clear | Writes or removes only the local rebuildable semantic index. |
| AI generate/review | Sends bounded metadata only after explicit enablement; accepting creates a non-mutating Change Plan. |
| Review Changes | Approves/rejects/edits actions, validates, shows final summary, and requires explicit Apply. |
| Structure preview/diagram/current capture | Reads metadata and writes preview history only. |
| Apply Plan | After separate confirmation and immediate revalidation, journals and verifies approved rename/move/create-directory actions without overwrite. |
| Operation History / Undo | Reads persistent attempts and performs only verified, conflict-safe inverse operations. |
| Clear structure history | Removes only OpenSorSe history; does not undo or modify files. |

Global AI and Advanced switches remain visible from every page. Disabling a switch hides affected pages and safely returns stale hidden navigation to Dashboard without resetting saved dependent values.
