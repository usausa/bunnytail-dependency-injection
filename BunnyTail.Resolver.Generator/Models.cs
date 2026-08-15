namespace BunnyTail.Resolver.Generator;

using SourceGenerateHelper;

// パイプラインに流す value-equatable モデル (Symbol/SyntaxNode は持たない)

// 依存の解決方法
internal static class DependencyKinds
{
    public const int Service = 0;        // 非 keyed サービス解決
    public const int ServiceKey = 1;     // [ServiceKey] : 解決中のキーを注入
    public const int KeyedExplicit = 2;  // [FromKeyedServices(key)] : 明示キー
    public const int KeyedInherit = 3;   // [FromKeyedServices] : キー継承
}

internal sealed record ParameterModel(
    string TypeName,
    int Kind,
    string? KeyLiteral,
    bool InCompilation);

internal sealed record PropertyModel(
    string Name,
    string TypeName,
    int Kind,
    string? KeyLiteral,
    bool InCompilation);

// 生成ファクトリの情報 (属性コンポーネント / Add* 収集 / 規約マッチで共通)
internal sealed record FactoryModel(
    string ImplementationType,
    EquatableArray<ParameterModel> Parameters,
    EquatableArray<PropertyModel> InjectProperties,
    bool EligibleUnkeyed,
    bool EligibleKeyed,
    bool AmbiguousConstructor);

// 属性 ([Singleton] 等) 付きコンポーネント
internal sealed record ComponentModel(
    FactoryModel Factory,
    string Lifetime,
    string? AsType,
    string? KeyLiteral,
    EquatableArray<string> Interfaces,
    string FilePath,
    int SpanStart,
    LocationInfo? Location);

// Add*/TryAdd* 呼び出しから収集した実装型
internal sealed record CollectedModel(
    FactoryModel Factory,
    string ServiceType,
    string Lifetime,
    string FilePath,
    int SpanStart);

// 規約マッチの候補クラス (アセンブリ内の具象クラス全て)
internal sealed record CandidateModel(
    string Name,
    string Namespace,
    FactoryModel Factory,
    EquatableArray<string> Interfaces,
    string FilePath,
    int SpanStart);

// [ComponentRegistration] のパターン指定
internal sealed record PatternModel(
    string Lifetime,
    string Pattern,
    string? Namespace);

// [ComponentRegistration] 付き partial メソッド
internal sealed record MethodModel(
    string? Namespace,
    string ClassName,
    string MethodName,
    string Accessibility,
    EquatableArray<PatternModel> Patterns,
    LocationInfo? Location);
