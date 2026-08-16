namespace BunnyTail.Resolver.Sandbox;

using BenchmarkDotNet.Attributes;

using BunnyTail.Resolver.Sandbox.Infrastructure;

// 解決した IEnumerable<T> を「どう消費するか」のコスト差。DI 固有の論点は、コンテナが T[] を実体化して返す
// (MEDI 互換) のに、利用側がインタフェース越しに列挙すると enumerator 確保とディスパッチが乗る点にある。
// JIT では escape analysis がこれを消すことがあるが、NativeAOT では消えないため差が表面化する
// Cost difference of how a resolved IEnumerable<T> is consumed. The DI specific point is that the container
// materializes and returns a T[] (MEDI compatible), yet enumerating through the interface adds an enumerator
// allocation and interface dispatch. The JIT can erase that with escape analysis, NativeAOT cannot, so the gap shows.
[Config(typeof(BenchmarkConfig))]
public class EnumerableConsumptionBenchmark
{
    private object resolved = default!;

    [Params(2, 5, 10)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var array = new IElement[N];
        for (var i = 0; i < N; i++)
        {
            array[i] = new Element();
        }

        // 解決結果は object として保持する (実行時の型は T[]) / the resolution result is held as object while the runtime type is T[]
        resolved = array;
    }

    // 利用側がインタフェース越しに列挙する形 (素直な書き方)
    // The straightforward shape where the consumer enumerates through the interface.
    [Benchmark(Baseline = true)]
    public int EnumerateInterface()
    {
        var count = 0;
        foreach (var element in (IEnumerable<IElement>)resolved)
        {
            count += element.Value;
        }

        return count;
    }

    // T[] へキャストして配列として列挙する形 (enumerator 確保なし)
    // Casting to T[] and enumerating the array, without allocating an enumerator.
    [Benchmark]
    public int EnumerateArray()
    {
        var count = 0;
        if (resolved is IElement[] array)
        {
            foreach (var element in array)
            {
                count += element.Value;
            }
        }

        return count;
    }

    public interface IElement
    {
        int Value { get; }
    }

    private sealed class Element : IElement
    {
        public int Value => 1;
    }
}
