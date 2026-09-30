# Manual-testing preparation evidence — 2026-09-29

Initial preparation scope: documentation/templates/drafts only. No application
behavior, package,
configuration or model changes; no release downloads, manual runs, broad audit,
benchmarks, authenticated callbacks, commits, pushes, tags or issue creation.

The later finalization section records separately authorized local review and
documentation commit preparation; it does not change the earlier participation history.

## Repository and publication baseline

- Actual repository: OmniSorSe remote `https://github.com/nishdel/OmniSorSe.git`,
  existing `codex/v2.13-publication-docs` worktree.
- HEAD: `23fd66deb207d5c30d076aed4c82711fe1cb9139`.
- Pre-edit tree: `91c2fff21735f52a9288f8a3e809161c2991db91`, equal to
  published v2.13.0-rc. No distinct unpublished application candidate found.
- Before this task: no staged changes; CONTRIBUTING was modified and the short
  manual guide, issue template and v2.13 draft were untracked. These task-relevant
  materials were reused and extended. Other checkouts and their work were left alone.
- Paginated GitHub Releases and tag API metadata verified on the date above:
  eight releases, six stable/two prereleases. Remote main was the published RC.
  Per-release source SHA, assets and API digests are in the drafts. No asset
  downloaded or locally rehashed. All-state issue listing had no testing issue.
- Changelog, Release History, original versioned checklists and validation
  reports were inspected; milestone dates are not publication dates.

## Initial OmniLAB availability and actual contribution

Read the supplied LocalAgentBridge checkout's README, CODEX_POLICY.md,
`docs/WORKFLOWS.md` and `docs/ADAPTIVE-DELEGATION.md`. Its README identifies
0.5.0.dev0 development and v0.4.1 stable setup. These are source documentation,
not proof of an installed active version.

The active tool catalog exposed no LocalAgentBridge/OmniLAB tools;
`codex mcp list --json` returned `[]`. The inspected user configuration contained
no LocalAgentBridge registration. No active installed runtime was verified in
the checked standard application locations/processes. The supplied checkout's
existing `source_root = "."` resolves to LocalAgentBridge, not OmniSorSe. Running
that checkout as if it were the installed runtime or changing its scope/models
would make an unsupported assumption, so neither was done.

**Actual OmniLAB contributions: none.** No inference request, request ID, model
output or workflow checkpoint is claimed. This limitation is specific to this
session; it does not claim that no installation exists anywhere on the machine.
The new agent guidance documents supported optional setup and supervision.

### Part A follow-up verification

The earlier runtime-availability conclusion above was limited to that inspection.
A focused follow-up found the newer `release-staging/companion-dev` checkout and
its README, AGENTS, contributor, quickstart/configuration, companion-workflow,
feature-preservation, migration and qualification documentation. These distinguish
the unreleased development tree from a fixed, non-editable coordinating runtime.

An existing separate installation referenced by its runtime evidence was verified:
Python **3.13.14**, LocalAgentBridge **0.5.0.dev0**, imported from the installation's
`Lib/site-packages`, not the development checkout. Read-only top-level,
`workflow --help` and `companion --help` invocations confirmed stdio `server`,
`codex-config`, caller-driven workflow actions and durable companion actions.
No running controller or bridge connected to this chat was established by those
checks; `codex mcp list --json` still returned `[]`. Installation, active connection
and actual local contribution are separate facts.

No inference, workflow job or authenticated callback was initiated by this
follow-up. No configuration, model, runtime or LocalAgentBridge file was changed.
OmniSorSe guidance now explicitly covers the supported optional development aid,
profiles/skills/advisory roles, adaptive direct/workflow behavior and separate
review/checkpoint obligations. The lack of participation does not disable or
remove that documented development option.

## Initial Codex work and verification

Codex verified Git identities/publication metadata, inspected release-specific
controls and launch instructions, drafted the reusable guide and records, and
kept source/package/test evidence distinct. A separate Codex documentation
specialist mapped old coverage. That specialist is not OmniLAB.

