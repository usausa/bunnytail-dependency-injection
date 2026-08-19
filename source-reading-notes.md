# Source reading notes

Working notes from the source-reading session. Kept for folding into `README.md` later.

Explanations here follow a fixed framing, deliberately different from the current README's
reference-table style:

1. **What it is** — the purpose, and how it differs from the neighbouring feature
2. **When to use it** — the situation that calls for it, and when it does not pay off
3. **Where it is used** — who writes it, who consumes it, and real examples in this repository

Notes are appended in reading order. Nothing here is final wording.

---

## Attributes

### ComponentRegistration

**What it is.** An instruction to generate bulk registration code from class name patterns instead
of writing the registrations by hand. Declare an empty partial extension method, attach the
attribute, and the body is generated.

The difference from the lifetime attributes is *where the instruction lives*: `[Singleton]` and
friends go on the class being registered, `[ComponentRegistration]` goes on the method doing the
registering.

**When to use it.** Two situations:

* Many classes share a naming convention and should be registered in one place. No per-class
  annotation, and the registration policy is readable from a single method.
* The classes cannot be annotated at all. With the `Assembly` parameter the generator scans a
  referenced assembly's metadata, so types from a library that does not reference this package can
  still be registered by convention.

`Namespace` narrows the match. Several attributes on one method give different patterns per
lifetime; several methods in one class split registration by purpose.

**Where it is used.** Written in user code. In this repository:

* `BunnyTail.DependencyInjection.Tests/ConventionRegistration.cs` — three methods in one class,
  registering `Service$` / `Repository$` / `Gadget$` with different lifetimes
* `Example/Registrations.cs` — `Assembly = "Example.Library2"`, registering types from a library
  that does not reference the generator

Consumed at compile time only. The generated body is ordinary MEDI registration code,
indistinguishable from hand-written `Add*` calls.

### ComponentModule

**What it is.** An assembly level sign saying "this assembly carries a registration module", naming
the type that holds the registration method.

It exists because of the compilation boundary: a generator only sees the source it compiles, so it
cannot know the components of a referenced library. Each library advertises its own registration
method through this assembly attribute, and the application's generator collects them into
`AddAllGeneratedComponents()`.

**When to use it.** Only when the marker is *not* embedded automatically. It is embedded for an
assembly that has attribute components, so a library whose components carry `[Singleton]` and friends
never writes it. `ComponentModuleAttribute` is not `AllowMultiple`, so **one marker per assembly** —
the embedded and the hand-written form are mutually exclusive, and hand-writing one in an assembly
that has attribute components is a duplicate-attribute compile error.

That leaves one case: a library **with the generator active but no attribute components** — one that
registers everything through factories, options binding or conditional logic. It gets no marker, yet
may still want to be discovered by the application's single aggregating call.

Be aware of how thin that benefit is. With the generator active, the library's own `Add*` calls are
already collected and its factories already generated, so the marker buys **auto-discovery alone** —
the application not having to call `AddSomeLibrary()` itself. Explicitly calling the library's
extension method is the ordinary .NET convention and is a defensible choice instead.

A library that does not reference this package at all cannot declare a module: the attribute type is
not available to it. Referencing the NuGet package always activates the generator, because the
package ships it under `analyzers/dotnet/cs`, so "a library without the generator" is not a situation
a package consumer produces.

**Where it is used.** `Example.Library` is the embedded case. The hand-written form is documented but
not sampled: demonstrating it needs an assembly whose only distinguishing trait is "generator active,
no attribute components", and the sample set is clearer without it.

Read by the generator of the *referencing* project. This is the only attribute read from another
assembly's metadata. References are visible transitively, so modules of indirectly referenced
libraries are aggregated as well. Compile time only.

### GenerateComponentFactory

**What it is.** An instruction to prepare a reflection-free factory **without registering the
type** — "have a way to construct this ready; someone else performs the registration".

Registration and factory generation are separate here. `[Singleton]` does both; this does only the
latter.

**When to use it.** When a type you do not control is registered through someone else's extension
method. Types that a library registers itself through `services.AddSomeLibrary()` can neither be
annotated nor intercepted, so they keep resolving through the runtime (reflection) path. This
attribute leaves the registration to the library and moves only the resolution onto the generated
path.

What is missing in that situation is not the construction information — the constructor of a public
type is visible through metadata. What is missing is the **registration site**: factory generation is
triggered by `Add*` calls appearing in the compiled source, and a registration that happens inside
the library's own method never appears there. The attribute supplies that trigger, and nothing is
registered by hand at runtime: the generated factory registers itself from a `[ModuleInitializer]`.

