# Review Changes

## Purpose

**Review Changes** is the only Desktop workflow that can request v1.1 organization execution. It presents a Change Plan, never an AI response or rule operation directly.

## Presentation

The page shows plan/root/status, approved/rejected/pending/invalid/conflict/warning counts, action-type and issue filters, and rows containing current path, proposed path, action type, suggestion source, reason, decision, validation, warning, and conflict details.

Users can:

- approve all currently safe actions;
- deselect all;
- approve or reject one action;
- edit a rename filename or action destination;
- validate the current plan;
- inspect rename/move/folder/excluded/warning/overwrite summary;
- explicitly confirm or cancel Apply;
- cancel at safe boundaries while Apply is running;
- inspect the result and request Undo;
- return to Files without mutation.

Approval or editing returns the plan to review and clears prior validation. Apply is disabled until at least one approved action is valid and no approved blocking conflict exists. A second confirmation is required after the final summary.

## MVVM boundary

`ChangePlanReviewViewModel` owns presentation, commands, progress, and state transitions. It calls `IChangePlanValidator`, `IChangePlanStore`, and `IChangePlanExecutionService`; it performs no raw filesystem operations. `MainViewModel` hosts the page and routes accepted suggestions into it. Global status and cancellation include review validation/apply/Undo.

## Failure behavior

Validation failures remain editable. Apply results expose the journal summary and verified Undo availability. Raw exceptions are not the only user message. Cancelling before execution changes no file; cancellation during execution is handled by the service at action boundaries and remains journalled.
