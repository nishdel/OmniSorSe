# v0.6 Implementation Decisions

## Scope selection

The roadmap does not assign v0.6. Specification 033 explicitly deferred persistent tags and saved searches, while v0.4 later supplied catalog persistence for accepted tags and v0.5 made those tags searchable. The smallest remaining workflow gap is direct user tag management: without Ollama, a user can see deterministic extension tags but cannot create a personal search label. v0.6 therefore exposes the already-modeled `UserApproved` source before introducing another search persistence feature.

## Application-owned metadata only

Tags remain associations with opaque snapshot file IDs. They are not NTFS alternate streams, extended attributes, embedded document properties, or sidecar files. This keeps the read-only user-file guarantee exact and cross-platform behavior deterministic.

## Reuse without AI coupling

The existing AI validator contains useful tag rules but is named and documented as an untrusted-provider boundary. A small `UserTagFactory` owns the equivalent user-input contract instead of making core Results editing conceptually depend on optional AI. Both paths deliberately retain the same 64-character normalized-tag behavior.

## Bounds and removal policy

Each file may have at most twelve accepted non-deterministic tags, matching the established bounded input scale. The Results UI may remove any accepted non-deterministic tag, including one accepted from an AI suggestion, because all such values are application-owned and user-controlled. Deterministic extension tags remain reproducible and cannot be removed.

## Persistence compatibility

No catalog migration is needed. Schema version one already stores accepted non-deterministic `TagAssociation` values, and the existing shell identity checks prevent stale Results state from replacing another catalog entry.
