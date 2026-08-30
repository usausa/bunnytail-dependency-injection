namespace BunnyTail.DependencyInjection.Generator.Models;

using SourceGenerateHelper;

internal sealed record ClosedGenericUsageModel(
    string ServiceDefinitionKey,
    bool HasValueTypeArgument,
    EquatableArray<string> TypeArgumentMetadataNames,
    string FilePath,
    int SpanStart,
    LocationInfo? Location);
