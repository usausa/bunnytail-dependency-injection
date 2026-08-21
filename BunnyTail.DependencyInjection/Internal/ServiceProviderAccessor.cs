namespace BunnyTail.DependencyInjection.Internal;

internal sealed class ServiceProviderAccessor : ServiceAccessor
{
    public ServiceProviderAccessor()
        : base(ResultCache.None, -1, trackDisposable: false)
    {
    }

    public override object GetValue(ServiceProviderScope scope) => scope;

    protected override object Create(ServiceProviderScope scope) => scope;
}
