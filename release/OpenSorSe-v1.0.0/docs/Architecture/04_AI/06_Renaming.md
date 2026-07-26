# Renaming

> This document defines the Renaming component, which is responsible for generating meaningful filename suggestions using Artificial Intelligence.

---

## Purpose

The Renaming component analyzes document information and generates descriptive filename suggestions.

Its primary purpose is to improve file organization by recommending clear, consistent, and meaningful filenames based on a document's content and metadata.

The Renaming component provides recommendations only. It does not rename files or modify the filesystem.

## v0.9.1 implementation status

Rename suggestions are disabled by default and require both the global AI switch and **Enable file rename suggestions**. A request covers exactly one known result identity and metadata-only context. The required JSON response must repeat that identity and return one safe filename, a bounded reason, and optional confidence between 0 and 1.

Validation preserves the original extension exactly and rejects paths, separators, traversal, control or portable-invalid characters, reserved names, empty/no-change results, overly long values, and known sibling conflicts. Unknown JSON properties are ignored for forward compatibility, but all required fields and safety rules remain strict. Markdown-fenced or partially valid output is not accepted. The user may review, edit, accept, or reject the proposal; these actions do not rename the file.

---

# Responsibilities

The Renaming component is responsible for:

* Generating filename suggestions.
* Producing descriptive names.
* Preserving important document context.
* Respecting configured naming conventions.
* Enriching document representations with rename suggestions.

---

# Scope

### In Scope

* Filename suggestions
* Naming conventions
* AI-generated recommendations
* Multiple filename alternatives
* Suggested file titles

### Out of Scope

The Renaming component is **not** responsible for:

* Renaming files
* Moving files
* Rule execution
* Filesystem operations
* Search indexing
* Database persistence

These responsibilities belong to downstream architectural components.

---

# Architectural Overview

The Renaming component enriches document representations with AI-generated filename suggestions.

```mermaid
flowchart LR

Document["Enriched Document"]

Renaming["Renaming"]

Suggested["Rename Suggestions"]

Document --> Renaming

Renaming --> Suggested
```

---

# Renaming Workflow

A typical renaming process consists of the following stages:

1. Receive an enriched document.
2. Analyze available document information.
3. Apply configured naming preferences.
4. Generate one or more filename suggestions.
5. Validate the generated filenames.
6. Attach the suggestions to the document representation.
7. Return the enriched document.

---

# Suggested Filename Characteristics

Generated filenames should strive to be:

* Descriptive.
* Concise.
* Human-readable.
* Consistent.
* Filesystem-safe.
* Free from unsupported characters.

Suggestions should communicate the document's purpose without unnecessary complexity.

---

# Naming Preferences

The architecture should support configurable naming preferences, including:

* Date formats
* Separator styles
* Filename length
* Inclusion of document type
* Inclusion of author or organization where appropriate
* User-defined naming templates

These preferences should guide filename generation while remaining independent of AI providers.

---

# Multiple Suggestions

The component may generate multiple alternative filenames.

Examples include:

| Suggestion Type | Description                                             |
| --------------- | ------------------------------------------------------- |
| Descriptive     | Clear human-readable filename.                          |
| Short           | Compact filename.                                       |
| Structured      | Organized according to a naming convention.             |
| Contextual      | Includes additional document context where appropriate. |

Providing alternatives allows users and automation rules to choose the most appropriate option.

---

# Design Principles

The Renaming component should remain:

* Advisory.
* Provider-independent.
* Read-only.
* Extensible.
* Deterministic where practical.

Filename suggestions should never modify the original document or filesystem directly.

---

# Error Handling

Renaming failures should be isolated to the affected document.

Examples include:

* AI inference failures.
* Invalid filename generation.
* Missing document context.
* Unsupported naming templates.

Failure to generate rename suggestions should not interrupt subsequent document processing.

---

# Future Considerations

The architecture should support future enhancements, including:

* User-defined naming templates.
* Organization-specific naming conventions.
* Multi-language filename generation.
* Confidence scoring.
* Bulk rename optimization.
* Plugin-defined naming providers.

These enhancements should preserve the component's primary responsibility of generating filename recommendations.

---

# Related Documents

* [AI Overview](00_Overview.md)
* [Summarization](05_Summarization.md)
* [Folder Suggestions](07_Folder_Suggestions.md)
* [Rules Overview](../07-Rules/00_Overview.md)
* [User Rules](../07-Rules/05_User_Rules.md)
