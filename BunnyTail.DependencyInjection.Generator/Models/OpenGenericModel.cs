namespace BunnyTail.DependencyInjection.Generator.Models;

internal sealed record OpenGenericModel(
    string ServiceDefinitionKey,
    string ImplementationMetadataName,
    string FilePath,
    int SpanStart);