Checks: documentation link/anchor existence, exact table columns and status
counts, package/tag/hash comparison with fetched metadata, coverage item counts,
and staged documentation-only diff/whitespace inspection. No application tests
were run for this documentation-only change. The locally selected SDK 10.0.400
was unavailable (installed 8.0.423/9.0.316); no SDK/configuration was changed.

Historical native/provider evidence remains linked with limits; unknown human
results remain unknown. Prepared future tests remain No / Not tested. Human
execution, conditional environment assignments and package/release approval
remain for the owner.

## Finalization: actual OmniLAB review and Codex supervision

The owner subsequently authorized finalizing and committing the reviewed
Markdown changes, without publication. This phase began with 20 staged documents
and no unstaged/untracked work. The original manual-testing system was preserved.

The fixed, non-editable Python 3.13.14 / LocalAgentBridge 0.5.0.dev0 installation
was verified again, including recorded wheel SHA-256
`3fde801aa34b5769ed68185ed7dfd857825d819f3af807f49bcc798ce37bbb2c`.
A task-local MCP client used the installed interpreter with
`-I -B -m localagentbridge --config PATH server` from outside the development
checkout. The handshake exposed the seven baseline tools. No Codex registration
or companion controller was added; installation alone was not treated as a
connection. The existing configuration disables workflow tools. Direct requests
used its enabled adaptive routing, coding role, Large cap, two-attempt logical
budget, 10,000-character input limit, 600-token/3,000-byte output limits and
180-second timeout. No project workflow/checkpoint enforcement is claimed.

The existing `source_root` covers LocalAgentBridge, so Codex supplied bounded
inline staged content instead of requesting OmniSorSe paths or changing scope.
Installed `server.py`, `service.py`, `schemas.py`, `adaptive.py`, `config.py` and
the packaged MCP client example were inspected to verify this invocation.

The first guidance review failed because the configured Ollama endpoint was
unavailable (request `76661537-3d8d-4acd-844f-4a9f0bf61d30`). The documented
`ollama serve` command started the existing installation with its existing
models/configuration. A retry retained the original logical history and returned
`no_enabled_capable_model`; the router permits only a stronger retry and none was
eligible. Codex reviewed that guidance. The separate policy-wording draft below
was incomplete despite a successful response flag; its history-preserving
follow-up likewise handed back without inference. Codex drafted the small
optional-but-preferred-worker and independent-verification wording changes.

OmniLAB was the primary first-pass reviewer for this finalization: 16 bounded
review packets were delegated, yielding 14 structured reviews and two handoffs
without usable review text. Codex reviewed those failed packets. The whole guide,
issue template, routing diff, release index, current RC and next-candidate records
were supplied in bounded parts. Historical records omitted repeated execution
rows and shortened link targets; coverage review used explicit excerpts. Codex
independently checked the complete tables, links, source ordinals and metadata.
This is not a claim that the model reviewed omitted rows or performed human tests.

All returned model results used the existing `gpt-oss:20b` model, whose installed
digest was `17052f91a42e97930aa6e28a6c6c06a983e6a58dbb00434885a0cf5313e376f7`.
The scope-wording proposal was checked against the installed implementation and
integrated into AGENTS.md with clarity/Markdown edits: authorized `source_root`
is required for file-path access; supported inline tools need no scope change.

### Supervised dispositions

The table records actual returned IDs, not planned delegation. Accepted means a
verified useful proposal, bounded review or known prerequisite; corrected means
Codex corrected the assessment, not necessarily a file. Rejected findings were
not integrated. Sixteen result outcomes were recorded through feedback-only
`triage_task` calls: **7 accepted, 3 corrected, 6 rejected**. The two error packets
have no usable result to accept. Private request/response packets and decision
notes were retained locally under the finalization evidence bundle.

