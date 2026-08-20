# OmniSorSe to OmniBrille companion handoff

**Status:** unreleased v2.5 implementation contract

## Boundary

OmniBrille is an optional separately installed process. OmniSorSe owns indexed
data, Explorer Protocol host lifecycle, authorization scope, and launch.
OmniBrille owns visualization and interaction. Neither product accesses the
other's database, and OmniSorSe has no production dependency on OmniBrille
assemblies or source code.

Explorer Protocol v1 remains the data contract. Its operations, DTOs, version,
named-pipe transport, authentication, opaque node IDs, limits, and read-only
scope are unchanged. Existing Protocol v1 clients and test harnesses do not
need to implement this desktop bootstrap.

## Discovery

Discovery is on-demand and stops at the first reviewed executable:

1. the absolute path saved in Settings;
2. the explicit `OMNISORSE_OMNIBRILLE_PATH` override;
3. the OmniSorSe application directory;
4. bounded conventional installed locations for the current platform;
5. the current `PATH` entries.

There is no recursive search, download, updater, startup probe, or requirement
that OmniBrille be installed. A configured but missing path is reported as a
misconfiguration rather than silently falling through to another executable.

## Bootstrap contract version 1

OmniSorSe uses the launch seam already implemented and documented by
OmniBrille Stage 4. It creates one random current-user-only named pipe, starts
the reviewed executable with `UseShellExecute=false`, and supplies only:

- `--omnisorse-handoff <one-time-pipe-name>`.

The random 128-bit pipe suffix is an unguessable rendezvous identity, not a
bearer credential. The parent accepts at most one connection and writes one
length-prefixed strict-JSON `ExplorerSessionGrant`, capped at 4 KiB:

- transport and random Protocol v1 endpoint;
- session ID and bearer token;
- absolute session expiry;
- Protocol v1 major and minor versions.

No SQLite path, index path, raw authorized root, source file path, persistent
credential, or Explorer node ID is included. The bearer material is never put
on the command line or in a durable file.

OmniBrille connects to the handoff pipe, validates the grant, then connects to
the supplied Explorer endpoint, authenticates, and negotiates Protocol v1.
Unknown JSON properties and oversized frames are rejected. The one-way handoff
pipe closes after its single frame. OmniSorSe treats only the exact session's
first authenticated request with a compatible Protocol v1 major as successful
acknowledgement; a wrong token or incompatible major cannot acknowledge launch.

## Lifecycle and failure

The Explorer host remains dormant until the user invokes **Open in
OmniBrille**, a companion is found, and at least one enabled indexed source can
be authorized. The session contains only those source IDs and does not grant
raw paths. Handoff plus authenticated-use acknowledgement is bounded to 15
seconds.

Launch failure, handoff timeout, early process exit, or absence of compatible
authenticated use revokes the newly issued session. The established one-way
OmniBrille receiver does not send a separate failure-detail frame. OmniSorSe can
still attribute a request carrying the issued session ID to authentication
rejection or an incompatible protocol major; exit/no-request cases remain a
bounded generic connection failure rather than a guessed category. A successful
session remains bounded by the existing Protocol v1 15-minute maximum and is
revoked when the observed companion process exits. Closing OmniBrille never
closes OmniSorSe; OmniSorSe shutdown retains the existing host-wide secret
revocation behavior. Repeated launches use independent handoff endpoints,
sessions, and bearer tokens.

## Threat model

The bootstrap avoids durable files, environment secrets, and command-line
bearer tokens. The random handoff endpoint is visible to local process
inspection, while launch material is framed once over a current-user-only pipe.
Protocol v1 continues
to enforce current-user-only local transport, authenticated requests, bounded
scope, strict JSON, opaque IDs, and no write operations.

This does not defend against a fully compromised process running as the same
operating-system user; such a process could inspect the endpoint argument and
race the intended child. That residual local same-user risk already exists for
desktop process inspection and input injection. Random endpoint names,
current-user isolation, one accepted connection, a 15-second bound, and
immediate channel disposal reduce exposure. The design prevents replay through
a retained launch file or bearer-token command line, cross-user pipe access,
arbitrary-root authorization, network exposure, and reuse of a completed
handoff channel.

## Non-goals

The handoff adds no OmniBrille renderer or Context mode, relationship IDs,
Explorer Protocol v2, HTTP/TCP/WebSocket listener, direct SQLite access,
companion auto-update, or process/window manager. OmniSorSe remains fully usable
when OmniBrille is absent.
