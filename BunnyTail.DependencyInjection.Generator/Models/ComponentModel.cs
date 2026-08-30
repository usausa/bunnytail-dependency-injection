namespace BunnyTail.DependencyInjection.Generator.Models;

using SourceGenerateHelper;

internal sealed record ComponentModel(
    FactoryModel Factory,
    string Lifetime,
    string? AsType,
    string? KeyLiteral,
    string? Tracking,
    bool WithInterfaces,
    EquatableArray<string> Interfaces,
    string FilePath,
    int SpanStart,
    LocationInfo? Location);
