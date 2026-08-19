namespace Example.Library1;

using BunnyTail.DependencyInjection;

// For AddAllGeneratedComponents to collect the generated factories,

public interface IDataStore
{
    void Store(string value);

    IReadOnlyList<string> Values { get; }
}

[Singleton(As = typeof(IDataStore))]
public sealed class MemoryDataStore : IDataStore
{
    private readonly List<string> values = [];

    public IReadOnlyList<string> Values => values;

    public void Store(string value) => values.Add(value);
}

[Singleton]
public sealed class LibraryCounter
{
    private int count;

    public int Increment() => ++count;
}

[Transient(PostConstruct = nameof(Setup))]
public sealed class LibraryWorker
{
    private readonly LibraryCounter counter;

    public bool Initialized { get; private set; }

    public LibraryWorker(LibraryCounter counter)
    {
        this.counter = counter;
    }

    public int Work() => counter.Increment();

    public void Setup() => Initialized = true;
}

[Scoped]
public sealed class LibraryScopedContext
{
    public Guid Id { get; } = Guid.NewGuid();
}

// Library service registered through a conventional extension method
public interface IPlainLibraryService
{
    string Describe();
}

public sealed class PlainLibraryService : IPlainLibraryService
{
    private readonly LibraryCounter counter;

    public PlainLibraryService(LibraryCounter counter)
    {
        this.counter = counter;
    }

    public string Describe() => $"plain library service (counter {counter.Increment()})";
}

public static class Library1ServiceCollectionExtensions
{
    public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddLibrary1Services(
        this Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<IPlainLibraryService, PlainLibraryService>(services);
        return services;
    }
}
