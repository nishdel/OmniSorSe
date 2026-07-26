# v0.4 Implementation Decisions

## Catalog format

The catalog uses versioned JSON rather than SQLite. This is deliberately a narrow transition from process-local results, not implementation of the broader Database architecture. It avoids a package dependency and is easy to inspect, test, and remove through future explicit controls.

## Privacy and storage

Catalog persistence is off by default because result paths and filenames are personal metadata. Once enabled, storage is confined to the existing OpenSorSe local-application-data directory. Catalog entries contain no file bytes or raw SHA-256 values. OpenSorSe never writes beside selected folders.

## Bounded behavior

Entries retain whole snapshots or are not stored; they are never silently truncated. The store keeps ten newest entries and rejects snapshots above 2,000 files. This preserves an honest read-only review while preventing unbounded application-data growth.

## Failure policy

Malformed catalogs are preserved and considered unavailable. We do not auto-delete or overwrite them because that would destroy application data and mask a diagnosis. A later retention-management release can add explicit user-authorized deletion.
