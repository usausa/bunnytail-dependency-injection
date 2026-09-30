namespace BunnyTail.DependencyInjection.Generator.Models;

internal sealed record CollectedModel(
    FactoryModel? Factory,
    string ServiceType,
    string ServiceTypeName,
    string? Lifetime,
    int Kind,
    string FilePath,
    int SpanStart);
