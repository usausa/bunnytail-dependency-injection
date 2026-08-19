namespace Example;

using BunnyTail.DependencyInjection;
using BunnyTail.DependencyInjection.Diagnostics;

using Example.Library1;

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

// Verification of module aggregation. AddAllGeneratedComponents registers both kinds of referenced modules
// (Example.Library1 with the auto-embedded marker, Example.Library2 with a hand-written marker and module)
// plus this assembly's attribute components in a single call.
internal static class Program
{
    private static int failed;

    public static int Main()
    {
        var services = new ServiceCollection();
        services.AddAllGeneratedComponents();
        services.AddLibraryWorkers();
        services.AddManualServices();
        services.AddLibrary1Services();
        Library2.ReportedServiceRegistrations.AddReportedService(services);

        using (var provider = services.BuildGeneratedServiceProvider())
        {
            // Library module registration (interface forwarding and singleton identity).
            var store = provider.GetRequiredService<IDataStore>();
            Assert(store is MemoryDataStore, "library interface forwarding");
            Assert(ReferenceEquals(store, provider.GetRequiredService<IDataStore>()), "library singleton identity");

            // Cross assembly injection (the app component receives library components).
            var app = provider.GetRequiredService<AppService>();
            var message = app.Run();
            Assert(message.Contains("work 1", StringComparison.Ordinal), "cross assembly injection");
            Assert(message.Contains("initialized: True", StringComparison.Ordinal), "post construct callback");
            Assert(store.Values.Count == 1, "shared singleton state");

            // transients are fresh per resolution
            // ReSharper disable once EqualExpressionComparison
            Assert(!ReferenceEquals(provider.GetRequiredService<LibraryWorker>(), provider.GetRequiredService<LibraryWorker>()), "library transient distinct");

            // the manually declared module is aggregated as well
            var messageSource = provider.GetRequiredService<Library2.IMessageSource>();
            Assert(messageSource.GetMessage() == "manual module", "manual module registration");
            Assert(ReferenceEquals(messageSource, provider.GetRequiredService<Library2.IMessageSource>()), "manual module singleton identity");

            // assembly scoped convention registration
            var worker = provider.GetRequiredService<Library2.ExternalWorker>();
            Assert(worker.Describe() == "external worker (manual module)", "assembly scoped convention registration");
            Assert(!ReferenceEquals(worker, provider.GetRequiredService<Library2.ExternalWorker>()), "assembly scoped convention transient");

            // Registration through the library's own extension method. Registration is ordinary MEDI behavior, and the
            // generated factory emitted by the library's generator is adopted automatically by implementation type.
            var plain = provider.GetRequiredService<IPlainLibraryService>();
            Assert(plain.Describe().StartsWith("plain library service", StringComparison.Ordinal), "library extension method registration");
            // Comparing identical expressions is the verification itself: resolve twice and check identity.
            Assert(ReferenceEquals(plain, provider.GetRequiredService<IPlainLibraryService>()), "library extension method singleton");

            // Standard Add* registrations without attributes: generic, typeof, keyed, TryAddEnumerable and factory shapes.
            Assert(provider.GetRequiredService<IManualService>().Describe() == "manual singleton", "manual generic registration");
            Assert(provider.GetRequiredKeyedService<IManualKeyed>("primary").Kind == "primary", "manual keyed registration");
            Assert(provider.GetServices<IManualPlugin>().Select(static x => x.Name).SequenceEqual(["A", "B"]), "manual enumerable registration");
            Assert(provider.GetRequiredService<ManualOptions>().Value == "from factory", "manual factory registration");

            // A type marked with [GenerateComponentFactory] resolves through the generated path even when the registration
            // comes from another library's extension method. The diagnostic report shows the actual path (development use only).
            var reported = provider.GetRequiredService<Library2.ReportedService>();
            Assert(reported.Describe().StartsWith("reported service", StringComparison.Ordinal), "generate factory target resolves");
            Assert(reported.Prepared, "generate factory post construct");

            var report = provider.CreateFactoryReport();
            var reportedEntry = report.First(static x => x.ImplementationType == typeof(Library2.ReportedService));
            Assert(reportedEntry.Status == ServiceFactoryStatus.Generated, "generate factory target uses generated path");

            var messageEntry = report.First(static x => x.ImplementationType == typeof(Library2.MessageSource));
            Assert(messageEntry.Status == ServiceFactoryStatus.RuntimeFallback, "untracked library type falls back");

            // Runtime path types can be written out as ready-to-paste attribute lines
            var suggestion = provider.DescribeRuntimeFallbacks();
            Assert(suggestion.Contains("Example.Library2.MessageSource", StringComparison.Ordinal), "runtime fallback is suggested");
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
