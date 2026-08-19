namespace BunnyTail.DependencyInjection.Sandbox.Infrastructure;

using System.Runtime.CompilerServices;

// Composite (Type, key) key for keyed services, used for the Dictionary baseline.
// Composite (Type, key) for keyed services. Used by the Dictionary baseline.
public readonly struct CompositeKey : IEquatable<CompositeKey>
{
    public readonly Type Type;
    public readonly object Key;

    public CompositeKey(Type type, object key)
    {
        Type = type;
        Key = key;
    }

    public bool Equals(CompositeKey other) => ReferenceEquals(Type, other.Type) && Key.Equals(other.Key);

    public override bool Equals(object? obj) => obj is CompositeKey other && Equals(other);

    public override int GetHashCode() => (RuntimeHelpers.GetHashCode(Type) * 397) ^ Key.GetHashCode();
}

// Shipping shape: inline node list, a head Node per bucket linked through Next with a sentinel, plus a composite hash.
// Same layout as FixedKeyedServiceTable in BunnyTail.DependencyInjection.
// Shipped shape: nodes stored directly in buckets (head node + Next chain + sentinel) with a composite hash.
// Same layout as FixedKeyedServiceTable in BunnyTail.DependencyInjection.
public sealed class NodeCompositeTable<TValue>
{
    private sealed class EmptySentinel;

    private sealed class Node
    {
        public readonly Type Type;
        public readonly object Key;
        public readonly TValue Value;
        public Node? Next;

        public Node(Type type, object key, TValue value)
        {
            Type = type;
            Key = key;
            Value = value;
        }
    }

    private static readonly Node EmptyNode = new(typeof(EmptySentinel), string.Empty, default!);

    private readonly Node[] nodes;
    private readonly int mask;

    public NodeCompositeTable(IReadOnlyList<KeyValuePair<CompositeKey, TValue>> source)
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
            var index = Hash(pair.Key.Type, pair.Key.Key) & mask;
            var node = new Node(pair.Key.Type, pair.Key.Key, pair.Value);
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
    private static int Hash(Type type, object key) => (RuntimeHelpers.GetHashCode(type) * 397) ^ key.GetHashCode();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(Type type, object key, out TValue value)
    {
        var node = nodes[Hash(type, key) & mask];
        do
        {
            if (ReferenceEquals(node.Type, type) && node.Key.Equals(key))
            {
                value = node.Value;
                return true;
            }

            node = node.Next;
        }
        while (node is not null);

        value = default!;
        return false;
    }
}

// Control: small Entry[] array buckets plus a composite hash, the layout that lost to the node list on the non keyed side.
// Control: Entry[] bucket arrays with a composite hash. The layout that lost to node lists on the non-keyed side.
public sealed class BucketCompositeTable<TValue>
{
    private readonly struct Entry
    {
        public readonly Type Type;
        public readonly object Key;
        public readonly TValue Value;

        public Entry(Type type, object key, TValue value)
        {
            Type = type;
            Key = key;
            Value = value;
        }
    }

    private readonly Entry[]?[] buckets;
    private readonly int mask;

    public BucketCompositeTable(IReadOnlyList<KeyValuePair<CompositeKey, TValue>> source)
    {
        var capacity = 1;
        while (capacity < source.Count * 2)
        {
            capacity <<= 1;
        }

        mask = capacity - 1;

        var lists = new List<Entry>?[capacity];
        foreach (var pair in source)
        {
            var index = Hash(pair.Key.Type, pair.Key.Key) & mask;
            (lists[index] ??= []).Add(new Entry(pair.Key.Type, pair.Key.Key, pair.Value));
        }

        buckets = new Entry[]?[capacity];
        for (var i = 0; i < capacity; i++)
        {
            buckets[i] = lists[i]?.ToArray();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Hash(Type type, object key) => (RuntimeHelpers.GetHashCode(type) * 397) ^ key.GetHashCode();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(Type type, object key, out TValue value)
    {
        var bucket = buckets[Hash(type, key) & mask];
        if (bucket is not null)
        {
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < bucket.Length; i++)
            {
                ref readonly var entry = ref bucket[i];
                if (ReferenceEquals(entry.Type, type) && entry.Key.Equals(key))
                {
                    value = entry.Value;
                    return true;
                }
            }
        }

        value = default!;
        return false;
    }
}

// Control: a two level structure of a Type table over key subtables, not adopted.
// Control: two-stage Type table into per-key sub-tables (rejected).
public sealed class TwoStageKeyedTable<TValue>
{
    private readonly Dictionary<Type, Dictionary<object, TValue>> table;

    // ReSharper disable once ParameterTypeCanBeEnumerable.Local
    public TwoStageKeyedTable(IReadOnlyList<KeyValuePair<CompositeKey, TValue>> source)
    {
        table = [];
        foreach (var pair in source)
        {
            if (!table.TryGetValue(pair.Key.Type, out var sub))
            {
                sub = [];
                table[pair.Key.Type] = sub;
            }

            sub[pair.Key.Key] = pair.Value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(Type type, object key, out TValue value)
    {
        if (table.TryGetValue(type, out var sub) && sub.TryGetValue(key, out value!))
        {
            return true;
        }

        value = default!;
        return false;
    }
}
