# File Discovery

> This document defines the File Discovery component, which is responsible for identifying files that are eligible for processing within the OpenSorSe pipeline.

---

## Purpose

The File Discovery component identifies files encountered during directory traversal and determines whether they should enter the OpenSorSe processing pipeline.

It acts as the bridge between filesystem traversal and document processing by filtering, validating, and forwarding eligible files to the next stage of the Scanner subsystem.

File Discovery does not analyze file contents or perform document interpretation.

---

# Responsibilities

The File Discovery component is responsible for:

* Identifying filesystem objects that represent files.
* Determining file eligibility.
* Filtering unsupported or excluded files.
* Creating file descriptors for downstream processing.
* Forwarding discovered files to the Metadata Reader.

---

# Scope

### In Scope

* File identification
* File eligibility
* File filtering
* File validation
* File descriptor creation

### Out of Scope

The File Discovery component is **not** responsible for:

* Reading file metadata
* Reading document contents
* File hashing
* Duplicate detection
* AI analysis
* Database operations

These responsibilities belong to other components.

---

# Architectural Overview

The File Discovery component receives filesystem entries from the Folder Scanner and forwards eligible files to the Metadata Reader.

```mermaid
flowchart LR

FolderScanner["Folder Scanner"]

FileDiscovery["File Discovery"]

MetadataReader["Metadata Reader"]

FolderScanner --> FileDiscovery

FileDiscovery --> MetadataReader
```

---

# Discovery Workflow

A typical discovery operation consists of the following stages:

1. Receive a filesystem entry.
2. Determine whether the entry represents a file.
3. Validate that the file is eligible for processing.
4. Apply configured inclusion and exclusion rules.
5. Create a file descriptor.
6. Forward the file to the Metadata Reader.

---

# File Eligibility

A file may be considered eligible for processing based on criteria such as:

* Supported file type.
* User configuration.
* Scan scope.
* Accessibility.
* Exclusion rules.

The exact eligibility rules are defined by application configuration and may evolve over time.

---

# File Descriptors

Once a file has been accepted, the File Discovery component creates a file descriptor.

A file descriptor represents the discovered file and provides the information required for subsequent processing stages.

The exact structure of a file descriptor is defined elsewhere within the architecture.

---

# Design Principles

The File Discovery component should remain:

* Fast
* Lightweight
* Deterministic
* Independent of document processing
* Independent of AI
* Easy to extend with additional filtering rules

Its responsibility ends once a valid file has been accepted into the processing pipeline.

---

# Error Handling

Discovery errors should be isolated to the affected file whenever possible.

Examples include:

* Invalid paths
* Inaccessible files
* Unsupported filesystem objects
* Files removed during scanning

Recoverable failures should not interrupt the overall scanning operation.

---

# Future Considerations

The architecture should support future enhancements, including:

* Custom file filters
* Plugin-defined discovery rules
* Advanced exclusion patterns
* Virtual filesystem support
* Network storage discovery

These enhancements should integrate with the existing discovery pipeline without changing its primary responsibility.

---

# Related Documents

* [Folder Scanner](01_Folder_Scanner.md)
* [Metadata Reader](03_Metdata_reader.md)
* [File Hashing](04_File_Hashing.md)
* [Scanner Overview](00_Overview.md)
