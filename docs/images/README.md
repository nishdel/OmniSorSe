# OmniSorSe public media assets

This directory owns public documentation imagery. The tracked
`opensorse-logo.png` is the current application mark; its retained filename is a
compatibility detail. The [README gallery](../../README.md#v3-screenshot-gallery)
now contains seven reviewed real v3 captures. No v2 capture or usage video is
presented as v3 evidence.

## v3 capture provenance

The complete set is in [screenshots/v3.0.0-rc.1](screenshots/v3.0.0-rc.1).
The unmodified [manifest](screenshots/v3.0.0-rc.1/manifest.json) records per-image
SHA-256 values, exact synthetic-fixture hashes, actual indexing/search/proposal
facts, and successful source-integrity checks before and after cleanup.

| Property | Recorded value |
| --- | --- |
| Product | v3.0.0-rc.1 candidate; production source build |
| Published production source | `df3984fab5eaf94424ec6cd032e91468799d62d6` |
| Capture harness source | `1ea2569b8c54aa6496517b33c23eeb7d9d991f8a` |
| Native capture run | [37777001533](https://github.com/nishdel/OmniSorSe/actions/runs/37777001533), 2026-10-08 |
| Platform | Ubuntu 24.04.5 LTS, x64, .NET 10.0.12, X11 under Xvfb |
| Appearance | Light theme, 1600×1000 native pixels, 100% scaling |
| Inputs | Nine committed synthetic UTF-8 documents, including one identical pair |
| Models | AI enrichment and learned embeddings explicitly disabled; no fabricated model output |
| Original files | Same path set and SHA-256 values before and after the run; no Apply invoked |
| Image processing | Native X11 window capture with ImageMagick `import -strip`; no resizing, compositing or retouching |

The [capture harness and reproduction instructions](../../eng/screenshots/README.md)
use the production Avalonia `MainWindow`, composition root, view-models and
services in a fresh isolated profile. The workflow checks that production files,
build inputs and the existing RC tag match the recorded production commit. It
drives real scan, text extraction, Search, relationship analysis and Organize
proposal commands. This is a Linux source preview, not a Linux release package
or an installed Windows/macOS capture.

Avalonia documents [X11 with Xvfb](https://docs.avaloniaui.net/docs/deployment/docker)
for its Linux desktop backend; ImageMagick documents native X server/window
capture through [import](https://imagemagick.org/import/). This supported path
replaced the earlier failed Windows app-access/capture attempts. It does not use
Avalonia's headless test renderer or generated UI images.

## Reviewed views and boundaries

| Image | What is actually visible |
| --- | --- |
| [Search](screenshots/v3.0.0-rc.1/01-search.png) | The real `camping` query, first-ranked sample checklist, filename evidence and complete indexing coverage. |
| [Ranking explanation](screenshots/v3.0.0-rc.1/02-search-ranking.png) | The expanded first-result explanation, scrolled into the results viewport. The harness asserts the full explanation body is visible. |
| [Rules](screenshots/v3.0.0-rc.1/03-rules.png) | Two synthetic in-memory inputs in the existing review-only Sorting Recipes surface. They were added after scanning and never executed. |
| [Organize](screenshots/v3.0.0-rc.1/04-organize.png) | Current/recommended trees for five selected files; one eligible move and unresolved rows. This is a proposal, not an approved or applied plan. |
| [Duplicates](screenshots/v3.0.0-rc.1/05-duplicates.png) | The real exact-hash budget pair. No file is selected for removal. |
| [Related Files](screenshots/v3.0.0-rc.1/06-related-files.png) | The production DocumentSet filter shows retained identical-content evidence for the budget copy; the separate semantic-similarity panel reports disabled. |
| [AI/indexing status](screenshots/v3.0.0-rc.1/07-ai-indexing-status.png) | Nine completed documents, zero failed/skipped/remaining; learned semantic Search is disabled. |

All seven PNGs were inspected at original resolution. Their only PNG chunks are
IHDR, IDAT and IEND; metadata was stripped during capture. The first native run
captured an expanded ranking header with its explanation below the viewport;
that set was rejected. The committed set comes entirely from the corrected run.

The fixture's repeated wording and timestamps can produce broad automatic
relationships. These images establish the displayed workflow, not ranking
quality, model inference, accessibility, installer behavior or human usability.
All 24 [human acceptance rows](../MANUAL_TESTING_v3.0.md) remain **Not run**.
The published RC tag and release assets are unchanged by this follow-up.

## Future capture rules

Use a disposable profile and synthetic inputs only. Keep versioned sets separate,
record exact product and harness/package identities, and identify the version in
each gallery's introduction. Inspect every image at original resolution, verify
its hash, strip metadata, and give it meaningful alt text. Never substitute an
older interface, a mockup or an AI-generated image for a real application capture.
App access and successful startup alone are not capture evidence.

The historical [v2.0 screenshot checklist](../SCREENSHOT_CHECKLIST_v2.0.md) remains
release evidence. A future real 60–90 second captioned video could show scanning,
Search, Related Files, a reviewed Change Plan and a disposable Apply/Undo flow.
It must identify its exact source/package and use an approved versioned media
host rather than adding a large GIF or MP4 to source history. OmniBrille is a
separate companion and must not appear to be bundled here.
