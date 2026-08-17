namespace BunnyTail.Resolver;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.DependencyInjection;

// ServiceDescriptor 集合から解決グラフ (accessor 群) をビルドするレジストリ。
// エントリ実現は初回解決時 (MEDI の callsite 構築と同タイミング)。実現済みエントリはイミュータブル
// Registry that builds the resolution graph (accessors) from the ServiceDescriptor set.
// Entries are realized on first resolution (same timing as MEDI callsite construction) and are immutable once realized.
internal sealed class ServiceRegistry
{
    [ThreadStatic]
    private static List<ServiceIdentifier>? realizationStack;

    private readonly ServiceDescriptor[] descriptors;                                          // 登録順 / registration order (used to build IEnumerable)
    private readonly Dictionary<ServiceIdentifier, List<ServiceDescriptor>> exactMap;          // (ServiceType, key) → 登録順リスト / list in registration order
    private readonly HashSet<Type> keyedServiceTypes;                                          // AnyKey クエリ判定用 / for AnyKey query checks
    private readonly ConcurrentDictionary<ServiceIdentifier, ServiceAccessor?> entries = new();
    private readonly ConcurrentDictionary<AccessorCacheKey, ServiceAccessor> descriptorAccessors = new();

    // 主テーブル (FixedServiceTable 参照)。ビルド時に実現できたエントリを収め、実行時に実現した
    // 派生エントリ (IEnumerable / closed generic / AnyKey 派生など) は COW 再構築で昇格する。
    // テーブル自体は常にイミュータブルで、resolve 経路に同期はない。
    // 実現できなかった登録 (null エントリ) は overlay (entries) 側に残る
    // Main table (see FixedServiceTable). Holds entries realized at build time; derived entries realized
    // at runtime (IEnumerable / closed generics / AnyKey derivations) are promoted by COW rebuild. The table itself
    // is always immutable and the resolve path has no synchronization. Registrations that could not be realized
    // (null entries) stay on the overlay (entries) side.
    private readonly Lock tableSync = new();
    private FixedTypeServiceTable typeTable;
    private FixedKeyedServiceTable keyedTable;
    private readonly List<KeyValuePair<Type, ServiceAccessor>>? typeTableEntries;              // 昇格用スナップショット (tableSync 下でのみ変更) / promotion snapshot (mutated only under tableSync)
    private readonly List<(Type Type, object Key, ServiceAccessor Accessor)>? keyedTableEntries;

    private int slotCounter;

    private readonly bool disposedSentinel;

    // dispose 済み scope 用の番兵。テーブルが空なので全解決がミスして GetEntrySlow へ落ち、そこで throw する。
    // これによりホット経路から disposed フラグの分岐が消える (S-10)
    // Sentinel for disposed scopes. The tables are empty, so every resolution misses into GetEntrySlow, which throws.
    // This removes the disposed-flag branch from the hot path (S-10).
    internal static readonly ServiceRegistry DisposedSentinel = new();

    private ServiceRegistry()
    {
        disposedSentinel = true;
        typeTable = new FixedTypeServiceTable([]);
        keyedTable = new FixedKeyedServiceTable([]);
        descriptors = [];
        exactMap = [];
        keyedServiceTypes = [];
    }

