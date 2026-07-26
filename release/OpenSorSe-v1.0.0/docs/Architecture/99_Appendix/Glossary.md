# Glossary

> This document defines the common terminology used throughout the OpenSorSe architecture and documentation.

---

# Purpose

The Glossary establishes a shared vocabulary for contributors, users, and plugin developers.

Its purpose is to ensure that important concepts are used consistently across the codebase, documentation, architecture, and user interface.

Unless explicitly stated otherwise, the definitions in this document should be considered authoritative.

---

# Core Terms

## Application

The complete OpenSorSe software system.

---

## Document

A file managed by OpenSorSe.

Examples include:

* PDF
* Word document
* Image
* Text file
* Spreadsheet

A document is the primary unit processed throughout the application.

---

## Document Library

The collection of documents currently managed by OpenSorSe.

---

## Metadata

Structured factual information describing a document.

Examples include:

* File size
* Author
* Creation date
* Page count
* MIME type

Metadata describes a document without interpreting it.

---

## Enrichment

Additional information generated after basic document processing.

Examples include:

* AI summaries
* AI classifications
* Embeddings
* Generated tags

Enrichments extend a document's knowledge without modifying the original file.

---

## Knowledge

The complete information known about a document, including metadata, enrichments, tags, relationships, and processing history.

Knowledge combines factual and generated information.

---

## Reader

A component responsible for extracting information from a specific document type.

Examples include:

* PDF Reader
* Image Reader
* Office Reader

Readers transform raw files into structured information.

---

## Scanner

The subsystem responsible for discovering documents and initiating processing.

---

## Rule

A user-defined automation consisting of conditions and actions.

Rules determine when automated behavior should occur.

---

## Condition

A logical expression evaluated by the Rules subsystem.

Conditions determine whether a rule should execute.

---

## Action

An operation performed after a rule's conditions evaluate successfully.

Examples include moving files, adding tags, or requesting AI processing.

---

## Tag

A descriptive label associated with one or more documents.

Tags may originate from users, AI, plugins, or the system.

---

## Index

A searchable data structure used to improve retrieval performance.

Indexes support search but are not the authoritative source of information.

---

## Plugin

An external extension that adds functionality to OpenSorSe through the supported Plugin API.

---

## Plugin API

The official public interface through which plugins interact with the application.

---

## Provider

A component that implements a particular service behind a common interface.

Examples include:

* AI providers
* OCR providers
* Embedding providers

---

## Report

A structured analysis or summary generated from application data.

Reports are read-only representations of existing information.

---

## Processing Pipeline

The sequence of stages through which a document moves after discovery.

Typical stages include:

1. Discovery
2. Reading
3. Metadata extraction
4. AI enrichment
5. Database storage
6. Indexing
7. Search availability
8. Rule evaluation

---

# Design Principles

The terminology defined in this glossary should remain:

* Consistent.
* Unambiguous.
* Architecture-independent.
* Stable.
* Easy to understand.

Changes to core terminology should be made carefully to preserve consistency throughout the project.

---

# Related Documents

All architectural documentation may reference definitions contained in this glossary.
