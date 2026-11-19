# BGADLL.Cpp

A C++ port of the BGADLL engine (`PIMC`, `PIMCDef` and the classes they use) with the same C
ABI as `BGADLL.Native` (the 61 `bga_*` functions of [BGAFFI.cs](../BGADLL.Native/BGAFFI.cs)).
It is a drop-in for BEN's `bin/BGA/linux/x64/BGADLL.so` (and `bin/BGA/macos/arm64/BGADLL.dylib`):
BEN's ctypes bindings (`src/pimc/BGADLL_Native.py`) load it unchanged.

Its double-dummy solver is [ddfast](https://github.com/randalburns/ddfast)
(`ddfast::card_results`), with Bo Haglund's DDS as a fallback. Without ddfast it runs on DDS
alone, with the same results as the C# library and no .NET build.

Linux x86-64 and macOS (Apple silicon), both tested; Arm Linux builds (`-mcpu=native`) but is untested.

## Build

```sh
BGADLL.Cpp/build.sh          # Linux: BGADLL.Cpp/bin/BGADLL.so; macOS: BGADLL.Cpp/bin/BGADLL.dylib
```

C++17, no other dependencies: g++ on Linux (libstdc++ linked statically, `-march=x86-64-v3` by
default on x86-64; `ARCH` overrides), clang++ on macOS. The solver library is opened at run
time with `dlopen`.

## Use with BEN

Copy `bin/BGADLL.so` over `<ben>/bin/BGA/linux/x64/BGADLL.so` (macOS: `bin/BGADLL.dylib` over
`<ben>/bin/BGA/macos/arm64/BGADLL.dylib`).

The solver is chosen once per process:

| | |
|---|---|
| `BGA_DDS_BACKEND=ddfast` / `haglund` | force a backend; unset, ddfast if it can be loaded, else Haglund |
| `BGA_DDFAST_LIB` / `BGA_DDS_LIB` | path of `libddfast` / `libdds` (`.so`, or `.dylib` on macOS) |
| `DDFAST_TABLES` | ddfast's table directory (as for BEN's own ddfast use) |

A library already in the process is used first. BEN preloads ddfast's `libdds.so`, which
loads `libddfast.so`, so BGADLL and BEN's DDSolver share one copy of the tables. Otherwise
the library is looked for beside `BGADLL.so`, then on the loader's path. `bga_dds_backend()`
reports `ddfast`, `haglund` or `none`. With no solver, `bga_pimc_setup` and
`bga_pimcdef_setup` return -1.

## Same results as the C# library

Given the same inputs, the port makes the same samples in the same order:
- the same seed (SHA-256 of the two known hands);
- the same SplitMix32 Fisher-Yates order of the opponents' splits;
- the same constraint handling and the same weights.

So it returns the same `(tricks, weight)` lists. `bench/bench_bga.cpp` checks this. It drives
any `BGADLL.so` through the C ABI, as BEN does, and dumps every result. On the 3 fixed
positions and 300 random positions at every stage of play, every combination below gave
byte-identical dumps:
- C# NativeAOT on Haglund DDS;
- C++ on Haglund DDS;
- C++ on ddfast;
- C# on ddfast's `libdds` shim.

Milliseconds per evaluation over those positions:

| | C# on DDS | C++ on DDS | C# on ddfast | C++ on ddfast |
|---|---|---|---|---|
| Linux x86-64 (Zen 5, 12 threads) | 185.5 | 143.9 | 67.7 | 36.0 |
| macOS (Apple M5, 10 threads) | 133.6 | 124.7 | 31.0 | 21.5 |

```sh
g++ -std=c++17 -O2 -o bin/bench_bga bench/bench_bga.cpp -ldl
bin/bench_bga --lib bin/BGADLL.so --positions 300 --dump cpp.txt
bin/bench_bga --lib <csharp>/BGADLL.so --preload <ben>/bin/libdds.so --positions 300 --dump cs.txt
cmp cpp.txt cs.txt
```

## Differences from the C# library

- `version()` is `1.0.0.0`.
- Samples are 64-bit card masks rather than lists of `Card` objects and strings.
- Worker threads come from one persistent pool instead of new threads per `Evaluate`, because
  ddfast keeps its search state per OS thread.
- `AwaitEvaluation` wakes on a condition variable instead of polling every 10 ms.
- On ddfast, a sample can have a revoke in the current trick: a hidden hand that discarded
  while still holding the suit led. DDS accepts that position and ddfast does not, so the trick
  is played out card by card, as ddfast's own DDS layer does. The fusion strategy's "x" command
  plays the lowest of the best cards, where DDS plays whichever best card it finds first. BEN's
  configs have fusion off.
- Behaviour that is a quirk in C# is kept, so results match:
  - `PIMCDef` records the trick count as the weight.
  - `Clear()` leaves the shown hands.
  - Results are read back in descending order after `SortResults`.
