namespace BunnyTail.DependencyInjection.Generator.Models;

using SourceGenerateHelper;

// closed generic 発見の結果。生成できた factory と、値型引数のまま実行時経路に残る使用への警告 (BTDI0011)
// Result of closed generic discovery: generated factories plus warnings for usages left on the runtime path
// with value type arguments (BTDI0011).
internal sealed record ClosedGenericScanResult(
    EquatableArray<FactoryModel> Factories,
    EquatableArray<ClosedGenericWarningModel> Warnings,
    EquatableArray<string> DefinitionKeys);
