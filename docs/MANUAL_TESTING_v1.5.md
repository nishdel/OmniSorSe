# OpenSorSe 1.5 Manual Testing

Use disposable data. Do not publish a binary based only on this checklist.
Record distribution, desktop, filesystem, mount type, architecture, Tesseract
version, and the exact commit tested.

## Both supported targets

- [ ] About shows `1.5`; product is `1.5.0`; assembly/file version is `1.5.0.0`.
- [ ] Settings opens and Platform diagnostics lists every capability and owned
      location without secrets.
- [ ] Copy platform report and open diagnostics folder succeed or show a
      non-fatal, specific unavailable explanation.
- [ ] Scan a Unicode/hidden-file tree. Confirm symlink/reparse targets outside
      the root are not traversed and cycles terminate.
- [ ] Preview a portable recipe containing `:`, `?`, trailing dot/space, and a
      Windows device name. Confirm sanitization and no root escape.
- [ ] Rename and same-filesystem move through review, final confirmation,
      journal, verification, Undo, restart, and recovery.
- [ ] Confirm an occupied destination, linked destination, unwritable
      directory, changed source, and cross-filesystem move fail closed.
- [ ] Exercise watched create/modify/rename/delete, event bursts, root loss,
      startup reconciliation, manual reconciliation, and overflow reporting.
- [ ] Discover a managed plugin. Confirm it is not activated automatically.
- [ ] Confirm a native plugin with a mismatched/missing runtime identifier is
      incompatible and cannot load.
- [ ] Check Tesseract through `PATH`, then through an absolute configured path;
      verify missing executable and missing language-pack messages.
- [ ] Connect to an already running configured Ollama HTTP endpoint. Confirm no
      provider executable is auto-started.

## Windows

- [ ] Existing `%LocalAppData%\OpenSorSe` settings and state remain readable;
      no duplicate migration or deletion occurs.
- [ ] Case-only rename uses the safe temporary rename path and verifies result.
- [ ] Junctions and reparse points remain excluded.

## Linux preview

- [ ] Follow [Linux build and launch](LINUX_BUILD_AND_LAUNCH.md) on a supported
      x64 distribution with a graphical session.
- [ ] Verify XDG overrides and fallback directories separately.
- [ ] Verify executable-bit failure for configured Tesseract.
- [ ] Verify case-sensitive files with names differing only by case remain
      distinct.
- [ ] Verify device/inode identity survives rename and distinguishes a copy.
- [ ] Verify same-mount rename/Undo and cross-mount rejection using disposable
      mounts.
- [ ] Inspect inotify descriptor/queue pressure and reconcile after overflow.
- [ ] Confirm file picker, clipboard, theme/fonts, and default file manager.

macOS is not a v1.5 manual release target. Do not convert an incidental launch
into a support claim.
