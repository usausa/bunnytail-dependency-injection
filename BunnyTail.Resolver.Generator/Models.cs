namespace BunnyTail.Resolver.Generator;

using SourceGenerateHelper;

// パイプラインに流す value-equatable モデル (Symbol/SyntaxNode は持たない)
// Value-equatable models flowing through the pipeline (no Symbol/SyntaxNode references).

// 依存の解決方法
// How a dependency is resolved.
internal static class DependencyKinds
{
    public const int Service = 0;        // 非 keyed サービス解決 / non-keyed service resolution
    public const int ServiceKey = 1;     // [ServiceKey] : 解決中のキーを注入 / injects the key being resolved
    public const int KeyedExplicit = 2;  // [FromKeyedServices(key)] : 明示キー / explicit key
    public const int KeyedInherit = 3;   // [FromKeyedServices] : キー継承 / inherits the key
}

internal sealed record ParameterModel(
    string TypeName,
    int Kind,
    string? KeyLiteral,
    bool InCompilation,
    bool IsValueType);

internal sealed record PropertyModel(
    string Name,
    string TypeName,
    int Kind,
    string? KeyLiteral,
    bool InCompilation,
    bool IsValueType);

// 生成ファクトリの情報 (属性コンポーネント / Add* 収集 / 規約マッチで共通)
// Generated factory information (shared by attribute components, Add* collection and convention matches).
internal sealed record FactoryModel(
    string ImplementationType,
    EquatableArray<ParameterModel> Parameters,
    EquatableArray<PropertyModel> InjectProperties,
    bool EligibleUnkeyed,
    bool EligibleKeyed,
    bool AmbiguousConstructor,
    bool Disposable,
    string? PostConstruct,
    bool InitializableInterface,
    bool InvalidPostConstruct,
    bool ConflictingPostConstruct)
{
    // 初期化コールバックを持つか (PostConstruct 指定 or IInitializable 実装)
    // Whether the type carries an initialization callback (PostConstruct specification or IInitializable).
    public bool HasInitializer => (PostConstruct is not null) || InitializableInterface;
}

// 属性 ([Singleton] 等) 付きコンポーネント
// Component annotated with [Singleton] and friends.
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
// Implementation type collected from Add*/TryAdd* invocations.
internal sealed record CollectedModel(
    FactoryModel Factory,
    string ServiceType,
    string Lifetime,
    string FilePath,
    int SpanStart,
    int Kind);

// CollectedModel.Kind の値。Direct はインライン/enumerable の前提に参加し、FactoryOnly (TryAddEnumerable 由来)
// はファクトリ生成のみ + 前提の毒化、Keyed は keyed ファクトリ生成のみ
// Values of CollectedModel.Kind. Direct participates in inline and enumerable assumptions; FactoryOnly
// (from TryAddEnumerable) only generates factories and poisons assumptions; Keyed only generates keyed factories.
internal static class CollectedKinds
{
    public const int Direct = 0;
    public const int FactoryOnly = 1;
    public const int Keyed = 2;
}

// 規約マッチの候補クラス (アセンブリ内の具象クラス全て)
// Convention match candidate (every concrete class in the assembly).
internal sealed record CandidateModel(
    string Name,
    string Namespace,
    FactoryModel Factory,
    EquatableArray<string> Interfaces,
    string FilePath,
    int SpanStart,
    string? Assembly);

// Assembly 指定つき規約パターンの外部走査要求と結果 (値等価: 変化した時だけ下流が再実行される)
// External scan request and result for assembly-scoped convention patterns (value-equatable so downstream reruns only on change).
internal sealed record ExternalRequest(
    string Assembly,
    string Pattern,
    string? Namespace);

internal sealed record ExternalScanResult(
    EquatableArray<CandidateModel> Candidates,
    EquatableArray<string> MissingAssemblies);

// open generic 定義登録 (typeof オーバーロード経由。例: AddTransient(typeof(IRepo<>), typeof(Repo<>)))
// Open generic definition registration through the typeof overload (e.g. AddTransient(typeof(IRepo<>), typeof(Repo<>))).
internal sealed record OpenGenericModel(
    string ServiceDefinitionKey,
    string ImplementationMetadataName,
    string Lifetime,
    string FilePath,
    int SpanStart);

// 登録済み open generic の閉型使用 (typeof(IRepo<Foo>) またはコンストラクタ/プロパティ依存)。
// 型引数はメタデータ名で保持し、パイプライン側でシンボルへ解決する
// Closed usage of a registered open generic (typeof(IRepo<Foo>) or a constructor/property dependency).
// Type arguments are kept as metadata names and resolved back to symbols on the pipeline side.
internal sealed record ClosedGenericUsageModel(
    string ServiceDefinitionKey,
    EquatableArray<string> TypeArgumentMetadataNames,
    bool HasValueTypeArgument,
    string FilePath,
    int SpanStart,
    LocationInfo? Location);

// closed generic 発見の結果。生成できた factory と、値型引数のまま実行時経路に残る使用への警告 (BTRS0010)
// Result of closed generic discovery: generated factories plus warnings for usages left on the runtime path
// with value type arguments (BTRS0010).
internal sealed record ClosedGenericScanResult(
    EquatableArray<FactoryModel> Factories,
    EquatableArray<ClosedGenericWarningModel> Warnings,
    EquatableArray<string> DefinitionKeys);

internal sealed record ClosedGenericWarningModel(
    string DisplayName,
    LocationInfo? Location);

// [ComponentRegistration] のパターン指定
// Pattern specification of [ComponentRegistration].
internal sealed record PatternModel(
    string Lifetime,
    string Pattern,
    string? Namespace,
    string? Assembly);

// [ComponentRegistration] 付き partial メソッド
// Partial method annotated with [ComponentRegistration].
internal sealed record MethodModel(
    string? Namespace,
    string ClassName,
    string MethodName,
    string Accessibility,
    EquatableArray<PatternModel> Patterns,
    LocationInfo? Location);