`PostConstruct` gives an initialization hook to a type that cannot be annotated.

Not every runtime fallback is worth marking. Factory and instance registrations gain nothing, and a
singleton pays the cost once. **The ones that pay off are public transients and scoped services on
hot paths.**

**Where it is used.** Written as an assembly attribute in user code. The clearest example is
`Example.WebApplication/FactoryGeneration.cs`, covering five ASP.NET Core framework types
(`DefaultHttpContextFactory`, `MiddlewareFactory`, …) that the host registers and the application
therefore cannot annotate. `Example/FactoryGeneration.cs` has another example.

Finding candidates is supported by the diagnostics: `DescribeRuntimeFallbacks()` in
`Diagnostics/ServiceFactoryReportExtensions.cs` prints ready-to-paste attribute lines for the
runtime-path types that generated code could actually construct. This is the one attribute meant to
be added while watching a diagnostic report.

At runtime the generated factory is registered in `GeneratedComponentRegistry` keyed by
implementation type, and the engine adopts it when it realizes a registration of that type. Who
called `Add*` does not matter.

### Common point

All three are consumed at compile time and leave nothing behind at runtime.

### Assembly scoped convention: the cost to be aware of

`Assembly = "..."` on `[ComponentRegistration]` is the one convention feature with an incremental
build cost worth calling out. The external scan provider is combined with the compilation, so as soon
as a single request exists it runs on **every compilation** — in the IDE, on every keystroke. The scan
walks the requested assembly's whole namespace and type tree, nested types included, applying the
regex to every type name. Only matched types go on to symbol analysis, so the expensive half is
bounded, but the walk itself is always full. With no request it short-circuits to an empty result.

The output is protected: the scan result is value-equatable, so downstream generation is skipped when
it does not change (`PipelineIncrementalityTest` asserts a `Cached` output after an unrelated edit
with an `Assembly` request present). Nothing breaks — the walk is simply paid for on every edit.

Prefer, in order:

1. Annotate the types in the owning library — no scan at all
2. Call the library's own registration extension method, adding `[GenerateComponentFactory]` if the
   generated path matters
3. Put a hand-written `[ComponentModule]` in the owning library

`Assembly` earns its place when the owning source cannot be touched *and* no registration extension
method is offered.

---

## Registration entry points

### Why registration is split into two methods

| Method | Role | Emitted when |
|---|---|---|
| `AddGeneratedComponents()` | The unit of **one module** — registers this assembly's components only | this assembly has attribute components |
| `AddAllGeneratedComponents()` | The **application entry point** — calls every referenced module flatly, then its own | components or referenced modules exist |

An application that also wants components from other assemblies calls
`AddAllGeneratedComponents()`.

They cannot be merged into one method, because the aggregator calls every module flatly. References
are visible transitively, so the application's generator can enumerate modules of indirectly
referenced libraries. If a single method registered "its own plus its references", every module would
re-register its own dependencies when the aggregator invoked it, and a diamond reference (A→B, A→C,
B→D, C→D) would register D twice. MEDI accepts duplicate registrations, so this would surface quietly
as an extra element in `IEnumerable<T>`.

The current split deduplicates **at compile time** through flat enumeration and costs nothing at
runtime; merging would require runtime deduplication on every build of the provider. The split also
leaves the finer-grained option open: an application can call one module's
`AddGeneratedComponents()` directly to register just that library.

---

## Sample projects

### What each one stands for

The two sample libraries differ by **what the application's generator can see**, which is what the
features under test hinge on.

| Project | References | Role |
|---|---|---|
| `Example.Library` | this package (runtime + generator) | The ordinary case. Attribute components, marker embedded automatically, aggregated by `AddAllGeneratedComponents()` |
| `Example.ThirdPartyLibrary` | MEDI abstractions only | Stands in for a third party. Registrations live in its own extension methods, invisible to the application's generator |

Three features need that second shape and would silently stop demonstrating anything without it:

* **Convention scanning with `Assembly`** — `ExternalWorker` carries no attributes and is registered by
  a pattern from the application side
* **`[GenerateComponentFactory]`** — `ReportedService` is registered by the library's own
  `AddReportedService()`, so nothing triggers factory generation until the attribute asks for it
* **Runtime fallback diagnostics** — `MessageSource` never gets a factory, so the report classifies it
  as `RuntimeFallback` and `DescribeRuntimeFallbacks()` suggests it

Adding a generator reference to that project would collect its `Add*` calls, generate factories for
both types, and break the last two demos. Its lack of a reference is the point, not an omission.
