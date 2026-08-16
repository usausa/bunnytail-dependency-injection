namespace Example;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// 属性を付けない普通のクラス群。標準の Add* 呼び出しで登録し、ジェネレータが呼び出しを収集して
// リフレクションレスなファクトリを生成する (ユーザーコードの書き換えは不要)
// Plain classes without attributes. They are registered through the standard Add* calls, and the generator
// collects those calls to emit reflection-free factories without any change to user code.
internal interface IManualService
{
    string Describe();
}

internal sealed class ManualService : IManualService
{
    public string Describe() => "manual singleton";
}

internal sealed class ManualScopedService
{
    public Guid Id { get; } = Guid.NewGuid();
}

// open generic 登録。コード中に現れる閉型 (下の ManualConsumer 依存 = int) は閉じたファクトリが生成され、
// 値型引数でも NativeAOT で解決できる
// Open generic registration. Closed forms appearing in code (here int, through the ManualConsumer dependency)
// get closed factories generated, so even value type arguments resolve on NativeAOT.
internal interface IManualBox<T>
{
    T? Value { get; }
}

internal sealed class ManualBox<T> : IManualBox<T>
{
    public T? Value => default;
}

internal interface IManualPlugin
{
    string Name { get; }
}

internal sealed class ManualPluginA : IManualPlugin
{
    public string Name => "A";
}

internal sealed class ManualPluginB : IManualPlugin
{
    public string Name => "B";
}

internal interface IManualKeyed
{
    string Kind { get; }
}

internal sealed class PrimaryManualKeyed : IManualKeyed
{
    public string Kind => "primary";
}

internal sealed class ManualConsumer
{
    public ManualConsumer(IManualService service, ManualScopedService scoped, IManualBox<int> box)
    {
        Service = service;
        Scoped = scoped;
        Box = box;
    }

    public IManualService Service { get; }

    public ManualScopedService Scoped { get; }

    public IManualBox<int> Box { get; }
}

internal static class ManualRegistrations
{
    // 生成ファクトリが作られる形: ジェネリック / typeof / keyed / ServiceDescriptor / TryAddEnumerable
    // Shapes that produce generated factories: generic, typeof, keyed, ServiceDescriptor and TryAddEnumerable.
    public static IServiceCollection AddManualServices(this IServiceCollection services)
    {
        services.AddSingleton<IManualService, ManualService>();
        services.AddScoped<ManualScopedService>();
        services.AddTransient(typeof(IManualBox<>), typeof(ManualBox<>));
        services.AddTransient<ManualConsumer>();
        services.AddKeyedSingleton<IManualKeyed, PrimaryManualKeyed>("primary");
        services.TryAddEnumerable(ServiceDescriptor.Transient<IManualPlugin, ManualPluginA>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IManualPlugin, ManualPluginB>());

        // ファクトリ登録はコンテナが型を生成しないため収集対象外 (実行時経路で解決される)
        // Factory registrations are not collected because the container does not instantiate the type (resolved on the runtime path).
        services.AddSingleton(static _ => new ManualOptions("from factory"));
        return services;
    }
}

internal sealed class ManualOptions
{
    public ManualOptions(string value)
    {
        Value = value;
    }

    public string Value { get; }
}
