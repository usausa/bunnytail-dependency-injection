namespace BunnyTail.DependencyInjection.Tests;

using System.Reflection;

using BunnyTail.DependencyInjection.Generator;

using Microsoft.CodeAnalysis;

public sealed class DiagnosticTests
{
    // ------------------------------------------------------------
    // Declaration (00xx)
    // ------------------------------------------------------------

    [Fact]
    public void Btdi0001InvalidMethodDefinitionEmitsDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public static class Registrations
            {
                [ComponentRegistration(Lifetime.Singleton, "Service$")]
                public static IServiceCollection AddServices(IServiceCollection services) => services;   // neither partial nor an extension method
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0001");
    }

    [Theory]
    [InlineData("public sealed class Outer { [Singleton] private sealed class Hidden; }")]
    [InlineData("[Singleton] file sealed class FileLocal;")]
    [InlineData("public sealed class GenericOuter<T> { [Singleton] public sealed class Inner; }")]
    [InlineData("[Singleton] [System.Obsolete(\"removed\", true)] public sealed class Removed;")]
    public void Btdi0002ComponentNotReferableEmitsDiagnostic(string declaration)
    {
        var problems = GeneratorTestHelper.GetProblemIds(
            """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            """ + declaration);

        Assert.Equal(["BTDI0002"], problems);
    }

    [Fact]
    public void Btdi0003UndefinedLifetimeEmitsDiagnostic()
    {
        var problems = GeneratorTestHelper.GetProblemIds(
            """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public sealed class FooService;

            public static partial class Registrations
            {
                [ComponentRegistration((Lifetime)7, "Service$")]
                public static partial IServiceCollection AddServices(this IServiceCollection services);
            }
            """);

        Assert.Equal(["BTDI0003"], problems);
    }

    [Fact]
    public void Btdi0003UndefinedTrackingEmitsDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            [Transient(Tracking = (DisposableTracking)9)]
            public sealed class Tracked;
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().WithAssemblyName("Demo").Run(source);

