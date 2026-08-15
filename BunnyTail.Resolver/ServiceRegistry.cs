namespace BunnyTail.Resolver;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.DependencyInjection;

// ServiceDescriptor 集合から解決グラフ (accessor 群) をビルドするレジストリ (互換経路)。
// エントリ実現は初回解決時 (MEDI の callsite 構築と同タイミング)。実現済みエントリはイミュータブル。
internal sealed class ServiceRegistry
{
    [ThreadStatic]
    private static List<ServiceIdentifier>? realizationStack;

    private readonly ServiceDescriptor[] descriptors;                                          // 登録順 (IEnumerable 構築用)
    private readonly Dictionary<ServiceIdentifier, List<ServiceDescriptor>> exactMap;          // (ServiceType, key) → 登録順リスト
    private readonly HashSet<Type> keyedServiceTypes;                                          // AnyKey クエリ判定用
    private readonly ConcurrentDictionary<ServiceIdentifier, ServiceAccessor?> entries = new();
    private readonly ConcurrentDictionary<AccessorCacheKey, ServiceAccessor> descriptorAccessors = new();

    // 主テーブル (VB-01/VB-01b の勝者形状)。ビルド時に実現できたエントリを収め、以後イミュータブル。
    // 派生エントリ (IEnumerable / closed generic / AnyKey 派生) とビルド時に実現できなかった登録は overlay (entries) 側
    private readonly FixedTypeServiceTable typeTable;
    private readonly FixedKeyedServiceTable keyedTable;

    private int slotCounter;

