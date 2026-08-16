// 手動モジュール宣言パターン。Generator を参照しないライブラリでも、登録メソッドを持つモジュール型を自作して
// ComponentModule マーカーを手書きすれば、参照側の AddAllGeneratedComponents の集約対象になる
// Manual module declaration pattern. A library that does not reference the generator can still participate in
// AddAllGeneratedComponents aggregation by hand-writing a module type with a registration method and the ComponentModule marker.
[assembly: BunnyTail.Resolver.ComponentModule(typeof(Example.Library2.LibraryModule))]

namespace Example.Library2;

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
    public static IServiceCollection AddGeneratedComponents(this IServiceCollection services)
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

// ライブラリ純正の登録用拡張メソッド。このライブラリはジェネレータを参照していないため、
// この呼び出しはアプリ側のジェネレータから追跡できない (= 実行時経路になる)。
// アプリ側で [assembly: GenerateComponentFactory(typeof(ReportedService))] を書くとファクトリだけが生成される
// The library's own registration extension method. Since this library does not reference the generator, the call
// cannot be tracked by the application's generator (so it takes the runtime path). Writing
// [assembly: GenerateComponentFactory(typeof(ReportedService))] in the application generates the factory alone.
public sealed class ReportedService
{
    private readonly IMessageSource source;

    public ReportedService(IMessageSource source)
    {
        this.source = source;
    }

    public bool Prepared { get; private set; }

    // BunnyTail の属性を付けられない型でも、利用側の [GenerateComponentFactory(PostConstruct = ...)] で呼び出せる
    // Even without BunnyTail attributes, the consumer can invoke this through [GenerateComponentFactory(PostConstruct = ...)].
    public void Prepare() => Prepared = true;

    public string Describe() => $"reported service ({source.GetMessage()})";
}

public static class ReportedServiceRegistrations
{
    public static IServiceCollection AddReportedService(this IServiceCollection services)
    {
        services.AddTransient<ReportedService>();
        return services;
    }
}
