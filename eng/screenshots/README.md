# Native v3 documentation capture

This dedicated harness displays the real production Avalonia `MainWindow` and
its production composition root on Linux X11. It scans nine committed synthetic
text documents into a fresh application profile, waits for actual text extraction,
then drives existing view-model commands. It does not replace services, generate
AI/vector results, render controls offscreen, or alter product source.

The seven images show Search, an expanded ranking explanation, the current
in-memory Sorting rules review surface, side-by-side Organize proposal trees,
the real exact-hash duplicate pair, retained Related Files evidence, and actual
AI/indexing status. Organization stops at a proposal. Rules are explicitly
synthetic in-memory inputs loaded after scanning; they are never executed.
Chat AI and learned embeddings are disabled because CI has no installed model.
The Search images therefore show the real non-model fallback.

The production source is fixed to `v3.0.0-rc.1` at
`df3984fab5eaf94424ec6cd032e91468799d62d6`. The workflow checks that the tag,
production files, and build inputs still match that commit. The built production
assembly carries that revision; the separate harness revision is recorded from
the exact capture commit. No release tag or package is updated.

Run [.github/workflows/capture-v3-docs.yml](../../.github/workflows/capture-v3-docs.yml)
on the visual-documentation branch. It installs native Linux prerequisites,
builds with the repository .NET SDK into an isolated artifacts directory, runs
the desktop under Xvfb, and invokes ImageMagick `import -window <X11 ID> -strip`
on the real native window. The images are not resized, composited or retouched.
The workflow uploads partial images, manifest and logs even when capture fails.

For a Linux machine with those prerequisites, use fresh runtime/output paths:

```sh
export CAPTURE_HARNESS_SHA="$(git rev-parse HEAD)"
dotnet build eng/screenshots/Screenshots.csproj --configuration Release \
  --artifacts-path .artifacts/screenshot-build \
  -p:SourceRevisionId=df3984fab5eaf94424ec6cd032e91468799d62d6
xvfb-run -a -s "-screen 0 1920x1200x24 -nolisten tcp" \
  dotnet .artifacts/screenshot-build/bin/Screenshots/release/OmniSorSe.Screenshots.dll \
  /tmp/omnisorse-v3-docs .artifacts/v3-docs
python3 eng/screenshots/verify.py .artifacts/v3-docs
```

`manifest.json` records source and harness identities, assembly version, OS,
framework, dimensions, theme, per-image hashes, input fixture hashes, actual
Search explanations, organization proposals, relationship evidence and indexing
status. The harness verifies the source library is byte-for-byte unchanged,
including its path set, before and after cleanup. Both the runner and verifier
require all seven nonempty 1600×1000 PNGs.

A successful build is only compilation evidence. A successful workflow with
inspected PNGs supplies native Linux visual evidence for this synthetic workflow.
It does not establish human usability, accessibility, other-platform appearance,
installed-package behavior, or successful learned-model inference.
