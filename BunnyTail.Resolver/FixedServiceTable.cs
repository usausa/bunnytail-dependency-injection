namespace BunnyTail.Resolver;

using System.Runtime.CompilerServices;

// 主テーブルの形状: ノードリスト直置き (バケット先頭 Node + Next 連結 + センチネル) + identity hash + 容量 2^n。
// 構築後イミュータブルなので resolve 経路に同期は存在しない。非 keyed 用 (Type キー)。
// 形状の実測根拠は dotnet-performance の候補検証 (TypeIdentityHashBenchmark / NodeTypeHashMap) と TYP-01 を参照
// Main table shape: nodes stored directly in buckets (head node + Next chain + empty sentinel), identity hash and
// power-of-two capacity. Immutable once built, so the resolve path needs no synchronization. Non-keyed (Type key) variant.
// See the dotnet-performance candidate verification (TypeIdentityHashBenchmark / NodeTypeHashMap) and TYP-01 for the measurements behind this shape.
internal sealed class FixedTypeServiceTable
{
#pragma warning disable CA1812
    private sealed class EmptySentinel;
#pragma warning restore CA1812

#pragma warning disable SA1401
    private sealed class Node
    {
        public readonly Type Key;
        public readonly ServiceAccessor Accessor;
        public Node? Next;

        public Node(Type key, ServiceAccessor accessor)
        {
            Key = key;
            Accessor = accessor;
        }
    }
#pragma warning restore SA1401

    private static readonly Node EmptyNode = new(typeof(EmptySentinel), null!);

    private readonly Node[] nodes;
    private readonly int mask;

    public FixedTypeServiceTable(IReadOnlyList<KeyValuePair<Type, ServiceAccessor>> source)
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
    public ServiceAccessor? Get(Type key)
    {
        var node = nodes[RuntimeHelpers.GetHashCode(key) & mask];
        do
        {
            if (ReferenceEquals(node.Key, key))
            {
                return node.Accessor;
            }

            node = node.Next;
        }
        while (node is not null);

        return null;
    }
}

// keyed 用。(Type, key) の複合ハッシュ 1 テーブル (Sandbox の KeyedLookupBenchmark で確定)
// Keyed variant. Single table with a composite (Type, key) hash, settled by KeyedLookupBenchmark in the sandbox.
internal sealed class FixedKeyedServiceTable
{
#pragma warning disable CA1812
    private sealed class EmptySentinel;
#pragma warning restore CA1812

#pragma warning disable SA1401
    private sealed class Node
    {
        public readonly Type Type;
        public readonly object Key;
        public readonly ServiceAccessor Accessor;
        public Node? Next;

        public Node(Type type, object key, ServiceAccessor accessor)
        {
            Type = type;
            Key = key;
            Accessor = accessor;
        }
    }
#pragma warning restore SA1401

    private static readonly Node EmptyNode = new(typeof(EmptySentinel), string.Empty, null!);

    private readonly Node[] nodes;
    private readonly int mask;

    public FixedKeyedServiceTable(IReadOnlyList<(Type Type, object Key, ServiceAccessor Accessor)> source)
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

        foreach (var (type, key, accessor) in source)
        {
            var index = Hash(type, key) & mask;
            var node = new Node(type, key, accessor);
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
    public ServiceAccessor? Get(Type type, object key)
    {
        var node = nodes[Hash(type, key) & mask];
        do
        {
            if (ReferenceEquals(node.Type, type) && node.Key.Equals(key))
            {
                return node.Accessor;
            }

            node = node.Next;
        }
        while (node is not null);

        return null;
    }
}