    public ServiceRegistry(IEnumerable<ServiceDescriptor> source, ResolverServiceProvider provider)
    {
        // ウォームアップ中は空テーブルを置く。null 許容にすると解決のホット経路に null チェックが乗るため
        // Empty tables during warmup: making the fields nullable would put a null check on the hot resolution path.
        typeTable = new FixedTypeServiceTable([]);
        keyedTable = new FixedKeyedServiceTable([]);

        descriptors = source.ToArray();
        exactMap = [];
        keyedServiceTypes = [];
        foreach (var descriptor in descriptors)
        {
            var key = descriptor.IsKeyedService ? descriptor.ServiceKey : null;
            var id = new ServiceIdentifier(descriptor.ServiceType, key);
            if (!exactMap.TryGetValue(id, out var list))
            {
                list = [];
                exactMap[id] = list;
            }

            list.Add(descriptor);

            if (descriptor.IsKeyedService)
            {
                keyedServiceTypes.Add(descriptor.ServiceType);
            }
        }

        // built-in サービス (ユーザー登録より優先: MEDI 互換)
        // Built-in services (take precedence over user registrations: MEDI compatible).
        entries[new ServiceIdentifier(typeof(IServiceProvider), null)] = new ServiceProviderAccessor();
        entries[new ServiceIdentifier(typeof(IServiceScopeFactory), null)] = new ConstantAccessor(provider);
        entries[new ServiceIdentifier(typeof(IServiceProviderIsService), null)] = new ConstantAccessor(provider);
        entries[new ServiceIdentifier(typeof(IServiceProviderIsKeyedService), null)] = new ConstantAccessor(provider);

        // ビルド時ウォームアップ: 実現可能な exact 登録を主テーブルへ固める。実現に失敗する登録
        // (未解決依存・循環など) はここでは無視し、初回 resolve 時に例外を投げ直す (MEDI 互換: ビルドは失敗させない)
        // Build-time warmup: realizable exact registrations are frozen into the main table. Registrations that fail
        // to realize (unresolved dependencies, cycles, ...) are ignored here and rethrow on first resolve
        // (MEDI compatible: building never fails).
        var typeEntries = new List<KeyValuePair<Type, ServiceAccessor>>
        {
            new(typeof(IServiceProvider), entries[new ServiceIdentifier(typeof(IServiceProvider), null)]!),
            new(typeof(IServiceScopeFactory), entries[new ServiceIdentifier(typeof(IServiceScopeFactory), null)]!),
            new(typeof(IServiceProviderIsService), entries[new ServiceIdentifier(typeof(IServiceProviderIsService), null)]!),
            new(typeof(IServiceProviderIsKeyedService), entries[new ServiceIdentifier(typeof(IServiceProviderIsKeyedService), null)]!)
        };
        var keyedEntries = new List<(Type, object, ServiceAccessor)>();
        foreach (var id in exactMap.Keys)
        {
            if (id.ServiceType.IsGenericTypeDefinition
                || ReferenceEquals(id.Key, KeyedService.AnyKey)
                || (id.Key is null && typeEntries.Exists(x => ReferenceEquals(x.Key, id.ServiceType))))
            {
                continue;
            }

            ServiceAccessor? accessor;
            try
            {
                accessor = GetEntry(id);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (accessor is null)
            {
                continue;
            }

            if (id.Key is null)
            {
                typeEntries.Add(new KeyValuePair<Type, ServiceAccessor>(id.ServiceType, accessor));
            }
            else
            {
                keyedEntries.Add((id.ServiceType, id.Key, accessor));
            }
        }

        typeTable = new FixedTypeServiceTable(typeEntries);
        keyedTable = new FixedKeyedServiceTable(keyedEntries);
        typeTableEntries = typeEntries;
        keyedTableEntries = keyedEntries;
    }

    private int NextSlot() => Interlocked.Increment(ref slotCounter) - 1;

    // 開発時診断 (Diagnostics.ServiceFactoryReportExtensions)。各登録を実現して、生成ファクトリが採用されたかを分類する。
    // 実現は accessor の構築までで、インスタンスは生成しない
    // Development-time diagnostics (Diagnostics.ServiceFactoryReportExtensions): realizes every registration and classifies whether a
    // generated factory was adopted. Realization builds accessors only and never creates instances.
    internal List<Diagnostics.ServiceFactoryReportEntry> CreateFactoryReport()
    {
        var entries = new List<Diagnostics.ServiceFactoryReportEntry>();
        foreach (var pair in exactMap)
        {
            // 実際に解決されるのは同一 (サービス型, キー) の最後の登録 (MEDI の last-wins)
            // The registration actually resolved is the last one for the same (service type, key) pair (MEDI last-wins).
            var descriptor = pair.Value[^1];
            var key = descriptor.IsKeyedService ? descriptor.ServiceKey : null;
            var implementationType = descriptor.IsKeyedService ? descriptor.KeyedImplementationType : descriptor.ImplementationType;

            // コンテナが型を構築しない登録 (ファクトリ・インスタンス・open generic 定義) は生成対象外。
            // 実装型を持つ登録だけが分類対象なので、生成ファクトリとユーザーデリゲートが混同されることはない
            // Registrations where the container does not construct the type (factories, instances, open generic
            // definitions) have nothing to generate. Only registrations carrying an implementation type are
            // classified, so generated factories are never confused with user delegates.
            if ((implementationType is null) || implementationType.IsGenericTypeDefinition || descriptor.ServiceType.IsGenericTypeDefinition)
            {
                entries.Add(new Diagnostics.ServiceFactoryReportEntry(descriptor.ServiceType, implementationType, key, descriptor.Lifetime, Diagnostics.ServiceFactoryStatus.NotApplicable));
                continue;
            }

            ServiceAccessor? accessor;
            try
            {
                accessor = GetEntry(new ServiceIdentifier(descriptor.ServiceType, key));
            }
            catch (InvalidOperationException)
            {
                accessor = null;
            }

            var status = accessor switch
            {
                FactoryAccessor or DepsFactoryAccessor or KeyedFactoryAccessor or KeyedDepsFactoryAccessor => Diagnostics.ServiceFactoryStatus.Generated,
                ConstructorAccessor => Diagnostics.ServiceFactoryStatus.RuntimeFallback,
                null => Diagnostics.ServiceFactoryStatus.Unresolvable,
                _ => Diagnostics.ServiceFactoryStatus.NotApplicable
            };
            entries.Add(new Diagnostics.ServiceFactoryReportEntry(descriptor.ServiceType, implementationType, key, descriptor.Lifetime, status));
        }

        return entries;
    }

    //--------------------------------------------------------------------------------
    // Entry realization (エントリ実現)
    //--------------------------------------------------------------------------------

    // ホット経路は主テーブル (イミュータブル、同期なし) を引くだけ。realization は低速パスへ分離してあり、
    // このメソッドが呼び出し元にインライン展開されることを維持する (JIT-04)。
    // 分離前は realization を同一メソッドに抱えていたため JIT がインライン化を諦め、
    // 巻き添えで RuntimeHelpers.GetHashCode すら call として残っていた
    // The hot path only probes the main tables (immutable, no synchronization). Realization lives in a separate slow
    // path so this method stays inlineable into its callers (JIT-04). Before the split, realization sat in the same
    // method, the JIT gave up inlining it and even RuntimeHelpers.GetHashCode was left as a call.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ServiceAccessor? GetEntry(ServiceIdentifier id)
    {
        var fixedAccessor = id.Key is null
            ? typeTable.Get(id.ServiceType)
            : keyedTable.Get(id.ServiceType, id.Key);
        return fixedAccessor ?? GetEntrySlow(id);
    }

    // 解決のホット経路。テーブルヒットは定数短絡込みでテーブル側が解決し、ミスのみ realization へ回る。
    // 非 keyed / keyed でメソッドを分け、各経路に相手側テーブルの死にコードと ServiceIdentifier 構築を持ち込まない
    // Hot resolution paths. Table hits resolve inside the table (constant short-circuit included); only misses go to
    // realization. Split into non-keyed and keyed methods so neither path carries the other table's dead code or a
    // ServiceIdentifier construction.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? ResolveType(Type serviceType, ServiceProviderScope scope)
    {
        if (typeTable.TryResolve(serviceType, scope, out var value))
        {
            return value;
        }

        return GetEntrySlow(new ServiceIdentifier(serviceType, null))?.GetValue(scope);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? ResolveKeyed(Type serviceType, object serviceKey, ServiceProviderScope scope)
    {
        if (keyedTable.TryResolve(serviceType, serviceKey, scope, out var value))
        {
            return value;
        }

        return GetEntrySlow(new ServiceIdentifier(serviceType, serviceKey))?.GetValue(scope);
    }

    // 主テーブルに無いもの: 派生エントリ (IEnumerable / closed generic / AnyKey 派生)、未実現の登録、未登録型
    // Not in the main tables: derived entries (IEnumerable / closed generics / AnyKey derivations), unrealized registrations and unknown types.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private ServiceAccessor? GetEntrySlow(ServiceIdentifier id)
    {
        ObjectDisposedException.ThrowIf(disposedSentinel, typeof(IServiceProvider));

        if (entries.TryGetValue(id, out var existing))
        {
            // ウォームアップ中に実現された派生エントリはテーブル未収載のことがあるため、ここでも昇格する
            // Derived entries realized during warmup may not be in the table yet, so promote here as well.
            if (existing is not null)
            {
                PromoteEntry(id, existing);
            }

            return existing;
        }

        var stack = realizationStack ??= [];
        for (var i = 0; i < stack.Count; i++)
        {
            if (stack[i].Equals(id))
            {
                throw CreateCircularDependencyException(stack, i, id);
            }
        }

        stack.Add(id);
        try
        {
            var created = CreateEntry(id);
            var realized = entries.GetOrAdd(id, created);
            if (realized is not null)
            {
                PromoteEntry(id, realized);
            }

            return realized;
        }
        finally
        {
            stack.RemoveAt(stack.Count - 1);
        }
    }

    // 実現済みエントリの主テーブル昇格。スナップショットへ追記して再構築し、Volatile.Write で差し替える (COW)。
    // 読み出し側はロックなしの素の読みのままで、差し替え前の古いテーブルを読んだ場合も overlay 側で解決できるため常に正しい
    // Promotion of a realized entry into the main table. Appends to the snapshot, rebuilds and swaps with
    // Volatile.Write (COW). Readers keep plain lock-free reads; reading a pre-swap table is still correct
    // because the overlay can serve the entry.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void PromoteEntry(ServiceIdentifier id, ServiceAccessor accessor)
    {
        lock (tableSync)
        {
            if (typeTableEntries is null || keyedTableEntries is null)
            {
                return;   // ウォームアップ中 (コンストラクタ末尾でまとめて構築される) / during warmup (built in one go at the end of the constructor)
            }

            if (id.Key is null)
            {
                if (typeTable.Get(id.ServiceType) is null)
                {
                    typeTableEntries.Add(new KeyValuePair<Type, ServiceAccessor>(id.ServiceType, accessor));
                    Volatile.Write(ref typeTable, new FixedTypeServiceTable(typeTableEntries));
                }
            }
            else
            {
                if (keyedTable.Get(id.ServiceType, id.Key) is null)
                {
                    keyedTableEntries.Add((id.ServiceType, id.Key, accessor));
                    Volatile.Write(ref keyedTable, new FixedKeyedServiceTable(keyedTableEntries));
                }
            }
        }
    }

    private static InvalidOperationException CreateCircularDependencyException(List<ServiceIdentifier> stack, int startIndex, ServiceIdentifier id)
    {
        var chain = string.Join(" -> ", stack.Skip(startIndex).Select(static x => TypeNameHelper.GetTypeDisplayName(x.ServiceType)));
        var message = $"A circular dependency was detected for the service of type '{TypeNameHelper.GetTypeDisplayName(id.ServiceType)}'."
                      + Environment.NewLine
                      + chain + " -> " + TypeNameHelper.GetTypeDisplayName(id.ServiceType);
        return new InvalidOperationException(message);
    }

    private ServiceAccessor? CreateEntry(ServiceIdentifier id)
    {
        var serviceType = id.ServiceType;
        if (serviceType.IsGenericTypeDefinition)
        {
            return null;
        }

        // 1. 完全一致 (closed)。単一解決は最後の登録 (MEDI 互換: exact は open より優先)
        // 1. Exact (closed) match. Single resolution takes the last registration (MEDI compatible: exact wins over open).
        if (exactMap.TryGetValue(id, out var list))
        {
            return TryRealizeDescriptor(list[^1], serviceType, id.Key);
        }

        // 2. AnyKey 登録への concrete key 問い合わせ
        // 2. Concrete key query against AnyKey registrations.
        if (id.Key is not null && !ReferenceEquals(id.Key, KeyedService.AnyKey)
            && exactMap.TryGetValue(new ServiceIdentifier(serviceType, KeyedService.AnyKey), out var anyList))
        {
            return TryRealizeDescriptor(anyList[^1], serviceType, id.Key);
        }

        if (serviceType.IsConstructedGenericType)
        {
            var definition = serviceType.GetGenericTypeDefinition();

            // 3. IEnumerable<T>
            if (definition == typeof(IEnumerable<>))
            {
                return CreateEnumerableAccessor(serviceType.GenericTypeArguments[0], id.Key);
            }

            // 4. open generic (後方から、型制約を満たす最初のもの)
            // 4. Open generics (searched backwards, first registration satisfying the type constraints).
            if (exactMap.TryGetValue(new ServiceIdentifier(definition, id.Key), out var openList))
            {
                for (var i = openList.Count - 1; i >= 0; i--)
                {
                    var accessor = TryRealizeDescriptor(openList[i], serviceType, id.Key);
                    if (accessor is not null)
                    {
                        return accessor;
                    }
                }
            }

            if (id.Key is not null && !ReferenceEquals(id.Key, KeyedService.AnyKey)
                && exactMap.TryGetValue(new ServiceIdentifier(definition, KeyedService.AnyKey), out var anyOpenList))
            {
                for (var i = anyOpenList.Count - 1; i >= 0; i--)
                {
                    var accessor = TryRealizeDescriptor(anyOpenList[i], serviceType, id.Key);
                    if (accessor is not null)
                    {
                        return accessor;
                    }
                }
            }
        }

        return null;
    }

    //--------------------------------------------------------------------------------
    // Descriptor realization (ディスクリプタ実現)
    //--------------------------------------------------------------------------------

    private ServiceAccessor? TryRealizeDescriptor(ServiceDescriptor descriptor, Type serviceType, object? requestedKey)
    {
        var effectiveKey = descriptor.IsKeyedService
            ? (ReferenceEquals(descriptor.ServiceKey, KeyedService.AnyKey) ? requestedKey : descriptor.ServiceKey)
            : null;

        var cacheKey = new AccessorCacheKey(descriptor, serviceType, effectiveKey);
        if (descriptorAccessors.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var created = CreateAccessor(descriptor, serviceType, effectiveKey);
        if (created is null)
        {
            return null;
        }

        return descriptorAccessors.GetOrAdd(cacheKey, created);
    }

    private ServiceAccessor? CreateAccessor(ServiceDescriptor descriptor, Type serviceType, object? effectiveKey)
    {
        var cache = descriptor.Lifetime switch
        {
            ServiceLifetime.Singleton => ResultCache.Root,
            ServiceLifetime.Scoped => ResultCache.Scoped,
            _ => ResultCache.None
        };

        if (!descriptor.IsKeyedService)
        {
            if (descriptor.ImplementationInstance is not null)
            {
                return new ConstantAccessor(descriptor.ImplementationInstance);
            }

            if (descriptor.ImplementationFactory is not null)
            {
                // ユーザーファクトリは実装型が不明なため追跡は常に有効
                // User factories have unknown implementation types, so tracking is always enabled.
                return new FactoryAccessor(descriptor.ImplementationFactory, cache, cache == ResultCache.Scoped ? NextSlot() : -1, trackDisposable: true);
            }

            return CreateConstructorAccessor(descriptor.ImplementationType!, serviceType, cache, serviceKey: null);
        }

        if (descriptor.KeyedImplementationInstance is not null)
        {
            return new ConstantAccessor(descriptor.KeyedImplementationInstance);
        }

        if (descriptor.KeyedImplementationFactory is not null)
        {
            return new KeyedFactoryAccessor(descriptor.KeyedImplementationFactory, effectiveKey, cache, cache == ResultCache.Scoped ? NextSlot() : -1, trackDisposable: true);
        }

        return CreateConstructorAccessor(descriptor.KeyedImplementationType!, serviceType, cache, serviceKey: effectiveKey);
    }

    // open generic 定義から closed 型を作る (互換経路のみ)。NativeAOT では参照型引数は shared generic で動作し、
    // 値型引数のみ実行時例外になり得る (生成経路はコンパイル時に閉じるため影響しない)
    // Builds the closed type from an open generic definition (runtime path only). On NativeAOT, reference type
    // arguments work through shared generics and only value type arguments may fail at runtime
    // (the generated path closes types at compile time and is unaffected).
    [UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "値型引数の open generic のみ実行時に失敗し得る。互換経路限定の挙動としてドキュメント化済み")]
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "closed 型のコンストラクタは登録された open generic 実装型のメタデータから保持される")]
    [UnconditionalSuppressMessage("Trimming", "IL2068", Justification = "同上")]
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private static Type MakeClosedGenericType(Type definition, Type[] typeArguments) => definition.MakeGenericType(typeArguments);

