namespace BunnyTail.DependencyInjection.Generator;

using Microsoft.CodeAnalysis;

#pragma warning disable RS2008
internal static class Diagnostics
{
    // 指定の解析 (BTDI0001-0004) / directive parsing
    public static DiagnosticDescriptor InvalidMethodDefinition { get; } = new(
        id: "BTDI0001",
        title: "Invalid registration method",
        messageFormat: "Method must be a static partial extension method with an IServiceCollection parameter and return type. method=[{0}].",
        category: "BunnyTail.DependencyInjection",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor InvalidPattern { get; } = new(
        id: "BTDI0002",
        title: "Invalid registration pattern",
        messageFormat: "Invalid regex pattern. pattern=[{0}].",
        category: "BunnyTail.DependencyInjection",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor AssemblyNotFound { get; } = new(
        id: "BTDI0003",
        title: "Referenced assembly not found",
        messageFormat: "Assembly specified on [ComponentRegistration] is not referenced by this project. assembly=[{0}].",
        category: "BunnyTail.DependencyInjection",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor InvalidGenerateComponentFactoryTarget { get; } = new(
        id: "BTDI0004",
        title: "Invalid GenerateComponentFactory target",
        messageFormat: "Type must be a publicly accessible concrete class with a usable public constructor. type=[{0}].",
        category: "BunnyTail.DependencyInjection",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // 型の解析 (BTDI0005-0007) / per-type analysis
    public static DiagnosticDescriptor AmbiguousConstructor { get; } = new(
        id: "BTDI0005",
        title: "Ambiguous constructor",
        messageFormat: "Type has multiple public constructors with the same maximum parameter count. type=[{0}].",
        category: "BunnyTail.DependencyInjection",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor InvalidPostConstruct { get; } = new(
        id: "BTDI0006",
        title: "Invalid PostConstruct method",
        messageFormat: "PostConstruct method must be a public parameterless instance method returning void. type=[{1}] method=[{0}].",
        category: "BunnyTail.DependencyInjection",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor ConflictingPostConstruct { get; } = new(
        id: "BTDI0007",
        title: "Conflicting PostConstruct specifications",
        messageFormat: "Type has conflicting PostConstruct specifications across its lifetime attributes. type=[{0}].",
        category: "BunnyTail.DependencyInjection",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // 依存グラフの解析 (BTDI0008-0010) / dependency graph analysis
    public static DiagnosticDescriptor CircularDependency { get; } = new(
        id: "BTDI0008",
        title: "Circular dependency",
        messageFormat: "Circular dependency detected. chain=[{0}].",
        category: "BunnyTail.DependencyInjection",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor UnresolvedDependency { get; } = new(
        id: "BTDI0009",
        title: "Unresolved dependency",
        messageFormat: "Dependency cannot be resolved from the registrations visible at compile time. type=[{1}] dependency=[{0}].",
        category: "BunnyTail.DependencyInjection",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor CaptiveDependency { get; } = new(
        id: "BTDI0010",
        title: "Captive dependency",
        messageFormat: "Singleton component depends on scoped service. type=[{0}] dependency=[{1}].",
        category: "BunnyTail.DependencyInjection",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // 生成の限界 (BTDI0011) / generation limit
    public static DiagnosticDescriptor ValueTypeRuntimeGeneric { get; } = new(
        id: "BTDI0011",
        title: "Closed generic with value type arguments on the runtime path",
        messageFormat: "Closed generic with value type arguments has no generated factory and resolves through the runtime path, which is not supported on NativeAOT. type=[{0}].",
        category: "BunnyTail.DependencyInjection",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // 指定の矛盾 (BTDI0012) / conflicting specification
    public static DiagnosticDescriptor ConflictingInterfaceDelegate { get; } = new(
        id: "BTDI0012",
        title: "Conflicting interface registration",
        messageFormat: "As replaces the service type, so the implementation is not registered and the interface delegate has nothing to resolve. Specify only one of them. type=[{0}].",
        category: "BunnyTail.DependencyInjection",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
#pragma warning restore RS2008
