# OpenSorSe documentation

This is the documentation entry point for OpenSorSe 1.6. Start with the section
that matches what you are trying to do. Documents with a version in their name
describe that release; older versioned documents are retained as history and
must not override the current architecture or safety documentation.

## Recommended starting points

| Audience | Start here | Then read |
| --- | --- | --- |
| User | [OpenSorSe README](../README.md) | [Installation](INSTALLATION.md), [v1.6 User Guide](USER_GUIDE_v1.6.md), and [Troubleshooting](TROUBLESHOOTING_v1.6.md) |
| Contributor | [Contributing](../CONTRIBUTING.md) | [Developer Guide](DEVELOPER_GUIDE.md), [Repository Structure](REPOSITORY_STRUCTURE.md), and [Architecture Overview](ARCHITECTURE_OVERVIEW.md) |
| Plugin author | [Extension SDK](EXTENSION_SDK_v1.4.md) | [Plugin Author Guide](PLUGIN_AUTHOR_GUIDE_v1.4.md), [Manifest Reference](PLUGIN_MANIFEST_REFERENCE_v1.4.md), and [v1.5 platform constraints](PLUGIN_PLATFORM_COMPATIBILITY_v1.5.md) |
| Maintainer | [Maintainer Guide](MAINTAINER_GUIDE.md) | [Release Status](RELEASE_STATUS.md), [Safety and Privacy](SAFETY_AND_PRIVACY.md), and [Implementation Specifications](Implementation_Spec/README.md) |

## Users

- [Installation and updates](INSTALLATION.md)
- [OpenSorSe 1.6 User Guide](USER_GUIDE_v1.6.md)
- [Manual verification checklist](MANUAL_TESTING_v1.6.md)
- [Troubleshooting](TROUBLESHOOTING_v1.6.md)
- [Safety and Privacy](SAFETY_AND_PRIVACY.md)
- [Version notes](VERSION_NOTES_v1.6.md)
- [Implementation report](V1.6_IMPLEMENTATION_REPORT.md)
- [Validation report](V1.6_VALIDATION_REPORT.md)
- [Platform capability matrix](PLATFORM_COMPATIBILITY_MATRIX.md)
- [Linux build and launch](LINUX_BUILD_AND_LAUNCH.md)
- [Known limitations and release readiness](RELEASE_STATUS.md)

The user guide covers the first scan, Files, Watched Folders, Workflow Profiles,
Sorting Recipes, Change Plan review, Apply, Undo, Recovery, local AI, and Plugin
management. The safety document is authoritative for privacy and mutation
boundaries.

## Contributors

- [Contributing](../CONTRIBUTING.md)
- [Developer Guide](DEVELOPER_GUIDE.md)
- [Repository Structure](REPOSITORY_STRUCTURE.md)
- [Architecture Overview](ARCHITECTURE_OVERVIEW.md)
- [OpenSorSe System Map](Architecture/OpenSorSe_System_Map.md)
- [Architecture library](Architecture/README.md)
- [Coding standards](Architecture/99_Appendix/Coding_Standards.md)
- [Naming conventions](Architecture/99_Appendix/Naming_Conventions.md)
- [Technology stack](Architecture/99_Appendix/Technology_Stack.md)
- [FOSS dependency policy](FOSS_DEPENDENCY_POLICY.md)

The architecture overview and repository structure guide describe the current
implementation. Detailed architecture documents that describe future or legacy
designs are labelled by the [architecture index](Architecture/README.md).

## Current subsystem references

- [Scanning, metadata, and OCR](Architecture/03_Readers/10_v1_OCR_and_Metadata.md)
- [Advanced Diagnostics](Architecture/01_Core/10_Advanced_Diagnostics.md)
- [AI prompt and validation contracts](Architecture/04_AI/11_Small_Model_Prompt_Contracts.md)
- [Change Plans and Operation Journal](Architecture/07-Rules/07_v1.1_Change_Plans_and_Operation_Journal.md)
- [Watched Folders and incremental scanning](Architecture/02_Scanner/09_v1.2_Watched_Folders_and_Incremental_Scanning.md)
- [Workflow Profiles and Sorting Recipes](Architecture/07-Rules/08_v1.3_Workflow_Profiles_and_Recipes.md)
- [Plugin Foundation](Architecture/10_Plugins/06_v1.4_Plugin_Foundation.md)
- [v1.5 platform architecture](Architecture/00_System/08_v1.5_Platform_Architecture.md)
- [v1.6 reliability architecture](Architecture/00_System/09_v1.6_Reliability_Architecture.md)
- [Workflow portability](WORKFLOW_PORTABILITY_v1.5.md)
- [Watched Folders on Linux](WATCHED_FOLDERS_LINUX_v1.5.md)

## Extension SDK and plugins

- [Extension SDK contract](EXTENSION_SDK_v1.4.md)
- [Plugin authoring](PLUGIN_AUTHOR_GUIDE_v1.4.md)
- [Manifest schema](PLUGIN_MANIFEST_REFERENCE_v1.4.md)
- [Local package lifecycle](LOCAL_PLUGIN_PACKAGES_v1.4.md)
- [Plugin platform compatibility](PLUGIN_PLATFORM_COMPATIBILITY_v1.5.md)
- [Plugin implementation specification](Implementation_Spec/v1.4/056_Plugin_Foundation_and_Extension_SDK.md)
- [Cross-platform implementation specification](Implementation_Spec/v1.5/057_Cross_Platform_Foundation_and_Linux_Preview.md)
- [Production-hardening implementation specification](Implementation_Spec/v1.6/058_Reliability_Performance_and_Production_Hardening.md)

External plugins run in-process with the current user's permissions. A
collectible assembly-load context is a dependency-isolation mechanism, not a
security sandbox. The supported SDK does not expose file mutation, Change Plan
approval, the executor, credentials, or host dependency injection.

## Maintainers and releases

- [Maintainer Guide](MAINTAINER_GUIDE.md)
- [Release Status](RELEASE_STATUS.md)
- [Release checklist for the last packaged baseline](RELEASE_CHECKLIST_v1.0.md)
- [Changelog](CHANGELOG.md)
- [Roadmap](roadmap.md)
- [Implementation specification index](Implementation_Spec/README.md)
- [Documentation inventory](DOCUMENTATION_INVENTORY.md)

`RELEASE_CHECKLIST_v1.0.md` is historical and remains useful as the most recent
packaging checklist. It is not by itself sufficient to publish v1.6; the v1.6
manual checklist and maintainer guide identify the additional gates.

## Historical documentation

The repository intentionally keeps earlier User Guides, Troubleshooting guides,
Manual Testing guides, Version Notes, release proposals, decisions, and
implementation specifications. They explain why compatibility behavior exists
and provide release-specific test evidence. Use them for history, not as the
current product contract.

The [documentation inventory](DOCUMENTATION_INVENTORY.md) classifies every
documentation family and records the one obsolete file removed during the v1.4
comprehension pass.
