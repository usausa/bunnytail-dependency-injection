namespace BunnyTail.Resolver.Tests;

using BunnyTail.Resolver;
using BunnyTail.Resolver.Generator;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

// パイプラインの増分性検証。モデルに影響しない編集では出力段 (Execute) が Cached のまま
// = 出力テキストの再構築が走らないことを、ステップ追跡つきドライバで機械的に確認する
// Incremental pipeline verification. With step tracking enabled, edits that do not affect the models must leave
// the output stage (Execute) cached, proving the output text is not rebuilt.
public sealed class PipelineIncrementalityTest
{
    private const string ComponentSource = """
        using BunnyTail.Resolver;

        namespace Demo;

        [Singleton]
        public sealed class CachedComponent;

        public static partial class Registrations
        {
            [ComponentRegistration(Lifetime.Transient, "^MultiLeafA$", Assembly = "BunnyTail.Resolver.Tests")]
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
            [new ResolverGenerator().AsSourceGenerator()],
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
        var componentTree = CSharpSyntaxTree.ParseText(ComponentSource, parseOptions, path: "Components.cs");
        var bodyTree = CSharpSyntaxTree.ParseText(
            """
            namespace Demo;

            public static class Runner
            {
                public static int Run() => 1;
            }
            """,
            parseOptions,
            path: "Runner.cs");

        var compilation = CreateCompilation(componentTree, bodyTree);
        var driver = CreateDriver().RunGenerators(compilation);

        // メソッド本体だけの編集 (モデル不変) / a method body only edit that leaves every model unchanged
        var editedBody = CSharpSyntaxTree.ParseText(
            """
            namespace Demo;

            public static class Runner
            {
                public static int Run() => 1 + 1;
            }
            """,
            parseOptions,
            path: "Runner.cs");
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(bodyTree, editedBody));

        // Assembly 指定の外部走査を含む状態でも、出力段はすべて Cached = Execute は再実行されない
        // Even with the assembly-scoped external scan active, every output stays cached and Execute is not rerun.
        var reasons = OutputReasons(driver);
        Assert.NotEmpty(reasons);
        Assert.All(reasons, static x => Assert.Equal(IncrementalStepRunReason.Cached, x));
    }

    [Fact]
    public void ComponentEditRegeneratesOutput()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var componentTree = CSharpSyntaxTree.ParseText(ComponentSource, parseOptions, path: "Components.cs");

        var compilation = CreateCompilation(componentTree);
        var driver = CreateDriver().RunGenerators(compilation);

        // コンポーネント追加 (モデル変化) では出力が再生成されること (追跡が空振りしていない対照)
        // Adding a component (a model change) must regenerate the output, proving the tracking is not vacuous.
        var editedTree = CSharpSyntaxTree.ParseText(
            ComponentSource +
            """

            namespace Demo
            {
                [BunnyTail.Resolver.Transient]
                public sealed class AddedComponent;
            }
            """,
            parseOptions,
            path: "Components.cs");
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(componentTree, editedTree));

        var reasons = OutputReasons(driver);
        Assert.NotEmpty(reasons);
        Assert.Contains(reasons, static x => x != IncrementalStepRunReason.Cached);
    }
}
