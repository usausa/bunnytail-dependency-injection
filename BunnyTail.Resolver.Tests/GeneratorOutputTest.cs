namespace BunnyTail.Resolver.Tests;

using BunnyTail.Resolver;
using BunnyTail.Resolver.Generator;

using Microsoft.Extensions.DependencyInjection;

using SourceGenerateHelper.Testing;

using Xunit;

// ジェネレータ出力の検証 (期待値一致 / Add* 収集 / 規約登録 / インライン展開 / 診断)
// Verification of generator output (expected text match / Add* collection / convention registration / inline expansion / diagnostics).
// テスト内の const は他のソースと同じく PascalCase で統一する (ReSharper 既定の camelCase 規約とは異なる)
// Constants in tests use PascalCase like the rest of the sources, unlike the ReSharper default of camelCase.
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
                    services.AddSingleton(static _ => new CollectedComponent());   // factory 登録は収集対象外
                }
            }
            """;

        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        Assert.Contains("typeof(global::Demo.CollectedComponent)", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.DependentComponent),", generated, StringComparison.Ordinal);
        // transient 依存はインライン展開され、前提 (InlinedDependency) が登録される
        // Transient dependencies are inlined and the assumption (InlinedDependency) is registered.
        Assert.Contains("new global::Demo.CollectedComponent())", generated, StringComparison.Ordinal);
        Assert.Contains("new global::BunnyTail.Resolver.InlinedDependency(typeof(global::Demo.CollectedComponent), typeof(global::Demo.CollectedComponent))", generated, StringComparison.Ordinal);
        // AddGeneratedComponents は属性コンポーネントが無いので出力されない
        // AddGeneratedComponents is not emitted because there are no attribute components.
        Assert.DoesNotContain("AddGeneratedComponents", generated, StringComparison.Ordinal);
    }

    // ---- 命名規約ベース登録メソッド生成 / convention based registration method generation ----

    [Fact]
    public void ConventionMethodBodyIsGenerated()
    {
        const string source = """
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
            .Run(source);

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
        const string source = """
            using BunnyTail.Resolver;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public static class Registrations
            {
                [ComponentRegistration(Lifetime.Singleton, "Service$")]
                public static IServiceCollection AddServices(IServiceCollection services) => services;   // partial でも拡張メソッドでもない
            }
            """;

        var result = CreateRunner().Run(source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0001");
    }

    [Fact]
    public void CircularDependencyIsReported()
    {
        const string source = """
            using BunnyTail.Resolver;

            namespace Demo;

            [Singleton]
            public sealed class First(Second second);

            [Singleton]
            public sealed class Second(First first);
            """;

        var result = CreateRunner().Run(source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0008");
    }

    [Fact]
    public void UnresolvedDependencyIsReported()
    {
        const string source = """
            using BunnyTail.Resolver;

            namespace Demo;

            public sealed class NotRegistered;

            [Singleton]
            public sealed class Component(NotRegistered dependency);
            """;

        var result = CreateRunner().Run(source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0009");
    }

    [Fact]
    public void CaptiveDependencyIsReported()
    {
        const string source = """
            using BunnyTail.Resolver;

            namespace Demo;

            [Scoped]
            public sealed class ScopedDependency;

            [Singleton]
            public sealed class SingletonComponent(ScopedDependency dependency);
            """;

        var result = CreateRunner().Run(source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0010");
    }

    [Fact]
    public void AmbiguousConstructorIsReported()
    {
        const string source = """
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

        var result = CreateRunner().Run(source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0005");
    }

    [Fact]
    public void KeyedFactoryIsGeneratedWithServiceKeyInjection()
    {
        const string source = """
            using BunnyTail.Resolver;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public interface IKeyed;

            [Singleton(As = typeof(IKeyed), Key = "primary")]
            public sealed class KeyedComponent([ServiceKey] string key) : IKeyed;
            """;

        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        Assert.Contains("RegisterKeyed(", generated, StringComparison.Ordinal);
        Assert.Contains("static (provider, key) =>", generated, StringComparison.Ordinal);
        Assert.Contains("(string)key!", generated, StringComparison.Ordinal);
    }

    // ---- transient 依存のインライン展開 / inline expansion of transient dependencies ----

    [Fact]
    public void TransientDependenciesAreInlined()
    {
        const string source = """
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
            .Run(source);

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        // transient 依存はネストも含めリテラル new 展開。同一依存も使用箇所ごとに new (インスタンス共有しない)
        // Transient dependencies are expanded as literal new including nesting. The same dependency gets a fresh new per use site (never shared).
        Assert.Contains("new global::Demo.Branch(new global::Demo.Leaf()),", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.Branch(new global::Demo.Leaf()));", generated, StringComparison.Ordinal);

        // 前提はトップレベルの展開のみ (Root は Branch のみ。Leaf は Branch 自身のエントリが検証する)
        // Assumptions cover top-level expansions only (Root records Branch only; Leaf is validated by Branch's own entry).
        Assert.Contains("[new global::BunnyTail.Resolver.InlinedDependency(typeof(global::Demo.Branch), typeof(global::Demo.Branch))],", generated, StringComparison.Ordinal);

        // singleton 依存はインスタンススロット (Unsafe.As)、disposable transient はアクセサスロット
        // (DisposableLeaf 自身の Register ファクトリの new は正当な出力なので、使用箇所側の解決式で判定する)
        // Singleton dependencies become instance slots (Unsafe.As); disposable transients become accessor slots
        // (the new inside DisposableLeaf's own Register factory is legitimate output, so the assertion checks the use site).
        Assert.Contains("global::System.Runtime.CompilerServices.Unsafe.As<global::Demo.Shared>(deps[0])!", generated, StringComparison.Ordinal);
        Assert.Contains("[new global::BunnyTail.Resolver.DependencyPlan(typeof(global::Demo.Shared), typeof(global::Demo.Shared)), new global::BunnyTail.Resolver.DependencyPlan(typeof(global::Demo.DisposableLeaf))],", generated, StringComparison.Ordinal);
        Assert.Contains("static (provider, deps) =>", generated, StringComparison.Ordinal);
        Assert.Contains("global::System.Runtime.CompilerServices.Unsafe.As<global::BunnyTail.Resolver.DependencyAccessor>(deps[1])!.GetValue<global::Demo.DisposableLeaf>(scope)", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.Mixed(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TransientCycleDoesNotBreakInlineExpansion()
    {
        // 循環は BTRS0008 (Error) だが、インライン展開自体は無限再帰せず生成が完了すること
        // Cycles are BTRS0008 (Error), but inline expansion itself must finish generation without infinite recursion.
        const string source = """
            using BunnyTail.Resolver;

            namespace Demo;

            [Transient]
            public sealed class First(Second second);

            [Transient]
            public sealed class Second(First first);
            """;

        var result = CreateRunner().Run(source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0008");

        // 循環箇所はアクセサスロットへフォールバックして出力される (実行時は採用検証が循環を検出する)
        // The cyclic edge falls back to an accessor slot (adoption validation detects the cycle at runtime).
        var generated = result.GeneratedSource("GeneratedComponents.g.cs");
        Assert.Contains(".GetValue<global::Demo.First>(scope)", generated, StringComparison.Ordinal);
    }

    // ---- Add* 収集の拡張形 / expanded Add* collection shapes ----

    [Fact]
    public void ExpandedAddShapesGenerateFactories()
    {
        // typeof オーバーロード / TryAddEnumerable + descriptor / keyed / Add(descriptor) の 4 形式
        // Four shapes: typeof overloads, TryAddEnumerable with a descriptor, keyed and Add(descriptor).
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

        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        // 非 keyed 形はすべて Register、keyed 形は RegisterKeyed のファクトリになる
        // Non-keyed shapes get Register factories; the keyed shape gets a RegisterKeyed factory.
        Assert.Contains("typeof(global::Demo.ThingA)", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.ThingB)", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.ThingD)", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.SelfThing)", generated, StringComparison.Ordinal);
        var keyedIndex = generated.IndexOf("RegisterKeyed(", StringComparison.Ordinal);
        Assert.True((keyedIndex >= 0) && (generated.IndexOf("typeof(global::Demo.ThingC)", keyedIndex, StringComparison.Ordinal) > keyedIndex));

        // TryAddEnumerable は enumerable 前提を毒化するため、IThing の enumerable ファクトリは生成されない
        // TryAddEnumerable poisons the enumerable assumption, so no enumerable factory is generated for IThing.
        Assert.DoesNotContain("RegisterEnumerable(", generated, StringComparison.Ordinal);
    }

    // ---- GenerateComponentFactory / factory generation without registration ----

    [Fact]
    public void GenerateComponentFactoryEmitsFactoryWithoutRegistration()
    {
        const string source = """
            using BunnyTail.Resolver;

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

        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        // ファクトリは出力されるが、登録メソッドには現れない (登録は利用側の責務)
        // The factory is emitted but never appears in a registration method (registration stays the caller's responsibility).
        Assert.Contains("typeof(global::Demo.Uncontrolled)", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.Uncontrolled(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("services.Add", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateComponentFactoryEmitsPostConstruct()
    {
        const string source = """
            using BunnyTail.Resolver;

            [assembly: GenerateComponentFactory(typeof(Demo.Uncontrolled), PostConstruct = "Prepare")]

            namespace Demo;

            public sealed class Uncontrolled
            {
                public bool Prepared { get; private set; }

                public void Prepare() => Prepared = true;
            }
            """;

        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        // 生成ファクトリが初期化を呼び、実行時経路のために登録も出力される
        // The generated factory invokes the initializer, and the registration for the runtime path is emitted too.
        Assert.Contains("instance.Prepare();", generated, StringComparison.Ordinal);
        Assert.Contains("RegisterInitializer(typeof(global::Demo.Uncontrolled), \"Prepare\")", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidGenerateComponentFactoryPostConstructIsReported()
    {
        const string source = """
            using BunnyTail.Resolver;

            [assembly: GenerateComponentFactory(typeof(Demo.Uncontrolled), PostConstruct = "Missing")]

            namespace Demo;

            public sealed class Uncontrolled;
            """;

        var result = CreateRunner().Run(source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0006");
    }

    [Fact]
    public void InvalidGenerateComponentFactoryTargetIsReported()
    {
        const string source = """
            using BunnyTail.Resolver;

            [assembly: GenerateComponentFactory(typeof(Demo.NotConstructible))]

            namespace Demo;

            public abstract class NotConstructible;
            """;

        var result = CreateRunner().Run(source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0004");
    }

    // ---- Assembly 指定の規約登録 / assembly scoped convention registration ----

    [Fact]
    public void AssemblyScopedConventionRegistersExternalTypes()
    {
        // このテストアセンブリのメタデータから規約で候補を拾う (Generator 非参照ライブラリ相当)
        // Picks candidates from this test assembly's metadata, standing in for a library without the generator.
        const string source = """
            using BunnyTail.Resolver;

            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public static partial class Registrations
            {
                [ComponentRegistration(Lifetime.Transient, "^MultiLeafA$", Assembly = "BunnyTail.Resolver.Tests")]
                public static partial IServiceCollection AddExternal(this IServiceCollection services);
            }
            """;

        var result = GeneratorTestRunner.For<ResolverGenerator>()
            .WithAssemblyName("Demo")
            .WithReference(typeof(SingletonAttribute).Assembly)
            .WithReference(typeof(IServiceCollection).Assembly)
            .WithReference(typeof(GeneratorOutputTest).Assembly)
            .VerifyCompiles()
            .Run(source);

        var generated = result.GeneratedSource("Demo_Registrations.g.cs");

        // 外部型が規約メソッド本体に登録され、生成ファクトリも作られる
        // The external type is registered in the convention method body and gets a generated factory.
        Assert.Contains("global::BunnyTail.Resolver.Tests.Components.MultiLeafA", generated, StringComparison.Ordinal);
        var components = result.GeneratedSource("GeneratedComponents.g.cs");
        Assert.Contains("typeof(global::BunnyTail.Resolver.Tests.Components.MultiLeafA)", components, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingAssemblyIsReported()
    {
        const string source = """
            using BunnyTail.Resolver;

            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public static partial class Registrations
            {
                [ComponentRegistration(Lifetime.Transient, ".*", Assembly = "No.Such.Assembly")]
                public static partial IServiceCollection AddExternal(this IServiceCollection services);
            }
            """;

        var result = CreateRunner().Run(source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0003");
    }

    // ---- モジュール集約 / module aggregation ----

    [Fact]
    public void ModuleMarkerIsEmittedForAttributeComponents()
    {
        const string source = """
            using BunnyTail.Resolver;

            namespace Demo;

            [Singleton]
            public sealed class AppComponent;
            """;

        var result = GeneratorTestRunner.For<ResolverGenerator>()
            .WithAssemblyName("Demo")
            .WithReference(typeof(SingletonAttribute).Assembly)
            .WithReference(typeof(IServiceCollection).Assembly)
            .VerifyCompiles()
            .Run(source);

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        // 属性コンポーネントを持つアセンブリはモジュールマーカーを埋め込む
        // Assemblies with attribute components embed the module marker.
        Assert.Contains("[assembly: global::BunnyTail.Resolver.ComponentModule(typeof(global::Demo.GeneratedComponents))]", generated, StringComparison.Ordinal);
        Assert.Contains("public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddAllGeneratedComponents(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferencedModulesAreAggregatedIntoAddAllGeneratedComponents()
    {
        // このテストアセンブリ自身が属性コンポーネントを持つ生成モジュール (マーカー入り) なので、参照モジュールとして使う
        // This test assembly itself is a generated module with the marker, so it doubles as the referenced module.
        const string source = """
            using BunnyTail.Resolver;

            namespace Demo;

            [Singleton]
            public sealed class AppComponent;
            """;

        var result = GeneratorTestRunner.For<ResolverGenerator>()
            .WithAssemblyName("Demo")
            .WithReference(typeof(SingletonAttribute).Assembly)
            .WithReference(typeof(IServiceCollection).Assembly)
            .WithReference(typeof(GeneratorOutputTest).Assembly)
            .VerifyCompiles()
            .Run(source);

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        // 参照モジュール → 自アセンブリの順で集約される
        // Aggregation calls referenced modules first, then this assembly's components.
        Assert.Contains("global::BunnyTail.Resolver.Tests.GeneratedComponents.AddGeneratedComponents(services);", generated, StringComparison.Ordinal);
        var moduleCall = generated.IndexOf("global::BunnyTail.Resolver.Tests.GeneratedComponents.AddGeneratedComponents(services);", StringComparison.Ordinal);
        var selfCall = generated.IndexOf("        AddGeneratedComponents(services);", StringComparison.Ordinal);
        Assert.True((moduleCall >= 0) && (selfCall > moduleCall));
    }

    // ---- 生成 enumerable ファクトリ / generated enumerable factories ----

    [Fact]
    public void EnumerableFactoryIsGeneratedForAllTransientElements()
    {
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

        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        // 全要素 transient の enumerable は配列リテラルへ畳まれる
        // All-transient enumerables are folded into an array literal.
        Assert.Contains("RegisterEnumerable(", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.IMulti),", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.IMulti[]", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.Multi1(),", generated, StringComparison.Ordinal);
    }

    // ---- open generic の閉型生成 / closed factories from open generic registrations ----

    [Fact]
    public void ClosedGenericFactoriesAreDiscoveredFromConstructorDependencies()
    {
        // typeof の出現なし。コンストラクタ依存だけから閉型ファクトリが発見される
        // No typeof usage anywhere; the closed factory is discovered from the constructor dependency alone.
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

        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        Assert.Contains("typeof(global::Demo.Repository<int>)", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.Repository<int>())", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ValueTypeRuntimeGenericIsReported()
    {
        // 既定値付き ctor で生成不適格 → 値型引数の閉型が実行時経路に残る → BTRS0011。
        // 参照型引数側は AOT でも動くため警告しない
        // A default-valued constructor makes generation ineligible, leaving the closed forms on the runtime path.
        // The value type argument case reports BTRS0011; the reference type case works on AOT and stays silent.
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

        var result = CreateRunner().Run(source);

        var diagnostics = result.Diagnostics(["BTRS"]).Where(static x => x.Id == "BTRS0011").ToArray();
        Assert.Single(diagnostics);
        Assert.Contains("Repository<int>", diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void ClosedGenericFactoriesAreGeneratedFromOpenGenericRegistrations()
    {
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

        var result = CreateRunner()
            .VerifyCompiles()
            .Run(source);

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        // 閉型使用ごとに閉じた実装型の生成ファクトリが出力される (値型引数も AOT 安全になる)
        // A generated factory is emitted per closed usage (value type arguments become AOT safe as well).
        Assert.Contains("typeof(global::Demo.Repository<string>)", generated, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.Repository<int>)", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.Repository<string>())", generated, StringComparison.Ordinal);
    }

    // ---- 初期化コールバック / initialization callbacks ----

    [Fact]
    public void InitializationCallbacksAreEmitted()
    {
        const string source = """
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
            .Run(source);

        var generated = result.GeneratedSource("GeneratedComponents.g.cs");

        // PostConstruct はメソッド名を直接呼び、IInitializable はインタフェース経由で呼ぶ
        // PostConstruct calls the named method directly; IInitializable is invoked through the interface.
        Assert.Contains("instance.Setup();", generated, StringComparison.Ordinal);
        Assert.Contains("((global::BunnyTail.Resolver.IInitializable)instance).Initialize();", generated, StringComparison.Ordinal);

        // IInitializable はサービスとして転送登録されない
        // IInitializable is never registered as a forwarded service.
        Assert.DoesNotContain("services.AddTransient<global::BunnyTail.Resolver.IInitializable>", generated, StringComparison.Ordinal);

        // 初期化コールバックを持つ型はインライン展開されない (親はアクセサスロット経由)
        // Types with an initialization callback are not inlined; the parent resolves them through an accessor slot.
        Assert.Contains(".GetValue<global::Demo.WithInterface>(scope)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidPostConstructIsReported()
    {
        const string source = """
            using BunnyTail.Resolver;

            namespace Demo;

            [Singleton(PostConstruct = "Missing")]
            public sealed class Component;
            """;

        var result = CreateRunner().Run(source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0006");
    }

    [Fact]
    public void ConflictingPostConstructIsReported()
    {
        const string source = """
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

        var result = CreateRunner().Run(source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0007");
    }

    [Fact]
    public void InvalidPatternIsReported()
    {
        const string source = """
            using BunnyTail.Resolver;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public static partial class Registrations
            {
                [ComponentRegistration(Lifetime.Singleton, "([")]
                public static partial IServiceCollection AddServices(this IServiceCollection services);
            }
            """;

        var result = CreateRunner().Run(source);

        Assert.Contains(result.Diagnostics(["BTRS"]), static x => x.Id == "BTRS0002");
    }
}
