# OmniSorSe v2.12.0 — Trusted Relationships & Context

**Status:** Unreleased candidate. No tag, package publication, or GitHub release
is claimed by this document.

v2.12 makes the existing relationship system easier to trust and correct. Direct
Related Files no longer depends on the optional Knowledge Graph. Evidence is
grouped into capped independent families, noisy correlated signals cannot stack
without bounds, and semantic or AI-derived evidence cannot establish a
relationship alone.

Users can mark a pair **Related**, **Not Related**, or return it to **Use automatic
result**. Negative corrections remain discoverable for reversal. Multiple typed
edges are shown as one related target with a bounded explanation. Search and
Files provide direct entry points while exact filename, stem, and prefix intent
remain dominant.

Large-library work remains bounded through indexed candidate buckets, compact
batch hydration, a 512 defensive ceiling, and resumable relationship-only
version refresh. Explorer Protocol stays at 1.0 and returns aggregated,
authorized, opaque related context without adding writes.

Logical `.oms-state` format 2 adds pair and user-authored Smart Collection
authority. Format-1 import remains supported. Generated edges, evidence,
automatic membership, and graph projections remain rebuildable and are not
backed up. Restore never guesses unresolved identity, and a format-1 import does
not clear newer Smart Collection authority that its older payload cannot carry.

Architecture remains intentionally stable: .NET 10 LTS, schema 6, no new
production dependency, optional unchanged AI, optional derived Knowledge Graph,
Smart Collections as grouping authority, and reviewed Change Plans as the only
file-mutation path.

Automated results and outstanding manual evidence are recorded in
[Release Status](RELEASE_STATUS.md) and the [v2.12 manual addendum](MANUAL_TESTING_v2.12.md).
