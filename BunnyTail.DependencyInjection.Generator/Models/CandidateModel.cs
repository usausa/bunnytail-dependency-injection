namespace BunnyTail.DependencyInjection.Generator.Models;

using SourceGenerateHelper;

internal sealed record CandidateModel(
    string Namespace,
    string Name,
    FactoryModel Factory,
    string? Assembly,
    EquatableArray<string> Interfaces,
    string FilePath,
    int SpanStart);
