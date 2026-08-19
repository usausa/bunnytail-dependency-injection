namespace Example;

using BunnyTail.DependencyInjection;
using BunnyTail.DependencyInjection.Diagnostics;

using Example.Library;
using Example.ThirdPartyLibrary;

using Microsoft.Extensions.DependencyInjection;

// Attribute component on the application side, receiving library components through constructor injection
[Singleton]
internal sealed class AppService
{
    private readonly IDataStore store;

    private readonly LibraryWorker worker;

    public AppService(IDataStore store, LibraryWorker worker)
    {
        this.store = store;
        this.worker = worker;
    }

    public string Run()
    {
        var count = worker.Work();
        var message = $"work {count} (worker initialized: {worker.Initialized})";
        store.Store(message);
        return message;
    }
}

// モジュール集約の検証。AddAllGeneratedComponents が、参照モジュール (Example.Library = 生成コードによる
// 自動マーカー) と自アセンブリ (Example) の属性コンポーネントを 1 呼び出しで登録することを確認する。
// Example.ThirdPartyLibrary は BunnyTail を参照しないサードパーティの代役で、集約対象にはならず、
// 自身の拡張メソッド経由で登録される
// Verification of module aggregation. AddAllGeneratedComponents registers the referenced module
// (Example.Library, with the auto-embedded marker) plus this assembly's attribute components in a single call.
// Example.ThirdPartyLibrary stands in for a third party that does not reference BunnyTail: it is not an
// aggregation target and registers through its own extension methods.
internal static class Program
{
    private static int failed;

