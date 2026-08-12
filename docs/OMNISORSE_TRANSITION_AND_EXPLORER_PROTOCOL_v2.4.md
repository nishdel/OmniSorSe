# OmniSorSe v2.4 transition and Explorer Protocol v1

**Status:** released as OmniSorSe v2.4.0 from `v2.4-omnisorse-transition`

**Stable baseline:** OpenSorSe v2.3.0, commit
`abe43e171bdcefa48cc55a6af6e560e2c8c8ce94`, schema 5

This document is the authoritative design and compatibility record for the
OpenSorSe-to-OmniSorSe product rename and the local read-only boundary intended
for a future, separately distributed OmniExplorer application.

## Product boundary

OmniSorSe is the core application. It owns scanning, indexing, unified Search,
Media and Content Intelligence, duplicates, Related Files, Collections,
Change Plans, organization, privacy controls, and persistent state.

OmniExplorer is an optional future application. It is not implemented, bundled,
downloaded, detected through filesystem scans, or presented as available in
v2.4. The protocol adds no renderer, layout engine, GPU dependency, visual
assets, voice feature, or standalone scanner. Normal OmniSorSe startup does not
instantiate the protocol host unless a future explicit launch flow requests a
session.

## Rename and compatibility decisions

Active desktop identity changes to **OmniSorSe**:

- window, navigation, About, Settings, Help, diagnostics, accessibility, and
  error text;
- Windows apphost `OmniSorSe.exe`, product metadata, installer display name,
  and Start Menu shortcut;
- `OmniSorSe.app`, visible macOS bundle fields, DMG name, and executable;
- future release artifact/checksum names and CI packaging inputs.

The following compatibility identities deliberately remain unchanged:

- internal `OpenSorSe.*` project names and namespaces;
- solution/project paths and persisted type/schema contracts;
- Windows `%LOCALAPPDATA%\OpenSorSe` data/configuration/cache/log/plugin root;
- Linux `opensorse` XDG subdirectories and `.opensorse` conservative fallback;
- macOS `Application Support/OpenSorSe`, `Caches/OpenSorSe`, and `Logs/OpenSorSe`;
- Windows installer AppId `{3F3BCA7E-38A1-45D3-B068-B22D25BCECF4}` and legacy
  default install directory `%LOCALAPPDATA%\Programs\OpenSorSe`;
- macOS bundle identifier `io.github.nishdel.OpenSorSe`;
- the current GitHub repository URL until the repository is actually renamed;
- historical release names, tags, artifacts, notes, frozen snapshots, and
  version-specific evidence.

This **compatibility-in-place** strategy performs no data migration and creates
no competing OmniSorSe profile. Existing schema-5 databases, settings, indexed
sources, watched folders, AI/privacy/media/content settings, external-tool
paths, operation journals, Change Plans, recovery data, and appropriate caches
are opened at their established locations. Source files are never touched by
the rename. Schema remains 5, and the v2.3 indexing processor fingerprint stays
unchanged because branding/protocol projection does not alter indexed evidence.

The Windows installer retains its AppId so v2.4 is an in-place product upgrade.
It explicitly removes the obsolete v2.3 `OpenSorSe` apphost/managed entrypoint
files and Start Menu group before installing the OmniSorSe entrypoint. Uninstall
continues to preserve application data under the established policy. On macOS,
visible identity changes without changing the bundle or data identity.

## Explorer Protocol v1

Protocol version **1.0** is independent of OmniSorSe application version 2.4.0.
Breaking protocol changes require a new major version; additive behavior is
advertised through capabilities and limits.

### Contract boundary

`src/OmniSorSe.ExplorerProtocol` is a small dependency-free `net8.0` assembly
containing only versioned DTOs, enums, capabilities, operations, limits, and
stable error contracts. It does not reference SQLite, Search implementations,
indexing providers, file-operation services, UI, or rendering code.

OmniSorSe implements these contracts through the provider-neutral
`IExplorerDataSource`. A future OmniExplorer client can consume the contract
assembly (or a future schema/package derived from it) without knowing any
database schema. Publishing a NuGet package is deliberately deferred until a
real companion repository needs a distribution contract.

### Capabilities

Protocol info reports application/protocol versions, transport, read-only state,
hard limits, and these capabilities:

