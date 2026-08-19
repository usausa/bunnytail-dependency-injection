# BunnyTail.DependencyInjection.Sandbox

Benchmarks that verify DI specific design decisions. The rejected candidates are kept here with their implementations, so every comparison against the adopted shape stays reproducible even though the library itself no longer carries them.

General purpose performance patterns (type keyed lookup, dispatch shapes, lazy creation, `Unsafe.As` and the like) **belong in the [dotnet-performance](https://github.com/usausa/dotnet-performance) catalog**, not here.

## Contents

| Benchmark | Subject | Why it is DI specific |
|---|---|---|
| `KeyedLookupBenchmark` | `(Type, key)` lookup structures for keyed services | Keyed services in MEDI have the shape of one service type with several keys, which a table keyed by `Type` alone cannot answer |
| `DisposalTrackingBenchmark` | Disposal tracking cost of transients | Tracking and disposing transients is a constraint imposed by MEDI compatibility; this measures the gain from settling the need for tracking by type at generation time |
| `EnumerableMaterializationBenchmark` | Array creation strategies for `IEnumerable<T>` materialization | A comparison under the DI specific constraint that a real `T[]` must be returned even though the element type is known only at runtime, and that transient elements are rebuilt on every resolution |
| `ResolutionEntryBenchmark` | Resolution entry shapes: accessor virtual call against a constant short circuit | A container specific design comparison of whether a service table entry holds a virtual layer that manages lifetime or the resolved instance directly, with disassembly |
| `EnumerableConsumptionBenchmark` | Consumption shapes of a resolved `IEnumerable<T>`: interface enumeration against `T[]` cast enumeration | The container materializes and returns a `T[]` for MEDI compatibility, yet enumerating through the interface adds an enumerator allocation. Escape analysis does not help on NativeAOT, where the difference surfaces (measured on AOT: 44.5ns/32B against 12.0ns/0B) |

## Subjects that defer to dotnet-performance

The following were verified here, then removed along with their implementations because the conclusions matched the dotnet-performance catalog. Consult the catalog when they need to be revisited.

| Original subject | Reference | Conclusion |
|---|---|---|
| Main table shape for `Type` to `Entry` (identity hash / node list / Robin Hood / `FrozenDictionary`) | `TypeIdentityHashBenchmark`, `NodeTypeHashMap` and `RobinHoodTypeTable` in `CandidateVerification.Benchmarks`, TYP-01, R-08 | A node list with identity hash, reference comparison and a 2^n mask is fastest. `FrozenDictionary` loses to `Dictionary` for `Type` keys |
| Resolution path of generic public APIs (`typeof(T)` branching / `TypeSlot<T>`) | TYP-01, JIT-03 | A branch chain costs about 0.23ns where the type argument is statically known. A two level lookup through a runtime `Type` is slower than `Dictionary` |
| Factory dispatch shapes (closed delegate / sealed virtual / interface / `delegate*`) | DSP-02, `GuardedDevirtBenchmark` | `delegate*` was rejected because `calli` can be neither inlined nor speculated. A closed instance delegate was adopted |
| Storage shapes for singletons and scoped instances (typed field / `object[]` slot / lazy) | STK-07, TYP-05, MEM-02 | Typed fields were adopted. Where slots are used, they go through `Unsafe.As` rather than `castclass` |

## Running

```bash
dotnet run -c Release -- --verify                    # equivalence verification only
dotnet run -c Release -- --filter "*Keyed*"          # keyed tables
dotnet run -c Release -- --filter "*"                # everything
```

Measurement practice follows `docs/benchmark-methodology.md` in dotnet-performance: judge on the three axes of time, allocation and code size, treat a result as significant only when the confidence intervals do not overlap, and verify equivalence before measuring.

## Maintenance note

`NodeCompositeTable` mirrors the layout of `FixedKeyedServiceTable` in the library. **When that layout changes, change this one too**, otherwise the comparison stops meaning anything.