| Packet | Request ID | Codex disposition |
| --- | --- | --- |
| guidance-wording-draft | `2fa16cb2-7077-45b4-9ce7-731f0be795a7` | rejected |
| manual-guide-1 | `edc64f6d-dc1b-4298-8248-f4975b19c627` | rejected |
| manual-guide-2 | `c42c9e5f-1d82-4552-9c21-c8dd5baf5ea9` | accepted |
| manual-guide-3 | `53bc8138-36cb-487e-a3cb-f7a3e37a1e91` | No usable result; Codex fallback |
| template-and-routing | `31345c9c-3613-4acd-ad1b-023cdca11559` | rejected |
| release-index | `4f0e0344-f15f-4581-a0de-460cc32fcc8b` | accepted |
| current-rc-record | `3b0b25e9-ae6a-4eeb-bdb1-03584e252f5f` | accepted |
| next-candidate-record | `cbc464f9-a2b8-4f8f-bc33-3eb9f5b1c0f6` | accepted |
| historical-records-1 | `1407e16a-2fdf-4c67-a4be-dad5ed00e47a` | rejected |
| historical-records-2 | `3c6c0033-9d70-4c12-9a23-e7e6addba773` | corrected |
| historical-records-3 | `9665e538-8ec6-4b80-bf2c-5c83eaea5210` | accepted |
| historical-records-4 | `a7955d6c-10f1-4e14-b75b-1e71b3745b78` | accepted |
| historical-records-5 | `e30fce70-06cb-440e-9cb5-bc28a411b760` | corrected |
| historical-records-6 | `eff301f1-c020-4cb1-acec-5fb6cc6c29f5` | corrected |
| historical-records-7 | `7b52f95d-9dc8-43f2-9cd3-c63e5f7b34a4` | rejected |
| coverage-policy | `483b5335-ccda-4002-aa2f-45c272a3073b` | rejected |
| participation-records | `8e361ba5-b2df-494b-bfbd-b22dadac8a8c` | No usable result; Codex fallback |
| inline-scope-draft | `087b6191-7495-460f-8613-9da55772c90f` | accepted |

- Accepted: inline/path scope clarification; bounded reviews with no actionable
  issue; existing requirements to pin definition links and obtain actual human
  evidence before claiming test completion. Those publication/execution
  prerequisites remain pending, not newly completed work.
- Corrected: historical-review assessments conflated a missing past observation
  with a new unperformed test, or publication classification with proof of QA.
  The complete files distinguish these states and have matching row counts.
- Rejected: executable-name changes ignore the verified v2.3/v2.4 distinction;
  alleged missing rows/IDs/assets rely on omitted excerpts or speculation;
  anchor/count/rendering allegations do not match the full documents; the first
  wording draft ends mid-sentence. No such claim justified changing test cases
  or results.

Codex integrated only the guidance changes and phase-specific provenance notes,
then independently validated the final documents. All 15 other staged files,
including every test definition, template, release draft and coverage map, stayed
byte-for-byte unchanged from this phase's baseline. Checks covered relative and
immutable source links/anchors, table columns and result counts, eight releases'
source SHAs/dates/classifications/asset-hash pairs against retained API metadata,
130 current and 906 older checkbox ordinals, and 225 v1.0 plus 55 v0.9.1 numbered/
bullet/model definitions. Documentation-only scope and whitespace checks apply;
no build, package smoke or human test result is claimed.

The runtime/configuration hashes and installed model-name/digest inventory were
unchanged. Every task-local MCP session closed; the task-owned Ollama server and
its console host stopped, leaving no listener on its port. No installation,
model download, runtime upgrade, authenticated callback, issue publication,
push, tag, release or installer rebuild occurred. Human execution, fixture/owner
assignment and publication remain separate owner work. Part A's optional aid and
supervision guidance is satisfied; local participation is now recorded for this
finalization without rewriting the earlier zero-contribution phases.
