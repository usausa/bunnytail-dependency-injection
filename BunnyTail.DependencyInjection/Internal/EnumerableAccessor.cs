namespace BunnyTail.DependencyInjection.Internal;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

internal sealed class EnumerableAccessor : ServiceAccessor
{
    private static readonly MethodInfo CreateTypedArrayMethod = new Func<int, Array>(CreateTypedArray<object>).Method.GetGenericMethodDefinition();

    private readonly Type elementType;

    private readonly ServiceAccessor[] items;

    private readonly Func<int, Array>? arrayFactory;

    public EnumerableAccessor(Type elementType, ServiceAccessor[] items, ResultCache cache, int slot)
        : base(cache, slot, trackDisposable: false)
    {
        this.elementType = elementType;
        this.items = items;
        arrayFactory = elementType.IsValueType ? null : CreateArrayFactory(elementType);
    }

    private static T[] CreateTypedArray<T>(int length) => new T[length];

    [UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "Reference type elements only, which run through shared generics; value type elements fall back to Array.CreateInstance.")]
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "The only target is new T[n] inside this class, and the type argument carries no metadata requirement.")]
    private static Func<int, Array> CreateArrayFactory(Type elementType) =>
        CreateTypedArrayMethod.MakeGenericMethod(elementType).CreateDelegate<Func<int, Array>>();

    [UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "Array types of element types requested through IEnumerable<T> are preserved by the reference path, as verified by AotTests.")]
    protected override object Create(ServiceProviderScope scope)
    {
        if (arrayFactory is not null)
        {
            var typed = arrayFactory(items.Length);
            var view = (object?[])typed;
            for (var i = 0; i < items.Length; i++)
            {
                view[i] = items[i].GetValue(scope);
            }

            return typed;
        }

        var array = Array.CreateInstance(elementType, items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            array.SetValue(items[i].GetValue(scope), i);
        }

        return array;
    }
}
