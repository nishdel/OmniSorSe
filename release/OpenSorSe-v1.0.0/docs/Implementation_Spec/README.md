# Implementation Specification Index

This index distinguishes historical foundation specifications from release-specific packages. Implemented code and the latest release proposal are authoritative when an older planning document describes a then-future boundary.

| Release | Status | Specification package |
| --- | --- | --- |
| v0.1 Foundation | Historical / complete | [Specifications 001–028](../Implementation_Spec/) and archived coding prompts |
| v0.2 Results Exploration | Complete | [Release proposal](v0.2/00_v0.2_Release_Proposal.md), specifications 029–031, and decisions |
| v0.3 Suggestions and Ranked Search | Complete | [Release proposal](v0.3/00_v0.3_Release_Proposal.md), specifications 032–034 |
| v0.4 Local Catalog | Complete | [Release proposal](v0.4/00_v0.4_Release_Proposal.md), specifications 035–036, and decisions |
| v0.5 Catalog Search and Maintenance | Complete | [Release proposal](v0.5/00_v0.5_Release_Proposal.md), specifications 037–038, and decisions |
| v0.6 User-Managed Tags | Complete | [Release proposal](v0.6/00_v0.6_Release_Proposal.md), specification 039, and decisions |
| v0.7 Saved Catalog Searches | Complete | [Release proposal](v0.7/00_v0.7_Release_Proposal.md), specifications 040–041, and decisions |
| v0.8 Snapshot Identity and Scope | Complete | [Release proposal](v0.8/00_v0.8_Release_Proposal.md), specifications 042-043, and decisions |
| v0.9 Historical Snapshot Comparison | Complete / audited | [Release proposal](v0.9/00_v0.9_Release_Proposal.md), specifications 044-045, [audit corrections](v0.9/AUDIT_CORRECTIONS.md), and decisions |
| v0.9.1 Optional AI and Feature Controls | Implementation and corrective pass complete; manual GUI verification pending | [Release proposal](v0.9.1/00_v0.9.1_Release_Proposal.md), [specification 046](v0.9.1/046_Optional_AI_and_Advanced_Feature_Controls.md), [corrective specification 047](v0.9.1/047_Correction_Reliability_and_Usability_Pass.md), and [decisions](v0.9.1/IMPLEMENTATION_DECISIONS.md) |
| v1.0 Integrated Local Intelligence and Structure History | Release-candidate UX/AI hardening, README, portable Windows distribution, and live Ollama diagnostics implemented on local `v1.0`; automated validation complete, manual GUI/OCR/Ollama/platform verification pending | [Release proposal](v1.0/00_v1.0_Release_Proposal.md), [integrated specification 048](v1.0/048_v1.0_Integrated_Release.md), [final-completion specification 049](v1.0/049_Final_Product_Completion.md), [UX and AI hardening specification 050](v1.0/050_Release_Candidate_UX_and_AI_Hardening.md), [README and Windows distribution specification 051](v1.0/051_Readme_and_Windows_Distribution.md), [live AI diagnostics specification 052](v1.0/052_Live_AI_Request_Diagnostics.md), and [decisions](v1.0/IMPLEMENTATION_DECISIONS.md) |

## Current boundary

v1.0 preserves the v0.9.1 local-first and suggestion-only AI boundary while adding bounded page-aware local OCR and metadata extraction, a separately gated one-document AI text proposal, provenance-aware tags, deterministic Semantic Search Beta, and explicitly confirmed folder-restructuring history. Preview and diagram workflows are read-only. The only new file mutation is an independently confirmed, root-confined restructuring apply operation for disposable/user-approved content; it rejects overwrite, traversal, and missing-source conditions and records history only after success. No current specification authorizes autonomous AI filesystem control, monitoring, plugins, cloud indexing, or unreviewed execution.
