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

    public static IncrementalRunResult RunIncremental(string source, string addedSource) =>
        CreateRunner().WithTracking().RunIncremental(source, addedSource);
}
