# Naming Conventions

> This document defines the naming conventions used throughout the OpenSorSe project to promote consistency, readability, and maintainability.

---

# Purpose

The Naming Conventions document establishes consistent naming practices for source code, documentation, configuration, and project structure.

Its purpose is to ensure that contributors use clear, descriptive, and predictable names throughout the OpenSorSe codebase.

Consistent naming reduces ambiguity and improves collaboration.

---

# General Principles

Names should be:

* Descriptive.
* Consistent.
* Domain-oriented.
* Easy to pronounce.
* Easy to search.

Avoid abbreviations unless they are universally recognized.

---

# Classes

Class names should:

* Use PascalCase.
* Represent nouns or concepts.
* Describe a single responsibility.

Examples:

* `DocumentReader`
* `RuleEngine`
* `EmbeddingProvider`
* `SearchIndex`

Avoid vague names such as:

* `Helper`
* `Manager` (unless coordinating multiple components)
* `Processor` (unless it represents a well-defined processing stage)
* `Utils`

---

# Interfaces

Interfaces should:

* Use the project's chosen interface prefix (if applicable).
* Describe capabilities rather than implementations.

Examples:

* `IDocumentReader`
* `IAIProvider`
* `ISearchProvider`

Interface names should describe *what* they provide, not *how* they work.

---

# Methods

Method names should:

* Use PascalCase.
* Begin with a verb.
* Describe a single action.

Examples:

* `LoadDocument()`
* `GenerateSummary()`
* `FindDuplicates()`
* `BuildIndex()`

Methods should communicate intent clearly.

---

# Properties

Property names should:

* Use PascalCase.
* Represent nouns or characteristics.

Examples:

* `DocumentName`
* `CreatedDate`
* `ConfidenceScore`

---

# Variables

Variable names should:

* Be descriptive.
* Avoid unnecessary abbreviations.
* Reflect their purpose.

Examples:

* `document`
* `duplicateGroup`
* `classificationResult`

Avoid generic names such as:

* `temp`
* `obj`
* `data`
* `value`

unless their meaning is immediately obvious.

---

# Files

File names should:

* Match their primary class where practical.
* Use PascalCase.
* Reflect the contained responsibility.

Examples:

* `RuleEngine.cs`
* `DocumentReader.cs`
* `DuplicateDetector.cs`

---

# Folders

Folder names should:

* Represent architectural concepts.
* Use singular or plural consistently.
* Reflect project organization.

Examples:

* `Readers`
* `Search`
* `Plugins`
* `Reports`

---

# Events

Event names should describe what has happened.

Examples:

* `ScanCompleted`
* `DocumentIndexed`
* `RuleExecuted`

Event names should represent completed occurrences rather than commands.

---

# Constants

Constants should:

* Use descriptive names.
* Avoid unexplained literals.
* Clearly communicate purpose.

---

# Acronyms

Widely recognized acronyms may be used consistently.

Examples include:

* AI
* OCR
* API
* PDF
* JSON
* HTML

Avoid inventing project-specific abbreviations.

---

# Design Principles

Naming should remain:

* Predictable.
* Consistent.
* Stable.
* Easy to understand.
* Focused on the problem domain.

Names should communicate intent rather than implementation details.

---

# Future Considerations

As the project evolves, additional guidance may be introduced for:

* Generic type parameters.
* Database naming.
* Plugin identifiers.
* Localization resources.
* Configuration keys.

The primary objective should remain clear and consistent communication.

---

# Related Documents

* [Glossary](Glossary.md)
* [Coding Standards](Coding_Standards.md)
* [Architecture Decision Records](ADR.md)
