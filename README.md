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

Annotate components with `[Singleton]` / `[Scoped]` / `[Transient]`. The generated `AddComponents()` method registers the class itself and all implemented interfaces (interfaces are forwarded to the same instance).

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
    .AddComponents()          // generated
    .BuildResolverServiceProvider();

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

### Existing Add* registrations

`AddSingleton<TService, TImplementation>()` style calls in user code are detected by the generator, and reflection-free factories are generated for the implementation types automatically. Existing MEDI registration code benefits from the generated path without any changes.

### Multi-project modules

Components can live in other projects. When a class library references the generator, its components compile into that library's own `GeneratedComponents` module, marked with an assembly level `[ComponentModule]` attribute. The application's generated `AddAllComponents()` discovers every referenced module (transitively, each exactly once) and registers them together with the application's own components in a single call.

```csharp
// Class library (references BunnyTail.Resolver and the generator)
[Singleton]
public sealed class LibraryComponent;
```

```csharp
// Application
using var provider = new ServiceCollection()
    .AddAllComponents()       // referenced modules + own components
    .BuildResolverServiceProvider();
```

Each module's `AddComponents()` registers only its own components, so modules can still be registered individually when finer control is needed. The `Develop` / `Develop.Library` projects contain a working example.

## Microsoft.Extensions.DependencyInjection integration

The `BunnyTail.Resolver.Extensions.DependencyInjection` package provides the MEDI bridge: `BuildResolverServiceProvider()` and `ResolverServiceProviderFactory`.

### ServiceCollection

```csharp
using var provider = new ServiceCollection()
    .AddComponents()
    .AddSingleton<IFoo, Foo>()
    .BuildResolverServiceProvider();
```

### Generic Host

```csharp
using var host = Host.CreateDefaultBuilder(args)
    .UseServiceProviderFactory(new ResolverServiceProviderFactory())
    .ConfigureServices(static services => services.AddComponents())
    .Build();
```

### ASP.NET Core

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseServiceProviderFactory(new ResolverServiceProviderFactory());
```

Framework services registered by the host are resolved through the runtime path, application components through the generated path — both with identical semantics.

## How it works

Two resolution paths share one runtime core, so lifetime, disposal and collection semantics are always identical:

| Path | Registrations | Implementation |
|---|---|---|
| Generated | Visible at compile time (attributes, `Add*` calls, conventions) | Generated factories with literal `new`. Transient dependency graphs are inlined into a single factory. Reflection-free |
| Runtime | Known only at runtime (framework assemblies, factories, instances, replacements) | `ConstructorInfo.Invoke` based. No Emit, so it also works on NativeAOT |

When the provider is built, every `ServiceDescriptor` is verified against the generated assumptions (selected constructor, inlined dependency lifetimes and implementation types). A registration that no longer matches — replaced via `Replace`, re-registered with a different lifetime, or overridden by a factory — automatically falls back to the runtime path, so behavior always follows the actual registrations.

## Diagnostics

| ID | Severity | Description |
|---|---|---|
| BTRS0001 | Error | Registration method must be a static partial extension method with an `IServiceCollection` parameter and return type |
| BTRS0002 | Warning | Registration pattern is not a valid regular expression |
| BTRS0003 | Error | Circular dependency detected at compile time |
| BTRS0004 | Warning | Dependency cannot be resolved from the registrations visible at compile time |
| BTRS0005 | Warning | Captive dependency (singleton component depends on a scoped service) |
| BTRS0006 | Error | Multiple public constructors with the same maximum parameter count |
| BTRS0007 | Error | The `PostConstruct` method is missing or is not a public parameterless instance method returning void |
| BTRS0008 | Error | Conflicting `PostConstruct` specifications across lifetime attributes |

## Limitations

* Runtime targets .NET 10 or later (the generator itself is netstandard2.0)
* Open generic definition registrations (`typeof(IRepository<>)`): closed forms appearing as `typeof(IRepository<Foo>)` in code get generated factories and are fully AOT safe, including value type arguments. Closed forms known only at runtime are served by the runtime path, where value type arguments are not supported on NativeAOT
* Method injection is not supported
* On trimmed applications, `[Inject]` properties are only guaranteed for types with compile-time visible registrations

## TODO

- [ ] Open generic definitions: extend closed-form discovery to constructor dependencies (currently `typeof(IRepository<Foo>)` usages are collected), plus a diagnostic for closed forms that stay on the runtime path with value type arguments
- [ ] `Add*` collection coverage: non-generic `typeof` overloads, `TryAddEnumerable`, `ServiceDescriptor` based registrations and `AddKeyed*`
- [ ] `[ComponentRegistration]` `Assembly` parameter (referenced assembly scan)
