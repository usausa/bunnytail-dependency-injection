# 🐰 BunnyTail.DependencyInjection

| Package | Info |
|:-|:-|
| BunnyTail.DependencyInjection | [![NuGet](https://img.shields.io/nuget/v/BunnyTail.DependencyInjection.svg)](https://www.nuget.org/packages/BunnyTail.DependencyInjection) |

## ❓ What is this?

A BunnyTail extension for Microsoft.Extensions.DependencyInjection - not another DI container. You keep the standard MEDI API with its exact semantics; this package swaps the engine underneath for an AOT-safe service provider that wires object graphs with source generated factories instead of reflection.

* 🤝 **100% MEDI compatible** - passes the official `Microsoft.Extensions.DependencyInjection.Specification.Tests` suite in full (base + keyed, **143/143**), and drops in via `IServiceProviderFactory`
  * Keyed services: `KeyedService.AnyKey`, `[ServiceKey]`, `[FromKeyedServices]`
  * Scope-aware injected `IServiceProvider`
  * Enumerable semantics and constrained open generics
  * Container-tracked reverse-order disposal
* 🛡️ **AOT safe** - no `Reflection.Emit` / `Expression.Compile` on the resolution path. Verified on NativeAOT with zero trim/AOT warnings
* ⚡ **Source generator powered** - constructor selection, lifetime shapes, disposal tracking and transient graph inlining are all settled at compile time
* 🔎 **Automatic registration** - components are collected from attributes, existing `Add*` calls and naming conventions

Only need registration generation on top of the stock MEDI engine? That is the sibling package [BunnyTail.ServiceRegistration](https://www.nuget.org/packages/BunnyTail.ServiceRegistration) - this package replaces the engine itself.

## 🚀 Usage

Two entry points swap the engine in: `BuildGeneratedServiceProvider()` on `IServiceCollection`, and `GeneratedServiceProviderFactory` for hosts that take an `IServiceProviderFactory`.

### 🟥 ServiceCollection

```csharp
using var provider = new ServiceCollection()
    .AddGeneratedComponents()
    .AddSingleton<IFoo, Foo>()
    .BuildGeneratedServiceProvider();
```

### 🟥 Generic Host

```csharp
using var host = Host.CreateDefaultBuilder(args)
    .UseServiceProviderFactory(new GeneratedServiceProviderFactory())
    .ConfigureServices(static services => services.AddGeneratedComponents())
    .Build();
```

### 🟥 ASP.NET Core

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseServiceProviderFactory(new GeneratedServiceProviderFactory());
builder.Services.AddGeneratedComponents();
```

* Framework services registered by the host take the runtime path
* Application components take the generated path
* Both with identical semantics
* `Example.WebApplication` is a runnable minimal API using this setup

## 📖 API reference

Every entry point carries `Generated` in its name: that is the source generated, reflection-free path.

### 🟦 Extension methods

| Method | Target | Description |
|---|---|---|
| `AddGeneratedComponents()` | `IServiceCollection` | The one method to call. Registers the attribute components (`[Singleton]` / `[Scoped]` / `[Transient]`) of this assembly **plus every referenced component module** (transitively, each exactly once). Emitted into `<AssemblyName>.GeneratedComponents` whenever components or referenced modules exist |
| `RegisterComponents()` | *(static, not an extension)* | The registration unit of one module: this assembly's components only. The integration point the aggregation calls across assemblies; call it directly only to leave other modules out |
| `BuildGeneratedServiceProvider()` | `IServiceCollection` | Builds the `GeneratedServiceProvider`. The counterpart of MEDI's `BuildServiceProvider()`. An overload takes `Action<GeneratedServiceProviderOptions>` |
| `AddTransient(...)` / `AddKeyedTransient(...)` with `DisposableTracking` | `IServiceCollection` | Transient registration carrying an explicit disposal tracking setting (`TrackingServiceDescriptor` under the hood) |
| *(user defined)* | `IServiceCollection` | Partial methods annotated with `[ComponentRegistration]` get their body generated from class name patterns |

### 🟦 Types

| Type | Description |
|---|---|
| `GeneratedServiceProviderFactory` | `IServiceProviderFactory<IServiceCollection>` for `UseServiceProviderFactory` (Generic Host / ASP.NET Core) |
| `GeneratedServiceProvider` | The provider itself. Implements `IServiceProvider`, `IKeyedServiceProvider`, `ISupportRequiredService`, `IServiceScopeFactory`, `IServiceProviderIsService`, `IServiceProviderIsKeyedService`, `IDisposable`, `IAsyncDisposable`. Also exposes typed `GetService<T>()` / `GetRequiredService<T>()` / `GetKeyedService<T>()` / `GetRequiredKeyedService<T>()` instance methods that skip the MEDI extension method dispatch |
| `ServiceProviderScope` | A scope, also the injected `IServiceProvider` inside that scope. Same typed methods as above |
| `GeneratedServiceProviderOptions` | Provider options: `TrackTransientDisposables` plus per-type `EnableTracking` / `DisableTracking` overrides |
| `ITypeActivator` | Unregistered, caller-owned construction: `Activate(Type)` / `Activate<T>()`, implemented by the provider and scopes and pre-registered as a built-in service. Constructor injection, `[Inject]` and `PostConstruct` apply; the instance is never tracked |
| `DisposableTracking` / `TrackingServiceDescriptor` | Three-state disposal tracking setting and the `ServiceDescriptor` subclass that carries it per registration |
| `ServiceFactoryReportExtensions` | Development-time diagnostics (`BunnyTail.DependencyInjection.Diagnostics`) as provider extension methods: `CreateFactoryReport()` classifies every registration by resolution path, `DescribeRuntimeFallbacks()` emits ready-to-paste `[GenerateComponentFactory]` lines for the publicly constructible ones |

### 🟦 Attributes

| Attribute | Target | Description |
|---|---|---|
| `[Singleton]` / `[Scoped]` / `[Transient]` | class | Registration with `As`, `Key` and `PostConstruct` parameters. `[Transient]` also takes `Tracking` (disposal tracking) |
| `[Inject]` | property | Property injection after construction |
| `[ComponentRegistration]` | partial method | Convention based registration with `Lifetime`, `Pattern`, `Namespace` and `Assembly` parameters |
| `[ComponentModule]` | assembly | Marks the module type aggregated by `AddGeneratedComponents()`. Emitted automatically for assemblies with attribute components; hand-write it when a library has none |
| `[GenerateComponentFactory]` | assembly | Generates a factory for a type without registering it, for libraries you do not control. Supports `PostConstruct` |
| `IInitializable` | interface | Initialization callback invoked after construction |

### 🟦 MSBuild properties

| Property | Default | Description |
|---|---|---|
| `DependencyInjectionIgnoreInterface` | (none) | Comma-separated interface names excluded from automatic registration. `IDisposable` / `IAsyncDisposable` / `IInitializable` are always excluded |

## 💡 Feature examples

### 🟨 Attribute based registration

```csharp
using BunnyTail.DependencyInjection;

[Singleton]
public sealed class Component1;

[Transient]
public sealed class Component2(Component1 component);

[Singleton(As = typeof(IService), Key = "primary")]
public sealed class Component4 : IService;
```

```csharp
using var provider = new ServiceCollection()
    .AddGeneratedComponents()
    .BuildGeneratedServiceProvider();

var component = provider.GetRequiredService<Component2>();
var keyed = provider.GetRequiredKeyedService<IService>("primary");
```

* One attribute maps to one MEDI registration shape, and nothing is registered implicitly. Plain is `AddX<TImpl>()`, `As` is `AddX<TService, TImpl>()`, `Key` is the keyed form
* `WithInterfaces = true` keeps `AddX<TImpl>()` and adds a delegate registration per **directly declared** interface, which is what preserves a single instance. Combining it with `As` reports `BTDI0012`
* `[FromKeyedServices]` and `[ServiceKey]` follow MEDI rules, on constructor parameters and `[Inject]` properties alike

### 🟨 Convention based registration

```csharp
public static partial class ServiceCollectionExtensions
{
    [ComponentRegistration(Lifetime.Singleton, "Service$")]
    [ComponentRegistration(Lifetime.Scoped, "Repository$", Namespace = "MyApp.Data")]
    public static partial IServiceCollection AddServices(this IServiceCollection services);

    [ComponentRegistration(Lifetime.Transient, "View$")]
    internal static partial IServiceCollection AddViews(this IServiceCollection services);
}
```

* `As` and `WithInterfaces` behave exactly as on the lifetime attributes, so there is nothing extra to learn. The default registers the implementation only, so views and view models need no extra option
* `Pattern` matching no type reports `BTDI0013`, so a typo does not silently register nothing
* `Assembly` scans a referenced assembly from metadata (publicly accessible classes only), which brings libraries without the generator into convention registration. An unreferenced name reports `BTDI0003`
* A class can hold any number of registration methods, each with its own patterns and accessibility

### 🟨 Property injection

```csharp
[Transient]
public sealed class Component3(Component1 component)
{
    [Inject]
    public Component2 Two { get; set; } = default!;

    [Inject]
    [FromKeyedServices("primary")]
    public IService Service { get; set; } = default!;
}
```

* Runs after the constructor and before the initialization callback
* Applies on every path, including type activation
* Constructor injection is still the default choice: property injection exists for types the container constructs but whose constructor is not yours to change

### 🟨 Initialization callback

```csharp
[Singleton(PostConstruct = nameof(Setup))]
public sealed class Component5
{
    public void Setup()
    {
    }
}

[Transient]
public sealed class Component6 : IInitializable
{
    public void Initialize()
    {
    }
}
```

* Runs after the constructor and `[Inject]` property injection, with identical timing on both paths
* Must be a public parameterless instance method returning void. `PostConstruct` wins when both are present
* Only container-constructed instances are initialized: factory and instance registrations are user-owned and never touched

### 🟨 Existing Add* registrations

Registration calls in user code are collected by the generator, so existing MEDI registration code gets the generated path with no changes.

* `Add{Lifetime}` / `TryAdd{Lifetime}` - generic overloads and non-generic `typeof` overloads (including single-argument self registration)
* `AddKeyed{Lifetime}` / `TryAddKeyed{Lifetime}` - generic and `typeof` overloads
* `Add` / `TryAdd` / `TryAddEnumerable` taking `ServiceDescriptor.{Lifetime}<TService, TImplementation>()`
* Open generic definition pairs such as `AddTransient(typeof(IRepository<>), typeof(Repository<>))`
* Factory, instance and `ServiceDescriptor.Describe` based registrations are not collected - they take the runtime path, with identical semantics

### 🟨 Excluding interfaces from automatic registration

Registration without an explicit `As` covers the class and every interface it implements, inherited ones included.

```xml
<PropertyGroup>
  <DependencyInjectionIgnoreInterface>MyApp.INavigation,System.ComponentModel.INotifyPropertyChanged</DependencyInjectionIgnoreInterface>
</PropertyGroup>
```

* Comma separated, matched against the namespace-qualified name (write generics as they appear in source, such as `MyApp.IHandler<MyApp.Command>`)
* Applies to attribute and convention registration alike. An explicit `As = typeof(...)` is never affected

### 🟨 Multi-project modules

```csharp
// Class library (references BunnyTail.DependencyInjection and the generator)
[Singleton]
public sealed class LibraryComponent;
```

```csharp
// Application
using var provider = new ServiceCollection()
    .AddGeneratedComponents()          // referenced modules + own components
    .BuildGeneratedServiceProvider();
```

* A library compiles its components into its own module, and `AddGeneratedComponents()` discovers every referenced module transitively, each exactly once. There is no list of libraries to keep track of
* Each module also gets `RegisterComponents(IServiceCollection)` for its own components only. That is the integration point the aggregation calls across assemblies; call it directly only to leave other modules out
* `Example` with `Example.Library` shows this layout

The module marker is embedded for assemblies that have attribute components. A library that has none declares one by hand, and only one marker per assembly is allowed:

```csharp
[assembly: BunnyTail.DependencyInjection.ComponentModule(typeof(MyLibrary.LibraryModule))]

namespace MyLibrary;

public static class LibraryModule
{
    public static IServiceCollection RegisterComponents(IServiceCollection services)
    {
        services.AddSingleton<IMessageSource>(static provider => new MessageSource(provider.GetRequiredService<Config>().Prefix));
        return services;
    }
}
```

### 🟨 Types you do not control

The generator only sees the source it compiles, so a library registering its own types through its own extension method resolves through the runtime path. `[GenerateComponentFactory]` prepares a factory for such a type **without registering it**:

```csharp
[assembly: GenerateComponentFactory(typeof(SomeLibrary.SomeService))]
[assembly: GenerateComponentFactory(typeof(SomeLibrary.OtherService), PostConstruct = nameof(SomeLibrary.OtherService.Initialize))]

// registration still comes from the library
services.AddSomeLibrary();
```

* Eligible targets: publicly accessible concrete classes with a usable public constructor. Anything else reports `BTDI0004`, and an invalid `PostConstruct` name reports `BTDI0006`
* `PostConstruct` runs on both paths, so behavior never depends on which one resolved the type

To find the types worth marking, ask the built provider which registrations fell back:

```csharp
using BunnyTail.DependencyInjection.Diagnostics;

// Ready-to-paste attribute lines, limited to types the generated code can actually construct
Console.Write(provider.DescribeRuntimeFallbacks());

// A predicate narrows it further, and CreateFactoryReport() gives the full classification
Console.Write(provider.DescribeRuntimeFallbacks(static x => x.Lifetime != ServiceLifetime.Singleton));
```

* ⚠️ Development-time only: it realizes every entry, so keep it out of release paths
* `CreateFactoryReport()` classifies each registration as `Generated`, `RuntimeFallback`, `NotApplicable` (factory, instance or open generic definition, which the container never constructs) or `Unresolvable`
* Not every fallback is worth marking: factory and instance registrations cannot benefit, singletons pay the cost once, and internal types cannot be constructed by generated code. ✅ The ones that pay off are public transient or scoped services on hot paths

### 🟨 Disposable tracking control

MEDI keeps a reference to every disposable transient and disposes it with the resolving scope. When another framework owns those objects - a navigation stack disposing its views, an MVVM layer disposing its view models - the container copy is retained until shutdown and then disposed a second time. Transient tracking can therefore be turned off:

```csharp
// Provider level
using var provider = services.BuildGeneratedServiceProvider(static o => o.TrackTransientDisposables = false);
```

```csharp
// Registration level
[Transient(Tracking = DisposableTracking.Disabled)]   // attribute components
public sealed class MainPageViewModel;

services.AddTransient<MainPage>(DisposableTracking.Disabled);            // Add* registrations
services.AddKeyedTransient<SidePanel>("side", DisposableTracking.Disabled);

// Registrations you do not control, by type
using var provider = services.BuildGeneratedServiceProvider(static o =>
{
    o.TrackTransientDisposables = false;
    o.EnableTracking(typeof(ThirdPartyJob));   // keep container disposal for this one
});
```

* Precedence: registration setting > per-type override > provider default (`TrackTransientDisposables`, default `true` = MEDI behavior)
* Transients only. Singleton and scoped disposal always stays container owned
* Uniform across every resolution path: generated factories, the runtime fallback, `ImplementationFactory` lambdas, keyed services
* An untracked instance is owned by whoever resolved it. Constructor injected dependencies keep their own settings

### 🟨 Type activation

`ITypeActivator` is the reflection-free counterpart of `ActivatorUtilities.CreateInstance()`. It constructs a type that is **not registered as a service**, taking constructor arguments from the container, and hands the instance to the caller:

```csharp
var page = provider.Activate<MainPage>();   // on the provider and on scopes
var view = activator.Activate(viewType);    // ITypeActivator is a built-in service, so it can be injected
```

Differences from `ActivatorUtilities.CreateInstance()`:

* Generated factories instead of reflection, so it stays on the AOT safe path. Generic call sites and `typeof` literal arguments are resolved at build time; a runtime `Type` falls back to the runtime path, so mark such types with `[GenerateComponentFactory]`
* The full construction pipeline applies: `[Inject]` properties and `PostConstruct` / `IInitializable`
* Everything comes from the container. There is no overload taking extra constructor arguments
* Deliberately **not** an `IServiceProvider` extension method: on a container that cannot honor the contract the API simply does not exist, instead of failing at runtime

Shared with the container path:

* Existing registrations are ignored: activation always constructs a fresh instance, and the container never tracks or disposes it
* Scope aware: activating from a scope resolves scoped dependencies from that scope

## ⚙️ How it works

### 🟪 What the generator collects

All sources are incremental - editing a method body regenerates nothing.

| Source | Collected from | Result |
|---|---|---|
| Attributes | `[Singleton]` / `[Scoped]` / `[Transient]` classes | `RegisterComponents()` body + a factory per implementation |
| `Add*` calls | `AddSingleton<T>()`, `typeof` overloads, `AddKeyed*`, `ServiceDescriptor` based `Add` / `TryAdd` / `TryAddEnumerable` in user code | A factory per implementation (the registration itself stays in user code) |
| Conventions | `[ComponentRegistration]` partial methods, optionally scanning a referenced assembly | The method body + a factory per matched implementation |
| Referenced modules | Assemblies marked with `[ComponentModule]` | The `AddGeneratedComponents()` aggregation |

### 🟪 What the generator emits

Every collected implementation type gets a factory registered from a `[ModuleInitializer]`, so startup pays no discovery cost:

```csharp
// Emitted for [Transient] Service(Repository repository, Logger logger) with a singleton Logger
GeneratedFactoryRegistry.Register(
    typeof(Service),
    [typeof(Repository), typeof(Logger)],
    [new InlinedDependency(typeof(Repository), typeof(Repository))],
    [new DependencyPlan(typeof(Logger), typeof(Logger))],
    static (provider, dependencies) => new Service(
        new Repository(),                                   // transient dependency inlined as a literal new
        Unsafe.As<Logger>(dependencies[0])!));                      // singleton dependency read from a resolved slot
```

Three shapes come out of this, chosen per dependency lifetime:

* 🧩 **Inline expansion** - a transient dependency becomes a literal `new` inside the parent factory, collapsing a whole transient graph into one allocation site
* 📌 **Instance slots** - an unambiguous singleton dependency is resolved once, then read straight from the dependency array
* 🎯 **Accessor slots** - scoped and non-inlinable dependencies get a validated accessor handle, skipping the service table lookup on every resolution

Two more cases are handled at compile time:

* `IEnumerable<T>` sets whose elements are all inlinable transients become an array literal
* Closed forms of open generic registrations appearing in code - as `typeof(IRepository<Foo>)`, a constructor parameter, or a property type - get their own factories, which is what makes value type arguments AOT safe. A closed form known only at runtime has no factory, and NativeAOT cannot instantiate it with a value type argument (`BTDI0011` warns about the compile-time visible cases)

### 🟪 How generated code stays correct

Generated factories are assumptions about registrations, and registrations are only final at runtime. Each assumption is verified when the provider realizes an entry, and any mismatch silently falls back to the runtime path:

| Assumption | Verified by |
|---|---|
| The constructor MEDI selects is the one the factory was generated for | Parameter type comparison |
| Every inlined transient dependency still resolves to that implementation's generated factory as a transient | Delegate reference comparison per dependency |
| Every dependency slot still matches its planned lifetime and implementation | Same comparison, per slot |
| An enumerable still has the same elements in the same order | Ordered per-element comparison |

So `Replace`, a lifetime change, a factory registration and a decorator all keep working - they simply take the runtime path.

### 🟪 The two paths

Both paths share one runtime core, so lifetime, disposal and collection semantics are always identical:

| Path | Registrations | Implementation |
|---|---|---|
| Generated | Visible at compile time (attributes, `Add*` calls, conventions) | Generated factories with literal `new`. Transient dependency graphs inlined into a single factory. Reflection-free |
| Runtime | Known only at runtime (framework assemblies, factories, instances, replacements) | `ConstructorInfo.Invoke` based. No Emit, so it works on NativeAOT too |

## 📂 Samples

| Project | Contents |
|---|---|
| `Example` | Console sample asserting every feature: attribute components, module aggregation, convention registration scanning a referenced assembly, `[GenerateComponentFactory]`, the diagnostic report, and standard `Add*` registrations (generic, open generic, keyed, `TryAddEnumerable`, factory) |
| `Example.Library` | Class library referencing this package: its components are marked as a module automatically and aggregated by the application |
| `Example.ThirdPartyLibrary` | Stands in for a third party library, referencing nothing of this package. Its registrations come from its own extension methods, so they are invisible to the application's generator - the target of convention scanning, `[GenerateComponentFactory]` and the runtime fallback diagnostics |
| `Example.WebApplication` | ASP.NET Core minimal API with the container replaced, showing singleton / scoped / transient behavior per request |

## ⚡ Benchmark

Resolution cost of the generated path against Microsoft.Extensions.DependencyInjection and Smart.Resolver. For reference purpose only.

* All three providers receive the identical `IServiceCollection`
* The same validator checks every provider before measurement, so the scenarios resolve equivalent object graphs
* Each method repeats its operation five times and `OperationsPerInvoke = 5` divides the result - **Mean is the cost of one operation**
* Measured with the `BunnyTail.DependencyInjection.Benchmark` project

| Scenario | Measured operation |
|---|---|
| `Singleton` | Resolve a singleton |
| `Transient` | Resolve a transient |
| `Combined` | Resolve a transient holding one singleton dependency |
| `Complex` | Resolve a transient with six dependencies (three singletons, three transients inlined) |
| `Generics` | Resolve a closed form of an open generic registration |
| `Scoped` | Resolve a scoped service from an already-populated scope |
| `Keyed` | Resolve a keyed singleton |
| `MultipleSingleton` | Enumerate an `IEnumerable<T>` of five singletons |
| `MultipleTransient` | Enumerate an `IEnumerable<T>` of five transients |
| `AspNet` | Create a scope, resolve a controller graph (three transients sharing one scoped service), dispose the scope |

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
AMD Ryzen AI 9 HX 370 w/ Radeon 890M 2.00GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.302
  [Host]    : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4
  MediumRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4

Job=MediumRun  Jit=RyuJit  Platform=X64  
IterationCount=15  LaunchCount=2  WarmupCount=10  

```

### 🟧 Comparison

Ratio is BunnyTail divided by the other provider, so **lower is better** - `0.21` means BunnyTail takes 21% of that provider's time.

* ✅ BunnyTail is faster
* ➖ tie - the 99.9% confidence intervals overlap
* 🔻 BunnyTail is slower

| Method | BunnyTail | MEDI | Smart | vs MEDI | vs Smart |
|---|---:|---:|---:|:---|:---|
| Singleton | 1.045 ns | 5.005 ns | 1.244 ns | ✅ 0.21 | ✅ 0.84 |
| Transient | 4.542 ns | 7.203 ns | 3.381 ns | ✅ 0.63 | 🔻 1.34 |
| Combined | 5.330 ns | 8.043 ns | 5.239 ns | ✅ 0.66 | ➖ 1.02 |
| Complex | 14.685 ns | 17.710 ns | 21.214 ns | ✅ 0.83 | ✅ 0.69 |
| Generics | 4.025 ns | 4.774 ns | 2.532 ns | ✅ 0.84 | 🔻 1.59 |
| Scoped | 2.628 ns | 19.615 ns | 11.493 ns | ✅ 0.13 | ✅ 0.23 |
| Keyed | 4.146 ns | 7.515 ns | 3.661 ns | ✅ 0.55 | 🔻 1.13 |
| MultipleSingleton | 2.892 ns | 7.429 ns | 2.903 ns | ✅ 0.39 | ➖ 1.00 |
| MultipleTransient | 20.179 ns | 21.545 ns | 16.568 ns | ✅ 0.94 | 🔻 1.22 |
| AspNet | 43.945 ns | 91.604 ns | 66.968 ns | ✅ 0.48 | ✅ 0.66 |

Allocation per operation:

| Method | BunnyTail | MEDI | Smart |
|---|---:|---:|---:|
| Singleton | 0 B | 0 B | 0 B |
| Transient | 24 B | 24 B | 24 B |
| Combined | 24 B | 24 B | 24 B |
| Complex | 136 B | 136 B | 136 B |
| Generics | 24 B | 24 B | 10 B |
| Scoped | 0 B | 0 B | 32 B |
| Keyed | 0 B | 0 B | 0 B |
| MultipleSingleton | 0 B | 6 B | 0 B |
| MultipleTransient | 190 B | 190 B | 184 B |
| AspNet | 312 B | 448 B | 232 B |

Against MEDI:

* ✅ Faster in all 10 scenarios
* ✅ Widest margins on `Scoped` (0.13), `Singleton` (0.21), `MultipleSingleton` (0.39) and `AspNet` (0.48)
* ✅ Allocates less on `AspNet` (312 B vs 448 B) and `MultipleSingleton` (0 B vs 6 B)

Against Smart.Resolver - mixed:

* ✅ Ahead on `Scoped` (0.23), `AspNet` (0.66), `Complex` (0.69) and `Singleton` (0.84)
* ✅ Allocates nothing on `Scoped`, where Smart takes 32 B per call
* 🔻 Behind on `Keyed` (1.13), `MultipleTransient` (1.22), `Transient` (1.34) and `Generics` (1.59)
* ➖ `MultipleSingleton` (1.00) and `Combined` (1.02) are ties

<details>
<summary>Full BenchmarkDotNet output</summary>

#### BunnyTail.DependencyInjection

| Method            | Mean      | Error     | StdDev    | Min       | Max       | P90       | Gen0   | Allocated |
|------------------ |----------:|----------:|----------:|----------:|----------:|----------:|-------:|----------:|
| Singleton         |  1.045 ns | 0.0221 ns | 0.0324 ns |  1.016 ns |  1.116 ns |  1.089 ns |      - |         - |
| Transient         |  4.542 ns | 0.0283 ns | 0.0406 ns |  4.481 ns |  4.613 ns |  4.591 ns | 0.0029 |      24 B |
| Combined          |  5.330 ns | 0.0475 ns | 0.0712 ns |  5.199 ns |  5.511 ns |  5.429 ns | 0.0029 |      24 B |
| Complex           | 14.685 ns | 0.3224 ns | 0.4624 ns | 14.128 ns | 15.813 ns | 15.314 ns | 0.0162 |     136 B |
| Generics          |  4.025 ns | 0.0383 ns | 0.0549 ns |  3.948 ns |  4.168 ns |  4.086 ns | 0.0029 |      24 B |
| Scoped            |  2.628 ns | 0.0098 ns | 0.0141 ns |  2.610 ns |  2.672 ns |  2.645 ns |      - |         - |
| Keyed             |  4.146 ns | 0.0220 ns | 0.0315 ns |  4.095 ns |  4.228 ns |  4.178 ns |      - |         - |
| MultipleSingleton |  2.892 ns | 0.0137 ns | 0.0201 ns |  2.859 ns |  2.940 ns |  2.924 ns |      - |         - |
| MultipleTransient | 20.179 ns | 0.4369 ns | 0.6266 ns | 18.858 ns | 21.600 ns | 21.065 ns | 0.0227 |     190 B |
| AspNet            | 43.945 ns | 0.6157 ns | 0.9216 ns | 42.534 ns | 46.041 ns | 45.272 ns | 0.0373 |     312 B |

#### Microsoft.Extensions.DependencyInjection

| Method            | Mean      | Error     | StdDev    | Median    | Min       | Max       | P90       | Gen0   | Allocated |
|------------------ |----------:|----------:|----------:|----------:|----------:|----------:|----------:|-------:|----------:|
| Singleton         |  5.005 ns | 0.0722 ns | 0.1036 ns |  4.967 ns |  4.883 ns |  5.227 ns |  5.153 ns |      - |         - |
| Transient         |  7.203 ns | 0.0976 ns | 0.1461 ns |  7.174 ns |  6.877 ns |  7.493 ns |  7.403 ns | 0.0029 |      24 B |
| Combined          |  8.043 ns | 0.2178 ns | 0.3124 ns |  7.991 ns |  7.472 ns |  8.502 ns |  8.393 ns | 0.0029 |      24 B |
| Complex           | 17.710 ns | 0.1649 ns | 0.2417 ns | 17.715 ns | 17.214 ns | 18.328 ns | 17.983 ns | 0.0162 |     136 B |
| Generics          |  4.774 ns | 0.0880 ns | 0.1290 ns |  4.827 ns |  4.558 ns |  4.971 ns |  4.931 ns | 0.0029 |      24 B |
| Scoped            | 19.615 ns | 0.0498 ns | 0.0715 ns | 19.610 ns | 19.513 ns | 19.774 ns | 19.714 ns |      - |         - |
| Keyed             |  7.515 ns | 0.0443 ns | 0.0650 ns |  7.510 ns |  7.427 ns |  7.631 ns |  7.623 ns |      - |         - |
| MultipleSingleton |  7.429 ns | 0.0748 ns | 0.1073 ns |  7.423 ns |  7.287 ns |  7.771 ns |  7.558 ns | 0.0008 |       6 B |
| MultipleTransient | 21.545 ns | 0.4177 ns | 0.6252 ns | 21.935 ns | 20.670 ns | 22.500 ns | 22.135 ns | 0.0227 |     190 B |
| AspNet            | 91.604 ns | 1.6439 ns | 2.4605 ns | 91.178 ns | 87.828 ns | 96.323 ns | 95.159 ns | 0.0535 |     448 B |

#### Smart.Resolver

| Method            | Mean      | Error     | StdDev    | Min       | Max       | P90       | Gen0   | Allocated |
|------------------ |----------:|----------:|----------:|----------:|----------:|----------:|-------:|----------:|
| Singleton         |  1.244 ns | 0.0137 ns | 0.0201 ns |  1.222 ns |  1.300 ns |  1.263 ns |      - |         - |
| Transient         |  3.381 ns | 0.1346 ns | 0.1974 ns |  3.096 ns |  3.700 ns |  3.570 ns | 0.0029 |      24 B |
| Combined          |  5.239 ns | 0.1267 ns | 0.1896 ns |  4.988 ns |  5.628 ns |  5.546 ns | 0.0029 |      24 B |
| Complex           | 21.214 ns | 0.2167 ns | 0.3177 ns | 20.578 ns | 21.871 ns | 21.751 ns | 0.0162 |     136 B |
| Generics          |  2.532 ns | 0.0249 ns | 0.0357 ns |  2.468 ns |  2.588 ns |  2.575 ns | 0.0011 |      10 B |
| Scoped            | 11.493 ns | 0.0684 ns | 0.1003 ns | 11.299 ns | 11.746 ns | 11.610 ns | 0.0038 |      32 B |
| Keyed             |  3.661 ns | 0.0560 ns | 0.0785 ns |  3.569 ns |  3.837 ns |  3.763 ns |      - |         - |
| MultipleSingleton |  2.903 ns | 0.0142 ns | 0.0208 ns |  2.867 ns |  2.949 ns |  2.934 ns |      - |         - |
| MultipleTransient | 16.568 ns | 0.3496 ns | 0.4901 ns | 15.933 ns | 18.419 ns | 16.925 ns | 0.0220 |     184 B |
| AspNet            | 66.968 ns | 0.6331 ns | 0.9476 ns | 64.941 ns | 69.515 ns | 68.138 ns | 0.0277 |     232 B |

</details>

## 🔗 Link

* [Smart.Resolver](https://github.com/usausa/Smart-Net-Resolver)
