namespace BunnyTail.DependencyInjection.Generator.Models;

// Add*/TryAdd* 呼び出しから収集した実装型
// Implementation type collected from Add*/TryAdd* invocations.
internal sealed record CollectedModel(
    FactoryModel Factory,
    string ServiceType,
    string Lifetime,
    int Kind,
    string FilePath,
    int SpanStart);
