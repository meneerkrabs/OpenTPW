#!/usr/bin/env bash
# Builds native/osx-arm64/libveldrid-spirv.dylib; a wrapper around scripts/build-native.py.
# VELDRID_SPIRV_SOURCE_DIR keeps working when its last component is "veldrid-spirv".
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd -P)"
arguments=(--rid osx-arm64 --jobs "${NATIVE_BUILD_JOBS:-4}")
if [[ -n "${VELDRID_SPIRV_SOURCE_DIR:-}" ]]; then
    if [[ "$(basename "$VELDRID_SPIRV_SOURCE_DIR")" != veldrid-spirv ]]; then
        echo "VELDRID_SPIRV_SOURCE_DIR must end in /veldrid-spirv (it is a source cache entry)." >&2
        exit 1
    fi
    arguments+=(--cache-dir "$(dirname "$VELDRID_SPIRV_SOURCE_DIR")")
fi
exec python3 "$root/scripts/build-native.py" "${arguments[@]}" veldrid-spirv "$@"
