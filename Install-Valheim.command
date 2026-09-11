#!/bin/bash
set -euo pipefail
BASE='https://github.com/Adweery/valheim-guild-mod'
TEMP_DIR=''
BACKUP=''
GAME=''
COMMITTING=0
finish() {
  result=$?
  if [ "$result" -ne 0 ] && [ "$COMMITTING" -eq 1 ]; then
    while IFS= read -r rel; do
      if [ -f "$BACKUP/files/$rel" ]; then cp -p "$BACKUP/files/$rel" "$GAME/$rel" || true
      else rm -f "$GAME/$rel"; fi
    done < "$BACKUP/changed.txt"
    echo 'Installation failed. Original files were restored from the backup.'
  fi
  [ -z "$TEMP_DIR" ] || rm -rf "$TEMP_DIR"
  if [ "$result" -ne 0 ]; then echo 'Installation failed. Report the error above at https://github.com/Adweery/valheim-guild-mod/issues'; fi
  if [ -t 0 ]; then read -r -p 'Press Enter to close.' _ || true; fi
  exit "$result"
}
trap finish EXIT
fail() { echo "$*" >&2; exit 1; }
fetch() { /usr/bin/curl --fail --location --silent --show-error --proto '=https' --proto-redir '=https' --connect-timeout 15 --max-time 180 --retry 2 "$1" -o "$2"; }
verify() { [[ "$2" =~ ^[a-f0-9]{64}$ ]] || fail 'Invalid checksum.'; [ "$(/usr/bin/shasum -a 256 "$1" | cut -d ' ' -f 1)" = "$2" ] || fail 'Downloaded file verification failed. Installation stopped.'; }
field() { /usr/bin/plutil -extract "$1" raw -o - "$TEMP_DIR/latest.json"; }
safe_zip() {
  /usr/bin/unzip -Z1 "$1" > "$TEMP_DIR/entries.txt"
  [ "$(wc -l < "$TEMP_DIR/entries.txt")" -le 500 ] || fail 'Too many files in the archive.'
  while IFS= read -r entry; do
    case "$entry" in /*|*\\*|../*|*/../*|*/..|*:*|*'//'*) fail 'Unsafe path in the archive.';; esac
  done < "$TEMP_DIR/entries.txt"
  if /usr/bin/zipinfo -l "$1" | /usr/bin/grep -q '^l'; then fail 'The archive contains a symbolic link.'; fi
}
[ "$(uname -s)" = Darwin ] || fail 'This installer is for macOS.'
/usr/bin/pgrep -x Valheim >/dev/null && fail 'Close Valheim completely, then run the installer again.'
/usr/bin/arch -x86_64 /usr/bin/true || fail 'Rosetta must be installed on this Mac before continuing.'
if [ "$#" -gt 0 ]; then GAME="$1"; else
  STEAM="$HOME/Library/Application Support/Steam"
  CANDIDATES=()
  for root in "$STEAM"; do
    [ ! -d "$root/steamapps/common/Valheim/valheim.app" ] || CANDIDATES+=("$root/steamapps/common/Valheim")
  done
  if [ -f "$STEAM/steamapps/libraryfolders.vdf" ]; then
    while IFS= read -r root; do
      path="$root/steamapps/common/Valheim"
      if [ "$root" != "$STEAM" ] && [ -d "$path/valheim.app" ]; then CANDIDATES+=("$path"); fi
    done < <(/usr/bin/sed -nE 's/^[[:space:]]*"path"[[:space:]]*"([^"]*)".*/\1/p' "$STEAM/steamapps/libraryfolders.vdf")
  fi
  if [ "${#CANDIDATES[@]}" -eq 1 ]; then GAME="${CANDIDATES[0]}"; else
    read -r -p 'Enter the path to the directory containing valheim.app: ' GAME
  fi
