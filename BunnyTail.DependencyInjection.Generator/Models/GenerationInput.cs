namespace BunnyTail.DependencyInjection.Generator.Models;

using System.Collections.Immutable;

using SourceGenerateHelper;

internal sealed record GenerationInput(
    ImmutableArray<Result<ComponentModel>> Singletons,
    ImmutableArray<Result<ComponentModel>> Scopeds,
    ImmutableArray<Result<ComponentModel>> Transients,
    ImmutableArray<CollectedModel> Collected,
    ImmutableArray<Result<MethodModel>> Methods,
    EquatableArray<CandidateModel> Candidates,
    ImmutableArray<Result<FactoryModel>> GenerateComponentFactoryTargets,
    ExternalScanResult ExternalScan,
    ClosedGenericScanResult ClosedGenerics,
    string AssemblyName,
    EquatableArray<string> ReferencedModules,
    EquatableArray<string> IgnoreInterfaces);
