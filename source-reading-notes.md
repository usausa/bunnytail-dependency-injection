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

### One method to call, and the unit behind it

| Member | Role | Emitted when |
|---|---|---|
| `AddGeneratedComponents()` — extension | **The only method users call.** Registers every referenced module flatly, then this assembly's own components | components or referenced modules exist |
| `RegisterComponents(IServiceCollection)` — plain static, `[EditorBrowsable(Never)]` | The registration unit of **one module**: this assembly's components only. The cross-assembly integration point the aggregation calls | this assembly has attribute components |

Whatever the module layout, `AddGeneratedComponents()` is the right call. With nothing else
referenced it is exactly "this assembly's components"; with referenced modules it adds theirs. There
is no list of libraries to track and nothing to forget when a reference is added.

**Why a per-module member has to exist.** The aggregation lives in the application's assembly and must
call into each referenced assembly, so a public entry point is required there. The application's
generator deliberately reads only the `[ComponentModule]` assembly attribute — one attribute list per
reference — and never enumerates the types inside references, which is what keeps the incremental cost
flat. It therefore cannot inline a referenced library's registrations; it can only call them.

**Why the two cannot be one member.** If a single method registered "its own plus its references",
every module would re-register its own dependencies when the aggregation invoked it, and a diamond
reference (A→B, A→C, B→D, C→D) would register D twice. MEDI accepts duplicate registrations, so this
would surface quietly as an extra element in `IEnumerable<T>`. The split deduplicates **at compile
time** through flat enumeration and costs nothing at runtime.

**What the unit does not have to be.** It does not have to be an extension method — the generated
aggregation already calls it in static form — and it does not have to share the user-facing name. It
is emitted as a plain static `RegisterComponents`, so it never appears in `IServiceCollection`
completion and the API surface users see is a single `AddGeneratedComponents()`.

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

---

## Findings that belong in the README

### Optional constructor parameters leave the generated path

A constructor with **any** parameter that has a default value is excluded from factory generation
altogether — the generator clears both the unkeyed and the keyed eligibility flags for it. A generated
factory resolves its dependencies the `GetRequiredService` way and has no place for the "not
registered, so use the default" branch that MEDI's rules require, so the type is handed to the runtime
path instead.

The consequence for a user is a performance one, and it is currently undocumented: adding a single
optional parameter to a constructor moves that type off the generated path permanently. Worth adding
to the limitations list, phrased as a trade-off rather than a defect — the behaviour is exactly MEDI's,
only the resolution path differs.

Everything else about optional parameters keeps working: `ParameterDefaults` normalizes the awkward
cases that reflection reports oddly (a value type default arrives as `null`, an enum default may
arrive as its underlying integral type).

### Exception message formatting is not uniform

`TypeNameHelper` renders a type the way MEDI's messages do — `MyApp.IRepository<MyApp.Foo>` rather
than the raw `FullName` form — and it is used in exactly one message, the circular dependency report,
where an unreadable chain would defeat the message's purpose.

The other messages ("No service for type ...", "Unable to resolve service for type ...") interpolate
the type directly, so a generic type reads differently depending on which error you hit. The
compatibility suite passes either way. Deciding whether to unify them is a small open question, not a
bug.

### Per-provider state, process-wide factory catalog

Nothing about a built provider is process-global. Singletons live in the provider's root scope, and the
accessors that hold them belong to that provider's registry, so **two providers built in the same
process share no instances**. Rebuilding a provider per test is safe, and so is running several
providers side by side.

The generated factories are the opposite: `GeneratedComponentRegistry` is static, populated once from
a `[ModuleInitializer]`, and read by every provider in the process. The split is deliberate — *what can
be constructed* is immutable knowledge worth sharing, *what has been constructed* has a lifetime and
must not be.

One consequence worth stating for users: `BuildGeneratedServiceProvider()` returns the concrete
`GeneratedServiceProvider`, not `IServiceProvider`, so the typed instance methods
(`GetRequiredService<T>()` and friends) bind ahead of MEDI's extension methods and skip the
`ISupportRequiredService` type test and the double interface dispatch. Assigning the result to an
`IServiceProvider` variable gives that up.

### What "both paths share one runtime core" actually means

The claim is worth stating concretely, because it is the reason two construction paths can coexist
without drifting apart. `ServiceAccessor` — the abstract base every service table entry derives from —
owns lifetime management (transient / singleton / scoped), the cache location, and disposal tracking.
Derived classes implement one method: *how to construct the instance*. The generated path and the
runtime path are two such derived classes.

So the only thing that can differ between the paths is construction. Lifetime, caching, disposal order
and the timing of the initialization callback are decided by shared code, which is why a type moving
between paths — because a registration changed, or an assumption was rejected — cannot change its
observable behaviour.