        // Assert
        Assert.Equal(["BTDI0003"], result.Problems.Select(static x => x.Id));
        Assert.DoesNotContain("AddTransient<global::Demo.Tracked>", result.AllGeneratedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Btdi0004ClassNamesDifferingOnlyInCaseEmitDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public sealed class FooService;

            public static partial class Registrations
            {
                [ComponentRegistration(Lifetime.Singleton, "Service$")]
                public static partial IServiceCollection AddServices(this IServiceCollection services);
            }

            public static partial class RegistrationS
            {
                [ComponentRegistration(Lifetime.Singleton, "Service$")]
                public static partial IServiceCollection AddOthers(this IServiceCollection services);
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().WithAssemblyName("Demo").Run(source);

        // Assert
        Assert.Contains(result.Problems, static x => x.Id == "BTDI0004");
        Assert.DoesNotContain(result.Problems, static x => x.Id == "CS8785");
        Assert.Single(result.GeneratedSources.Keys, static x => x.StartsWith("Demo_Registration", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Btdi0005InvalidPatternEmitsDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public static partial class Registrations
            {
                [ComponentRegistration(Lifetime.Singleton, "([")]
                public static partial IServiceCollection AddServices(this IServiceCollection services);
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0005");
    }

    [Fact]
    public void Btdi0006MissingAssemblyEmitsDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public static partial class Registrations
            {
                [ComponentRegistration(Lifetime.Transient, ".*", Assembly = "No.Such.Assembly")]
                public static partial IServiceCollection AddExternal(this IServiceCollection services);
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0006");
    }

    [Fact]
    public void Btdi0007ConflictingInterfaceDelegateEmitsDiagnostic()
    {
        // Arrange
        const string source =
            """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            public interface IFoo
            {
            }

            [Singleton(As = typeof(IFoo), WithInterfaces = true)]
            public sealed class Foo : IFoo
            {
            }
            """;

        // Act
        var diagnostics = GeneratorTestHelper.CreateRunner().GetDiagnosticsAll(source);

        // Assert
        Assert.Contains(diagnostics, static x => x.Id == "BTDI0007");
    }

    [Fact]
    public void Btdi0008InvalidGenerateComponentFactoryTargetEmitsDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            [assembly: GenerateComponentFactory(typeof(Demo.NotConstructible))]

            namespace Demo;

            public abstract class NotConstructible;
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0008");
    }

    // ------------------------------------------------------------
    // Convention matching (01xx)
    // ------------------------------------------------------------

    [Fact]
    public void Btdi0101ConventionClassNotReferableEmitsDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public sealed class Outer
            {
                private sealed class HiddenService;

                public sealed class VisibleService;
            }

            public static partial class Registrations
            {
                [ComponentRegistration(Lifetime.Singleton, "Service$")]
                public static partial IServiceCollection AddServices(this IServiceCollection services);
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().WithAssemblyName("Demo").Run(source);

        // Assert
        var diagnostic = Assert.Single(result.Problems);
        Assert.Equal("BTDI0101", diagnostic.Id);
        Assert.Contains("class=[HiddenService]", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("AddSingleton<global::Demo.Outer.VisibleService>(services)", result.GeneratedSource("Demo_Registrations.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Btdi0102PatternWithNoMatchEmitsDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public sealed class FooService
            {
            }

            public static partial class Registrations
            {
                [ComponentRegistration(Lifetime.Singleton, "Service$")]
                [ComponentRegistration(Lifetime.Transient, "NothingMatchesThis$")]
                public static partial IServiceCollection AddServices(this IServiceCollection services);
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0102");
    }

    [Fact]
    public void Btdi0102NamespaceMismatchEmitsDiagnostic()
    {
        // Arrange: the pattern itself matches but the Namespace filter excludes every candidate
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public sealed class FooService
            {
            }

            public static partial class Registrations
            {
                [ComponentRegistration(Lifetime.Singleton, "Service$", Namespace = "Demo.Other")]
                public static partial IServiceCollection AddServices(this IServiceCollection services);
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0102");
    }

    [Fact]
    public void Btdi0102PatternMatchedByAnotherPatternEmitsNoDiagnostic()
    {
        // Arrange: both patterns match the same type, so neither is a no-match
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public sealed class FooService
            {
            }

            public static partial class Registrations
            {
                [ComponentRegistration(Lifetime.Singleton, "Service$")]
                [ComponentRegistration(Lifetime.Singleton, "^Foo")]
                public static partial IServiceCollection AddServices(this IServiceCollection services);
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.DoesNotContain(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0102");
    }

    // ------------------------------------------------------------
    // Component factory (02xx)
    // ------------------------------------------------------------

    [Fact]
    public void Btdi0201InvalidGenerateComponentFactoryPostConstructEmitsDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            [assembly: GenerateComponentFactory(typeof(Demo.Uncontrolled), PostConstruct = "Missing")]

            namespace Demo;

            public sealed class Uncontrolled;
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0201");
    }

    [Fact]
    public void Btdi0201InvalidPostConstructEmitsDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            [Singleton(PostConstruct = "Missing")]
            public sealed class Component;
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0201");
    }

    [Fact]
    public void Btdi0202ConflictingPostConstructEmitsDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            [Singleton(PostConstruct = nameof(First))]
            [Transient(PostConstruct = nameof(Second))]
            public sealed class Component
            {
                public void First()
                {
                }

                public void Second()
                {
                }
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0202");
    }

    [Fact]
    public void Btdi0203InjectPropertyWithoutPublicSetterEmitsDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            public interface INavigator;

            [Singleton(As = typeof(INavigator))]
            public sealed class Navigator : INavigator;

            [Transient]
            public sealed class ViewModel
            {
                [Inject]
                public INavigator Navigator { get; private set; } = default!;
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().WithAssemblyName("Demo").Run(source);

        // Assert
        Assert.Equal(["BTDI0203"], result.Problems.Select(static x => x.Id));
        Assert.DoesNotContain("new global::Demo.ViewModel(", result.GeneratedSource("GeneratedComponents.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Btdi0204AmbiguousConstructorEmitsDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            [Singleton]
            public sealed class DependencyA;

            [Singleton]
            public sealed class DependencyB;

            [Singleton]
            public sealed class Component
            {
                public Component(DependencyA a) { }

                public Component(DependencyB b) { }
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        var diagnostic = Assert.Single(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0204");
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.DoesNotContain("new global::Demo.Component(", result.GeneratedSource("GeneratedComponents.g.cs"), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------
    // Dependency graph (03xx)
    // ------------------------------------------------------------

    [Fact]
    public void Btdi0301CircularDependencyEmitsDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            [Singleton]
            public sealed class First(Second second);

            [Singleton]
            public sealed class Second(First first);
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0301");
    }

    [Fact]
    public void Btdi0301TransientCycleDoesNotBreakInlineExpansionEmitsDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            [Transient]
            public sealed class First(Second second);

            [Transient]
            public sealed class Second(First first);
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0301");

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");
        Assert.Contains(".GetValue<global::Demo.First>(scope)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Btdi0302UnresolvedDependencyEmitsDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            public sealed class NotRegistered;

            [Singleton]
            public sealed class Component(NotRegistered dependency);
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0302");
    }

    [Fact]
    public void Btdi0302KeyedOnlyRegistrationEmitsDiagnostic()
    {
        // Arrange: a keyed registration never satisfies a non-keyed dependency
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public interface IDependency;

            public sealed class Dependency : IDependency;

            [Transient]
            public sealed class Component(IDependency dependency);

            public static class Registrations
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddKeyedSingleton<IDependency, Dependency>("key");
                }
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0302");
    }

    [Fact]
    public void Btdi0302FactoryAndInstanceRegistrationsEmitNoDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public interface IClock;

            public sealed class SystemClock : IClock;

            public interface ITimer;

            public sealed class SystemTimer : ITimer;

            public interface IStore;

            public sealed class MemoryStore : IStore;

            public interface ICache;

            public sealed class MemoryCache : ICache;

            [Singleton]
            public sealed class Component(IClock clock, ITimer timer, IStore store, ICache cache);

            public static class Setup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IClock>(static _ => new SystemClock());
                    services.Add(ServiceDescriptor.Describe(typeof(ITimer), typeof(SystemTimer), ServiceLifetime.Singleton));
                    services.Add(new ServiceDescriptor(typeof(IStore), new MemoryStore()));
                    services.AddSingleton(typeof(ICache), new MemoryCache());
                }
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.DoesNotContain(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0302");
    }

    [Fact]
    public void Btdi0302DefaultValueParameterEmitsNoDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            public interface IOptionalThing;

            [Singleton]
            public sealed class UsesOptional(IOptionalThing? thing = null)
            {
                public IOptionalThing? Thing { get; } = thing;
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.DoesNotContain(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0302");
    }

    [Fact]
    public void Btdi0302TupleElementNamesEmitNoDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public interface IHandler<T>;

            [Singleton(As = typeof(IHandler<(int, string)>))]
            public sealed class TupleHandler : IHandler<(int, string)>;

            [Singleton]
            public sealed class UsesTupleHandler(IHandler<(int Id, string Name)> handler);

            public interface IRepository<T>;

            public sealed class Repository<T> : IRepository<T>;

            [Singleton]
            public sealed class UsesTupleRepository(IRepository<(int, string)> repository);

            public static class Setup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddTransient(typeof(IRepository<>), typeof(Repository<>));
                }
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.DoesNotContain(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0302");
        Assert.Contains("typeof(global::Demo.Repository<global::System.ValueTuple<int, string>>)", result.GeneratedSource("GeneratedComponents.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Btdi0302SameDiagnosticIsReportedOnce()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            public interface IA;

            public interface IB;

            public sealed class NotRegistered;

            [Singleton(As = typeof(IA))]
            [Singleton(As = typeof(IB))]
            public sealed class Multi(NotRegistered notRegistered) : IA, IB;
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.Single(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0302");
    }

    [Fact]
    public void Btdi0303CaptiveDependencyEmitsDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            [Scoped]
            public sealed class ScopedDependency;

            [Singleton]
            public sealed class SingletonComponent(ScopedDependency dependency);
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0303");
    }

    [Fact]
    public void Btdi0303KeyedRegistrationEmitsNoDiagnostic()
    {
        // Arrange: the non-keyed IDependency is a singleton, and the later keyed scoped registration must not override it
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public interface IDependency;

            public sealed class SingletonDependency : IDependency;

            public sealed class ScopedDependency : IDependency;

            [Singleton]
            public sealed class Component(IDependency dependency);

            public static class Registrations
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddSingleton<IDependency, SingletonDependency>();
                    services.AddKeyedScoped<IDependency, ScopedDependency>("key");
                }
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.DoesNotContain(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0303");
    }

    [Fact]
    public void Btdi0303FactoryRegistrationLifetimeEmitsDiagnostic()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public interface IContext;

            public sealed class Context : IContext;

            [Singleton]
            public sealed class Component(IContext context);

            public static class Setup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.Add(ServiceDescriptor.Describe(typeof(IContext), static _ => new Context(), ServiceLifetime.Scoped));
                }
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0303");
    }

    // ------------------------------------------------------------
    // Generation limit (04xx)
    // ------------------------------------------------------------

    [Fact]
    public void Btdi0401ValueTypeRuntimeGenericEmitsDiagnostic()
    {
        // Arrange
        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public interface IRepository<T>;

            public sealed class Repository<T> : IRepository<T>
            {
                public Repository(int retries = 3)
                {
                    _ = retries;
                }
            }

            public sealed class Consumer(IRepository<int> intRepository, IRepository<string> stringRepository)
            {
                public IRepository<int> IntRepository { get; } = intRepository;

                public IRepository<string> StringRepository { get; } = stringRepository;
            }

            public static class Setup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddTransient(typeof(IRepository<>), typeof(Repository<>));
                    services.AddTransient<Consumer>();
                }
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().Run(source);

        // Assert
        var diagnostics = result.Diagnostics(["BTDI"]).Where(static x => x.Id == "BTDI0401").ToArray();
        Assert.Single(diagnostics);
        Assert.Contains("Repository<int>", diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------
    // Reporting
    // ------------------------------------------------------------

    [Fact]
    public void DiagnosticIsReportedInSource()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            [Singleton]
            public sealed class First(Second second);

            [Singleton]
            public sealed class Second(First first);
            """;

        // Act
        var diagnostics = GeneratorTestHelper.CreateRunner().Run(source).Diagnostics(["BTDI"]);

        // Assert
        var diagnostic = Assert.Single(diagnostics, static x => x.Id == "BTDI0301");
        Assert.True(diagnostic.Location.IsInSource);
    }

    [Fact]
    public void ErrorsCannotBeSuppressed()
    {
        // Arrange
        var descriptors = typeof(DependencyInjectionGenerator).Assembly.GetType("BunnyTail.DependencyInjection.Generator.Diagnostics", throwOnError: true)!
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(static x => x.PropertyType == typeof(DiagnosticDescriptor))
            .Select(static x => (DiagnosticDescriptor)x.GetValue(null)!)
            .ToList();

        // Assert
        Assert.All(
            descriptors.Where(static x => x.DefaultSeverity == DiagnosticSeverity.Error),
            static x => Assert.Equal([WellKnownDiagnosticTags.NotConfigurable, WellKnownDiagnosticTags.Compiler], x.CustomTags));
    }
}
