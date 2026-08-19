namespace BunnyTail.DependencyInjection.Sandbox;

using System.Runtime.CompilerServices;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;

using BunnyTail.DependencyInjection.Sandbox.Infrastructure;

// Configuration with disassembly, for JIT level investigation.
// Configuration with disassembly, for JIT-level investigation.
public sealed class DisasmConfig : ManualConfig
{
    public DisasmConfig()
    {
        _ = AddExporter(MarkdownExporter.GitHub);
        _ = AddColumn(StatisticColumn.Mean, StatisticColumn.Min, StatisticColumn.Max, StatisticColumn.Error, StatisticColumn.StdDev);
        _ = AddDiagnoser(MemoryDiagnoser.Default);
        _ = AddDiagnoser(new DisassemblyDiagnoser(new DisassemblyDiagnoserConfig(maxDepth: 3, exportDiff: true)));
        _ = AddJob(Job.MediumRun.WithJit(Jit.RyuJit).WithPlatform(Platform.X64));
    }
}

// Comparison of resolution entry path shapes.
// The current path is a table lookup, a virtual call on the accessor, a cache branch, a field read and a sentinel
// unwrap, whereas Smart.Resolver settles for a table lookup and a constant field falling back to a delegate.
// This checks how much the constant short circuit gains and whether the virtual call and the branch really disappear in the disassembly.
// Comparison of resolution entry path shapes. The current shape is table lookup, virtual accessor call, cache branch,
// field read and sentinel unwrap, whereas Smart.Resolver stops at a table lookup plus a Constant field with a delegate
// fallback. This measures how much the constant short-circuit gains and confirms in the disassembly that the virtual
// call and the branches actually disappear.
[Config(typeof(DisasmConfig))]
public class ResolutionEntryBenchmark
{
    private const int LookupCount = 1024;

    private static readonly object NullSentinel = new();

    public enum CacheKind
    {
        None,
        Root,
        Scoped
    }

    // Accessor that mimics the current runtime.
    // Accessor modelled on the current runtime.
    public abstract class Accessor
    {
        public readonly CacheKind Cache;

        private object? rootCached;

        protected Accessor(CacheKind cache)
        {
            Cache = cache;
        }

        public object? GetValue(object scope)
        {
            if (Cache == CacheKind.None)
            {
                return Create(scope);
            }

            if (Cache == CacheKind.Root)
            {
                var cached = rootCached;
                return cached is not null ? Unwrap(cached) : CreateRoot(scope);
            }

            return Create(scope);
        }

        public void Prime(object scope) => rootCached = GetValue(scope) ?? NullSentinel;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private object CreateRoot(object scope)
        {
            var value = Create(scope);
            Volatile.Write(ref rootCached, value);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static object? Unwrap(object value) => ReferenceEquals(value, NullSentinel) ? null : value;

        protected abstract object Create(object scope);
    }

    public sealed class SingletonAccessor : Accessor
    {
        private readonly object instance;

        public SingletonAccessor(object instance)
            : base(CacheKind.Root)
        {
            this.instance = instance;
        }

        protected override object Create(object scope) => instance;
    }

    // Current shape: the node holds only the accessor.
    // Current shape: the node holds only the accessor.
    public sealed class AccessorTable
    {
        private sealed class EmptySentinel;

        private sealed class Node
        {
            public readonly Type Key;
            public readonly Accessor Accessor;
            public Node? Next;

            public Node(Type key, Accessor accessor)
            {
                Key = key;
                Accessor = accessor;
            }
        }

        private static readonly Node EmptyNode = new(typeof(EmptySentinel), null!);

        private readonly Node[] nodes;
        private readonly int mask;

