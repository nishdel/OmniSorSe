# OpenSorSe 1.8 User Guide

OpenSorSe 1.8 keeps the v1.7 durable background index and makes Search easier to
refine, explain, measure, and control. It remains local and useful without
Ollama.

## What Search uses

Search can use filenames, folder names, paths, extensions, file type, metadata,
accepted tags, extracted document text, OCR text, summaries, generated
keywords, selected bounded text, and related concepts. Exact filename and
literal text evidence remain stronger than related-concept similarity.

The `?` help control beside Search works through hover where supported,
keyboard focus/activation, and click or touch. It has a screen-reader name and
help text.

## Natural-language filters

Queries such as these are interpreted locally:

- `PDF invoices from 2026 mentioning Mercedes`
- `large videos modified this month`
- `documents tagged tax`
- `files in the Raspberry Pi folder about monitoring`
- `metadata only household records`

Recognized filters appear below the query. Select a filter chip to remove it,
or use **Clear all filters**. Uncertain phrases remain topic words. Changing the
query clears the previous filter set.

Search accepts bounded file types, explicit `extension:`, `tag:`, and `source:`
forms, named folders, sizes, dates/months/years, indexing level/completeness,
OCR or related-concept availability, and failed indexing. A query is limited
to 512 characters, 32 topic terms, and 16 filters.

## Results, snippets, and explanations

A result may include a short snippet from retained index data. Its label says
whether the source was a filename, path, metadata, document text, OCR, summary,
tag/keyword, or selected text. A snippet is at most 240 characters and Search
does not reopen or re-extract the file to create it.

Open **Why this result?** to see only reasons that actually affected ranking,
such as an exact filename, folder, tag, document/OCR text, or related-concept
match. Ordinary users see reasons, not internal scoring mathematics or numeric
vectors.

## Progressive coverage

Search remains available during active, paused, cancelled, recovering, or
partially failed indexing. Coverage reports names/metadata, document text, OCR,
related concepts, and fully indexed files. Messages also identify exclusions,
OCR or local-AI dependency waits, failed stages, or temporary index
unavailability.

An empty result is not described as exhaustive while material coverage is
missing. Filename and metadata Search can continue when deeper data is
unavailable.

## Inspect and forget indexed data

Choose **Inspect indexed data** on a result to review:

- indexing level and last indexed date;
- metadata size;
- extracted/OCR character counts;
- whether a summary or related-concept data exists;
- keyword, selected-chunk, failure, and stage-history counts;
- source ownership and per-file privacy policy.

OpenSorSe does not show raw related-concept vectors or complete document text in
this panel.

Index-only actions can clear OCR data, clear related-concept data, use
metadata-only indexing, exclude future deep indexing, retry a failed stage, or
re-index a file. Forgetting a file or source requires confirmation. Every
action states that original files are unaffected.

Because summaries, keywords, selected text, and related-concept data can be
derived from OCR, clearing OCR also clears and suppresses those dependent
representations. This avoids retaining an indirect copy of text the user chose
to clear. An explicit selective repair can generate permitted data again.

Forgetting a watched source preserves watched-folder ownership. Forgetting
derived data stores a minimal suppression/exclusion rule so the same data is
not immediately generated again. Clearing shared duplicate-content data
reports how many records were affected.

## Selective repair

Use record verification, metadata refresh, document-text refresh, OCR refresh,
summary/keyword regeneration, related-concept regeneration, file
re-index/retry, or **Rebuild selected source** for isolated problems. The
selected repair invalidates only the requested stage and its dependants, then
uses normal persistent progress, pause, cancellation, retry, and restart
recovery. The repair description identifies optional OCR/local-AI dependencies
and confirms the source file remains untouched. A full database rebuild is not
required for one inconsistent record.

## Privacy settings

Background Indexing settings allow metadata-only Basic indexing, OCR off,
local-AI enrichment off, generated summaries/keywords off, related-concept
data/chunks off, generated-folder exclusions, binary/executable metadata-only
defaults, retention, quota, and source exclusions. The index can contain
searchable representations of selected document contents; protect application
data like the source documents.

**Clear generated Search data** requires confirmation, clears the compatible
Search store, and forgets generated data for every registered deep-index source.
The source registrations and original files remain unchanged. Durable
exclusions prevent immediate regeneration until the user explicitly repairs or
re-indexes the affected source.

The SQLite index is not advertised as encrypted. OpenSorSe does not implement a
custom encryption scheme. Operating-system account, disk, and backup
protections remain the supported at-rest controls.

## AI-optional behavior

Filename, folder, metadata, exact text, OCR, filters, date parsing, ranking,
snippets, and explanations are deterministic local features. Ollama is
optional. If enabled, local AI may improve indexing summaries/keywords, but its
absence does not block ordinary Search. OpenSorSe does not silently transmit
queries or document data to a remote service.

## Known limitations

- The grammar is deliberately conservative and is not conversational Search.
- Typo tolerance is filename-oriented, bounded, and language-neutral; it is not
  a full multilingual spell checker.
- Related-concept quality depends on retained local representations and
  coverage; it is not a statement of meaning or certainty.
- Inspection reports the embedded provider and processor contract version.
  v1.8 does not persist a per-file Ollama model identity for older generated
  rows, so it does not invent one in the inspection panel.
- v1.8 does not recursively Search archive contents beyond data supplied by
  existing bounded extractors.
- Relevance metrics describe the repository's synthetic regression corpus, not
  universal human judgement or million-file performance.
