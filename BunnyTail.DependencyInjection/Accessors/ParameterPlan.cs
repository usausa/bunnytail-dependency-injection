namespace BunnyTail.DependencyInjection.Accessors;

internal sealed class ParameterPlan
{
    private readonly ServiceAccessor? accessor;

    private readonly object? constantValue;

    private ParameterPlan(ServiceAccessor? accessor, object? constantValue, bool isServiceKey)
    {
        this.accessor = accessor;
        this.constantValue = constantValue;
        IsServiceKey = isServiceKey;
    }

    public bool IsService => accessor is not null;

    public bool IsServiceKey { get; }

    public static ParameterPlan FromService(ServiceAccessor accessor) => new(accessor, null, false);

    public static ParameterPlan FromConstant(object? value) => new(null, value, false);

    public static ParameterPlan FromServiceKey(object? key) => new(null, key, true);

    public object? Resolve(ServiceProviderScope scope) => accessor is not null ? accessor.GetValue(scope) : constantValue;
}
