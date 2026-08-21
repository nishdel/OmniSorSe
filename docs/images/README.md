# OmniSorSe public media assets

This directory owns public documentation imagery. The tracked
`opensorse-logo.png` is the genuine current application mark; its retained
filename is a compatibility detail. No real application screenshot, GIF, or
usage video is currently approved in the repository.

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
└── v2.12-candidate/
    ├── home-readiness.png
    ├── search-facets-explanation.png
    ├── related-files.png
    ├── review-changes.png
    └── operation-history.png
```

Capture v2.4.0 from the published package. Capture the candidate from a clean
checkout of a recorded commit. Each README caption must say either `v2.4.0
release` or `v2.12 candidate at <short SHA>`; never mix the two without labels.

Before committing a capture:

- use a dedicated disposable account or VM and synthetic files only;
- prefer a consistent 1600×1000 window at 100% scaling and one theme;
- exclude usernames, personal paths, file-picker history, notifications,
  secrets, endpoints, prompts, private text/OCR, GPS, and diagnostics;
- inspect every pixel at original resolution and strip image metadata;
- record source SHA, operating system, package/source identity, theme, and
  synthetic-fixture revision in the change description;
- write alt text that explains the visible workflow.

When the assets exist, use one full-width Search image followed by two compact
rows: Home with Related Files, then Review Changes with Operation History. Do
not publish an empty gallery or reserve broken image slots.

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
8. An end card distinguishing v2.4.0 from the v2.12 candidate.

Do not show OmniBrille as though it were included here. Avoid committing a
large GIF or MP4 to ordinary source history; use an approved versioned media
host and keep only a lightweight genuine poster image in this directory.
