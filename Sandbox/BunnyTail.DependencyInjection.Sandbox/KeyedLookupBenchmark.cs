namespace BunnyTail.DependencyInjection.Sandbox;

using BenchmarkDotNet.Attributes;

using BunnyTail.DependencyInjection.Sandbox.Infrastructure;

// keyed services の (Type, key) lookup 構造の比較。
// MEDI の keyed services は「同一サービス型 × 複数キー」という DI 固有の形状を持つため、
// 汎用の Type キーテーブル (dotnet-performance TYP-01 系) では答えが出ない
// Comparison of (Type, key) lookup structures for keyed services. Keyed services have a DI-specific shape
// (one service type across multiple keys), which the general Type-key tables (dotnet-performance TYP-01) do not answer.
[Config(typeof(BenchmarkConfig))]
public class KeyedLookupBenchmark
{
    private const int LookupCount = 1024;

    private static readonly string[] Keys = ["alpha", "beta", "gamma", "delta"];

    [Params(8, 64)]
    public int N { get; set; }

    private Dictionary<CompositeKey, ServiceEntry> dictComposite = default!;
    private NodeCompositeTable<ServiceEntry> nodeComposite = default!;
    private BucketCompositeTable<ServiceEntry> bucketComposite = default!;
    private TwoStageKeyedTable<ServiceEntry> twoStage = default!;
    private (Type Type, object Key)[] hitSequence = default!;
    private (Type Type, object Key)[] missSequence = default!;

    [GlobalSetup]
    public void Setup()
    {
        var pairs = KeyedFixture.CreatePairs(N, Keys);
        dictComposite = new Dictionary<CompositeKey, ServiceEntry>(pairs);
        nodeComposite = new NodeCompositeTable<ServiceEntry>(pairs);
        bucketComposite = new BucketCompositeTable<ServiceEntry>(pairs);
        twoStage = new TwoStageKeyedTable<ServiceEntry>(pairs);

        hitSequence = KeyedFixture.CreateHitSequence(N, Keys, LookupCount);
        missSequence = KeyedFixture.CreateMissSequence(N, Keys, LookupCount);
    }

    //--------------------------------------------------------------------------------
    // Hit
    //--------------------------------------------------------------------------------

    [Benchmark(Baseline = true, OperationsPerInvoke = LookupCount)]
    public int DictHit()
    {
        var sum = 0;
        var sequence = hitSequence;
        for (var i = 0; i < sequence.Length; i++)
        {
            if (dictComposite.TryGetValue(new CompositeKey(sequence[i].Type, sequence[i].Key), out var entry))
            {
                sum += entry.Index;
            }
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int NodeHit()
    {
        var sum = 0;
        var sequence = hitSequence;
        for (var i = 0; i < sequence.Length; i++)
        {
            if (nodeComposite.TryGet(sequence[i].Type, sequence[i].Key, out var entry))
            {
                sum += entry.Index;
            }
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int BucketHit()
    {
        var sum = 0;
        var sequence = hitSequence;
        for (var i = 0; i < sequence.Length; i++)
        {
            if (bucketComposite.TryGet(sequence[i].Type, sequence[i].Key, out var entry))
            {
                sum += entry.Index;
            }
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int TwoStageHit()
    {
        var sum = 0;
        var sequence = hitSequence;
        for (var i = 0; i < sequence.Length; i++)
        {
            if (twoStage.TryGet(sequence[i].Type, sequence[i].Key, out var entry))
            {
                sum += entry.Index;
            }
        }

        return sum;
    }

    //--------------------------------------------------------------------------------
    // Miss
    //--------------------------------------------------------------------------------

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int DictMiss()
    {
        var sum = 0;
        var sequence = missSequence;
        for (var i = 0; i < sequence.Length; i++)
        {
            if (dictComposite.TryGetValue(new CompositeKey(sequence[i].Type, sequence[i].Key), out var entry))
            {
                sum += entry.Index;
            }
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int NodeMiss()
    {
        var sum = 0;
        var sequence = missSequence;
        for (var i = 0; i < sequence.Length; i++)
        {
            if (nodeComposite.TryGet(sequence[i].Type, sequence[i].Key, out var entry))
            {
                sum += entry.Index;
            }
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int BucketMiss()
    {
        var sum = 0;
        var sequence = missSequence;
        for (var i = 0; i < sequence.Length; i++)
        {
            if (bucketComposite.TryGet(sequence[i].Type, sequence[i].Key, out var entry))
            {
                sum += entry.Index;
            }
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = LookupCount)]
    public int TwoStageMiss()
    {
        var sum = 0;
        var sequence = missSequence;
        for (var i = 0; i < sequence.Length; i++)
        {
            if (twoStage.TryGet(sequence[i].Type, sequence[i].Key, out var entry))
            {
                sum += entry.Index;
            }
        }

        return sum;
    }
}

// 測定と等価性検証で同じ入力を使うための共通生成
// Shared fixture so measurement and equivalence verification use identical inputs.
public static class KeyedFixture
{
    public static KeyValuePair<CompositeKey, ServiceEntry>[] CreatePairs(int n, string[] keys)
    {
        var pairs = new List<KeyValuePair<CompositeKey, ServiceEntry>>(n * keys.Length);
        var index = 0;
        for (var i = 0; i < n; i++)
        {
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var key in keys)
            {
                pairs.Add(new KeyValuePair<CompositeKey, ServiceEntry>(
                    new CompositeKey(KeyTypes.All[i], key),
                    new ServiceEntry(KeyTypes.All[i], index++)));
            }
        }

        return pairs.ToArray();
    }

    public static (Type Type, object Key)[] CreateHitSequence(int n, string[] keys, int count)
    {
        var random = new Random(12345);
        var sequence = new (Type, object)[count];
        for (var i = 0; i < count; i++)
        {
            sequence[i] = (KeyTypes.All[random.Next(n)], keys[random.Next(keys.Length)]);
        }

        return sequence;
    }

    // miss は「登録型 + 未知キー」と「未登録型」の半々
    // Misses are split evenly between a registered type with an unknown key and an unregistered type.
    public static (Type Type, object Key)[] CreateMissSequence(int n, string[] keys, int count)
    {
        var random = new Random(12345);
        var sequence = new (Type, object)[count];
        for (var i = 0; i < count; i++)
        {
            sequence[i] = (i & 1) == 0
                ? (KeyTypes.All[random.Next(n)], "unknown")
                : (KeyTypes.Miss[random.Next(KeyTypes.Miss.Length)], keys[random.Next(keys.Length)]);
        }

        return sequence;
    }
}
