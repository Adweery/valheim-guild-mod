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
    echo 'Instalacia zlyhala. Povodne subory boli obnovene zo zalohy.'
  fi
  [ -z "$TEMP_DIR" ] || rm -rf "$TEMP_DIR"
  if [ "$result" -ne 0 ]; then echo 'Nic dalsie nespustaj. Posli Adamovi chybu uvedenu vyssie.'; fi
  if [ -t 0 ]; then read -r -p 'Stlac Enter pre zavretie.' _ || true; fi
  exit "$result"
}
trap finish EXIT
fail() { echo "$*" >&2; exit 1; }
fetch() { /usr/bin/curl --fail --location --silent --show-error --proto '=https' --proto-redir '=https' --connect-timeout 15 --max-time 180 --retry 2 "$1" -o "$2"; }
verify() { [[ "$2" =~ ^[a-f0-9]{64}$ ]] || fail 'Neplatny kontrolny sucet.'; [ "$(/usr/bin/shasum -a 256 "$1" | cut -d ' ' -f 1)" = "$2" ] || fail 'Stiahnuty subor nepresiel kontrolou. Instalacia zastavena.'; }
field() { /usr/bin/plutil -extract "$1" raw -o - "$TEMP_DIR/latest.json"; }
safe_zip() {
  /usr/bin/unzip -Z1 "$1" > "$TEMP_DIR/entries.txt"
  [ "$(wc -l < "$TEMP_DIR/entries.txt")" -le 500 ] || fail 'Prilis vela suborov v baliku.'
  while IFS= read -r entry; do
    case "$entry" in /*|*\\*|../*|*/../*|*/..|*:*|*'//'*) fail 'Nebezpecna cesta v baliku.';; esac
  done < "$TEMP_DIR/entries.txt"
  if /usr/bin/zipinfo -l "$1" | /usr/bin/grep -q '^l'; then fail 'Balik obsahuje symbolicky odkaz.'; fi
}
[ "$(uname -s)" = Darwin ] || fail 'Tento instalator je pre macOS.'
/usr/bin/pgrep -x Valheim >/dev/null && fail 'Najprv uplne vypni Valheim a potom spusti instalator znova.'
/usr/bin/arch -x86_64 /usr/bin/true || fail 'Na tomto Macu je najprv potrebna Rosetta. Ozvi sa Adamovi.'
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
    read -r -p 'Vloz cestu k priecinku, ktory obsahuje valheim.app: ' GAME
  fi
fi
[ -d "$GAME/valheim.app/Contents/MacOS" ] || fail 'V zvolenom priecinku nie je Valheim.'
GAME="$(cd "$GAME" && pwd -P)"
TEMP_DIR="$(mktemp -d)"
echo 'Stahujem aktualnu verziu Valheim Guild modu...'
fetch "$BASE/releases/latest/download/latest.json" "$TEMP_DIR/latest.json"
[ "$(field schema)" = 1 ] || fail 'Je potrebna nova verzia instalatora.'
VERSION="$(field version)"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || fail 'Neplatna verzia.'
URL="$(field mod_url)"
[ "$URL" = "$BASE/releases/download/v$VERSION/ValheimGuildTelemetry.zip" ] || fail 'Neocakavane miesto stahovania.'
fetch "$URL" "$TEMP_DIR/mod.zip"; verify "$TEMP_DIR/mod.zip" "$(field mod_sha256)"; safe_zip "$TEMP_DIR/mod.zip"
mkdir "$TEMP_DIR/mod" "$TEMP_DIR/stage"
[ "$(/usr/bin/unzip -Z1 "$TEMP_DIR/mod.zip")" = ValheimGuildTelemetry.dll ] || fail 'Neocakavany obsah modu.'
/usr/bin/unzip -q "$TEMP_DIR/mod.zip" -d "$TEMP_DIR/mod"
verify "$TEMP_DIR/mod/ValheimGuildTelemetry.dll" "$(field dll_sha256)"
if [ -f "$GAME/BepInEx/core/BepInEx.dll" ]; then
  verify "$GAME/BepInEx/core/BepInEx.dll" "$(field loader_core_sha256)"
  [ -f "$GAME/start_game_bepinex.sh" ] && [ -f "$GAME/doorstop_libs/libdoorstop_x64.dylib" ] || fail 'BepInEx nie je kompletne nastaveny pre Mac. Ozvi sa Adamovi.'
else
  for name in BepInEx doorstop_libs start_game_bepinex.sh; do
    [ ! -e "$GAME/$name" ] || fail 'Nasiel som inu alebo nekompletnu instalaciu modov. Ozvi sa Adamovi.'
  done
  LOADER="$(field loader_url)"
  [ "$LOADER" = 'https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/5.4.2350/' ] || fail 'Neocakavany zdroj BepInEx.'
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
  [ ! -e "$GAME/$rel" ] || [ -f "$GAME/$rel" ] || fail 'Cielovy subor je priecinok. Instalacia zastavena.'
  check="$GAME/$rel"
  while [ "$check" != "$GAME" ]; do
    [ ! -L "$check" ] || fail 'Ciel obsahuje symbolicky odkaz. Instalacia zastavena.'
    check="$(dirname "$check")"
  done
done < "$TEMP_DIR/targets.txt"
/usr/bin/pgrep -x Valheim >/dev/null && fail 'Hra sa medzitym spustila. Najprv ju vypni.'
[ ! -L "$GAME/ValheimGuildBackups" ] || fail 'Priecinok zaloh je symbolicky odkaz.'
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
echo "Hotovo. Nainstalovana verzia: $VERSION"
echo "Hru spustaj cez: $GAME/Start-Valheim-Guild-Mac.command"
echo 'XP, svety a prepojenie uctu zostali zachovane.'
