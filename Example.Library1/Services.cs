namespace Example.Library1;

using BunnyTail.Resolver;

// ライブラリ側の属性コンポーネント。このアセンブリの GeneratedComponents (モジュール) が生成され、
// アセンブリレベルの ComponentModule マーカーが埋め込まれる。参照するアプリ側は AddAllGeneratedComponents で一括登録できる
// Attribute components on the library side. The GeneratedComponents module of this assembly is generated with the
// assembly level ComponentModule marker, so referencing applications can register everything through AddAllGeneratedComponents.

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

// ライブラリ側が提供する「普通の」登録用拡張メソッド。属性を使わず標準の Add* で登録するが、
// このライブラリはジェネレータを参照しているため、呼び出しはライブラリ側で収集され
// PlainLibraryService の生成ファクトリがこのアセンブリの ModuleInitializer で登録される
// A conventional registration extension method provided by the library. It registers through the standard Add*
// calls without attributes, but because this library references the generator, the calls are collected here and
// the generated factory for PlainLibraryService is registered by this assembly's module initializer.
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
