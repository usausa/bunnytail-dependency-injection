namespace BunnyTail.DependencyInjection.Sandbox;

using BenchmarkDotNet.Attributes;

// Verification of the disposal tracking cost of transients.
// Under the DI specific constraint that transients are tracked for MEDI compatibility, this quantifies that
// resolving a non disposable costs nothing once the need for tracking is settled by type at generation time,
// and measures the gap against the runtime is check. Instances come from a Func<object> factory that mimics the runtime path so the check is not folded away by the JIT.
// Verification of transient disposal tracking cost. Under the DI-specific constraint that transients must also be
// tracked (MEDI compatibility), settling the tracking decision from the type at generation time removes the cost
// for non-disposable resolutions. Instances are created through a Func<object> factory, mirroring the runtime path,
// so the JIT cannot fold the type check away.
[Config(typeof(BenchmarkConfig))]
public class DisposalTrackingBenchmark
{
    private const int CreateCount = 64;

    public sealed class PlainService;

    public sealed class DisposableService : IDisposable
    {
        public void Dispose()
        {
        }
    }

    // The tracking list only reproduces the cost of disposal tracking; its contents are never read.
    // The tracking list reproduces the cost of disposal tracking; its contents are never read.
    // ReSharper disable once CollectionNeverQueried.Local
    private readonly List<IDisposable> tracked = [with(CreateCount * 2)];

    private Func<object> plainFactory = default!;
    private Func<object> disposableFactory = default!;
    private Func<DisposableService> disposableTypedFactory = default!;

    // Sink for the measured results. It is never read, but writing to it prevents JIT dead code elimination.
    // Sink for measured values: never read, but written so the JIT cannot eliminate the work.
    // ReSharper disable once NotAccessedField.Local
    private object? sink;

    [GlobalSetup]
    public void Setup()
    {
        plainFactory = static () => new PlainService();
        disposableFactory = static () => new DisposableService();
        disposableTypedFactory = static () => new DisposableService();
    }

    // Generated path: settled as non disposable at generation time, so there is neither a check nor tracking.
    // Generated path: settled as non-disposable at generation time, so neither check nor tracking remains.
    [Benchmark(Baseline = true, OperationsPerInvoke = CreateCount)]
    public void KnownPlain()
    {
        for (var i = 0; i < CreateCount; i++)
        {
            sink = plainFactory();
        }

        tracked.Clear();
    }

    // Runtime path: a runtime is check, whose branch is not taken because the type is not disposable.
    // Runtime path: runtime is-check (the branch never taken because the type is not disposable).
    [Benchmark(OperationsPerInvoke = CreateCount)]
    public void RuntimeCheckPlain()
    {
        for (var i = 0; i < CreateCount; i++)
        {
            var instance = plainFactory();
            sink = instance;
            if (instance is IDisposable disposable)
            {
                tracked.Add(disposable);
            }
        }

        tracked.Clear();
    }

    // Generated path: settled as disposable at generation time, so tracking is unconditional with no type check.
    // Generated path: settled as disposable at generation time, so tracking happens unconditionally with no type check.
    [Benchmark(OperationsPerInvoke = CreateCount)]
    public void KnownDisposable()
    {
        for (var i = 0; i < CreateCount; i++)
        {
            var instance = disposableTypedFactory();
            sink = instance;
            tracked.Add(instance);
        }

        tracked.Clear();
    }

    // Runtime path: a runtime is check, whose branch is taken because the type is disposable, followed by tracking.
    // Runtime path: runtime is-check (the branch is taken and the instance is tracked).
    [Benchmark(OperationsPerInvoke = CreateCount)]
    public void RuntimeCheckDisposable()
    {
        for (var i = 0; i < CreateCount; i++)
        {
            var instance = disposableFactory();
            sink = instance;
            if (instance is IDisposable disposable)
            {
                tracked.Add(disposable);
            }
        }

        tracked.Clear();
    }
}
