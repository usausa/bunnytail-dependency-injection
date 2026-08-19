namespace BunnyTail.DependencyInjection.Generator.Models;

using SourceGenerateHelper;

// 登録済み open generic の閉型使用 (typeof(IRepo<Foo>) またはコンストラクタ/プロパティ依存)。
// 型引数はメタデータ名で保持し、パイプライン側でシンボルへ解決する
// Closed usage of a registered open generic (typeof(IRepo<Foo>) or a constructor/property dependency).
// Type arguments are kept as metadata names and resolved back to symbols on the pipeline side.
internal sealed record ClosedGenericUsageModel(
    string ServiceDefinitionKey,
    bool HasValueTypeArgument,
    EquatableArray<string> TypeArgumentMetadataNames,
    string FilePath,
    int SpanStart,
    LocationInfo? Location);