- Structure, Search, Context, and Related Files;
- Media Intelligence and Content Intelligence;
- OCR, Transcripts, Topics, Entities, and Summaries.

Capabilities describe retained evidence that may be projected; they do not
activate providers or promise that every indexed file has every evidence type.

### Read-only operations

| Operation | Semantics |
| --- | --- |
| `GetProtocolInfo` | Negotiated version, capabilities, transport, and limits. |
| `GetAccessibleRoots` | Only explicitly authorized configured index sources. |
| `GetChildren` | Stable folders-first containment with bounded paging. |
| `GetNeighborhood` | Bounded structural depth with optional retained context. |
| `Search` | Existing unified deterministic-first Search, grounded to authorized indexed file IDs. |
| `GetRelated` | Existing medium-or-strong Related Files evidence with bounded reasons/provenance. |
| `GetNodeDetails` | Bounded metadata, concepts, summary, media facts, and relationship summaries. |

There are no write, delete, rename, move, create-directory, Change Plan,
duplicate-removal, settings, source, index-clearing, arbitrary-read, arbitrary
path enumeration, or generic-command operations.

Structure means indexed containment. Context means OmniSorSe-derived retained
relationships. The protocol does not invent graph state or grant file authority.

### DTO and evidence limits

Nodes use session-bound opaque HMAC identifiers and may expose name, kind,
parent, extension, size, bounded safe metadata, child/relationship counts, and
an authorized path only when the session separately enables path projection.
Details intentionally omit complete documents, complete OCR, complete
transcripts, precise GPS coordinates, binary media, prompts, and diagnostics.

Edges use a small taxonomy (`Contains`, `Related`, `Topic`, `Entity`,
`Temporal`, `Ocr`, `Transcript`) plus bounded strength, reason, provenance, and
structural/deterministic/derived classification. Exact file identity always
comes from indexed records. Unknown or ungrounded Search identities are dropped.

Server-enforced v1 limits include:

- 64 KiB request and 1 MiB response frames;
- 512 query characters, 240-character snippets, and 256-character reasons;
- 256 nodes, 512 edges, 100 Search results, and 100 Related Files results;
- depth 2, 12 topics, and 12 entities;
- 20,000 documents examined by one structural projection;
- four concurrent requests, sixteen queued requests, and a 15-second timeout;
- maximum 64 authorized sources and bounded identifiers/tokens.

Unsafe counts are clamped; malformed continuations, identifiers, payloads, and
oversized frames are rejected. Large child sets return total/truncation and an
opaque continuation token. Future aggregation can be additive without changing
the core node/edge model.

## Transport decision

The selected transport is an on-demand .NET named pipe. On Unix hosts the .NET
implementation is Unix-domain-socket-backed. On Windows the server requests
`CurrentUserOnly`. The endpoint name is unpredictable, process-local session
material is required, and no TCP socket, loopback HTTP server, LAN listener,
discovery service, internet endpoint, or cloud relay is created.

The endpoint carries 128 bits of randomness in a compact 36-character name so
the complete Unix-domain socket path remains within the macOS platform limit.

Alternatives were rejected for v1:

- loopback HTTP still exposes a network listener and needs additional binding,
  origin, authentication, and server lifecycle hardening;
- gRPC adds a server stack, generated tooling, and package weight unjustified by
  seven bounded read operations;
- direct SQLite reads couple a future client to schema, locking, recovery, and
  privacy internals and are prohibited;
- ad-hoc files cannot provide safe cancellation, authentication, or current
  scope semantics.

## Authorization and lifecycle

A future explicit launcher supplies a bounded set of configured source IDs and
optionally authorizes full-path projection. OmniSorSe then:

1. validates the sources against current configured indexed roots;
2. starts the otherwise dormant local host;
3. creates a random 128-bit session ID and 256-bit bearer secret;
4. stores only a SHA-256 token hash and compares it in fixed time;
5. creates an independent random HMAC key for opaque node IDs;
6. returns the token once in a launch grant without command-line persistence;
7. enforces a five-minute default absolute expiry (15 seconds to 15 minutes);
8. revokes and zeroes token/node secrets on revoke, expiry, or shutdown.

