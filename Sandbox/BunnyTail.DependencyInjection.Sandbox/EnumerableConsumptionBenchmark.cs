namespace BunnyTail.DependencyInjection.Sandbox;

using BenchmarkDotNet.Attributes;

// Cost difference in how a resolved IEnumerable<T> is consumed. The DI specific point is that the container
// materializes and returns a T[] for MEDI compatibility, yet enumerating it through the interface adds an
// enumerator allocation and per element dispatch. Escape analysis can remove that on the JIT but not on NativeAOT, where the difference surfaces.
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

        // the resolution result is held as object while the runtime type is T[]
        resolved = array;
    }

    // Enumerating through the interface, the straightforward way to write it.
    // The straightforward shape where the consumer enumerates through the interface.
    [Benchmark(Baseline = true)]
    public int EnumerateInterface()
    {
        var count = 0;
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var element in (IEnumerable<IElement>)resolved)
        {
            count += element.Value;
        }

        return count;
    }

    // Casting to T[] and enumerating as an array, without an enumerator allocation.
    // Casting to T[] and enumerating the array, without allocating an enumerator.
    [Benchmark]
    public int EnumerateArray()
    {
        var count = 0;
        if (resolved is IElement[] array)
        {
            // ReSharper disable once LoopCanBeConvertedToQuery
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
