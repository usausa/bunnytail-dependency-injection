namespace BunnyTail.Resolver.Tests;

using BunnyTail.Resolver;
using BunnyTail.Resolver.Generator;

using Microsoft.Extensions.DependencyInjection;

using SourceGenerateHelper.Testing;

using Xunit;

// ジェネレータ出力の検証 (M2b: プロトタイプ一致 / M3: Add* 収集・規約登録・診断)
public sealed class GeneratorOutputTest
{
    private static GeneratorTestRunner CreateRunner() =>
        GeneratorTestRunner.For<ResolverGenerator>()
            .WithAssemblyName("BunnyTail.Resolver.Tests")
            .WithReference(typeof(SingletonAttribute).Assembly)
            .WithReference(typeof(IServiceCollection).Assembly);

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    // ---- M2b: 属性コンポーネントの出力一致 ----

    [Fact]
    public void GeneratedSourceMatchesHandWrittenPrototype()
    {
        // ImplicitUsings 相当 (harness コンパイルはプロジェクト設定を持たないため)
        var source = "global using System;" + Environment.NewLine + File.ReadAllText(Path.Combine("Components", "Components.cs"));
        var expected = File.ReadAllText(Path.Combine("Expected", "ComponentRegistration.expected.txt"));

        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        var actual = result.GeneratedSource("GeneratedComponents.g.cs");

        // 失敗時の調査用に実出力を保存
        File.WriteAllText("actual-generated.txt", actual);

        Assert.Equal(Normalize(expected), Normalize(actual));
    }

    // ---- M3: Add* 呼び出し収集 ----

    [Fact]
    public void FactoryIsCollectedFromAddInvocation()
    {
        const string Source = """
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
                    services.AddSingleton(static _ => new CollectedComponent());   // factory 登録は収集対象外
                }
            }
            """;

        var result = CreateRunner()
            .VerifyCompiles()
            .Run(Source);

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        Assert.Contains("typeof(global::Demo.CollectedComponent)", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.DependentComponent),", generated, StringComparison.Ordinal);
        Assert.Contains("provider.GetRequiredService<global::Demo.CollectedComponent>()", generated, StringComparison.Ordinal);
        // AddComponents は属性コンポーネントが無いので出力されない
        Assert.DoesNotContain("AddComponents", generated, StringComparison.Ordinal);
    }

    // ---- M3: 命名規約ベース登録メソッド生成 ----

    [Fact]
    public void ConventionMethodBodyIsGenerated()
    {
        const string Source = """
            using BunnyTail.Resolver;
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

        var result = CreateRunner()
            .VerifyCompiles()
            .Run(Source);

        var generated = result.GeneratedSource("Demo_Registrations.g.cs");

        Assert.Contains("services.AddScoped<global::Demo.IFooService, global::Demo.FooService>();", generated, StringComparison.Ordinal);
        Assert.Contains("services.AddScoped<global::Demo.PlainService>();", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("OtherComponent", generated, StringComparison.Ordinal);

        // 規約マッチしたクラスには生成ファクトリも出力される
        var factories = result.GeneratedSource("GeneratedComponents.g.cs");
        Assert.Contains("typeof(global::Demo.FooService)", factories, StringComparison.Ordinal);
    }

    // ---- M3: 診断 ----

    [Fact]
    public void InvalidMethodDefinitionIsReported()
    {
        const string Source = """
            using BunnyTail.Resolver;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public static class Registrations
            {
                [ComponentRegistration(Lifetime.Singleton, "Service$")]
                public static IServiceCollection AddServices(IServiceCollection services) => services;   // partial でも拡張メソッドでもない
            }
            """;

        var result = CreateRunner().Run(Source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0001");
    }

    [Fact]
    public void CircularDependencyIsReported()
    {
        const string Source = """
            using BunnyTail.Resolver;

            namespace Demo;

            [Singleton]
            public sealed class First(Second second);

            [Singleton]
            public sealed class Second(First first);
            """;

        var result = CreateRunner().Run(Source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0003");
    }

    [Fact]
    public void UnresolvedDependencyIsReported()
    {
        const string Source = """
            using BunnyTail.Resolver;

            namespace Demo;

            public sealed class NotRegistered;

            [Singleton]
            public sealed class Component(NotRegistered dependency);
            """;

        var result = CreateRunner().Run(Source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0004");
    }

    [Fact]
    public void CaptiveDependencyIsReported()
    {
        const string Source = """
            using BunnyTail.Resolver;

            namespace Demo;

            [Scoped]
            public sealed class ScopedDependency;

            [Singleton]
            public sealed class SingletonComponent(ScopedDependency dependency);
            """;

        var result = CreateRunner().Run(Source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0005");
    }

    [Fact]
    public void AmbiguousConstructorIsReported()
    {
        const string Source = """
            using BunnyTail.Resolver;

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

        var result = CreateRunner().Run(Source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0006");
    }

    [Fact]
    public void KeyedFactoryIsGeneratedWithServiceKeyInjection()
    {
        const string Source = """
            using BunnyTail.Resolver;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public interface IKeyed;

            [Singleton(As = typeof(IKeyed), Key = "primary")]
            public sealed class KeyedComponent([ServiceKey] string key) : IKeyed;
            """;

        var result = CreateRunner()
            .VerifyCompiles()
            .Run(Source);

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        Assert.Contains("RegisterKeyed(", generated, StringComparison.Ordinal);
        Assert.Contains("static (provider, key) =>", generated, StringComparison.Ordinal);
        Assert.Contains("(string)key!", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidPatternIsReported()
    {
        const string Source = """
            using BunnyTail.Resolver;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public static partial class Registrations
            {
                [ComponentRegistration(Lifetime.Singleton, "([")]
                public static partial IServiceCollection AddServices(this IServiceCollection services);
            }
            """;

        var result = CreateRunner().Run(Source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0002");
    }
}
