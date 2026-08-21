namespace BunnyTail.DependencyInjection.Internal;

internal sealed class KeyedFactoryAccessor : ServiceAccessor
{
    private readonly Func<IServiceProvider, object?, object> factory;

    private readonly object? key;

    public KeyedFactoryAccessor(Func<IServiceProvider, object?, object> factory, object? key, ResultCache cache, int slot, bool trackDisposable)
        : base(cache, slot, trackDisposable)
    {
        this.factory = factory;
        this.key = key;
    }

    protected override object Create(ServiceProviderScope scope) => factory(scope, key);
}
