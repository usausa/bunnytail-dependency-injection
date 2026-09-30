namespace BunnyTail.DependencyInjection.Generator.Models;

internal sealed record ClosedGenericUsageModel(
    string Name,
    int Arity,
    string FilePath,
    int SpanStart,
    int SpanLength);
