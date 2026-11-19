#!/bin/sh
# Builds the C++ BGADLL: bin/BGADLL.so (Linux) with the C ABI of BGADLL.Native, a drop-in
# for BEN's bin/BGA/linux/x64/BGADLL.so. No link-time dependencies beyond libc/libstdc++:
# the solver (libddfast.so or libdds.so) is opened at run time (see src/dds.hpp).
#   CXX=...    compiler (default g++)
#   ARCH=...   target flags (default -march=x86-64-v3; the solver libraries carry their own)
#   OUT=...    output directory (default bin)
set -eu
cd "$(dirname "$0")"
CXX=${CXX:-g++}
ARCH=${ARCH:--march=x86-64-v3}
OUT=${OUT:-bin}
mkdir -p "$OUT"
# shellcheck disable=SC2086
$CXX -std=c++17 -O2 -g -fPIC -fvisibility=hidden -Wall -Wextra $ARCH \
  -shared -o "$OUT/BGADLL.so" src/model.cpp src/dds.cpp src/pimc.cpp src/ffi.cpp \
  -static-libstdc++ -static-libgcc -pthread -ldl
echo "built $OUT/BGADLL.so"
