namespace BunnyTail.DependencyInjection.Generator.Models;

using SourceGenerateHelper;

internal sealed record ClosedGenericScanResult(
    EquatableArray<FactoryModel> Factories,
    EquatableArray<ClosedGenericWarningModel> Warnings,
    EquatableArray<string> DefinitionKeys);
