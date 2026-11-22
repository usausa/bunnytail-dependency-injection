namespace BunnyTail.DependencyInjection.Accessors;

using System.Reflection;
using System.Runtime.CompilerServices;

internal sealed class ConstructorAccessor : ServiceAccessor
{
    private readonly ConstructorInvoker invoker;

    private readonly ParameterPlan[] plans;

    private readonly PropertyInjection[] properties;

    private readonly MethodInvoker? postConstruct;

    private readonly bool initializable;

    public ConstructorAccessor(ConstructorInfo constructor, ParameterPlan[] plans, PropertyInjection[] properties, MethodInfo? postConstruct, bool initializable, ResultCache cache, int slot, bool trackDisposable)
        : base(cache, slot, trackDisposable)
    {
        invoker = ConstructorInvoker.Create(constructor);
        this.plans = plans;
        this.properties = properties;
        this.postConstruct = postConstruct is null ? null : MethodInvoker.Create(postConstruct);
        this.initializable = initializable;
    }

    protected override object Create(ServiceProviderScope scope)
    {
        var p = plans;
        var instance = p.Length switch
        {
            0 => invoker.Invoke(),
            1 => invoker.Invoke(p[0].Resolve(scope)),
            2 => invoker.Invoke(p[0].Resolve(scope), p[1].Resolve(scope)),
            3 => invoker.Invoke(p[0].Resolve(scope), p[1].Resolve(scope), p[2].Resolve(scope)),
            4 => invoker.Invoke(p[0].Resolve(scope), p[1].Resolve(scope), p[2].Resolve(scope), p[3].Resolve(scope)),
            _ => InvokeMany(scope)
        };

        // Property injection
        foreach (ref readonly var injection in properties.AsSpan())
        {
            injection.Setter.Invoke(instance, injection.Plan.Resolve(scope));
        }

        // Initialization
        if (postConstruct is not null)
        {
            postConstruct.Invoke(instance);
        }
        else if (initializable)
        {
            ((IInitializable)instance).Initialize();
        }

        return instance;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private object InvokeMany(ServiceProviderScope scope)
    {
        var arguments = new object?[plans.Length];
        for (var i = 0; i < plans.Length; i++)
        {
            arguments[i] = plans[i].Resolve(scope);
        }

        return invoker.Invoke(arguments.AsSpan());
    }
}
