# BunnyTail.Resolver

| Package | Info |
|:-|:-|
| BunnyTail.Resolver | [![NuGet](https://img.shields.io/nuget/v/BunnyTail.Resolver.svg)](https://www.nuget.org/packages/BunnyTail.Resolver) |
| BunnyTail.Resolver.Extensions.DependencyInjection | [![NuGet](https://img.shields.io/nuget/v/BunnyTail.Resolver.Extensions.DependencyInjection.svg)](https://www.nuget.org/packages/BunnyTail.Resolver.Extensions.DependencyInjection) |

## What is this?

Source generator based AOT-safe DI library compatible with Microsoft.Extensions.DependencyInjection.

* **100% MEDI compatible** — passes the complete official `Microsoft.Extensions.DependencyInjection.Specification.Tests` suite (base + keyed, 143/143), including keyed services (`KeyedService.AnyKey`, `[ServiceKey]`, `[FromKeyedServices]`), scope-aware injected `IServiceProvider`, enumerable semantics, constrained open generics and container-tracked reverse-order disposal. Drop-in replacement via `IServiceProviderFactory`
* **AOT safe** — no `Reflection.Emit` / `Expression.Compile` on the resolution path. Verified on NativeAOT with zero trim/AOT warnings
* **Source generator powered** — dependency graphs are resolved at compile time: constructor selection, lifetime shapes, disposal tracking and transient graph inlining are settled before the app starts
* **Automatic registration** — components are collected from attributes, existing `Add*` calls and naming conventions

## Usage

### Attribute based registration

Annotate components with `[Singleton]` / `[Scoped]` / `[Transient]`. The generated `AddGeneratedComponents()` method registers the class itself and all implemented interfaces (interfaces are forwarded to the same instance).

```csharp
using BunnyTail.Resolver;

[Singleton]
public sealed class Component1;

[Transient]
public sealed class Component2(Component1 component)
{
    [Inject]
    public Component3 Three { get; set; } = default!;
}

[Singleton(As = typeof(IService), Key = "primary")]
public sealed class Component4 : IService;
```

```csharp
using var provider = new ServiceCollection()
    .AddGeneratedComponents()          // generated
    .BuildGeneratedServiceProvider();

var component = provider.GetRequiredService<Component2>();
var keyed = provider.GetRequiredKeyedService<IService>("primary");
```

| Parameter | Description |
|---|---|
| `As` | Explicit service type. When omitted, the class itself and all implemented interfaces are registered (`IDisposable` / `IAsyncDisposable` / `IInitializable` excluded) |
| `Key` | Keyed service registration |
| `PostConstruct` | Name of a method invoked after construction and property injection |

`[Inject]` marks a public settable property for property injection after construction. `[FromKeyedServices]` and `[ServiceKey]` on constructor parameters and `[Inject]` properties follow MEDI rules.

### Initialization callback

A component can run initialization after the container constructs it — either name a method on the lifetime attribute, or implement `IInitializable`.

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

The callback runs after constructor and `[Inject]` property injection, with identical timing on both the generated and the runtime path. The method must be a public parameterless instance method returning void; `PostConstruct` takes precedence when both are present. Only container-constructed instances are initialized — factory and instance registrations are user-owned and never touched. Types without a callback pay no resolution cost.

### Convention based registration

Class name patterns generate the registration method body, same as attribute-free bulk registration.

```csharp
public static partial class ServiceCollectionExtensions
{
    [ComponentRegistration(Lifetime.Singleton, "Service$")]
    [ComponentRegistration(Lifetime.Scoped, "Repository$", Namespace = "MyApp.Data")]
    public static partial IServiceCollection AddServices(this IServiceCollection services);
}
```

| Parameter | Description |
|---|---|
| `Lifetime` | Service lifetime: `Transient`, `Singleton`, or `Scoped` |
| `Pattern` | Regex pattern to match class names to register |
| `Namespace` | Namespace prefix to filter classes |
| `Assembly` | Name of a referenced assembly to scan instead of the current project. Types are taken from metadata (publicly accessible classes only), so libraries without the generator can be registered by convention. An unreferenced assembly name is reported as `BTRS0003` |

### Existing Add* registrations

Registration calls in user code are detected by the generator, and reflection-free factories are generated for the implementation types automatically. Existing MEDI registration code benefits from the generated path without any changes. Collected shapes:

* `Add{Lifetime}` / `TryAdd{Lifetime}` — generic overloads and non-generic `typeof` overloads (including single-argument self registration)
* `AddKeyed{Lifetime}` / `TryAddKeyed{Lifetime}` — generic and `typeof` overloads
* `Add` / `TryAdd` / `TryAddEnumerable` taking `ServiceDescriptor.{Lifetime}<TService, TImplementation>()`
* Open generic definition pairs such as `AddTransient(typeof(IRepository<>), typeof(Repository<>))`

Factory, instance and `ServiceDescriptor.Describe` based registrations are not collected — they resolve through the runtime path with identical semantics.

### Types you do not control

The generator only sees the source it compiles. When a library registers its own types through its own extension method — `services.AddSomeLibrary()` — and that library does not reference the generator, those types resolve through the runtime path. `[GenerateComponentFactory]` prepares a factory for such a type **without registering it**, leaving the registration to the library:

```csharp
[assembly: GenerateComponentFactory(typeof(SomeLibrary.SomeService))]

// registration still comes from the library
services.AddSomeLibrary();
```

A `PostConstruct` method name can be specified for types you cannot annotate. It runs on both the generated and the runtime path, so the behavior never depends on which path resolved the type:

```csharp
[assembly: GenerateComponentFactory(typeof(SomeLibrary.SomeService), PostConstruct = nameof(SomeLibrary.SomeService.Initialize))]
```

Only publicly accessible concrete classes with a usable public constructor are eligible; anything else reports `BTRS0004`, and an invalid `PostConstruct` name reports `BTRS0006`.

To find the types worth marking, ask the built provider which registrations fell back at runtime. This is a development-time diagnostic — it realizes every entry (without creating instances), so keep it out of release paths:

```csharp
using BunnyTail.Resolver.Diagnostics;

// Ready-to-paste attribute lines, limited to types the generated code can actually construct
Console.Write(provider.DescribeRuntimeFallbacks());

// A predicate narrows it further — singletons are constructed once, so they rarely pay off
Console.Write(provider.DescribeRuntimeFallbacks(static x => x.Lifetime != ServiceLifetime.Singleton));

// Or inspect the full classification
foreach (var entry in provider.CreateFactoryReport())
{
    Console.WriteLine($"{entry.Status,-16} {entry.Lifetime,-9} {entry.ServiceType.Name}");
}
```

| Status | Meaning |
|---|---|
| `Generated` | Resolved through a generated factory |
| `RuntimeFallback` | No factory, or the assumptions were rejected — a `[GenerateComponentFactory]` candidate |
| `NotApplicable` | Factory, instance or open generic definition registration: the container never constructs the type, so nothing can be generated |
| `Unresolvable` | Could not be realized from the visible registrations |

Not every fallback is worth marking. Factory and instance registrations cannot benefit at all, singletons pay the runtime cost once, and internal types cannot be constructed by generated code. The types that pay off are public transient or scoped services resolved on hot paths.

### Multi-project modules

Components can live in other projects. When a class library references the generator, its components compile into that library's own `GeneratedComponents` module, marked with an assembly level `[ComponentModule]` attribute. The application's generated `AddAllGeneratedComponents()` discovers every referenced module (transitively, each exactly once) and registers them together with the application's own components in a single call.

```csharp
// Class library (references BunnyTail.Resolver and the generator)
[Singleton]
public sealed class LibraryComponent;
```

```csharp
// Application
using var provider = new ServiceCollection()
    .AddAllGeneratedComponents()       // referenced modules + own components
    .BuildGeneratedServiceProvider();
```

Each module's `AddGeneratedComponents()` registers only its own components, so modules can still be registered individually when finer control is needed.

A library that does not reference the generator can also participate by declaring a module by hand: write a static class with an `AddGeneratedComponents(IServiceCollection)` method and mark the assembly with `[assembly: ComponentModule(typeof(...))]`.

```csharp
[assembly: BunnyTail.Resolver.ComponentModule(typeof(MyLibrary.LibraryModule))]

namespace MyLibrary;

public static class LibraryModule
{
    public static IServiceCollection AddGeneratedComponents(this IServiceCollection services)
    {
        services.AddSingleton<IMessageSource, MessageSource>();
        return services;
    }
}
```

The `Example` / `Example.Library1` (generated marker) / `Example.Library2` (hand-written marker) projects contain a working example of both patterns.

## API reference

Every entry point of this library carries `Generated` in its name: it is the source generated, reflection-free path. Nothing here replaces the MEDI API — registrations stay `IServiceCollection`, resolution stays `IServiceProvider`.

### Extension methods

| Method | Target | Description |
|---|---|---|
| `AddGeneratedComponents()` | `IServiceCollection` | Registers the attribute components (`[Singleton]` / `[Scoped]` / `[Transient]`) of **this assembly**. Emitted by the generator into `<AssemblyName>.GeneratedComponents` |
| `AddAllGeneratedComponents()` | `IServiceCollection` | Registers the attribute components of this assembly **plus every referenced component module** (transitively, each exactly once). Emitted whenever components or referenced modules exist |
| `BuildGeneratedServiceProvider()` | `IServiceCollection` | Builds the `ResolverServiceProvider`. The counterpart of MEDI's `BuildServiceProvider()` |
| *(user defined)* | `IServiceCollection` | Partial methods annotated with `[ComponentRegistration]` get their body generated from class name patterns |

### Types

| Type | Description |
|---|---|
| `GeneratedServiceProviderFactory` | `IServiceProviderFactory<IServiceCollection>` for `UseServiceProviderFactory` (Generic Host / ASP.NET Core) |
| `ResolverServiceProvider` | The provider itself. Implements `IServiceProvider`, `IKeyedServiceProvider`, `ISupportRequiredService`, `IServiceScopeFactory`, `IServiceProviderIsService`, `IServiceProviderIsKeyedService`, `IDisposable`, `IAsyncDisposable`. Also exposes typed `GetService<T>()` / `GetRequiredService<T>()` / `GetKeyedService<T>()` / `GetRequiredKeyedService<T>()` instance methods that skip the MEDI extension method dispatch |
| `ServiceProviderScope` | A scope, also the injected `IServiceProvider` inside that scope. Same typed methods as above |
| `ServiceFactoryReportExtensions` | Development-time diagnostics (`BunnyTail.Resolver.Diagnostics`) as provider extension methods: `CreateFactoryReport()` classifies every registration by resolution path, `DescribeRuntimeFallbacks()` emits ready-to-paste `[GenerateComponentFactory]` lines for the publicly constructible ones |

### Attributes

| Attribute | Target | Description |
|---|---|---|
| `[Singleton]` / `[Scoped]` / `[Transient]` | class | Registration with `As`, `Key` and `PostConstruct` parameters |
| `[Inject]` | property | Property injection after construction |
| `[ComponentRegistration]` | partial method | Convention based registration with `Lifetime`, `Pattern`, `Namespace` and `Assembly` parameters |
| `[ComponentModule]` | assembly | Marks the module type aggregated by `AddAllGeneratedComponents()`. Emitted automatically; hand-write it for libraries without the generator |
| `[GenerateComponentFactory]` | assembly | Generates a factory for a type without registering it, for libraries you do not control. Supports `PostConstruct` |
| `IInitializable` | interface | Initialization callback invoked after construction |

## Microsoft.Extensions.DependencyInjection integration

The `BunnyTail.Resolver.Extensions.DependencyInjection` package provides the MEDI bridge: `BuildGeneratedServiceProvider()` and `GeneratedServiceProviderFactory`.

### ServiceCollection

```csharp
using var provider = new ServiceCollection()
    .AddGeneratedComponents()
    .AddSingleton<IFoo, Foo>()
    .BuildGeneratedServiceProvider();
```

### Generic Host

```csharp
using var host = Host.CreateDefaultBuilder(args)
    .UseServiceProviderFactory(new GeneratedServiceProviderFactory())
    .ConfigureServices(static services => services.AddGeneratedComponents())
    .Build();
```

### ASP.NET Core

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseServiceProviderFactory(new GeneratedServiceProviderFactory());
builder.Services.AddAllGeneratedComponents();
```

Framework services registered by the host are resolved through the runtime path, application components through the generated path — both with identical semantics. The `Example.WebApplication` project is a runnable minimal API using this setup.

## How it works

### What the generator collects

At compile time the generator scans the compilation and builds a registration model from four sources. All of them are incremental: editing a method body regenerates nothing.

| Source | Collected from | Result |
|---|---|---|
| Attributes | `[Singleton]` / `[Scoped]` / `[Transient]` classes | `AddGeneratedComponents()` body + a factory per implementation |
| `Add*` calls | `AddSingleton<T>()`, `typeof` overloads, `AddKeyed*`, `ServiceDescriptor` based `Add` / `TryAdd` / `TryAddEnumerable` in user code | A factory per implementation (the registration itself stays in user code) |
| Conventions | `[ComponentRegistration]` partial methods, optionally scanning a referenced assembly | The method body + a factory per matched implementation |
| Referenced modules | Assemblies marked with `[ComponentModule]` | The `AddAllGeneratedComponents()` aggregation |

### What the generator emits

For every collected implementation type, a factory is registered from a `[ModuleInitializer]`, so no discovery cost is paid at startup:

```csharp
// Emitted for [Transient] Service(Repository repository, Logger logger) with a singleton Logger
GeneratedComponentRegistry.Register(
    typeof(Service),
    [typeof(Repository), typeof(Logger)],
    [new InlinedDependency(typeof(Repository), typeof(Repository))],
    [new DependencyPlan(typeof(Logger), typeof(Logger))],
    static (provider, deps) => new Service(
        new Repository(),                                   // transient dependency inlined as a literal new
        Unsafe.As<Logger>(deps[0])!));                      // singleton dependency read from a resolved slot
```

Three shapes come out of this, chosen per dependency lifetime:

* **Inline expansion** — a transient dependency becomes a literal `new` inside the parent factory, so a whole transient graph collapses into one allocation site
* **Instance slots** — an unambiguous singleton dependency is resolved once and read straight from the deps array afterwards
* **Accessor slots** — scoped and non-inlinable dependencies get a validated accessor handle, skipping the service table lookup on every resolution

`IEnumerable<T>` sets whose elements are all inlinable transients are materialized as an array literal, and closed forms of open generic registrations that appear in code (as `typeof(IRepository<Foo>)`, a constructor parameter, or a property type) get their own generated factories — which is what makes value type arguments AOT safe.

### How generated code stays correct

Generated factories are assumptions about registrations, and registrations are only final at runtime. Every assumption is therefore verified when the provider realizes an entry, and any mismatch silently falls back to the runtime path:

| Assumption | Verified by |
|---|---|
| The constructor MEDI selects is the one the factory was generated for | Parameter type comparison |
| Every inlined transient dependency still resolves to that implementation's generated factory as a transient | Delegate reference comparison per dependency |
| Every deps slot still matches its planned lifetime and implementation | Same comparison, per slot |
| An enumerable still has the same elements in the same order | Ordered per-element comparison |

So `Replace`, a lifetime change, a factory registration or a decorator all keep working: they simply take the runtime path.

### The two paths

Two resolution paths share one runtime core, so lifetime, disposal and collection semantics are always identical:

| Path | Registrations | Implementation |
|---|---|---|
| Generated | Visible at compile time (attributes, `Add*` calls, conventions) | Generated factories with literal `new`. Transient dependency graphs are inlined into a single factory. Reflection-free |
| Runtime | Known only at runtime (framework assemblies, factories, instances, replacements) | `ConstructorInfo.Invoke` based. No Emit, so it also works on NativeAOT |

When the provider is built, every `ServiceDescriptor` is verified against the generated assumptions (selected constructor, inlined dependency lifetimes and implementation types). A registration that no longer matches — replaced via `Replace`, re-registered with a different lifetime, or overridden by a factory — automatically falls back to the runtime path, so behavior always follows the actual registrations.

## Diagnostics

| ID | Severity | Description |
|---|---|---|
| BTRS0001 | Error | `[ComponentRegistration]` method is not a static partial extension method with the required signature |
| BTRS0002 | Warning | Registration pattern is not a valid regular expression |
| BTRS0003 | Warning | Assembly named on `[ComponentRegistration]` is not referenced by the project |
| BTRS0004 | Warning | `[GenerateComponentFactory]` target is not a publicly accessible concrete class with a usable public constructor |
| BTRS0005 | Error | Multiple public constructors share the same maximum parameter count |
| BTRS0006 | Error | `PostConstruct` method is not a public parameterless instance method returning void |
| BTRS0007 | Error | Conflicting `PostConstruct` specifications across lifetime attributes |
| BTRS0008 | Error | Circular dependency between components |
| BTRS0009 | Warning | Dependency cannot be resolved from the registrations visible at compile time |
| BTRS0010 | Warning | Captive dependency: a singleton depends on a scoped service |
| BTRS0011 | Warning | Closed generic with value type arguments has no generated factory and resolves through the runtime path, which fails on NativeAOT |

## Samples

| Project | Contents |
|---|---|
| `Example` | Console sample asserting every feature: attribute components, module aggregation across two libraries, convention registration scanning a referenced assembly, and standard `Add*` registrations (generic, open generic, keyed, `TryAddEnumerable`, factory) |
| `Example.Library1` | Class library referencing the generator: its components form a module marked automatically |
| `Example.Library2` | Class library **without** the generator: a hand-written module marker plus plain types picked up by convention scanning |
| `Example.WebApplication` | ASP.NET Core minimal API with the container replaced, showing singleton / scoped / transient behavior per request |

## Limitations

* Runtime targets .NET 10 or later (the generator itself is netstandard2.0)
* Open generic definition registrations (`typeof(IRepository<>)`): closed forms appearing in code — as `typeof(IRepository<Foo>)`, constructor parameters or property types — get generated factories and are fully AOT safe, including value type arguments. Closed forms known only at runtime are served by the runtime path, where value type arguments are not supported on NativeAOT (`BTRS0011` warns about compile-time visible cases)
* Method injection is not supported
* On trimmed applications, `[Inject]` properties are only guaranteed for types with compile-time visible registrations
* Resolved `IEnumerable<T>` services are materialized `T[]` arrays (MEDI compatible). On NativeAOT, enumerating through the interface allocates the enumerator (32 B) and dispatches per element; casting the result to `T[]` enumerates allocation-free and roughly 3x faster on hot paths

