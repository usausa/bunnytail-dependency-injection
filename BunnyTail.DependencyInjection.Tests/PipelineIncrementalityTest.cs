namespace BunnyTail.DependencyInjection.Tests;

using BunnyTail.DependencyInjection;
using BunnyTail.DependencyInjection.Generator;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

public sealed class PipelineIncrementalityTest
{
    private const string ComponentSource = """
        using BunnyTail.DependencyInjection;

        namespace Demo;

        [Singleton]
        public sealed class CachedComponent;

        public static partial class Registrations
        {
            [ComponentRegistration(Lifetime.Transient, "^MultiLeafA$", Assembly = "BunnyTail.DependencyInjection.Tests")]
            public static partial global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddExternal(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services);
        }
        """;

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] trees)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static x => !x.IsDynamic && !string.IsNullOrEmpty(x.Location))
            .Select(static x => x.Location)
            .Append(typeof(SingletonAttribute).Assembly.Location)
            .Append(typeof(IServiceCollection).Assembly.Location)
            .Append(typeof(PipelineIncrementalityTest).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static x => (MetadataReference)MetadataReference.CreateFromFile(x))
            .ToArray();

        return CSharpCompilation.Create(
            "Demo",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static CSharpGeneratorDriver CreateDriver() =>
        CSharpGeneratorDriver.Create(
            [new DependencyInjectionGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static IncrementalStepRunReason[] OutputReasons(GeneratorDriver driver) =>
        driver.GetRunResult().Results[0].TrackedOutputSteps
            .SelectMany(static x => x.Value)
            .SelectMany(static x => x.Outputs)
            .Select(static x => x.Reason)
            .ToArray();

    [Fact]
    public void UnrelatedEditKeepsOutputCached()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var componentTree = CSharpSyntaxTree.ParseText(
            ComponentSource,
            parseOptions,
            path: "Components.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        var bodyTree = CSharpSyntaxTree.ParseText(
            """
            namespace Demo;

            public static class Runner
            {
                public static int Run() => 1;
            }
            """,
            parseOptions,
            path: "Runner.cs",
            cancellationToken: TestContext.Current.CancellationToken);

        var compilation = CreateCompilation(componentTree, bodyTree);
        var driver = CreateDriver().RunGenerators(compilation, TestContext.Current.CancellationToken);

        // Method body edit that does not change any model
        var editedBody = CSharpSyntaxTree.ParseText(
            """
            namespace Demo;

            public static class Runner
            {
                public static int Run() => 1 + 1;
            }
            """,
            parseOptions,
            path: "Runner.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(bodyTree, editedBody), TestContext.Current.CancellationToken);

        // Assembly scoped external scan is active, but every output stays cached and Execute is not rerun
        var reasons = OutputReasons(driver);
        Assert.NotEmpty(reasons);
        Assert.All(reasons, static x => Assert.Equal(IncrementalStepRunReason.Cached, x));
    }

    [Fact]
    public void ComponentEditRegeneratesOutput()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var componentTree = CSharpSyntaxTree.ParseText(
            ComponentSource,
            parseOptions,
            path: "Components.cs",
            cancellationToken: TestContext.Current.CancellationToken);

        var compilation = CreateCompilation(componentTree);
        var driver = CreateDriver().RunGenerators(compilation, TestContext.Current.CancellationToken);

        // Adding a component (a model change) must regenerate the output
        var editedTree = CSharpSyntaxTree.ParseText(
            ComponentSource +
            """

            namespace Demo
            {
                [BunnyTail.DependencyInjection.Transient]
                public sealed class AddedComponent;
            }
            """,
            parseOptions,
            path: "Components.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(componentTree, editedTree), TestContext.Current.CancellationToken);

        var reasons = OutputReasons(driver);
        Assert.NotEmpty(reasons);
        Assert.Contains(reasons, static x => x != IncrementalStepRunReason.Cached);
    }
}
