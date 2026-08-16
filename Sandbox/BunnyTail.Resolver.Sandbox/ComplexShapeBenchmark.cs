namespace BunnyTail.Resolver.Sandbox;

using BenchmarkDotNet.Attributes;

using BunnyTail.Resolver;
using BunnyTail.Resolver.Sandbox.Classes;

using Microsoft.Extensions.DependencyInjection;

// 対外ベンチの Complex (MEDI の 2.65 倍) の内訳を 3 段で分解する。
// 生成ファクトリが採用されていることは診断で確認済みなので、コストは
// 「外側の解決機構」「依存 6 件の解決」「生成と割り当ての下限」のどこかにある
// Decomposes Complex from the external benchmark (2.65x MEDI) into three steps. The diagnostic confirmed the generated
// factory is adopted, so the cost sits in the outer resolution machinery, the six dependency resolutions, or the
// floor of construction and allocation.
[Config(typeof(BenchmarkConfig))]
public class ComplexShapeBenchmark
{
    private ResolverServiceProvider provider = default!;
    private ServiceProviderScope scope = default!;

    private IDiagSingleton1 singleton1 = default!;
    private IDiagSingleton2 singleton2 = default!;
    private IDiagSingleton3 singleton3 = default!;

    private object? sink;

    [GlobalSetup]
    public void Setup()
    {
        var services = new List<ServiceDescriptor>();
        DiagRegistration.Register(new ServiceCollectionAdapter(services));

        provider = new ResolverServiceProvider(services);
        scope = provider.RootScope;

        // 事前解決 (hot 条件を揃える)
        // Prime everything so all measurements share the hot condition.
        _ = provider.GetService(typeof(DiagComplex));
        singleton1 = (IDiagSingleton1)provider.GetService(typeof(IDiagSingleton1))!;
        singleton2 = (IDiagSingleton2)provider.GetService(typeof(IDiagSingleton2))!;
        singleton3 = (IDiagSingleton3)provider.GetService(typeof(IDiagSingleton3))!;
    }

    [GlobalCleanup]
    public void Cleanup() => provider.Dispose();

    // A: 利用者が通る経路そのもの
    // A: the path a consumer actually takes.
    [Benchmark(Baseline = true)]
    public object? FullResolution()
    {
        sink = provider.GetService(typeof(DiagComplex));
        return sink;
    }

    // B: 生成ファクトリ本体と同じ形 (外側の解決機構を外し、依存解決はそのまま)
    // B: the same shape as the generated factory body, without the outer resolution machinery.
    [Benchmark]
    public object? FactoryBodyShape()
    {
        IServiceProvider p = scope;
        var instance = new DiagComplex(
            p.GetRequiredService<IDiagSingleton1>(),
            p.GetRequiredService<IDiagSingleton2>(),
            p.GetRequiredService<IDiagSingleton3>(),
            new DiagCombined1(p.GetRequiredService<IDiagSingleton1>()),
            new DiagCombined2(p.GetRequiredService<IDiagSingleton2>()),
            new DiagCombined3(p.GetRequiredService<IDiagSingleton3>()));
        sink = instance;
        return instance;
    }

    // C: 下限。コンテナを一切通さない生成と割り当てだけ
    // C: the floor. Construction and allocation only, with no container involved.
    [Benchmark]
    public object? ConstructionFloor()
    {
        var instance = new DiagComplex(
            singleton1,
            singleton2,
            singleton3,
            new DiagCombined1(singleton1),
            new DiagCombined2(singleton2),
            new DiagCombined3(singleton3));
        sink = instance;
        return instance;
    }
}

// DiagRegistration が IServiceCollection を要求するため、List<ServiceDescriptor> を包む最小アダプタ
// Minimal adapter wrapping a List<ServiceDescriptor>, because DiagRegistration takes an IServiceCollection.
internal sealed class ServiceCollectionAdapter(List<ServiceDescriptor> items) : IServiceCollection
{
    public ServiceDescriptor this[int index] { get => items[index]; set => items[index] = value; }

    public int Count => items.Count;

    public bool IsReadOnly => false;

    public void Add(ServiceDescriptor item) => items.Add(item);

    public void Clear() => items.Clear();

    public bool Contains(ServiceDescriptor item) => items.Contains(item);

    public void CopyTo(ServiceDescriptor[] array, int arrayIndex) => items.CopyTo(array, arrayIndex);

    public IEnumerator<ServiceDescriptor> GetEnumerator() => items.GetEnumerator();

    public int IndexOf(ServiceDescriptor item) => items.IndexOf(item);

    public void Insert(int index, ServiceDescriptor item) => items.Insert(index, item);

    public bool Remove(ServiceDescriptor item) => items.Remove(item);

    public void RemoveAt(int index) => items.RemoveAt(index);

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => items.GetEnumerator();
}
