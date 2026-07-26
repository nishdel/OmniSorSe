# Folder Scanner

> This document defines the Folder Scanner component, which is responsible for traversing directory structures and coordinating file discovery operations.

---

## Purpose

The Folder Scanner is responsible for traversing one or more user-selected directories and coordinating the discovery of files within those locations.

It acts as the entry point of the Scanner subsystem by walking the filesystem, identifying directories to process, and forwarding discovered files to the File Discovery component.

The Folder Scanner focuses solely on directory traversal and scan orchestration. It does not interpret files or perform content analysis.

---

# Responsibilities

The Folder Scanner is responsible for:

* Starting scan operations.
* Traversing directory structures.
* Enumerating files and folders.
* Respecting scan boundaries.
* Coordinating recursive directory traversal.
* Forwarding discovered filesystem entries for further processing.

---

# Scope

### In Scope

* Directory traversal
* Recursive scanning
* Folder enumeration
* Scan coordination
* Traversal order

### Out of Scope

The Folder Scanner is **not** responsible for:

* File filtering
* Metadata extraction
* File hashing
* Duplicate detection
* Reading file contents
* AI analysis
* Search indexing

These responsibilities belong to other Scanner components or downstream subsystems.

---

# Architectural Overview

The Folder Scanner serves as the entry point for filesystem traversal.

```mermaid
flowchart LR

Folders["Selected Folders"]

FolderScanner["Folder Scanner"]

FileDiscovery["File Discovery"]

Folders --> FolderScanner

FolderScanner --> FileDiscovery
```

The Folder Scanner coordinates traversal while delegating subsequent processing to specialized components.

---

# Scan Lifecycle

A typical scan follows these stages:

1. Receive one or more scan locations.
2. Validate the scan locations.
3. Begin directory traversal.
4. Enumerate files and subdirectories.
5. Continue traversal until all reachable directories have been processed.
6. Signal scan completion.

---

# Traversal Principles

Directory traversal should follow these principles:

* Predictable behavior.
* Efficient recursion.
* Minimal memory usage.
* Support for large directory structures.
* Safe handling of inaccessible folders.
* Consistent processing order where practical.

The traversal strategy should remain independent of the underlying operating system whenever possible.

---

# Boundary Management

The Folder Scanner should respect user-defined scan boundaries.

Examples include:

* Selected root directories.
* Excluded folders.
* Hidden directories (where configured).
* System directories (where applicable).
* Symbolic links (according to application configuration).

The exact traversal rules are determined by application configuration.

---

# Error Handling

Filesystem traversal should continue whenever possible after encountering recoverable errors.

Examples include:

* Permission denied
* Missing directories
* Unavailable network locations
* Corrupted directory entries

Recoverable failures should be reported without terminating the entire scan operation.

---

# Design Principles

The Folder Scanner should remain:

* Lightweight
* Deterministic
* Platform independent
* Focused solely on directory traversal
* Independent of document processing

Maintaining a narrow responsibility simplifies testing and future maintenance.

---

# Related Documents

* [Scanner Overview](00_Overview.md)
* [File Discovery](02_File_Discovery.md)
* [Metadata Reader](03_Metdata_reader.md)
* [Progress Tracking](06_Progress_Tracking.md)
* [Cancellation](07_Cancellation.md)
