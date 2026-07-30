# Maintainer guide

This guide records the cross-cutting responsibilities required to keep an
OpenSorSe release compatible, safe, and understandable.

Use [Engineering Principles](../ENGINEERING_PRINCIPLES.md) for the reasoning
behind these operational requirements and
[Product Roadmap](../PRODUCT_ROADMAP.md) for version/integration status.

## Release checklist

1. Confirm the release branch, base commit, upstream, worktrees, and absence of
   an in-progress Git operation.
2. Review every working-tree file; separate implementation, documentation,
   generated output, and unrelated user work.
3. Verify version metadata and release documentation.
4. Run restore, Debug build/tests, Release build/tests, formatting, whitespace,
   documentation-link, Mermaid, machine-path, and artifact checks.
5. Complete the current manual checklist on disposable data, including
   interruption/recovery and hostile plugin-package cases where applicable.
6. Inspect privacy-sensitive diagnostics and package contents.
7. Only after all gates pass, follow an explicitly approved commit/tag/package/
   publish workflow. Source validation alone does not claim a release exists.

The historical [v1.0 release checklist](RELEASE_CHECKLIST_v1.0.md) remains the
packaging baseline. Apply the current [v1.8 manual checklist](MANUAL_TESTING_v1.8.md)
and [Release Status](RELEASE_STATUS.md) in addition.

Record Windows, Linux, and macOS results independently. A green local Windows
run does not prove the Ubuntu workflow ran, and a successful Linux source build
does not prove every distribution, desktop, mount, watcher limit, native
dependency, or packaging format. Never turn an unobserved CI definition into a
passed-CI claim.

## Version metadata locations

| Location | Responsibility |
| --- | --- |
| `Directory.Build.props` | Product, informational, assembly, and file version defaults |
| `src/OpenSorSe.Desktop/OpenSorSe.Desktop.csproj` | Desktop package version |
| `src/OpenSorSe.Desktop/app.manifest` | Windows assembly identity |
| `src/OpenSorSe.Desktop/ViewModels/AboutViewModel.cs` | User-visible short version |
| `README.md`, `docs/CHANGELOG.md`, `docs/VERSION_NOTES_v*.md` | User/release narrative |
| `docs/RELEASE_STATUS.md` | Current integration, validation, package, tag, and publication readiness |
| `PRODUCT_ROADMAP.md`, `RELEASE_HISTORY.md` | Planned direction and concise historical branch/date/merge index |
| Workflow/plugin constants and import envelopes | Compatibility/schema identity, not marketing text |

Tests should assert compiled metadata and About presentation. Do not update only
the visible version.

## Persistence and migration ownership

The owner of a persisted model owns its bounds, validation, migration, atomic
write, corruption behavior, and tests.

| Store | Owner |
| --- | --- |
| Settings and logs | Core |
| Catalog, saved searches, content, semantic index, structure history | Application subsystem containing the store |
| Durable Search index | Application contracts and `OpenSorSe.Indexing.Sqlite` provider |
| Watched configuration/catalogue/activity | Application/Watching |
| Workflow library/import/export | Application/Workflows |
| Plugin state and controlled installed versions | Application/Plugins |
| Change Plans and Operation Journal | Executor |
| AI decision history | AI transport project |

For a schema change:

1. decide whether old data migrates, remains readable, or is explicitly
   unsupported;
2. increment the owning schema identity where required;
3. validate all records after migration;
4. write the upgraded representation atomically;
5. preserve or safely reject corrupt/unknown data;
6. add old-to-new, future-version, malformed, oversized, and cancellation
   tests;
7. update Architecture Overview, Safety and Privacy, migration notes, and
   Version Notes.

For the durable Search index, also test `PRAGMA quick_check`, unsupported newer
schemas, interrupted migration recovery copies, startup recovery of every
`running` job/stage row, WAL/backup lifecycle on Windows, connection disposal,
quota maintenance, and rebuild source preservation. PostgreSQL is not a
desktop dependency; alternate providers must preserve the provider-neutral
contract.

