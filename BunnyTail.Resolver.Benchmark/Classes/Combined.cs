namespace BunnyTail.Resolver.Benchmark.Classes;

public sealed class Combined1(ISingleton1 singleton)
{
    public ISingleton1 Singleton { get; } = singleton;
}

public sealed class Combined2(ISingleton2 singleton)
{
    public ISingleton2 Singleton { get; } = singleton;
}

public sealed class Combined3(ISingleton3 singleton)
{
    public ISingleton3 Singleton { get; } = singleton;
}

public sealed class Combined4(ISingleton4 singleton)
{
    public ISingleton4 Singleton { get; } = singleton;
}

public sealed class Combined5(ISingleton5 singleton)
{
    public ISingleton5 Singleton { get; } = singleton;
}
