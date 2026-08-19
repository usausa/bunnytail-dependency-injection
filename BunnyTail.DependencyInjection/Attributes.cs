namespace BunnyTail.DependencyInjection;

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
[AttributeUsage(AttributeTargets.Property)]
public sealed class InjectAttribute : Attribute
{
}

// 命名規約ベース登録。partial 拡張メソッドに付与すると、クラス名が正規表現にマッチするコンポーネントの登録コードが本体として生成される
// Convention based registration. Applied to a partial extension method, the generator emits the method body registering components whose class names match the regex pattern.
public enum Lifetime
{
    Transient = 0,
    Singleton = 1,
    Scoped = 2
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ComponentRegistrationAttribute : Attribute
{
    public Lifetime Lifetime { get; }

    public string Pattern { get; }

    public string? Namespace { get; set; }

    // 指定した参照アセンブリのメタデータから候補を走査する (省略時は自コンパイル)
    // Scans candidates from the metadata of the named referenced assembly (defaults to the current compilation).
    public string? Assembly { get; set; }

    public ComponentRegistrationAttribute(
        Lifetime lifetime,
        [System.Diagnostics.CodeAnalysis.StringSyntax(System.Diagnostics.CodeAnalysis.StringSyntaxAttribute.Regex)] string pattern)
    {
        Lifetime = lifetime;
        Pattern = pattern;
    }
}

// 生成コードが埋め込むアセンブリレベルのマーカー。属性コンポーネントを持つアセンブリの生成モジュール型
// (GeneratedComponents) を示し、参照側のジェネレータが AddAllGeneratedComponents の集約に使う
// Assembly level marker embedded by generated code. Points to the generated module type (GeneratedComponents)
// of an assembly containing attribute components; referencing projects' generators use it to build AddAllGeneratedComponents.
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

// 登録を伴わないファクトリ生成の指示。自分で制御できないライブラリの型 (登録はそのライブラリの拡張メソッドが行う)
// に対して、リフレクションレスな生成ファクトリだけを用意させる。対象は public にアクセスできる具象クラスに限る
// Requests factory generation without registration. For types of libraries you do not control (the library's own
// extension method performs the registration), this prepares the reflection-free factory only.
// Only publicly accessible concrete classes are eligible.
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class GenerateComponentFactoryAttribute : Attribute
{
    public Type ImplementationType { get; }

    // 生成後に呼び出すメソッド名。属性を付けられない型に初期化フックを与える。
    // 生成経路・実行時経路のどちらで解決されても呼ばれる
    // Name of a method invoked after construction, giving an initialization hook to types you cannot annotate.
    // It runs whichever path resolves the type, generated or runtime.
    public string? PostConstruct { get; set; }

    public GenerateComponentFactoryAttribute(Type implementationType)
    {
        ArgumentNullException.ThrowIfNull(implementationType);
        ImplementationType = implementationType;
    }
}
