# Release-testing index

**v3 preparation, 2026-10-05:** the next major candidate is `v3.0.0-rc.1`.
[Live testing issue #53](https://github.com/nishdel/OmniSorSe/issues/53) owns its
human results, all currently Not run. The [v3 checklist](../MANUAL_TESTING_v3.0.md)
and [frozen preparation draft](3.0.0-rc.1-issue-draft.md) cover the new workflows.
The exact installer/source/hash will be assigned after release gates. The
September inventory and historical package observations below remain dated evidence.

Verified **2026-09-29** against the paginated [GitHub Releases API](https://api.github.com/repos/nishdel/OmniSorSe/releases?per_page=100), [tag API](https://api.github.com/repos/nishdel/OmniSorSe/tags?per_page=100), local tags, [Release History](../../RELEASE_HISTORY.md), [Changelog](../CHANGELOG.md) and retained versioned manual/validation reports. Eight non-draft releases were returned: **six stable, two prereleases**. Latest stable is **v2.4.0**; latest published prerelease is **v2.13.0-rc**. Recheck at the next preparation; this is a dated inventory, not a timeless latest-version constant.

| Release | Classification | Source/package identity | Test-record link |
| --- | --- | --- | --- |
| [v1.0.0](https://github.com/nishdel/OmniSorSe/releases/tag/v1.0.0) | Published stable | `7b242dc80a3c`; `OpenSorSe-v1.0.0-win-x64.zip` (+ platforms in draft) | [Draft](1.0.0-issue-draft.md) |
| [v2.0.0](https://github.com/nishdel/OmniSorSe/releases/tag/v2.0.0) | Published stable | `d353ce2fb5c9`; `OpenSorSe-v2.0.0-win-x64.zip` (+ platforms in draft) | [Draft](2.0.0-issue-draft.md) |
| [v2.1.0](https://github.com/nishdel/OmniSorSe/releases/tag/v2.1.0) | Published stable | `cf593a3b6736`; `OpenSorSe-v2.1.0-win-x64.zip` (+ platforms in draft) | [Draft](2.1.0-issue-draft.md) |
| [v2.2.0](https://github.com/nishdel/OmniSorSe/releases/tag/v2.2.0) | Published stable | `68e12fe2735f`; `OpenSorSe-v2.2.0-win-x64.zip` (+ platforms in draft) | [Draft](2.2.0-issue-draft.md) |
| [v2.3.0](https://github.com/nishdel/OmniSorSe/releases/tag/v2.3.0) | Published stable | `abe43e171bdc`; `OpenSorSe-v2.3.0-win-x64.zip` (+ platforms in draft) | [Draft](2.3.0-issue-draft.md) |
| [v2.4.0](https://github.com/nishdel/OmniSorSe/releases/tag/v2.4.0) | Published stable | `40552b9b2b18`; `OmniSorSe-v2.4.0-win-x64.zip` (+ platforms in draft) | [Planned retrospective](2.4.0-issue-draft.md) |
| [v2.12.0-rc](https://github.com/nishdel/OmniSorSe/releases/tag/v2.12.0-rc) | Published prerelease | `4dd27d62fc4e`; `OmniSorSe-v2.12.0-rc-win-x64.zip` (+ platforms in draft) | [Draft](2.12.0-rc-issue-draft.md) |
| [v2.13.0-rc](https://github.com/nishdel/OmniSorSe/releases/tag/v2.13.0-rc) | Published prerelease | `05f2972f3def`; `OmniSorSe-v2.13.0-rc-win-x64.zip` (+ platforms in draft) | [Live testing issue](https://github.com/nishdel/OmniSorSe/issues/51) · [Frozen draft](2.13.0-rc-issue-draft.md) |
| v3.0.0-rc.1 | Implementation under final validation; unpublished | Exact release package/hash pending | [Live testing issue](https://github.com/nishdel/OmniSorSe/issues/53) · [Checklist](../MANUAL_TESTING_v3.0.md) |

The worktree HEAD differs from the published tag commit, but both pre-edit trees are `91c2fff21735f52a9288f8a3e809161c2991db91`. A merge commit or a documentation change does not invent an unpublished product version. GitHub `main` resolved to the published RC commit at verification. v0.x, v1.1–v1.9 and v2.5–v2.11 documentation describe implementation milestones; no separate GitHub Releases or tags for them were returned. v1.6's retained maintainer smoke evidence stays attached to that milestone, not a fabricated public release or a later binary.

## Evidence limits

All published packages have GitHub asset SHA-256 metadata recorded in their drafts; no binaries were downloaded, installed, rebuilt or retested. Original human environments, dates, testers and broad outcomes are generally **Unknown / Not recorded**. The v2.2–v2.4 checked native/provider/installer reports are linked with their scope; they do not pass human overview rows. Historical issue reports retain their author/report date and missing exact-package attribution. A closed bug is not a passed retest. The latest-stable draft plans a new retrospective run; earlier historical drafts record missing evidence as Unknown / Not recorded.

The eight published rows each have a separate issue-body draft. The [v2.13.0-rc testing issue](https://github.com/nishdel/OmniSorSe/issues/51) now owns its live results; its checked-in draft is frozen preparation. The other release drafts remain preparation material. Never copy live results back into these files. When publishing another draft, replace its record link here with the one authoritative issue and retain the draft as frozen preparation.

## First test and later issue creation

1. Open the chosen draft: [stable v2.4.0](2.4.0-issue-draft.md) or [published v2.13.0-rc](2.13.0-rc-issue-draft.md). Assign an owner and environment; follow [Start testing here](../MANUAL_RELEASE_TESTING.md#start-testing-here), beginning with OS-M01. The next-candidate shell must first receive a real source/package/hash.
2. Before execution, have the owner publish the reviewed documentation through the normal repository workflow. Replace draft `blob/main` links with that immutable documentation commit. Confirm exact package and full checksum; select applicable [conditional/legacy coverage](coverage-map.md), one focused row per case. For a historical retest label it Retrospective with its actual date; do not overwrite the original unknown evidence.
3. Search [all issues](https://github.com/nishdel/OmniSorSe/issues?q=is%3Aissue) for “Release manual testing” plus the release. Reuse an existing issue if present. Otherwise choose **New issue → Release manual testing** after this template is published, use title `Release manual testing — VERSION`, and replace the body with its prepared draft. Remove the preparation-only banner and assign owner/environment/definition revision. Click **Submit new issue** only when the owner chooses to publish it. Do not open one issue per test or development task.
4. Put the issue link into this index. Testers update that issue's table and evidence comments only. If the owner wants to start before publication, preserve dated observations in a temporary local note and transcribe them into the issue; do not turn the checked-in draft into a live status copy.
5. Preserve each failed attempt and each older package table before retesting or changing candidate. Update counts and affected IDs; prior passes stay bound to their original hash. Automated results and owner release decisions stay separate.

[Optional OmniLAB development aid](../../AGENTS.md#optional-omnilab-development-aid) · [Preparation evidence](preparation-2026-09-29.md)
