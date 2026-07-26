# Excel Reader

> This document defines the Excel Reader component, which is responsible for extracting content and metadata from Microsoft Excel workbooks.

---

## Purpose

The Excel Reader extracts structured information from Microsoft Excel workbooks and converts it into a normalized representation that can be processed by downstream subsystems.

The component is responsible for reading workbook contents, extracting worksheet data and document metadata, and preparing the extracted information for further processing.

The Excel Reader does not interpret spreadsheet data or perform calculations beyond those required for extraction.

---

# Responsibilities

The Excel Reader is responsible for:

* Reading supported spreadsheet files.
* Extracting workbook metadata.
* Extracting worksheet structure.
* Extracting cell values.
* Identifying tables.
* Identifying formulas.
* Identifying charts and embedded objects.
* Forwarding extracted information for further processing.

---

# Scope

### In Scope

* Workbook metadata
* Worksheet information
* Cell values
* Tables
* Formulas
* Named ranges
* Charts
* Embedded objects
* Workbook properties

### Out of Scope

The Excel Reader is **not** responsible for:

* AI analysis
* Spreadsheet interpretation
* Formula optimization
* Data visualization
* Search indexing
* Editing spreadsheet files

These responsibilities belong to downstream subsystems.

---

# Architectural Overview

The Excel Reader extracts structured information from spreadsheet documents before forwarding the resulting document representation for further processing.

```mermaid
flowchart LR

FileDescriptor["File Descriptor"]

ExcelReader["Excel Reader"]

DocumentRepresentation["Document Representation"]

FileDescriptor --> ExcelReader

ExcelReader --> DocumentRepresentation
```

---

# Processing Workflow

A typical Excel processing operation consists of the following stages:

1. Receive a file descriptor.
2. Verify that the file is a supported spreadsheet format.
3. Open the workbook.
4. Extract workbook metadata.
5. Read worksheet structure.
6. Extract cell values.
7. Extract tables, formulas, charts, and embedded objects where applicable.
8. Produce a normalized document representation.
9. Forward the document for further processing.

---

# Extracted Information

The Excel Reader may extract information including:

| Information       | Description                              |
| ----------------- | ---------------------------------------- |
| Workbook Title    | Workbook title where available.          |
| Author            | Workbook author information.             |
| Creation Date     | Workbook creation timestamp.             |
| Modification Date | Last modification timestamp.             |
| Worksheet Names   | Names of worksheets within the workbook. |
| Cell Values       | Values contained within worksheet cells. |
| Tables            | Structured table data.                   |
| Formulas          | Spreadsheet formulas where present.      |
| Named Ranges      | Defined workbook ranges.                 |
| Charts            | Information about embedded charts.       |
| Embedded Objects  | References to embedded content.          |

The exact information extracted depends on the workbook.

---

# Supported Formats

The architecture should support common spreadsheet formats, including:

* XLSX
* XLS
* XLSM
* CSV (where appropriate)

Support for additional spreadsheet formats may be introduced in the future.

---

# Design Principles

The Excel Reader should remain:

* Read-only.
* Deterministic.
* Format-specific.
* Independent of AI.
* Independent of indexing.
* Independent of business logic.

Its responsibility is limited to extracting spreadsheet information.

---

# Error Handling

The Excel Reader should handle workbook-related failures gracefully.

Examples include:

* Corrupted workbooks.
* Password-protected files.
* Unsupported workbook features.
* Invalid worksheet structures.
* Extraction failures.

Whenever practical, partially readable information should still be extracted.

---

# Future Considerations

The architecture should support future enhancements, including:

* Pivot table extraction.
* Conditional formatting extraction.
* Data validation rules.
* Workbook relationships.
* Macro detection.
* Improved chart extraction.

These enhancements should expand extraction capabilities while preserving the component's primary responsibility.

---

# Related Documents

* [Readers Overview](00_Overview.md)
* [Text Reader](04_Text.md)
* [Document Classification](../04_AI/04_Document_Classification.md)
* [Summarization](../04_AI/05_Summarization.md)
