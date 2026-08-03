# OpenSorSe v1.9 — Relationships, Context & Smart Collections

v1.9 builds directly on the unmerged, validated v1.8 branch. It adds a local,
provider-neutral relationship projection over indexed files while keeping every
existing file-operation safety boundary.

## Highlights

- deterministic, versioned file relationships with retained evidence and
  understandable Low/Medium/High/Confirmed confidence;
- virtual Smart Collections, member inspection, and bounded timestamp timeline;
- Related Files inspection, sorting, filtering, explanations, and provenance;
- persistent manual link/unlink, confirm/reject, always/never, rename, pin,
  merge, split, forget, rebuild, and repair controls;
- optional explainable relationship expansion in Search, with exact and literal
  results still ranked first;
- SQLite schema 3 migration from the v1.8 schema with recovery-copy and
  transactional migration behavior;
- privacy exclusions, file/source/collection forgetting, aggregate diagnostics,
  graph bounds, corruption repair, and synthetic regression coverage.

## Preserved behavior

The v1.7 durable indexing pipeline and v1.8 Search/ranking/privacy design are
extended, not reimplemented. Existing catalogs, saved scans, watched folders,
duplicate detection, workflows, plugins, Change Plans, Operation Journal,
recovery, and Undo retain their contracts. Ollama is not required.

## Important limits

Smart Collections never move files. Automatic relationships are conservative
and may miss useful context. Semantic similarity alone cannot create an edge.
This branch has no release tag or package and is not merged to `main`.
Interactive manual testing is not claimed; every item in
`MANUAL_TESTING_v1.9.md` remains unchecked until a maintainer observes it.
