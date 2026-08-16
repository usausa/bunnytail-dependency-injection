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

// [ComponentRegistration] の Assembly 指定 (参照アセンブリのメタデータ走査) の対象。
// モジュールにも属性にも載っていない素の型で、アプリ側の規約で登録される
// Target of the [ComponentRegistration] Assembly parameter (referenced assembly metadata scan).
// A plain type carried by neither the module nor attributes; the application registers it by convention.
public sealed class ExternalWorker
{
    private readonly IMessageSource source;

    public ExternalWorker(IMessageSource source)
    {
        this.source = source;
    }

    public string Describe() => $"external worker ({source.GetMessage()})";
}
