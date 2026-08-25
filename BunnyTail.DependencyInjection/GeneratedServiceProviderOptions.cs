namespace BunnyTail.DependencyInjection;

public sealed class GeneratedServiceProviderOptions
{
    public bool TrackTransientDisposables { get; set; } = true;

    internal Dictionary<Type, DisposableTracking>? TrackingOverrides { get; private set; }

    public GeneratedServiceProviderOptions EnableTracking(Type type) => SetTracking(type, DisposableTracking.Enabled);

    public GeneratedServiceProviderOptions DisableTracking(Type type) => SetTracking(type, DisposableTracking.Disabled);

    private GeneratedServiceProviderOptions SetTracking(Type type, DisposableTracking tracking)
    {
        (TrackingOverrides ??= [])[type] = tracking;
        return this;
    }
}
