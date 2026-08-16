namespace BunnyTail.Resolver.Benchmark;

using BunnyTail.Resolver.Benchmark.Classes;

using Microsoft.Extensions.DependencyInjection;

// 測定前の等価性検証。全プロバイダが同一セマンティクスで解決できることを確認する
// Equivalence verification before measurement. Confirms every provider resolves with identical semantics.
public static class Validator
{
    public static void Validate(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        // Singleton: 同一インスタンス / same instance
        Ensure(ReferenceEquals(provider.GetRequiredService<ISingleton1>(), provider.GetRequiredService<ISingleton1>()), "singleton");
        Ensure(ReferenceEquals(provider.GetRequiredService<ISingleton2>(), provider.GetRequiredService<ISingleton2>()), "singleton");
        Ensure(ReferenceEquals(provider.GetRequiredService<ISingleton3>(), provider.GetRequiredService<ISingleton3>()), "singleton");
        Ensure(ReferenceEquals(provider.GetRequiredService<ISingleton4>(), provider.GetRequiredService<ISingleton4>()), "singleton");
        Ensure(ReferenceEquals(provider.GetRequiredService<ISingleton5>(), provider.GetRequiredService<ISingleton5>()), "singleton");

        // Transient: 毎回新規 / fresh every time
        Ensure(!ReferenceEquals(provider.GetRequiredService<ITransient1>(), provider.GetRequiredService<ITransient1>()), "transient");
        Ensure(!ReferenceEquals(provider.GetRequiredService<ITransient2>(), provider.GetRequiredService<ITransient2>()), "transient");
        Ensure(!ReferenceEquals(provider.GetRequiredService<ITransient3>(), provider.GetRequiredService<ITransient3>()), "transient");
        Ensure(!ReferenceEquals(provider.GetRequiredService<ITransient4>(), provider.GetRequiredService<ITransient4>()), "transient");
        Ensure(!ReferenceEquals(provider.GetRequiredService<ITransient5>(), provider.GetRequiredService<ITransient5>()), "transient");

        // Combined: transient 本体 + singleton 依存 / transient body with a singleton dependency
        Ensure(!ReferenceEquals(provider.GetRequiredService<Combined1>(), provider.GetRequiredService<Combined1>()), "combined");
        Ensure(ReferenceEquals(provider.GetRequiredService<Combined1>().Singleton, provider.GetRequiredService<ISingleton1>()), "combined dependency");
        Ensure(ReferenceEquals(provider.GetRequiredService<Combined5>().Singleton, provider.GetRequiredService<ISingleton5>()), "combined dependency");

        // Complex: グラフ結線 / graph wiring
        var complex = provider.GetRequiredService<Complex>();
        Ensure(!ReferenceEquals(complex, provider.GetRequiredService<Complex>()), "complex");
        Ensure(ReferenceEquals(complex.Singleton1, provider.GetRequiredService<ISingleton1>()), "complex dependency");
        Ensure(ReferenceEquals(complex.Combined2.Singleton, provider.GetRequiredService<ISingleton2>()), "complex dependency");

        // Generics: closed 型の解決 + transient の個別性 / closed type resolution and transient distinctness
        Ensure(provider.GetRequiredService<IGenericObject<string>>() is GenericObject<string>, "generics");
        Ensure(provider.GetRequiredService<IGenericObject<int>>() is GenericObject<int>, "generics");
        Ensure(
            !ReferenceEquals(provider.GetRequiredService<IGenericObject<string>>(), provider.GetRequiredService<IGenericObject<string>>()),
            "generics transient");

        // MultipleSingleton: 登録順 5 件 + 同一インスタンス / five in registration order, same instances
        var singletons1 = provider.GetServices<IMultipleSingletonService>().ToArray();
        var singletons2 = provider.GetServices<IMultipleSingletonService>().ToArray();
        Ensure(singletons1.Length == 5, "multiple singleton count");
        for (var i = 0; i < singletons1.Length; i++)
        {
            Ensure(ReferenceEquals(singletons1[i], singletons2[i]), "multiple singleton");
        }

        // MultipleTransient: 登録順 5 件 + 毎回新規 / five in registration order, fresh every time
        var transients1 = provider.GetServices<IMultipleTransientService>().ToArray();
        var transients2 = provider.GetServices<IMultipleTransientService>().ToArray();
        Ensure(transients1.Length == 5, "multiple transient count");
        for (var i = 0; i < transients1.Length; i++)
        {
            Ensure(!ReferenceEquals(transients1[i], transients2[i]), "multiple transient");
        }

        // Keyed: キーごとに対応する実装が解決され、同一キーは同一インスタンス / each key resolves its implementation and repeats share the instance
        Ensure(provider.GetRequiredKeyedService<IKeyedService>("key1") is KeyedService1, "keyed");
        Ensure(provider.GetRequiredKeyedService<IKeyedService>("key3") is KeyedService3, "keyed");
        Ensure(provider.GetRequiredKeyedService<IKeyedService>("key5") is KeyedService5, "keyed");
        Ensure(
            ReferenceEquals(provider.GetRequiredKeyedService<IKeyedService>("key1"), provider.GetRequiredKeyedService<IKeyedService>("key1")),
            "keyed singleton");

        // AspNet: スコープ内共有 + スコープ間分離 / shared within a scope, isolated across scopes
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        using var scope1 = scopeFactory.CreateScope();
        using var scope2 = scopeFactory.CreateScope();
        var controller = scope1.ServiceProvider.GetRequiredService<Controller>();
        var scoped1 = ((TransientService1)controller.TransientService1).ScopedService;
        var scoped2 = ((TransientService2)controller.TransientService2).ScopedService;
        Ensure(ReferenceEquals(scoped1, scoped2), "scoped shared in scope");
        var other = scope2.ServiceProvider.GetRequiredService<Controller>();
        Ensure(!ReferenceEquals(scoped1, ((TransientService1)other.TransientService1).ScopedService), "scoped distinct across scopes");

        // Scoped: スコープ内で同一、スコープ間で別 / same within a scope, distinct across scopes
        Ensure(
            ReferenceEquals(scope1.ServiceProvider.GetRequiredService<IScoped1>(), scope1.ServiceProvider.GetRequiredService<IScoped1>()),
            "scoped");
        Ensure(
            !ReferenceEquals(scope1.ServiceProvider.GetRequiredService<IScoped5>(), scope2.ServiceProvider.GetRequiredService<IScoped5>()),
            "scoped");
    }

    private static void Ensure(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Validation error of {name}.");
        }
    }
}
