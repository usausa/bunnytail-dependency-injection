namespace BunnyTail.DependencyInjection.Generator.Models;

using SourceGenerateHelper;

internal sealed record CandidateModel(
    string Namespace,
    string Name,
    FactoryModel Factory,
    string? Assembly,
    EquatableArray<TypeNameModel> Interfaces,
    bool IsReferable,
    string FilePath,
    int SpanStart);
