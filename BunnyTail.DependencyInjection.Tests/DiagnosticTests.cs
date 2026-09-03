namespace BunnyTail.DependencyInjection.Tests;

public sealed class DiagnosticTests
{
    // ------------------------------------------------------------
    // Interface conflict
    // ------------------------------------------------------------

    [Fact]
    public void Btdi0012ConflictingInterfaceDelegateEmitsDiagnostic()
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
        Assert.Contains(diagnostics, static x => x.Id == "BTDI0012");
    }

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

    [Fact]
    public void Btdi0008CircularDependencyEmitsDiagnostic()
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
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0008");
    }

    [Fact]
    public void Btdi0009UnresolvedDependencyEmitsDiagnostic()
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
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0009");
    }

    [Fact]
    public void Btdi0010CaptiveDependencyEmitsDiagnostic()
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
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0010");
    }

    [Fact]
    public void Btdi0005AmbiguousConstructorEmitsDiagnostic()
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
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0005");
    }

    [Fact]
    public void Btdi0008TransientCycleDoesNotBreakInlineExpansionEmitsDiagnostic()
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
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0008");

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");
        Assert.Contains(".GetValue<global::Demo.First>(scope)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Btdi0006InvalidGenerateComponentFactoryPostConstructEmitsDiagnostic()
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
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0006");
    }

    [Fact]
    public void Btdi0004InvalidGenerateComponentFactoryTargetEmitsDiagnostic()
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
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0004");
    }

    [Fact]
    public void Btdi0003MissingAssemblyEmitsDiagnostic()
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
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0003");
    }

    [Fact]
    public void Btdi0011ValueTypeRuntimeGenericEmitsDiagnostic()
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
        var diagnostics = result.Diagnostics(["BTDI"]).Where(static x => x.Id == "BTDI0011").ToArray();
        Assert.Single(diagnostics);
        Assert.Contains("Repository<int>", diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Btdi0006InvalidPostConstructEmitsDiagnostic()
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
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0006");
    }

    [Fact]
    public void Btdi0007ConflictingPostConstructEmitsDiagnostic()
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
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0007");
    }

    [Fact]
    public void Btdi0002InvalidPatternEmitsDiagnostic()
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
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0002");
    }

    [Fact]
    public void Btdi0013PatternWithNoMatchEmitsDiagnostic()
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
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0013");
    }

    [Fact]
    public void Btdi0013NamespaceMismatchEmitsDiagnostic()
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
        Assert.Contains(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0013");
    }

    [Fact]
    public void Btdi0013PatternMatchedByAnotherPatternEmitsNoDiagnostic()
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
        Assert.DoesNotContain(result.Diagnostics(["BTDI"]), static x => x.Id == "BTDI0013");
    }
}
