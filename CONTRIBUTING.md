# Contributing to OpenSorSe

Thank you for improving OpenSorSe. Changes should remain local-first,
reviewable, bounded, and explicit about their safety and privacy effects.

Before a cross-cutting change, read the
[Product Vision](PRODUCT_VISION.md),
[Engineering Principles](ENGINEERING_PRINCIPLES.md), and
[Architecture Overview](docs/ARCHITECTURE_OVERVIEW.md). The
[Documentation Index](docs/README.md) provides a one-hour project reading path.

## Prerequisites

- Windows 10 or later for the primary verified Desktop runtime, or a Linux x64
  graphical environment for the conservative source-build preview.
- The .NET SDK selected by [`global.json`](global.json).
- Git.
- Optional: Ollama for manual AI-provider testing.
- Optional: Tesseract 5 with `eng` and/or `deu` data for manual OCR testing.

Neither Ollama nor Tesseract is required for the automated test suite.
Linux setup and framework-dependent launch details are in
[Linux Build and Launch](docs/LINUX_BUILD_AND_LAUNCH.md).

## Restore, build, test, and run

```powershell
dotnet restore .\OpenSorSe.sln
dotnet build .\OpenSorSe.sln --configuration Debug --no-restore
dotnet test .\OpenSorSe.sln --configuration Debug --no-build --no-restore
dotnet build .\OpenSorSe.sln --configuration Release --no-restore
dotnet test .\OpenSorSe.sln --configuration Release --no-build --no-restore
dotnet format .\OpenSorSe.sln --verify-no-changes --no-restore
git diff --check
dotnet run --project .\src\OpenSorSe.Desktop\OpenSorSe.Desktop.csproj
```

Run both complete configurations before requesting review. Do not skip or
weaken an existing test to make a change pass.

## Branch and commit guidance

- Use a focused, descriptive branch. A prefix is optional when it adds no
  useful information.
- Release branches normally use `v<version>-<primary-feature>` only when a
  maintainer starts that release. Planned roadmap concepts do not receive
  invented branches.
- Preserve historical exceptions: v1.1 used `v1.1`; v0.1/v0.2 used
  `coding/v0.1` and `coding/v0.2`; v0.4-v0.9 were delivered together on
  `v0.9`.
- Do not commit directly to `main`.
- Do not modify or reuse an earlier release branch for unrelated work.
- Keep commits reviewable and describe behavior, not just edited files.
- Do not mix generated output, release packages, or unrelated formatting with a
  product change.
- Never rewrite shared release history or force-push without explicit
  maintainer coordination.

## Project structure

Read the [Repository Structure](docs/REPOSITORY_STRUCTURE.md) before choosing a
project:

- Scanner: read-only discovery and deterministic file analysis.
- Rules: pure evaluation, planning, and conflict detection.
- Executor: Change Plans, validation, journal, approved mutation, recovery, and
  Undo.
- Application: use-case orchestration, stores, content/OCR, AI policy,
  workflows, watchers, and plugin host.
- AI: concrete Ollama-compatible transport.
- Indexing.Sqlite: the embedded implementation of provider-neutral durable
  indexing contracts; SQL and migrations stay inside this project.
- Desktop: Avalonia composition and presentation.
- Extensions.Abstractions: stable plugin-author API only.
- Core/Platform: path semantics, application locations, filesystem identity and
  capability inspection, external-tool discovery, and platform reporting.

Preserve the documented dependency direction. New cycles or an internal
dependency in the standalone SDK are not acceptable.

## MVVM expectations

- Views own layout, bindings, accessibility metadata, and strictly
  presentation-specific interaction.
- ViewModels own observable presentation state, commands, command gating,
  cancellation sources, and user-safe status.
- Application/domain services own workflows, Search interpretation/ranking,
  relationship evidence/confidence, persistence, provider calls, validation,
  and side effects.
- Code-behind must not become an alternate service locator or contain
  filesystem, database, AI, plugin, or business logic.
- ViewModels must not create SQL, calculate Search weights or relationship
  confidence, project/query Knowledge Graph storage directly, call Ollama
  directly, or perform raw file operations.
- New ViewModel behavior should be covered without requiring a live desktop
  where practical. Keyboard, focus, assistive-technology, pointer/touch, and
  visual behavior still require manual validation.

## Safety expectations

These invariants are release blockers:

- No AI response may directly mutate a file.
- No watcher event may directly mutate a file.
- No Workflow Profile or Sorting Recipe may imply approval.
- No supported plugin API may expose direct user-file mutation, Change Plan
  approval, the executor, credentials, or the host service provider.
- New organization proposals must become persisted, reviewable Change Plans.
- Production mutation must pass through `IChangePlanExecutionService` and
  `IFileSystemGateway`.
- The Operation Journal must be durable before mutation.
- Immediate preflight must reject stale or unsafe plans.
- Paths must remain under the explicit approved root.
- Reparse/link escapes and silent overwrites must remain forbidden.
- Undo must block rather than overwrite externally changed data.
- Platform-sensitive behavior belongs behind a focused adapter; do not scatter
  OS checks through business logic.
- No operation may elevate, run `sudo`, change ownership, broadly change user
  permissions, or silently cross a filesystem boundary.

