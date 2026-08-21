namespace BunnyTail.DependencyInjection.Accessors;

using System.Runtime.CompilerServices;

using BunnyTail.DependencyInjection.Internal;

internal sealed class DependencyFactoryAccessor : ServiceAccessor
{
    public Func<IServiceProvider, object?[], object> Factory { get; }

    private readonly ServiceAccessor[] dependencyAccessors;

    private readonly DependencyAccessor?[] dependencyHandles;

    private object?[]? resolved;

    public DependencyFactoryAccessor(Func<IServiceProvider, object?[], object> factory, ServiceAccessor[] dependencyAccessors, DependencyAccessor?[] dependencyHandles, ResultCache cache, int slot, bool trackDisposable)
        : base(cache, slot, trackDisposable)
    {
        Factory = factory;
        this.dependencyAccessors = dependencyAccessors;
        this.dependencyHandles = dependencyHandles;
    }

    protected override object Create(ServiceProviderScope scope)
    {
        var dependencies = resolved ?? FillDependencies(scope);
        return Factory(scope, dependencies);
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
