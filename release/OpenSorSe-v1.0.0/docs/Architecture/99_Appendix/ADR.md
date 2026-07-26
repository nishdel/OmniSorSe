# Architecture Decision Records (ADRs)

> This document defines the purpose and structure of Architecture Decision Records (ADRs) used throughout the OpenSorSe project.

---

# Purpose

Architecture Decision Records document significant architectural decisions made during the design and evolution of OpenSorSe.

Their purpose is to preserve the reasoning behind important decisions, making it easier for contributors to understand the project's direction and avoid revisiting previously resolved discussions without new evidence.

ADRs record decisions, not implementation details.

## Current records

| ADR | Status | Decision |
| --- | --- | --- |
| [ADR-001](ADR-001_Optional_Ollama_Suggestions.md) | Accepted for v0.3 | Keep Ollama behind a provider-neutral application boundary and make suggestions review-only. |
| [ADR-002](ADR-002_Bounded_Saved_Query_Persistence.md) | Accepted for v0.7 | Store bounded query presets in separate atomic JSON and never persist hits. |
| [ADR-003](ADR-003_Historical_Metadata_Comparison.md) | Accepted for v0.9 | Compare two bounded stored snapshots in memory without live filesystem verification or persisted reports. |

---

# Why ADRs?

As the project evolves, contributors may ask questions such as:

* Why was this technology selected?
* Why is the architecture organized this way?
* Why was an alternative approach rejected?
* Why does this subsystem exist?

Architecture Decision Records provide documented answers to these questions.

---

# When to Create an ADR

An ADR should be created whenever a significant architectural decision is made.

Examples include:

* Selecting a database technology.
* Choosing an application architecture.
* Defining a plugin model.
* Introducing a major subsystem.
* Adopting a new framework.
* Changing an existing architectural direction.

Minor implementation details generally do not require ADRs.

---

# Recommended ADR Structure

Each Architecture Decision Record should include:

| Section                 | Purpose                                        |
| ----------------------- | ---------------------------------------------- |
| Title                   | Short description of the decision.             |
| Status                  | Proposed, Accepted, Superseded, or Deprecated. |
| Context                 | The problem or motivation.                     |
| Decision                | The chosen solution.                           |
| Consequences            | Expected benefits and trade-offs.              |
| Alternatives Considered | Other options that were evaluated.             |

A consistent structure makes ADRs easier to review and maintain.

---

# Decision Principles

Architecture decisions should be:

* Well reasoned.
* Clearly documented.
* Evidence-based where practical.
* Consistent with project goals.
* Open to future revision when justified.

Every architectural decision involves trade-offs that should be acknowledged.

---

# Versioning

ADRs should be:

* Immutable once accepted.
* Superseded rather than rewritten.
* Version controlled alongside the source code.

Historical decisions remain valuable even after newer decisions replace them.

---

# Example Topics

Potential ADRs for OpenSorSe include:

* Adoption of MVVM.
* Local AI as the primary processing model.
* SQLite as the embedded database.
* Plugin-based extensibility.
* Separation of Readers and AI.
* Event-driven processing pipeline.

Additional ADRs should be added as the architecture evolves.

---

# Design Principles

Architecture Decision Records should remain:

* Concise.
* Informative.
* Stable.
* Easy to reference.
* Focused on reasoning.

Their purpose is to preserve architectural knowledge rather than implementation details.

---

# Future Considerations

The project may eventually support:

* ADR templates.
* ADR numbering conventions.
* Automated ADR validation.
* Decision dependency mapping.
* Community discussion links.

These enhancements should preserve the primary purpose of documenting architectural reasoning.

---

# Related Documents

* [Glossary](Glossary.md)
* [Coding Standards](Coding_Standards.md)
* [Naming Conventions](Naming_Conventions.md)
* [Technology Stack](Technology_Stack.md)