Schema 2 adds durable privacy rules. A v1-to-v2 migration must retain sources,
files, shared content, stages, coverage, and watched/manual ownership while
creating a pre-migration recovery copy. Validate inspection, forgetting,
selective clearing, suppression against immediate re-index loops, targeted
repair, duplicate-content impact reporting, and unchanged source files.

For Search changes, run `Category=SearchRelevance` and
`Category=PerformanceRegression` in addition to the full suite. Inspect the
SQLite query plans exercised by provider tests, keep query/candidate/snippet
bounds intact, and compare exact-match preservation, top-k recall, reciprocal
rank, stability, cancellation, allocations, and increasing synthetic corpus
sizes. These are regression controls, not universal quality or latency claims.

Never silently reinterpret a field in a way that could authorize broader file
operations.

## Safety invariants

Review these on every release:

- All current Desktop organization flows create Change Plans.
- `ChangePlanReviewViewModel` still requires action decisions, validation, and
  a separate Apply confirmation.
- `ChangePlanExecutionService` revalidates immediately and journals before
  mutation.
- `PhysicalFileSystemGateway` forbids overwrite.
- Rollback/Undo verify current identity and never replace occupied data.
- Watchers call proposal/review services, not execution.
- AI remains optional, bounded, validated, and suggestion-only.
- Workflow policy can narrow application safety gates but cannot broaden them.
- Plugins have no supported direct mutation/approval path; capabilities are
  checked at registration and invocation.
- Plugin integrity and load-context isolation are not described as publisher
  authentication or sandboxing.
- Application stores cannot escape their controlled files/directories.

## Journal compatibility

The Operation Journal is recovery state, not expendable telemetry. Maintain:

- stable operation/action identities;
- durable pending/running state before the first corresponding mutation;
- enough original/result identity to inspect interruption and Undo;
- safe errors without raw sensitive content;
- monotonic action transitions;
- backward-compatible loading or an explicit release-blocking migration;
- bounded retention that does not remove active recovery facts.

Changing execution ordering, case-only rename handling, rollback, or Undo
requires interruption tests at every durable boundary.

## Plugin compatibility

- Keep `OpenSorSe.Extensions.Abstractions` independent of internal projects.
- Treat public SDK types and semantics as compatibility surface.
- Update XML documentation, SDK guide, author guide, manifest reference,
  package guide, host validation, and compatibility notes together.
- Do not add a capability without user-visible grant semantics and enforcement.
- Do not add an extension point without input/output bounds, cancellation,
  timeout, validation, provenance, failure containment, and adversarial tests.
- Profile/recipe dependencies use exact plugin versions; do not silently drift.
- A stronger isolation model requires a new out-of-process compatibility
  design, not a documentation-only claim.

## Documentation maintenance

Every release must keep these entry points current:

- root README;
- Product Vision, Product Roadmap, Engineering Principles, and Release History
  when their scope changes;
- `docs/README.md`;
- User Guide, Troubleshooting, Manual Testing, and Version Notes;
- Architecture Overview, Repository Structure, and System Map;
- Safety and Privacy;
- Changelog and Release Status;
- relevant implementation specification and subsystem architecture;
- Extension SDK/plugin documents when applicable.

Run documentation validation after renames. Preserve meaningful historical
documents and label them; do not rewrite old release evidence to look current.
The [documentation inventory](DOCUMENTATION_INVENTORY.md) records the authority
model.

## Generated and private data

Never commit:

- `bin`, `obj`, `.artifacts`, `TestResults`, or IDE state;
- new release binaries, ZIPs, checksums, or packages outside an approved release;
- `%LOCALAPPDATA%\OpenSorSe` settings, indexes, histories, plugins, or logs;
- diagnostic exports without explicit review and redaction;
- OCR temporary pages or test workspaces;
- credentials, tokens, private endpoints, machine-specific paths, or user file
  samples.
