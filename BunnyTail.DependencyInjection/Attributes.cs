namespace BunnyTail.DependencyInjection;

//--------------------------------------------------------------------------------
// Scope
//--------------------------------------------------------------------------------

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class SingletonAttribute : Attribute
{
    public Type? As { get; set; }

    public bool WithInterfaces { get; set; }

    public object? Key { get; set; }

    public string? PostConstruct { get; set; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ScopedAttribute : Attribute
{
    public Type? As { get; set; }

    public bool WithInterfaces { get; set; }

    public object? Key { get; set; }

    public string? PostConstruct { get; set; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class TransientAttribute : Attribute
{
    public Type? As { get; set; }

    public bool WithInterfaces { get; set; }

    public object? Key { get; set; }

    public string? PostConstruct { get; set; }

    public DisposableTracking Tracking { get; set; }
}

//--------------------------------------------------------------------------------
// Property injection
//--------------------------------------------------------------------------------

[AttributeUsage(AttributeTargets.Property)]
public sealed class InjectAttribute : Attribute
{
    public object? Key { get; set; }
}

//--------------------------------------------------------------------------------
// Convention based
//--------------------------------------------------------------------------------

#pragma warning disable CA1724
public enum Lifetime
{
    Transient = 0,
    Singleton = 1,
    Scoped = 2
}
#pragma warning restore CA1724

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ComponentRegistrationAttribute : Attribute
{
    public Lifetime Lifetime { get; }

    public string Pattern { get; }

    public string? Namespace { get; set; }

    public string? Assembly { get; set; }

    public Type? As { get; set; }

    public bool WithInterfaces { get; set; }

    public ComponentRegistrationAttribute(
        Lifetime lifetime,
        [System.Diagnostics.CodeAnalysis.StringSyntax(System.Diagnostics.CodeAnalysis.StringSyntaxAttribute.Regex)] string pattern)
    {
        Lifetime = lifetime;
        Pattern = pattern;
    }
}

//--------------------------------------------------------------------------------
// Assembly module
//--------------------------------------------------------------------------------

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ComponentModuleAttribute : Attribute
{
    public Type ModuleType { get; }

    public ComponentModuleAttribute(Type moduleType)
    {
        ModuleType = moduleType;
    }
}

//--------------------------------------------------------------------------------
// Generated factory
//--------------------------------------------------------------------------------

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class GenerateComponentFactoryAttribute : Attribute
{
    public Type ImplementationType { get; }

    public string? PostConstruct { get; set; }

    public GenerateComponentFactoryAttribute(Type implementationType)
    {
        ImplementationType = implementationType;
    }
}
