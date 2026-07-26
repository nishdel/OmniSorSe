# OpenSorSe 1.0 Migration Strategy

1. Load v0.9.1 settings with missing OCR, metadata, semantic, AI document-text, and history properties defaulted off or to conservative bounds.
2. Keep catalog schema 1 and 2 readers unchanged; do not require catalog deletion or eager rewrite.
3. Project existing accepted user/deterministic tags into the provenance-aware runtime model.
4. Preserve saved-search schema and query behavior.
5. Treat absent content cache, semantic index, or structure history as valid empty state.
6. Reject unsupported or corrupt optional content/index/history envelopes into a controlled empty or rebuildable state; never let corrupt optional data activate repeat protection.
7. Treat content records without the v2 extraction fingerprint/page provenance as stale but readable; reprocess them only when content indexing next runs.
8. Preserve user-created and accepted tags while regenerating source-derived candidates; preserve rejection suppression for the same source fingerprint.
9. Write new stores atomically under the existing OpenSorSe local application-data directory.

Migration never reads beyond explicitly selected source scope and never modifies a source file. Representative pre-v0.9.1 settings fixtures, missing-field defaults, catalog schema readers, tag/search compatibility, and corrupt optional stores are covered by tests. Structure history begins as a separate absent/empty schema-1 store and does not require an eager settings-schema migration.
