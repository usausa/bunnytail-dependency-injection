namespace BunnyTail.Resolver;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

// 生成コードが deps 配列のアクセサスロット経由で依存を解決するための公開ハンドル (S-7 第 2 段階)。
// テーブル probe と GetRequiredService ラッパを経ずに、採用時に検証済みの accessor を直接呼び出す。
// null 時の throw は GetRequiredService と同じ意味論・同じメッセージを維持する
// Public handle used by generated code to resolve dependencies through accessor slots of the deps array
// (S-7 stage 2). Calls the accessor validated at adoption time directly, skipping the table probe and the
// GetRequiredService wrapper. Throwing on null keeps the exact GetRequiredService semantics and message.
public sealed class DependencyAccessor
{
    private readonly ServiceAccessor accessor;

    private readonly Type serviceType;

    internal DependencyAccessor(ServiceAccessor accessor, Type serviceType)
    {
        this.accessor = accessor;
        this.serviceType = serviceType;
    }

    // 参照型用。前提検証済みのため Unsafe.As (インスタンススロットと同じ信頼レベル)
    // For reference types. Assumptions are validated, so Unsafe.As is used (same trust level as instance slots).
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

    // 値型用 (box 経由のキャストは呼び出し側で行う)
    // For value types (the callers cast through the box).
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
