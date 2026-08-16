namespace Develop;

using BunnyTail.Resolver;

using Develop.Library;

using Microsoft.Extensions.DependencyInjection;

// アプリ側の属性コンポーネント。ライブラリのコンポーネントをコンストラクタ注入で受け取る
// Attribute component on the application side, receiving library components through constructor injection.
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

// モジュール集約 (案 1) の検証。AddAllComponents が参照モジュール 2 種
// (Develop.Library = 生成コードによる自動マーカー / Develop.Library2 = 手書きマーカー + 自作モジュール) と
// 自アセンブリ (Develop) の属性コンポーネントを 1 呼び出しで登録することを確認する
// Verification of module aggregation. AddAllComponents registers both kinds of referenced modules
// (Develop.Library with the auto-embedded marker, Develop.Library2 with a hand-written marker and module)
// plus this assembly's attribute components in a single call.
internal static class Program
{
    private static int failed;

    public static int Main()
    {
        var services = new ServiceCollection();
        services.AddAllComponents();
        services.AddLibraryWorkers();

        using (var provider = services.BuildResolverServiceProvider())
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
            Assert(!ReferenceEquals(provider.GetRequiredService<LibraryWorker>(), provider.GetRequiredService<LibraryWorker>()), "library transient distinct");

            // 手動宣言モジュール (Develop.Library2) も集約される / the manually declared module is aggregated as well
            var messageSource = provider.GetRequiredService<Develop.Library2.IMessageSource>();
            Assert(messageSource.GetMessage() == "manual module", "manual module registration");
            Assert(ReferenceEquals(messageSource, provider.GetRequiredService<Develop.Library2.IMessageSource>()), "manual module singleton identity");

            // Assembly 指定の規約登録 (Generator 非参照ライブラリの素の型) / assembly scoped convention registration
            var worker = provider.GetRequiredService<Develop.Library2.ExternalWorker>();
            Assert(worker.Describe() == "external worker (manual module)", "assembly scoped convention registration");
            Assert(!ReferenceEquals(worker, provider.GetRequiredService<Develop.Library2.ExternalWorker>()), "assembly scoped convention transient");

            // scoped はスコープ内共有・スコープ間分離 / scoped instances are shared inside a scope and distinct across scopes
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
