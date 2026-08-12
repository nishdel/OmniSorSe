#!/usr/bin/env bash
set -euo pipefail

version="${1:-2.3.0}"
rid="${2:?A macOS runtime identifier is required.}"
output_directory="${3:?A release output directory is required.}"

case "$rid" in
  osx-x64) architecture="x86_64" ;;
  osx-arm64) architecture="arm64" ;;
  *) echo "Unsupported macOS runtime identifier: $rid" >&2; exit 2 ;;
esac

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd "$script_directory/../.." && pwd)"
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
  -p:PublishSingleFile=false

app_bundle="$staging_root/OpenSorSe.app"
contents="$app_bundle/Contents"
macos_directory="$contents/MacOS"
resources_directory="$contents/Resources"
mkdir -p "$macos_directory" "$resources_directory"
cp -R "$staging_root/publish/." "$macos_directory/"
chmod +x "$macos_directory/OpenSorSe"
cp "$repository_root/LICENSE" "$resources_directory/LICENSE"
cp "$repository_root/THIRD_PARTY_NOTICES.md" "$resources_directory/THIRD_PARTY_NOTICES.md"
cp "$repository_root/docs/dependency-licenses.json" "$resources_directory/dependency-licenses.json"
cp "$repository_root/docs/INSTALLATION.md" "$resources_directory/INSTALLATION.md"
release_notes="$repository_root/docs/RELEASE_NOTES_v$version.md"
if [[ ! -f "$release_notes" ]]; then
  echo "Release notes for v$version are missing: $release_notes" >&2
  exit 1
fi
cp "$release_notes" "$resources_directory/RELEASE_NOTES.md"
find "$macos_directory" -type f -name '*.pdb' -delete

icon_source="$repository_root/src/OpenSorSe.Desktop/Assets/opensorse-app-icon.png"
iconset="$staging_root/OpenSorSe.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  double_size=$((size * 2))
  sips -z "$size" "$size" "$icon_source" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  sips -z "$double_size" "$double_size" "$icon_source" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$resources_directory/OpenSorSe.icns"

cat > "$contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleDisplayName</key><string>OpenSorSe</string>
  <key>CFBundleExecutable</key><string>OpenSorSe</string>
  <key>CFBundleIconFile</key><string>OpenSorSe</string>
  <key>CFBundleIdentifier</key><string>io.github.nishdel.OpenSorSe</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>OpenSorSe</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
plutil -lint "$contents/Info.plist"

if find "$app_bundle" -type f \( -name '*.pdb' -o -name '*.trx' -o -name '*.db' -o -name '*.log' -o -name '*.cs' -o -name '*.csproj' -o -name '*.sln' \) | grep -q .; then
  echo 'The macOS app bundle contains a forbidden development or local-data artifact.' >&2
  exit 1
fi
if ! file "$macos_directory/OpenSorSe" | grep -q "$architecture"; then
  echo "The packaged executable is not $architecture." >&2
  file "$macos_directory/OpenSorSe" >&2
  exit 1
fi
if ! find "$macos_directory" -type f -name 'libe_sqlite3.dylib' | grep -q .; then
  echo 'The macOS app bundle is missing the native SQLite library.' >&2
  exit 1
fi

dmg_root="$staging_root/dmg-root"
mkdir -p "$dmg_root"
cp -R "$app_bundle" "$dmg_root/OpenSorSe.app"
ln -s /Applications "$dmg_root/Applications"
suffix="${rid#osx-}"
dmg="$output_root/OpenSorSe-v$version-macos-$suffix.dmg"
rm -f "$dmg"
hdiutil create -volname "OpenSorSe $version" -srcfolder "$dmg_root" -ov -format UDZO "$dmg"
printf '%s\n' "$dmg"
