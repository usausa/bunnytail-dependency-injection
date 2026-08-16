namespace BunnyTail.Resolver.Sandbox.Classes;

// 対外ベンチの Complex と同じ形状 (singleton 3 + transient 3、transient は各 1 singleton 依存)
// Same shape as Complex in the external benchmark: three singletons plus three transients that each take one singleton.

public interface IDiagSingleton1;

public sealed class DiagSingleton1 : IDiagSingleton1;

public interface IDiagSingleton2;

public sealed class DiagSingleton2 : IDiagSingleton2;

public interface IDiagSingleton3;

public sealed class DiagSingleton3 : IDiagSingleton3;

public sealed class DiagCombined1(IDiagSingleton1 singleton)
{
    public IDiagSingleton1 Singleton { get; } = singleton;
}

public sealed class DiagCombined2(IDiagSingleton2 singleton)
{
    public IDiagSingleton2 Singleton { get; } = singleton;
}

public sealed class DiagCombined3(IDiagSingleton3 singleton)
{
    public IDiagSingleton3 Singleton { get; } = singleton;
}

public sealed class DiagComplex(
    IDiagSingleton1 singleton1,
    IDiagSingleton2 singleton2,
    IDiagSingleton3 singleton3,
    DiagCombined1 combined1,
    DiagCombined2 combined2,
    DiagCombined3 combined3)
{
    public IDiagSingleton1 Singleton1 { get; } = singleton1;

    public IDiagSingleton2 Singleton2 { get; } = singleton2;

    public IDiagSingleton3 Singleton3 { get; } = singleton3;

    public DiagCombined1 Combined1 { get; } = combined1;

    public DiagCombined2 Combined2 { get; } = combined2;

    public DiagCombined3 Combined3 { get; } = combined3;
}

// 生成ファクトリを出させるためのアンカー。ジェネレータは Add* 呼び出しから実装型を収集する
// Anchor that makes the generator emit factories: it collects implementation types from Add* invocations.
public static class DiagRegistration
{
    public static Microsoft.Extensions.DependencyInjection.IServiceCollection Register(
        Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<IDiagSingleton1, DiagSingleton1>(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<IDiagSingleton2, DiagSingleton2>(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<IDiagSingleton3, DiagSingleton3>(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddTransient<DiagCombined1>(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddTransient<DiagCombined2>(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddTransient<DiagCombined3>(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddTransient<DiagComplex>(services);
        return services;
    }
}