    public ServiceRegistry(IEnumerable<ServiceDescriptor> source, ResolverServiceProvider provider)
    {
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

        // built-in (ユーザー登録より優先: MEDI 互換)
        entries[new ServiceIdentifier(typeof(IServiceProvider), null)] = new ServiceProviderAccessor();
        entries[new ServiceIdentifier(typeof(IServiceScopeFactory), null)] = new ConstantAccessor(provider);
        entries[new ServiceIdentifier(typeof(IServiceProviderIsService), null)] = new ConstantAccessor(provider);
        entries[new ServiceIdentifier(typeof(IServiceProviderIsKeyedService), null)] = new ConstantAccessor(provider);

        // ビルド時ウォームアップ: 実現可能な exact 登録を主テーブルへ固める。
        // 実現に失敗する登録 (未解決依存・循環など) はここでは無視し、初回 resolve 時に
        // 例外を投げ直す (MEDI 互換: ビルドは失敗させない)
        var typeEntries = new List<KeyValuePair<Type, ServiceAccessor>>
        {
            new(typeof(IServiceProvider), entries[new ServiceIdentifier(typeof(IServiceProvider), null)]!),
            new(typeof(IServiceScopeFactory), entries[new ServiceIdentifier(typeof(IServiceScopeFactory), null)]!),
            new(typeof(IServiceProviderIsService), entries[new ServiceIdentifier(typeof(IServiceProviderIsService), null)]!),
            new(typeof(IServiceProviderIsKeyedService), entries[new ServiceIdentifier(typeof(IServiceProviderIsKeyedService), null)]!),
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
    }

    private int NextSlot() => Interlocked.Increment(ref slotCounter) - 1;

    //--------------------------------------------------------------------------------
    // Entry realization
    //--------------------------------------------------------------------------------

    public ServiceAccessor? GetEntry(ServiceIdentifier id)
    {
        // 主テーブル (イミュータブル、同期なし)。ウォームアップ中は未構築なので overlay 側を使う
        if (id.Key is null)
        {
            var fixedAccessor = typeTable?.Get(id.ServiceType);
            if (fixedAccessor is not null)
            {
                return fixedAccessor;
            }
        }
        else
        {
            var fixedAccessor = keyedTable?.Get(id.ServiceType, id.Key);
            if (fixedAccessor is not null)
            {
                return fixedAccessor;
            }
        }

        if (entries.TryGetValue(id, out var existing))
        {
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
            return entries.GetOrAdd(id, created);
        }
        finally
        {
            stack.RemoveAt(stack.Count - 1);
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
        if (exactMap.TryGetValue(id, out var list))
        {
            return TryRealizeDescriptor(list[^1], serviceType, id.Key);
        }

        // 2. AnyKey 登録への concrete key 問い合わせ
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
    // Descriptor realization
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
            _ => ResultCache.None,
        };

        if (!descriptor.IsKeyedService)
        {
            if (descriptor.ImplementationInstance is not null)
            {
                return new ConstantAccessor(descriptor.ImplementationInstance);
            }

            if (descriptor.ImplementationFactory is not null)
            {
                return new FactoryAccessor(descriptor.ImplementationFactory, cache, cache == ResultCache.None ? -1 : NextSlot());
            }

            return CreateConstructorAccessor(descriptor.ImplementationType!, serviceType, cache, serviceKey: null);
        }

        if (descriptor.KeyedImplementationInstance is not null)
        {
            return new ConstantAccessor(descriptor.KeyedImplementationInstance);
        }

        if (descriptor.KeyedImplementationFactory is not null)
        {
            return new KeyedFactoryAccessor(descriptor.KeyedImplementationFactory, effectiveKey, cache, cache == ResultCache.None ? -1 : NextSlot());
        }

        return CreateConstructorAccessor(descriptor.KeyedImplementationType!, serviceType, cache, serviceKey: effectiveKey);
    }

    // open generic 定義から closed 型を作る (互換経路のみ)。
    // NativeAOT では参照型引数は shared generic で動作し、値型引数のみ実行時例外になり得る
    // (BTRS0007 診断で誘導予定。生成経路はコンパイル時に閉じるため影響しない)
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
                return null;
            }
        }

        var slot = cache == ResultCache.None ? -1 : NextSlot();

        var constructors = implType.GetConstructors();
        if (constructors.Length == 0)
        {
            if (implType.IsValueType)
            {
                return new ValueTypeAccessor(implType, cache, slot);
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
                // 既存の best が優位
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

    private ServiceAccessor CreateFinalAccessor(Type implType, ConstructorInfo constructor, ParameterPlan[] plans, object? serviceKey, ResultCache cache, int slot)
    {
        // 生成経路フック: MEDI 規則で選択されたコンストラクタが生成時の前提と一致し、
        // かつ全引数がサービス解決 (既定値フォールバックなし) の場合のみ生成ファクトリを採用する
        // (SPEC 2.3 のビルド時検証。不一致は互換経路へフォールバック)
        if (serviceKey is null)
        {
            if (GeneratedComponentRegistry.TryGet(implType, out var generated)
                && ConstructorMatches(constructor, generated.ConstructorParameterTypes)
                && AllPlansAreServices(plans))
            {
                return new FactoryAccessor(generated.Factory, cache, slot);
            }
        }
        else
        {
            // keyed: [ServiceKey] 注入は生成ファクトリが key 引数として受け取るため採用可
            if (GeneratedComponentRegistry.TryGetKeyed(implType, out var generatedKeyed)
                && ConstructorMatches(constructor, generatedKeyed.ConstructorParameterTypes)
                && AllPlansAreServicesOrServiceKey(plans))
            {
                return new KeyedFactoryAccessor(generatedKeyed.Factory, serviceKey, cache, slot);
            }
        }

        var properties = BuildPropertyInjections(implType, serviceKey);
        return new ConstructorAccessor(constructor, plans, properties, cache, slot);
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
    // 「トリミング環境 + 実行時のみ判明する登録 + [Inject] プロパティ」の組合せのみ制約 (ドキュメント化)
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

    private EnumerableAccessor CreateEnumerableAccessor(Type elementType, object? key)
    {
        var items = new List<ServiceAccessor>();

        if (ReferenceEquals(key, KeyedService.AnyKey))
        {
            // AnyKey クエリ: concrete key で登録された全 keyed サービス (AnyKey 登録は除外)
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

        return new EnumerableAccessor(elementType, items.ToArray(), cache, cache == ResultCache.None ? -1 : NextSlot());
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
    // IsService (shallow: 実現せず登録の有無だけを判定)
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
