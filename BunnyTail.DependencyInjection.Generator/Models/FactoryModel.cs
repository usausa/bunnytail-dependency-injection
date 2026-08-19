namespace BunnyTail.DependencyInjection.Generator.Models;

using SourceGenerateHelper;

// 生成ファクトリの情報 (属性コンポーネント / Add* 収集 / 規約マッチで共通)
// Generated factory information (shared by attribute components, Add* collection and convention matches).
internal sealed record FactoryModel(
    string ImplementationType,
    bool EligibleUnkeyed,
    bool EligibleKeyed,
    bool AmbiguousConstructor,
    bool Disposable,
    string? PostConstruct,
    bool InitializableInterface,
    bool InvalidPostConstruct,
    bool ConflictingPostConstruct,
    EquatableArray<ParameterModel> Parameters,
    EquatableArray<PropertyModel> InjectProperties)
{
    // 初期化コールバックを持つか (PostConstruct 指定 or IInitializable 実装)
    // Whether the type carries an initialization callback (PostConstruct specification or IInitializable).
    public bool HasInitializer => (PostConstruct is not null) || InitializableInterface;
}
