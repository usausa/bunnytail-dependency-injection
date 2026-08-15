# BunnyTail.Resolver

Source generator based AOT-safe DI library compatible with Microsoft.Extensions.DependencyInjection.

## Features

- **100% MEDI compatible**: passes all `Microsoft.Extensions.DependencyInjection.Specification.Tests` (base + keyed). Drop-in replacement via `IServiceProviderFactory`.
- **AOT safe**: no `Reflection.Emit`, no `Expression.Compile` on the resolution path. Verified on NativeAOT with zero trim/AOT warnings.
- **Source generator powered**: dependency graphs are resolved at compile time. Constructor selection, lifetimes and slot assignment are settled before the app starts.
- **Automatic registration**: components are collected from attributes, existing `Add*` calls, and naming conventions — no manual registration required.

## Usage

### Attribute based registration

```csharp
[Singleton]
public class Component1;

[Transient]
public class Component2(Component1 component)
{
    [Inject]
    public Component3 Three { get; set; } = default!;
}

[Singleton(As = typeof(IService), Key = "primary")]
public class Component4 : IService;
```

```csharp
var provider = new ServiceCollection()
    .AddComponents()          // generated
    .BuildResolverServiceProvider();
```

### Convention based registration

```csharp
public static partial class ServiceCollectionExtensions
{
    [ComponentRegistration(Lifetime.Singleton, "Service$")]
    public static partial IServiceCollection AddServices(this IServiceCollection services);
}
```

### Generic Host / ASP.NET Core

```csharp
builder.Host.UseServiceProviderFactory(new ResolverServiceProviderFactory());
```

Existing `AddSingleton<TService, TImplementation>()` style registrations are also detected by the generator and served by generated factories automatically.

## Packages

| Package | Description |
|---------|-------------|
| BunnyTail.Resolver | Runtime, attributes and the source generator |
| BunnyTail.Resolver.Extensions.DependencyInjection | `IServiceProviderFactory` integration |

## Documents

- [SPEC.md](SPEC.md) — design specification
- [__benchmarks/results/VERDICT.md](__benchmarks/results/VERDICT.md) — measured design decisions
