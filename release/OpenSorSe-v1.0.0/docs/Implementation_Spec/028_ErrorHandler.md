# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 028 |
| Component | Error Handler |
| Project | OpenSorSe.Core |
| Version | 1.0 |
| Target Release | v0.1 |

---

# Purpose

Provide centralized handling of application exceptions and recoverable errors.

---

# Responsibilities

- Process exceptions.
- Determine severity.
- Log errors.
- Notify the UI.
- Decide whether execution may continue.

---

# Does NOT

- Execute business logic.
- Modify files.
- Retry operations automatically.

---

# Acceptance Criteria

- Recoverable errors continue safely.
- Fatal errors stop safely.
- Errors are logged.
- Notifications are generated.

---

# Dependencies

Depends on:

- Logging Service

Required by:

- All application components


graph TD

subgraph Core
A["001 Scanner"]
B["002 Metadata"]
C["003 Hasher"]
D["004 Classifier"]
E["005 Duplicate Detector"]
F["006 Rule Engine"]
G["007 Move Planner"]
H["008 Conflict Resolver"]
I["009 Move Executor"]
J["010 Undo Engine"]
end

subgraph Infrastructure
K["011 Logging"]
L["012 Configuration"]
end

subgraph UI
M["013 Main Window"]
N["014 Dashboard"]
O["015 Folder Selection"]
P["016 Scan Progress"]
Q["017 Results"]
R["018 Rule Editor"]
S["019 Settings"]
T["020 Log Viewer"]
U["021 Undo History"]
V["022 About"]
W["023 Notification Center"]
end

subgraph Application
X["024 Scan Orchestrator"]
Y["025 Session Manager"]
Z["026 Application Controller"]
AA["027 Event Bus"]
AB["028 Error Handler"]
end


graph TD

A[Vision]
--> B[README]

B --> C[Architecture]

C --> D[Implementation Specs]

D --> E[Coding Standards]

E --> F[Development]

F --> G[v0.1 Release]

---

# Autonomous v0.1 Decisions

The Phase 2 Core foundation already provides `IErrorHandler`, `ApplicationError`, severity classification, and `ErrorHandler`; no replacement is introduced. v0.1 callers report user-safe `ApplicationError` values with optional diagnostic exceptions. The handler maps declared severities to centralized logging and raises `ErrorReported` for UI adapters without directly depending on notification UI.

The error handler does not retry, decide business recovery, suppress caller exceptions, persist diagnostic history, expose raw exception data to users, or invoke destructive operations. Notification-center adaptation remains a later application-controller concern.
