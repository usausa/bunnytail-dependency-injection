namespace BunnyTail.DependencyInjection.Sandbox;

using System.Reflection;
using System.Runtime.CompilerServices;

using BenchmarkDotNet.Attributes;

// Comparison of storage strategies for IEnumerable<T> materialization.
// DI specific constraint: the element type is known only at runtime as a Type value, yet the returned array must
// be a real T[], and transient elements cannot be cached, so the array is rebuilt on every resolution.
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

    // Sink for the measured results. It is never read, but writing to it prevents JIT dead code elimination.
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

        // The length is fixed per accessor, so one instance can be built up front.
        // The length is fixed per accessor, so a single instance can be built up front.
        prototype = Array.CreateInstance(elementType, N);

        typedFactory = (Func<int, Array>)typeof(EnumerableMaterializationBenchmark)
            .GetMethod(nameof(CreateTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(elementType)
            .CreateDelegate(typeof(Func<int, Array>));
    }

    private static Array CreateTyped<T>(int length) => new T[length];

    // Current implementation: Array.CreateInstance plus Array.SetValue, both on the reflection path.
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

    // Candidate: for reference type elements a T[] is covariantly visible as object[], so plain array writes can store them
    // and the write only pays the stelem.ref covariance check. It cannot be applied to value type elements.
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

    // Upper bound reference: the element type known at compile time, the ideal shape the generated path can reach.
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

    // Candidate: hold a fixed length prototype and clone it on every resolution, avoiding Array.CreateInstance.
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

    // Candidate: build and cache a delegate that creates the typed array once.
    // It uses MakeGenericMethod, so value type elements are unavailable on NativeAOT; reference types run through shared generics.
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

    // Cost breakdown: creation only, without storing the elements.
    // Cost attribution: allocation only, no stores.
    [Benchmark]
    public object CreateOnly()
    {
        var array = Array.CreateInstance(elementType, factories.Length);
        sink = array;
        return array;
    }
}
