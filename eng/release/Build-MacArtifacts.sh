#!/usr/bin/env bash
set -euo pipefail

version="${1:-2.12.0-rc}"
rid="${2:?A macOS runtime identifier is required.}"
output_directory="${3:?A release output directory is required.}"
source_revision="${4:-}"

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$ ]]; then
  echo 'The package version must be a filename-safe semantic version.' >&2
  exit 2
fi
base_version="${version%%-*}"
file_version="$base_version.0"

case "$rid" in
  osx-x64) architecture="x86_64" ;;
  osx-arm64) architecture="arm64" ;;
  *) echo "Unsupported macOS runtime identifier: $rid" >&2; exit 2 ;;
esac

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd "$script_directory/../.." && pwd)"
if [[ -z "$source_revision" ]]; then
  source_revision="$(git -C "$repository_root" rev-parse HEAD)"
fi
if [[ ! "$source_revision" =~ ^[0-9a-fA-F]{40}$ ]]; then
  echo 'An exact 40-character source revision is required for release packaging.' >&2
  exit 2
fi
output_root="$(mkdir -p "$output_directory" && cd "$output_directory" && pwd)"
staging_root="$output_root/staging/$rid"
case "$staging_root" in
  "$output_root"/*) ;;
  *) echo "The macOS staging directory escaped the release output root." >&2; exit 2 ;;
esac
rm -rf "$staging_root"
mkdir -p "$staging_root/publish"

dotnet publish "$repository_root/src/OpenSorSe.Desktop/OpenSorSe.Desktop.csproj" \
  --configuration Release \
  --runtime "$rid" \
  --self-contained true \
  --no-restore \
  --output "$staging_root/publish" \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -p:PublishSingleFile=false \
  -p:OmniSorSeVersion="$version" \
  -p:OmniSorSeFileVersion="$file_version" \
  -p:SourceRevisionId="$source_revision" \
  -p:ContinuousIntegrationBuild=true

app_bundle="$staging_root/OmniSorSe.app"
contents="$app_bundle/Contents"
macos_directory="$contents/MacOS"
resources_directory="$contents/Resources"
mkdir -p "$macos_directory" "$resources_directory"
cp -R "$staging_root/publish/." "$macos_directory/"
chmod +x "$macos_directory/OmniSorSe"
cp "$repository_root/LICENSE" "$resources_directory/LICENSE"
cp "$repository_root/THIRD_PARTY_NOTICES.md" "$resources_directory/THIRD_PARTY_NOTICES.md"
cp "$repository_root/docs/dependency-licenses.json" "$resources_directory/dependency-licenses.json"
cp "$repository_root/docs/INSTALLATION.md" "$resources_directory/INSTALLATION.md"
if [[ "$version" != "$base_version" ]]; then
  cat > "$resources_directory/VALIDATION_BUILD.md" <<NOTICE
# OmniSorSe $version validation build

This is a publisher-unsigned and unnotarized prerelease build from exact source \`$source_revision\`. A toolchain-provided ad-hoc signature does not authenticate the publisher. It is not a stable or GA release; it is intended for final real-world and manual validation. Opening it can migrate the retained OpenSorSe profile and schema. Use a disposable machine/profile or make a reviewed backup before manual validation.
NOTICE
fi
runtime_version="$(python3 - "$macos_directory/OmniSorSe.runtimeconfig.json" <<'PY'
import json, sys
path = sys.argv[1]
with open(path, encoding="utf-8") as stream:
    frameworks = json.load(stream).get("runtimeOptions", {}).get("includedFrameworks", [])
matches = [item.get("version") for item in frameworks if item.get("name") == "Microsoft.NETCore.App"]
if len(matches) != 1:
    raise SystemExit("The self-contained publish does not identify exactly one bundled .NET runtime")
print(matches[0])
PY
)"
printf '{"productVersion":"%s","baseVersion":"%s","sourceRevision":"%s","configuration":"Release","targetFramework":"net10.0","runtimeIdentifier":"%s","runtimeVersion":"%s","selfContained":true}\n' \
  "$version" "$base_version" "$source_revision" "$rid" "$runtime_version" > "$resources_directory/OmniSorSe.build.json"
release_notes="$repository_root/docs/RELEASE_NOTES_v$base_version.md"
if [[ ! -f "$release_notes" ]]; then
  echo "Release notes for v$base_version are missing: $release_notes" >&2
  exit 1
fi
cp "$release_notes" "$resources_directory/RELEASE_NOTES.md"
find "$macos_directory" -type f -name '*.pdb' -delete

icon_source="$repository_root/src/OpenSorSe.Desktop/Assets/opensorse-app-icon.png"
iconset="$staging_root/OmniSorSe.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  double_size=$((size * 2))
  sips -z "$size" "$size" "$icon_source" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  sips -z "$double_size" "$double_size" "$icon_source" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$resources_directory/OmniSorSe.icns"

cat > "$contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleDisplayName</key><string>OmniSorSe</string>
  <key>CFBundleExecutable</key><string>OmniSorSe</string>
  <key>CFBundleIconFile</key><string>OmniSorSe</string>
  <key>CFBundleIdentifier</key><string>io.github.nishdel.OpenSorSe</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>OmniSorSe</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$base_version</string>
  <key>CFBundleVersion</key><string>$base_version</string>
  <key>CFBundleGetInfoString</key><string>OmniSorSe $version</string>
  <key>OmniSorSeProductVersion</key><string>$version</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
plutil -lint "$contents/Info.plist"

if find "$app_bundle" -type f \( -name '*.pdb' -o -name '*.trx' -o -name '*.db' -o -name '*.sqlite' -o -name '*.db-wal' -o -name '*.db-shm' -o -name '*.log' -o -name '*.oms-state' -o -name '*.bak' -o -name '*.cs' -o -name '*.csproj' -o -name '*.sln' -o -name 'settings.json' -o -name 'operation-journal*.json' -o -name 'change-plan*.json' -o -name 'saved-view*.json' -o -name 'recipe*.json' \) | grep -q .; then
  echo 'The macOS app bundle contains a forbidden development or local-data artifact.' >&2
  exit 1
fi
if ! file "$macos_directory/OmniSorSe" | grep -q "$architecture"; then
  echo "The packaged executable is not $architecture." >&2
  file "$macos_directory/OmniSorSe" >&2
  exit 1
fi
if ! find "$macos_directory" -type f -name 'libe_sqlite3.dylib' | grep -q .; then
  echo 'The macOS app bundle is missing the native SQLite library.' >&2
  exit 1
fi
for runtime_asset in libcoreclr.dylib libhostfxr.dylib libSkiaSharp.dylib; do
  if [[ ! -f "$macos_directory/$runtime_asset" ]]; then
    echo "The macOS app bundle is missing required runtime/native asset '$runtime_asset'." >&2
    exit 1
  fi
done

dmg_root="$staging_root/dmg-root"
mkdir -p "$dmg_root"
cp -R "$app_bundle" "$dmg_root/OmniSorSe.app"
ln -s /Applications "$dmg_root/Applications"
suffix="${rid#osx-}"
dmg="$output_root/OmniSorSe-v$version-macos-$suffix.dmg"
rm -f "$dmg"
hdiutil create -volname "OmniSorSe $version" -srcfolder "$dmg_root" -ov -format UDZO "$dmg"
printf '%s\n' "$dmg"
