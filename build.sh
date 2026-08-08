#!/bin/sh
# Build the native cdylib for the listed targets and lay each one out under
# src/AnyDocToMarkdown/runtimes/{rid}/native so `dotnet pack` bundles a NuGet
# package (AnyDocToMarkdown) that carries macOS, Windows, and Linux binaries for
# both x64 and arm64 (plus Windows x86).
#
# Cross-compiling to a target you do not natively develop on needs that target
# installed (`rustup target add <triple>`) and, for non-host targets, a linker;
# this script prefers `cargo-zigbuild` (zig) when it is available for the Linux
# and Windows gnu targets. Windows/MSVC targets cannot be produced on macOS or
# Linux hosts; build those on Windows runners.
#
# Usage (from the repo root, or anydoc-to-markdown/):
#   sh build.sh                  # host target only
#   sh build.sh --all            # every supported native target
#   sh build.sh osx-arm64 ...    # a specific RID subset
set -eu

cd "$(dirname "$0")"
root=$(pwd)

# RID -> rust target triple -> artifact filename
# (cargo names cdylibs lib<name>.dylib/.so and <name>.dll on their platforms)
targets() {
  echo osx-x64:x86_64-apple-darwin:libanydoc_dotnet.dylib
  echo osx-arm64:aarch64-apple-darwin:libanydoc_dotnet.dylib
  echo win-x86:i686-pc-windows-gnu:anydoc_dotnet.dll
  echo win-x64:x86_64-pc-windows-gnu:anydoc_dotnet.dll
  echo win-arm64:aarch64-pc-windows-gnullvm:anydoc_dotnet.dll
  echo linux-x64:x86_64-unknown-linux-gnu:libanydoc_dotnet.so
  echo linux-arm64:aarch64-unknown-linux-gnu:libanydoc_dotnet.so
}

host_rid() {
  case "$(uname -s)-$(uname -m)" in
    Darwin-arm64) echo osx-arm64 ;;
    Darwin-x86_64|Darwin-i386) echo osx-x64 ;;
    Linux-x86_64|Linux-amd64) echo linux-x64 ;;
    Linux-aarch64|Linux-arm64) echo linux-arm64 ;;
    *) echo "unsupported host $(uname -s) $(uname -m)" >&2; exit 2 ;;
  esac
}

has_zigbuild() {
  command -v cargo-zigbuild >/dev/null 2>&1
}

build_rid() {
  rid=$1; shift
  line=$(targets | grep "^$rid:" || true)
  [ -n "$line" ] || { echo "unknown RID: $rid" >&2; exit 2; }
  rest=${line#*:}          # strip rid
  triple=${rest%%:*}       # rust target triple
  artifact=${rest#*:}      # cdylib filename

  echo "-- building $rid ($triple)"
  if ! rustup target list --installed 2>/dev/null | grep -qx "$triple"; then
    echo "   target $triple not installed; skipping (add it with: rustup target add $triple)" >&2
    return 1
  fi

  if has_zigbuild && [ "$triple" != "x86_64-apple-darwin" ] && [ "$triple" != "aarch64-apple-darwin" ]; then
    cargo zigbuild --manifest-path "$root/native/Cargo.toml" --release --target "$triple" -p anydoc-dotnet
  else
    cargo build --manifest-path "$root/native/Cargo.toml" --release --target "$triple" -p anydoc-dotnet
  fi

  out=$(find "$root/native/target/$triple/release" -type f -name "$artifact" 2>/dev/null | head -n 1)
  [ -n "$out" ] || { echo "   artifact $artifact not found" >&2; return 1; }
  mkdir -p "$root/src/AnyDocToMarkdown/runtimes/$rid/native"
  cp "$out" "$root/src/AnyDocToMarkdown/runtimes/$rid/native/$artifact"
  echo "   -> src/AnyDocToMarkdown/runtimes/$rid/native/$artifact"
}

if [ "$#" -eq 0 ]; then
  build_rid "$(host_rid)"
elif [ "$1" = --all ]; then
  for rid in $(targets | cut -d: -f1); do
    build_rid "$rid" || true
  done
else
  for rid in "$@"; do
    build_rid "$rid"
  done
fi

echo "-- packing dotnet"
dotnet pack "$root/src/AnyDocToMarkdown/AnyDocToMarkdown.csproj" -c Release -o "$root/artifacts"
echo "package:"
ls -1 "$root/artifacts"/AnyDocToMarkdown.*.nupkg 2>/dev/null || true