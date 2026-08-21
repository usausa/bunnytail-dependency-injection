namespace BunnyTail.DependencyInjection.Internal;

internal sealed class ConstantAccessor : ServiceAccessor
{
    private readonly object? value;

    public ConstantAccessor(object? value)
        : base(ResultCache.None, -1, trackDisposable: false)
    {
        this.value = value;
    }

    public override object? GetValue(ServiceProviderScope scope) => value;

    protected override object? Create(ServiceProviderScope scope) => value;
}
