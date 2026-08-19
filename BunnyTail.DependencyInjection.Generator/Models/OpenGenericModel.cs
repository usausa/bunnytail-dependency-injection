namespace BunnyTail.DependencyInjection.Generator.Models;

// open generic 定義登録 (typeof オーバーロード経由。例: AddTransient(typeof(IRepo<>), typeof(Repo<>)))
// Open generic definition registration through the typeof overload (e.g. AddTransient(typeof(IRepo<>), typeof(Repo<>))).
internal sealed record OpenGenericModel(
    string ServiceDefinitionKey,
    string ImplementationMetadataName,
    string Lifetime,
    string FilePath,
    int SpanStart);
