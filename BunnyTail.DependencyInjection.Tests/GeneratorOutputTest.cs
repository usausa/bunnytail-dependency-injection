namespace BunnyTail.DependencyInjection.Tests;

using BunnyTail.DependencyInjection;
using BunnyTail.DependencyInjection.Generator;

using Microsoft.Extensions.DependencyInjection;

using SourceGenerateHelper.Testing;

using Xunit;

public sealed class GeneratorOutputTest
{
    private static GeneratorTestRunner CreateRunner() =>
        GeneratorTestRunner.For<DependencyInjectionGenerator>()
            .WithAssemblyName("BunnyTail.DependencyInjection.Tests")
            .WithReference(typeof(SingletonAttribute).Assembly)
            .WithReference(typeof(IServiceCollection).Assembly);

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    private const string ImplicitUsings =
        """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;

        """;

    //--------------------------------------------------------------------------------
    // Attribute component
    //--------------------------------------------------------------------------------

    [Fact]
    public void GeneratedSourceMatchesHandWrittenPrototype()
    {
        // Arrange
        var source = ImplicitUsings + File.ReadAllText(Path.Combine("Components", "Components.cs"));
        var expected = File.ReadAllText(Path.Combine("Expected", "ComponentRegistration.expected.txt"));

        // Act
        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        // Assert
        var actual = result.GeneratedSource("GeneratedComponents.g.cs");

        File.WriteAllText("actual-generated.txt", actual);

        Assert.Equal(Normalize(expected), Normalize(actual));
    }

    //--------------------------------------------------------------------------------
    // Add*
    //--------------------------------------------------------------------------------

