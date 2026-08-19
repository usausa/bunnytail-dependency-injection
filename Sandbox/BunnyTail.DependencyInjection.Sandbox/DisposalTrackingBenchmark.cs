namespace BunnyTail.DependencyInjection.Sandbox;

using BenchmarkDotNet.Attributes;

// transient の disposal 追跡コストの検証。
// 「MEDI 互換のため transient も追跡する」という DI 固有の制約下で、追跡要否を生成時に型で確定させれば
// 非 disposable の resolve に追跡コストが発生しないことを定量化する。互換経路の実行時 is チェックとの差を測る。
// 実行時型チェックが JIT に畳み込まれないよう、互換経路を模して Func<object> ファクトリ経由で生成する
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

    // 追跡リストは disposal 追跡のコストを再現するためのもので、内容の参照はしない
    // The tracking list reproduces the cost of disposal tracking; its contents are never read.
    // ReSharper disable once CollectionNeverQueried.Local
    private readonly List<IDisposable> tracked = [with(CreateCount * 2)];

    private Func<object> plainFactory = default!;
    private Func<object> disposableFactory = default!;
    private Func<DisposableService> disposableTypedFactory = default!;

    // 計測結果の格納先。読み出さないが、書き込むことで JIT のデッドコード削除を防ぐ
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

    // 生成経路: 非 disposable と生成時に確定 → チェックも追跡もなし
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

    // 互換経路: 実行時 is チェック (非 disposable なので分岐は不成立)
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

    // 生成経路: disposable と生成時に確定 → 型チェックなしで無条件追跡
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

    // 互換経路: 実行時 is チェック (disposable なので分岐成立 + 追跡)
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
