namespace BunnyTail.DependencyInjection.Accessors;

using System.Diagnostics.CodeAnalysis;

internal sealed class ValueTypeAccessor : ServiceAccessor
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    private readonly Type type;

    private readonly bool initializable;

    public ValueTypeAccessor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type type,
        bool initializable,
        ResultCache cache,
        int slot,
        bool trackDisposable)
        : base(cache, slot, trackDisposable)
    {
        this.type = type;
        this.initializable = initializable;
    }

    protected override object? Create(ServiceProviderScope scope)
    {
        var value = Activator.CreateInstance(type);
        if (initializable)
        {
            ((IInitializable)value!).Initialize();
        }

        return value;
    }
}
