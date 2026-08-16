namespace BunnyTail.Resolver.Tests;

using BunnyTail.Resolver;
using BunnyTail.Resolver.Generator;

using Microsoft.Extensions.DependencyInjection;

using SourceGenerateHelper.Testing;

using Xunit;

// ジェネレータ出力の検証 (期待値一致 / Add* 収集 / 規約登録 / インライン展開 / 診断)
// Verification of generator output (expected text match / Add* collection / convention registration / inline expansion / diagnostics).
public sealed class GeneratorOutputTest
{
    private static GeneratorTestRunner CreateRunner() =>
        GeneratorTestRunner.For<ResolverGenerator>()
            .WithAssemblyName("BunnyTail.Resolver.Tests")
            .WithReference(typeof(SingletonAttribute).Assembly)
            .WithReference(typeof(IServiceCollection).Assembly);

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    // ---- 属性コンポーネントの出力一致 / attribute component output match ----

    [Fact]
    public void GeneratedSourceMatchesHandWrittenPrototype()
    {
        // ImplicitUsings 相当 (harness コンパイルはプロジェクト設定を持たないため)
        // Equivalent of ImplicitUsings (the harness compilation has no project settings).
        var source = "global using System;" + Environment.NewLine + File.ReadAllText(Path.Combine("Components", "Components.cs"));
        var expected = File.ReadAllText(Path.Combine("Expected", "ComponentRegistration.expected.txt"));

        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        var actual = result.GeneratedSource("GeneratedComponents.g.cs");

        // 失敗時の調査用に実出力を保存
        // Saves the actual output for investigating failures.
        File.WriteAllText("actual-generated.txt", actual);

        Assert.Equal(Normalize(expected), Normalize(actual));
    }

    // ---- Add* 呼び出し収集 / Add* invocation collection ----

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
        // transient 依存はインライン展開され、前提 (InlinedDependency) が登録される
        // Transient dependencies are inlined and the assumption (InlinedDependency) is registered.
        Assert.Contains("new global::Demo.CollectedComponent())", generated, StringComparison.Ordinal);
        Assert.Contains("new global::BunnyTail.Resolver.InlinedDependency(typeof(global::Demo.CollectedComponent), typeof(global::Demo.CollectedComponent))", generated, StringComparison.Ordinal);
        // AddComponents は属性コンポーネントが無いので出力されない
        // AddComponents is not emitted because there are no attribute components.
        Assert.DoesNotContain("AddComponents", generated, StringComparison.Ordinal);
    }

    // ---- 命名規約ベース登録メソッド生成 / convention based registration method generation ----

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
        // Generated factories are also emitted for convention matched classes.
        var factories = result.GeneratedSource("GeneratedComponents.g.cs");
        Assert.Contains("typeof(global::Demo.FooService)", factories, StringComparison.Ordinal);
    }

    // ---- 診断 / diagnostics ----

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

    // ---- transient 依存のインライン展開 / inline expansion of transient dependencies ----

    [Fact]
    public void TransientDependenciesAreInlined()
    {
        const string Source = """
            using System;

            using BunnyTail.Resolver;

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

        var result = CreateRunner()
            .VerifyCompiles()
            .Run(Source);

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        // transient 依存はネストも含めリテラル new 展開。同一依存も使用箇所ごとに new (インスタンス共有しない)
        // Transient dependencies are expanded as literal new including nesting. The same dependency gets a fresh new per use site (never shared).
        Assert.Contains("new global::Demo.Branch(new global::Demo.Leaf()),", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.Branch(new global::Demo.Leaf()));", generated, StringComparison.Ordinal);

        // 前提はトップレベルの展開のみ (Root は Branch のみ。Leaf は Branch 自身のエントリが検証する)
        // Assumptions cover top-level expansions only (Root records Branch only; Leaf is validated by Branch's own entry).
        Assert.Contains("[new global::BunnyTail.Resolver.InlinedDependency(typeof(global::Demo.Branch), typeof(global::Demo.Branch))],", generated, StringComparison.Ordinal);

        // singleton 依存は deps スロット (Unsafe.As)、disposable transient は scope 経由のまま
        // (DisposableLeaf 自身の Register ファクトリの new は正当な出力なので、使用箇所側の解決式で判定する)
        // Singleton dependencies become deps slots (Unsafe.As); disposable transients stay on the scope path
        // (the new inside DisposableLeaf's own Register factory is legitimate output, so the assertion checks the use site).
        Assert.Contains("global::System.Runtime.CompilerServices.Unsafe.As<global::Demo.Shared>(deps[0])!", generated, StringComparison.Ordinal);
        Assert.Contains("[new global::BunnyTail.Resolver.InlinedDependency(typeof(global::Demo.Shared), typeof(global::Demo.Shared))],", generated, StringComparison.Ordinal);
        Assert.Contains("static (provider, deps) =>", generated, StringComparison.Ordinal);
        Assert.Contains("scope.GetRequiredService<global::Demo.DisposableLeaf>()", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.Mixed(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TransientCycleDoesNotBreakInlineExpansion()
    {
        // 循環は BTRS0003 (Error) だが、インライン展開自体は無限再帰せず生成が完了すること
        // Cycles are BTRS0003 (Error), but inline expansion itself must finish generation without infinite recursion.
        const string Source = """
            using BunnyTail.Resolver;

            namespace Demo;

            [Transient]
            public sealed class First(Second second);

            [Transient]
            public sealed class Second(First first);
            """;

        var result = CreateRunner().Run(Source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0003");

        // 循環箇所は GetRequiredService へフォールバックして出力される
        // The cyclic edge is emitted as a GetRequiredService fallback.
        var generated = result.GeneratedSource("GeneratedComponents.g.cs");
        Assert.Contains("scope.GetRequiredService<global::Demo.First>()", generated, StringComparison.Ordinal);
    }

    // ---- 初期化コールバック / initialization callbacks ----

    [Fact]
    public void InitializationCallbacksAreEmitted()
    {
        const string Source = """
            using BunnyTail.Resolver;

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

        var result = CreateRunner()
            .VerifyCompiles()
            .Run(Source);

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        // PostConstruct はメソッド名を直接呼び、IInitializable はインタフェース経由で呼ぶ
        // PostConstruct calls the named method directly; IInitializable is invoked through the interface.
        Assert.Contains("instance.Setup();", generated, StringComparison.Ordinal);
        Assert.Contains("((global::BunnyTail.Resolver.IInitializable)instance).Initialize();", generated, StringComparison.Ordinal);

        // IInitializable はサービスとして転送登録されない
        // IInitializable is never registered as a forwarded service.
        Assert.DoesNotContain("services.AddTransient<global::BunnyTail.Resolver.IInitializable>", generated, StringComparison.Ordinal);

        // 初期化コールバックを持つ型はインライン展開されない (親は GetRequiredService 経由)
        // Types with an initialization callback are not inlined; the parent resolves them through GetRequiredService.
        Assert.Contains("scope.GetRequiredService<global::Demo.WithInterface>()", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidPostConstructIsReported()
    {
        const string Source = """
            using BunnyTail.Resolver;

            namespace Demo;

            [Singleton(PostConstruct = "Missing")]
            public sealed class Component;
            """;

        var result = CreateRunner().Run(Source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0007");
    }

    [Fact]
    public void ConflictingPostConstructIsReported()
    {
        const string Source = """
            using BunnyTail.Resolver;

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

        var result = CreateRunner().Run(Source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0008");
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
