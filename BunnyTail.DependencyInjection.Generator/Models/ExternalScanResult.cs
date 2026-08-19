namespace BunnyTail.DependencyInjection.Generator.Models;

using SourceGenerateHelper;

// Assembly 指定つき規約パターンの外部走査結果
// External scan result for assembly-scoped convention patterns.
internal sealed record ExternalScanResult(
    EquatableArray<CandidateModel> Candidates,
    EquatableArray<string> MissingAssemblies);
