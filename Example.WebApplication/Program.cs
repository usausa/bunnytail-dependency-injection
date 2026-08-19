using BunnyTail.DependencyInjection;
#if DEBUG
using BunnyTail.DependencyInjection.Diagnostics;
#endif

using Example.WebApplication;

var builder = WebApplication.CreateBuilder(args);

// Replace container with generated factories
builder.Host.UseServiceProviderFactory(new GeneratedServiceProviderFactory());

// Register attribute components in one call (referenced modules included)
builder.Services.AddGeneratedComponents();
// Register manually written services (conventional registration)
builder.Services.AddSingleton<ClockService>();

var app = builder.Build();

#if DEBUG
// Development only: lists which components resolve through the runtime path (reflection)
if (app.Services is GeneratedServiceProvider resolverProvider)
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

    // Singletons are constructed once at startup, so the suggestion is narrowed to the transient and scoped ones that pay off.
    _ = text.AppendLine("---- suggested attributes (transient / scoped only) ----")
        .Append(resolverProvider.DescribeRuntimeFallbacks(static x => x.Lifetime != ServiceLifetime.Singleton))
        .AppendLine("--------------------------------");
    Console.Write(text.ToString());
}
#endif

app.MapGet("/", static () => "BunnyTail.DependencyInjection web sample");

// Resolves singleton, scoped and transient services: the scoped id changes per request while the singleton accumulates
app.MapGet("/greet/{name}", static (string name, GreetingService greeting) => greeting.Greet(name));

// Within one request the scoped instance is shared while transients are distinct
app.MapGet("/scope-check", static (GreetingService first, GreetingService second, RequestContext context) =>
    new
    {
        SameScoped = first.Greet("a").RequestId == second.Greet("b").RequestId,
        DistinctTransient = !ReferenceEquals(first, second),
        RequestId = context.Id
    });

// Standard registration and the actual container implementation
app.MapGet("/info", static (ClockService clock, IServiceProvider provider) =>
    new
    {
        Clock = clock.Now(),
        Provider = provider.GetType().FullName,
        Counter = provider.GetRequiredService<CounterService>().Current
    });

app.Run();
