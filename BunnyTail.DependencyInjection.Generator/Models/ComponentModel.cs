namespace BunnyTail.DependencyInjection.Generator.Models;

using SourceGenerateHelper;

// 属性 ([Singleton] 等) 付きコンポーネント
// Component annotated with [Singleton] and friends.
internal sealed record ComponentModel(
    FactoryModel Factory,
    string Lifetime,
    string? AsType,
    string? KeyLiteral,
    string? Tracking,
    EquatableArray<string> Interfaces,
    string FilePath,
    int SpanStart,
    LocationInfo? Location);
