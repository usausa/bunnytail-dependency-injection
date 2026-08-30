namespace BunnyTail.DependencyInjection.Generator.Models;

using SourceGenerateHelper;

internal sealed record ExternalScanResult(
    EquatableArray<CandidateModel> Candidates,
    EquatableArray<string> MissingAssemblies);
