# OmniSorSe public media assets

This directory owns public documentation imagery. The tracked
`opensorse-logo.png` is the genuine current application mark; its retained
filename is a compatibility detail. No real application screenshot, GIF, or
usage video is currently approved in the repository.

The v3.0.0-rc.1 screenshots are a required incomplete deliverable. Native capture
timed out, including two retries with the visible rebuilt application window;
no screenshot has been captured. Startup smoke and source review do not satisfy
this requirement. Keep the gallery absent until genuine captures are reviewed.

Do not use mockups, AI-generated UI, design-tool previews, or an older interface
as evidence of the current product. The historical
[v2.0 screenshot checklist](../SCREENSHOT_CHECKLIST_v2.0.md) remains preserved
as release evidence; this file owns the current capture layout.

## Screenshot layout

Keep released and candidate captures separate:

```text
docs/images/screenshots/
├── v2.4.0/
│   ├── home.png
│   ├── search.png
│   └── review-changes.png
└── v3.0.0-rc.1/
    ├── search.png
    ├── ranking-explanation.png
    ├── rules.png
    ├── organize.png
    ├── duplicates.png
    ├── related-files.png
    └── ai-indexing-status.png
```

This is the capture plan; the listed screenshots are not existing assets.
Capture v2.4.0 only from its published package. Capture v3.0.0-rc.1 from the actual
application built from a recorded clean commit, or from its matching released
package when available. Each caption must identify `v2.4.0 release` or
`v3.0.0-rc.1 candidate at <short SHA>` and its source/package identity. Never mix
versions without labels. A working-tree capture must explicitly disclose that
state and cannot establish final released-commit identity.

Before committing a capture:

- use a dedicated disposable account or VM and synthetic files only;
- prefer a consistent 1600×1000 window at 100% scaling and one theme;
- exclude usernames, personal paths, file-picker history, notifications,
  secrets, endpoints, prompts, private text/OCR, GPS, and diagnostics;
- inspect every pixel at original resolution and strip image metadata;
- record source SHA, operating system, package/source identity, theme, and
  synthetic-fixture revision in the change description;
- write alt text that explains the visible workflow.

The required v3 set covers seven real views: Search, ranking explanation, Rules,
Organize, Duplicates, Related Files and AI/indexing status. Show semantic similarity
separately from verified relationship evidence and show the actual optional model
and indexing state. When reviewed assets exist, lead with Search and group the
remaining six views into compact pairs. Do not publish an empty gallery or reserve
broken image slots.

## Short usage video

A real 60–90 second captioned video would materially improve the landing page
after the static captures are approved. It should show a synthetic library:

1. Home readiness and navigation.
2. An explicit scan with bounded progress.
3. Search with a facet and **Why this result?** evidence.
4. Files and Related Files with user-controlled relationship decisions.
5. An organization proposal entering Review Changes.
6. A disposable Apply followed by Operation History and safe Undo.
7. Settings showing AI optional and disabled by default.
8. An end card identifying v3.0.0-rc.1 and its source/package identity.

Do not show OmniBrille as though it were included here. Avoid committing a
large GIF or MP4 to ordinary source history; use an approved versioned media
host and keep only a lightweight genuine poster image in this directory.
