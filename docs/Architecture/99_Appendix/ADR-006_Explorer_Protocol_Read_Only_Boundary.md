# ADR-006: Local Scoped Read-Only Explorer Protocol Boundary

| Field | Value |
| --- | --- |
| Status | Accepted in the current architecture; reconstructed from source and Git history |
| Effective history | Explorer Protocol v1 introduced in v2.4; OmniBrille handoff completed in v2.5 |
| Reconstruction date | 2026-08-18 |
| Decision owners | OmniSorSe maintainers |

## Context

OmniBrille is a separate optional visual companion. It needs bounded structural, Search, Related Files, media, and content-intelligence projections without becoming an owner of OmniSorSe persistence or gaining an implicit filesystem-mutation channel. The integration also needs to remain absent from normal startup and harmless when no companion is installed.

This boundary was reconstructed from commits `a5863d3` and `59be07c`, the current protocol/transport/launcher source, and protocol tests. This ADR records the implemented contract rather than asserting undocumented historical alternatives.

## Decision

OmniSorSe exposes an independently versioned, visualization-neutral Explorer Protocol over an on-demand local named-pipe transport.

- Protocol contracts live in the standalone `OmniSorSe.ExplorerProtocol` assembly and declare protocol version, capabilities, hard limits, request/response DTOs, and stable privacy-safe errors.
- The protocol is read-only. It provides negotiation, authorized roots, structural children/neighborhoods, unified deterministic-first Search, Related Files, and bounded node details.
- `ExplorerDataSource` adapts the existing background index, Search, and relationship services. The protocol does not read SQLite directly or create a companion-specific index.
- A session grants exact enabled source IDs for an absolute lifetime of at most 15 minutes. Production OmniBrille launch does not include authorized filesystem paths in the grant.
- Authorization tokens are random, retained only as hashes, compared in fixed time, revocable, and zeroed on revocation. Node IDs are opaque session-secret HMAC identities rather than persistence keys or raw paths.
- The host starts lazily when an explicitly authorized launch requests a session. Bootstrap uses a one-time local handoff, waits for a bounded acknowledgement, and revokes unsuccessful sessions.
- OmniBrille remains optional. Discovery probes only documented bounded locations on demand; absence or launch failure does not affect OmniSorSe Search, indexing, or startup.

## Consequences

- OmniBrille can visualize current authorized projections but cannot approve a Change Plan, mutate files, modify persistence, or become a source of truth.
- Protocol consumers receive bounded explainable provenance, including distinctions between deterministic, derived, and user-authoritative relationships.
- The server enforces a 64 KiB request frame, 1 MiB response frame, 256 nodes, 512 edges, depth 2, 100 Search or Related results, four concurrent requests, a 16-request queue, and a 15-second operation timeout.
- Failures return stable categories without exception, stack, path, or database detail.
- Protocol compatibility follows major/minor negotiation; a breaking contract change requires a new major version and coordinated companion work.
- `IExplorerCompanionPresence` remains a compatibility abstraction registered as unavailable, while current readiness and launch use `IExplorerCompanionLocator`. It is not an authority and should not be used to infer launchability.

## Alternatives considered

The surviving source and history do not establish a reliable complete alternatives record. No rejected alternatives are reconstructed here.

## Evidence

- `src/OmniSorSe.ExplorerProtocol/ExplorerProtocolContracts.cs`
- `src/OpenSorSe.Application/Explorer/ExplorerReadService.cs`
- `src/OpenSorSe.Application/Explorer/ExplorerSessionSecurity.cs`
- `src/OpenSorSe.Application/Explorer/ExplorerProtocolTransport.cs`
- `src/OpenSorSe.Application/Explorer/ExplorerCompanionLaunch.cs`
- `tests/OpenSorSe.Application.Tests/ExplorerProtocolTests.cs`
- `tests/OpenSorSe.Application.Tests/ExplorerCompanionLaunchTests.cs`
- [Explorer Protocol transition](../../OMNISORSE_TRANSITION_AND_EXPLORER_PROTOCOL_v2.4.md)
- [OmniBrille companion handoff](../../OMNIBRILLE_COMPANION_HANDOFF_v2.5.md)
