namespace BunnyTail.DependencyInjection.Internal;

using System.Runtime.CompilerServices;

using BunnyTail.DependencyInjection.Accessors;

//--------------------------------------------------------------------------------
// Type table
//--------------------------------------------------------------------------------

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Hash(Type key) => (int)(key.TypeHandle.Value >> 4);

    public FixedTypeServiceTable(IReadOnlyList<KeyValuePair<Type, ServiceAccessor>> source)
    {
        var capacity = 1;
        while (capacity < source.Count * 2)
        {
            capacity <<= 1;
        }

        var mask = capacity - 1;
        nodes = new Node[capacity];
        for (var i = 0; i < nodes.Length; i++)
        {
            nodes[i] = EmptyNode;
        }

        foreach (var pair in source)
        {
            var index = Hash(pair.Key) & mask;
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
        var table = nodes;
        var node = table[Hash(key) & (table.Length - 1)];
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryResolve(Type key, ServiceProviderScope scope, out object? value)
    {
        var table = nodes;
        var node = table[Hash(key) & (table.Length - 1)];
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

//--------------------------------------------------------------------------------
// Keyed table
//--------------------------------------------------------------------------------

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

    public FixedKeyedServiceTable(IReadOnlyList<(Type Type, object Key, ServiceAccessor Accessor)> source)
    {
        var capacity = 1;
        while (capacity < source.Count * 2)
        {
            capacity <<= 1;
        }

        var mask = capacity - 1;
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
        var table = nodes;
        var node = table[hash & (table.Length - 1)];
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryResolve(Type type, object key, ServiceProviderScope scope, out object? value)
    {
        var hash = key.GetHashCode();
        var table = nodes;
        var node = table[hash & (table.Length - 1)];
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