Two consequences worth documenting:

* Singletons are held on the accessor, and accessors belong to a provider's registry, so a singleton
  resolved for the first time from a child scope is still constructed **in the root scope context** —
  the injected `IServiceProvider` points at the root, as MEDI requires. Otherwise a singleton would
  capture a scope that later gets disposed.
* Whether disposal needs tracking is settled from the implementation type when the accessor is built,
  so resolution carries no runtime type check. Only user factories, whose implementation type is
  unknown, keep tracking permanently on.

### Instance registrations are never disposed by the container

`AddSingleton<IFoo>(new Foo())` hands the container something the caller owns, so it is not tracked for
disposal — the container disposes only what it constructed. This matches MEDI, and it pairs with the
initialization rule already documented ("factory and instance registrations are user-owned and never
touched"). Worth stating for disposal as well, since it is a common expectation mismatch.

### The README explains the generator but not why resolution is fast

"How it works" currently covers what the generator collects and emits, and how generated code stays
correct. It says nothing about the runtime side, even though the benchmark table right below it is
mostly measuring exactly that. A reader who wants to know *why* `Singleton` lands at ~1ns and `Scoped`
at ~2.6ns has nowhere to look.

Three mechanisms account for most of it, and each is short to state:

* **An immutable service table.** Registrations realizable at build time are frozen into a table that
  is never mutated, so resolution reads it lock-free — no synchronization on the hot path. Runtime
  additions rebuild and swap the table instead of mutating it.
* **The constant short-circuit.** Once a singleton has been resolved, the value is promoted into the
  table node itself, so later resolutions are a table lookup plus one field read, with no virtual call
  on the accessor at all.
* **Slot-indexed scopes.** A scoped instance lives at a fixed array index assigned when the accessor is
  built, so a scoped read is an array index rather than a hash lookup.

Worth noting that the first two came out of measurement rather than intuition: the sandbox's
`ResolutionEntryBenchmark` compared the accessor-virtual-call shape against the constant short-circuit
(with disassembly) before the latter shipped, and `KeyedLookupBenchmark` did the same for the keyed
table layout. That is a concrete illustration of what the sandbox project is for.

### The assumption table in the README is missing one row

"How generated code stays correct" lists four assumptions that are verified when an entry is realized.
The adoption check enforces a fifth: **every constructor argument must be a service resolution, with no
default-value fallback**. It is the runtime-side half of the generator rule noted above — a constructor
with an optional parameter is excluded from generation, and the engine independently refuses to adopt a
factory whose argument plan falls back to a default.

Adding the row keeps the table honest and gives the optional-parameter limitation a place to point at.

Two more things about that table worth stating in prose, because they explain *why* the mechanism is
trustworthy rather than merely present:

* Verification compares **delegate references**, not type names or registration shapes. The question
  asked is "would resolving this dependency right now invoke exactly this delegate?", answered by
  identity, so nothing is inferred and nothing is missed.
* A failed assumption falls back **silently** — no diagnostic, no warning. That is the intended
  behaviour, and it is what lets `Replace`, a lifetime change, a decorator or a factory registration
  keep working unchanged. Worth saying out loud so the silence does not read as an oversight.

### How the factory report classifies a registration

The status table in the README says what each status means. What it does not say is where the answer
comes from, and that is what makes the report trustworthy: **the status is read off the accessor the
engine actually built**, not inferred from the shape of the registration.

| Accessor built | Status |
|---|---|
| `FactoryAccessor` / `DependencyFactoryAccessor` / `KeyedFactoryAccessor` / `KeyedDependencyFactoryAccessor` | `Generated` |
| `ConstructorAccessor` | `RuntimeFallback` |
| nothing could be built | `Unresolvable` |
| anything else | `NotApplicable` |

So `Generated` means the generated factory passed every assumption check and was adopted for real — not
that a factory merely exists for the type. A type whose assumptions were rejected reports
`RuntimeFallback` even though its factory is sitting in the registry unused. That is exactly the
distinction you want when hunting for `[GenerateComponentFactory]` candidates.

Registrations that carry no implementation type (factory, instance and open generic definition
registrations) are classified `NotApplicable` **without being realized at all** — there is nothing to
generate for them, so they are short-circuited before the accessor is built. A useful side effect is
that a user-supplied delegate can never be mistaken for a generated factory.

Two properties worth documenting because they surprise people:

* **One row per `(service type, key)` pair, describing the last registration.** Single resolution takes
  the last registration under MEDI's last-wins rule, so that is the one classified. Register the same
  service three times and the report still shows one row.
* **Every classified registration is realized.** No instances are created, but accessors are built for
  the whole set, which is why this is a development-time tool and not something to leave in a release
  path. The README already warns about this; the reason is that the report has to build the accessor to
  be able to look at it.
