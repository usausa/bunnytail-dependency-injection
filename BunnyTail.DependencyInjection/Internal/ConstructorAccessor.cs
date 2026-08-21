namespace BunnyTail.DependencyInjection.Internal;

using System.Reflection;
using System.Runtime.ExceptionServices;

internal sealed class ConstructorAccessor : ServiceAccessor
{
    private readonly ConstructorInvoker invoker;

    private readonly ParameterPlan[] plans;

    private readonly PropertyInjection[] properties;

    private readonly MethodInfo? postConstruct;

    private readonly bool initializable;

    public ConstructorAccessor(ConstructorInfo constructor, ParameterPlan[] plans, PropertyInjection[] properties, MethodInfo? postConstruct, bool initializable, ResultCache cache, int slot, bool trackDisposable)
        : base(cache, slot, trackDisposable)
    {
        invoker = ConstructorInvoker.Create(constructor);
        this.plans = plans;
        this.properties = properties;
        this.postConstruct = postConstruct;
        this.initializable = initializable;
    }

    protected override object Create(ServiceProviderScope scope)
    {
        object instance;
        if (plans.Length == 0)
        {
            instance = invoker.Invoke();
        }
        else
        {
            var arguments = new object?[plans.Length];
            for (var i = 0; i < plans.Length; i++)
            {
                arguments[i] = plans[i].Resolve(scope);
            }

            instance = invoker.Invoke(arguments.AsSpan());
        }

        // Property injection
        for (var i = 0; i < properties.Length; i++)
        {
            properties[i].Property.SetValue(instance, properties[i].Plan.Resolve(scope));
        }

        // Initialization
        if (postConstruct is not null)
        {
            try
            {
                postConstruct.Invoke(instance, null);
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }
        else if (initializable)
        {
            ((IInitializable)instance).Initialize();
        }

        return instance;
    }
}
