namespace Example.WebApplication;

using BunnyTail.DependencyInjection;

// アプリケーションサービス。属性で登録され、生成ファクトリ経由で解決される
// Application services registered by attributes and resolved through generated factories.

// リクエストをまたいで共有される状態 / state shared across requests
[Singleton]
internal sealed class CounterService
{
    private int count;

    public int Increment() => Interlocked.Increment(ref count);

    public int Current => Volatile.Read(ref count);
}

// リクエストスコープごとに 1 つ。スコープ内では同一インスタンスが共有される
// One per request scope; the same instance is shared inside the scope.
[Scoped]
internal sealed class RequestContext
{
    public Guid Id { get; } = Guid.NewGuid();
}

// 都度生成。scoped と singleton を注入して両者の寿命の違いを示す
// Created per resolution, injecting the scoped and singleton services to show the lifetime difference.
[Transient]
internal sealed class GreetingService
{
    private readonly CounterService counter;

    private readonly RequestContext context;

    public GreetingService(CounterService counter, RequestContext context)
    {
        this.counter = counter;
        this.context = context;
    }

    public GreetingResult Greet(string name) =>
        new(name, counter.Increment(), context.Id);
}

internal sealed record GreetingResult(string Name, int Count, Guid RequestId);

// 属性を付けない普通のクラス。標準の Add* 登録から生成ファクトリが作られる
// A plain class without attributes; the generated factory comes from the standard Add* registration.
internal sealed class ClockService
{
    private readonly string value = "fixed-clock";

    public string Now() => value;
}