fi
[ -d "$GAME/valheim.app/Contents/MacOS" ] || fail 'Valheim was not found in the selected directory.'
GAME="$(cd "$GAME" && pwd -P)"
TEMP_DIR="$(mktemp -d)"
echo 'Downloading the latest Valheim Guild mod...'
fetch "$BASE/releases/latest/download/latest.json" "$TEMP_DIR/latest.json"
[ "$(field schema)" = 1 ] || fail 'Please download the latest installer version.'
VERSION="$(field version)"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || fail 'Invalid version.'
URL="$(field mod_url)"
[ "$URL" = "$BASE/releases/download/v$VERSION/ValheimGuildTelemetry.zip" ] || fail 'Unexpected download location.'
fetch "$URL" "$TEMP_DIR/mod.zip"; verify "$TEMP_DIR/mod.zip" "$(field mod_sha256)"; safe_zip "$TEMP_DIR/mod.zip"
mkdir "$TEMP_DIR/mod" "$TEMP_DIR/stage"
[ "$(/usr/bin/unzip -Z1 "$TEMP_DIR/mod.zip")" = ValheimGuildTelemetry.dll ] || fail 'Unexpected mod archive contents.'
/usr/bin/unzip -q "$TEMP_DIR/mod.zip" -d "$TEMP_DIR/mod"
verify "$TEMP_DIR/mod/ValheimGuildTelemetry.dll" "$(field dll_sha256)"
if [ -f "$GAME/BepInEx/core/BepInEx.dll" ]; then
  verify "$GAME/BepInEx/core/BepInEx.dll" "$(field loader_core_sha256)"
  [ -f "$GAME/start_game_bepinex.sh" ] && [ -f "$GAME/doorstop_libs/libdoorstop_x64.dylib" ] || fail 'BepInEx setup for Mac is incomplete. Report this in a GitHub issue.'
else
  for name in BepInEx doorstop_libs start_game_bepinex.sh; do
    [ ! -e "$GAME/$name" ] || fail 'An unrecognized or incomplete mod loader was found. Report this in a GitHub issue.'
  done
  LOADER="$(field loader_url)"
  [ "$LOADER" = 'https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/5.4.2350/' ] || fail 'Unexpected BepInEx download source.'
  fetch "$LOADER" "$TEMP_DIR/loader.zip"; verify "$TEMP_DIR/loader.zip" "$(field loader_sha256)"; safe_zip "$TEMP_DIR/loader.zip"
  /usr/bin/unzip -q "$TEMP_DIR/loader.zip" 'BepInExPack_Valheim/*' -d "$TEMP_DIR/loader"
  PACK="$TEMP_DIR/loader/BepInExPack_Valheim"
  for name in BepInEx doorstop_libs start_game_bepinex.sh; do cp -R "$PACK/$name" "$TEMP_DIR/stage/"; done
fi
mkdir -p "$TEMP_DIR/stage/BepInEx/plugins/ValheimGuildTelemetry"
cp "$TEMP_DIR/mod/ValheimGuildTelemetry.dll" "$TEMP_DIR/stage/BepInEx/plugins/ValheimGuildTelemetry/"
cat > "$TEMP_DIR/stage/Start-Valheim-Guild-Mac.command" <<'LAUNCH'
#!/bin/bash
set -e
cd "$(dirname "$0")"
open -a Steam
exec /usr/bin/arch -x86_64 /bin/bash ./start_game_bepinex.sh ./valheim.app
LAUNCH
printf '%s\n' "$VERSION" > "$TEMP_DIR/stage/valheim-guild-version.txt"
# Validate the entire destination before touching files, including symlinked ancestors.
(cd "$TEMP_DIR/stage" && find . -type f) | sed 's|^./||' > "$TEMP_DIR/targets.txt"
while IFS= read -r rel; do
  [ ! -e "$GAME/$rel" ] || [ -f "$GAME/$rel" ] || fail 'A destination file is a directory. Installation stopped.'
  check="$GAME/$rel"
  while [ "$check" != "$GAME" ]; do
    [ ! -L "$check" ] || fail 'The destination contains a symbolic link. Installation stopped.'
    check="$(dirname "$check")"
  done
done < "$TEMP_DIR/targets.txt"
/usr/bin/pgrep -x Valheim >/dev/null && fail 'Valheim started during installation. Close the game first.'
[ ! -L "$GAME/ValheimGuildBackups" ] || fail 'The backup directory is a symbolic link.'
BACKUP="$GAME/ValheimGuildBackups/$(date +%Y%m%d-%H%M%S)-$$"
mkdir -p "$BACKUP/files"; : > "$BACKUP/changed.txt"
while IFS= read -r rel; do
  if [ -f "$GAME/$rel" ]; then mkdir -p "$BACKUP/files/$(dirname "$rel")"; cp -p "$GAME/$rel" "$BACKUP/files/$rel"; fi
done < "$TEMP_DIR/targets.txt"
COMMITTING=1
while IFS= read -r rel; do
  mkdir -p "$GAME/$(dirname "$rel")"
  printf '%s\n' "$rel" >> "$BACKUP/changed.txt"
  cp "$TEMP_DIR/stage/$rel" "$GAME/$rel"
done < "$TEMP_DIR/targets.txt"
chmod u+x "$GAME/Start-Valheim-Guild-Mac.command" "$GAME/start_game_bepinex.sh"
COMMITTING=0
echo "Done. Installed version: $VERSION"
echo "Launch the game using: $GAME/Start-Valheim-Guild-Mac.command"
echo 'XP, worlds and account linking have been preserved.'
