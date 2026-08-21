namespace BunnyTail.DependencyInjection;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

public sealed class DependencyAccessor
{
    private readonly ServiceAccessor accessor;

    private readonly Type serviceType;

    internal DependencyAccessor(ServiceAccessor accessor, Type serviceType)
    {
        this.accessor = accessor;
        this.serviceType = serviceType;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetValue<T>(ServiceProviderScope scope)
        where T : class
    {
        var value = accessor.GetValue(scope);
        if (value is null)
        {
            ThrowNoService(serviceType);
        }

        return Unsafe.As<T>(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object GetValue(ServiceProviderScope scope)
    {
        var value = accessor.GetValue(scope);
        if (value is null)
        {
            ThrowNoService(serviceType);
        }

        return value;
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNoService(Type serviceType) =>
        throw new InvalidOperationException($"No service for type '{serviceType}' has been registered.");
}
