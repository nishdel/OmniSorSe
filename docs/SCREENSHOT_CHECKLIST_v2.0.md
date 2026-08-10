# OpenSorSe v2.0 screenshot capture checklist

No mock or generated UI image may be presented as an application screenshot.
If native capture cannot be completed reliably during release engineering,
leave the README without screenshots and complete this checklist later without
blocking code, packaging, or safety validation.

Every capture must use a disposable application-data root and synthetic demo
folders. Review the full image at original resolution before commit.

- [ ] Dashboard or Scan page with synthetic roots and no user/profile path.
- [ ] Background indexing progress with synthetic filenames and representative
      stage/coverage/storage information.
- [ ] Search with synthetic results, visible filters, bounded snippets, and a
      truthful “Why this result?” explanation.
- [ ] Knowledge Graph with synthetic evidence and no private aliases or paths.
- [ ] Smart Collections or Related Files using synthetic relationships.
- [ ] Change Plan review using disposable synthetic source/destination paths.
- [ ] Verify no screenshot contains a username, personal filename, personal
      path, secret, token, private prompt, raw document/OCR text, or sensitive
      diagnostic.
- [ ] Crop cleanly, retain readable resolution, use descriptive lowercase file
      names in `docs/images/screenshots/`, and add meaningful README alt text.
- [ ] Confirm the screenshot matches the exact released application rather than
      a mock, design surface, or AI-generated image.

**Status:** intentionally unchecked; no real v2.0 screenshots are claimed yet.
