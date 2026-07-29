# OpenSorSe 1.8 Version Notes

OpenSorSe 1.8 — Search Intelligence, Quality and Privacy builds on the v1.7
Deep Indexing Foundation.

- One deterministic hybrid pipeline ranks filename, folder/path, type,
  extension, tag, metadata, document/OCR text, summary, keyword, chunk, and
  optional related-concept signals.
- Exact filenames and literal evidence remain stronger than semantic-only
  similarity.
- Conservative local natural-language filters are visible, removable, and
  clearable; ordinary filtering does not require Ollama.
- Results provide a keyboard/click/touch accessible **Why this result?** view
  backed by actual ranking components and bounded retained-data snippets.
- Coverage now distinguishes exclusions, OCR/local-AI waits, failed stages, and
  temporary deep-index unavailability.
- Indexed-data inspection reports retained categories without showing raw
  vectors or complete document contents.
- Confirmed index-only forget, metadata-only, exclusion, clear, retry, file
  rebuild, and source rebuild actions preserve original files and source
  ownership.
- A confirmed clear-all action removes generated compatible/deep Search data
  while preserving source registration and original files.
- SQLite schema 2 adds transactional durable privacy/repair rules with a safe
  migration and pre-migration backup from v1.7 schema 1.
- Query, token, filter, fuzzy, candidate, snippet, malformed Unicode, generated
  field, concurrent request, and diagnostic privacy limits are explicit.
- A deterministic synthetic relevance framework reports top-result, top-k
  recall, reciprocal rank, exact-match preservation, and ordering stability.
- Search remains fully useful without Ollama and degrades to compatible
  filename/metadata coverage when deeper storage is recoverably unavailable.

This release is not a conversational file assistant, learned ranking engine,
cloud Search service, server database, recursive archive Search, or
source-file automation feature.
