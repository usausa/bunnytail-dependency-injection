namespace BunnyTail.Resolver;

// 属性ベース登録のマーカー。ジェネレータが収集し、登録メソッドと生成ファクトリを出力する
// Markers for attribute based registration. The generator collects them and emits the registration method and factories.

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class SingletonAttribute : Attribute
{
    public Type? As { get; set; }

    public object? Key { get; set; }

    public string? PostConstruct { get; set; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ScopedAttribute : Attribute
{
    public Type? As { get; set; }

    public object? Key { get; set; }

    public string? PostConstruct { get; set; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class TransientAttribute : Attribute
{
    public Type? As { get; set; }

    public object? Key { get; set; }

    public string? PostConstruct { get; set; }
}

// プロパティインジェクションのマーカー。インスタンス生成後に注入される
// Marker for property injection. Injected after the instance is constructed.
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class InjectAttribute : Attribute
{
}

// 命名規約ベース登録。partial 拡張メソッドに付与すると、クラス名が正規表現にマッチするコンポーネントの登録コードが本体として生成される
// Convention based registration. Applied to a partial extension method, the generator emits the method body registering components whose class names match the regex pattern.
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

// 生成コードが埋め込むアセンブリレベルのマーカー。属性コンポーネントを持つアセンブリの生成モジュール型
// (GeneratedComponents) を示し、参照側のジェネレータが AddAllComponents の集約に使う
// Assembly level marker embedded by generated code. Points to the generated module type (GeneratedComponents)
// of an assembly containing attribute components; referencing projects' generators use it to build AddAllComponents.
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ComponentModuleAttribute : Attribute
{
    public Type ModuleType { get; }

    public ComponentModuleAttribute(Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(moduleType);
        ModuleType = moduleType;
    }
}
