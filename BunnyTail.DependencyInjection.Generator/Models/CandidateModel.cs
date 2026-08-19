namespace BunnyTail.DependencyInjection.Generator.Models;

using SourceGenerateHelper;

// 規約マッチの候補クラス (アセンブリ内の具象クラス全て)
// Convention match candidate (every concrete class in the assembly).
internal sealed record CandidateModel(
    string Namespace,
    string Name,
    FactoryModel Factory,
    string? Assembly,
    EquatableArray<string> Interfaces,
    string FilePath,
    int SpanStart);
