namespace BunnyTail.DependencyInjection.Generator;

using Microsoft.CodeAnalysis;

internal static class Diagnostics
{
    // 指定の解析 (BTDI0001-0004) / directive parsing
    public static DiagnosticDescriptor InvalidMethodDefinition { get; } = new(
        id: "BTDI0001",
        title: "Invalid registration method",
        messageFormat: "[ComponentRegistration] method must be a static partial extension. method=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

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

    // 型の解析 (BTDI0005-0007) / per-type analysis
    public static DiagnosticDescriptor AmbiguousConstructor { get; } = new(
        id: "BTDI0005",
        title: "Ambiguous constructor",
        messageFormat: "Maximum parameter count is not unique. type=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor InvalidPostConstruct { get; } = new(
        id: "BTDI0006",
        title: "Invalid PostConstruct method",
        messageFormat: "Method must be public parameterless void. type=[{1}] method=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor ConflictingPostConstruct { get; } = new(
        id: "BTDI0007",
        title: "Conflicting PostConstruct specifications",
        messageFormat: "PostConstruct specifications conflict. type=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // 依存グラフの解析 (BTDI0008-0010) / dependency graph analysis
    public static DiagnosticDescriptor CircularDependency { get; } = new(
        id: "BTDI0008",
        title: "Circular dependency",
        messageFormat: "Dependency chain forms a cycle. chain=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

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

    // 生成の限界 (BTDI0011) / generation limit
    public static DiagnosticDescriptor ValueTypeRuntimeGeneric { get; } = new(
        id: "BTDI0011",
        title: "Value type generic on runtime path",
        messageFormat: "Value type generic has no factory. type=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // 指定の矛盾 (BTDI0012) / conflicting specification
    public static DiagnosticDescriptor ConflictingInterfaceDelegate { get; } = new(
        id: "BTDI0012",
        title: "Conflicting interface delegate",
        messageFormat: "As and WithInterfaces conflict. type=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // 空振り (BTDI0013) / no match
    public static DiagnosticDescriptor PatternNoMatch { get; } = new(
        id: "BTDI0013",
        title: "Pattern matched no type",
        messageFormat: "No type matched the pattern. pattern=[{0}]",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
