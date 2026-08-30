# 🐰 BunnyTail.DependencyInjection

| Package | Info |
|:-|:-|
| BunnyTail.DependencyInjection | [![NuGet](https://img.shields.io/nuget/v/BunnyTail.DependencyInjection.svg)](https://www.nuget.org/packages/BunnyTail.DependencyInjection) |

## ❓ What is this?

A BunnyTail extension for Microsoft.Extensions.DependencyInjection. You keep the standard MEDI API with its exact semantics; this package swaps the engine underneath for an AOT-safe service provider that wires object graphs with source generated factories instead of reflection.

* 🤝 **100% MEDI compatible** - passes the official `Microsoft.Extensions.DependencyInjection.Specification.Tests` suite in full (base + keyed, **143/143**)
* 🛡️ **AOT safe** - no `Reflection.Emit` / `Expression.Compile` on the resolution path. Verified on NativeAOT with zero trim/AOT warnings
* ⚡ **Source generator powered** - constructor selection, lifetime shapes, disposal tracking and transient graph inlining are all settled at compile time
* 🔎 **Automatic registration** - components are collected from attributes, existing `Add*` calls and naming conventions

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

* One attribute maps to one MEDI registration shape. Plain is `AddX<TImpl>()`, `As` is `AddX<TService, TImpl>()`, `Key` is the keyed form
* `WithInterfaces = true` keeps `AddX<TImpl>()` and adds a delegate registration per directly declared interface, so a single instance is shared

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

* `As` and `WithInterfaces` behave exactly as on the lifetime attributes. The default registers the implementation only
* `Assembly` scans a referenced assembly from metadata (publicly accessible classes only)

### 🟨 Property injection

```csharp
[Transient]
public sealed class Component3(Component1 component)
{
    [Inject]
    public Component2 Two { get; set; } = default!;

    [Inject(Key = "primary")]
    public IService Service { get; set; } = default!;
}
```

* Runs after the constructor and before the initialization callback

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

* Must be a public parameterless instance method returning void. `PostConstruct` wins when both are present

### 🟨 Existing Add* registrations

Registration calls in user code are collected by the generator, so existing MEDI registration code gets the generated path with no changes.

* `Add{Lifetime}` / `TryAdd{Lifetime}` - generic overloads and non-generic `typeof` overloads (including single-argument self registration)
* `AddKeyed{Lifetime}` / `TryAddKeyed{Lifetime}` - generic and `typeof` overloads
* `Add` / `TryAdd` / `TryAddEnumerable` taking `ServiceDescriptor.{Lifetime}<TService, TImplementation>()`
* Open generic definition pairs such as `AddTransient(typeof(IRepository<>), typeof(Repository<>))`

### 🟨 Excluding interfaces from automatic registration

Registration without an explicit `As` covers the class and every interface it implements, inherited ones included.

```xml
<PropertyGroup>
  <DependencyInjectionIgnoreInterface>MyApp.INavigation,System.ComponentModel.INotifyPropertyChanged</DependencyInjectionIgnoreInterface>
</PropertyGroup>
```

* Comma separated, matched against the namespace-qualified name
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

* A library compiles its components into its own module, and `AddGeneratedComponents()` discovers every referenced module transitively, each exactly once
* The module marker is embedded for assemblies that have attribute components. A library that has none declares one by hand - one marker per assembly, naming a type with a `RegisterComponents(IServiceCollection)` static

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

The generator only sees the source it compiles, so a library registering its own types through its own extension method resolves through the runtime path. `[GenerateComponentFactory]` prepares a factory for such a type without registering it:

```csharp
[assembly: GenerateComponentFactory(typeof(SomeLibrary.SomeService))]
[assembly: GenerateComponentFactory(typeof(SomeLibrary.OtherService), PostConstruct = nameof(SomeLibrary.OtherService.Initialize))]

// registration still comes from the library
services.AddSomeLibrary();
```

To find the types worth marking, ask the built provider which registrations fell back:

```csharp
using BunnyTail.DependencyInjection.Diagnostics;

// Ready-to-paste attribute lines, limited to types the generated code can actually construct
Console.Write(provider.DescribeRuntimeFallbacks());

// A predicate narrows it further, and CreateFactoryReport() gives the full classification
Console.Write(provider.DescribeRuntimeFallbacks(static x => x.Lifetime != ServiceLifetime.Singleton));
```

* ⚠️ Development-time only: it realizes every entry, so keep it out of release paths

### 🟨 Disposable tracking control

MEDI keeps a reference to every disposable transient and disposes it with the resolving scope. Transient tracking can therefore be turned off:

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

* Transients only. Singleton and scoped disposal always stays container owned

### 🟨 Type activation

`ITypeActivator` is the reflection-free counterpart of `ActivatorUtilities.CreateInstance()`. It constructs a type from container dependencies without registering it, and the fresh instance belongs to the caller:

```csharp
var page = provider.Activate<MainPage>();   // on the provider and on scopes
var view = activator.Activate(viewType);    // ITypeActivator is a built-in service, so it can be injected
```

Differences from `ActivatorUtilities.CreateInstance()`:

* Generated factories instead of reflection, so it stays on the AOT safe path. Generic call sites and `typeof` literal arguments are resolved at build time; a runtime `Type` falls back to the runtime path, so mark such types with `[GenerateComponentFactory]`
* The full construction pipeline applies: `[Inject]` properties and `PostConstruct` / `IInitializable`
* Everything comes from the container. There is no overload taking extra constructor arguments

## 📖 API reference

### 🟦 Extension methods

All extend `IServiceCollection`.

| Method | Description |
|---|---|
| `AddGeneratedComponents()` | This assembly's attribute components plus every referenced module. The one method to call |
| `BuildGeneratedServiceProvider()` | Builds the provider. An overload takes `Action<GeneratedServiceProviderOptions>` |
| `AddTransient()` / `AddKeyedTransient()` with `DisposableTracking` | Transient registration with an explicit disposal tracking setting |

### 🟦 Types

| Type | Description |
|---|---|
| `GeneratedServiceProviderFactory` | `IServiceProviderFactory<IServiceCollection>` for `UseServiceProviderFactory` |
| `GeneratedServiceProvider` | The provider. Adds typed `GetService<T>()` and keyed equivalents that skip the MEDI extension method dispatch |
| `ServiceProviderScope` | A scope, and the `IServiceProvider` injected inside it. Same typed methods |
| `GeneratedServiceProviderOptions` | `TrackTransientDisposables` plus per-type `EnableTracking` / `DisableTracking` |
| `ITypeActivator` | Built-in service. Constructs a type from container dependencies without registering it |
| `DisposableTracking` / `TrackingServiceDescriptor` | Per-registration disposal tracking setting, and the descriptor carrying it |
| `ServiceFactoryReportExtensions` | Development-time diagnostics: `CreateFactoryReport()` / `DescribeRuntimeFallbacks()` |

### 🟦 Attributes

| Attribute | Target | Description |
|---|---|---|
| `[Singleton]` / `[Scoped]` / `[Transient]` | class | `As`, `Key`, `WithInterfaces`, `PostConstruct`. `[Transient]` also takes `Tracking` |
| `[Inject]` | property | Property injection. `Key` resolves a keyed service |
| `[ComponentRegistration]` | partial method | `Lifetime`, `Pattern`, `Namespace`, `Assembly`, `As`, `WithInterfaces` |
| `[ComponentModule]` | assembly | The module type aggregated by `AddGeneratedComponents()`. Emitted automatically for assemblies with attribute components |
| `[GenerateComponentFactory]` | assembly | A factory without a registration, for libraries you do not control. Supports `PostConstruct` |
| `IInitializable` | interface | Initialization callback |

### 🟦 MSBuild properties

* `DependencyInjectionIgnoreInterface` - comma-separated interfaces excluded from automatic registration. `IDisposable` / `IAsyncDisposable` / `IInitializable` always are

## ⚙️ How it works

### 🟪 What the generator collects

All sources are incremental - editing a method body regenerates nothing.

| Source | Collected from | Result |
|---|---|---|
| Attributes | `[Singleton]` / `[Scoped]` / `[Transient]` classes | `RegisterComponents()` body + a factory per implementation |
| `Add*` calls | `Add*` / `TryAdd*` / `AddKeyed*` in user code | A factory per implementation (the registration itself stays in user code) |
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

### 🟪 How generated code stays correct

Generated factories are assumptions about registrations, and registrations are only final at runtime. Every assumption is re-checked when the provider realizes an entry, and any mismatch silently falls back to the runtime path.

## 📂 Samples

| Project | Contents |
|---|---|
| `Example` | Console sample asserting every feature |
| `Example.Library` | Class library aggregated by the application as a module |
| `Example.ThirdPartyLibrary` | Third party stand-in that references nothing of this package. The target of convention scanning, `[GenerateComponentFactory]` and the fallback diagnostics |
| `Example.WebApplication` | ASP.NET Core minimal API with the container replaced |

## ⚡ Benchmark

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
