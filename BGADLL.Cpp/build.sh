#!/bin/sh
# Builds the C++ BGADLL with the C ABI of BGADLL.Native, a drop-in for BEN's
#   Linux:  bin/BGADLL.so     (BEN: bin/BGA/linux/x64/BGADLL.so)
#   macOS:  bin/BGADLL.dylib  (BEN: bin/BGA/macos/arm64/BGADLL.dylib)
# No link-time dependencies beyond libc/libstdc++: the solver (libddfast / libdds) is opened at
# run time (see src/dds.hpp).
#   CXX=...    compiler (default g++ on Linux, clang++ on macOS)
#   ARCH=...   target flags (default -march=x86-64-v3 on x86-64, -mcpu=native on Arm; the
#              solver libraries carry their own)
#   OUT=...    output directory (default bin)
set -eu
cd "$(dirname "$0")"
OUT=${OUT:-bin}
mkdir -p "$OUT"
SRC="src/model.cpp src/dds.cpp src/pimc.cpp src/ffi.cpp"
if [ "$(uname -s)" = Darwin ]; then
  CXX=${CXX:-clang++}
  ARCH=${ARCH:--mcpu=native}
  # shellcheck disable=SC2086
  $CXX -std=c++17 -O2 -g -fPIC -fvisibility=hidden -Wall -Wextra $ARCH \
    -dynamiclib -install_name @rpath/BGADLL.dylib -o "$OUT/BGADLL.dylib" $SRC -pthread
  echo "built $OUT/BGADLL.dylib"
else
  CXX=${CXX:-g++}
  case "$(uname -m)" in
    x86_64) ARCH=${ARCH:--march=x86-64-v3} ;;
    *) ARCH=${ARCH:--mcpu=native} ;;
  esac
  # shellcheck disable=SC2086
  $CXX -std=c++17 -O2 -g -fPIC -fvisibility=hidden -Wall -Wextra $ARCH \
    -shared -o "$OUT/BGADLL.so" $SRC -static-libstdc++ -static-libgcc -pthread -ldl
  echo "built $OUT/BGADLL.so"
fi
