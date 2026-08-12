# OmniSorSe v2.4 manual testing

**Status:** v2.4.0 release validation record

This checklist distinguishes automated evidence, Windows-native protocol
execution, maintainer-interactive work, and native packaging/platform work.
An unchecked item is not a failed test; it has not been genuinely exercised in
the stated environment.

## Automated and Windows-host evidence

- [x] Baseline v2.3.0 commit, clean worktree, synchronized `main`, tag, and Git
  integrity were verified before branching.
- [x] Baseline Release build completed with zero warnings/errors.
- [x] Baseline Release suite passed 1,637 tests with no failures/skips.
- [x] Protocol contracts contain only read-only operations and the contract
  assembly has no SQLite/provider/UI/renderer dependency.
- [x] Strict JSON rejects unknown fields and runtime type metadata.
- [x] High-entropy session/token creation, fixed-time hashed validation, expiry,
  revocation, and session-bound opaque IDs were exercised automatically.
- [x] Authorized roots, stable/paged children, traversal rejection, bounded
  neighborhoods, grounded Search, Related Files scope, and private-detail
  omission were exercised automatically.
- [x] A real Windows named-pipe request/response round trip completed using the
  production frame format and authorization boundary.
- [x] Invalid-secret and malformed-payload connection failures were isolated.
- [x] Provider cancellation reached a controlled long-running request.
- [x] A 5,000-document synthetic structural projection remained bounded.
- [x] Legacy Windows/Linux/macOS profile path selection was exercised through
  deterministic platform tests.
- [x] Final forced/no-cache restore completed for all 17 solution projects.
- [x] Final Debug and Release non-incremental builds completed with zero
  warnings and zero errors.
- [x] Final Debug and Release suites each passed 1,671 tests with zero failures
  and zero skipped/not-executed tests; totals were independently parsed from
  the generated TRX counters.
- [x] Focused Explorer Protocol (33), performance-regression (19), and
  repository documentation/dependency policy (8) suites passed.
- [x] Whitespace, style, analyzer, patch-format, and live NuGet vulnerability
  checks passed; the advisory audit reported no vulnerable direct or transitive
  packages in any solution project.

## Rename and desktop branding

- [ ] Launch the final desktop interactively and inspect title, navigation,
  About, Settings, Help, dialogs, notifications, diagnostics, and accessibility
  names for current OmniSorSe branding.
- [ ] Verify About reports OmniSorSe 2.4 and repository links still reach the
  not-yet-renamed repository.
- [x] Verify a fresh user profile starts without errors and creates data only in
  the established legacy compatibility directory.
- [x] Verify no duplicate OpenSorSe/OmniSorSe profiles appear.
- [ ] Exercise primary Scan, Search, Duplicates, Related Files, and Organize
  workflows without OmniExplorer installed.

## Genuine v2.3 profile upgrade

- [x] Create a controlled profile using the published OpenSorSe v2.3.0 Windows
  portable package and its real application services.
- [x] Record settings, sources, watched folders, schema-5 index/Search results,
  privacy/AI settings, and external-tool paths. The controlled text files were
  metadata-only in the published v2.3 pipeline, so this fixture did not contain
  Media/Content Intelligence, Change Plan, journal, or recovery rows; their
  schema-5 round trips remain covered by the existing migration/store suites.
- [x] Start the v2.4 candidate twice against that profile and verify indexed
  documents, deterministic Search, watched folders, diagnostics/privacy state,
  Ollama selection, and ffmpeg/ffprobe/Tesseract/whisper paths remain.
- [x] Confirm schema stays at 5 and migration/backup is not run for branding.
- [x] Confirm controlled source files are byte-for-byte untouched.
- [x] Exercise absent profile, invalid settings, and a pre-existing unrelated
  OmniSorSe directory without destructive overwrite. Invalid settings remained
  byte-for-byte intact and startup degraded to defaults.
- [ ] Exercise a genuine NTFS permission-denied legacy profile. This was not
  changed on the maintainer account because it risked leaving inaccessible
  test ACLs; deterministic path/configuration failure coverage remains green.

## Windows installer transition

- [x] Install the official v2.3.0 Windows setup package into a disposable
  controlled directory.
- [x] Upgrade with a native v2.4 candidate using the retained installer AppId.
- [x] Verify one Add/Remove Programs entry named OmniSorSe 2.4.0.
- [x] Verify the legacy-compatible install directory is reused,
  `OmniSorSe.exe` launches,
  and obsolete `OpenSorSe` entrypoint files are removed.
- [x] Verify the old Start Menu group/shortcut is removed and only the new
  OmniSorSe shortcut is visible.
