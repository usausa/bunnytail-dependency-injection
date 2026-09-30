namespace BunnyTail.DependencyInjection.Tests;

using SourceGenerateHelper.Testing;

public sealed class PipelineCacheTests
{
    private const string Source =
        """
        using BunnyTail.DependencyInjection;

        namespace Demo;

        [Singleton]
        public sealed class CachedComponent;
        """;

    private const string UnrelatedSource =
        """
        // unrelated edit
        """;

    private const string AddedTargetSource =
        """
        using BunnyTail.DependencyInjection;

        namespace Demo;

        [Transient]
        public sealed class AddedComponent;
        """;

    private static IncrementalRunResult RunIncremental(string addedSource) =>
        GeneratorTestHelper.RunIncremental(Source, addedSource);

    // ------------------------------------------------------------
    // Cache
    // ------------------------------------------------------------

    [Fact]
    public void UnrelatedEditKeepsModelCached()
    {
        // Arrange & Act
        var result = RunIncremental(UnrelatedSource);

        // Assert
        Assert.Equal(result.FirstGeneratedText, result.SecondGeneratedText);
        Assert.NotEmpty(result.OutputReasons);
        Assert.DoesNotContain(result.OutputReasons, static x => x.IsChanged());
    }

    [Fact]
    public void TargetEditRebuildsModel()
    {
        // Arrange & Act
        var result = RunIncremental(AddedTargetSource);

        // Assert
        Assert.Contains(result.OutputReasons, static x => x.IsChanged());
    }

    [Fact]
    public void UnrelatedEditKeepsReferenceScanCached()
    {
        // Arrange & Act
        var result = RunIncremental(UnrelatedSource);

        // Assert
        Assert.NotEmpty(result.StepReasons("ReferencedModules"));
        Assert.DoesNotContain(result.StepReasons("ReferencedModules"), static x => x.IsChanged());
    }

    [Fact]
    public void UnrelatedEditKeepsCandidatesUnchanged()
    {
        // Arrange
        const string source =
            """
            using BunnyTail.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public sealed class FooService;

            public static partial class Registrations
            {
                [ComponentRegistration(Lifetime.Singleton, "Service$")]
                public static partial IServiceCollection AddServices(this IServiceCollection services);
            }
            """;

        // Act
        var result = GeneratorTestHelper.RunIncremental(source, UnrelatedSource);

        // Assert
        Assert.NotEmpty(result.StepReasons("Candidates"));
        Assert.DoesNotContain(result.StepReasons("Candidates"), static x => x.IsChanged());
        Assert.Equal(result.FirstGeneratedText, result.SecondGeneratedText);
    }
}
