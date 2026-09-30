namespace BunnyTail.DependencyInjection.Generator;

using Microsoft.CodeAnalysis;

using SourceGenerateHelper;

internal static class Diagnostics
{
    // Directive parsing

    public static DiagnosticDescriptor InvalidMethodDefinition { get; } = new(
        id: "BTDI0001",
        title: "Invalid registration method",
        messageFormat: "[ComponentRegistration] method must be a static partial extension. method=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: DiagnosticTags.NotSuppressible);

    public static DiagnosticDescriptor InvalidPattern { get; } = new(
        id: "BTDI0002",
        title: "Invalid registration pattern",
        messageFormat: "Pattern is not a valid regex. pattern=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor AssemblyNotFound { get; } = new(
        id: "BTDI0003",
        title: "Referenced assembly not found",
        messageFormat: "[ComponentRegistration] assembly is not referenced. assembly=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor InvalidGenerateComponentFactoryTarget { get; } = new(
        id: "BTDI0004",
        title: "Invalid GenerateComponentFactory target",
        messageFormat: "Type must be a public concrete class. type=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // Per-type analysis

    public static DiagnosticDescriptor AmbiguousConstructor { get; } = new(
        id: "BTDI0005",
        title: "Ambiguous constructor",
        messageFormat: "Maximum parameter count is not unique, so the runtime path selects the constructor. type=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor InvalidPostConstruct { get; } = new(
        id: "BTDI0006",
        title: "Invalid PostConstruct method",
        messageFormat: "Method must be public parameterless void. type=[{1}] method=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: DiagnosticTags.NotSuppressible);

    public static DiagnosticDescriptor ConflictingPostConstruct { get; } = new(
        id: "BTDI0007",
        title: "Conflicting PostConstruct specifications",
        messageFormat: "PostConstruct specifications conflict. type=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: DiagnosticTags.NotSuppressible);

    public static DiagnosticDescriptor InvalidInjectProperty { get; } = new(
        id: "BTDI0014",
        title: "Invalid Inject property",
        messageFormat: "[Inject] property must have a public setter. type=[{1}] property=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: DiagnosticTags.NotSuppressible);

    // Dependency graph analysis

    public static DiagnosticDescriptor CircularDependency { get; } = new(
        id: "BTDI0008",
        title: "Circular dependency",
        messageFormat: "Dependency chain forms a cycle. chain=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: DiagnosticTags.NotSuppressible);

    public static DiagnosticDescriptor UnresolvedDependency { get; } = new(
        id: "BTDI0009",
        title: "Unresolved dependency",
        messageFormat: "Dependency is not resolvable. type=[{1}] dependency=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor CaptiveDependency { get; } = new(
        id: "BTDI0010",
        title: "Captive dependency",
        messageFormat: "Singleton depends on scoped service. type=[{0}] dependency=[{1}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // Generation limit

    public static DiagnosticDescriptor ValueTypeRuntimeGeneric { get; } = new(
        id: "BTDI0011",
        title: "Value type generic on runtime path",
        messageFormat: "Value type generic has no factory. type=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // Conflicting specification

    public static DiagnosticDescriptor ConflictingInterfaceDelegate { get; } = new(
        id: "BTDI0012",
        title: "Conflicting interface delegate",
        messageFormat: "As and WithInterfaces conflict. type=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // No match

    public static DiagnosticDescriptor PatternNoMatch { get; } = new(
        id: "BTDI0013",
        title: "Pattern matched no type",
        messageFormat: "No type matched the pattern. pattern=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor UndefinedEnumValue { get; } = new(
        id: "BTDI0015",
        title: "Undefined enum value",
        messageFormat: "Attribute argument is not a defined value. argument=[{0}], value=[{1}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: DiagnosticTags.NotSuppressible);

    public static DiagnosticDescriptor ComponentNotReferable { get; } = new(
        id: "BTDI0016",
        title: "Component not referable",
        messageFormat: "Component cannot be referred to from the generated code, and is not registered. type=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: DiagnosticTags.NotSuppressible);

    public static DiagnosticDescriptor CandidateNotReferable { get; } = new(
        id: "BTDI0017",
        title: "Class not referable",
        messageFormat: "Class matched by the pattern cannot be referred to from the generated code, and is not registered. class=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor HintNameCollision { get; } = new(
        id: "BTDI0018",
        title: "Class name differs only in case",
        messageFormat: "Class name differs only in case from another class, and its registration methods are not generated. class=[{0}], other=[{1}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: DiagnosticTags.NotSuppressible);
}
