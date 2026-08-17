namespace BunnyTail.Resolver.Diagnostics;

using System.Text;

using Microsoft.Extensions.DependencyInjection;

// 1 登録あたりの解決経路
// Resolution path of a single registration.
public enum ServiceFactoryStatus
{
    // 生成ファクトリが採用された (リフレクションなし)
    // A generated factory was adopted (reflection free).
    Generated,

    // 生成ファクトリが無い / 前提が崩れて棄却され、実行時経路 (ConstructorInvoker) になった。
    // [GenerateComponentFactory] の追加候補
    // No generated factory, or the assumptions were rejected, so the runtime path (ConstructorInvoker) is used.
    // These are the candidates for [GenerateComponentFactory].
    RuntimeFallback,

    // ファクトリ登録・インスタンス登録・特殊サービスなど、コンテナが型を構築しないもの (生成対象外)
    // Factory registrations, instance registrations and built-in services: the container does not construct the type, so nothing can be generated.
    NotApplicable,

    // コンパイル時に見える登録では解決できなかった (依存不足・循環など)
    // Could not be realized from the visible registrations (missing dependency, cycle, ...).
    Unresolvable
}

public sealed class ServiceFactoryReportEntry
{
    public Type ServiceType { get; }

    public Type? ImplementationType { get; }

    public object? ServiceKey { get; }

    public ServiceLifetime Lifetime { get; }

    public ServiceFactoryStatus Status { get; }

    // [GenerateComponentFactory] の対象にできるか。生成コードは実装型を直接 new するため、
    // アセンブリ外から見える型でなければ指定しても BTRS0004 になる
    // Whether the entry can be a [GenerateComponentFactory] target. The generated code news the implementation type
    // up directly, so a type not visible outside its assembly would only report BTRS0004.
    public bool CanGenerateFactory { get; }

    internal ServiceFactoryReportEntry(Type serviceType, Type? implementationType, object? serviceKey, ServiceLifetime lifetime, ServiceFactoryStatus status)
    {
        ServiceType = serviceType;
        ImplementationType = implementationType;
        ServiceKey = serviceKey;
        Lifetime = lifetime;
        Status = status;
        CanGenerateFactory = (implementationType is not null) && IsPubliclyVisible(implementationType);
    }

    // 生成コードは対象型を直接 new するため、アセンブリ外から見える型だけが候補になる
    // The generated code news the type up directly, so only types visible outside their assembly are candidates.
    private static bool IsPubliclyVisible(Type type)
    {
        while (type.IsNested)
        {
            if (!type.IsNestedPublic || (type.DeclaringType is null))
            {
                return false;
            }

            type = type.DeclaringType;
        }

        return type.IsPublic;
    }
}

// 開発時の診断。どの登録が生成ファクトリで解決され、どれが実行時経路に落ちているかを一覧する。
// 全エントリを実現するためリリース経路では使わない (インスタンスは生成されないが、テーブルは温まる)
// Development-time diagnostics listing which registrations resolve through generated factories and which fall back
// to the runtime path. It realizes every entry, so it is not meant for release paths (no instances are created,
// but the tables get warmed up).
public static class ServiceFactoryReportExtensions
{
    // 全登録を解決経路で分類する
    // Classifies every registration by its resolution path.
    public static IReadOnlyList<ServiceFactoryReportEntry> CreateFactoryReport(this ResolverServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.Registry.CreateFactoryReport();
    }

    // [GenerateComponentFactory] の追加候補だけを、そのまま貼り付けられる属性行として書き出す。
    // 実行時経路かつ生成可能なエントリが対象で、predicate を渡すとさらに絞り込める
    // (例: singleton は一度しか構築されないため除く、特定アセンブリだけに限る、など)。
    // formatter を渡すと 1 行の書式を差し替えられる。C# として正しい型名 (入れ子は '.'、構築済み
    // ジェネリックは山かっこ) が渡されるので、整形をやり直す必要はない
    // Writes the [GenerateComponentFactory] candidates as attribute lines ready to paste. Entries on the runtime
    // path that can actually be generated are considered, and a predicate narrows the set further (for example
    // excluding singletons, which are constructed once, or limiting it to one assembly). A formatter replaces the
    // per-line format; it receives a type name already valid in C# (nesting as '.', constructed generics in angle
    // brackets), so it never has to redo the formatting.
    public static string DescribeRuntimeFallbacks(
        this ResolverServiceProvider provider,
        Func<ServiceFactoryReportEntry, bool>? predicate = null,
        Func<ServiceFactoryReportEntry, string, string>? formatter = null)
    {
        var builder = new StringBuilder();
        var typeName = new StringBuilder();
        var written = new HashSet<Type>();
        foreach (var entry in provider.CreateFactoryReport())
        {
            if ((entry.Status != ServiceFactoryStatus.RuntimeFallback)
                || !entry.CanGenerateFactory
                || (entry.ImplementationType is null)
                || ((predicate is not null) && !predicate(entry))
                || !written.Add(entry.ImplementationType))
            {
                continue;
            }

            _ = typeName.Clear();
            AppendTypeName(typeName, entry.ImplementationType);

            _ = formatter is null
                ? builder.Append("[assembly: global::BunnyTail.Resolver.GenerateComponentFactory(typeof(global::").Append(typeName).AppendLine("))]")
                : builder.AppendLine(formatter(entry, typeName.ToString()));
        }

        return builder.ToString();
    }

    // C# の typeof に書ける形へ整形する。メタデータ名の入れ子区切り ('+') と構築済みジェネリックの
    // アセンブリ修飾形はそのままでは書けないため、名前空間 + 型引数の山かっこ形へ組み立て直す
    // Formats a name usable in a C# typeof. Metadata names use '+' for nesting and an assembly qualified form for
    // constructed generics, neither of which is valid, so the namespace and angle bracket arguments are rebuilt.
    private static void AppendTypeName(StringBuilder builder, Type type)
    {
        if (type.IsNested && (type.DeclaringType is not null))
        {
            AppendTypeName(builder, type.DeclaringType);
            _ = builder.Append('.');
        }
        else if (!String.IsNullOrEmpty(type.Namespace))
        {
            _ = builder.Append(type.Namespace).Append('.');
        }

        var name = type.Name;
        var index = name.IndexOf('`', StringComparison.Ordinal);
        _ = builder.Append(index >= 0 ? name.AsSpan(0, index) : name.AsSpan());

        if (!type.IsGenericType)
        {
            return;
        }

        var arguments = type.GetGenericArguments();
        _ = builder.Append('<');
        for (var i = 0; i < arguments.Length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            _ = builder.Append("global::");
            AppendTypeName(builder, arguments[i]);
        }

        _ = builder.Append('>');
    }
}
