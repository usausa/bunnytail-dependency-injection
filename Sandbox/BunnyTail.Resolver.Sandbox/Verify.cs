namespace BunnyTail.Resolver.Sandbox;

using BunnyTail.Resolver.Sandbox.Infrastructure;

// ベンチマーク実行前の等価性検証。全候補が「同一入力 → 同一結果」であることを確認してから測定する
// Equivalence verification before running benchmarks. Every candidate must produce identical results for identical input.
public static class Verify
{
    private static readonly string[] Keys = ["alpha", "beta", "gamma", "delta"];

    public static void RunAll()
    {
        VerifyKeyedLookup();
        VerifyResolutionEntry();
        Console.WriteLine("Equivalence verification passed.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Equivalence verification failed: {message}");
        }
    }

    // 現状形状と定数短絡形状が同一インスタンスを返すことを確認する
    // Confirms the current shape and the constant short-circuit shape return the same instances.
    private static void VerifyResolutionEntry()
    {
        var scope = new object();
        var pairs = new List<KeyValuePair<Type, ResolutionEntryBenchmark.Accessor>>();
        for (var i = 0; i < 64; i++)
        {
            pairs.Add(new KeyValuePair<Type, ResolutionEntryBenchmark.Accessor>(
                KeyTypes.All[i],
                new ResolutionEntryBenchmark.SingletonAccessor(new object())));
        }

        var accessorTable = new ResolutionEntryBenchmark.AccessorTable(pairs);
        var constantTable = new ResolutionEntryBenchmark.ConstantTable(pairs, primeConstants: true);
        foreach (var pair in pairs)
        {
            pair.Value.Prime(scope);
        }

        foreach (var pair in pairs)
        {
            var viaAccessor = accessorTable.Resolve(pair.Key, scope);
            var viaConstant = constantTable.Resolve(pair.Key, scope);
            Assert(viaAccessor is not null, "resolution entry found");
            Assert(ReferenceEquals(viaAccessor, viaConstant), "resolution entry identity");
        }

        // 未登録型は双方 null
        // Unregistered types resolve to null in both shapes.
        Assert(accessorTable.Resolve(typeof(Verify), scope) is null, "resolution entry miss");
        Assert(constantTable.Resolve(typeof(Verify), scope) is null, "resolution entry miss");
    }

    private static void VerifyKeyedLookup()
    {
        foreach (var n in (int[])[8, 64])
        {
            var pairs = KeyedFixture.CreatePairs(n, Keys);
            var dict = new Dictionary<CompositeKey, ServiceEntry>(pairs);
            var node = new NodeCompositeTable<ServiceEntry>(pairs);
            var bucket = new BucketCompositeTable<ServiceEntry>(pairs);
            var twoStage = new TwoStageKeyedTable<ServiceEntry>(pairs);

            foreach (var sequence in (IReadOnlyList<(Type Type, object Key)>[])
                     [
                         KeyedFixture.CreateHitSequence(n, Keys, 256),
                         KeyedFixture.CreateMissSequence(n, Keys, 256),
                     ])
            {
                foreach (var (type, key) in sequence)
                {
                    var expectedFound = dict.TryGetValue(new CompositeKey(type, key), out var expected);

                    Assert(node.TryGet(type, key, out var nodeValue) == expectedFound, $"node found (N={n})");
                    Assert(bucket.TryGet(type, key, out var bucketValue) == expectedFound, $"bucket found (N={n})");
                    Assert(twoStage.TryGet(type, key, out var twoStageValue) == expectedFound, $"twoStage found (N={n})");

                    if (expectedFound)
                    {
                        Assert(ReferenceEquals(nodeValue, expected), $"node value (N={n})");
                        Assert(ReferenceEquals(bucketValue, expected), $"bucket value (N={n})");
                        Assert(ReferenceEquals(twoStageValue, expected), $"twoStage value (N={n})");
                    }
                }
            }
        }
    }
}
