namespace BunnyTail.DependencyInjection.Accessors;

using System.Runtime.CompilerServices;

internal sealed class KeyedDependencyFactoryAccessor : ServiceAccessor
{
    private readonly Func<IServiceProvider, object?, object?[], object> factory;

    private readonly object? key;

    private readonly ServiceAccessor[] dependencyAccessors;

    private readonly DependencyAccessor?[] dependencyHandles;

    private object?[]? resolved;

    public KeyedDependencyFactoryAccessor(Func<IServiceProvider, object?, object?[], object> factory, object? key, ServiceAccessor[] dependencyAccessors, DependencyAccessor?[] dependencyHandles, ResultCache cache, int slot, bool trackDisposable)
        : base(cache, slot, trackDisposable)
    {
        this.factory = factory;
        this.key = key;
        this.dependencyAccessors = dependencyAccessors;
        this.dependencyHandles = dependencyHandles;
    }

    protected override object Create(ServiceProviderScope scope)
    {
        var dependencies = resolved ?? FillDependencies(scope);
        return factory(scope, key, dependencies);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private object?[] FillDependencies(ServiceProviderScope scope)
    {
        var array = new object?[dependencyAccessors.Length];
        for (var i = 0; i < dependencyAccessors.Length; i++)
        {
            array[i] = dependencyHandles[i] ?? dependencyAccessors[i].GetValue(scope.RootScope);
        }

        Volatile.Write(ref resolved, array);
        return array;
    }
}
