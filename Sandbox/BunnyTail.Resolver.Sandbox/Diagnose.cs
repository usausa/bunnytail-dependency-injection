namespace BunnyTail.Resolver.Sandbox;

using BunnyTail.Resolver;
using BunnyTail.Resolver.Sandbox.Classes;

using Microsoft.Extensions.DependencyInjection;

// 生成ファクトリが実行時に採用されているかを確認する診断。
// 対外ベンチの Complex が S-4 の効果を受けなかった原因を切り分けるために使う
// Diagnostic that reports whether generated factories are adopted at runtime.
// Used to isolate why Complex in the external benchmark did not benefit from S-4.
public static class Diagnose
{
    public static void Run()
    {
        var services = new List<ServiceDescriptor>
        {
            ServiceDescriptor.Describe(typeof(IDiagSingleton1), typeof(DiagSingleton1), ServiceLifetime.Singleton),
            ServiceDescriptor.Describe(typeof(IDiagSingleton2), typeof(DiagSingleton2), ServiceLifetime.Singleton),
            ServiceDescriptor.Describe(typeof(IDiagSingleton3), typeof(DiagSingleton3), ServiceLifetime.Singleton),
            ServiceDescriptor.Describe(typeof(DiagCombined1), typeof(DiagCombined1), ServiceLifetime.Transient),
            ServiceDescriptor.Describe(typeof(DiagCombined2), typeof(DiagCombined2), ServiceLifetime.Transient),
            ServiceDescriptor.Describe(typeof(DiagCombined3), typeof(DiagCombined3), ServiceLifetime.Transient),
            ServiceDescriptor.Describe(typeof(DiagComplex), typeof(DiagComplex), ServiceLifetime.Transient),
        };

        using var provider = new ResolverServiceProvider(services);
        var registry = new ServiceRegistry(services, provider);

        Console.WriteLine("accessor kinds (generated factory adopted?)");
        foreach (var type in (Type[])
                 [
                     typeof(IDiagSingleton1), typeof(DiagCombined1), typeof(DiagCombined2),
                     typeof(DiagCombined3), typeof(DiagComplex),
                 ])
        {
            var accessor = registry.GetEntry(new ServiceIdentifier(type, null));
            var kind = accessor?.GetType().Name ?? "(null)";
            var generated = accessor is FactoryAccessor ? "GENERATED" : "reflection";
            Console.WriteLine($"  {type.Name,-20} {kind,-22} {generated}");
        }

        // 生成レジストリに何が登録されているか
        // What the generated registry actually holds.
        Console.WriteLine("generated registry entries");
        foreach (var type in (Type[]) [typeof(DiagCombined1), typeof(DiagComplex)])
        {
            var found = GeneratedComponentRegistry.TryGet(type, out var entry);
            Console.WriteLine($"  {type.Name,-20} registered={found} inlined={(found ? entry.InlinedDependencies.Length : 0)}");
        }
    }
}
