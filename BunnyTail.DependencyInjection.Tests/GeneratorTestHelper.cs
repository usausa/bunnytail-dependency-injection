namespace BunnyTail.DependencyInjection.Tests;

using BunnyTail.DependencyInjection;
using BunnyTail.DependencyInjection.Generator;

using Microsoft.Extensions.DependencyInjection;

using SourceGenerateHelper.Testing;

internal static class GeneratorTestHelper
{
    public static GeneratorTestRunner CreateRunner() =>
        GeneratorTestRunner.For<DependencyInjectionGenerator>()
            .WithAssemblyName("BunnyTail.DependencyInjection.Tests")
            .WithReference(typeof(SingletonAttribute).Assembly)
            .WithReference(typeof(IServiceCollection).Assembly);

    // The GeneratedComponents generated for the default assembly name conflicts with that of this test assembly (CS0436)
    public static IReadOnlyList<string> GetProblemIds(string source) =>
        [.. CreateRunner().WithAssemblyName("Demo").GetProblems(source).Select(static x => x.Id)];

    public static IncrementalRunResult RunIncremental(string source, string addedSource) =>
        CreateRunner().WithTracking().RunIncremental(source, addedSource);
}