        public AccessorTable(IReadOnlyList<KeyValuePair<Type, Accessor>> source)
        {
            var capacity = 1;
            while (capacity < source.Count * 2)
            {
                capacity <<= 1;
            }

            mask = capacity - 1;
            nodes = new Node[capacity];
            for (var i = 0; i < nodes.Length; i++)
            {
                nodes[i] = EmptyNode;
            }

            foreach (var pair in source)
            {
                var index = RuntimeHelpers.GetHashCode(pair.Key) & mask;
                var node = new Node(pair.Key, pair.Value);
                if (nodes[index] == EmptyNode)
                {
                    nodes[index] = node;
                }
                else
                {
                    var last = nodes[index];
                    while (last.Next is not null)
                    {
                        last = last.Next;
                    }

                    last.Next = node;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object? Resolve(Type key, object scope)
        {
            var node = nodes[RuntimeHelpers.GetHashCode(key) & mask];
            do
            {
                if (ReferenceEquals(node.Key, key))
                {
                    return node.Accessor.GetValue(scope);
                }

                node = node.Next;
            }
            while (node is not null);

            return null;
        }
    }

    // Constant short circuit shape: the node holds the resolved instance directly.
    // Constant short-circuit shape: the node holds the resolved instance directly.
    public sealed class ConstantTable
    {
        private sealed class EmptySentinel;

        private sealed class Node
        {
            public readonly Type Key;
            public readonly Accessor Accessor;
            public readonly object? Constant;
            public Node? Next;

            public Node(Type key, Accessor accessor, object? constant)
            {
                Key = key;
                Accessor = accessor;
                Constant = constant;
            }
        }

        private static readonly Node EmptyNode = new(typeof(EmptySentinel), null!, null);

        private readonly Node[] nodes;
        private readonly int mask;

        public ConstantTable(IReadOnlyList<KeyValuePair<Type, Accessor>> source, bool primeConstants)
        {
            var capacity = 1;
            while (capacity < source.Count * 2)
            {
                capacity <<= 1;
            }

            mask = capacity - 1;
            nodes = new Node[capacity];
            for (var i = 0; i < nodes.Length; i++)
            {
                nodes[i] = EmptyNode;
            }

            foreach (var pair in source)
            {
                var index = RuntimeHelpers.GetHashCode(pair.Key) & mask;
                var constant = primeConstants && pair.Value.Cache == CacheKind.Root ? pair.Value.GetValue(this) : null;
                var node = new Node(pair.Key, pair.Value, constant);
                if (nodes[index] == EmptyNode)
                {
                    nodes[index] = node;
                }
                else
                {
                    var last = nodes[index];
                    while (last.Next is not null)
                    {
                        last = last.Next;
                    }

                    last.Next = node;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object? Resolve(Type key, object scope)
        {
            var node = nodes[RuntimeHelpers.GetHashCode(key) & mask];
            do
            {
                if (ReferenceEquals(node.Key, key))
                {
                    return node.Constant ?? node.Accessor.GetValue(scope);
                }

                node = node.Next;
            }
            while (node is not null);

            return null;
        }
    }

    [Params(8, 64)]
    public int N { get; set; }

    private AccessorTable accessorTable = default!;
    private ConstantTable constantTable = default!;
    private Type[] sequence = default!;
    private readonly object scope = new();

    // Sink for the measured results. It is never read, but writing to it prevents JIT dead code elimination.
    // Sink for measured values: never read, but written so the JIT cannot eliminate the work.
    // ReSharper disable once NotAccessedField.Local
    private object? sink;

    [GlobalSetup]
    public void Setup()
    {
        var pairs = new List<KeyValuePair<Type, Accessor>>(N);
        for (var i = 0; i < N; i++)
        {
            pairs.Add(new KeyValuePair<Type, Accessor>(KeyTypes.All[i], new SingletonAccessor(new object())));
        }

        accessorTable = new AccessorTable(pairs);
        constantTable = new ConstantTable(pairs, primeConstants: true);

        // A singleton on the current shape fills its field on first resolution, so the hot read starts from the same state.
        // The current shape fills its field on first resolution, so prime it to match the hot-read condition.
        foreach (var pair in pairs)
        {
            pair.Value.Prime(scope);
        }

        var random = new Random(12345);
        sequence = new Type[LookupCount];
        for (var i = 0; i < LookupCount; i++)
        {
            sequence[i] = KeyTypes.All[random.Next(N)];
        }
    }

    // Current shape: table lookup, virtual call, cache branch, field read, sentinel unwrap.
    // Current shape: table lookup, virtual call, cache branch, field read, sentinel unwrap.
    [Benchmark(Baseline = true, OperationsPerInvoke = LookupCount)]
    public object? ViaAccessor()
    {
        object? last = null;
        var keys = sequence;
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < keys.Length; i++)
        {
            last = accessorTable.Resolve(keys[i], scope);
        }

        sink = last;
        return last;
    }

    // Constant short circuit shape: table lookup then a constant field read, with no virtual call.
    // Constant short-circuit shape: table lookup then a Constant field read, with no virtual call.
    [Benchmark(OperationsPerInvoke = LookupCount)]
    public object? ViaConstant()
    {
        object? last = null;
        var keys = sequence;
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < keys.Length; i++)
        {
            last = constantTable.Resolve(keys[i], scope);
        }

        sink = last;
        return last;
    }
}
