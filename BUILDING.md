# Building & Distributing BGADLL

This document describes how to build the BGADLL native libraries for **all
platforms** and distribute them to the [BEN](https://github.com/ThorvaldAagaard/ben)
project. Follow this every time a new version ships.

## What gets built

`BGADLL.Native` ([BGADLL.Native/BGADLL.Native.csproj](BGADLL.Native/BGADLL.Native.csproj))
is a **NativeAOT** project that compiles the shared C# source from `BGADLL/` into a
native shared library with a C ABI (see [BGADLL.Native/BGAFFI.cs](BGADLL.Native/BGAFFI.cs)).
It produces one binary per platform:

| Platform        | RID           | Output file       |
|-----------------|---------------|-------------------|
| Windows x64     | `win-x64`     | `BGADLL.dll`      |
| Windows ARM64   | `win-arm64`   | `BGADLL.dll`      |
| Linux x64       | `linux-x64`   | `BGADLL.so`       |
| Linux ARM64     | `linux-arm64` | `BGADLL.so`       |
| macOS ARM64     | `osx-arm64`   | `BGADLL.dylib`    |

Each `BGADLL` library depends on the native Haglund **DDS** solver
(`dds.dll` / `libdds.so` / `libdds.dylib`), which BEN supplies alongside it.

## Key constraint: NativeAOT cannot cross-compile across operating systems

From a Windows machine you can **only** build the Windows RIDs (`win-x64`,
`win-arm64`). NativeAOT has no cross-OS toolchain, so `linux-*` and `osx-arm64`
**cannot** be produced locally on Windows. The Linux and macOS binaries must come
from CI (GitHub Actions), which runs native Linux and macOS runners.

➡️ **The canonical way to build all five platforms is the GitHub Actions
workflow** — see below. Local builds are only for quick Windows-only iteration.

## Versioning

Bump the version in **two** places before building:

1. [BGADLL.Native/BGADLL.Native.csproj](BGADLL.Native/BGADLL.Native.csproj) —
   `<Version>` (this is what the shipped native libraries report).
2. [BGADLL/Properties/AssemblyInfo.cs](BGADLL/Properties/AssemblyInfo.cs) —
   `AssemblyVersion` / `AssemblyFileVersion` (the legacy .NET Framework build).

Keep them in sync.

## Recommended workflow: build all platforms via CI, then distribute

This is the path that actually produces all five platforms. The order is
**commit → push → CI builds → download → distribute** — you cannot get the
Linux/macOS binaries without pushing first, because CI is what builds them.

1. **Bump the version** (see above) and commit your changes.

2. **Push to `main`.** The [Build Native Libraries](.github/workflows/build-native.yml)
   workflow triggers automatically on any push touching `BGADLL/**` or
   `BGADLL.Native/**` (or run it manually with `workflow_dispatch`). It builds all
   five platforms in parallel and a `collect` job assembles them into the
   `BGA/{windows,linux,macos}/{x64,arm64}/` layout.

   ```bash
   git push origin main
   ```

3. **Wait for the run to finish** (≈2 minutes):

   ```bash
   gh run list --workflow=build-native.yml --limit 1
   gh run watch <run-id> --exit-status
   ```

4. **Download the combined artifact.** The workflow publishes one named
   `bgadll-all-platforms` that already has the correct directory layout. (Note:
   `gh run download` must be run from inside a git checkout, or pass `-R`.)

   ```bash
   gh run download <run-id> -R ThorvaldAagaard/BGA -n bgadll-all-platforms -D <tmp>
   ```

   You get:
   ```
   windows/x64/BGADLL.dll
   windows/arm64/BGADLL.dll
   linux/x64/BGADLL.so
   linux/arm64/BGADLL.so
   macos/arm64/BGADLL.dylib
   ```

5. **Distribute to BEN.** Copy each file into `<ben>/bin/BGA/` preserving the
   same `platform/arch` layout. BEN resolves the library from there via
   `src/pimc/BGADLL_Native.py` (`bin/BGA/<platform>/<arch>/BGADLL.<ext>`).

   ```
   <ben>/bin/BGA/windows/x64/BGADLL.dll
   <ben>/bin/BGA/windows/arm64/BGADLL.dll
   <ben>/bin/BGA/linux/x64/BGADLL.so
   <ben>/bin/BGA/linux/arm64/BGADLL.so
   <ben>/bin/BGA/macos/arm64/BGADLL.dylib
   ```

   > There are additional packaged copies under `<ben>/install/{BEN,BENAll,BBA,MvsM}/bin/BGA/`
   > (each with an `_internal/` mirror). Update those only when you are rebuilding
   > the corresponding install/distribution package — the primary runtime location
   > is `bin/BGA/`.

6. **Verify** the Windows DLLs report the new version:

   ```powershell
   (Get-Item "<ben>\bin\BGA\windows\x64\BGADLL.dll").VersionInfo.FileVersion
   ```

   (`.so` / `.dylib` don't expose a Windows file version; check the file size /
   timestamp instead.)

### Gotcha: the Windows DLL may be locked by a running BEN

If BEN is running, its Python processes hold `windows/x64/BGADLL.dll` open and the
copy fails with "being used by another process". Find and stop them first, then
copy, then restart BEN:

```powershell
$t = (Resolve-Path "<ben>\bin\BGA\windows\x64\BGADLL.dll").Path
Get-Process | Where-Object { $_.Modules 2>$null | Where-Object FileName -eq $t } |
  Select-Object Id, ProcessName, Path
# Stop-Process -Id <pids> -Force   # then copy the new DLL
```

## Local builds (Windows only — for quick iteration)

NativeAOT requires the MSVC toolchain (Visual Studio C++ build tools) on the PATH.
[BGADLL.Native/build.bat](BGADLL.Native/build.bat) sets up `VsDevCmd` and publishes
`win-x64`:

```bat
BGADLL.Native\build.bat
```

Or directly:

```bash
dotnet publish BGADLL.Native/BGADLL.Native.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
dotnet publish BGADLL.Native/BGADLL.Native.csproj -c Release -r win-arm64 --self-contained true -o publish/win-arm64
```

[BGADLL.Native/build-all.sh](BGADLL.Native/build-all.sh) lists all RIDs, but the
Linux/macOS targets in it only work on the matching OS (or in CI) — they will fail
to cross-compile from Windows.

## Requirements

- **.NET 9 SDK** (`dotnet --version` ≥ 9.0).
- For local Windows NativeAOT builds: Visual Studio C++ build tools (MSVC linker).
- [`gh`](https://cli.github.com/) CLI authenticated against `ThorvaldAagaard/BGA`
  for triggering/downloading CI artifacts.
