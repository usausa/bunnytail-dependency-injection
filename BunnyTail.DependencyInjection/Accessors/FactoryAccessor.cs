namespace BunnyTail.DependencyInjection.Accessors;

internal sealed class FactoryAccessor : ServiceAccessor
{
    public Func<IServiceProvider, object> Factory { get; }

    public FactoryAccessor(Func<IServiceProvider, object> factory, ResultCache cache, int slot, bool trackDisposable)
        : base(cache, slot, trackDisposable)
    {
        Factory = factory;
    }

    protected override object Create(ServiceProviderScope scope) => Factory(scope);
}