Lifecycle states are unavailable, waiting, authorized, connected,
incompatible, disconnected, expired, and shutting down. Client disconnect,
timeout, process failure, cancellation, or application shutdown is isolated
without modal error spam. The current companion-presence implementation reports
unavailable and exposes no broken launch action.

## Threat model and controls

| Threat | Control |
| --- | --- |
| Malicious local process | Current-user-only pipe where supported, unpredictable endpoint, high-entropy short-lived authorization, source scope. |
| Stale/stolen token | Absolute expiry, revocation, no persistence/logging, secret zeroing, session-bound node IDs. |
| Unauthorized enumeration | No global filesystem roots, no arbitrary paths, configured-source validation, opaque IDs, non-disclosing out-of-scope failures. |
| Scope escalation/path traversal | Client never supplies filesystem paths; indexed relative paths are normalized and traversal segments rejected. |
| Request denial of service | Frame/string/result/depth/document bounds, four-worker semaphore, bounded queue, timeout, cancellation. |
| Repeated expensive Search | Existing bounded Search service, request admission, provider cancellation, no AI assistance. |
| Malformed/hostile payload | Strict JSON, unknown-member rejection, depth cap, string enums only, no runtime-type deserialization. |
| Protocol downgrade | Authorization precedes major-version negotiation; incompatible majors receive a stable error. |
| Diagnostic leakage | No secrets, queries, paths, snippets, transcripts, OCR, or payloads are logged; only operation/state/count/timing/failure category is retained when diagnostics are enabled. |
| Accidental external binding | Transport contains no TCP/IP listener. |

## Cancellation, backpressure, and UI isolation

Every provider call receives the request token. A cancellable asynchronous pipe
read probe detects peer disconnect and cancels unnecessary work promptly. The
accept loop also retries boundedly when the operating system's pipe-instance
limit is temporarily saturated rather than faulting host shutdown. The
dispatcher combines caller cancellation with the hard timeout, bounds
queued/concurrent work, and isolates each connection failure.
All transport/index calls are asynchronous; no protocol request is dispatched
through the Avalonia UI thread. OmniSorSe startup, Search, indexing, and shutdown
do not depend on OmniExplorer.

## Stable errors

Protocol errors are `Unauthorized`, `SessionExpired`, `UnsupportedProtocol`,
`CapabilityUnavailable`, `NodeNotFound`, `OutOfScope`, `RequestTooLarge`,
`LimitExceeded`, `MalformedRequest`, `Cancelled`, `TemporarilyUnavailable`, and
`InternalFailure`. Responses never contain a stack trace, SQL text, table name,
database path, exception message, or secret.

## Privacy and clearing

The protocol reads current OmniSorSe-owned indexed projections only. It neither
opens arbitrary files nor adds persistence. Existing Forget/Clear operations
remain authoritative; once indexed evidence is removed, subsequent protocol
reads cannot return it. No cloud or remote transport exists. A future client
receives only its authorized bounded response and must not treat derived
evidence as objective truth.

## Repository rename checklist

The repository is still hosted at the OpenSorSe URL. At an actual repository
rename, the maintainer should:

1. rename the GitHub repository and verify redirect behavior;
2. update `origin`, README/About/installer/support/update URLs, badges, clone
   commands, issue templates, workflows, and package source metadata;
3. keep historical release/tag links valid through redirects or explicit
   archival references;
4. run documentation-link, release-workflow, source-build, and package checks;
5. avoid combining repository renaming with internal namespace or profile-path
   migrations.

## Known limitations and deferred work

- OmniExplorer is not implemented or released.
- Protocol v1 is read-only and has no remote/LAN mode.
- No companion launcher or hand-off UI is shown until a reviewed companion
  installation contract exists.
- No native Linux/macOS protocol runtime validation is claimed by Windows-host
  cross-compilation.
- The Windows installer upgrade was validated from the published v2.3.0
  installer with profile retention. macOS visible naming and both architectures
  are validated by the native packaging workflow; a real macOS user-profile
  upgrade was not performed.
- Internal project/namespace names, app-data paths, installer AppId/install
  directory, macOS bundle ID, asset filenames, and repository URL retain the
  OpenSorSe identity intentionally for compatibility.
- There is no graph renderer, voice interface, standalone Explorer scanner,
  remote protocol, cloud relay, or write operation in this repository.
