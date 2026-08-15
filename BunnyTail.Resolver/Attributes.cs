namespace BunnyTail.Resolver;

// 属性ベース登録 (SPEC 3.1)。ジェネレータが収集して登録メソッド+生成ファクトリを出力する

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class SingletonAttribute : Attribute
{
    public Type? As { get; set; }

    public object? Key { get; set; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ScopedAttribute : Attribute
{
    public Type? As { get; set; }

    public object? Key { get; set; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class TransientAttribute : Attribute
{
    public Type? As { get; set; }

    public object? Key { get; set; }
}

// プロパティインジェクション (SPEC 3.4)。インスタンス生成後に注入される
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class InjectAttribute : Attribute
{
}

// 命名規約ベース登録 (SPEC 3.3)。partial 拡張メソッドに付与すると、
// クラス名が正規表現にマッチするコンポーネントの登録コードが本体として生成される
public enum Lifetime
{
    Transient = 0,
    Singleton = 1,
    Scoped = 2,
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ComponentRegistrationAttribute : Attribute
{
    public Lifetime Lifetime { get; }

    public string Pattern { get; }

    public string? Namespace { get; set; }

    public ComponentRegistrationAttribute(
        Lifetime lifetime,
        [System.Diagnostics.CodeAnalysis.StringSyntax(System.Diagnostics.CodeAnalysis.StringSyntaxAttribute.Regex)] string pattern)
    {
        Lifetime = lifetime;
        Pattern = pattern;
    }
}