    [Fact]
    public void FactoryIsCollectedFromAddInvocation()
    {
        // Arrange
        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public sealed class CollectedComponent;

            public sealed class DependentComponent(CollectedComponent dependency);

            public static class Setup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddTransient<CollectedComponent>();
                    services.AddSingleton<DependentComponent>();
                    services.AddSingleton(static _ => new CollectedComponent());   // factory registrations are not collected
                }
            }
            """;

        // Act
        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        // Assert
        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        Assert.Contains("typeof(global::Demo.CollectedComponent)", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.DependentComponent),", generated, StringComparison.Ordinal);

        Assert.Contains("new global::Demo.CollectedComponent())", generated, StringComparison.Ordinal);
        Assert.Contains("new global::BunnyTail.DependencyInjection.InlinedDependency(typeof(global::Demo.CollectedComponent), typeof(global::Demo.CollectedComponent))", generated, StringComparison.Ordinal);

        Assert.DoesNotContain("AddGeneratedComponents", generated, StringComparison.Ordinal);
    }

    //--------------------------------------------------------------------------------
    // Convention
    //--------------------------------------------------------------------------------

    [Fact]
    public void ConventionMethodBodyIsGenerated()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public interface IFooService;

            public sealed class FooService : IFooService;

            public sealed class PlainService;

            public sealed class OtherComponent;

            public static partial class Registrations
            {
                [ComponentRegistration(Lifetime.Scoped, "Service$")]
                public static partial IServiceCollection AddServices(this IServiceCollection services);
            }
            """;

        // Act
        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        // Assert
        var generated = result.GeneratedSource("Demo_Registrations.g.cs");

        Assert.Contains("services.AddScoped<global::Demo.IFooService, global::Demo.FooService>();", generated, StringComparison.Ordinal);
        Assert.Contains("services.AddScoped<global::Demo.PlainService>();", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("OtherComponent", generated, StringComparison.Ordinal);

        var factories = result.GeneratedSource("GeneratedComponents.g.cs");
        Assert.Contains("typeof(global::Demo.FooService)", factories, StringComparison.Ordinal);
    }

    [Fact]
    public void ConventionMethodsOnSameClassShareOneOutputFile()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public sealed class FooService;

            public sealed class BarRepository;

            public static partial class Registrations
            {
                [ComponentRegistration(Lifetime.Singleton, "Service$")]
                public static partial IServiceCollection AddServices(this IServiceCollection services);

                [ComponentRegistration(Lifetime.Scoped, "Repository$")]
                internal static partial IServiceCollection AddRepositories(this IServiceCollection services);
            }
            """;

        // Act
        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        // Assert
        var generated = result.GeneratedSource("Demo_Registrations.g.cs");

        Assert.Contains("public static partial global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddServices", generated, StringComparison.Ordinal);
        Assert.Contains("internal static partial global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddRepositories", generated, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<global::Demo.FooService>();", generated, StringComparison.Ordinal);
        Assert.Contains("services.AddScoped<global::Demo.BarRepository>();", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ConventionMethodKeepsDeclaredAccessibility()
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
                private static partial IServiceCollection AddServices(this IServiceCollection services);

                public static IServiceCollection Use(this IServiceCollection services) => services.AddServices();
            }
            """;

        // Act
        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        // Assert
        var generated = result.GeneratedSource("Demo_Registrations.g.cs");

        Assert.Contains("private static partial global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddServices", generated, StringComparison.Ordinal);
    }

    //--------------------------------------------------------------------------------
    // Ignore interface
    //--------------------------------------------------------------------------------

    [Fact]
    public void IgnoredInterfaceIsExcludedFromRegistration()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public interface IKept;

            public interface IIgnored;

            [Singleton]
            public sealed class AttributeComponent : IKept, IIgnored;

            public sealed class ConventionService : IIgnored;

            public static partial class Registrations
            {
                [ComponentRegistration(Lifetime.Singleton, "Service$")]
                public static partial IServiceCollection AddServices(this IServiceCollection services);
            }
            """;

        // Act
        var result = CreateRunner()
            .WithGlobalOption("build_property.DependencyInjectionIgnoreInterface", "Demo.IIgnored")
            .VerifyCompiles()
            .Run(source);

        // Assert
        var components = result.GeneratedSource("GeneratedComponents.g.cs");
        Assert.Contains("services.AddSingleton<global::Demo.IKept>(", components, StringComparison.Ordinal);
        Assert.DoesNotContain("global::Demo.IIgnored", components, StringComparison.Ordinal);

        var registrations = result.GeneratedSource("Demo_Registrations.g.cs");
        Assert.Contains("services.AddSingleton<global::Demo.ConventionService>();", registrations, StringComparison.Ordinal);
        Assert.DoesNotContain("global::Demo.IIgnored", registrations, StringComparison.Ordinal);
    }

    //--------------------------------------------------------------------------------
    // Diagnostics
    //--------------------------------------------------------------------------------

    [Fact]
    public void InvalidMethodDefinitionIsReported()
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
        var result = CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0001");
    }

    [Fact]
    public void CircularDependencyIsReported()
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
        var result = CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0008");
    }

    [Fact]
    public void UnresolvedDependencyIsReported()
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
        var result = CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0009");
    }

    [Fact]
    public void CaptiveDependencyIsReported()
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
        var result = CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0010");
    }

    [Fact]
    public void AmbiguousConstructorIsReported()
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
        var result = CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0005");
    }

    [Fact]
    public void KeyedFactoryIsGeneratedWithServiceKeyInjection()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public interface IKeyed;

            [Singleton(As = typeof(IKeyed), Key = "primary")]
            public sealed class KeyedComponent([ServiceKey] string key) : IKeyed;
            """;

        // Act
        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        // Assert
        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        Assert.Contains("RegisterKeyed(", generated, StringComparison.Ordinal);
        Assert.Contains("static (provider, key) =>", generated, StringComparison.Ordinal);
        Assert.Contains("(string)key!", generated, StringComparison.Ordinal);
    }

    //--------------------------------------------------------------------------------
    // Transient inline expansion
    //--------------------------------------------------------------------------------

    [Fact]
    public void TransientDependenciesAreInlined()
    {
        // Arrange
        const string source = """
            using System;

            using BunnyTail.DependencyInjection;

            namespace Demo;

            [Transient]
            public sealed class Leaf;

            [Transient]
            public sealed class Branch(Leaf leaf);

            [Transient]
            public sealed class Root(Branch a, Branch b);

            [Singleton]
            public sealed class Shared;

            [Transient]
            public sealed class DisposableLeaf : IDisposable
            {
                public void Dispose()
                {
                }
            }

            [Transient]
            public sealed class Mixed(Shared shared, Leaf leaf, DisposableLeaf disposable);
            """;

        // Act
        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        // Assert
        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        Assert.Contains("new global::Demo.Branch(new global::Demo.Leaf()),", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.Branch(new global::Demo.Leaf()));", generated, StringComparison.Ordinal);

        Assert.Contains("[new global::BunnyTail.DependencyInjection.InlinedDependency(typeof(global::Demo.Branch), typeof(global::Demo.Branch))],", generated, StringComparison.Ordinal);

        Assert.Contains("global::System.Runtime.CompilerServices.Unsafe.As<global::Demo.Shared>(deps[0])!", generated, StringComparison.Ordinal);
        Assert.Contains("[new global::BunnyTail.DependencyInjection.DependencyPlan(typeof(global::Demo.Shared), typeof(global::Demo.Shared)), new global::BunnyTail.DependencyInjection.DependencyPlan(typeof(global::Demo.DisposableLeaf))],", generated, StringComparison.Ordinal);
        Assert.Contains("static (provider, deps) =>", generated, StringComparison.Ordinal);
        Assert.Contains("global::System.Runtime.CompilerServices.Unsafe.As<global::BunnyTail.DependencyInjection.DependencyAccessor>(deps[1])!.GetValue<global::Demo.DisposableLeaf>(scope)", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.Mixed(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TransientCycleDoesNotBreakInlineExpansion()
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
        var result = CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0008");

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");
        Assert.Contains(".GetValue<global::Demo.First>(scope)", generated, StringComparison.Ordinal);
    }

    //--------------------------------------------------------------------------------
    // Expanded Add*
    //--------------------------------------------------------------------------------

    [Fact]
    public void ExpandedAddShapesGenerateFactories()
    {
        // Arrange
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            namespace Demo;

            public interface IThing;

            public sealed class ThingA : IThing;

            public sealed class ThingB : IThing;

            public sealed class ThingC : IThing;

            public sealed class ThingD : IThing;

            public sealed class SelfThing;

            public static class Registrations
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddTransient(typeof(IThing), typeof(ThingA));
                    services.TryAddEnumerable(ServiceDescriptor.Transient<IThing, ThingB>());
                    services.AddKeyedSingleton<IThing, ThingC>("key");
                    services.Add(ServiceDescriptor.Singleton<IThing, ThingD>());
                    services.AddSingleton(typeof(SelfThing));
                }
            }
            """;

        // Act
        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        // Assert
        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        Assert.Contains("typeof(global::Demo.ThingA)", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.ThingB)", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.ThingD)", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.SelfThing)", generated, StringComparison.Ordinal);
        var keyedIndex = generated.IndexOf("RegisterKeyed(", StringComparison.Ordinal);
        Assert.True((keyedIndex >= 0) && (generated.IndexOf("typeof(global::Demo.ThingC)", keyedIndex, StringComparison.Ordinal) > keyedIndex));

        Assert.DoesNotContain("RegisterEnumerable(", generated, StringComparison.Ordinal);
    }

    //--------------------------------------------------------------------------------
    // Factory generation
    //--------------------------------------------------------------------------------

    [Fact]
    public void GenerateComponentFactoryEmitsFactoryWithoutRegistration()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            [assembly: GenerateComponentFactory(typeof(Demo.Uncontrolled))]

            namespace Demo;

            public sealed class Dependency;

            public sealed class Uncontrolled
            {
                public Uncontrolled(Dependency dependency)
                {
                    Dependency = dependency;
                }

                public Dependency Dependency { get; }
            }
            """;

        // Act
        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        // Assert
        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        Assert.Contains("typeof(global::Demo.Uncontrolled)", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.Uncontrolled(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("services.Add", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateComponentFactoryEmitsPostConstruct()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            [assembly: GenerateComponentFactory(typeof(Demo.Uncontrolled), PostConstruct = "Prepare")]

            namespace Demo;

            public sealed class Uncontrolled
            {
                public bool Prepared { get; private set; }

                public void Prepare() => Prepared = true;
            }
            """;

        // Act
        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        // Assert
        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        Assert.Contains("instance.Prepare();", generated, StringComparison.Ordinal);
        Assert.Contains("RegisterInitializer(typeof(global::Demo.Uncontrolled), \"Prepare\")", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidGenerateComponentFactoryPostConstructIsReported()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            [assembly: GenerateComponentFactory(typeof(Demo.Uncontrolled), PostConstruct = "Missing")]

            namespace Demo;

            public sealed class Uncontrolled;
            """;

        // Act
        var result = CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0006");
    }

    [Fact]
    public void InvalidGenerateComponentFactoryTargetIsReported()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            [assembly: GenerateComponentFactory(typeof(Demo.NotConstructible))]

            namespace Demo;

            public abstract class NotConstructible;
            """;

        // Act
        var result = CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0004");
    }

    //--------------------------------------------------------------------------------
    // Assembly scoped
    //--------------------------------------------------------------------------------

    [Fact]
    public void AssemblyScopedConventionRegistersExternalTypes()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public static partial class Registrations
            {
                [ComponentRegistration(Lifetime.Transient, "^MultiLeafA$", Assembly = "BunnyTail.DependencyInjection.Tests")]
                public static partial IServiceCollection AddExternal(this IServiceCollection services);
            }
            """;

        // Act
        var result = GeneratorTestRunner.For<DependencyInjectionGenerator>()
            .WithAssemblyName("Demo")
            .WithReference(typeof(SingletonAttribute).Assembly)
            .WithReference(typeof(IServiceCollection).Assembly)
            .WithReference(typeof(GeneratorOutputTest).Assembly)
            .VerifyCompiles()
            .Run(source);

        // Assert
        var generated = result.GeneratedSource("Demo_Registrations.g.cs");

        Assert.Contains("global::BunnyTail.DependencyInjection.Tests.Components.MultiLeafA", generated, StringComparison.Ordinal);
        var components = result.GeneratedSource("GeneratedComponents.g.cs");
        Assert.Contains("typeof(global::BunnyTail.DependencyInjection.Tests.Components.MultiLeafA)", components, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingAssemblyIsReported()
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
        var result = CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0003");
    }

    //--------------------------------------------------------------------------------
    // Module aggregation
    //--------------------------------------------------------------------------------

    [Fact]
    public void ModuleMarkerIsEmittedForAttributeComponents()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            [Singleton]
            public sealed class AppComponent;
            """;

        // Act
        var result = GeneratorTestRunner.For<DependencyInjectionGenerator>()
            .WithAssemblyName("Demo")
            .WithReference(typeof(SingletonAttribute).Assembly)
            .WithReference(typeof(IServiceCollection).Assembly)
            .VerifyCompiles()
            .Run(source);

        // Assert
        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        Assert.Contains("[assembly: global::BunnyTail.DependencyInjection.ComponentModule(typeof(global::Demo.GeneratedComponents))]", generated, StringComparison.Ordinal);
        Assert.Contains("public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddAllGeneratedComponents(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferencedModulesAreAggregatedIntoAddAllGeneratedComponents()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            [Singleton]
            public sealed class AppComponent;
            """;

        // Act
        var result = GeneratorTestRunner.For<DependencyInjectionGenerator>()
            .WithAssemblyName("Demo")
            .WithReference(typeof(SingletonAttribute).Assembly)
            .WithReference(typeof(IServiceCollection).Assembly)
            .WithReference(typeof(GeneratorOutputTest).Assembly)
            .VerifyCompiles()
            .Run(source);

        // Assert
        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        Assert.Contains("global::BunnyTail.DependencyInjection.Tests.GeneratedComponents.AddGeneratedComponents(services);", generated, StringComparison.Ordinal);
        var moduleCall = generated.IndexOf("global::BunnyTail.DependencyInjection.Tests.GeneratedComponents.AddGeneratedComponents(services);", StringComparison.Ordinal);
        var selfCall = generated.IndexOf("        AddGeneratedComponents(services);", StringComparison.Ordinal);
        Assert.True((moduleCall >= 0) && (selfCall > moduleCall));
    }

    //--------------------------------------------------------------------------------
    // Enumerable
    //--------------------------------------------------------------------------------

    [Fact]
    public void EnumerableFactoryIsGeneratedForAllTransientElements()
    {
        // Arrange
        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public interface IMulti;

            public sealed class Multi1 : IMulti;

            public sealed class Multi2 : IMulti;

            public static class Setup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddTransient<IMulti, Multi1>();
                    services.AddTransient<IMulti, Multi2>();
                }
            }
            """;

        // Act
        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        // Assert
        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        Assert.Contains("RegisterEnumerable(", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.IMulti),", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.IMulti[]", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.Multi1(),", generated, StringComparison.Ordinal);
    }

    //--------------------------------------------------------------------------------
    // Open generic
    //--------------------------------------------------------------------------------

    [Fact]
    public void ClosedGenericFactoriesAreDiscoveredFromConstructorDependencies()
    {
        // Arrange
        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public interface IRepository<T>;

            public sealed class Repository<T> : IRepository<T>;

            public sealed class Consumer(IRepository<int> repository)
            {
                public IRepository<int> Repository { get; } = repository;
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
        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        // Assert
        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        Assert.Contains("typeof(global::Demo.Repository<int>)", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.Repository<int>())", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ValueTypeRuntimeGenericIsReported()
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
        var result = CreateRunner().Run(source);

        // Assert
        var diagnostics = result.Diagnostics(["BTDI"]).Where(static x => x.Id == "BTDI0011").ToArray();
        Assert.Single(diagnostics);
        Assert.Contains("Repository<int>", diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void ClosedGenericFactoriesAreGeneratedFromOpenGenericRegistrations()
    {
        // Arrange
        const string source = """
            using System;

            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public interface IRepository<T>
            {
            }

            public sealed class Repository<T> : IRepository<T>
            {
            }

            public static class Setup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddTransient(typeof(IRepository<>), typeof(Repository<>));
                }

                public static void Use(IServiceProvider provider)
                {
                    _ = provider.GetService(typeof(IRepository<string>));
                    _ = provider.GetService(typeof(IRepository<int>));
                }
            }
            """;

        // Act
        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        // Assert
        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        Assert.Contains("typeof(global::Demo.Repository<string>)", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.Repository<int>)", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.Repository<string>())", generated, StringComparison.Ordinal);
    }

    //--------------------------------------------------------------------------------
    // Initialization
    //--------------------------------------------------------------------------------

    [Fact]
    public void InitializationCallbacksAreEmitted()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            [Singleton(PostConstruct = nameof(Setup))]
            public sealed class WithMethod
            {
                public void Setup()
                {
                }
            }

            [Transient]
            public sealed class WithInterface : IInitializable
            {
                public void Initialize()
                {
                }
            }

            [Transient]
            public sealed class Parent(WithInterface dependency);
            """;

        // Act
        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        // Assert
        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        Assert.Contains("instance.Setup();", generated, StringComparison.Ordinal);
        Assert.Contains("((global::BunnyTail.DependencyInjection.IInitializable)instance).Initialize();", generated, StringComparison.Ordinal);

        Assert.DoesNotContain("services.AddTransient<global::BunnyTail.DependencyInjection.IInitializable>", generated, StringComparison.Ordinal);

        Assert.Contains(".GetValue<global::Demo.WithInterface>(scope)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidPostConstructIsReported()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            [Singleton(PostConstruct = "Missing")]
            public sealed class Component;
            """;

        // Act
        var result = CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0006");
    }

    [Fact]
    public void ConflictingPostConstructIsReported()
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
        var result = CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0007");
    }

    [Fact]
    public void InvalidPatternIsReported()
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
        var result = CreateRunner().Run(source);

        // Assert
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0002");
    }
}
