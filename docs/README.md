# OmniSorSe documentation index

Start with one route. Source and tests define behavior; [Current State](CURRENT-STATE.md)
owns current source facts, and [Release Status](RELEASE_STATUS.md) owns readiness.
Historical sections preserve their original version and evidence boundaries.

## Start here

| Intent | Canonical route |
| --- | --- |
| Current project state | [Repository README](../README.md) → [Current State](CURRENT-STATE.md) |
| Test the published prerelease | [Installation](INSTALLATION.md) → [Manual testing](MANUAL_TESTING.md) → [issue #53](https://github.com/nishdel/OmniSorSe/issues/53) |
| Use the latest stable release | [v2.4 release record](CHANGELOG.md#release-v2-4-0) and the [official release](https://github.com/nishdel/OmniSorSe/releases/tag/v2.4.0) |
| Understand the current source candidate | [How OmniSorSe Works](HOW_OMNISORSE_WORKS.md), [acceptance criteria](ACCEPTANCE_CRITERIA.md), [v3 changes](CHANGELOG.md#release-v3-0-0) |
| Develop or contribute | [Contributing](../CONTRIBUTING.md), [Developer Guide](DEVELOPER_GUIDE.md), [Engineering workflow](engineering/README.md) |
| Validate or assess readiness | [Release Status](RELEASE_STATUS.md), [Validation](VALIDATION.md), [Platform compatibility](PLATFORM_COMPATIBILITY_MATRIX.md) |
| Research releases or history | [Changelog](CHANGELOG.md), [technical chronology](../RELEASE_HISTORY.md), [Implementation history](IMPLEMENTATION_HISTORY.md) |

## Current project authorities

| Subject | Living documents |
| --- | --- |
| Product and roadmap | [Product Vision](../PRODUCT_VISION.md), [Product Roadmap](../PRODUCT_ROADMAP.md), [Engineering Principles](../ENGINEERING_PRINCIPLES.md) |
| Architecture and ownership | [Architecture Overview](ARCHITECTURE_OVERVIEW.md), [System Map](Architecture/OpenSorSe_System_Map.md), [Architecture library](Architecture/README.md), [Repository Structure](REPOSITORY_STRUCTURE.md) |
| Safety and privacy | [Safety and Privacy](SAFETY_AND_PRIVACY.md), [Knowledge Graph security](SECURITY.md), [Dependency policy](FOSS_DEPENDENCY_POLICY.md) |
| Engineering system | [Authority map](engineering/ARCHITECTURE_AUTHORITY.md), [Development system](engineering/DEVELOPMENT_SYSTEM.md), [Risk and validation](engineering/RISK_VALIDATION_MATRIX.md), [Learning system](engineering/LEARNING_SYSTEM.md) |
| Documentation | [Policy and archival exceptions](DOCUMENTATION_POLICY.md), [migration register](engineering/documentation-migration.tsv) |

## User and subsystem guidance

| Subject | Canonical documents |
| --- | --- |
| Getting started and recovery | [User guide](USER_GUIDE.md), [Installation](INSTALLATION.md), [Troubleshooting](TROUBLESHOOTING.md), [Operational runbooks](OPERATIONAL_RUNBOOKS.md), [Linux build and launch](LINUX_BUILD_AND_LAUNCH.md) |
| Search and understanding | [Hybrid Search](HYBRID_SEARCH.md), [Search and AI quality](SEARCH_AND_AI_QUALITY.md), [Media intelligence](MEDIA_INTELLIGENCE.md), [Content intelligence](CONTENT_INTELLIGENCE.md), [Smart Tags](EXPLAINABLE_SMART_TAGS.md), [Faceted discovery](SCALABLE_FACETED_DISCOVERY.md) |
| Relationships | [Relationships and Collections](RELATIONSHIPS_AND_COLLECTIONS.md), [Trusted relationships](TRUSTED_RELATIONSHIPS_CONTEXT.md), [Knowledge Graph](KNOWLEDGE_GRAPH.md), [Graph compatibility](KNOWLEDGE_GRAPH_COMPATIBILITY.md) |
| Organization and workflow | [Organize](ORGANIZE.md), [Reviewed organization](REVIEWED_INTELLIGENT_ORGANIZATION.md), [Guided workflows](GUIDED_WORKFLOWS_PRODUCT_COHERENCE.md), [Workflow and indexing quality](WORKFLOW_AND_INDEXING_QUALITY.md), [Workflow portability](WORKFLOW_PORTABILITY.md) |
| Storage and operations | [Storage management](STORAGE_MANAGEMENT.md), [Production hardening](PRODUCTION_HARDENING.md), [Runtime readiness](SUPPORTED_RUNTIME_PLATFORM_READINESS.md), [Watched folders on Linux](WATCHED_FOLDERS_LINUX.md) |
| Explorer integration | [Transition and Explorer Protocol](OMNISORSE_TRANSITION_AND_EXPLORER_PROTOCOL.md), [OmniBrille handoff](OMNIBRILLE_COMPANION_HANDOFF.md) |
| Plugins | [SDK](EXTENSION_SDK.md), [Author guide](PLUGIN_AUTHOR_GUIDE.md), [Manifest](PLUGIN_MANIFEST_REFERENCE.md), [Local packages](LOCAL_PLUGIN_PACKAGES.md), [Platform compatibility](PLUGIN_PLATFORM_COMPATIBILITY.md) |

## Latest stable release

**v2.4.0** remains the latest stable release. Its [published release](https://github.com/nishdel/OmniSorSe/releases/tag/v2.4.0)
owns exact downloadable assets and publication state. Later implementation
milestones and the v3 testing candidate do not establish stable/GA acceptance.

## Current source and prerelease lineage

**v3.0.0-rc.1** is the published testing candidate. Use [current facts](CURRENT-STATE.md),
[release-specific checks](MANUAL_TESTING.md#manual-v3-0), [validation evidence](VALIDATION.md#validation-v3-0-0)
and the [release](https://github.com/nishdel/OmniSorSe/releases/tag/v3.0.0-rc.1).
The repository's 24 human scenarios remain **Not run**. v2.13/v2.12 are historical
prerelease fixtures. Automated and package smoke do not establish human acceptance.

## Release and implementation records

| Subject | Canonical record |
| --- | --- |
| Release changes and chronology | [Changelog](CHANGELOG.md), [Release History](../RELEASE_HISTORY.md) |
| Release process and packaging | [Maintainer Guide](MAINTAINER_GUIDE.md), [Native release packaging](RELEASE_PACKAGING.md) |
| Testing and evidence | [Manual testing](MANUAL_TESTING.md), [Validation methodology and history](VALIDATION.md), [release-testing issue preparation](release-testing/README.md) |
| Technical implementation | [Implementation history](IMPLEMENTATION_HISTORY.md), [specification archive](Implementation_Spec/README.md) |
| Media policy and provenance | [Screenshots and media](images/README.md) |
| Documentation archaeology | [Original inventory](DOCUMENTATION_INVENTORY.md), [consolidation report](engineering/reports/2026-10-11-documentation-consolidation.md) |

Update one canonical document per subject. Add version sections instead of
version-suffixed siblings. Use Git history, tags, GitHub releases and CI artifacts
for exact snapshots; exceptions require the [documented policy](DOCUMENTATION_POLICY.md).
