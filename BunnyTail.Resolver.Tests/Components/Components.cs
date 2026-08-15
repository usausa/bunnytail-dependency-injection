namespace BunnyTail.Resolver.Tests.Components;

using BunnyTail.Resolver;

// ユーザーコード想定のサンプルコンポーネント群 (属性ベース登録 SPEC 3.1 / 3.4)

public interface IScopedService
{
    Guid Id { get; }
}

public interface IKeyedService;

[Singleton]
public sealed class PropDependency;

[Singleton]
public sealed class SingletonComponent;

[Transient]
public sealed class TransientComponent
{
    public SingletonComponent Singleton { get; }

    [Inject]
    public PropDependency Prop { get; set; } = default!;

    public TransientComponent(SingletonComponent singleton)
    {
        Singleton = singleton;
    }
}

[Scoped]
public sealed class ScopedComponent : IScopedService
{
    public Guid Id { get; } = Guid.NewGuid();
}

[Singleton(As = typeof(IKeyedService), Key = "primary")]
public sealed class PrimaryKeyedComponent : IKeyedService;

[Singleton]
public sealed class DisposableSingleton : IDisposable
{
    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}
