namespace Example.WebApplication;

using BunnyTail.DependencyInjection;

// Singleton

[Singleton]
internal sealed class CounterService
{
    private int count;

    public int Increment() => Interlocked.Increment(ref count);

    public int Current => Volatile.Read(ref count);
}

// Scoped

[Scoped]
internal sealed class RequestContext
{
    public Guid Id { get; } = Guid.NewGuid();
}

// Transient

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

// Plain class without attributes

internal sealed class ClockService
{
    private readonly string value = "fixed-clock";

    public string Now() => value;
}
