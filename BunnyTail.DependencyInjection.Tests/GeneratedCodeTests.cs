namespace BunnyTail.DependencyInjection.Tests;

using System.Text.RegularExpressions;

public sealed class GeneratedCodeTests
{
    private static string Compact(string text) => Regex.Replace(text, @"\s+", string.Empty);

    [Fact]
    public void InjectPropertiesOfBaseClassAndInitAndRequiredAreSetInInitializer()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            public interface INavigator;

            [Singleton(As = typeof(INavigator))]
            public sealed class Navigator : INavigator;

            public abstract class ViewModelBase
            {
                [Inject]
                public INavigator Navigator { get; set; } = default!;
            }

            [Transient]
            public sealed class MainViewModel : ViewModelBase;

            [Transient]
            public sealed class InitViewModel
            {
                [Inject]
                public INavigator Navigator { get; init; } = default!;
            }

            [Transient]
            public sealed class RequiredViewModel
            {
                [Inject]
                public required INavigator Navigator { get; set; }
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().WithAssemblyName("Demo").Run(source);

        // Assert
        Assert.Empty(result.Problems);
        var generated = Compact(result.GeneratedSource("GeneratedComponents.g.cs"));
        Assert.Contains("newglobal::Demo.MainViewModel(){Navigator=", generated, StringComparison.Ordinal);
        Assert.Contains("newglobal::Demo.InitViewModel(){Navigator=", generated, StringComparison.Ordinal);
        Assert.Contains("newglobal::Demo.RequiredViewModel(){Navigator=", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TypeWithRequiredMemberOtherThanInjectHasNoGeneratedFactory()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            [Transient]
            public sealed class Holder
            {
                public required string Name { get; set; }
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().WithAssemblyName("Demo").Run(source);

        // Assert
        Assert.Empty(result.Problems);
        Assert.DoesNotContain("new global::Demo.Holder(", result.GeneratedSource("GeneratedComponents.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationChainedAfterGeneratedComponentsIsCollected()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public interface IChained;

            public sealed class Chained : IChained;

            public sealed class Other;

            [Singleton]
            public sealed class Component;

            public static class Setup
            {
                public static IServiceCollection Configure(IServiceCollection services) =>
                    services.AddGeneratedComponents().AddSingleton<IChained, Chained>().AddTransient<Other>();
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().WithAssemblyName("Demo").Run(source);

        // Assert
        Assert.Empty(result.Problems);
        var generated = result.GeneratedSource("GeneratedComponents.g.cs");
        Assert.Contains("new global::Demo.Chained()", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.Other()", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationMethodRepeatsDeclaration()
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
                public static partial IServiceCollection AddRenamed(this IServiceCollection collection);

                [ComponentRegistration(Lifetime.Singleton, "Service$")]
                internal static partial IServiceCollection AddGeneric<T>(this IServiceCollection services)
                    where T : class;
            }
            """;

        // Act
        var problems = GeneratorTestHelper.GetProblemIds(source);

        // Assert
        Assert.Empty(problems);
    }

    [Fact]
    public void RegistrationClassesWithSimilarNamesDoNotCollide()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace App_Data
            {
                public sealed class FooService;

                public static partial class Registrations
                {
                    [ComponentRegistration(Lifetime.Singleton, "Service$")]
                    public static partial IServiceCollection AddDataServices(this IServiceCollection services);
                }
            }

            namespace App
            {
                public static partial class Data_Registrations
                {
                    [ComponentRegistration(Lifetime.Singleton, "Service$", Namespace = "App_Data")]
                    public static partial IServiceCollection AddAppServices(this IServiceCollection services);
                }
            }
            """;

        // Act
        var problems = GeneratorTestHelper.GetProblemIds(source);

        // Assert
        Assert.Empty(problems);
    }

    [Fact]
    public void AssemblyNameThatIsNotNamespaceIsConverted()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            [Singleton]
            public sealed class Component;
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().WithAssemblyName("My-App.1st").Run(source);

        // Assert
        Assert.Empty(result.Problems);
        Assert.Contains("namespace My_App._1st;", result.GeneratedSource("GeneratedComponents.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void NullableTypeArgumentsCompileWithoutWarning()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;

            namespace Demo;

            public interface IStore<T>;

            [Singleton(As = typeof(IStore<string?>))]
            public sealed class Store : IStore<string?>;

            [Transient]
            public sealed class UsesStore(IStore<string?> store)
            {
                public IStore<string?> Store { get; } = store;

                [Inject]
                public IStore<string?> Injected { get; set; } = default!;
            }

            [Transient]
            public sealed class UsesStoreTransient(IStore<string?> store)
            {
                public IStore<string?> Store { get; } = store;
            }

            [Transient]
            public sealed class Wrapper(UsesStoreTransient inner, IStore<string?> direct)
            {
                public object Inner { get; } = inner;

                public object Direct { get; } = direct;
            }

            public interface IKeyed<T>;

            [Singleton(WithInterfaces = true)]
            public sealed class KeyedStore : IKeyed<string?>;
            """;

        // Act
        var problems = GeneratorTestHelper.GetProblemIds(source);

        // Assert
        Assert.Empty(problems);
    }

    [Fact]
    public void RecordComponentIsRegistered()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public interface IClock;

            [Singleton(As = typeof(IClock))]
            public sealed class SystemClock : IClock;

            [Singleton]
            public sealed record RecordService(IClock Clock);

            public sealed record RecordHandler;

            public static partial class Registrations
            {
                [ComponentRegistration(Lifetime.Transient, "Handler$")]
                public static partial IServiceCollection AddHandlers(this IServiceCollection services);
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().WithAssemblyName("Demo").Run(source);

        // Assert
        Assert.Empty(result.Problems);
        Assert.Contains("AddSingleton<global::Demo.RecordService>(services)", result.GeneratedSource("GeneratedComponents.g.cs"), StringComparison.Ordinal);
        Assert.Contains("AddTransient<global::Demo.RecordHandler>(services)", result.GeneratedSource("Demo_Registrations.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationIsNotTakenByUserExtensionMethod()
    {
        // Arrange
        const string source = """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            [Singleton]
            public sealed class FooService;

            public static class UserExtensions
            {
                public static IServiceCollection AddSingleton<T>(this IServiceCollection services)
                    where T : class => services;
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().WithAssemblyName("Demo").Run(source);

        // Assert
        Assert.Empty(result.Problems);
        Assert.Contains("global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<global::Demo.FooService>(services)", result.GeneratedSource("GeneratedComponents.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ObsoleteMembersCompileWithoutWarning()
    {
        // Arrange
        const string source = """
            using System;

            using BunnyTail.DependencyInjection;

            namespace Demo;

            [Obsolete("old")]
            [Singleton]
            public sealed class OldService;

            [Singleton]
            public sealed class RemovedConstructor
            {
                [Obsolete("removed", true)]
                public RemovedConstructor()
                {
                }
            }
            """;

        // Act
        var result = GeneratorTestHelper.CreateRunner().WithAssemblyName("Demo").Run(source);

        // Assert
        Assert.Empty(result.Problems);
        Assert.DoesNotContain("new global::Demo.RemovedConstructor(", result.GeneratedSource("GeneratedComponents.g.cs"), StringComparison.Ordinal);
    }
}
