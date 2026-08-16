# OmniSorSe v2.10.0 - Production Hardening & Operational Resilience

**Candidate only:** no v2.10.0 package, tag, merge, or release exists yet.

v2.10 hardens long-lived local state and reviewed file operations. It adds
single-writer profile ownership, fail-closed corrupted Change Plan/Operation
Journal handling, bounded PDF parsing, logical state export/restore, complete
Forget coordination, traceable build provenance, bounded health checks,
abnormal-shutdown detection, and adversarial/failure regression coverage.
Logical state includes exact-ID Smart Tag authority and exact-pair manual
relationship decisions without path guessing.

It preserves schema 6, Explorer Protocol v1, Search ranking, Smart Tag
authority, organization recipes, Change Plans, reconciliation, and Undo. It
adds no new production dependency and no autonomous or network-listening
capability. PdfPig extraction and native PDFium rasterization remain in
process behind strict bounds; manual hostile-fixture and native-platform
validation remains required.

See [production hardening](PRODUCTION_HARDENING_v2.10.md), the
[master manual matrix](MANUAL_TESTING_v2.10.md), and
[operational runbooks](OPERATIONAL_RUNBOOKS_v2.10.md).
