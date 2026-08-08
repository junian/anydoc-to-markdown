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

# RID -> builder -> rust triple -> library artifact
# (cargo names cdylibs lib<name>.dylib/.so and <name>.dll on their platforms)
targets() {
  echo osx-x64:cargo:x86_64-apple-darwin:libanydoc.dylib
  echo osx-arm64:cargo:aarch64-apple-darwin:libanydoc.dylib
  echo win-x86:zigbuild:i686-pc-windows-gnu:anydoc.dll
  echo win-x64:zigbuild:x86_64-pc-windows-gnu:anydoc.dll
  echo win-arm64:zigbuild:aarch64-pc-windows-gnullvm:anydoc.dll
  echo linux-x64:zigbuild:x86_64-unknown-linux-gnu:libanydoc.so
  echo linux-arm64:zigbuild:aarch64-unknown-linux-gnu:libanydoc.so
  echo ios-arm64:cargo:aarch64-apple-ios:libanydoc.dylib
  echo iossimulator-arm64:cargo:aarch64-apple-ios-sim:libanydoc.dylib
  echo iossimulator-x64:cargo:x86_64-apple-ios:libanydoc.dylib
  echo maccatalyst-arm64:cargo:aarch64-apple-ios-macabi:libanydoc.dylib
  echo maccatalyst-x64:cargo:x86_64-apple-ios-macabi:libanydoc.dylib
  echo android-arm64:ndk:aarch64-linux-android:libanydoc.so
  echo android-arm:ndk:armv7-linux-androideabi:libanydoc.so
  echo android-x64:ndk:x86_64-linux-android:libanydoc.so
  echo android-x86:ndk:i686-linux-android:libanydoc.so
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
  command -v cargo-zigbuild >/dev/null 2>&1 \
    || [ -x "$HOME/.cargo/bin/cargo-zigbuild" ] \
    || command -v zigbuild >/dev/null 2>&1
}

build_rid() {
  rid=$1; shift
  line=$(targets | grep "^$rid:" || true)
  [ -n "$line" ] || { echo "unknown RID: $rid" >&2; exit 2; }
  rest=${line#*:}          # strip rid
  builder=${rest%%:*}      # cargo / zigbuild / ndk
  rest=${rest#*:}          # strip builder
  triple=${rest%%:*}       # rust target triple
  artifact=${rest#*:}      # cdylib filename

  echo "-- building $rid ($builder, $triple)"
  if ! rustup target list --installed 2>/dev/null | grep -qx "$triple"; then
    echo "   target $triple not installed; skipping (add it with: rustup target add $triple)" >&2
    return 1
  fi

  if ! run_build "$root/native" "$builder" "$triple"; then
    echo "   build failed for $triple" >&2
    return 1
  fi

  out=$(find "$root/native/target/$triple/release" -type f -name "$artifact" 2>/dev/null | head -n 1)
  [ -n "$out" ] || { echo "   artifact $artifact not found" >&2; return 1; }
  mkdir -p "$root/src/AnyDocToMarkdown/runtimes/$rid/native"
  cp "$out" "$root/src/AnyDocToMarkdown/runtimes/$rid/native/$artifact"
  echo "   -> src/AnyDocToMarkdown/runtimes/$rid/native/$artifact"
}

# Run the relevant cargo build for a triple.
#   cargo: the Apple triples, built with the Xcode SDK.
#   zigbuild: Linux/Windows via cargo-zigbuild (zig) when available.
#   ndk: Android via cargo-ndk (requires the NDK, e.g. ANDROID_NDK_HOME).
run_build() {
  native_dir=$1; builder=$2; triple=$3
  case "$builder" in
    ndk)
      [ -d "${ANDROID_NDK_HOME:-}" ] || { echo "   ANDROID_NDK_HOME is not set" >&2; return 1; }
      (cd "$native_dir" && cargo ndk -t "$triple" build --release)
      ;;
    zigbuild)
      if has_zigbuild; then
        cargo zigbuild --manifest-path "$native_dir/Cargo.toml" --release --target "$triple" -p anydoc-dotnet
      else
        echo "   cargo-zigbuild not installed; falling back to plain cargo" >&2
        cargo build --manifest-path "$native_dir/Cargo.toml" --release --target "$triple" -p anydoc-dotnet
      fi
      ;;
    *)
      cargo build --manifest-path "$native_dir/Cargo.toml" --release --target "$triple" -p anydoc-dotnet
      ;;
  esac
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