- [x] Uninstall and confirm application data remains preserved.

## Native protocol lifecycle and security

- [x] Windows current-user named-pipe round trip using a valid session.
- [x] Missing/invalid secret is rejected before operation/version processing.
- [x] Expired and revoked sessions are rejected in deterministic tests.
- [x] Paths are omitted by default and appear only with an explicit path grant.
- [x] Unavailable OmniExplorer state has no filesystem scan or broken UI action.
- [x] Bounded concurrency and queue saturation return a stable busy response.
- [x] Launch two real processes and verify a grant can be handed off through a
  current-user-only ACL-protected disposable channel without a
  command-line token, then revoked immediately after the companion exits.
- [x] Disconnect the client mid-Search and observe prompt provider cancellation
  and clean host recovery.
- [x] Saturate bounded concurrency with 24 controlled requests: 16 completed,
  eight returned the bounded busy response, none caused a transport failure,
  and host shutdown remained clean.
- [x] Shut down the host during an active request and verify the client receives
  a predictable terminated connection, provider work is cancelled, and host
  state reaches unavailable.
- [x] Exercise actual 15-second expiry, immediate revocation, wrong/missing
  tokens, strict unknown-property rejection, and oversized-frame rejection.
- [ ] Inspect Advanced Diagnostics and confirm no token, query, path, snippet,
  OCR, transcript, or request payload appears.

## Search, Structure, and Context round trips

- [x] Root scope and out-of-scope filtering are automated.
- [x] Stable folders-first structure, paging, missing/traversal paths, and bounded
  neighborhood behavior are automated.
- [x] Unified deterministic Search ordering, known-ID grounding, AI-disabled
  fallback, explanations, and cancellation are automated.
- [x] Related Files filtering, reason/provenance projection, and bounded details
  without complete OCR/transcript/GPS exposure are automated.
- [ ] Exercise protocol reads while indexing updates/deletes controlled files and
  verify removed nodes fail safely without stale content.
- [x] Exercise Unicode, spaces, and punctuation through roots, children,
  neighborhood, Search, Related Files, and details over the native transport.
- [ ] Exercise a safe near-maximum Windows long path and controlled UNC indexed
  source. No controlled UNC environment was available.

## Native platform and packaging

- [x] Windows x64 Release runtime output launched through the production
  non-interactive package-smoke entry point and exited successfully using an
  isolated profile. File metadata reported OmniSorSe 2.4.0 / 2.4.0.0, while the
  profile was correctly created under the retained `OpenSorSe` storage name.
- [x] Release cross-target compilation passed with zero warnings/errors for
  `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64`.
- [x] Every cross-target output contained the OmniSorSe app host, Explorer
  Protocol contract assembly, one target-appropriate SQLite native asset, and
  one target-appropriate SkiaSharp native asset.
- [x] Windows x64 v2.4 portable package inspection and package-smoke launch.
- [x] Windows x64 v2.3-to-v2.4 installer upgrade/uninstall test.
- [ ] Native Linux x64 protocol round trip and source-profile compatibility.
- [ ] Native macOS x64 protocol round trip, visible bundle transition, and
  Application Support continuity.
- [ ] Native macOS arm64 protocol round trip, visible bundle transition, and
  Application Support continuity.
- [ ] Screen-reader and full keyboard traversal of affected branded surfaces.

## Evidence boundary

Automated and controlled native success does not prove the unchecked broad
interactive, permission-denied, screen-reader, long-path/UNC, or native
Linux/macOS scenarios. Cross-target compilation is not native execution.
No OmniExplorer UI, renderer, launcher, package, or external listener is part of
this checklist.

## Defects found during final native validation

- The original disconnect monitor polled `NamedPipeServerStream.IsConnected`,
  which did not promptly observe a disappeared peer during active provider
  work. It now uses a cancellable asynchronous read probe; native and automated
  regression tests observe provider cancellation promptly.
- The accept loop originally created the next named-pipe instance outside its
  failure boundary. Twenty-four simultaneous clients could exhaust the Windows
  instance limit and fault host disposal. Creation is now guarded with bounded
  retry/backoff and native saturation completes with deterministic busy
  responses.
- The original 54-character random pipe name exceeded macOS's 104-character
  full Unix-domain socket path limit after .NET added the host temp-directory
  prefix. The compact endpoint retains 128 bits of randomness in 36 characters.
- Inno Setup reused the v2.3 Start Menu group name during an AppId-compatible
  upgrade. `UsePreviousGroup=no` now preserves the installer identity while
  replacing the old group with a single visible OmniSorSe group.
