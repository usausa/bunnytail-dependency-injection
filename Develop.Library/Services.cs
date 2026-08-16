namespace Develop.Library;

using BunnyTail.Resolver;

// ライブラリ側の属性コンポーネント。このアセンブリの GeneratedComponents (モジュール) が生成され、
// アセンブリレベルの ComponentModule マーカーが埋め込まれる。参照するアプリ側は AddAllComponents で一括登録できる
// Attribute components on the library side. The GeneratedComponents module of this assembly is generated with the
// assembly level ComponentModule marker, so referencing applications can register everything through AddAllComponents.

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