    public static int Main()
    {
        var services = new ServiceCollection();
        services.AddAllGeneratedComponents();
        services.AddLibraryWorkers();
        services.AddManualServices();
        services.AddLibraryServices();
        services.AddMessageSource();
        services.AddReportedService();

        using (var provider = services.BuildGeneratedServiceProvider())
        {
            // ライブラリモジュールの登録 (interface 転送 + singleton 同一性)
            // Library module registration (interface forwarding and singleton identity).
            var store = provider.GetRequiredService<IDataStore>();
            Assert(store is MemoryDataStore, "library interface forwarding");
            Assert(ReferenceEquals(store, provider.GetRequiredService<IDataStore>()), "library singleton identity");

            // クロスアセンブリ注入 (アプリ component がライブラリ component を受け取る)
            // Cross assembly injection (the app component receives library components).
            var app = provider.GetRequiredService<AppService>();
            var message = app.Run();
            Assert(message.Contains("work 1", StringComparison.Ordinal), "cross assembly injection");
            Assert(message.Contains("initialized: True", StringComparison.Ordinal), "post construct callback");
            Assert(store.Values.Count == 1, "shared singleton state");

            // transient は毎回新規 / transients are fresh per resolution
            // ReSharper disable once EqualExpressionComparison
            Assert(!ReferenceEquals(provider.GetRequiredService<LibraryWorker>(), provider.GetRequiredService<LibraryWorker>()), "library transient distinct");

            // サードパーティの拡張メソッド経由の登録 / registration through the third party's own extension method
            var messageSource = provider.GetRequiredService<ThirdPartyLibrary.IMessageSource>();
            Assert(messageSource.GetMessage() == "third party message", "third party extension method registration");
            Assert(ReferenceEquals(messageSource, provider.GetRequiredService<ThirdPartyLibrary.IMessageSource>()), "third party singleton identity");

            // Assembly 指定の規約登録 (サードパーティの素の型) / assembly scoped convention registration
            var worker = provider.GetRequiredService<ThirdPartyLibrary.ExternalWorker>();
            Assert(worker.Describe() == "external worker (third party message)", "assembly scoped convention registration");
            Assert(!ReferenceEquals(worker, provider.GetRequiredService<ThirdPartyLibrary.ExternalWorker>()), "assembly scoped convention transient");

            // ライブラリが提供する登録用拡張メソッド経由の登録。登録は通常の MEDI 動作で、
            // 生成ファクトリはライブラリ側のジェネレータが出力したものが実装型で自動採用される
            // Registration through the library's own extension method. Registration is ordinary MEDI behavior, and the
            // generated factory emitted by the library's generator is adopted automatically by implementation type.
            var plain = provider.GetRequiredService<IPlainLibraryService>();
            Assert(plain.Describe().StartsWith("plain library service", StringComparison.Ordinal), "library extension method registration");
            // 同一式どうしの比較は「2 回解決して同一インスタンスか」の検証そのもの
            // Comparing identical expressions is the verification itself: resolve twice and check identity.
            Assert(ReferenceEquals(plain, provider.GetRequiredService<IPlainLibraryService>()), "library extension method singleton");

            // 標準 Add* 登録 (属性なし)。ジェネリック / typeof / keyed / TryAddEnumerable / ファクトリの各形
            // Standard Add* registrations without attributes: generic, typeof, keyed, TryAddEnumerable and factory shapes.
            Assert(provider.GetRequiredService<IManualService>().Describe() == "manual singleton", "manual generic registration");
            Assert(provider.GetRequiredKeyedService<IManualKeyed>("primary").Kind == "primary", "manual keyed registration");
            Assert(provider.GetServices<IManualPlugin>().Select(static x => x.Name).SequenceEqual(["A", "B"]), "manual enumerable registration");
            Assert(provider.GetRequiredService<ManualOptions>().Value == "from factory", "manual factory registration");

            // [GenerateComponentFactory] を書いた型は、登録が他ライブラリの拡張メソッド経由でも生成経路で解決される。
            // 診断レポートで経路を確認する (開発時のみの用途)
            // A type marked with [GenerateComponentFactory] resolves through the generated path even when the registration
            // comes from another library's extension method. The diagnostic report shows the actual path (development use only).
            var reported = provider.GetRequiredService<ThirdPartyLibrary.ReportedService>();
            Assert(reported.Describe().StartsWith("reported service", StringComparison.Ordinal), "generate factory target resolves");
            Assert(reported.Prepared, "generate factory post construct");

            var report = provider.CreateFactoryReport();
            var reportedEntry = report.First(static x => x.ImplementationType == typeof(ThirdPartyLibrary.ReportedService));
            Assert(reportedEntry.Status == ServiceFactoryStatus.Generated, "generate factory target uses generated path");

            var messageEntry = report.First(static x => x.ImplementationType == typeof(ThirdPartyLibrary.MessageSource));
            Assert(messageEntry.Status == ServiceFactoryStatus.RuntimeFallback, "untracked library type falls back");

            // Runtime path types can be written out as ready-to-paste attribute lines
            var suggestion = provider.DescribeRuntimeFallbacks();
            Assert(suggestion.Contains("Example.ThirdPartyLibrary.MessageSource", StringComparison.Ordinal), "runtime fallback is suggested");
            Console.Write(suggestion);

            using (var manualScope = provider.CreateScope())
            {
                var consumer = manualScope.ServiceProvider.GetRequiredService<ManualConsumer>();
                Assert(ReferenceEquals(consumer.Service, provider.GetRequiredService<IManualService>()), "manual singleton shared");
                Assert(ReferenceEquals(consumer.Scoped, manualScope.ServiceProvider.GetRequiredService<ManualScopedService>()), "manual scoped identity");
                Assert(consumer.Box is ManualBox<int>, "manual open generic registration");
            }

            // Scoped instances are shared within a scope and distinct across scopes
            using var scope1 = provider.CreateScope();
            using var scope2 = provider.CreateScope();
            var context1 = scope1.ServiceProvider.GetRequiredService<LibraryScopedContext>();
            Assert(ReferenceEquals(context1, scope1.ServiceProvider.GetRequiredService<LibraryScopedContext>()), "scoped identity in scope");
            Assert(!ReferenceEquals(context1, scope2.ServiceProvider.GetRequiredService<LibraryScopedContext>()), "scoped distinct across scopes");
        }

        Console.WriteLine(failed == 0 ? "ALL OK" : $"FAILED: {failed}");
        return failed == 0 ? 0 : 1;
    }

    private static void Assert(bool condition, string name)
    {
        if (condition)
        {
            Console.WriteLine($"OK   : {name}");
        }
        else
        {
            failed++;
            Console.WriteLine($"NG   : {name}");
        }
    }
}
