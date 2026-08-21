namespace BunnyTail.DependencyInjection.Internal;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

using BunnyTail.DependencyInjection.Accessors;

using Microsoft.Extensions.DependencyInjection;

internal sealed class ServiceRegistry
{
    [ThreadStatic]
    private static List<ServiceIdentifier>? realizationStack;

    internal static readonly ServiceRegistry DisposedSentinel = new();

    // Registrations

    private readonly ServiceDescriptor[] descriptors;

    private readonly Dictionary<ServiceIdentifier, List<ServiceDescriptor>> exactMap;

    private readonly HashSet<Type> keyedServiceTypes;

    // Entries

    private readonly ConcurrentDictionary<ServiceIdentifier, ServiceAccessor?> entries = new();

    private readonly ConcurrentDictionary<AccessorCacheKey, ServiceAccessor> descriptorAccessors = new();

    // Lookup

    private FixedTypeServiceTable typeTable;

    private FixedKeyedServiceTable keyedTable;

    // Promotion

    private readonly List<KeyValuePair<Type, ServiceAccessor>>? typeTableEntries;

    private readonly List<(Type Type, object Key, ServiceAccessor Accessor)>? keyedTableEntries;

    // Misc

    private readonly Lock tableSync = new();

    private int slotCounter;

    private readonly bool disposedSentinel;

    //--------------------------------------------------------------------------------
    // Constructor
    //--------------------------------------------------------------------------------

    private ServiceRegistry()
    {
        disposedSentinel = true;
        typeTable = new FixedTypeServiceTable([]);
        keyedTable = new FixedKeyedServiceTable([]);
        descriptors = [];
        exactMap = [];
        keyedServiceTypes = [];
    }

