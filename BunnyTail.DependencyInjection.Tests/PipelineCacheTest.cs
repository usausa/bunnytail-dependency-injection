namespace BunnyTail.DependencyInjection.Tests;

using SourceGenerateHelper.Testing;

using Xunit;

public sealed class PipelineCacheTest
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
}
