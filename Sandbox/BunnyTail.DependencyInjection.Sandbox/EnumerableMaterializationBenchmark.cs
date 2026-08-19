namespace BunnyTail.DependencyInjection.Sandbox;

using System.Reflection;
using System.Runtime.CompilerServices;

using BenchmarkDotNet.Attributes;

// IEnumerable<T> 実体化の格納方式の比較。
// DI 固有の制約: 要素型は実行時にしか判らない (Type 変数) が、返す配列は要求された T[] の実体でなければならない。
// かつ要素が transient の場合はキャッシュできず、解決のたびに配列を作り直す
// Comparison of store strategies when materializing IEnumerable<T>. DI-specific constraint: the element type is only
// known at runtime (as a Type), yet the returned array must be an actual T[]. When elements are transient the array
// cannot be cached and must be rebuilt on every resolution.
[Config(typeof(BenchmarkConfig))]
public class EnumerableMaterializationBenchmark
{
    public interface IElement;

    public sealed class Element : IElement;

    [Params(1, 3, 5, 10)]
    public int N { get; set; }

    private Func<object>[] factories = default!;
    private Type elementType = default!;
    private Array prototype = default!;
    private Func<int, Array> typedFactory = default!;

    // 計測結果の格納先。読み出さないが、書き込むことで JIT のデッドコード削除を防ぐ
    // Sink for measured values: never read, but written so the JIT cannot eliminate the work.
    // ReSharper disable once NotAccessedField.Local
    private object? sink;

    [GlobalSetup]
    public void Setup()
    {
        factories = new Func<object>[N];
        for (var i = 0; i < N; i++)
        {
            factories[i] = static () => new Element();
        }

        elementType = typeof(IElement);

        // 長さは accessor ごとに固定なので、ビルド時に 1 本作っておける
        // The length is fixed per accessor, so a single instance can be built up front.
        prototype = Array.CreateInstance(elementType, N);

        typedFactory = (Func<int, Array>)typeof(EnumerableMaterializationBenchmark)
            .GetMethod(nameof(CreateTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(elementType)
            .CreateDelegate(typeof(Func<int, Array>));
    }

    private static Array CreateTyped<T>(int length) => new T[length];

    // 現状の実装: Array.CreateInstance + Array.SetValue (どちらもリフレクション経路)
    // Current implementation: Array.CreateInstance + Array.SetValue (both go through reflection).
    [Benchmark(Baseline = true)]
    public object SetValueLoop()
    {
        var array = Array.CreateInstance(elementType, factories.Length);
        for (var i = 0; i < factories.Length; i++)
        {
            array.SetValue(factories[i](), i);
        }

        sink = array;
        return array;
    }

    // 候補: 参照型要素なら T[] は object[] として共変に見えるので、素の配列書き込みで格納する
    // (書き込みは stelem.ref の共変チェックのみ。値型要素には適用できない)
    // Candidate: for reference element types a T[] is covariantly viewable as object[], so plain array stores can be used
    // (the write only pays the stelem.ref covariance check). Not applicable to value type elements.
    [Benchmark]
    public object CovariantStore()
    {
        var array = Array.CreateInstance(elementType, factories.Length);
        var view = Unsafe.As<object?[]>(array);
        for (var i = 0; i < factories.Length; i++)
        {
            view[i] = factories[i]();
        }

        sink = array;
        return array;
    }

    // 上限の目安: 要素型がコンパイル時に判る場合 (生成経路が到達しうる理想形)
    // Upper bound: the element type known at compile time (the ideal shape a generated path could reach).
    [Benchmark]
    public object TypedArray()
    {
        var array = new IElement[factories.Length];
        for (var i = 0; i < factories.Length; i++)
        {
            array[i] = (IElement)factories[i]();
        }

        sink = array;
        return array;
    }

    // 候補: 長さ固定のプロトタイプを保持し、解決のたびに Clone する (Array.CreateInstance を回避)
    // Candidate: keep a fixed-length prototype and Clone it per resolution, avoiding Array.CreateInstance.
    [Benchmark]
    public object ClonePrototype()
    {
        var array = (Array)prototype.Clone();
        var view = Unsafe.As<object?[]>(array);
        for (var i = 0; i < factories.Length; i++)
        {
            view[i] = factories[i]();
        }

        sink = array;
        return array;
    }

    // 候補: 型付き配列を作るデリゲートを一度だけ構築してキャッシュする
    // (MakeGenericMethod を使うため値型要素は NativeAOT で不可。参照型は shared generic で動く)
    // Candidate: build a typed array creation delegate once and cache it.
    // (MakeGenericMethod means value type elements are unusable on NativeAOT; reference types work via shared generics.)
    [Benchmark]
    public object TypedFactory()
    {
        var array = typedFactory(factories.Length);
        var view = Unsafe.As<object?[]>(array);
        for (var i = 0; i < factories.Length; i++)
        {
            view[i] = factories[i]();
        }

        sink = array;
        return array;
    }

    // 配分の切り分け: 生成コストのみ (格納なし)
    // Cost attribution: allocation only, no stores.
    [Benchmark]
    public object CreateOnly()
    {
        var array = Array.CreateInstance(elementType, factories.Length);
        sink = array;
        return array;
    }
}
