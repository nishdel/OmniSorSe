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
| v1.1 Safe File Operations and Robustness | Stable implementation and automated validation complete; manual UI/platform verification pending | [Specification 053](v1.1/053_Safe_File_Operations_and_Robustness.md), [architecture](../Architecture/07-Rules/07_v1.1_Change_Plans_and_Operation_Journal.md), [user guide](../USER_GUIDE_v1.1.md), and [manual checklist](../MANUAL_TESTING_v1.1.md) |
| v1.2 Watched Folders and Incremental Scanning | Source implementation and automated validation complete; manual GUI/filesystem/platform verification pending | [Specification 054](v1.2/054_Watched_Folders_and_Incremental_Scanning.md), [architecture](../Architecture/02_Scanner/09_v1.2_Watched_Folders_and_Incremental_Scanning.md), [user guide](../USER_GUIDE_v1.2.md), and [manual checklist](../MANUAL_TESTING_v1.2.md) |
| v1.3 Workflow Profiles and Recipe Library | Source implementation and automated validation complete; manual verification pending | [Specification 055](v1.3/055_Workflow_Profiles_and_Recipe_Library.md), [architecture](../Architecture/07-Rules/08_v1.3_Workflow_Profiles_and_Recipes.md), [user guide](../USER_GUIDE_v1.3.md), and [manual checklist](../MANUAL_TESTING_v1.3.md) |
| v1.4 Plugin Foundation and Extension SDK | Source implementation and automated validation complete; manual verification pending | [Specification 056](v1.4/056_Plugin_Foundation_and_Extension_SDK.md), [architecture](../Architecture/10_Plugins/06_v1.4_Plugin_Foundation.md), [SDK](../EXTENSION_SDK_v1.4.md), [user guide](../USER_GUIDE_v1.4.md), and [manual checklist](../MANUAL_TESTING_v1.4.md) |
| v1.5 Cross-Platform Foundation and Linux Preview | Source implementation complete; exact platform validation tracked in Release Status | [Specification 057](v1.5/057_Cross_Platform_Foundation_and_Linux_Preview.md), [platform architecture](../Architecture/00_System/08_v1.5_Platform_Architecture.md), [matrix](../PLATFORM_COMPATIBILITY_MATRIX.md), [user guide](../USER_GUIDE_v1.5.md), and [manual checklist](../MANUAL_TESTING_v1.5.md) |

## Current boundary

v1.5 preserves the local-first, suggestion-only AI and v1.1 execution boundary while making platform behavior explicit. Reusable profiles and local plugin contributions resolve typed scan/analysis policy and recipes create proposals; neither grants approval. Watched roots automate detection, stability checking, incremental analysis, catalogue reconciliation, and optional suggestion creation. Suggestions remain non-mutating Change Plans; only user-approved, validated actions can enter the dedicated execution service after final confirmation. No specification authorizes autonomous AI filesystem control, permanent deletion, plugin direct mutation, cloud indexing, or unreviewed execution.

The current release is `v1.5`, **Cross-Platform Foundation and Linux Preview**, on `v1.5-cross-platform-foundation`. Release branches follow `v<version>-<primary-feature>`, for example `v1.1-safe-file-operations`, `v1.2-watched-folders`, `v1.3-workflow-profiles`, `v1.4-plugin-foundation`, and `v1.5-cross-platform-foundation`.

> Workflow profiles automate configuration and analysis, not approval or file modification.