    public ServiceRegistry(IEnumerable<ServiceDescriptor> source, GeneratedServiceProvider provider)
    {
        typeTable = new FixedTypeServiceTable([]);
        keyedTable = new FixedKeyedServiceTable([]);

        descriptors = [.. source];
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

        // Built-in
        entries[new ServiceIdentifier(typeof(IServiceProvider), null)] = new ServiceProviderAccessor();
        entries[new ServiceIdentifier(typeof(IServiceScopeFactory), null)] = new ConstantAccessor(provider);
        entries[new ServiceIdentifier(typeof(IServiceProviderIsService), null)] = new ConstantAccessor(provider);
        entries[new ServiceIdentifier(typeof(IServiceProviderIsKeyedService), null)] = new ConstantAccessor(provider);

        // Warmup
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
            if (id.ServiceType.IsGenericTypeDefinition ||
                ReferenceEquals(id.Key, KeyedService.AnyKey) ||
                (id.Key is null && typeEntries.Exists(x => ReferenceEquals(x.Key, id.ServiceType))))
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

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private int NextSlot() => Interlocked.Increment(ref slotCounter) - 1;

    //--------------------------------------------------------------------------------
    // Diagnostics
    //--------------------------------------------------------------------------------

    internal List<Diagnostics.ServiceFactoryReportEntry> CreateFactoryReport()
    {
        var report = new List<Diagnostics.ServiceFactoryReportEntry>();
        foreach (var pair in exactMap)
        {
            var descriptor = pair.Value[^1];
            var key = descriptor.IsKeyedService ? descriptor.ServiceKey : null;
            var implementationType = descriptor.IsKeyedService ? descriptor.KeyedImplementationType : descriptor.ImplementationType;

            if ((implementationType is null) || implementationType.IsGenericTypeDefinition || descriptor.ServiceType.IsGenericTypeDefinition)
            {
                report.Add(new Diagnostics.ServiceFactoryReportEntry(descriptor.ServiceType, implementationType, key, descriptor.Lifetime, Diagnostics.ServiceFactoryStatus.NotApplicable));
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
                FactoryAccessor or DependencyFactoryAccessor or KeyedFactoryAccessor or KeyedDependencyFactoryAccessor => Diagnostics.ServiceFactoryStatus.Generated,
                ConstructorAccessor => Diagnostics.ServiceFactoryStatus.RuntimeFallback,
                null => Diagnostics.ServiceFactoryStatus.Unresolvable,
                _ => Diagnostics.ServiceFactoryStatus.NotApplicable
            };
            report.Add(new Diagnostics.ServiceFactoryReportEntry(descriptor.ServiceType, implementationType, key, descriptor.Lifetime, status));
        }

        return report;
    }

    //--------------------------------------------------------------------------------
    // Entry
    //--------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ServiceAccessor? GetEntry(ServiceIdentifier id)
    {
        var fixedAccessor = id.Key is null ? typeTable.Get(id.ServiceType) : keyedTable.Get(id.ServiceType, id.Key);
        return fixedAccessor ?? GetEntrySlow(id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? ResolveType(Type serviceType, ServiceProviderScope scope)
    {
        return typeTable.TryResolve(serviceType, scope, out var value) ? value : GetEntrySlow(new ServiceIdentifier(serviceType, null))?.GetValue(scope);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? ResolveKeyed(Type serviceType, object serviceKey, ServiceProviderScope scope)
    {
        return keyedTable.TryResolve(serviceType, serviceKey, scope, out var value) ? value : GetEntrySlow(new ServiceIdentifier(serviceType, serviceKey))?.GetValue(scope);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private ServiceAccessor? GetEntrySlow(ServiceIdentifier id)
    {
        // IEnumerable, Closed generic, AnyKey, Unrealized registration, Unknown type

        ObjectDisposedException.ThrowIf(disposedSentinel, typeof(IServiceProvider));

        if (entries.TryGetValue(id, out var existing))
        {
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void PromoteEntry(ServiceIdentifier id, ServiceAccessor accessor)
    {
        lock (tableSync)
        {
            if ((typeTableEntries is null) || (keyedTableEntries is null))
            {
                // During warmup
                return;
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

    // ReSharper disable ParameterTypeCanBeEnumerable.Local
    private static InvalidOperationException CreateCircularDependencyException(List<ServiceIdentifier> stack, int startIndex, ServiceIdentifier id)
    {
        var chain = String.Join(" -> ", stack.Skip(startIndex).Select(static x => TypeNameHelper.GetTypeDisplayName(x.ServiceType)));
        var message = $"A circular dependency was detected for the service of type '{TypeNameHelper.GetTypeDisplayName(id.ServiceType)}'." +
                      Environment.NewLine +
                      chain +
                      " -> " + TypeNameHelper.GetTypeDisplayName(id.ServiceType);
        return new InvalidOperationException(message);
    }
    // ReSharper restore ParameterTypeCanBeEnumerable.Local

    private ServiceAccessor? CreateEntry(ServiceIdentifier id)
    {
        var serviceType = id.ServiceType;
        if (serviceType.IsGenericTypeDefinition)
        {
            return null;
        }

        // Exact match
        if (exactMap.TryGetValue(id, out var list))
        {
            return TryRealizeDescriptor(list[^1], serviceType, id.Key);
        }

        // Check for AnyKey match
        if (id.Key is not null &&
            !ReferenceEquals(id.Key, KeyedService.AnyKey) &&
            exactMap.TryGetValue(new ServiceIdentifier(serviceType, KeyedService.AnyKey), out var anyList))
        {
            return TryRealizeDescriptor(anyList[^1], serviceType, id.Key);
        }

        if (serviceType.IsConstructedGenericType)
        {
            var definition = serviceType.GetGenericTypeDefinition();

            // IEnumerable<T>
            if (definition == typeof(IEnumerable<>))
            {
                return CreateEnumerableAccessor(serviceType.GenericTypeArguments[0], id.Key);
            }

            // Open generics
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

            if (id.Key is not null && !ReferenceEquals(id.Key, KeyedService.AnyKey) &&
                exactMap.TryGetValue(new ServiceIdentifier(definition, KeyedService.AnyKey), out var anyOpenList))
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
    // Descriptor
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
                // Constant
                return new ConstantAccessor(descriptor.ImplementationInstance);
            }

            if (descriptor.ImplementationFactory is not null)
            {
                // Factory
                return new FactoryAccessor(descriptor.ImplementationFactory, cache, cache == ResultCache.Scoped ? NextSlot() : -1, trackDisposable: true);
            }

            return CreateConstructorAccessor(descriptor.ImplementationType!, serviceType, cache, serviceKey: null);
        }

        if (descriptor.KeyedImplementationInstance is not null)
        {
            // Constant
            return new ConstantAccessor(descriptor.KeyedImplementationInstance);
        }

        if (descriptor.KeyedImplementationFactory is not null)
        {
            // Keyed
            return new KeyedFactoryAccessor(descriptor.KeyedImplementationFactory, effectiveKey, cache, cache == ResultCache.Scoped ? NextSlot() : -1, trackDisposable: true);
        }

        return CreateConstructorAccessor(descriptor.KeyedImplementationType!, serviceType, cache, serviceKey: effectiveKey);
    }

    [UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "Only open generics with value type arguments can fail at runtime, which is documented as a runtime path limitation.")]
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "Constructors of the closed type are preserved through the metadata of the registered open generic implementation type.")]
    [UnconditionalSuppressMessage("Trimming", "IL2068", Justification = "Same as above.")]
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private static Type MakeClosedGenericType(Type definition, Type[] typeArguments) => definition.MakeGenericType(typeArguments);

    //--------------------------------------------------------------------------------
    // Constructor
    //--------------------------------------------------------------------------------

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
                // Invalid closed generic type
                return null;
            }
        }

        var slot = cache == ResultCache.Scoped ? NextSlot() : -1;

        var constructors = implType.GetConstructors();
        if (constructors.Length == 0)
        {
            if (implType.IsValueType)
            {
                // Value type
                return new ValueTypeAccessor(implType, typeof(IInitializable).IsAssignableFrom(implType), cache, slot, IsDisposableType(implType));
            }

            throw new InvalidOperationException($"A suitable constructor for type '{implType}' could not be located. Ensure the type is concrete and all parameters of a public constructor are either registered as services or passed as arguments. Also ensure no extraneous arguments are provided.");
        }

        if (constructors.Length == 1)
        {
            var plans = BuildParameterPlans(constructors[0], implType, serviceKey, throwOnMiss: true)!;
            return CreateFinalAccessor(implType, constructors[0], plans, serviceKey, cache, slot);
        }

        // Select best constructor according to the MEDI rules
        Array.Sort(constructors, static (x, y) => y.GetParameters().Length.CompareTo(x.GetParameters().Length));

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
                // Current is better
            }
            else
            {
                throw new InvalidOperationException($"Unable to activate type '{implType}'. The following constructors are ambiguous:{Environment.NewLine}{best}{Environment.NewLine}{constructor}");
            }
        }

        if (best is null)
        {
            throw new InvalidOperationException($"No constructor for type '{implType}' can be instantiated using services from the service container and default values.");
        }

        return CreateFinalAccessor(implType, best, bestPlans!, serviceKey, cache, slot);
    }

    private static bool IsDisposableType(Type type) => typeof(IDisposable).IsAssignableFrom(type) || typeof(IAsyncDisposable).IsAssignableFrom(type);

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Types with a PostConstruct specification are preserved because generated code references them statically, and types registered only at runtime are documented as a limitation.")]
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

        if (GeneratedFactoryRegistry.TryGetInitializer(implType, out var registered))
        {
            name = registered;
        }

        if (name is not null)
        {
            var method = implType.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
            if (method is null || method.ReturnType != typeof(void) || method.IsGenericMethodDefinition)
            {
                throw new InvalidOperationException($"PostConstruct method must be a public parameterless instance method returning void. type=[{implType}] method=[{name}]");
            }

            return (method, false);
        }

        return (null, typeof(IInitializable).IsAssignableFrom(implType));
    }