Direct `File`/`Directory` writes outside Executor are allowed only for a
clearly-owned application-data store, controlled plugin/package location, log,
export chosen by the user, or bounded temporary workspace. Document the
ownership and confinement.

## Where to add functionality

- **Scanner or extractor:** add an interface/implementation in Scanner for
  basic file analysis, or Application/Content for format content/OCR. Preserve
  read-only, bounded, cancellable behavior and per-file failure isolation.
- **Workflow capability:** define typed policy in Application/Workflows, update
  validation, resolution snapshots, persistence/import/export, Desktop
  presentation, and watcher/manual paths.
- **Desktop view:** put state and commands in a ViewModel, layout/bindings in
  XAML, and service composition in `App`. Do not put filesystem business logic
  in code-behind.
- **Plugin contribution:** evolve the standalone SDK compatibly, enforce the
  capability at registration and invocation, validate outputs, record
  provenance, and add hostile/timeout/cancellation tests.
- **Persistence field:** update the owning schema, migration or explicit
  rejection, bounds, corruption behavior, tests, Safety and Privacy, and
  release notes together.
- **Relationship signal:** retain the actual bounded evidence and algorithm
  version, preserve user corrections, keep candidate/graph queries bounded,
  and test conservative false-positive and false-negative behavior.
- **Knowledge Graph behavior:** keep provider-neutral contracts in Application,
  SQLite mechanics in the provider, schema-3 v1.9 authority unchanged, source/
  decision/privacy ingestion and applied watermarks distinct, and source files
  unopened. Preserve hard page/traversal/Search/component bounds and test
  recovery/fencing before widening a contract.

## Tests

Add tests at the narrowest owning layer and include adversarial cases where
inputs cross a trust boundary. Relevant cases include:

- malformed, oversized, duplicate, traversal, reparse, and incompatible data;
- cancellation before and during work;
- timeouts and provider/plugin failure;
- atomic-write failure and corrupt-store behavior;
- stale identity, occupied destinations, and overwrite prevention;
- concurrent command/lifecycle races;
- migration and backward compatibility;
- completed-manifest count/hash, idempotent projection, stale claim fencing,
  applied-watermark lag, decision recovery privacy floor, graph bounds, and
  ordinary Search fallback;
- exact diagnostics/provenance where it is a safety fact.

Use unique temporary directories and delete only the exact path created by the
test. Never point a test at a user folder.

## Manual validation

Automated coverage does not prove desktop interaction, accessibility,
platform/filesystem behavior, watcher delivery, real Tesseract/Ollama behavior,
or subjective Search quality. Use the smallest relevant current manual
checklist with disposable synthetic data.

Record the operating system, architecture, commit, dependencies, locale,
filesystem/data shape, and observed result. Leave an item unchecked when it was
not actually performed. A target build is not native execution, and a CI
workflow definition is not a passing hosted run.

## Documentation

Every behavior change should update the smallest authoritative document:

- product purpose or future direction: Product Vision or Product Roadmap;
- cross-cutting engineering policy: Engineering Principles;
- user-visible workflow: current User Guide and Troubleshooting if needed;
- architecture or ownership: Architecture Overview/System Map;
- safety/privacy/persistence: Safety and Privacy;
- plugin contract: SDK, author guide, manifest/package documentation;
- release behavior: Changelog, Version Notes, Release Status, Release History,
  and manual checklist as applicable;
- public API: meaningful XML documentation.

Retain historical versioned documentation. If a document is removed, preserve
its useful information elsewhere, update inbound links, and state why.
Do not rewrite a release report, validation report, manual checklist,
implementation specification, or packaged-release document to describe current
behavior. Label planned concepts and research rather than writing them as
implemented features.

## Release workflow

Implementation, integration, automated validation, manual validation,
packaging, tagging, and publication are separate steps and must be reported
separately.

1. Start from an identified base and focused branch.
2. Update implementation, tests, migration/compatibility, and documentation
   together.
3. Run the complete repository validation plus relevant focused/manual checks.
4. Review every changed and generated artifact.
5. Merge only after the intended review and integration gates.
6. Tag, package, sign, or publish only with explicit maintainer authorization.

Do not infer a release from a version string, successful build, source branch,
or unchecked release checklist.

## Pull-request checklist

- [ ] The change has one clear purpose and respects project dependencies.
- [ ] User-file mutation still uses the Change Plan/executor boundary.
- [ ] AI, watcher, recipe, workflow, and plugin paths remain proposal-only.
- [ ] New input and persistence have explicit bounds and corruption behavior.
- [ ] Cancellation, concurrency, and failure semantics are tested.
- [ ] Debug and Release builds pass with zero warnings.
- [ ] Complete Debug and Release test suites pass with no skips.
- [ ] `dotnet format --verify-no-changes` and `git diff --check` pass.
- [ ] Documentation links and Mermaid blocks validate.
- [ ] Relevant manual scenarios were performed and recorded, or are explicitly
  reported as not performed.
- [ ] No secrets, machine-specific paths, `bin`, `obj`, logs, packages, or
  application-data files are included.
- [ ] The diff contains no unrelated formatting churn.

See the [Developer Guide](docs/DEVELOPER_GUIDE.md) for a guided first change and
the [Maintainer Guide](docs/MAINTAINER_GUIDE.md) for release responsibilities.
