// 手動モジュール宣言パターン。Generator を参照しないライブラリでも、登録メソッドを持つモジュール型を自作して
// ComponentModule マーカーを手書きすれば、参照側の AddAllComponents の集約対象になる
// Manual module declaration pattern. A library that does not reference the generator can still participate in
// AddAllComponents aggregation by hand-writing a module type with a registration method and the ComponentModule marker.
[assembly: BunnyTail.Resolver.ComponentModule(typeof(Develop.Library2.LibraryModule))]

namespace Develop.Library2;

using Microsoft.Extensions.DependencyInjection;

public interface IMessageSource
{
    string GetMessage();
}

public sealed class MessageSource : IMessageSource
{
    public string GetMessage() => "manual module";
}

public static class LibraryModule
{
    public static IServiceCollection AddComponents(this IServiceCollection services)
    {
        services.AddSingleton<IMessageSource, MessageSource>();
        return services;
    }
}