    //--------------------------------------------------------------------------------
    // Generated factory
    //--------------------------------------------------------------------------------

    private ServiceAccessor CreateFinalAccessor(Type implType, ConstructorInfo constructor, ParameterPlan[] plans, object? serviceKey, ResultCache cache, int slot)
    {
        var track = IsDisposableType(implType);

        if (serviceKey is null)
        {
            if (GeneratedFactoryRegistry.TryGet(implType, out var generated) &&
                ConstructorMatches(constructor, generated.ConstructorParameterTypes) &&
                IsAllPlansAreServices(plans) &&
                InlinedDependenciesMatch(generated.InlinedDependencies))
            {
                if (generated.DependencyFactory is null)
                {
                    // Factory
                    return new FactoryAccessor(generated.Factory!, cache, slot, track);
                }

                if (TryResolveDependencies(generated.Dependencies, out var dependencyAccessors, out var dependencyHandles))
                {
                    // Dependency factory
                    return new DependencyFactoryAccessor(generated.DependencyFactory, dependencyAccessors, dependencyHandles, cache, slot, track);
                }
            }
        }
        else
        {
            if (GeneratedFactoryRegistry.TryGetKeyed(implType, out var generatedKeyed) &&
                ConstructorMatches(constructor, generatedKeyed.ConstructorParameterTypes) &&
                IsAllPlansAreServicesOrServiceKey(plans) &&
                InlinedDependenciesMatch(generatedKeyed.InlinedDependencies))
            {
                if (generatedKeyed.KeyedDependencyFactory is null)
                {
                    // Keyed
                    return new KeyedFactoryAccessor(generatedKeyed.Factory!, serviceKey, cache, slot, track);
                }

                if (TryResolveDependencies(generatedKeyed.Dependencies, out var keyedDependencyAccessors, out var keyedDependencyHandles))
                {
                    // Keyed dependency
                    return new KeyedDependencyFactoryAccessor(generatedKeyed.KeyedDependencyFactory, serviceKey, keyedDependencyAccessors, keyedDependencyHandles, cache, slot, track);
                }
            }
        }

        // Constructor
        var properties = BuildPropertyInjections(implType, serviceKey);
        var (postConstruct, initializable) = ResolveInitializer(implType);
        return new ConstructorAccessor(constructor, plans, properties, postConstruct, initializable, cache, slot, track);
    }

