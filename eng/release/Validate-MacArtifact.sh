#!/usr/bin/env bash
set -euo pipefail

version="${1:?A release version is required.}"
rid="${2:?A macOS runtime identifier is required.}"
artifact_directory="${3:?An artifact directory is required.}"

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
test "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$app/Contents/Info.plist")" = "$version"
file "$executable" | grep -q "$architecture"
find "$app/Contents/MacOS" -type f -name 'libe_sqlite3.dylib' | grep -q .
if find "$app" -type f \( -name '*.pdb' -o -name '*.trx' -o -name '*.db' -o -name '*.log' -o -name '*.cs' -o -name '*.csproj' -o -name '*.sln' \) | grep -q .; then
  echo 'The mounted macOS package contains a forbidden artifact.' >&2
  exit 1
fi

"$executable" --package-smoke-test "$smoke_root"
test -d "$smoke_root"

echo "Validated unsigned macOS $suffix package: $dmg"
