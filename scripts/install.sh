#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
game_dir="${1:-${DV_GAME_PATH:-/home/deck/.local/share/Steam/steamapps/common/Derail Valley}}"
package_dir="$project_dir/dist/DVRouteOverlay"
if [[ ! -f "$game_dir/DerailValley_Data/Managed/UnityModManager/UnityModManager.dll" ]]; then
    echo "Unity Mod Manager was not found in: $game_dir" >&2
    exit 1
fi
if [[ ! -f "$package_dir/DVRouteOverlay.dll" || ! -f "$package_dir/Info.json" ]]; then
    echo 'Run bash scripts/build.sh first.' >&2
    exit 1
fi
target_dir="$game_dir/Mods/DVRouteOverlay"
# Preserve settings and keep the previous build available when updating.
if [[ -d "$target_dir" ]]; then
    backup_dir="$project_dir/dist/backups/DVRouteOverlay-$(date +%Y%m%d-%H%M%S)"
    mkdir -p -- "$(dirname -- "$backup_dir")"
    cp -a -- "$target_dir" "$backup_dir"
    echo "Previous installation backed up: $backup_dir"
fi
mkdir -p -- "$target_dir"
# Replace files atomically so an already-running game can keep its loaded DLL.
install_tmp=""
trap 'if [[ -n "$install_tmp" ]]; then rm -f -- "$install_tmp"; fi' EXIT
for name in DVRouteOverlay.dll Info.json README.md; do
    install_tmp="$(mktemp "$target_dir/.route-overlay.XXXXXX")"
    cp -- "$package_dir/$name" "$install_tmp"
    chmod 644 -- "$install_tmp"
    mv -f -- "$install_tmp" "$target_dir/$name"
    install_tmp=""
done
echo "Installed: $target_dir"
echo 'Start or restart Derail Valley to load Route Overlay.'
