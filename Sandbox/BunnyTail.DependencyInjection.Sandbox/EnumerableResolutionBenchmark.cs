namespace BunnyTail.DependencyInjection.Sandbox;

using BenchmarkDotNet.Attributes;

// Breakdown of MultipleTransient, measured at 99.6ns against 32.7ns for MEDI.
// The current materialization is a virtual call on the enumerable accessor, then typed array creation, then two
// levels of accessor virtual calls plus a delegate invocation per element, and the benchmark foreach is included.
// This identifies the dominant layer and estimates how much S-6, a generated enumerable factory written as an array literal, can recover.
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

    // Model of the current runtime accessor chain: virtual GetValue, virtual Create, delegate, then new.
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

        protected override object Create(object scope) => factory(scope);
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

        protected override object Create(object scope)
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

    // Current shape plus enumeration, the same shape as MultipleTransient in the published benchmark.
    // Current shape plus enumeration (same shape as MultipleTransient in the external benchmark).
    [Benchmark(Baseline = true)]
    public int CurrentShapeWithForeach()
    {
        var count = 0;
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var element in (IEnumerable<IElement>)enumerableAccessor.GetValue(scope)!)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            count += element is null ? 0 : 1;
        }

        sink = count;
        return count;
    }

    // Current shape only, with the enumeration cost removed.
    // Current shape only, without enumeration.
    [Benchmark]
    public object? CurrentShapeOnly()
    {
        sink = enumerableAccessor.GetValue(scope);
        return sink;
    }

    // Target shape of S-6: an array literal plus enumeration. Enumeration always goes through the interface to match
    // real usage, because dropping the cast would turn it into array enumeration and compare something else.
    // The S-6 target shape: a literal array expression plus enumeration. It always enumerates through the interface to
    // match how consumers actually resolve (dropping the cast would turn it into array enumeration, changing what is compared).
    [Benchmark]
    public int InlineArrayWithForeach()
    {
        IElement[] array = [new Element1(), new Element2(), new Element3(), new Element4(), new Element5()];
        var count = 0;
        // ReSharper disable once RedundantCast
        // ReSharper disable once LoopCanBeConvertedToQuery
#pragma warning disable IDE0004
        foreach (var element in (IEnumerable<IElement>)array)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            count += element is null ? 0 : 1;
        }
#pragma warning restore IDE0004

        sink = count;
        return count;
    }

    // Target shape of S-6 only, the lower bound.
    // The S-6 target shape alone (the floor).
    [Benchmark]
    public object InlineArrayOnly()
    {
        IElement[] array = [new Element1(), new Element2(), new Element3(), new Element4(), new Element5()];
        sink = array;
        return array;
    }

    // Enumeration cost alone, enumerating a cached array through IEnumerable<T>. The cast is what is being measured.
    // Enumeration cost alone (a cached array enumerated through IEnumerable<T>); the cast is what is being measured.
    [Benchmark]
    public int ForeachOnCachedArray()
    {
        var count = 0;
        // ReSharper disable once RedundantCast
        // ReSharper disable once LoopCanBeConvertedToQuery
#pragma warning disable IDE0004
        foreach (var element in (IEnumerable<IElement>)cachedArray)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            count += element is null ? 0 : 1;
        }
#pragma warning restore IDE0004

        sink = count;
        return count;
    }
}
