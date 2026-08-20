---
name: omnisorse-engineering-run
description: Run a substantial OmniSorSe discovery, implementation, audit, validation, or release task using the repository's evidence hierarchy, risk-based specialist routing, documentation synchronization, independent review, controlled learning, and project-owner report. Use when work crosses a subsystem boundary, changes durable state or user workflow, carries material regression risk, or the owner requests the standard OmniSorSe workflow. Do not use for a tiny read-only question or trivial isolated edit.
---

# OmniSorSe substantial engineering run

Use this checklist as an orchestrator. Load the linked detail only when relevant.

## 1. Protect and orient

Capture repository path, branch, HEAD, upstream/remotes, worktree including
staged/untracked files, active Git operations, relevant tags, SDK/runtime, and
pre-existing work. Do not mutate until the baseline is clear.

Read [Current State](../../../docs/CURRENT-STATE.md), then use the
[documentation router](../../../docs/README.md) to select the affected
subsystem. Source/tests outrank documentation when they disagree.

## 2. Classify and route

Use the [risk and validation matrix](../../../docs/engineering/RISK_VALIDATION_MATRIX.md).
Name touched authorities/contracts, consumers, failure paths, bounds, and likely
documentation impact. Invoke only specialists whose discipline can change the
answer. Send them durable source links, the verified baseline, and only the live
task delta; label inference and unverified assumptions. Do not make each
specialist repeat general archaeology or recursively delegate overlapping
discovery without orchestrator routing.

For large or uncertain work, finish bounded Product/Architecture/UX/AX/
Performance/Documentation discovery before implementation. Contradictory source
evidence returns to the orchestrator.

## 3. Implement and review

Resolve the objective, non-goals, authority decisions, validation plan, manual
gaps, and—for behavior changes—the existing public or observable test seam.
When a stable seam reproduces the behavior, capture one failing focused
regression before the minimal fix. Do not force tests without an independent
behavioral oracle. Then delegate or implement narrowly. Preserve existing
owners and compatibility; prefer executable regression memory.

Give the independent reviewer the objective, baseline, risk classification,
diff, and [authority map](../../../docs/engineering/ARCHITECTURE_AUTHORITY.md).
Require it to challenge fidelity to the objective/resolved plan and conformity
with repository authorities and standards, including non-success paths, stale
consumers, persistence, optionality, bounds, accessibility, documentation, and
validation claims.

## 4. Validate and synchronize

Run all applicable focused checks. Inspect the final diff and repository state.
Confirm no generated artifacts or unrelated product changes entered the patch.
Separate observed automated/native/manual/package/release evidence from checks
that were unavailable or reasoned about.

The Documentation specialist compares the final implementation with affected
current docs, diagrams, glossary, ADRs, contracts, and validation guidance. Ask
whether another competent developer could understand the result without the
conversation.

## 5. Learn without self-modification

Complete the [retrospective](../../../docs/engineering/templates/RETROSPECTIVE.md).
Every observation starts as a candidate. No agent promotes its own observation.
Use the independent evidence/promotion process in the
[learning system](../../../docs/engineering/LEARNING_SYSTEM.md), preferring a
regression or architecture test when appropriate. Historical run detail stays
out of routine context.

## 6. Report

Finish with the [project-owner report](../../../docs/engineering/templates/OWNER_REPORT.md).
State exact Git status and distinguish Verified, Derived, Remaining uncertainty,
and Recommended next action. Retain the report only when it improves future
auditability.
