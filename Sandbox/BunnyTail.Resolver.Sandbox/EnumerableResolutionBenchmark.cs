namespace BunnyTail.Resolver.Sandbox;

using BenchmarkDotNet.Attributes;

// MultipleTransient (実測 99.6ns、MEDI 32.7ns) の内訳分解。
// 現行の実体化は「enumerable accessor の仮想呼び出し → 型付き配列生成 → 要素ごとに
// accessor 仮想呼び出し 2 段 + デリゲート起動」で、ベンチ側の foreach 列挙も含まれる。
// どの層が支配的かを特定し、S-6 (生成 enumerable ファクトリ = 配列リテラル直書き) の回収幅を見積もる
// Decomposition of MultipleTransient (measured 99.6ns vs MEDI 32.7ns). Current materialization goes through the
// enumerable accessor's virtual call, a typed array factory, then two virtual hops plus a delegate per element,
// and the benchmark's foreach enumeration on top. This locates the dominant layer and bounds what S-6
// (a generated enumerable factory emitting an array literal) can recover.
[Config(typeof(BenchmarkConfig))]
public class EnumerableResolutionBenchmark
{
    public interface IElement;

    public sealed class Element1 : IElement;

    public sealed class Element2 : IElement;

    public sealed class Element3 : IElement;

    public sealed class Element4 : IElement;

    public sealed class Element5 : IElement;

    // 現行ランタイムの accessor 連鎖を模したモデル (GetValue 仮想 → Create 仮想 → デリゲート → new)
    // Models the current accessor chain: virtual GetValue, virtual Create, delegate invoke, then new.
    public abstract class AccessorModel
    {
        public readonly bool TrackDisposable;

        protected AccessorModel(bool trackDisposable)
        {
            TrackDisposable = trackDisposable;
        }

        public virtual object? GetValue(object scope)
        {
            var value = Create(scope);
            if (TrackDisposable)
            {
                throw new InvalidOperationException();
            }

            return value;
        }

        protected abstract object? Create(object scope);
    }

    public sealed class FactoryAccessorModel : AccessorModel
    {
        private readonly Func<object, object> factory;

        public FactoryAccessorModel(Func<object, object> factory)
            : base(trackDisposable: false)
        {
            this.factory = factory;
        }

        protected override object? Create(object scope) => factory(scope);
    }

    public sealed class EnumerableAccessorModel : AccessorModel
    {
        private readonly AccessorModel[] items;

        private readonly Func<int, Array> arrayFactory;

        public EnumerableAccessorModel(AccessorModel[] items, Func<int, Array> arrayFactory)
            : base(trackDisposable: false)
        {
            this.items = items;
            this.arrayFactory = arrayFactory;
        }

        protected override object? Create(object scope)
        {
            var typed = arrayFactory(items.Length);
            var view = (object?[])typed;
            for (var i = 0; i < items.Length; i++)
            {
                view[i] = items[i].GetValue(scope);
            }

            return typed;
        }
    }

    private AccessorModel enumerableAccessor = default!;
    private IElement[] cachedArray = default!;
    private readonly object scope = new();

    private object? sink;

    [GlobalSetup]
    public void Setup()
    {
        enumerableAccessor = new EnumerableAccessorModel(
            [
                new FactoryAccessorModel(static _ => new Element1()),
                new FactoryAccessorModel(static _ => new Element2()),
                new FactoryAccessorModel(static _ => new Element3()),
                new FactoryAccessorModel(static _ => new Element4()),
                new FactoryAccessorModel(static _ => new Element5())
            ],
            static length => new IElement[length]);

        cachedArray = [new Element1(), new Element2(), new Element3(), new Element4(), new Element5()];
    }

    // 現行形状 + 列挙 (対外ベンチの MultipleTransient と同型)
    // Current shape plus enumeration (same shape as MultipleTransient in the external benchmark).
    [Benchmark(Baseline = true)]
    public int CurrentShapeWithForeach()
    {
        var count = 0;
        foreach (var element in (IEnumerable<IElement>)enumerableAccessor.GetValue(scope)!)
        {
            count += element is null ? 0 : 1;
        }

        sink = count;
        return count;
    }

    // 現行形状のみ (列挙コストを外す)
    // Current shape only, without enumeration.
    [Benchmark]
    public object? CurrentShapeOnly()
    {
        sink = enumerableAccessor.GetValue(scope);
        return sink;
    }

    // S-6 の目標形状: 配列リテラル直書き + 列挙
    // The S-6 target shape: a literal array expression, plus enumeration.
    [Benchmark]
    public int InlineArrayWithForeach()
    {
        IElement[] array = [new Element1(), new Element2(), new Element3(), new Element4(), new Element5()];
        var count = 0;
        foreach (var element in array)
        {
            count += element is null ? 0 : 1;
        }

        sink = count;
        return count;
    }

    // S-6 の目標形状のみ (下限)
    // The S-6 target shape alone (the floor).
    [Benchmark]
    public object? InlineArrayOnly()
    {
        IElement[] array = [new Element1(), new Element2(), new Element3(), new Element4(), new Element5()];
        sink = array;
        return array;
    }

    // 列挙コスト単体 (キャッシュ済み配列を IEnumerable<T> 経由で列挙)
    // Enumeration cost alone (a cached array enumerated through IEnumerable<T>).
    [Benchmark]
    public int ForeachOnCachedArray()
    {
        var count = 0;
        foreach (var element in cachedArray)
        {
            count += element is null ? 0 : 1;
        }

        sink = count;
        return count;
    }
}
