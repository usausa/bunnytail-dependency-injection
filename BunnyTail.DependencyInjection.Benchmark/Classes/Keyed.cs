namespace BunnyTail.DependencyInjection.Benchmark.Classes;

// 同一サービス型を異なるキーで登録し、キー指定解決を測る
// The same service type is registered under different keys to measure keyed resolution.

public interface IKeyedService
{
    void DoSomething();
}

public sealed class KeyedService1 : IKeyedService
{
    public void DoSomething()
    {
    }
}

public sealed class KeyedService2 : IKeyedService
{
    public void DoSomething()
    {
    }
}

public sealed class KeyedService3 : IKeyedService
{
    public void DoSomething()
    {
    }
}

public sealed class KeyedService4 : IKeyedService
{
    public void DoSomething()
    {
    }
}

public sealed class KeyedService5 : IKeyedService
{
    public void DoSomething()
    {
    }
}
