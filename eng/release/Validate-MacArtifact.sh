#!/usr/bin/env bash
set -euo pipefail

version="${1:?A release version is required.}"
rid="${2:?A macOS runtime identifier is required.}"
artifact_directory="${3:?An artifact directory is required.}"
source_revision="${4:?An exact source revision is required.}"

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$ ]]; then
  echo 'The package version must be a filename-safe semantic version.' >&2
  exit 2
fi
base_version="${version%%-*}"

case "$rid" in
  osx-x64) suffix="x64"; architecture="x86_64" ;;
  osx-arm64) suffix="arm64"; architecture="arm64" ;;
  *) echo "Unsupported macOS runtime identifier: $rid" >&2; exit 2 ;;
esac

artifact_root="$(cd "$artifact_directory" && pwd)"
dmg="$artifact_root/OmniSorSe-v$version-macos-$suffix.dmg"
test -s "$dmg"
mount_point="$artifact_root/validation/macos-$suffix/mount"
smoke_root="$artifact_root/validation/macos-$suffix/user-data"
rm -rf "$artifact_root/validation/macos-$suffix"
mkdir -p "$mount_point" "$smoke_root"

cleanup() {
  hdiutil detach "$mount_point" -quiet >/dev/null 2>&1 || true
}
trap cleanup EXIT
hdiutil attach "$dmg" -nobrowse -readonly -mountpoint "$mount_point" >/dev/null
app="$mount_point/OmniSorSe.app"
executable="$app/Contents/MacOS/OmniSorSe"
test -x "$executable"
plutil -lint "$app/Contents/Info.plist"
test "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$app/Contents/Info.plist")" = 'io.github.nishdel.OpenSorSe'
test "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$app/Contents/Info.plist")" = "$base_version"
test "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleVersion' "$app/Contents/Info.plist")" = "$base_version"
test "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleGetInfoString' "$app/Contents/Info.plist")" = "OmniSorSe $version"
test "$(/usr/libexec/PlistBuddy -c 'Print :OmniSorSeProductVersion' "$app/Contents/Info.plist")" = "$version"
python3 - "$app/Contents/Resources/OmniSorSe.build.json" "$version" "$base_version" "$source_revision" "$rid" <<'PY'
import json, sys
path, version, base_version, revision, rid = sys.argv[1:]
with open(path, encoding="utf-8") as stream:
    value = json.load(stream)
expected = {
    "productVersion": version,
    "baseVersion": base_version,
    "sourceRevision": revision,
    "configuration": "Release",
    "targetFramework": "net10.0",
    "runtimeIdentifier": rid,
    "selfContained": True,
}
if any(value.get(key) != expected_value for key, expected_value in expected.items()):
    raise SystemExit("macOS package provenance does not match the requested source")
if not str(value.get("runtimeVersion", "")).startswith("10."):
    raise SystemExit("macOS package does not identify a .NET 10 runtime")
PY
if [[ "$version" != "$base_version" ]]; then
  validation_notice="$app/Contents/Resources/VALIDATION_BUILD.md"
  test -f "$validation_notice"
  grep -Fq "OmniSorSe $version validation build" "$validation_notice"
  grep -Fq "$source_revision" "$validation_notice"
  grep -Fq 'not a stable or GA release' "$validation_notice"
  grep -Fq 'final real-world and manual validation' "$validation_notice"
  grep -Fq 'unsigned and unnotarized' "$validation_notice"
  grep -Fq 'disposable machine/profile' "$validation_notice"
  grep -Fq 'migrate the retained OpenSorSe profile and schema' "$validation_notice"
fi
if codesign -dv --verbose=4 "$app" >/dev/null 2>&1 || codesign -dv --verbose=4 "$dmg" >/dev/null 2>&1; then
  echo 'The macOS prerelease package is unexpectedly code-signed.' >&2
  exit 1
fi
if xcrun stapler validate "$app" >/dev/null 2>&1 || xcrun stapler validate "$dmg" >/dev/null 2>&1; then
  echo 'The macOS prerelease package is unexpectedly notarized or stapled.' >&2
  exit 1
fi
file "$executable" | grep -q "$architecture"
find "$app/Contents/MacOS" -type f -name 'libe_sqlite3.dylib' | grep -q .
python3 - "$app/Contents/MacOS/OmniSorSe.runtimeconfig.json" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as stream:
    value = json.load(stream)
if value.get("runtimeOptions", {}).get("tfm") != "net10.0":
    raise SystemExit("macOS package runtime configuration is not net10.0")
PY
for runtime_asset in libcoreclr.dylib libhostfxr.dylib libSkiaSharp.dylib; do
  test -f "$app/Contents/MacOS/$runtime_asset"
done
if find "$app" -type f \( -name '*.pdb' -o -name '*.trx' -o -name '*.db' -o -name '*.sqlite' -o -name '*.db-wal' -o -name '*.db-shm' -o -name '*.log' -o -name '*.oms-state' -o -name '*.bak' -o -name '*.cs' -o -name '*.csproj' -o -name '*.sln' -o -name 'settings.json' -o -name 'operation-journal*.json' -o -name 'change-plan*.json' -o -name 'saved-view*.json' -o -name 'recipe*.json' \) | grep -q .; then
  echo 'The mounted macOS package contains a forbidden artifact.' >&2
  exit 1
fi

"$executable" --package-smoke-test "$smoke_root"
test -d "$smoke_root"

echo "Validated unsigned macOS $suffix package: $dmg"
