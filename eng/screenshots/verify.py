"""Verify real-window capture evidence; never generate or modify screenshot pixels."""

import hashlib
import json
import math
import struct
import sys
from pathlib import Path

EXPECTED = [
    "01-search.png",
    "02-search-ranking.png",
    "03-rules.png",
    "04-organize.png",
    "05-duplicates.png",
    "06-related-files.png",
    "07-ai-indexing-status.png",
]
PRODUCTION_SHA = "df3984fab5eaf94424ec6cd032e91468799d62d6"

root = Path(sys.argv[1])
manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
assert manifest["completed"] is True, manifest.get("error")
assert manifest["error"] is None
assert manifest["productionSourceRevision"] == PRODUCTION_SHA
assert manifest["productionTag"] == "v3.0.0-rc.1"
assert len(manifest["captureHarnessRevision"]) == 40
assert manifest["facts"]["sourceFixtureUnchanged"] is True
assert manifest["facts"]["indexedDocumentCount"] == 9
assert manifest["facts"]["aiEnabled"] is False
assert manifest["facts"]["embeddingsEnabled"] is False
assert manifest["facts"]["duplicateGroupCount"] == 1
assert len(manifest["facts"]["relatedFiles"]) > 0
assert len(manifest["facts"]["searchResults"]) >= 3
visibility = manifest["facts"]["rankingExplanationViewport"]
assert visibility["fullyVisible"] is True and visibility["tolerance"] == 1
window = visibility["window"]
body = visibility["body"]
visible = visibility["visibleIntersection"]
ancestors = visibility["clippingAncestors"]
assert any(ancestor["type"] == "ScrollViewer" for ancestor in ancestors)
rectangles = [window] + [ancestor["rectangle"] for ancestor in ancestors]
for rectangle in rectangles + [body, visible]:
    assert all(math.isfinite(value) for value in rectangle.values())
    assert rectangle["width"] > 0 and rectangle["height"] > 0
left = max(rectangle["x"] for rectangle in rectangles)
top = max(rectangle["y"] for rectangle in rectangles)
right = min(rectangle["x"] + rectangle["width"] for rectangle in rectangles)
bottom = min(rectangle["y"] + rectangle["height"] for rectangle in rectangles)
assert right > left and bottom > top
assert abs(visible["x"] - left) < 0.001 and abs(visible["y"] - top) < 0.001
assert abs(visible["width"] - (right - left)) < 0.001
assert abs(visible["height"] - (bottom - top)) < 0.001
assert visible["width"] <= window["width"] and visible["height"] <= window["height"]
assert body["x"] >= left - 1 and body["y"] >= top - 1
assert body["x"] + body["width"] <= right + 1
assert body["y"] + body["height"] <= bottom + 1
assert manifest["facts"]["relatedFileFilter"] == "DocumentSet"
assert manifest["facts"]["organization"]["applied"] is False
assert sorted(path.name for path in root.glob("*.png")) == EXPECTED
assert [capture["filename"] for capture in manifest["captures"]] == EXPECTED
for capture in manifest["captures"]:
    data = (root / capture["filename"]).read_bytes()
    assert len(data) > 1024 and data[:8] == b"\x89PNG\r\n\x1a\n"
    assert struct.unpack(">II", data[16:24]) == (1600, 1000)
    assert capture["sha256"] == hashlib.sha256(data).hexdigest()
    assert capture["bytes"] == len(data)
print("Verified seven native 1600x1000 PNGs and completed production/fixture provenance.")
