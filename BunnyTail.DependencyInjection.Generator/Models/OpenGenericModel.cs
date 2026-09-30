namespace BunnyTail.DependencyInjection.Generator.Models;

internal sealed record OpenGenericModel(
    string ServiceDefinitionKey,
    string ServiceName,
    int ServiceArity,
    string ImplementationMetadataName,
    string FilePath,
    int SpanStart);
