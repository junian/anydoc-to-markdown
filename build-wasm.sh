#!/bin/sh
# Build the anydoc WASM engine from the repo's wasm/ crate and lay the
# wasm-bindgen output into AnyDocToMarkdown.BlazorWasm/wwwroot so the NuGet
# package ships them as static web assets.
#
# Produces:
#   src/AnyDocToMarkdown.BlazorWasm/wwwroot/anydoc_wasm.js      wasm-bindgen glue
#   src/AnyDocToMarkdown.BlazorWasm/wwwroot/anydoc_wasm_bg.wasm engine binary
#
# The static wwwroot/anydoc-wasm.js interop shim is checked in (not generated).
#
# Prerequisites:
#   rustup target add wasm32-unknown-unknown
#   cargo install wasm-bindgen-cli --version <same as libs/anydoc/Cargo.lock>
set -eu

cd "$(dirname "$0")"
root=$(pwd)
wasm_dir="$root/libs/anydoc/wasm"
out="$root/src/AnyDocToMarkdown.BlazorWasm/wwwroot"

# The `anydoc` submodule is a separate workspace, so its build artifacts are
# tailed into a fresh target dir (never into the checked-out submodule).
target_dir="$root/.cache/anydoc-wasm-target"

# The wasm-bindgen version must match the one the crate compiled against.
bindgen_version=$(grep -A1 'name = "wasm-bindgen"' "$wasm_dir/../Cargo.lock" | grep version | head -n 1 | sed 's/.*"\([0-9.]*\)".*/\1/')

# It is typically on PATH, but also found under the cargo bin dir if not.
WA_BINDGEN=""
for c in wasm-bindgen "$HOME/.cargo/bin/wasm-bindgen"; do
  if command -v "$c" >/dev/null 2>&1 || [ -x "$c" ]; then WA_BINDGEN="$c"; break; fi
done
[ -n "$WA_BINDGEN" ] || { echo "wasm-bindgen not found; install it with: cargo install wasm-bindgen-cli --version $bindgen_version" >&2; exit 1; }
echo "-- using $WA_BINDGEN (wasm-bindgen $bindgen_version)"

rustup target add wasm32-unknown-unknown >/dev/null

echo "-- building wasm for target wasm32-unknown-unknown"
CARGO_TARGET_DIR="$target_dir" cargo build --manifest-path "$wasm_dir/Cargo.toml" --release --target wasm32-unknown-unknown --package anydoc-wasm

rm -f "$out/anydoc_wasm.js" "$out/anydoc_wasm_bg.wasm" "$out/anydoc_wasm.js.d.ts" "$out/anydoc_wasm_bg.wasm.d.ts"
"$WA_BINDGEN" "$target_dir/wasm32-unknown-unknown/release/anydoc_wasm.wasm" \
  --target web --out-dir "$out" --out-name anydoc_wasm

echo "-> $out/anydoc_wasm.js"
echo "-> $out/anydoc_wasm_bg.wasm"