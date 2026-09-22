namespace BunnyTail.DependencyInjection.Internal;

using System.Runtime.CompilerServices;

using BunnyTail.DependencyInjection.Accessors;

//--------------------------------------------------------------------------------
// Type table
//--------------------------------------------------------------------------------

// Lock-free reads, single-writer appends. Nodes never change after they are published (Key / Accessor are readonly,
// Next is fixed before the node becomes reachable), so a writer can prepend to a bucket with a volatile store while
// readers walk the chain. The array is rebuilt only when the load factor would exceed 1/2, so appends are amortized O(1).
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

    private int count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Hash(Type key) => (int)(key.TypeHandle.Value >> 4);

    public FixedTypeServiceTable(IReadOnlyList<KeyValuePair<Type, ServiceAccessor>> source)
        : this(CapacityFor(source.Count, 1))
    {
        foreach (var pair in source)
        {
            Append(pair.Key, pair.Value, null);
        }
    }

    // An empty table that can take initialCapacity / 2 entries before its first rebuild
    public FixedTypeServiceTable(int initialCapacity)
    {
        nodes = new Node[CapacityFor(0, initialCapacity)];
        for (var i = 0; i < nodes.Length; i++)
        {
            nodes[i] = EmptyNode;
        }
    }

    private static int CapacityFor(int entries, int minimum)
    {
        var capacity = 1;
        while ((capacity < entries * 2) || (capacity < minimum))
        {
            capacity <<= 1;
        }

        return capacity;
    }

    // Construction only: appends to the tail so that the iteration order matches the source
    private void Append(Type key, ServiceAccessor accessor, object? constant)
    {
        var index = Hash(key) & (nodes.Length - 1);
        var node = new Node(key, accessor) { Constant = constant };
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

        count++;
    }

    // Writer side, under the owner's lock. Adds in place while the load factor stays at or below 1/2 and returns this;
    // otherwise returns a rebuilt table with doubled capacity that the caller publishes in place of this one.
    public FixedTypeServiceTable Add(Type key, ServiceAccessor accessor)
    {
        if ((count + 1) * 2 > nodes.Length)
        {
            var grown = new FixedTypeServiceTable(nodes.Length * 2);
            foreach (var head in nodes)
            {
                for (var node = head; (node is not null) && (node != EmptyNode); node = node.Next)
                {
                    grown.Append(node.Key, node.Accessor, Volatile.Read(ref node.Constant));
                }
            }

            grown.Append(key, accessor, null);
            return grown;
        }

        var index = Hash(key) & (nodes.Length - 1);
        var current = nodes[index];
        var added = new Node(key, accessor) { Next = current == EmptyNode ? null : current };

        // The node's fields are complete before the reference is published; readers reach them through it
        Volatile.Write(ref nodes[index], added);
        count++;
        return this;
    }

    // Snapshot for diagnostics; concurrent appends may or may not be included
    public KeyValuePair<Type, ServiceAccessor>[] ToArray()
    {
        var table = nodes;
        var result = new List<KeyValuePair<Type, ServiceAccessor>>(count);
        foreach (var head in table)
        {
            for (var node = head; (node is not null) && (node != EmptyNode); node = node.Next)
            {
                result.Add(new KeyValuePair<Type, ServiceAccessor>(node.Key, node.Accessor));
            }
        }

        return [.. result];
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

// Same publication rules as FixedTypeServiceTable
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

    private int count;

    public FixedKeyedServiceTable(IReadOnlyList<(Type Type, object Key, ServiceAccessor Accessor)> source)
        : this(CapacityFor(source.Count, 1))
    {
        foreach (var (type, key, accessor) in source)
        {
            Append(type, key, accessor, null);
        }
    }

    public FixedKeyedServiceTable(int initialCapacity)
    {
        nodes = new Node[CapacityFor(0, initialCapacity)];
        for (var i = 0; i < nodes.Length; i++)
        {
            nodes[i] = EmptyNode;
        }
    }

    private static int CapacityFor(int entries, int minimum)
    {
        var capacity = 1;
        while ((capacity < entries * 2) || (capacity < minimum))
        {
            capacity <<= 1;
        }

        return capacity;
    }

    private void Append(Type type, object key, ServiceAccessor accessor, object? constant)
    {
        var node = new Node(type, key, accessor) { Constant = constant };
        var index = node.Hash & (nodes.Length - 1);
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

        count++;
    }

    public FixedKeyedServiceTable Add(Type type, object key, ServiceAccessor accessor)
    {
        if ((count + 1) * 2 > nodes.Length)
        {
            var grown = new FixedKeyedServiceTable(nodes.Length * 2);
            foreach (var head in nodes)
            {
                for (var node = head; (node is not null) && (node != EmptyNode); node = node.Next)
                {
                    grown.Append(node.Type, node.Key, node.Accessor, Volatile.Read(ref node.Constant));
                }
            }

            grown.Append(type, key, accessor, null);
            return grown;
        }

        var added = new Node(type, key, accessor);
        var index = added.Hash & (nodes.Length - 1);
        var current = nodes[index];
        added.Next = current == EmptyNode ? null : current;

        Volatile.Write(ref nodes[index], added);
        count++;
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ServiceAccessor? Get(Type type, object key)
    {
        var hash = key.GetHashCode();
        var table = nodes;
        var node = table[hash & (table.Length - 1)];
        do
        {
            if ((hash == node.Hash) && ReferenceEquals(node.Type, type) && node.Key.Equals(key))
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
            if ((hash == node.Hash) && ReferenceEquals(node.Type, type) && node.Key.Equals(key))
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
