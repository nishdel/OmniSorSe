# Implementation Specification Index

This index distinguishes historical foundation specifications from release-specific packages. Implemented code and the latest release proposal are authoritative when an older planning document describes a then-future boundary.

Specifications record intended and delivered implementation boundaries; they
are not the roadmap or current release-readiness authority. Use
[Product Roadmap](../../PRODUCT_ROADMAP.md),
[Release History](../../RELEASE_HISTORY.md), and
[Release Status](../RELEASE_STATUS.md) for those questions.

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
| v1.1 Safe File Operations and Robustness | Stable implementation and automated validation complete; manual UI/platform verification pending | [Specification 053](v1.1/053_Safe_File_Operations_and_Robustness.md), [architecture](../Architecture/07-Rules/07_v1.1_Change_Plans_and_Operation_Journal.md), [user guide](../USER_GUIDE_v1.1.md), and [manual checklist](../MANUAL_TESTING_v1.1.md) |
| v1.2 Watched Folders and Incremental Scanning | Source implementation and automated validation complete; manual GUI/filesystem/platform verification pending | [Specification 054](v1.2/054_Watched_Folders_and_Incremental_Scanning.md), [architecture](../Architecture/02_Scanner/09_v1.2_Watched_Folders_and_Incremental_Scanning.md), [user guide](../USER_GUIDE_v1.2.md), and [manual checklist](../MANUAL_TESTING_v1.2.md) |
| v1.3 Workflow Profiles and Recipe Library | Source implementation and automated validation complete; manual verification pending | [Specification 055](v1.3/055_Workflow_Profiles_and_Recipe_Library.md), [architecture](../Architecture/07-Rules/08_v1.3_Workflow_Profiles_and_Recipes.md), [user guide](../USER_GUIDE_v1.3.md), and [manual checklist](../MANUAL_TESTING_v1.3.md) |
| v1.4 Plugin Foundation and Extension SDK | Source implementation and automated validation complete; manual verification pending | [Specification 056](v1.4/056_Plugin_Foundation_and_Extension_SDK.md), [architecture](../Architecture/10_Plugins/06_v1.4_Plugin_Foundation.md), [SDK](../EXTENSION_SDK_v1.4.md), [user guide](../USER_GUIDE_v1.4.md), and [manual checklist](../MANUAL_TESTING_v1.4.md) |
| v1.5 Cross-Platform Foundation and Linux Preview | Source implementation complete; exact platform validation tracked in Release Status | [Specification 057](v1.5/057_Cross_Platform_Foundation_and_Linux_Preview.md), [platform architecture](../Architecture/00_System/08_v1.5_Platform_Architecture.md), [matrix](../PLATFORM_COMPATIBILITY_MATRIX.md), [user guide](../USER_GUIDE_v1.5.md), and [manual checklist](../MANUAL_TESTING_v1.5.md) |
| v1.6 Reliability, Performance, and Production Hardening | Source implementation complete; exact final validation tracked in the v1.6 report | [Specification 058](v1.6/058_Reliability_Performance_and_Production_Hardening.md), [reliability architecture](../Architecture/00_System/09_v1.6_Reliability_Architecture.md), [implementation report](../V1.6_IMPLEMENTATION_REPORT.md), and [manual checklist](../MANUAL_TESTING_v1.6.md) |
| v1.7 Deep Indexing Foundation | Source implementation complete; exact final validation tracked in the v1.7 report | [Specification 059](v1.7/059_Deep_Indexing_Foundation.md), [deep-indexing architecture](../Architecture/00_System/10_v1.7_Deep_Indexing_Architecture.md), [implementation report](../V1.7_IMPLEMENTATION_REPORT.md), and [manual checklist](../MANUAL_TESTING_v1.7.md) |
| v1.8 Search Intelligence, Quality and Privacy | Source implementation complete; final automated validation and interactive manual verification tracked separately | [Specification 060](v1.8/060_Search_Intelligence_Quality_and_Privacy.md), [Search architecture](../Architecture/06_Search/09_v1.8_Search_Intelligence_Privacy.md), [implementation report](../V1.8_IMPLEMENTATION_REPORT.md), and [manual checklist](../MANUAL_TESTING_v1.8.md) |
| v1.9 Relationships, Context & Smart Collections | Source implementation complete; final automated validation and interactive manual verification tracked separately | [Specification 061](v1.9/061_Relationships_Context_and_Smart_Collections.md), [relationship architecture](../Architecture/06_Search/10_v1.9_Relationships_Context.md), [implementation report](../V1.9_IMPLEMENTATION_REPORT.md), and [manual checklist](../MANUAL_TESTING_v1.9.md) |
| v2.0 Knowledge Graph | Stability-first design package only; no runtime implementation or release claim | [Design proposal](v2.0/00_v2.0_Knowledge_Graph_Stability_Proposal.md), [specification 062](v2.0/062_Knowledge_Graph_Stability_Design.md), [architecture](../Architecture/06_Search/11_v2.0_Knowledge_Graph_Stability_Design.md), [release-readiness checklist](../RELEASE_READINESS_v2.0.md), and [unchecked manual checklist](../MANUAL_TESTING_v2.0.md) |

## Current boundary

v1.9 preserves the local-first, suggestion-only AI and v1.1 execution boundary,
reuses v1.7 provider-independent durable indexing and v1.8 Search, and adds
deterministic evidence-backed relationships, virtual collections/context, user
corrections, contextual Search, and index-only privacy/repair controls.
Reusable profiles and local plugin contributions resolve typed scan/analysis
policy and recipes create proposals; neither grants approval. No specification
authorizes autonomous AI filesystem control, permanent deletion, plugin direct
mutation, cloud indexing, or unreviewed execution.

The newest implemented source milestone is `v1.9`, **Relationships, Context &
Smart Collections**, on `v1.9-relationships-context`. It remains
unmerged from `main`, and interactive manual validation is not complete.

The `v2.0-knowledge-graph-design` branch adds specification 062 and a stability
design package only. It deliberately does not change application version,
runtime source, or persistence schemas.

> Workflow profiles automate configuration and analysis, not approval or file modification.
