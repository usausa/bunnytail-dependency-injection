namespace BunnyTail.DependencyInjection.Benchmark.Classes;

// ASP.NET Core 風のスコープ利用シミュレーション用 (Controller → transient サービス → scoped サービス)
// Simulates ASP.NET Core style scope usage (controller -> transient services -> scoped service).

public interface IScopedService
{
    void DoSomething();
}

public sealed class ScopedService : IScopedService
{
    public void DoSomething()
    {
    }
}

public interface ITransientService1
{
    void DoSomething();
}

public sealed class TransientService1(IScopedService scopedService) : ITransientService1
{
    public IScopedService ScopedService { get; } = scopedService;

    public void DoSomething()
    {
    }
}

public interface ITransientService2
{
    void DoSomething();
}

public sealed class TransientService2(IScopedService scopedService) : ITransientService2
{
    public IScopedService ScopedService { get; } = scopedService;

    public void DoSomething()
    {
    }
}

public interface ITransientService3
{
    void DoSomething();
}

public sealed class TransientService3(IScopedService scopedService) : ITransientService3
{
    public IScopedService ScopedService { get; } = scopedService;

    public void DoSomething()
    {
    }
}

public sealed class Controller(ITransientService1 transientService1, ITransientService2 transientService2, ITransientService3 transientService3)
{
    public ITransientService1 TransientService1 { get; } = transientService1;

    public ITransientService2 TransientService2 { get; } = transientService2;

    public ITransientService3 TransientService3 { get; } = transientService3;
}