    private ServiceAccessor? CreateConstructorAccessor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType,
        Type serviceType,
        ResultCache cache,
        object? serviceKey)
    {
        var implType = implementationType;
        if (implType.IsGenericTypeDefinition)
        {
            try
            {
                implType = MakeClosedGenericType(implType, serviceType.GenericTypeArguments);
            }
            catch (ArgumentException)
            {
                // 型制約を満たさない → この登録はこの closed 型には適用不可
                // Type constraints not satisfied: this registration does not apply to this closed type.
                return null;
            }
        }

        var slot = cache == ResultCache.Scoped ? NextSlot() : -1;

        var constructors = implType.GetConstructors();
        if (constructors.Length == 0)
        {
            if (implType.IsValueType)
            {
                return new ValueTypeAccessor(implType, typeof(IInitializable).IsAssignableFrom(implType), cache, slot, IsDisposableType(implType));
            }

            throw new InvalidOperationException(
                $"A suitable constructor for type '{implType}' could not be located. Ensure the type is concrete and all parameters of a public constructor are either registered as services or passed as arguments. Also ensure no extraneous arguments are provided.");
        }

        if (constructors.Length == 1)
        {
            var plans = BuildParameterPlans(constructors[0], implType, serviceKey, throwOnMiss: true)!;
            return CreateFinalAccessor(implType, constructors[0], plans, serviceKey, cache, slot);
        }

        // 複数コンストラクタ: MEDI 規則 (解決可能な最大のもの、superset でなければ ambiguous)
        // Multiple constructors: MEDI rules (the largest resolvable one; ambiguous unless a superset).
        Array.Sort(constructors, static (a, b) => b.GetParameters().Length.CompareTo(a.GetParameters().Length));

        ConstructorInfo? best = null;
        ParameterPlan[]? bestPlans = null;
        HashSet<Type>? bestTypes = null;
        foreach (var constructor in constructors)
        {
            var plans = BuildParameterPlans(constructor, implType, serviceKey, throwOnMiss: false);
            if (plans is null)
            {
                continue;
            }

            var types = new HashSet<Type>(constructor.GetParameters().Select(static p => p.ParameterType));
            if (best is null)
            {
                best = constructor;
                bestPlans = plans;
                bestTypes = types;
            }
            else if (bestTypes!.IsSupersetOf(types))
            {
                // 既存の best が優位 / the current best remains superior
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unable to activate type '{implType}'. The following constructors are ambiguous:{Environment.NewLine}{best}{Environment.NewLine}{constructor}");
            }
        }

        if (best is null)
        {
            throw new InvalidOperationException(
                $"No constructor for type '{implType}' can be instantiated using services from the service container and default values.");
        }

        return CreateFinalAccessor(implType, best, bestPlans!, serviceKey, cache, slot);
    }

    // disposal 追跡要否を実装型で確定する (実行時 is チェックの排除。Sandbox の DisposalTrackingBenchmark)
    // Settles whether disposal tracking is needed from the implementation type (eliminates runtime type checks; see DisposalTrackingBenchmark in the sandbox).
    private static bool IsDisposableType(Type type) =>
        typeof(IDisposable).IsAssignableFrom(type) || typeof(IAsyncDisposable).IsAssignableFrom(type);

    // 初期化コールバックの解決 (PostConstruct 指定優先、なければ IInitializable)。accessor 構築時に確定し、
    // 初期化を持たない型の resolve にはコストを発生させない。
    // PostConstruct のメソッドは生成経路 (Source Generator) では静的に参照されるため保持される。
    // 「トリミング環境 + 実行時のみ判明する登録 + PostConstruct 指定」の組合せのみ制約 ([Inject] と同じ)
    // Resolves the initialization callback (an explicit PostConstruct wins, otherwise IInitializable). Settled when
    // the accessor is built, so types without initialization pay no resolve-time cost. PostConstruct methods are
    // statically referenced by the generated path and therefore preserved; only the combination of trimming +
    // runtime-only registrations + PostConstruct is constrained (same as [Inject]).
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "PostConstruct 指定付き型は生成コードが静的参照するため保持される。実行時登録のみの型は制約としてドキュメント化")]
    private static (MethodInfo? PostConstruct, bool Initializable) ResolveInitializer(Type implType)
    {
        string? name = null;
        foreach (var attribute in implType.GetCustomAttributes(inherit: false))
        {
            var candidate = attribute switch
            {
                SingletonAttribute singleton => singleton.PostConstruct,
                ScopedAttribute scoped => scoped.PostConstruct,
                TransientAttribute transient => transient.PostConstruct,
                _ => null
            };

            if (candidate is not null)
            {
                name = candidate;
                break;
            }
        }

        // [GenerateComponentFactory(PostConstruct = ...)] の指定は属性より優先する (属性を付けられない型のための指定)
        // A [GenerateComponentFactory(PostConstruct = ...)] specification wins over attributes (it exists for types that cannot be annotated).
        if (GeneratedComponentRegistry.TryGetInitializer(implType, out var registered))
        {
            name = registered;
        }

        if (name is not null)
        {
            var method = implType.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
            if (method is null || method.ReturnType != typeof(void) || method.IsGenericMethodDefinition)
            {
                throw new InvalidOperationException(
                    $"The PostConstruct method '{name}' on type '{implType}' must be a public parameterless instance method returning void.");
            }

            return (method, false);
        }

        return (null, typeof(IInitializable).IsAssignableFrom(implType));
    }

    private ServiceAccessor CreateFinalAccessor(Type implType, ConstructorInfo constructor, ParameterPlan[] plans, object? serviceKey, ResultCache cache, int slot)
    {
        var track = IsDisposableType(implType);

        // 生成経路フック: MEDI 規則で選択されたコンストラクタが生成時の前提と一致し、かつ全引数が
        // サービス解決 (既定値フォールバックなし) の場合のみ生成ファクトリを採用する (不一致は互換経路へフォールバック)
        // Generated path hook: the generated factory is adopted only when the constructor selected by MEDI rules
        // matches the generation-time assumption and every argument is a service resolution (no default value
        // fallback). Mismatches fall back to the runtime path.
        if (serviceKey is null)
        {
            if (GeneratedComponentRegistry.TryGet(implType, out var generated)
                && ConstructorMatches(constructor, generated.ConstructorParameterTypes)
                && AllPlansAreServices(plans)
                && InlinedDependenciesMatch(generated.InlinedDependencies))
            {
                if (generated.DepsFactory is null)
                {
                    return new FactoryAccessor(generated.Factory!, cache, slot, track);
                }

                // deps 形: スロット前提も成立する場合のみ採用し、検証済み accessor を保持する
                // Deps shape: adopted only when the slot assumptions also hold; keeps the validated accessors.
                if (TryResolveDependencies(generated.Dependencies, out var dependencyAccessors, out var dependencyHandles))
                {
                    return new DepsFactoryAccessor(generated.DepsFactory, dependencyAccessors, dependencyHandles, cache, slot, track);
                }
            }
        }
        else
        {
            // keyed: [ServiceKey] 注入は生成ファクトリが key 引数として受け取るため採用可
            // Keyed: [ServiceKey] injection is acceptable because the generated factory receives it as the key argument.
            if (GeneratedComponentRegistry.TryGetKeyed(implType, out var generatedKeyed)
                && ConstructorMatches(constructor, generatedKeyed.ConstructorParameterTypes)
                && AllPlansAreServicesOrServiceKey(plans)
                && InlinedDependenciesMatch(generatedKeyed.InlinedDependencies))
            {
                if (generatedKeyed.KeyedDepsFactory is null)
                {
                    return new KeyedFactoryAccessor(generatedKeyed.Factory!, serviceKey, cache, slot, track);
                }

                // keyed deps 形: スロット前提も成立する場合のみ採用 (非 keyed と同じ検証)
                // Keyed deps shape: adopted only when the slot assumptions also hold (same validation as non-keyed).
                if (TryResolveDependencies(generatedKeyed.Dependencies, out var keyedDependencyAccessors, out var keyedDependencyHandles))
                {
                    return new KeyedDepsFactoryAccessor(generatedKeyed.KeyedDepsFactory, serviceKey, keyedDependencyAccessors, keyedDependencyHandles, cache, slot, track);
                }
            }
        }

        var properties = BuildPropertyInjections(implType, serviceKey);
        var (postConstruct, initializable) = ResolveInitializer(implType);
        return new ConstructorAccessor(constructor, plans, properties, postConstruct, initializable, cache, slot, track);
    }

    private static bool AllPlansAreServices(ParameterPlan[] plans)
    {
        foreach (var plan in plans)
        {
            if (!plan.IsService)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AllPlansAreServicesOrServiceKey(ParameterPlan[] plans)
    {
        foreach (var plan in plans)
        {
            if (!plan.IsService && !plan.IsServiceKey)
            {
                return false;
            }
        }

        return true;
    }

    // インライン展開された依存の前提検証。生成ファクトリが依存をリテラル new で展開している場合、
    // その依存サービスの実行時解決が「前提どおりの実装型の生成ファクトリによる transient 解決」になる場合のみ
    // 採用できる。展開先の生成ファクトリ自体も採用時に同じ検証を受けているため、直接依存の検証で推移的に
    // 全体が保証される。不成立 (差し替え・lifetime 変更・ファクトリ登録等) なら呼び出し側が互換経路へフォールバックする
    // Validation of inlined dependency assumptions. When a generated factory inlines a dependency as a literal new,
    // it can only be adopted if resolving that dependency at runtime results in a transient resolution through the
    // assumed implementation type's generated factory. Each inlined factory goes through the same validation when
    // adopted, so validating direct dependencies transitively guarantees the whole graph. On mismatch (replacement,
    // lifetime change, factory registration, ...) the caller falls back to the runtime path.
    private bool InlinedDependenciesMatch(InlinedDependency[] dependencies)
    {
        foreach (var dependency in dependencies)
        {
            var accessor = GetEntry(new ServiceIdentifier(dependency.ServiceType, null));
            if (!GeneratedComponentRegistry.TryGet(dependency.ImplementationType, out var entry)
                || !UsesGeneratedFactory(accessor, entry, ResultCache.None))
            {
                return false;
            }
        }

        return true;
    }

    // deps スロット前提の検証。インスタンススロットは「前提どおりの実装型の生成ファクトリによる singleton 解決」を
    // 要求する。アクセサスロットは解決可能なことだけを要求する (accessor 呼び出しはレジストリ解決と意味論同一のため、
    // lifetime や実装の前提は不要)。成立時は検証済み accessor と、アクセサスロット用の生成済みハンドルを deps 充填用に返す
    // Validation of the deps slot assumptions. Instance slots require a singleton resolution through the assumed
    // implementation's generated factory. Accessor slots only require resolvability (calling the accessor is
    // semantically identical to a registry resolution, so no lifetime or implementation assumption is needed).
    // On success the validated accessors and the pre-created handles for accessor slots are returned for filling deps.
    private bool TryResolveDependencies(DependencyPlan[] dependencies, out ServiceAccessor[] accessors, out DependencyAccessor?[] handles)
    {
        accessors = dependencies.Length == 0 ? [] : new ServiceAccessor[dependencies.Length];
        handles = dependencies.Length == 0 ? [] : new DependencyAccessor?[dependencies.Length];
        for (var i = 0; i < dependencies.Length; i++)
        {
            var accessor = GetEntry(new ServiceIdentifier(dependencies[i].ServiceType, null));
            if (dependencies[i].UseAccessor)
            {
                if (accessor is null)
                {
                    return false;
                }

                accessors[i] = accessor;
                handles[i] = new DependencyAccessor(accessor, dependencies[i].ServiceType);
                continue;
            }

            if (!GeneratedComponentRegistry.TryGet(dependencies[i].ImplementationType!, out var entry)
                || !UsesGeneratedFactory(accessor, entry, ResultCache.Root))
            {
                return false;
            }

            accessors[i] = accessor!;
        }

        return true;
    }

    // 実行時解決が「entry の生成ファクトリによる指定 lifetime の解決」かを、ファクトリの参照比較で判定する
    // Checks whether the runtime resolution uses the entry's generated factory with the required lifetime, by reference comparison.
    private static bool UsesGeneratedFactory(ServiceAccessor? accessor, GeneratedComponentRegistry.Entry entry, ResultCache requiredCache)
    {
        return accessor switch
        {
            FactoryAccessor factory when factory.Cache == requiredCache => ReferenceEquals(factory.Factory, entry.Factory),
            DepsFactoryAccessor deps when deps.Cache == requiredCache => ReferenceEquals(deps.Factory, entry.DepsFactory),
            _ => false
        };
    }

    private static bool ConstructorMatches(ConstructorInfo constructor, Type[] assumedParameterTypes)
    {
        var parameters = constructor.GetParameters();
        if (parameters.Length != assumedParameterTypes.Length)
        {
            return false;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            if (!ReferenceEquals(parameters[i].ParameterType, assumedParameterTypes[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static readonly PropertyInjection[] EmptyPropertyInjections = [];

    // [Inject] プロパティは生成経路 (Source Generator) では静的に参照されるため保持される。
    // 「トリミング環境 + 実行時のみ判明する登録 + [Inject] プロパティ」の組合せのみ制約 (ドキュメント化済み)
    // [Inject] properties are statically referenced by the generated path and therefore preserved. Only the
    // combination of trimming + runtime-only registrations + [Inject] properties is constrained (documented).
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "[Inject] プロパティ付き型は生成コードが静的参照するため保持される。実行時登録のみの型は制約としてドキュメント化")]
    private PropertyInjection[] BuildPropertyInjections(Type implType, object? serviceKey)
    {
        List<PropertyInjection>? list = null;
        foreach (var property in implType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<InjectAttribute>() is null)
            {
                continue;
            }

            if (property.SetMethod is null || !property.SetMethod.IsPublic)
            {
                throw new InvalidOperationException(
                    $"The property '{property.Name}' marked with [Inject] on type '{implType}' must have a public setter.");
            }

            object? key = null;
            var fromKeyed = property.GetCustomAttribute<FromKeyedServicesAttribute>();
            if (fromKeyed is not null)
            {
                key = fromKeyed.LookupMode == ServiceKeyLookupMode.InheritKey ? serviceKey : fromKeyed.Key;
            }

            var accessor = GetEntry(new ServiceIdentifier(property.PropertyType, key));
            if (accessor is null)
            {
                throw new InvalidOperationException(
                    $"Unable to resolve service for type '{property.PropertyType}' while attempting to activate '{implType}'.");
            }

            (list ??= []).Add(new PropertyInjection(property, ParameterPlan.FromService(accessor)));
        }

        return list is null ? EmptyPropertyInjections : list.ToArray();
    }

    private ParameterPlan[]? BuildParameterPlans(ConstructorInfo constructor, Type implementationType, object? serviceKey, bool throwOnMiss)
    {
        var parameters = constructor.GetParameters();
        if (parameters.Length == 0)
        {
            return [];
        }

        var plans = new ParameterPlan[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];

            // [ServiceKey]: 解決中のキーを注入 (キー型がパラメータ型と非互換なら失敗)
            // [ServiceKey]: injects the key being resolved (fails when the key type is incompatible with the parameter type).
            if (parameter.GetCustomAttribute<ServiceKeyAttribute>() is not null)
            {
                if (serviceKey is not null)
                {
                    if (!parameter.ParameterType.IsInstanceOfType(serviceKey))
                    {
                        throw new InvalidOperationException(
                            $"The type of the key '{serviceKey.GetType()}' used for lookup doesn't match the type '{parameter.ParameterType}' in the constructor parameter with the ServiceKey attribute.");
                    }

                    plans[i] = ParameterPlan.FromServiceKey(serviceKey);
                    continue;
                }

                // 非 keyed 文脈での [ServiceKey] は解決不能扱い (既定値フォールバックあり)
                // [ServiceKey] in a non-keyed context is treated as unresolvable (with default value fallback).
                if (ParameterDefaults.TryGetDefaultValue(parameter, out var keyDefault))
                {
                    plans[i] = ParameterPlan.FromConstant(keyDefault);
                    continue;
                }

                if (throwOnMiss)
                {
                    throw new InvalidOperationException(
                        $"Unable to resolve service for type '{parameter.ParameterType}' while attempting to activate '{implementationType}'.");
                }

                return null;
            }

            object? key = null;
            var fromKeyed = parameter.GetCustomAttribute<FromKeyedServicesAttribute>();
            if (fromKeyed is not null)
            {
                // [FromKeyedServices] (キー省略) = 解決中のキーを継承 / [FromKeyedServices(null)] = 非 keyed 解決
                // [FromKeyedServices] without a key inherits the key being resolved; [FromKeyedServices(null)] resolves non-keyed.
                key = fromKeyed.LookupMode == ServiceKeyLookupMode.InheritKey ? serviceKey : fromKeyed.Key;
            }

            var accessor = GetEntry(new ServiceIdentifier(parameter.ParameterType, key));
            if (accessor is not null)
            {
                plans[i] = ParameterPlan.FromService(accessor);
                continue;
            }

            if (ParameterDefaults.TryGetDefaultValue(parameter, out var defaultValue))
            {
                plans[i] = ParameterPlan.FromConstant(defaultValue);
                continue;
            }

            if (throwOnMiss)
            {
                throw new InvalidOperationException(
                    $"Unable to resolve service for type '{parameter.ParameterType}' while attempting to activate '{implementationType}'.");
            }

            return null;
        }

        return plans;
    }

    //--------------------------------------------------------------------------------
    // IEnumerable<T>
    //--------------------------------------------------------------------------------

    private ServiceAccessor CreateEnumerableAccessor(Type elementType, object? key)
    {
        var items = new List<ServiceAccessor>();

        if (ReferenceEquals(key, KeyedService.AnyKey))
        {
            // AnyKey クエリ: concrete key で登録された全 keyed サービス (AnyKey 登録は除外)
            // AnyKey query: all keyed services registered with concrete keys (AnyKey registrations excluded).
            foreach (var descriptor in descriptors)
            {
                if (!descriptor.IsKeyedService || ReferenceEquals(descriptor.ServiceKey, KeyedService.AnyKey))
                {
                    continue;
                }

                AddEnumerableItem(items, descriptor, elementType, descriptor.ServiceKey);
            }
        }
        else
        {
            foreach (var descriptor in descriptors)
            {
                // 列挙は完全一致キーのみ (AnyKey 登録は単一解決のフォールバック専用。MEDI 互換)
                // Enumeration matches exact keys only (AnyKey registrations are a fallback for single resolution. MEDI compatible).
                var matches = key is null
                    ? !descriptor.IsKeyedService
                    : descriptor.IsKeyedService
                      && !ReferenceEquals(descriptor.ServiceKey, KeyedService.AnyKey)
                      && Equals(descriptor.ServiceKey, key);
                if (!matches)
                {
                    continue;
                }

                AddEnumerableItem(items, descriptor, elementType, key);
            }
        }

        // enumerable 自体のキャッシュ位置は要素の最弱に合わせる
        // The cache location of the enumerable itself follows the weakest element.
        var cache = ResultCache.Root;
        foreach (var item in items)
        {
            if (item.Cache == ResultCache.None)
            {
                cache = ResultCache.None;
                break;
            }

            if (item.Cache == ResultCache.Scoped)
            {
                cache = ResultCache.Scoped;
            }
        }

        if (items.Count == 0)
        {
            cache = ResultCache.None;
        }

        // 生成 enumerable ファクトリ: 要素構成 (数・順序・実装・lifetime) が生成時の前提と一致する場合のみ
        // 配列リテラル実体化を採用する。不一致 (実行時追加・差し替え等) は従来の accessor 経由で実体化する
        // Generated enumerable factory: adopted only when the element composition (count, order, implementations and
        // lifetimes) matches the generation-time assumptions; mismatches keep the accessor-based materialization.
        if (key is null
            && cache == ResultCache.None
            && GeneratedComponentRegistry.TryGetEnumerable(elementType, out var generatedEnumerable)
            && EnumerableElementsMatch(items, generatedEnumerable.ElementImplementationTypes))
        {
            return new FactoryAccessor(generatedEnumerable.Factory, ResultCache.None, -1, trackDisposable: false);
        }

        return new EnumerableAccessor(elementType, items.ToArray(), cache, cache == ResultCache.Scoped ? NextSlot() : -1);
    }

    // 各要素が「前提どおりの実装型の生成ファクトリによる transient 解決」であることを登録順に検証する
    // Validates in registration order that every element resolves as a transient through the assumed implementation's generated factory.
    private static bool EnumerableElementsMatch(List<ServiceAccessor> items, Type[] expected)
    {
        if (items.Count != expected.Length)
        {
            return false;
        }

        for (var i = 0; i < expected.Length; i++)
        {
            if (!GeneratedComponentRegistry.TryGet(expected[i], out var entry)
                || !UsesGeneratedFactory(items[i], entry, ResultCache.None))
            {
                return false;
            }
        }

        return true;
    }

    private void AddEnumerableItem(List<ServiceAccessor> items, ServiceDescriptor descriptor, Type elementType, object? requestedKey)
    {
        if (ReferenceEquals(descriptor.ServiceType, elementType))
        {
            var accessor = TryRealizeDescriptor(descriptor, elementType, requestedKey);
            if (accessor is not null)
            {
                items.Add(accessor);
            }
        }
        else if (elementType.IsConstructedGenericType && ReferenceEquals(descriptor.ServiceType, elementType.GetGenericTypeDefinition()))
        {
            var accessor = TryRealizeDescriptor(descriptor, elementType, requestedKey);
            if (accessor is not null)
            {
                items.Add(accessor);
            }
        }
    }

    //--------------------------------------------------------------------------------
    // IsService (shallow: 実現せず登録の有無だけを判定 / checks registration presence without realizing)
    //--------------------------------------------------------------------------------

    public bool IsService(ServiceIdentifier id)
    {
        var serviceType = id.ServiceType;
        if (serviceType.IsGenericTypeDefinition)
        {
            return false;
        }

        if (id.Key is null
            && (serviceType == typeof(IServiceProvider)
                || serviceType == typeof(IServiceScopeFactory)
                || serviceType == typeof(IServiceProviderIsService)
                || serviceType == typeof(IServiceProviderIsKeyedService)))
        {
            return true;
        }

        if (ReferenceEquals(id.Key, KeyedService.AnyKey))
        {
            return keyedServiceTypes.Contains(serviceType)
                   || (serviceType.IsConstructedGenericType && keyedServiceTypes.Contains(serviceType.GetGenericTypeDefinition()));
        }

        if (exactMap.ContainsKey(id))
        {
            return true;
        }

        if (id.Key is not null && exactMap.ContainsKey(new ServiceIdentifier(serviceType, KeyedService.AnyKey)))
        {
            return true;
        }

        if (serviceType.IsConstructedGenericType)
        {
            var definition = serviceType.GetGenericTypeDefinition();
            if (definition == typeof(IEnumerable<>))
            {
                return true;
            }

            if (exactMap.ContainsKey(new ServiceIdentifier(definition, id.Key)))
            {
                return true;
            }

            if (id.Key is not null && exactMap.ContainsKey(new ServiceIdentifier(definition, KeyedService.AnyKey)))
            {
                return true;
            }
        }

        return false;
    }

    //--------------------------------------------------------------------------------
    // Inner
    //--------------------------------------------------------------------------------

    private readonly struct AccessorCacheKey : IEquatable<AccessorCacheKey>
    {
        private readonly ServiceDescriptor descriptor;
        private readonly Type serviceType;
        private readonly object? key;

        public AccessorCacheKey(ServiceDescriptor descriptor, Type serviceType, object? key)
        {
            this.descriptor = descriptor;
            this.serviceType = serviceType;
            this.key = key;
        }

        public bool Equals(AccessorCacheKey other) =>
            ReferenceEquals(descriptor, other.descriptor)
            && ReferenceEquals(serviceType, other.serviceType)
            && (key is null ? other.key is null : other.key is not null && key.Equals(other.key));

        public override bool Equals(object? obj) => obj is AccessorCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = RuntimeHelpers.GetHashCode(descriptor) * 397;
            hash ^= RuntimeHelpers.GetHashCode(serviceType);
            return key is null ? hash : (hash * 397) ^ key.GetHashCode();
        }
    }
}