    // ReSharper disable ParameterTypeCanBeEnumerable.Local
    private static bool IsAllPlansAreServices(ParameterPlan[] plans)
    {
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var plan in plans)
        {
            if (!plan.IsService)
            {
                return false;
            }
        }

        return true;
    }
    // ReSharper restore ParameterTypeCanBeEnumerable.Local

    // ReSharper disable ParameterTypeCanBeEnumerable.Local
    private static bool IsAllPlansAreServicesOrServiceKey(ParameterPlan[] plans)
    {
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var plan in plans)
        {
            if (!plan.IsService && !plan.IsServiceKey)
            {
                return false;
            }
        }

        return true;
    }
    // ReSharper restore ParameterTypeCanBeEnumerable.Local

    // ReSharper disable ParameterTypeCanBeEnumerable.Local
    private bool InlinedDependenciesMatch(InlinedDependency[] dependencies)
    {
        foreach (var dependency in dependencies)
        {
            var accessor = GetEntry(new ServiceIdentifier(dependency.ServiceType, null));
            if (!GeneratedFactoryRegistry.TryGet(dependency.ImplementationType, out var entry) ||
                !UsesGeneratedFactory(accessor, entry, ResultCache.None))
            {
                return false;
            }
        }

        return true;
    }
    // ReSharper restore ParameterTypeCanBeEnumerable.Local

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

            if (!GeneratedFactoryRegistry.TryGet(dependencies[i].ImplementationType!, out var entry) ||
                !UsesGeneratedFactory(accessor, entry, ResultCache.Root))
            {
                return false;
            }

            accessors[i] = accessor!;
        }

        return true;
    }

    private static bool UsesGeneratedFactory(ServiceAccessor? accessor, GeneratedFactoryRegistry.Entry entry, ResultCache requiredCache)
    {
        return accessor switch
        {
            FactoryAccessor factory when factory.Cache == requiredCache => ReferenceEquals(factory.Factory, entry.Factory),
            DependencyFactoryAccessor withDependencies when withDependencies.Cache == requiredCache => ReferenceEquals(withDependencies.Factory, entry.DependencyFactory),
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

        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var i = 0; i < parameters.Length; i++)
        {
            if (!ReferenceEquals(parameters[i].ParameterType, assumedParameterTypes[i]))
            {
                return false;
            }
        }

        return true;
    }

    //--------------------------------------------------------------------------------
    // Injection
    //--------------------------------------------------------------------------------

    private static readonly PropertyInjection[] EmptyPropertyInjections = [];

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Types with [Inject] properties are preserved because generated code references them statically, and types registered only at runtime are documented as a limitation.")]
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
                throw new InvalidOperationException($"[Inject] property must have a public setter. type=[{implType}] property=[{property.Name}]");
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
                throw new InvalidOperationException($"Unable to resolve service for type '{property.PropertyType}' while attempting to activate '{implType}'.");
            }

            (list ??= []).Add(new PropertyInjection(property, ParameterPlan.FromService(accessor)));
        }

        return list is null ? EmptyPropertyInjections : [.. list];
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

            if (parameter.GetCustomAttribute<ServiceKeyAttribute>() is not null)
            {
                if (serviceKey is not null)
                {
                    if (!parameter.ParameterType.IsInstanceOfType(serviceKey))
                    {
                        throw new InvalidOperationException($"The type of the key '{serviceKey.GetType()}' used for lookup doesn't match the type '{parameter.ParameterType}' in the constructor parameter with the ServiceKey attribute.");
                    }

                    plans[i] = ParameterPlan.FromServiceKey(serviceKey);
                    continue;
                }

                if (ParameterDefaults.TryGetDefaultValue(parameter, out var keyDefault))
                {
                    plans[i] = ParameterPlan.FromConstant(keyDefault);
                    continue;
                }

                if (throwOnMiss)
                {
                    throw new InvalidOperationException($"Unable to resolve service for type '{parameter.ParameterType}' while attempting to activate '{implementationType}'.");
                }

                return null;
            }

            object? key = null;
            var fromKeyed = parameter.GetCustomAttribute<FromKeyedServicesAttribute>();
            if (fromKeyed is not null)
            {
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
                throw new InvalidOperationException($"Unable to resolve service for type '{parameter.ParameterType}' while attempting to activate '{implementationType}'.");
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
                var matches = key is null
                    ? !descriptor.IsKeyedService
                    : descriptor.IsKeyedService &&
                      !ReferenceEquals(descriptor.ServiceKey, KeyedService.AnyKey) &&
                      Equals(descriptor.ServiceKey, key);
                if (!matches)
                {
                    continue;
                }

                AddEnumerableItem(items, descriptor, elementType, key);
            }
        }

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

        if (key is null &&
            cache == ResultCache.None &&
            GeneratedFactoryRegistry.TryGetEnumerable(elementType, out var generatedEnumerable) &&
            IsEnumerableElementsMatch(items, generatedEnumerable.ElementImplementationTypes))
        {
            // Factory
            return new FactoryAccessor(generatedEnumerable.Factory, ResultCache.None, -1, trackDisposable: false);
        }

        // Enumerable
        return new EnumerableAccessor(elementType, [.. items], cache, cache == ResultCache.Scoped ? NextSlot() : -1);
    }

    private static bool IsEnumerableElementsMatch(List<ServiceAccessor> items, Type[] expected)
    {
        if (items.Count != expected.Length)
        {
            return false;
        }

        for (var i = 0; i < expected.Length; i++)
        {
            if (!GeneratedFactoryRegistry.TryGet(expected[i], out var entry) || !UsesGeneratedFactory(items[i], entry, ResultCache.None))
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
    // IsService
    //--------------------------------------------------------------------------------

    public bool IsService(ServiceIdentifier id)
    {
        var serviceType = id.ServiceType;
        if (serviceType.IsGenericTypeDefinition)
        {
            return false;
        }

        if ((id.Key is null) &&
            (serviceType == typeof(IServiceProvider) ||
             serviceType == typeof(IServiceScopeFactory) ||
             serviceType == typeof(IServiceProviderIsService) ||
             serviceType == typeof(IServiceProviderIsKeyedService)))
        {
            return true;
        }

        if (ReferenceEquals(id.Key, KeyedService.AnyKey))
        {
            return keyedServiceTypes.Contains(serviceType) || (serviceType.IsConstructedGenericType && keyedServiceTypes.Contains(serviceType.GetGenericTypeDefinition()));
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
            ReferenceEquals(descriptor, other.descriptor) &&
            ReferenceEquals(serviceType, other.serviceType) &&
            (key is null ? other.key is null : other.key is not null && key.Equals(other.key));

        public override bool Equals(object? obj) => obj is AccessorCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = RuntimeHelpers.GetHashCode(descriptor) * 397;
            hash ^= RuntimeHelpers.GetHashCode(serviceType);
            return key is null ? hash : (hash * 397) ^ key.GetHashCode();
        }
    }
}
