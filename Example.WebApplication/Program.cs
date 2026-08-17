// ASP.NET Core の DI コンテナを BunnyTail.Resolver へ差し替えるサンプル。
// 差し替えは UseServiceProviderFactory の 1 行のみで、フレームワークサービスも含めて MEDI 互換に動作する
// Sample replacing the ASP.NET Core DI container with BunnyTail.Resolver. The swap is a single
// UseServiceProviderFactory line, and everything including framework services keeps working MEDI compatible.
using BunnyTail.Resolver;
using BunnyTail.Resolver.Diagnostics;

using Example.WebApplication;

var builder = WebApplication.CreateBuilder(args);

// コンテナの差し替え / replace the container
builder.Host.UseServiceProviderFactory(new GeneratedServiceProviderFactory());

// 属性コンポーネントの一括登録 (参照モジュールも含む) / register attribute components in one call (referenced modules included)
builder.Services.AddAllGeneratedComponents();

// 標準の Add* 登録も生成ファクトリの対象 / standard Add* registrations produce generated factories too
builder.Services.AddSingleton<ClockService>();

var app = builder.Build();

#if DEBUG
// 開発時のみ: どのコンポーネントが実行時経路 (リフレクション) で解決されるかを一覧する。
// [GenerateComponentFactory] の追加候補を見つけるための診断で、全登録を実現するためリリースでは行わない
// Development only: lists which components resolve through the runtime path (reflection). This diagnostic finds
// [GenerateComponentFactory] candidates and realizes every registration, so it is not done in release builds.
if (app.Services is ResolverServiceProvider resolverProvider)
{
    var report = resolverProvider.CreateFactoryReport();
    var text = new System.Text.StringBuilder();
    _ = text.AppendLine("---- service factory report ----");
    foreach (var group in report.GroupBy(static x => x.Status).OrderBy(static x => x.Key))
    {
        _ = text.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"{group.Key,-16}: {group.Count()}");
    }

    _ = text.AppendLine("---- runtime path components ----");
    foreach (var entry in report.Where(static x => x.Status == ServiceFactoryStatus.RuntimeFallback))
    {
        _ = text.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"{entry.Lifetime,-9} {entry.ServiceType.Name} -> {entry.ImplementationType?.Name}");
    }

    // singleton は起動時に一度しか構築されないため、効果が出る transient / scoped だけに絞る
    // Singletons are constructed once at startup, so the suggestion is narrowed to the transient and scoped ones that pay off.
    _ = text.AppendLine("---- suggested attributes (transient / scoped only) ----")
        .Append(resolverProvider.DescribeRuntimeFallbacks(static x => x.Lifetime != ServiceLifetime.Singleton))
        .AppendLine("--------------------------------");
    Console.Write(text.ToString());
}
#endif

app.MapGet("/", static () => "BunnyTail.Resolver web sample");

// singleton + scoped + transient の解決。scoped はリクエストごとに変わり、singleton は積み上がる
// Resolves singleton, scoped and transient services: the scoped id changes per request while the singleton accumulates.
app.MapGet("/greet/{name}", static (string name, GreetingService greeting) => greeting.Greet(name));

// 同一リクエスト内で 2 回解決しても scoped は同一、transient は別インスタンス
// Within one request the scoped instance is shared while transients are distinct.
app.MapGet("/scope-check", static (GreetingService first, GreetingService second, RequestContext context) =>
    new
    {
        SameScoped = first.Greet("a").RequestId == second.Greet("b").RequestId,
        DistinctTransient = !ReferenceEquals(first, second),
        RequestId = context.Id
    });

// 標準登録のサービスとコンテナ実装の確認 / the standard registration and the actual container implementation
app.MapGet("/info", static (ClockService clock, IServiceProvider provider) =>
    new
    {
        Clock = clock.Now(),
        Provider = provider.GetType().FullName,
        Counter = provider.GetRequiredService<CounterService>().Current
    });

app.Run();
