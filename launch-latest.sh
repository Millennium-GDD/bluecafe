#!/usr/bin/env bash
# Launch the most recently built complete Linux Player in this checkout.
set -euo pipefail

project_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
print_path=false
explicit_pack=false
app_args=()
for arg in "$@"; do
    case "$arg" in
        --help)
            cat <<'HELP'
Usage: ./launch-latest.sh [--print-path] [BlueCafe arguments...]

Launch the newest complete Linux Player in Builds/ (by executable mtime).
--print-path   Print its executable path without launching.
--help         Show this help.

Other arguments are passed unchanged to BlueCafe. An explicit --asset-pack
overrides BLUECAFE_ASSET_PACK and the checkout's prepared Content pack.
Without a prepared pack, BlueCafe uses its saved asset-folder setting.
HELP
            exit 0
            ;;
        --print-path) print_path=true ;;
        *)
            app_args+=("$arg")
            case "$arg" in
                --asset-pack|--asset-pack=*) explicit_pack=true ;;
            esac
            ;;
    esac
done

shopt -s nullglob
latest=''
for candidate in "$project_root"/Builds/*/BlueCafe.x86_64; do
    build_dir=${candidate%/*}
    [[ -f "$candidate" && -x "$candidate" && -d "$build_dir/BlueCafe_Data" && -f "$build_dir/UnityPlayer.so" ]] || continue
    if [[ -z "$latest" || "$candidate" -nt "$latest" ]]; then
        latest=$candidate
    fi
done

if [[ -z "$latest" ]]; then
    printf 'No complete executable Linux build found in %s/Builds/.\n' "$project_root" >&2
    printf 'Build or extract a Player with BlueCafe.x86_64, BlueCafe_Data/, and UnityPlayer.so in one directory.\n' >&2
    exit 1
fi

if "$print_path"; then
    printf '%s\n' "$latest"
    exit 0
fi

if ! "$explicit_pack"; then
    pack_root=${BLUECAFE_ASSET_PACK:-}
    if [[ -z "$pack_root" && -f "$project_root/Assets/BlueCafe/Content/catalog.json" ]]; then
        pack_root="$project_root/Assets/BlueCafe/Content"
    fi
    if [[ -n "$pack_root" ]]; then
        # Preserve explicit missing paths so the app can report them.
        [[ "$pack_root" = /* ]] || pack_root="$PWD/$pack_root"
        app_args+=(--asset-pack "$pack_root")
    fi
fi

cd -- "${latest%/*}"
exec "$latest" "${app_args[@]}"
