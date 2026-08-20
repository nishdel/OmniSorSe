# ADR-004: Change Plan, Journal, and Reconciliation as the Mutation Boundary

| Field | Value |
| --- | --- |
| Status | Accepted in the current architecture; reconstructed from source and Git history |
| Effective history | Change Plan safety introduced in v1.1; reconciliation integrated in v2.5 |
| Reconstruction date | 2026-08-18 |
| Decision owners | OmniSorSe maintainers |

## Context

OmniSorSe proposes renames, moves, folder creation, duplicate recovery, watched-folder actions, and folder restructuring from several features. A successful filesystem operation is not sufficient evidence that the rest of the application is current: the operation may be cancelled, fail after earlier actions completed, be only partly rolled back, or be interrupted by process termination. Results, duplicate groups, Search, and durable indexing must reflect what the executor actually left on disk.

The current boundary was reconstructed from commit `5236f13` (v1.1 safe file operations), later hardening, commit `59be07c` (v2.5 reconciliation), current source, and regression tests. This ADR records the implemented authority model; it does not claim to reproduce every original design discussion.

## Decision

Production filesystem mutation is routed through one shared Change Plan boundary:

1. A feature creates a bounded plan and the user reviews exact actions.
2. `IChangePlanValidator` revalidates approved actions immediately before execution.
3. `IChangePlanExecutionService` durably records an operation before any filesystem mutation and records every later transition in the Operation Journal.
4. The executor verifies source and resulting identities, stops at action boundaries, and performs reverse-order rollback after cancellation or blocking failure.
5. Undo uses the same journal-aware service. It refuses to overwrite occupied paths, reverse a result that changed identity, or break a later OmniSorSe operation dependency.
6. For terminal Apply/Undo results published by Review Changes,
   `IChangePlanReconciliationService` combines verified journal outcomes with
   current filesystem existence to update the in-memory Results projection and
   identify affected paths.
7. `IBackgroundIndexingService.ReconcilePathsAsync` refreshes the durable
   indexed projection for those published paths. If immediate refresh fails,
   the mixed or stale projection is disclosed and a later scan/index pass
   remains the recovery path.

Authority is deliberately split:

- the Change Plan owns reviewed intent;
- the Operation Journal owns the durable record of attempted and completed actions, rollback, and Undo;
- the filesystem owns current path and file-identity truth;
- reconciliation derives UI and index refresh requests from those authorities.

Authoritative Change Plan or journal corruption blocks further mutation through `IRecoverySafetyState`. It is not treated as an empty store.

## Consequences

- Suggestions from rules, AI, duplicates, workflows, and restructuring cannot mutate files merely by being generated.
- Cancellation, rollback, interrupted startup recovery, and Undo are first-class states rather than variants of success.
- A task is not complete merely because filesystem calls returned successfully; reconciliation and its failure reporting are part of the operation lifecycle.
- That integration is not yet universal: Operation History Undo and startup
  interruption recovery do not forward returned journal records to
  `MainViewModel` reconciliation. They can leave Results/index projections stale
  until later scan/index reconciliation even though executor and journal truth
  are correct.
- The journal is bounded durable state: 500 retained operations and 128 MiB. Change Plans are bounded to 1,000 actions, 100 retained plans, and 64 MiB.
- Duplicate removal moves selected copies into the managed recovery area; it does not permanently delete them.
- The older standalone `IUndoEngine` and compatibility constructors using in-memory stores are not the production authority. Reusing them in production would require a new reviewed decision or removal of the ambiguity.

## Alternatives considered

The surviving source and history do not establish a reliable complete alternatives record. No rejected alternatives are reconstructed here.

## Evidence

- `src/OpenSorSe.Executor/ChangePlanExecutionService.cs`
- `src/OpenSorSe.Executor/ChangePlanValidator.cs`
- `src/OpenSorSe.Executor/JsonChangePlanStore.cs`
- `src/OpenSorSe.Executor/JsonOperationJournalStore.cs`
- `src/OpenSorSe.Application/ChangePlans/ChangePlanReconciliationService.cs`
- `src/OpenSorSe.Desktop/ViewModels/MainViewModel.cs`
- `src/OpenSorSe.Desktop/ViewModels/UndoHistoryViewModel.cs`
- `src/OpenSorSe.Desktop/App.axaml.cs`
- `tests/OpenSorSe.Executor.Tests/ChangePlanSafetyTests.cs`
- `tests/OpenSorSe.Application.Tests/ChangePlanReconciliationServiceTests.cs`
- [Change Plan architecture](../07-Rules/07_v1.1_Change_Plans_and_Operation_Journal.md)
