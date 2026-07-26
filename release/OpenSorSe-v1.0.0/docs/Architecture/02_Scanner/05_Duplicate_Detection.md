# Duplicate Detection

> This document defines the Duplicate Detection component, which is responsible for identifying duplicate files within the OpenSorSe processing pipeline.

---

## Purpose

The Duplicate Detection component analyzes discovered files to determine whether identical copies already exist within the scanned dataset.

By comparing file fingerprints and other relevant information, the component identifies duplicate files and records duplicate relationships for use by downstream subsystems.

Duplicate Detection identifies duplicates only. It does not delete, move, or otherwise modify files.

---

# Responsibilities

The Duplicate Detection component is responsible for:

* Identifying duplicate files.
* Comparing file fingerprints.
* Recording duplicate relationships.
* Marking duplicate status.
* Forwarding duplicate information to downstream components.

---

# Scope

### In Scope

* Duplicate identification
* Hash comparison
* Duplicate grouping
* Duplicate status assignment
* Duplicate relationship tracking

### Out of Scope

The Duplicate Detection component is **not** responsible for:

* Generating file hashes
* Deleting duplicate files
* Moving files
* User interaction
* Report generation
* Database persistence

These responsibilities belong to other architectural components.

---

# Architectural Overview

The Duplicate Detection component receives hashed file descriptors from the File Hashing component and determines whether duplicate relationships exist.

```mermaid
flowchart LR

FileHashing["File Hashing"]

DuplicateDetection["Duplicate Detection"]

Readers["Readers"]

FileHashing --> DuplicateDetection

DuplicateDetection --> Readers
```

---

# Detection Workflow

A typical duplicate detection operation consists of the following stages:

1. Receive a hashed file descriptor.
2. Compare the file fingerprint against previously processed files.
3. Determine whether duplicate relationships exist.
4. Record duplicate information.
5. Update the file descriptor.
6. Forward the file descriptor to the Readers subsystem.

---

# Duplicate Identification

Duplicate detection may consider information such as:

* File fingerprint
* File size
* Additional validation where required

The exact detection strategy may evolve as the application grows.

---

# Duplicate Status

Each processed file should receive a duplicate status.

Examples include:

| Status    | Description                               |
| --------- | ----------------------------------------- |
| Unique    | No duplicate detected.                    |
| Duplicate | One or more identical files detected.     |
| Unknown   | Duplicate status could not be determined. |

Additional statuses may be introduced as the detection strategy evolves.

---

# Design Principles

The Duplicate Detection component should remain:

* Accurate
* Deterministic
* Efficient
* Independent
* Non-destructive

Detection should never modify or remove files.

Its responsibility ends once duplicate information has been determined.

---

# Error Handling

Duplicate detection should continue whenever possible after encountering recoverable failures.

Examples include:

* Missing hash values
* Incomplete scan data
* Temporary database unavailability
* Interrupted scan operations

Recoverable failures should be isolated to the affected file whenever practical.

---

# Future Considerations

The architecture should support future enhancements, including:

* Near-duplicate detection
* Perceptual hashing
* Image similarity detection
* Audio similarity detection
* Duplicate confidence scoring
* Cross-library duplicate detection

These capabilities should extend the existing architecture while preserving the component's primary responsibility.

---

# Related Documents

* [File Hashing](04_File_Hashing.md)
* [Readers Overview](../03_Readers/00_Overview.md)
* [Duplicates Report](../09_Reports/03_Duplicates_Report.md)
* [Scanner Overview](00_Overview.md)
