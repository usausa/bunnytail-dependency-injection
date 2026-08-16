namespace BunnyTail.Resolver.Sandbox;

using BenchmarkDotNet.Attributes;

using BunnyTail.Resolver;
using BunnyTail.Resolver.Sandbox.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

// 実ライブラリの解決経路を層ごとに分解して、5.98ns の内訳を帰属させる (P-7)。
// Sandbox のモデル実験 (ResolutionEntryBenchmark) では素のテーブル引きが 2.1〜2.4ns だったが、
// 実ライブラリの Singleton は 5.98ns。差がどの層に費やされているかを、外側から順に剥がして特定する
// Decomposes the real resolution path layer by layer to attribute the 5.98 ns (P-7). The sandbox model
// (ResolutionEntryBenchmark) measured a bare table lookup at 2.1-2.4 ns while the real library resolves a singleton
// in 5.98 ns; this peels the outer layers off one at a time to locate where the difference goes.
[Config(typeof(DisasmConfig))]
public class ResolutionLayerBenchmark
{
    private const int LookupCount = 1024;

    [Params(8, 64)]
    public int N { get; set; }

    private IServiceProvider viaInterface = default!;
    private ResolverServiceProvider viaConcrete = default!;
    private ServiceProviderScope viaScope = default!;
    private ServiceRegistry registry = default!;
    private ServiceIdentifier[] identifiers = default!;
    private Type[] sequence = default!;

    private object? sink;

    [GlobalSetup]
    public void Setup()
    {
        var services = new List<ServiceDescriptor>(N);
        for (var i = 0; i < N; i++)
        {
            services.Add(ServiceDescriptor.Describe(KeyTypes.All[i], KeyTypes.All[i], ServiceLifetime.Singleton));
        }

        viaConcrete = new ResolverServiceProvider(services);
        viaInterface = viaConcrete;
        viaScope = viaConcrete.RootScope;

        // Singleton を全て初回解決させ、hot 読み出しの条件を揃える
        // Resolve every singleton once so all measurements share the hot-read condition.
        for (var i = 0; i < N; i++)
        {
            _ = viaConcrete.GetService(KeyTypes.All[i]);
        }

        // レジストリ直接測定用。別インスタンスだが形状は同じ。Singleton を事前解決して条件を揃える
        // For measuring the registry directly. A separate instance with the same shape; singletons are primed to match.
        registry = new ServiceRegistry(services, viaConcrete);
        for (var i = 0; i < N; i++)
        {
            _ = registry.GetEntry(new ServiceIdentifier(KeyTypes.All[i], null))?.GetValue(viaScope);
        }

        var random = new Random(12345);
        sequence = new Type[LookupCount];
        identifiers = new ServiceIdentifier[LookupCount];
        for (var i = 0; i < LookupCount; i++)
        {
            sequence[i] = KeyTypes.All[random.Next(N)];
            identifiers[i] = new ServiceIdentifier(sequence[i], null);
        }
    }

    [GlobalCleanup]
    public void Cleanup() => viaConcrete.Dispose();

    // 層 1: 利用者が通る経路そのもの (IServiceProvider のインタフェースディスパッチを含む)
    // Layer 1: the path a consumer actually takes, including interface dispatch on IServiceProvider.
    [Benchmark(Baseline = true, OperationsPerInvoke = LookupCount)]
    public object? ViaInterface()
    {
        object? last = null;
        var keys = sequence;
        for (var i = 0; i < keys.Length; i++)
        {
            last = viaInterface.GetService(keys[i]);
        }

        sink = last;
        return last;
    }

    // 層 2: 具象型経由 (インタフェースディスパッチを外す)
    // Layer 2: through the concrete type, removing interface dispatch.
    [Benchmark(OperationsPerInvoke = LookupCount)]
    public object? ViaConcreteProvider()
    {
        object? last = null;
        var keys = sequence;
        for (var i = 0; i < keys.Length; i++)
        {
            last = viaConcrete.GetService(keys[i]);
        }

        sink = last;
        return last;
    }

    // 層 3: ルートスコープ直接 (provider → RootScope の委譲を外す)
    // Layer 3: straight to the root scope, removing the provider-to-scope delegation.
    [Benchmark(OperationsPerInvoke = LookupCount)]
    public object? ViaScope()
    {
        object? last = null;
        var keys = sequence;
        for (var i = 0; i < keys.Length; i++)
        {
            last = viaScope.GetService(keys[i]);
        }

        sink = last;
        return last;
    }

    // 層 4: レジストリ直接 (CheckDisposed と ServiceIdentifier 構築と ResolveService の委譲を外す)
    // Layer 4: straight to the registry, removing CheckDisposed, the ServiceIdentifier construction and the ResolveService hop.
    [Benchmark(OperationsPerInvoke = LookupCount)]
    public object? ViaRegistry()
    {
        object? last = null;
        var ids = identifiers;
        for (var i = 0; i < ids.Length; i++)
        {
            last = registry.GetEntry(ids[i])?.GetValue(viaScope);
        }

        sink = last;
        return last;
    }

    // 層 5: エントリ取得のみ (accessor の呼び出しを外す = テーブル引きの素のコスト)
    // Layer 5: entry lookup only, without invoking the accessor (the bare table lookup cost).
    [Benchmark(OperationsPerInvoke = LookupCount)]
    public object? ViaTableOnly()
    {
        object? last = null;
        var ids = identifiers;
        for (var i = 0; i < ids.Length; i++)
        {
            last = registry.GetEntry(ids[i]);
        }

        sink = last;
        return last;
    }

    //--------------------------------------------------------------------------------
    // 生成ファクトリが依存解決に使う呼び出し形状の比較
    // Comparison of the call shape generated factories use to resolve dependencies
    //--------------------------------------------------------------------------------

    // 生成ファクトリが実際に出力している形。MEDI の拡張メソッドは
    // is ISupportRequiredService の型テスト + インタフェース二重ディスパッチ + キャストを伴う
    // The shape generated factories actually emit. The MEDI extension method performs an
    // is ISupportRequiredService type test, a double interface dispatch and a cast.
    [Benchmark(OperationsPerInvoke = 6)]
    public object? DependencyViaRequiredServiceGeneric()
    {
        var p = viaInterface;
        sink = p.GetRequiredService<K000>();
        sink = p.GetRequiredService<K001>();
        sink = p.GetRequiredService<K002>();
        sink = p.GetRequiredService<K003>();
        sink = p.GetRequiredService<K004>();
        sink = p.GetRequiredService<K005>();
        return sink;
    }

    // 対照: 拡張メソッドを介さない直接呼び出し
    // Control: a direct call that does not go through the extension method.
    [Benchmark(OperationsPerInvoke = 6)]
    public object? DependencyViaGetService()
    {
        var p = viaInterface;
        sink = p.GetService(typeof(K000));
        sink = p.GetService(typeof(K001));
        sink = p.GetService(typeof(K002));
        sink = p.GetService(typeof(K003));
        sink = p.GetService(typeof(K004));
        sink = p.GetService(typeof(K005));
        return sink;
    }
}
