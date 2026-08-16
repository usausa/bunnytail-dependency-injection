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

        // Root 解決済みインスタンス (WrapSlotValue 済み)。初回 Root 解決時に書き戻され、以後は accessor を経由しない
        // Resolved Root instance (already wrapped). Back-filled on the first Root resolution; later reads skip the accessor.
        public object? Constant;
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

    // 解決のホット経路。Root 解決済みは定数読みだけで返す (accessor の仮想呼び出しなし)。
    // Root の初回解決時にノードへ書き戻す。COW 再構築で定数は失われるが、次の解決で再充填される
    // Hot resolution path: resolved Root entries return with a constant read alone (no virtual accessor call).
    // Back-filled on the first Root resolution; constants lost by a COW rebuild are refilled on the next resolution.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryResolve(Type key, ServiceProviderScope scope, out object? value)
    {
        var node = nodes[RuntimeHelpers.GetHashCode(key) & mask];
        do
        {
            if (ReferenceEquals(node.Key, key))
            {
                var constant = node.Constant;
                if (constant is not null)
                {
                    value = ServiceProviderScope.UnwrapSlotValue(constant);
                    return true;
                }

                var accessor = node.Accessor;
                value = accessor.GetValue(scope);
                if (accessor.Cache == ResultCache.Root)
                {
                    Volatile.Write(ref node.Constant, ServiceProviderScope.WrapSlotValue(value));
                }

                return true;
            }

            node = node.Next;
        }
        while (node is not null);

        value = null;
        return false;
    }
}

// keyed 用。1 テーブル方式は Sandbox の KeyedLookupBenchmark で確定。バケット選択は key ハッシュのみで行い、
// 型は連結内の参照比較で棄却する (type の identity hash 呼び出しをホット経路から除去)。
// ノードに key ハッシュをキャッシュし、Equals の前に int 比較で棄却する
// Keyed variant. The single-table design was settled by KeyedLookupBenchmark in the sandbox. Buckets are selected by
// the key hash alone and types are rejected by reference comparison inside the chain (removing the identity-hash call
// for the type from the hot path). Nodes cache the key hash so mismatches are rejected by an int compare before Equals.
internal sealed class FixedKeyedServiceTable
{
#pragma warning disable CA1812
    private sealed class EmptySentinel;
#pragma warning restore CA1812

#pragma warning disable SA1401
    private sealed class Node
    {
        public readonly int Hash;
        public readonly Type Type;
        public readonly object Key;
        public readonly ServiceAccessor Accessor;

        // Root 解決済みインスタンス (WrapSlotValue 済み)
        // Resolved Root instance (already wrapped).
        public object? Constant;
        public Node? Next;

        public Node(Type type, object key, ServiceAccessor accessor)
        {
            Hash = key.GetHashCode();
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
            var node = new Node(type, key, accessor);
            var index = node.Hash & mask;
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
    public ServiceAccessor? Get(Type type, object key)
    {
        var hash = key.GetHashCode();
        var node = nodes[hash & mask];
        do
        {
            if (hash == node.Hash && ReferenceEquals(node.Type, type) && node.Key.Equals(key))
            {
                return node.Accessor;
            }

            node = node.Next;
        }
        while (node is not null);

        return null;
    }

    // 解決のホット経路 (非 keyed 側と同形。詳細は FixedTypeServiceTable.TryResolve を参照)
    // Hot resolution path (same shape as the non-keyed table; see FixedTypeServiceTable.TryResolve).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryResolve(Type type, object key, ServiceProviderScope scope, out object? value)
    {
        var hash = key.GetHashCode();
        var node = nodes[hash & mask];
        do
        {
            if (hash == node.Hash && ReferenceEquals(node.Type, type) && node.Key.Equals(key))
            {
                var constant = node.Constant;
                if (constant is not null)
                {
                    value = ServiceProviderScope.UnwrapSlotValue(constant);
                    return true;
                }

                var accessor = node.Accessor;
                value = accessor.GetValue(scope);
                if (accessor.Cache == ResultCache.Root)
                {
                    Volatile.Write(ref node.Constant, ServiceProviderScope.WrapSlotValue(value));
                }

                return true;
            }

            node = node.Next;
        }
        while (node is not null);

        value = null;
        return false;
    }
}
