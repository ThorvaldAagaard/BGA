#!/bin/bash
# Build BGADLL native libraries for all platforms
# Requires .NET 9.0 SDK installed
# Run from the BGADLL.Native directory
#
# Cross-compilation from Windows:
#   Windows targets work directly
#   Linux/macOS targets require cross-compilation toolchains or CI (GitHub Actions)
#
# For CI, use the GitHub Actions workflow below

set -e

PROJECT="BGADLL.Native.csproj"
OUTPUT_BASE="../bin/BGA"

# Build for a specific RID
build_rid() {
    local rid=$1
    local outdir=$2
    echo "Building for $rid -> $outdir"
    dotnet publish "$PROJECT" \
        -c Release \
        -r "$rid" \
        --self-contained true \
        -o "$outdir"
    echo "Done: $rid"
}

# Windows x64
build_rid "win-x64" "$OUTPUT_BASE/windows/x64"

# Windows ARM64
build_rid "win-arm64" "$OUTPUT_BASE/windows/arm64"

# Linux x64
build_rid "linux-x64" "$OUTPUT_BASE/linux/x64"

# Linux ARM64
build_rid "linux-arm64" "$OUTPUT_BASE/linux/arm64"

# macOS ARM64
build_rid "osx-arm64" "$OUTPUT_BASE/macos/arm64"

echo ""
echo "All builds complete. Output in $OUTPUT_BASE/"
echo ""
echo "Native libraries produced:"
find "$OUTPUT_BASE" -name "BGADLL.*" -o -name "libBGADLL.*" | sort
