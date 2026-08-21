namespace BunnyTail.DependencyInjection.Generator;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;

using BunnyTail.DependencyInjection.Generator.Models;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using SourceGenerateHelper;

[Generator]
public sealed class DependencyInjectionGenerator : IIncrementalGenerator
{
    private const string SingletonAttributeName = "BunnyTail.DependencyInjection.SingletonAttribute";
    private const string ScopedAttributeName = "BunnyTail.DependencyInjection.ScopedAttribute";
    private const string TransientAttributeName = "BunnyTail.DependencyInjection.TransientAttribute";
    private const string InjectAttributeName = "BunnyTail.DependencyInjection.InjectAttribute";
    private const string InitializableInterfaceName = "global::BunnyTail.DependencyInjection.IInitializable";
    private const string ComponentRegistrationAttributeName = "BunnyTail.DependencyInjection.ComponentRegistrationAttribute";
    private const string FromKeyedServicesAttributeName = "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute";
    private const string ServiceKeyAttributeName = "Microsoft.Extensions.DependencyInjection.ServiceKeyAttribute";
    private const string ServiceCollectionExtensionsName = "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions";
    private const string ServiceCollectionDescriptorExtensionsName = "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions";
    private const string GenerateComponentFactoryAttributeName = "BunnyTail.DependencyInjection.GenerateComponentFactoryAttribute";
    private const string ServiceCollectionName = "Microsoft.Extensions.DependencyInjection.IServiceCollection";
    private const string ServiceDescriptorName = "Microsoft.Extensions.DependencyInjection.ServiceDescriptor";
    private const string ServiceDescriptorCollectionName = "System.Collections.Generic.ICollection<Microsoft.Extensions.DependencyInjection.ServiceDescriptor>";

    // 自動登録の対象から外すインタフェース (カンマ区切り)。IDisposable / IAsyncDisposable / IInitializable は常に除外
    // Interfaces excluded from automatic registration (comma separated). IDisposable / IAsyncDisposable / IInitializable are always excluded.
    private const string IgnoreInterfaceProperty = "build_property.DependencyInjectionIgnoreInterface";

    // ------------------------------------------------------------
    // Diagnostics
    // ------------------------------------------------------------

#pragma warning disable RS2008
    // 指定の解析 (BTDI0001-0004) / directive parsing
    private static readonly DiagnosticDescriptor InvalidMethodDefinition = new(
        "BTDI0001",
        "Invalid registration method",
        "Method must be a static partial extension method with an IServiceCollection parameter and return type. method=[{0}].",
        "BunnyTail.DependencyInjection",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor InvalidPattern = new(
        "BTDI0002",
        "Invalid registration pattern",
        "Invalid regex pattern. pattern=[{0}].",
        "BunnyTail.DependencyInjection",
        DiagnosticSeverity.Warning,
        true);

    private static readonly DiagnosticDescriptor AssemblyNotFound = new(
        "BTDI0003",
        "Referenced assembly not found",
        "Assembly specified on [ComponentRegistration] is not referenced by this project. assembly=[{0}].",
        "BunnyTail.DependencyInjection",
        DiagnosticSeverity.Warning,
        true);

    private static readonly DiagnosticDescriptor InvalidGenerateComponentFactoryTarget = new(
        "BTDI0004",
        "Invalid GenerateComponentFactory target",
        "Type must be a publicly accessible concrete class with a usable public constructor. type=[{0}].",
        "BunnyTail.DependencyInjection",
        DiagnosticSeverity.Warning,
        true);

    // 型の解析 (BTDI0005-0007) / per-type analysis
    private static readonly DiagnosticDescriptor AmbiguousConstructor = new(
        "BTDI0005",
        "Ambiguous constructor",
        "Type has multiple public constructors with the same maximum parameter count. type=[{0}].",
        "BunnyTail.DependencyInjection",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor InvalidPostConstruct = new(
        "BTDI0006",
        "Invalid PostConstruct method",
        "PostConstruct method must be a public parameterless instance method returning void. type=[{1}] method=[{0}].",
        "BunnyTail.DependencyInjection",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor ConflictingPostConstruct = new(
        "BTDI0007",
        "Conflicting PostConstruct specifications",
        "Type has conflicting PostConstruct specifications across its lifetime attributes. type=[{0}].",
        "BunnyTail.DependencyInjection",
        DiagnosticSeverity.Error,
        true);

    // 依存グラフの解析 (BTDI0008-0010) / dependency graph analysis
    private static readonly DiagnosticDescriptor CircularDependency = new(
        "BTDI0008",
        "Circular dependency",
        "Circular dependency detected. chain=[{0}].",
        "BunnyTail.DependencyInjection",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor UnresolvedDependency = new(
        "BTDI0009",
        "Unresolved dependency",
        "Dependency cannot be resolved from the registrations visible at compile time. type=[{1}] dependency=[{0}].",
        "BunnyTail.DependencyInjection",
        DiagnosticSeverity.Warning,
        true);

    private static readonly DiagnosticDescriptor CaptiveDependency = new(
        "BTDI0010",
        "Captive dependency",
        "Singleton component depends on scoped service. type=[{0}] dependency=[{1}].",
        "BunnyTail.DependencyInjection",
        DiagnosticSeverity.Warning,
        true);

    // 生成の限界 (BTDI0011) / generation limit
    private static readonly DiagnosticDescriptor ValueTypeRuntimeGeneric = new(
        "BTDI0011",
        "Closed generic with value type arguments on the runtime path",
        "Closed generic with value type arguments has no generated factory and resolves through the runtime path, which is not supported on NativeAOT. type=[{0}].",
        "BunnyTail.DependencyInjection",
        DiagnosticSeverity.Warning,
        true);
#pragma warning restore RS2008

    // ------------------------------------------------------------
    // Initialize
    // ------------------------------------------------------------

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 除外インタフェース指定 (値等価なので、指定が変わらない限り下流は再実行されない)
        // Ignored interface specification (value-equatable, so downstream reruns only when the specification changes).
        var ignoreInterfacesProvider = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => SelectIgnoreInterfaces(provider));

        var singletonProvider = CreateComponentProvider(context, SingletonAttributeName, "Singleton");
        var scopedProvider = CreateComponentProvider(context, ScopedAttributeName, "Scoped");
        var transientProvider = CreateComponentProvider(context, TransientAttributeName, "Transient");

        var collectedProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsAddInvocationSyntax(node),
                static (ctx, _) => CreateCollectedModel(ctx))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!);

        var methodProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ComponentRegistrationAttributeName,
                static (node, _) => node is MethodDeclarationSyntax,
                static (ctx, _) => CreateMethodModel(ctx));

        var candidateProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsCandidateClassSyntax(node),
                static (ctx, _) => CreateCandidateModel(ctx))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!);

        var openGenericProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsAddInvocationSyntax(node),
                static (ctx, _) => CreateOpenGenericModel(ctx))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!);

        // [assembly: GenerateComponentFactory(typeof(T))] — 登録は行わずファクトリだけを生成する
        // [assembly: GenerateComponentFactory(typeof(T))] generates the factory only, without any registration.
        var generateComponentFactoryProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GenerateComponentFactoryAttributeName,
                static (_, _) => true,
                static (ctx, _) => CreateGenerateComponentFactoryModels(ctx))
            .SelectMany(static (models, _) => models);

        var closedUsageProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is TypeOfExpressionSyntax { Type: GenericNameSyntax },
                static (ctx, _) => CreateClosedGenericUsageModel(ctx))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!);

        // コンストラクタ引数・プロパティ型に現れる closed generic も usage として収集する (依存駆動の発見)
        // Closed generics appearing as constructor parameter or property types are collected as usages too (dependency driven discovery).
        var dependencyUsageProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsGenericDependencySyntax(node),
                static (ctx, _) => CreateDependencyUsageModel(ctx))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!);

        // Compilation 依存の値は狭い Select で切り出し、値等価な形にしてから最終 Combine に載せる。
        // Compilation 自体を Combine すると毎編集で Execute (出力構築) がフル再実行されるため。
        // 毎コンパイルで再計算されるのは以下の 3 つ (アセンブリ名 / closed generic 解決 / 参照モジュール走査) のみで、
        // いずれも軽量。値が変わらない限り Execute はスキップされる
        // Compilation dependent values are extracted through narrow Selects into value-equatable shapes before the
        // final combine. Combining the Compilation itself would rerun Execute (output construction) on every edit.
        // Only these three (assembly name, closed generic resolution and the referenced module scan) recompute per
        // compilation, and all are lightweight; Execute is skipped as long as the values stay equal.
        var assemblyNameProvider = context.CompilationProvider
            .Select(static (compilation, _) => compilation.AssemblyName ?? "Generated");

        var referencedModulesProvider = context.CompilationProvider
            .Select(static (compilation, _) => CollectReferencedModules(compilation));

        var closedFactoriesProvider = openGenericProvider.Collect()
            .Combine(closedUsageProvider.Collect())
            .Combine(dependencyUsageProvider.Collect())
            .Combine(context.CompilationProvider)
            .Select(static (source, _) => DiscoverClosedGenericFactories(source.Left.Left.Left, source.Left.Left.Right, source.Left.Right, source.Right));

        // Assembly 指定つき規約パターンの外部走査。要求 (メソッド属性から抽出、値等価) が空なら即空を返す軽量パス。
        // 要求がある場合のみ対象アセンブリを走査し、結果も値等価なので Execute は変化時だけ再実行される
        // External scan for assembly-scoped convention patterns. The requests (extracted from method attributes,
        // value-equatable) short-circuit to an empty result when absent. Only requested assemblies are scanned and
        // the result is value-equatable, so Execute reruns only when it changes.
        var externalCandidatesProvider = methodProvider.Collect()
            .Select(static (methods, _) => CollectExternalRequests(methods))
            .Combine(context.CompilationProvider)
            .Select(static (source, _) => CollectExternalCandidates(source.Left, source.Right));

        var source = singletonProvider.Collect()
            .Combine(scopedProvider.Collect())
            .Combine(transientProvider.Collect())
            .Combine(collectedProvider.Collect())
            .Combine(methodProvider.Collect())
            .Combine(candidateProvider.Collect())
            .Combine(generateComponentFactoryProvider.Collect())
            .Combine(externalCandidatesProvider)
            .Combine(closedFactoriesProvider)
            .Combine(assemblyNameProvider)
            .Combine(referencedModulesProvider)
            .Combine(ignoreInterfacesProvider);

        context.RegisterSourceOutput(source, static (context, source) =>
            Execute(
                context,
                source.Left.Left.Left.Left.Left.Left.Left.Left.Left.Left.Left,
                source.Left.Left.Left.Left.Left.Left.Left.Left.Left.Left.Right,
                source.Left.Left.Left.Left.Left.Left.Left.Left.Left.Right,
                source.Left.Left.Left.Left.Left.Left.Left.Left.Right,
                source.Left.Left.Left.Left.Left.Left.Left.Right,
                source.Left.Left.Left.Left.Left.Left.Right,
                source.Left.Left.Left.Left.Left.Right,
                source.Left.Left.Left.Left.Right,
                source.Left.Left.Left.Right,
                source.Left.Left.Right,
                source.Left.Right,
                source.Right));
    }

    private static IncrementalValuesProvider<ComponentModel> CreateComponentProvider(IncrementalGeneratorInitializationContext context, string attributeName, string lifetime) =>
        context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeName,
                static (syntax, _) => syntax is ClassDeclarationSyntax,
                (ctx, _) => CreateComponentModels(ctx, lifetime))
            .SelectMany(static (models, _) => models);

    // ------------------------------------------------------------
    // Parser : shared factory analysis (共通ファクトリ解析)
    // ------------------------------------------------------------

    private static FactoryModel CreateFactoryModel(INamedTypeSymbol symbol, IAssemblySymbol compilationAssembly)
    {
        var implementationType = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // コンストラクタ選択: MEDI 規則の前提 = 最大パラメータの public コンストラクタ
        // Constructor selection: assumes MEDI rules = the public constructor with the most parameters.
        var constructors = symbol.InstanceConstructors
            .Where(static x => x.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(static x => x.Parameters.Length)
            .ToArray();
        var constructor = constructors.Length > 0 ? constructors[0] : null;

        // 同数の最大コンストラクタが複数あり、互いに superset でない場合は曖昧 (BTDI0005)
        // Ambiguous when multiple constructors share the maximum parameter count and are not supersets of each other (BTDI0005).
        var ambiguous = false;
        if ((constructors.Length > 1) && (constructors[0].Parameters.Length == constructors[1].Parameters.Length))
        {
            var first = new HashSet<string>(constructors[0].Parameters.Select(static x => x.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            var second = new HashSet<string>(constructors[1].Parameters.Select(static x => x.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            ambiguous = !first.SetEquals(second);
        }

        var eligibleUnkeyed = constructor is not null;
        var eligibleKeyed = constructor is not null;
        var parameters = new ParameterModel[constructor is not null ? constructor.Parameters.Length : 0];
        if (constructor is not null)
        {
            for (var i = 0; i < constructor.Parameters.Length; i++)
            {
                var parameter = constructor.Parameters[i];
                var (typeName, kind, keyLiteral, inCompilation, isValueType) = CreateDependencyModel(parameter.Type, parameter.GetAttributes(), compilationAssembly);
                parameters[i] = new ParameterModel(typeName, inCompilation, isValueType, kind, keyLiteral);

                // 既定値付き引数は生成ファクトリ不可 (GetRequiredService と挙動が変わるため互換経路へ)
                // Parameters with default values disqualify the generated factory (behavior differs from GetRequiredService; runtime path is used).
                if (parameter.HasExplicitDefaultValue)
                {
                    eligibleUnkeyed = false;
                    eligibleKeyed = false;
                }

                // keyed 依存 ([ServiceKey]/[FromKeyedServices]) は keyed ファクトリでのみ扱える
                // Keyed dependencies ([ServiceKey]/[FromKeyedServices]) can only be handled by keyed factories.
                if (kind != DependencyKinds.Service)
                {
                    eligibleUnkeyed = false;
                }
            }
        }

        // [Inject] プロパティの収集
        // Collect [Inject] properties.
        var injectProperties = new List<PropertyModel>();
        foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || !HasAttribute(property.GetAttributes(), InjectAttributeName))
            {
                continue;
            }

            if ((property.SetMethod is null) || (property.SetMethod.DeclaredAccessibility != Accessibility.Public))
            {
                continue;
            }

            var (typeName, kind, keyLiteral, inCompilation, isValueType) = CreateDependencyModel(property.Type, property.GetAttributes(), compilationAssembly);
            if (kind == DependencyKinds.ServiceKey)
            {
                // プロパティへの [ServiceKey] は非対応 / [ServiceKey] on properties is not supported
                eligibleUnkeyed = false;
                eligibleKeyed = false;
                continue;
            }

            if (kind != DependencyKinds.Service)
            {
                eligibleUnkeyed = false;
            }

            injectProperties.Add(new PropertyModel(property.Name, typeName, inCompilation, isValueType, kind, keyLiteral));
        }

        // IDisposable / IAsyncDisposable 実装型は disposal 追跡が必要なためインライン展開不可
        // Types implementing IDisposable / IAsyncDisposable need disposal tracking and cannot be inlined.
        var disposable = false;
        var initializableInterface = false;
        foreach (var interfaceType in symbol.AllInterfaces)
        {
            var interfaceName = interfaceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if ((interfaceType.SpecialType == SpecialType.System_IDisposable) || (interfaceName == "global::System.IAsyncDisposable"))
            {
                disposable = true;
            }
            else if (interfaceName == InitializableInterfaceName)
            {
                initializableInterface = true;
            }
        }

        // 初期化コールバック: ライフタイム属性の PostConstruct 指定を収集する (相違があれば BTDI0007)
        // Initialization callback: collect PostConstruct from lifetime attributes (BTDI0007 when they disagree).
        string? postConstruct = null;
        var conflictingPostConstruct = false;
        foreach (var attribute in symbol.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.ToDisplayString();
            if (attributeName is not (SingletonAttributeName or ScopedAttributeName or TransientAttributeName))
            {
                continue;
            }

            foreach (var argument in attribute.NamedArguments)
            {
                if ((argument.Key == "PostConstruct") && argument.Value.Value is string value)
                {
                    if (postConstruct is null)
                    {
                        postConstruct = value;
                    }
                    else if (postConstruct != value)
                    {
                        conflictingPostConstruct = true;
                    }
                }
            }
        }

        var invalidPostConstruct = (postConstruct is not null) && !HasValidPostConstructMethod(symbol, postConstruct);

        return new FactoryModel(
            implementationType,
            eligibleUnkeyed,
            eligibleKeyed,
            ambiguous,
            disposable,
            postConstruct,
            initializableInterface,
            invalidPostConstruct,
            conflictingPostConstruct,
            new EquatableArray<ParameterModel>(parameters),
            new EquatableArray<PropertyModel>([.. injectProperties]));
    }

    private static bool HasValidPostConstructMethod(INamedTypeSymbol symbol, string name)
    {
        for (var type = symbol; type is not null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers(name))
            {
                if (member is IMethodSymbol
                    {
                        IsStatic: false,
                        Parameters.Length: 0,
                        ReturnsVoid: true,
                        IsGenericMethod: false,
                        DeclaredAccessibility: Accessibility.Public,
                        MethodKind: MethodKind.Ordinary
                    })
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static (string TypeName, int Kind, string? KeyLiteral, bool InCompilation, bool IsValueType) CreateDependencyModel(ITypeSymbol type, ImmutableArray<AttributeData> attributes, IAssemblySymbol compilationAssembly)
    {
        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var inCompilation = SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilationAssembly);
        var isValueType = type.IsValueType;

        foreach (var attribute in attributes)
        {
            var attributeName = attribute.AttributeClass?.ToDisplayString();
            if (attributeName == ServiceKeyAttributeName)
            {
                return (typeName, DependencyKinds.ServiceKey, null, inCompilation, isValueType);
            }

            if (attributeName == FromKeyedServicesAttributeName)
            {
                if (attribute.ConstructorArguments.Length == 0)
                {
                    return (typeName, DependencyKinds.KeyedInherit, null, inCompilation, isValueType);
                }

                var argument = attribute.ConstructorArguments[0];
                if (argument.IsNull)
                {
                    // [FromKeyedServices(null)] = 非 keyed 解決 / resolves non-keyed
                    return (typeName, DependencyKinds.Service, null, inCompilation, isValueType);
                }

                return (typeName, DependencyKinds.KeyedExplicit, SymbolDisplay.FormatPrimitive(argument.Value!, quoteStrings: true, useHexadecimalNumbers: false), inCompilation, isValueType);
            }
        }

        return (typeName, DependencyKinds.Service, null, inCompilation, isValueType);
    }

    private static EquatableArray<string> CollectInterfaces(INamedTypeSymbol symbol)
    {
        var interfaces = symbol.AllInterfaces
            .Where(static x => x.SpecialType != SpecialType.System_IDisposable)
            .Select(static x => x.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .Where(static x => x is not ("global::System.IAsyncDisposable" or InitializableInterfaceName))
            .ToArray();
        return [with(interfaces)];
    }

    private static EquatableArray<string> SelectIgnoreInterfaces(AnalyzerConfigOptionsProvider provider)
    {
        if (!provider.GlobalOptions.TryGetValue(IgnoreInterfaceProperty, out var value) || String.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var names = value.Split(',')
            .Select(static x => x.Trim())
            .Where(static x => x.Length > 0)
            .ToArray();
        return [with(names)];
    }

    // 除外指定は名前空間つきの名前 (global:: なし) で比較する。ジェネリックは型引数まで含めた形が対象
    // Exclusions are compared by namespace qualified name without global::; generics match the form including type arguments.
    private static EquatableArray<string> FilterIgnoredInterfaces(EquatableArray<string> interfaces, EquatableArray<string> ignoreInterfaces)
    {
        if ((ignoreInterfaces.Count == 0) || (interfaces.Count == 0))
        {
            return interfaces;
        }

        var filtered = new List<string>(interfaces.Count);
        foreach (var interfaceType in interfaces)
        {
            if (!IsIgnoredInterface(interfaceType, ignoreInterfaces))
            {
                filtered.Add(interfaceType);
            }
        }

        // ReSharper disable UseCollectionExpression
#pragma warning disable IDE0028
        return filtered.Count == interfaces.Count ? interfaces : new([.. filtered]);
#pragma warning restore IDE0028
        // ReSharper restore UseCollectionExpression
    }

    private static bool IsIgnoredInterface(string fullyQualifiedName, EquatableArray<string> ignoreInterfaces)
    {
        var displayName = fullyQualifiedName.Replace("global::", string.Empty);
        foreach (var ignore in ignoreInterfaces)
        {
            if (String.Equals(displayName, ignore, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAttribute(ImmutableArray<AttributeData> attributes, string attributeName)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeClass?.ToDisplayString() == attributeName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsTypeParameter(ITypeSymbol type)
    {
        if (type is ITypeParameterSymbol)
        {
            return true;
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var argument in named.TypeArguments)
            {
                if (ContainsTypeParameter(argument))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ------------------------------------------------------------
    // Parser : attribute components (属性コンポーネント)
    // ------------------------------------------------------------

    private static ImmutableArray<ComponentModel> CreateComponentModels(GeneratorAttributeSyntaxContext context, string lifetime)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
        {
            return [];
        }

        if (symbol.IsAbstract || symbol.IsStatic || (symbol.TypeParameters.Length > 0))
        {
            return [];
        }

        var factory = CreateFactoryModel(symbol, context.SemanticModel.Compilation.Assembly);
        var interfaces = CollectInterfaces(symbol);
        var filePath = context.TargetNode.SyntaxTree.FilePath;
        var spanStart = context.TargetNode.SpanStart;
        var location = LocationInfo.CreateFrom(context.TargetNode);

        var models = ImmutableArray.CreateBuilder<ComponentModel>(context.Attributes.Length);
        foreach (var attribute in context.Attributes)
        {
            string? asType = null;
            string? keyLiteral = null;
            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key == "As")
                {
                    asType = (argument.Value.Value as ITypeSymbol)?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
                else if (argument.Key == "Key")
                {
                    keyLiteral = argument.Value.IsNull
                        ? null
                        : SymbolDisplay.FormatPrimitive(argument.Value.Value!, quoteStrings: true, useHexadecimalNumbers: false);
                }
            }

            models.Add(new ComponentModel(
                factory,
                lifetime,
                asType,
                keyLiteral,
                interfaces,
                filePath,
                spanStart,
                location));
        }

        return models.ToImmutable();
    }

    // ------------------------------------------------------------
    // Parser : Add* invocation collection (Add* 呼び出し収集)
    // ------------------------------------------------------------

    private static bool IsAddInvocationSyntax(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member })
        {
            return false;
        }

        var name = member.Name.Identifier.ValueText;
        return name.StartsWith("Add", StringComparison.Ordinal) || name.StartsWith("TryAdd", StringComparison.Ordinal);
    }

    private static CollectedModel? CreateCollectedModel(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return null;
        }

        var methodName = method.Name;
        var containingType = method.ContainingType?.ToDisplayString();

        // ServiceDescriptor 引数形: Add / TryAdd / TryAddEnumerable(ServiceDescriptor.{Lifetime}<S, I>())。
        // Describe や typeof 形の descriptor は lifetime が定数で読めないか稀なため対象外
        // ServiceDescriptor argument shapes: Add / TryAdd / TryAddEnumerable(ServiceDescriptor.{Lifetime}<S, I>()).
        // Describe and typeof shaped descriptors are excluded (the lifetime is not a readable constant, or they are rare).
        if (methodName is "Add" or "TryAdd" or "TryAddEnumerable")
        {
            if (containingType is not (ServiceDescriptorCollectionName or ServiceCollectionDescriptorExtensionsName))
            {
                return null;
            }

            if ((invocation.ArgumentList.Arguments.Count != 1)
                || (invocation.ArgumentList.Arguments[0].Expression is not InvocationExpressionSyntax descriptorInvocation)
                || (context.SemanticModel.GetSymbolInfo(descriptorInvocation).Symbol is not IMethodSymbol descriptorMethod)
                || (descriptorMethod.ContainingType?.ToDisplayString() != ServiceDescriptorName))
            {
                return null;
            }

            var descriptorLifetime = descriptorMethod.Name switch
            {
                "Singleton" => "Singleton",
                "Scoped" => "Scoped",
                "Transient" => "Transient",
                _ => null
            };
            if ((descriptorLifetime is null) || (descriptorMethod.TypeArguments.Length == 0))
            {
                return null;
            }

            if (HasFactoryOrInstanceParameter(descriptorMethod))
            {
                return null;
            }

            // TryAddEnumerable は複数登録の合成が前提なので、前提には参加させずファクトリ生成のみ
            // TryAddEnumerable implies multi-registration composition, so it only generates factories and never joins assumptions.
            var descriptorKind = methodName == "TryAddEnumerable" ? CollectedKinds.FactoryOnly : CollectedKinds.Direct;
            return CreateCollectedModelCore(
                context,
                invocation,
                descriptorMethod.TypeArguments[0],
                descriptorMethod.TypeArguments[descriptorMethod.TypeArguments.Length - 1],
                descriptorLifetime,
                descriptorKind);
        }

        // Add* / TryAdd* / AddKeyed* / TryAddKeyed* (ジェネリック + 非ジェネリック typeof オーバーロード)
        // Add* / TryAdd* / AddKeyed* / TryAddKeyed* (generic and non-generic typeof overloads).
        var (lifetime, keyed) = methodName switch
        {
            "AddSingleton" or "TryAddSingleton" => ("Singleton", false),
            "AddScoped" or "TryAddScoped" => ("Scoped", false),
            "AddTransient" or "TryAddTransient" => ("Transient", false),
            "AddKeyedSingleton" or "TryAddKeyedSingleton" => ("Singleton", true),
            "AddKeyedScoped" or "TryAddKeyedScoped" => ("Scoped", true),
            "AddKeyedTransient" or "TryAddKeyedTransient" => ("Transient", true),
            _ => (null, false)
        };
        if (lifetime is null)
        {
            return null;
        }

        if (containingType is not (ServiceCollectionExtensionsName or ServiceCollectionDescriptorExtensionsName))
        {
            return null;
        }

        // factory/instance オーバーロード (delegate / 型引数の実引数) はコンテナが型をインスタンス化しないため対象外
        // Factory/instance overloads (delegates or instance arguments) are excluded because the container does not instantiate the type.
        if (HasFactoryOrInstanceParameter(method))
        {
            return null;
        }

        ITypeSymbol serviceArgument;
        ITypeSymbol implementationArgument;
        if (method.TypeArguments.Length > 0)
        {
            serviceArgument = method.TypeArguments[0];
            implementationArgument = method.TypeArguments[method.TypeArguments.Length - 1];
        }
        else
        {
            // 非ジェネリック typeof オーバーロード。typeof 引数の先頭 = サービス、末尾 = 実装 (1 つなら自己登録)。
            // open generic 定義は openGenericProvider の担当なのでここでは対象外 (下の unbound 判定で除外)
            // Non-generic typeof overloads: the first typeof argument is the service and the last the implementation
            // (self registration when only one). Open generic definitions belong to openGenericProvider and are
            // rejected by the unbound check below.
            var typeofTypes = new List<ITypeSymbol>();
            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                if ((argument.Expression is TypeOfExpressionSyntax typeOf)
                    && (context.SemanticModel.GetTypeInfo(typeOf.Type).Type is { } typeSymbol))
                {
                    typeofTypes.Add(typeSymbol);
                }
            }

            if (typeofTypes.Count == 0)
            {
                return null;
            }

            serviceArgument = typeofTypes[0];
            implementationArgument = typeofTypes[typeofTypes.Count - 1];
        }

        return CreateCollectedModelCore(context, invocation, serviceArgument, implementationArgument, lifetime, keyed ? CollectedKinds.Keyed : CollectedKinds.Direct);
    }

    private static bool HasFactoryOrInstanceParameter(IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            if ((parameter.Type.TypeKind == TypeKind.Delegate) || (parameter.Type is ITypeParameterSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static CollectedModel? CreateCollectedModelCore(GeneratorSyntaxContext context, InvocationExpressionSyntax invocation, ITypeSymbol serviceArgument, ITypeSymbol implementationArgument, string lifetime, int kind)
    {
        if (implementationArgument is not INamedTypeSymbol implementationSymbol)
        {
            return null;
        }

        // closed generic は対象 (new Foo<int>() は生成可能)。open generic 定義と型パラメータを含む場合は対象外
        // Closed generics are eligible (new Foo<int>() can be generated); open generic definitions and types
        // containing type parameters are not.
        if (implementationSymbol.IsUnboundGenericType
            || implementationSymbol.IsAbstract
            || (implementationSymbol.TypeKind != TypeKind.Class)
            || ContainsTypeParameter(implementationSymbol))
        {
            return null;
        }

        // 生成ファクトリ (new 直書き) が現在のアセンブリからアクセスできること
        // The generated factory (literal new) must be able to access the type from the current assembly.
        if (!context.SemanticModel.Compilation.IsSymbolAccessibleWithin(implementationSymbol, context.SemanticModel.Compilation.Assembly))
        {
            return null;
        }

        var factory = CreateFactoryModel(implementationSymbol, context.SemanticModel.Compilation.Assembly);
        if (kind == CollectedKinds.Keyed ? !factory.EligibleKeyed : !factory.EligibleUnkeyed)
        {
            return null;
        }

        var serviceType = serviceArgument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new CollectedModel(factory, serviceType, lifetime, kind, invocation.SyntaxTree.FilePath, invocation.SpanStart);
    }

    // ------------------------------------------------------------
    // Parser : open generic registrations (open generic 登録と閉型使用)
    // ------------------------------------------------------------

    // 定義キー: "global::Ns.Name`arity"。unbound と constructed の両方から同じキーを作る
    // Definition key "global::Ns.Name`arity", produced identically from unbound and constructed symbols.
    private static string DefinitionKey(INamedTypeSymbol symbol)
    {
        var display = symbol.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var index = display.IndexOf('<');
        var name = index >= 0 ? display.Substring(0, index) : display;
        return name + "`" + symbol.Arity.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // GetTypeByMetadataName で解決できるメタデータ名 (ネスト型は '+' 区切り)。型引数付き・配列などは対象外
    // Metadata name resolvable by GetTypeByMetadataName (nested types joined by '+'). Generic instantiations and arrays are excluded.
    private static string? TryGetMetadataName(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { Arity: 0, IsAnonymousType: false } named || type.TypeKind == TypeKind.TypeParameter)
        {
            return null;
        }

        var parts = new List<string> { named.MetadataName };
        for (var containing = named.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            parts.Insert(0, containing.MetadataName);
        }

        var ns = named.ContainingNamespace;
        var nested = string.Join("+", parts);
        return (ns is null) || ns.IsGlobalNamespace ? nested : ns.ToDisplayString() + "." + nested;
    }

    private static OpenGenericModel? CreateOpenGenericModel(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return null;
        }

        var lifetime = method.Name switch
        {
            "AddSingleton" or "TryAddSingleton" => "Singleton",
            "AddScoped" or "TryAddScoped" => "Scoped",
            "AddTransient" or "TryAddTransient" => "Transient",
            _ => null
        };
        if (lifetime is null || (method.TypeArguments.Length != 0))
        {
            return null;
        }

        var containingType = method.ContainingType?.ToDisplayString();
        if (containingType is not (ServiceCollectionExtensionsName or ServiceCollectionDescriptorExtensionsName))
        {
            return null;
        }

        // typeof(IRepo<>) と typeof(Repo<>) の 2 引数形のみ対象
        // Only the two-argument form with unbound typeof expressions is collected.
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 2
            || arguments[0].Expression is not TypeOfExpressionSyntax serviceTypeOf
            || arguments[1].Expression is not TypeOfExpressionSyntax implementationTypeOf)
        {
            return null;
        }

        if (context.SemanticModel.GetTypeInfo(serviceTypeOf.Type).Type is not INamedTypeSymbol service
            || context.SemanticModel.GetTypeInfo(implementationTypeOf.Type).Type is not INamedTypeSymbol implementation
            || !service.IsUnboundGenericType
            || !implementation.IsUnboundGenericType
            || (service.Arity != implementation.Arity))
        {
            return null;
        }

        var implementationDefinition = implementation.ConstructedFrom;
        if (implementationDefinition.IsAbstract || (implementationDefinition.TypeKind != TypeKind.Class))
        {
            return null;
        }

        var metadataName = TryGetMetadataNameForDefinition(implementationDefinition);
        if (metadataName is null)
        {
            return null;
        }

        return new OpenGenericModel(
            DefinitionKey(service),
            metadataName,
            lifetime,
            invocation.SyntaxTree.FilePath,
            invocation.SpanStart);
    }

    // open generic 定義自体のメタデータ名 ("Ns.Repo`1")
    // Metadata name of the open generic definition itself ("Ns.Repo`1").
    private static string? TryGetMetadataNameForDefinition(INamedTypeSymbol definition)
    {
        var parts = new List<string> { definition.MetadataName };
        for (var containing = definition.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            if (containing.Arity != 0)
            {
                return null;
            }

            parts.Insert(0, containing.MetadataName);
        }

        var ns = definition.ContainingNamespace;
        var nested = string.Join("+", parts);
        return (ns is null) || ns.IsGlobalNamespace ? nested : ns.ToDisplayString() + "." + nested;
    }

    private static ClosedGenericUsageModel? CreateClosedGenericUsageModel(GeneratorSyntaxContext context)
    {
        var typeOf = (TypeOfExpressionSyntax)context.Node;
        return CreateUsageModel(context.SemanticModel.GetTypeInfo(typeOf.Type).Type, typeOf);
    }

    // コンストラクタ引数・プロパティの型構文に generic 名が含まれるか (軽量な構文プリフィルタ)
    // Whether the parameter or property type syntax contains a generic name (a lightweight syntax pre-filter).
    private static bool IsGenericDependencySyntax(SyntaxNode node)
    {
        var type = node switch
        {
            ParameterSyntax parameter => parameter.Type,
            PropertyDeclarationSyntax property => property.Type,
            _ => null
        };
        return ContainsGenericName(type);
    }

    private static bool ContainsGenericName(TypeSyntax? type) => type switch
    {
        GenericNameSyntax => true,
        QualifiedNameSyntax qualified => ContainsGenericName(qualified.Right) || ContainsGenericName(qualified.Left),
        NullableTypeSyntax nullable => ContainsGenericName(nullable.ElementType),
        AliasQualifiedNameSyntax alias => ContainsGenericName(alias.Name),
        _ => false
    };

    private static ClosedGenericUsageModel? CreateDependencyUsageModel(GeneratorSyntaxContext context)
    {
        var type = context.Node switch
        {
            ParameterSyntax parameter => parameter.Type,
            PropertyDeclarationSyntax property => property.Type,
            _ => null
        };
        if (type is null)
        {
            return null;
        }

        return CreateUsageModel(context.SemanticModel.GetTypeInfo(type).Type, context.Node);
    }

    private static ClosedGenericUsageModel? CreateUsageModel(ITypeSymbol? type, SyntaxNode locationNode)
    {
        if (type is not INamedTypeSymbol closed
            || !closed.IsGenericType
            || closed.IsUnboundGenericType)
        {
            return null;
        }

        // 全型引数がメタデータ名で往復できる場合のみ (それ以外は互換経路で解決される)
        // Collected only when every type argument round-trips through a metadata name (others stay on the runtime path).
        var arguments = new string[closed.TypeArguments.Length];
        var hasValueType = false;
        for (var i = 0; i < closed.TypeArguments.Length; i++)
        {
            var name = TryGetMetadataName(closed.TypeArguments[i]);
            if (name is null)
            {
                return null;
            }

            arguments[i] = name;
            hasValueType = hasValueType || closed.TypeArguments[i].IsValueType;
        }

        return new ClosedGenericUsageModel(
            DefinitionKey(closed),
            hasValueType,
            new EquatableArray<string>(arguments),
            locationNode.SyntaxTree.FilePath,
            locationNode.SpanStart,
            LocationInfo.CreateFrom(locationNode));
    }

    // [GenerateComponentFactory] の対象型からファクトリモデルを作る。生成コードは対象型を直接 new するため、
    // public にアクセスできる具象クラス (使用可能な public コンストラクタつき) だけを受け付ける
    // Builds factory models from [GenerateComponentFactory] targets. The generated code news the type up directly, so only
    // publicly accessible concrete classes with a usable public constructor are accepted.
    private static ImmutableArray<Result<FactoryModel>> CreateGenerateComponentFactoryModels(GeneratorAttributeSyntaxContext context)
    {
        var compilation = context.SemanticModel.Compilation;
        var models = ImmutableArray.CreateBuilder<Result<FactoryModel>>(context.Attributes.Length);
        foreach (var attribute in context.Attributes)
        {
            var location = LocationInfo.CreateFrom(context.TargetNode);
            if ((attribute.ConstructorArguments.Length != 1)
                || (attribute.ConstructorArguments[0].Value is not INamedTypeSymbol type))
            {
                continue;
            }

            string? postConstruct = null;
            foreach (var argument in attribute.NamedArguments)
            {
                if ((argument.Key == "PostConstruct") && (argument.Value.Value is string value))
                {
                    postConstruct = value;
                }
            }

            var displayName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (type.IsAbstract
                || type.IsStatic
                || (type.TypeKind != TypeKind.Class)
                || type.IsUnboundGenericType
                || ContainsTypeParameter(type)
                || !compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
            {
                models.Add(Results.Error<FactoryModel>(new DiagnosticInfo(InvalidGenerateComponentFactoryTarget, location, displayName)));
                continue;
            }

            var factory = CreateFactoryModel(type, compilation.Assembly);
            if (!factory.EligibleUnkeyed)
            {
                models.Add(Results.Error<FactoryModel>(new DiagnosticInfo(InvalidGenerateComponentFactoryTarget, location, displayName)));
                continue;
            }

            // PostConstruct 指定は属性由来の値より優先する。妥当でなければ BTDI0006
            // The PostConstruct specification wins over the attribute derived value; an invalid one reports BTDI0006.
            if (postConstruct is not null)
            {
                if (!HasValidPostConstructMethod(type, postConstruct))
                {
                    models.Add(Results.Error<FactoryModel>(new DiagnosticInfo(InvalidPostConstruct, location, postConstruct, displayName)));
                    continue;
                }

                factory = factory with { PostConstruct = postConstruct, InvalidPostConstruct = false, ConflictingPostConstruct = false };
            }

            models.Add(Results.Success(factory));
        }

        return models.ToImmutable();
    }

    // ------------------------------------------------------------
    // Parser : convention registration method (規約登録メソッド)
    // ------------------------------------------------------------

    private static Result<MethodModel> CreateMethodModel(GeneratorAttributeSyntaxContext context)
    {
        var syntax = (MethodDeclarationSyntax)context.TargetNode;
        if (context.TargetSymbol is not IMethodSymbol symbol)
        {
            return Results.Error<MethodModel>(new DiagnosticInfo(InvalidMethodDefinition, LocationInfo.CreateFrom(syntax), context.TargetSymbol.Name));
        }

        if (!symbol.IsStatic
            || !symbol.IsPartialDefinition
            || !symbol.IsExtensionMethod
            || (symbol.Parameters.Length != 1)
            || (symbol.Parameters[0].Type.ToDisplayString() != ServiceCollectionName)
            || (symbol.ReturnType.ToDisplayString() != ServiceCollectionName))
        {
            return Results.Error<MethodModel>(new DiagnosticInfo(InvalidMethodDefinition, LocationInfo.CreateFrom(syntax), symbol.Name));
        }

        var patterns = new List<PatternModel>();
        foreach (var attribute in context.Attributes)
        {
            if (attribute.ConstructorArguments.Length != 2)
            {
                continue;
            }

            var lifetime = attribute.ConstructorArguments[0].Value is int value
                ? value switch { 1 => "Singleton", 2 => "Scoped", _ => "Transient" }
                : "Transient";
            var pattern = attribute.ConstructorArguments[1].Value as string ?? string.Empty;
            string? ns = null;
            string? assembly = null;
            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key == "Namespace")
                {
                    ns = argument.Value.Value as string;
                }
                else if (argument.Key == "Assembly")
                {
                    assembly = argument.Value.Value as string;
                }
            }

            patterns.Add(new PatternModel(lifetime, pattern, ns, assembly));
        }

        var containingNamespace = symbol.ContainingType.ContainingNamespace.IsGlobalNamespace
            ? null
            : symbol.ContainingType.ContainingNamespace.ToDisplayString();

        return Results.Success(new MethodModel(
            containingNamespace,
            symbol.ContainingType.Name,
            symbol.DeclaredAccessibility,
            symbol.Name,
            new EquatableArray<PatternModel>([.. patterns]),
            LocationInfo.CreateFrom(syntax)));
    }

    // ------------------------------------------------------------
    // Parser : convention candidates (規約マッチ候補)
    // ------------------------------------------------------------

    private static bool IsCandidateClassSyntax(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax syntax)
        {
            return false;
        }

        foreach (var modifier in syntax.Modifiers)
        {
            if (modifier.IsKind(SyntaxKind.AbstractKeyword) || modifier.IsKind(SyntaxKind.StaticKeyword) || modifier.IsKind(SyntaxKind.FileKeyword))
            {
                return false;
            }
        }

        return true;
    }

    private static CandidateModel? CreateCandidateModel(GeneratorSyntaxContext context)
    {
        var syntax = (ClassDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(syntax) is not { } symbol)
        {
            return null;
        }

        if (symbol.IsAbstract || symbol.IsStatic || (symbol.TypeParameters.Length > 0))
        {
            return null;
        }

        // partial クラスの重複登録を避ける (最初の宣言のみ採用)
        // Avoids duplicate registration of partial classes (only the first declaration is used).
        if ((symbol.DeclaringSyntaxReferences.Length > 0) && (symbol.DeclaringSyntaxReferences[0].GetSyntax() != syntax))
        {
            return null;
        }

        return new CandidateModel(
            symbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : symbol.ContainingNamespace.ToDisplayString(),
            symbol.Name,
            CreateFactoryModel(symbol, context.SemanticModel.Compilation.Assembly),
            null,
            CollectInterfaces(symbol),
            syntax.SyntaxTree.FilePath,
            syntax.SpanStart);
    }

    // ------------------------------------------------------------
    // Generator
    // ------------------------------------------------------------

    private static void Execute(
        SourceProductionContext context,
        ImmutableArray<ComponentModel> singletons,
        ImmutableArray<ComponentModel> scopeds,
        ImmutableArray<ComponentModel> transients,
        ImmutableArray<CollectedModel> collected,
        ImmutableArray<Result<MethodModel>> methods,
        ImmutableArray<CandidateModel> candidates,
        ImmutableArray<Result<FactoryModel>> generateComponentFactoryTargets,
        ExternalScanResult externalScan,
        ClosedGenericScanResult closedGenerics,
        string assemblyName,
        EquatableArray<string> referencedModules,
        EquatableArray<string> ignoreInterfaces)
    {
        foreach (var method in methods)
        {
            foreach (var info in method.Diagnostics)
            {
                context.ReportDiagnostic(info.ToDiagnostic());
            }
        }

        var components = singletons.Concat(scopeds).Concat(transients)
            .Select(x => x with { Interfaces = FilterIgnoredInterfaces(x.Interfaces, ignoreInterfaces) })
            .OrderBy(static x => x.FilePath, StringComparer.Ordinal)
            .ThenBy(static x => x.SpanStart)
            .ToArray();

        var sortedCollected = collected
            .OrderBy(static x => x.FilePath, StringComparer.Ordinal)
            .ThenBy(static x => x.SpanStart)
            .ToArray();

        // 規約マッチ (メソッドごと)
        // Convention matching (per method).
        var sortedCandidates = candidates
            .OrderBy(static x => x.FilePath, StringComparer.Ordinal)
            .ThenBy(static x => x.SpanStart)
            .ToArray();
        var allCandidates = sortedCandidates.Concat(externalScan.Candidates)
            .Select(x => x with { Interfaces = FilterIgnoredInterfaces(x.Interfaces, ignoreInterfaces) })
            .ToArray();
        var conventionMatches = new List<(MethodModel Method, List<(CandidateModel Candidate, string Lifetime)> Matches)>();
        foreach (var method in methods)
        {
            if (!method.HasValue)
            {
                continue;
            }

            var matches = new List<(CandidateModel, string)>();
            var matched = new HashSet<string>();
            foreach (var pattern in method.Value.Patterns)
            {
                Regex regex;
                try
                {
                    regex = new Regex(pattern.Pattern);
                }
                catch (ArgumentException)
                {
                    context.ReportDiagnostic(new DiagnosticInfo(InvalidPattern, method.Value.Location, pattern.Pattern).ToDiagnostic());
                    continue;
                }

                if ((pattern.Assembly is not null) && externalScan.MissingAssemblies.Contains(pattern.Assembly))
                {
                    context.ReportDiagnostic(new DiagnosticInfo(AssemblyNotFound, method.Value.Location, pattern.Assembly).ToDiagnostic());
                    continue;
                }

                // ReSharper disable once LoopCanBeConvertedToQuery
                foreach (var candidate in allCandidates)
                {
                    // パターンの走査対象 (自コンパイル or 指定アセンブリ) と候補の出自を一致させる
                    // The candidate origin must match the pattern's scan target (current compilation or the named assembly).
                    if (!string.Equals(candidate.Assembly, pattern.Assembly, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!regex.IsMatch(candidate.Name))
                    {
                        continue;
                    }

                    if ((pattern.Namespace is not null)
                        && (candidate.Namespace != pattern.Namespace)
                        && !candidate.Namespace.StartsWith(pattern.Namespace + ".", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (matched.Add(candidate.Factory.ImplementationType))
                    {
                        matches.Add((candidate, pattern.Lifetime));
                    }
                }
            }

            conventionMatches.Add((method.Value, matches));
        }

        // コンパイル時診断 (循環 / 未解決 / captive / 曖昧 ctor)
        // Compile-time diagnostics (cycles / unresolved / captive / ambiguous constructors).
        ReportAnalysisDiagnostics(context, components, sortedCollected, conventionMatches, closedGenerics.DefinitionKeys);

        // ---- GeneratedComponents.g.cs (登録メソッド + 生成ファクトリ / registration method + generated factories) ----

        var unkeyedFactories = new List<FactoryModel>();
        var keyedFactories = new List<FactoryModel>();
        var emittedUnkeyed = new HashSet<string>();
        var emittedKeyed = new HashSet<string>();
        foreach (var component in components)
        {
            if (component.KeyLiteral is null)
            {
                if (component.Factory.EligibleUnkeyed && emittedUnkeyed.Add(component.Factory.ImplementationType))
                {
                    unkeyedFactories.Add(component.Factory);
                }
            }
            else
            {
                if (component.Factory.EligibleKeyed && emittedKeyed.Add(component.Factory.ImplementationType))
                {
                    keyedFactories.Add(component.Factory);
                }
            }
        }

        foreach (var model in sortedCollected)
        {
            if (model.Kind == CollectedKinds.Keyed)
            {
                if (model.Factory.EligibleKeyed && emittedKeyed.Add(model.Factory.ImplementationType))
                {
                    keyedFactories.Add(model.Factory);
                }
            }
            else if (emittedUnkeyed.Add(model.Factory.ImplementationType))
            {
                unkeyedFactories.Add(model.Factory);
            }
        }

        foreach (var (_, matches) in conventionMatches)
        {
            foreach (var (candidate, _) in matches)
            {
                if (candidate.Factory.EligibleUnkeyed && emittedUnkeyed.Add(candidate.Factory.ImplementationType))
                {
                    unkeyedFactories.Add(candidate.Factory);
                }
            }
        }

        // open generic 登録の閉型使用から作られた生成ファクトリ (パイプライン側で解決済み)。実行時は open generic 実現
        // (MakeGenericType → 閉じた実装型) が採用フック (TryGet) で自動的にこれを拾う。
        // 値型引数のまま実行時経路に残る使用は NativeAOT で失敗するため BTDI0011 を報告する
        // Generated factories built from closed usages of open generic registrations (resolved on the pipeline side).
        // At runtime the open generic realization (MakeGenericType into the closed implementation) picks them up
        // through the adoption hook (TryGet). Usages left on the runtime path with value type arguments fail on
        // NativeAOT, so BTDI0011 is reported for them.
        foreach (var factory in closedGenerics.Factories)
        {
            if (emittedUnkeyed.Add(factory.ImplementationType))
            {
                unkeyedFactories.Add(factory);
            }
        }

        // [GenerateComponentFactory] 指定分。登録は行わずファクトリのみ (実行時は実装型で採用される)
        // Targets of [GenerateComponentFactory]: factories only, no registration (adopted at runtime by implementation type).
        var generatedInitializers = new List<(string ImplementationType, string PostConstruct)>();
        foreach (var target in generateComponentFactoryTargets)
        {
            foreach (var info in target.Diagnostics)
            {
                context.ReportDiagnostic(info.ToDiagnostic());
            }

            if (!target.HasValue)
            {
                continue;
            }

            if (emittedUnkeyed.Add(target.Value.ImplementationType))
            {
                unkeyedFactories.Add(target.Value);
            }

            // 実行時経路でも同じ初期化が行われるよう、メソッド名をレジストリへ登録する
            // Registers the method name so the runtime path performs the same initialization.
            if (target.Value.PostConstruct is not null)
            {
                generatedInitializers.Add((target.Value.ImplementationType, target.Value.PostConstruct));
            }
        }

        foreach (var warning in closedGenerics.Warnings)
        {
            context.ReportDiagnostic(new DiagnosticInfo(ValueTypeRuntimeGeneric, warning.Location, warning.DisplayName).ToDiagnostic());
        }

        var inlineTargetMap = BuildInlineTargetMap(components, sortedCollected, conventionMatches);
        var enumerableModels = BuildEnumerableModels(components, sortedCollected, conventionMatches);
        if ((components.Length > 0) || (unkeyedFactories.Count > 0) || (keyedFactories.Count > 0) || (enumerableModels.Count > 0) || (referencedModules.Count > 0))
        {
            EmitGeneratedComponents(context, assemblyName, components, unkeyedFactories, keyedFactories, enumerableModels, inlineTargetMap, referencedModules, generatedInitializers);
        }

        // ---- 規約登録メソッドの本体 / convention registration method bodies ----

        // 同一クラスの複数メソッドは 1 ファイルへまとめる (出力単位はクラス。分けると hintName が衝突する)
        // Methods of the same class go into a single file (the output unit is the class; splitting would collide on hintName).
        foreach (var group in conventionMatches.GroupBy(static x => (x.Method.Namespace, x.Method.ClassName)))
        {
            EmitConventionClass(context, group.Key.Namespace, group.Key.ClassName, [.. group]);
        }
    }

    // 生成 enumerable ファクトリの対象: 同一サービス型へ 2 件以上、全登録が direct (Add<S, I> 形式) かつ
    // transient のインライン展開適格 (非 disposable・[Inject] なし・初期化なし)。順序は出力順 = RegisterComponents 相当。
    // 実行時の構成差 (追加・差し替え・順序) は EnumerableElementsMatch が検出してフォールバックする
    // Targets for generated enumerable factories: two or more registrations for the same service type, all direct
    // (Add<S, I> style) transients eligible for inline expansion (no disposable/[Inject]/initializer). Order follows
    // the emission order (equivalent to RegisterComponents); runtime composition differences fall back via EnumerableElementsMatch.
    // ReSharper disable ParameterTypeCanBeEnumerable.Local
    private static List<(string ElementServiceType, List<FactoryModel> Elements)> BuildEnumerableModels(
        ComponentModel[] components,
        CollectedModel[] collected,
        List<(MethodModel Method, List<(CandidateModel Candidate, string Lifetime)> Matches)> conventionMatches)
    {
        var lists = new Dictionary<string, List<(FactoryModel? Factory, string Lifetime)>>(StringComparer.Ordinal);
        var order = new List<string>();

        void Append(string service, FactoryModel? factory, string lifetime)
        {
            if (!lists.TryGetValue(service, out var list))
            {
                list = [];
                lists[service] = list;
                order.Add(service);
            }

            list.Add((factory, lifetime));
        }

        foreach (var component in components)
        {
            if (component.KeyLiteral is not null)
            {
                continue;
            }

            if (component.AsType is not null)
            {
                Append(component.AsType, component.Factory, component.Lifetime);
            }
            else
            {
                Append(component.Factory.ImplementationType, component.Factory, component.Lifetime);
                foreach (var interfaceType in component.Interfaces)
                {
                    Append(interfaceType, null, component.Lifetime);   // フォワーディングは検証不能 / forwarding cannot be identity-validated
                }
            }
        }

        foreach (var model in collected)
        {
            if (model.Kind == CollectedKinds.Keyed)
            {
                continue;
            }

            if (model.Kind == CollectedKinds.FactoryOnly)
            {
                // TryAddEnumerable は実行時の重複排除で構成が構文順と一致しない可能性があるため、
                // null factory (検証不能要素) として enumerable 生成を不成立にする
                // TryAddEnumerable composition can differ from syntax order due to runtime de-duplication, so a null
                // factory (an unverifiable element) disqualifies enumerable generation for the service.
                Append(model.ServiceType, null, model.Lifetime);
                continue;
            }

            Append(model.ServiceType, model.Factory, model.Lifetime);
        }

        foreach (var (_, matches) in conventionMatches)
        {
            foreach (var (candidate, lifetime) in matches)
            {
                if (candidate.Interfaces.Count == 1)
                {
                    Append(candidate.Interfaces[0], candidate.Factory, lifetime);
                }
                else
                {
                    Append(candidate.Factory.ImplementationType, candidate.Factory, lifetime);
                    foreach (var interfaceType in candidate.Interfaces)
                    {
                        Append(interfaceType, null, lifetime);
                    }
                }
            }
        }

        var models = new List<(string, List<FactoryModel>)>();
        foreach (var service in order)
        {
            var list = lists[service];
            if (list.Count < 2)
            {
                continue;
            }

            var eligible = true;
            var elements = new List<FactoryModel>(list.Count);
            foreach (var (factory, lifetime) in list)
            {
                if ((factory is null)
                    || (lifetime != "Transient")
                    || !factory.EligibleUnkeyed
                    || (factory.InjectProperties.Count > 0)
                    || factory.Disposable
                    || factory.HasInitializer)
                {
                    eligible = false;
                    break;
                }

                elements.Add(factory);
            }

            if (eligible)
            {
                models.Add((service, elements));
            }
        }

        return models;
    }
    // ReSharper restore ParameterTypeCanBeEnumerable.Local

    private static ClosedGenericScanResult DiscoverClosedGenericFactories(
        ImmutableArray<OpenGenericModel> openGenerics,
        ImmutableArray<ClosedGenericUsageModel> closedUsages,
        ImmutableArray<ClosedGenericUsageModel> dependencyUsages,
        Compilation compilation)
    {
        var factories = new List<FactoryModel>();
        var warnings = new List<ClosedGenericWarningModel>();

        // 定義キー → 実装 (同一キーの再登録は後勝ち: 実行時の last-wins と一致)。キー集合は使用が無くても
        // BTDI0009 の解決可能性判定に使うため常に返す
        // Definition key -> implementation (re-registrations are last-wins, matching runtime behavior). The key set
        // is always returned because BTDI0009 resolvability checks need it even without usages.
        var registrations = new Dictionary<string, OpenGenericModel>(StringComparer.Ordinal);
        foreach (var model in openGenerics.OrderBy(static x => x.FilePath, StringComparer.Ordinal).ThenBy(static x => x.SpanStart))
        {
            registrations[model.ServiceDefinitionKey] = model;
        }

        var definitionKeys = new EquatableArray<string>([
            .. registrations.Keys.OrderBy(static x => x, StringComparer.Ordinal)
        ]);
        if (openGenerics.IsEmpty || (closedUsages.IsEmpty && dependencyUsages.IsEmpty))
        {
            return new ClosedGenericScanResult(new EquatableArray<FactoryModel>([]), new EquatableArray<ClosedGenericWarningModel>([]), definitionKeys);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var warned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var usage in closedUsages
            .Concat(dependencyUsages)
            .OrderBy(static x => x.FilePath, StringComparer.Ordinal)
            .ThenBy(static x => x.SpanStart))
        {
            if (!registrations.TryGetValue(usage.ServiceDefinitionKey, out var registration))
            {
                continue;
            }

            // 値型引数のまま生成できない使用は NativeAOT の実行時経路で失敗するため警告対象
            // Usages that cannot be generated and carry value type arguments fail on the NativeAOT runtime path, so they are warned about.
            void Warn(string displayName)
            {
                if (usage.HasValueTypeArgument && warned.Add(displayName))
                {
                    warnings.Add(new ClosedGenericWarningModel(displayName, usage.Location));
                }
            }

            var definition = compilation.GetTypeByMetadataName(registration.ImplementationMetadataName);
            if ((definition is null) || (definition.Arity != usage.TypeArgumentMetadataNames.Count))
            {
                Warn(usage.ServiceDefinitionKey);
                continue;
            }

            var argumentSymbols = new ITypeSymbol[usage.TypeArgumentMetadataNames.Count];
            var resolved = true;
            for (var i = 0; i < usage.TypeArgumentMetadataNames.Count; i++)
            {
                var argument = compilation.GetTypeByMetadataName(usage.TypeArgumentMetadataNames[i]);
                if (argument is null)
                {
                    resolved = false;
                    break;
                }

                argumentSymbols[i] = argument;
            }

            if (!resolved)
            {
                Warn(usage.ServiceDefinitionKey);
                continue;
            }

            var closedImplementation = definition.Construct(argumentSymbols);
            var displayName = closedImplementation.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (seen.Contains(displayName))
            {
                continue;
            }

            if (!compilation.IsSymbolAccessibleWithin(closedImplementation, compilation.Assembly))
            {
                Warn(displayName);
                continue;
            }

            var factory = CreateFactoryModel(closedImplementation, compilation.Assembly);
            if (!factory.EligibleUnkeyed)
            {
                Warn(displayName);
                continue;
            }

            if (seen.Add(factory.ImplementationType))
            {
                factories.Add(factory);
            }
        }

        return new ClosedGenericScanResult(
            new EquatableArray<FactoryModel>([.. factories]),
            new EquatableArray<ClosedGenericWarningModel>([.. warnings]),
            definitionKeys);
    }

    // ------------------------------------------------------------
    // Diagnostics (compile-time analysis / コンパイル時解析)
    // ------------------------------------------------------------

    // 表示名 "global::Ns.IRepo<Foo, Bar>" が open generic 登録の閉型かを定義キーで判定する
    // Determines from the definition keys whether a display name like "global::Ns.IRepo<Foo, Bar>" is a closed form of an open generic registration.
    private static bool IsOpenGenericClosedForm(string typeName, HashSet<string> openGenericKeys)
    {
        if (openGenericKeys.Count == 0)
        {
            return false;
        }

        var start = typeName.IndexOf('<');
        if ((start < 0) || !typeName.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        var arity = 1;
        var depth = 0;
        for (var i = start + 1; i < typeName.Length - 1; i++)
        {
            var c = typeName[i];
            if (c == '<')
            {
                depth++;
            }
            else if (c == '>')
            {
                depth--;
            }
            else if ((c == ',') && (depth == 0))
            {
                arity++;
            }
        }

        return openGenericKeys.Contains(typeName.Substring(0, start) + "`" + arity.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // ReSharper disable ParameterTypeCanBeEnumerable.Local
    private static void ReportAnalysisDiagnostics(
        SourceProductionContext context,
        ComponentModel[] components,
        CollectedModel[] collected,
        List<(MethodModel Method, List<(CandidateModel Candidate, string Lifetime)> Matches)> conventionMatches,
        EquatableArray<string> openGenericDefinitionKeys)
    {
        var openGenericKeys = new HashSet<string>(openGenericDefinitionKeys, StringComparer.Ordinal);

        // 登録マップ: サービス型 → (実装型, lifetime)。登録順で last-wins
        // Registration map: service type -> (implementation type, lifetime). Last registration wins.
        var serviceMap = new Dictionary<string, (string Impl, string Lifetime)>(StringComparer.Ordinal);
        var nodes = new Dictionary<string, (FactoryModel Factory, string Lifetime, LocationInfo? Location)>(StringComparer.Ordinal);

        foreach (var component in components)
        {
            if (component.KeyLiteral is not null)
            {
                continue;   // keyed は解析対象外 / keyed registrations are not analyzed
            }

            var impl = component.Factory.ImplementationType;
            if (component.AsType is not null)
            {
                serviceMap[component.AsType] = (impl, component.Lifetime);
            }
            else
            {
                serviceMap[impl] = (impl, component.Lifetime);
                foreach (var interfaceType in component.Interfaces)
                {
                    serviceMap[interfaceType] = (impl, component.Lifetime);
                }
            }

            if (!nodes.ContainsKey(impl))
            {
                nodes[impl] = (component.Factory, component.Lifetime, component.Location);
            }
        }

        foreach (var model in collected)
        {
            serviceMap[model.ServiceType] = (model.Factory.ImplementationType, model.Lifetime);
            if (!nodes.ContainsKey(model.Factory.ImplementationType))
            {
                nodes[model.Factory.ImplementationType] = (model.Factory, model.Lifetime, null);
            }
        }

        foreach (var (_, matches) in conventionMatches)
        {
            foreach (var (candidate, lifetime) in matches)
            {
                var impl = candidate.Factory.ImplementationType;
                if (candidate.Interfaces.Count == 1)
                {
                    serviceMap[candidate.Interfaces[0]] = (impl, lifetime);
                }
                else
                {
                    serviceMap[impl] = (impl, lifetime);
                    foreach (var interfaceType in candidate.Interfaces)
                    {
                        serviceMap[interfaceType] = (impl, lifetime);
                    }
                }

                if (!nodes.ContainsKey(impl))
                {
                    nodes[impl] = (candidate.Factory, lifetime, null);
                }
            }
        }

        // 依存列挙 (非 keyed のみ)
        // Dependency enumeration (non-keyed only).
        static IEnumerable<(string TypeName, bool InCompilation)> Dependencies(FactoryModel factory)
        {
            foreach (var parameter in factory.Parameters)
            {
                if (parameter.Kind == DependencyKinds.Service)
                {
                    yield return (parameter.TypeName, parameter.InCompilation);
                }
            }

            foreach (var property in factory.InjectProperties)
            {
                if (property.Kind == DependencyKinds.Service)
                {
                    yield return (property.TypeName, property.InCompilation);
                }
            }
        }

        static string Display(string typeName) => typeName.StartsWith("global::", StringComparison.Ordinal) ? typeName.Substring(8) : typeName;

        // BTDI0009 (未解決) / BTDI0010 (captive) / BTDI0005 (曖昧 ctor) — 属性コンポーネント起点
        // BTDI0009 (unresolved) / BTDI0010 (captive) / BTDI0005 (ambiguous ctor) reported from attribute components.
        foreach (var component in components)
        {
            if (component.Factory.AmbiguousConstructor)
            {
                context.ReportDiagnostic(new DiagnosticInfo(AmbiguousConstructor, component.Location, Display(component.Factory.ImplementationType)).ToDiagnostic());
            }

            if (component.Factory.InvalidPostConstruct)
            {
                context.ReportDiagnostic(new DiagnosticInfo(InvalidPostConstruct, component.Location, component.Factory.PostConstruct!, Display(component.Factory.ImplementationType)).ToDiagnostic());
            }

            if (component.Factory.ConflictingPostConstruct)
            {
                context.ReportDiagnostic(new DiagnosticInfo(ConflictingPostConstruct, component.Location, Display(component.Factory.ImplementationType)).ToDiagnostic());
            }

            if (component.KeyLiteral is not null)
            {
                continue;
            }

            foreach (var (typeName, inCompilation) in Dependencies(component.Factory))
            {
                if (serviceMap.TryGetValue(typeName, out var target))
                {
                    if ((component.Lifetime == "Singleton") && (target.Lifetime == "Scoped"))
                    {
                        context.ReportDiagnostic(new DiagnosticInfo(CaptiveDependency, component.Location, Display(component.Factory.ImplementationType), Display(typeName)).ToDiagnostic());
                    }
                }
                else if (inCompilation
                    && !typeName.StartsWith("global::System.", StringComparison.Ordinal)
                    && !IsOpenGenericClosedForm(typeName, openGenericKeys))
                {
                    // コンパイル対象アセンブリ内の型で、コンパイル時に見える登録に無いもののみ警告
                    // (実行時登録は見えないため Warning。open generic 登録の閉型は解決可能なので除外)
                    // Warns only for types inside the compiling assembly missing from compile-time visible registrations
                    // (runtime registrations are invisible, hence Warning; closed forms of open generic registrations resolve, so they are exempt).
                    context.ReportDiagnostic(new DiagnosticInfo(UnresolvedDependency, component.Location, Display(typeName), Display(component.Factory.ImplementationType)).ToDiagnostic());
                }
            }
        }

        // BTDI0008 (循環) — 生成対象ノード全体で DFS
        // BTDI0008 (cycles) — DFS over all generation target nodes.
        var state = new Dictionary<string, int>(StringComparer.Ordinal);   // 0=未訪問 1=探索中 2=完了 / 0=unvisited 1=visiting 2=done
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes.Keys)
        {
            if (!state.TryGetValue(node, out var value) || (value == 0))
            {
                var stack = new List<string>();
                Visit(node, stack);
            }
        }

        void Visit(string impl, List<string> stack)
        {
            state[impl] = 1;
            stack.Add(impl);

            if (nodes.TryGetValue(impl, out var node))
            {
                foreach (var (typeName, _) in Dependencies(node.Factory))
                {
                    if (!serviceMap.TryGetValue(typeName, out var target) || !nodes.ContainsKey(target.Impl))
                    {
                        continue;
                    }

                    if (state.TryGetValue(target.Impl, out var targetState) && (targetState == 1))
                    {
                        // 循環検出 / cycle detected
                        var start = stack.IndexOf(target.Impl);
                        var chain = string.Join(" -> ", stack.Skip(start).Concat([target.Impl]).Select(Display));
                        if (reported.Add(chain))
                        {
                            context.ReportDiagnostic(new DiagnosticInfo(CircularDependency, node.Location, chain).ToDiagnostic());
                        }
                    }
                    else if (!state.TryGetValue(target.Impl, out var visited) || (visited == 0))
                    {
                        Visit(target.Impl, stack);
                    }
                }
            }

            stack.RemoveAt(stack.Count - 1);
            state[impl] = 2;
        }
    }
    // ReSharper restore ParameterTypeCanBeEnumerable.Local

    // ------------------------------------------------------------
    // Inline expansion (transient 依存のリテラル new 展開 / literal new expansion of transient dependencies)
    // ------------------------------------------------------------

    // 非 keyed サービス型 → インライン展開先ファクトリ。
    // 展開してよいのは「コンパイル時登録が一意 (登録間の不一致がない) な direct 登録 (Add<S, I> 形式)」かつ
    // 「transient・生成ファクトリ適格・[Inject] プロパティなし・IDisposable/IAsyncDisposable 非実装」のみ。
    // それ以外は従来どおり provider.GetRequiredService<T>() を出力する。
    // 実行時登録との最終整合はランタイム側 (ServiceRegistry.InlinedDependenciesMatch) が検証し、
    // 不成立なら互換経路へフォールバックする
    // Non-keyed service type -> inline expansion target factory. Expansion is allowed only for direct
    // (Add<S, I> style) registrations that are unambiguous at compile time, and whose target is transient,
    // eligible for a generated factory, has no [Inject] properties and does not implement
    // IDisposable/IAsyncDisposable. Everything else keeps emitting provider.GetRequiredService<T>().
    // Final consistency against runtime registrations is validated by ServiceRegistry.InlinedDependenciesMatch,
    // falling back to the runtime path on mismatch.
    private sealed class InlineTargetMap
    {
        private readonly Dictionary<string, FactoryModel> targets;

        // 一意な direct Singleton 登録 (サービス型 → 実装型)。依存配列渡しの対象
        // Unambiguous direct singleton registrations (service type -> implementation type), eligible for the dependency array.
        private readonly Dictionary<string, string> singletonTargets;

        public InlineTargetMap(Dictionary<string, FactoryModel> targets, Dictionary<string, string> singletonTargets)
        {
            this.targets = targets;
            this.singletonTargets = singletonTargets;
        }

        public FactoryModel? GetTarget(string serviceTypeName) =>
            targets.TryGetValue(serviceTypeName, out var factory) ? factory : null;

        public string? GetSingletonTarget(string serviceTypeName) =>
            singletonTargets.TryGetValue(serviceTypeName, out var implementation) ? implementation : null;
    }

    // サービス依存の展開計画。Parameters の null 要素は GetRequiredService 経由で解決する
    // Expansion plan for a service dependency. Null elements in Parameters resolve through GetRequiredService.
    private sealed record InlineNode(string ServiceType, FactoryModel Factory, InlineNode?[] Parameters);

    // ReSharper disable ParameterTypeCanBeEnumerable.Local
    private static InlineTargetMap BuildInlineTargetMap(
        ComponentModel[] components,
        CollectedModel[] collected,
        List<(MethodModel Method, List<(CandidateModel Candidate, string Lifetime)> Matches)> conventionMatches)
    {
        var factories = new Dictionary<string, FactoryModel>(StringComparer.Ordinal);
        var candidates = new Dictionary<string, (string Impl, string Lifetime, bool Direct)>(StringComparer.Ordinal);
        var conflicted = new HashSet<string>(StringComparer.Ordinal);

        void Add(string service, string impl, string lifetime, bool direct)
        {
            if (candidates.TryGetValue(service, out var existing))
            {
                if ((existing.Impl != impl) || (existing.Lifetime != lifetime) || (existing.Direct != direct))
                {
                    conflicted.Add(service);
                }
            }
            else
            {
                candidates[service] = (impl, lifetime, direct);
            }
        }

        // 属性コンポーネント (RegisterComponents の登録形と一致させる)
        // Attribute components (kept consistent with the registration shape of RegisterComponents).
        foreach (var component in components)
        {
            if (component.KeyLiteral is not null)
            {
                continue;   // keyed 登録は非 keyed 解決に影響しない / keyed registrations do not affect non-keyed resolution
            }

            var impl = component.Factory.ImplementationType;
            factories[impl] = component.Factory;
            if (component.AsType is not null)
            {
                Add(component.AsType, impl, component.Lifetime, direct: true);
            }
            else
            {
                Add(impl, impl, component.Lifetime, direct: true);
                foreach (var interfaceType in component.Interfaces)
                {
                    Add(interfaceType, impl, component.Lifetime, direct: false);   // フォワーディングファクトリ登録 / forwarding factory registration
                }
            }
        }

        // Add* 呼び出し収集。Direct のみ前提に参加し、TryAddEnumerable (FactoryOnly) は同一サービスの
        // 単独前提を成立させないよう非 direct として毒化する。keyed は非 keyed 解決に影響しない
        // Add* invocation collection. Only Direct entries join the assumptions; TryAddEnumerable (FactoryOnly)
        // poisons the service as non-direct so no single-registration assumption survives. Keyed entries do not
        // affect non-keyed resolution.
        foreach (var model in collected)
        {
            if (model.Kind == CollectedKinds.Keyed)
            {
                continue;
            }

            if (model.Kind == CollectedKinds.FactoryOnly)
            {
                Add(model.ServiceType, model.Factory.ImplementationType, model.Lifetime, direct: false);
                continue;
            }

            factories[model.Factory.ImplementationType] = model.Factory;
            Add(model.ServiceType, model.Factory.ImplementationType, model.Lifetime, direct: true);
        }

        // 規約登録 (EmitConventionMethod の登録形と一致させる)
        // Convention registrations (kept consistent with the shape emitted by EmitConventionMethod).
        foreach (var (_, matches) in conventionMatches)
        {
            foreach (var (candidate, lifetime) in matches)
            {
                var impl = candidate.Factory.ImplementationType;
                factories[impl] = candidate.Factory;
                if (candidate.Interfaces.Count == 1)
                {
                    Add(candidate.Interfaces[0], impl, lifetime, direct: true);
                }
                else
                {
                    Add(impl, impl, lifetime, direct: true);
                    foreach (var interfaceType in candidate.Interfaces)
                    {
                        Add(interfaceType, impl, lifetime, direct: false);
                    }
                }
            }
        }

        var targets = new Dictionary<string, FactoryModel>(StringComparer.Ordinal);
        var singletonTargets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in candidates)
        {
            if (conflicted.Contains(pair.Key) || !pair.Value.Direct)
            {
                continue;
            }

            if (!factories.TryGetValue(pair.Value.Impl, out var factory) || !factory.EligibleUnkeyed)
            {
                continue;
            }

            // Singleton: 依存配列渡しの対象 (disposal/初期化/[Inject] は実装側の accessor が担うため制限なし)
            // Singletons are dependency-array candidates (disposal/initialization/[Inject] are handled by the dependency's own accessor).
            if (pair.Value.Lifetime == "Singleton")
            {
                singletonTargets[pair.Key] = pair.Value.Impl;
                continue;
            }

            // Transient: リテラル new 展開の対象 (従来条件)
            // Transients are literal-new candidates (existing conditions).
            if ((pair.Value.Lifetime == "Transient")
                && (factory.InjectProperties.Count == 0)
                && !factory.Disposable
                && !factory.HasInitializer)
            {
                targets[pair.Key] = factory;
            }
        }

        return new InlineTargetMap(targets, singletonTargets);
    }
    // ReSharper restore ParameterTypeCanBeEnumerable.Local

    private static InlineNode? TryCreateInlineNode(string serviceTypeName, InlineTargetMap map, List<string> stack)
    {
        var factory = map.GetTarget(serviceTypeName);
        if ((factory is null) || stack.Contains(factory.ImplementationType))
        {
            return null;   // 展開不可 or 循環 / not expandable or cyclic (cycles themselves error separately via BTDI0008)
        }

        stack.Add(factory.ImplementationType);
        var parameters = new InlineNode?[factory.Parameters.Count];
        for (var i = 0; i < factory.Parameters.Count; i++)
        {
            if (factory.Parameters[i].Kind == DependencyKinds.Service)
            {
                parameters[i] = TryCreateInlineNode(factory.Parameters[i].TypeName, map, stack);
            }
        }

        stack.RemoveAt(stack.Count - 1);
        return new InlineNode(serviceTypeName, factory, parameters);
    }

    // ------------------------------------------------------------
    // Emit
    // ------------------------------------------------------------

    // Assembly 指定つき規約パターンの要求抽出。メソッド属性のみが入力なので、属性が変わらない限り値は安定
    // Extraction of assembly-scoped convention requests. Only method attributes feed this, so the value stays stable
    // unless the attributes change.
    private static EquatableArray<ExternalRequest> CollectExternalRequests(ImmutableArray<Result<MethodModel>> methods)
    {
        var requests = new List<ExternalRequest>();
        foreach (var method in methods)
        {
            if (!method.HasValue)
            {
                continue;
            }

            foreach (var pattern in method.Value.Patterns)
            {
                if (pattern.Assembly is null)
                {
                    continue;
                }

                var request = new ExternalRequest(pattern.Assembly, pattern.Pattern, pattern.Namespace);
                if (!requests.Contains(request))
                {
                    requests.Add(request);
                }
            }
        }

        requests.Sort(static (x, y) =>
        {
            var result = string.CompareOrdinal(x.Assembly, y.Assembly);
            if (result != 0)
            {
                return result;
            }

            result = string.CompareOrdinal(x.Pattern, y.Pattern);
            return result != 0 ? result : string.CompareOrdinal(x.Namespace, y.Namespace);
        });
        return [with([.. requests])];
    }

    // 外部アセンブリの候補走査。要求されたアセンブリだけを歩き、名前と名前空間で絞ってから
    // FactoryModel を構築する (シンボル解析は一致した型のみ)。不正な正規表現はここでは無視し、
    // 診断は Execute 側の BTDI0002 が報告する
    // Candidate scan of external assemblies. Only requested assemblies are walked, and names and namespaces are
    // filtered before FactoryModel construction (symbol analysis touches matched types only). Invalid regexes are
    // ignored here; BTDI0002 in Execute reports them.
    private static ExternalScanResult CollectExternalCandidates(EquatableArray<ExternalRequest> requests, Compilation compilation)
    {
        if (requests.Count == 0)
        {
            return new ExternalScanResult(new EquatableArray<CandidateModel>([]), new EquatableArray<string>([]));
        }

        var requestMap = new Dictionary<string, List<(Regex Regex, string? Namespace)>>(StringComparer.Ordinal);
        foreach (var request in requests)
        {
            Regex regex;
            try
            {
                regex = new Regex(request.Pattern);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (!requestMap.TryGetValue(request.Assembly, out var list))
            {
                list = [];
                requestMap[request.Assembly] = list;
            }

            list.Add((regex, request.Namespace));
        }

        var candidates = new List<CandidateModel>();
        var missing = new List<string>();
        foreach (var pair in requestMap.OrderBy(static x => x.Key, StringComparer.Ordinal))
        {
            IAssemblySymbol? assembly = null;
            foreach (var reference in compilation.References)
            {
                if ((compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol symbol)
                    && string.Equals(symbol.Name, pair.Key, StringComparison.Ordinal))
                {
                    assembly = symbol;
                    break;
                }
            }

            if (assembly is null)
            {
                missing.Add(pair.Key);
                continue;
            }

            CollectNamespaceCandidates(assembly.GlobalNamespace, pair.Key, pair.Value, compilation, candidates);
        }

        candidates.Sort(static (x, y) => string.CompareOrdinal(x.Factory.ImplementationType, y.Factory.ImplementationType));
        missing.Sort(StringComparer.Ordinal);
        return new ExternalScanResult(new EquatableArray<CandidateModel>([.. candidates]), new EquatableArray<string>([
            .. missing
        ]));
    }

    private static void CollectNamespaceCandidates(INamespaceSymbol ns, string assemblyName, List<(Regex Regex, string? Namespace)> filters, Compilation compilation, List<CandidateModel> candidates)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol nested)
            {
                CollectNamespaceCandidates(nested, assemblyName, filters, compilation, candidates);
            }
            else if (member is INamedTypeSymbol type)
            {
                CollectTypeCandidates(type, assemblyName, filters, compilation, candidates);
            }
        }
    }

    private static void CollectTypeCandidates(INamedTypeSymbol type, string assemblyName, List<(Regex Regex, string? Namespace)> filters, Compilation compilation, List<CandidateModel> candidates)
    {
        // 入れ子型も対象 (ローカル候補の構文述語と揃える) / nested types included, matching the local candidate predicate
        foreach (var nested in type.GetTypeMembers())
        {
            CollectTypeCandidates(nested, assemblyName, filters, compilation, candidates);
        }

        if ((type.TypeKind != TypeKind.Class) || type.IsAbstract || type.IsStatic || (type.TypeParameters.Length > 0))
        {
            return;
        }

        var namespaceName = type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString();
        var matchedFilter = false;
        foreach (var (regex, filterNamespace) in filters)
        {
            if (!regex.IsMatch(type.Name))
            {
                continue;
            }

            if ((filterNamespace is not null)
                && (namespaceName != filterNamespace)
                && !namespaceName.StartsWith(filterNamespace + ".", StringComparison.Ordinal))
            {
                continue;
            }

            matchedFilter = true;
            break;
        }

        if (!matchedFilter || !compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
        {
            return;
        }

        candidates.Add(new CandidateModel(
            namespaceName,
            type.Name,
            CreateFactoryModel(type, compilation.Assembly),
            assemblyName,
            CollectInterfaces(type),
            string.Empty,
            0));
    }

    // 参照アセンブリの ComponentModule マーカーから生成モジュール型を収集する (AddGeneratedComponents の集約対象)。
    // アセンブリ属性の走査のみで参照内の型列挙は行わないため、増分ビルドへの影響は参照 1 件あたり属性リスト 1 回分。
    // SDK プロジェクトの参照は推移的に compilation へ渡るため、間接参照のモジュールもフラットに列挙される
    // Collects generated module types from the ComponentModule markers of referenced assemblies (aggregation targets
    // for AddGeneratedComponents). Only assembly attributes are inspected, never the types inside the references, so the
    // incremental cost is one attribute list per reference. SDK projects flow references transitively into the
    // compilation, so indirectly referenced modules are enumerated flat as well.
    private static EquatableArray<string> CollectReferencedModules(Compilation compilation)
    {
        var modules = new List<string>();
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
            {
                continue;
            }

            // 自アセンブリと同名の参照は集約しない (自己参照 = 二重登録の防止)
            // References with the same name as the current assembly are never aggregated (guards self references and duplicate registration).
            if (string.Equals(assembly.Name, compilation.AssemblyName, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var attribute in assembly.GetAttributes())
            {
                if ((attribute.AttributeClass?.ToDisplayString() == "BunnyTail.DependencyInjection.ComponentModuleAttribute")
                    && (attribute.ConstructorArguments.Length == 1)
                    && (attribute.ConstructorArguments[0].Value is INamedTypeSymbol moduleType))
                {
                    modules.Add(moduleType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }
            }
        }

        modules.Sort(StringComparer.Ordinal);
        return [with([.. modules])];
    }

    private static void EmitGeneratedComponents(SourceProductionContext context, string assemblyName, ComponentModel[] components, List<FactoryModel> unkeyedFactories, List<FactoryModel> keyedFactories, List<(string ElementServiceType, List<FactoryModel> Elements)> enumerableModels, InlineTargetMap inlineMap, EquatableArray<string> referencedModules, List<(string ImplementationType, string PostConstruct)> generatedInitializers)
    {
        var builder = new SourceBuilder();
        builder.AutoGenerated();
        builder.EnableNullable();
        builder.NewLine();

        // 属性コンポーネントを持つアセンブリはモジュールマーカーを埋め込み、参照側の集約対象になる
        // Assemblies with attribute components embed the module marker and become aggregation targets for referencing projects.
        if (components.Length > 0)
        {
            builder.AppendLine("[assembly: global::BunnyTail.DependencyInjection.ComponentModule(typeof(global::" + assemblyName + ".GeneratedComponents))]");
            builder.NewLine();
        }

        builder.Namespace(assemblyName);
        builder.NewLine();

        builder.Using("System.Runtime.CompilerServices");
        builder.NewLine();
        builder.Using("BunnyTail.DependencyInjection");
        builder.NewLine();
        builder.Using("Microsoft.Extensions.DependencyInjection");
        builder.NewLine();

        builder.AppendLine("public static class GeneratedComponents");
        builder.BeginScope();

        builder.AppendLine("[ModuleInitializer]");
        builder.AppendLine("internal static void InitializeGeneratedFactories()");
        builder.BeginScope();

        var first = true;
        foreach (var factory in unkeyedFactories)
        {
            if (!first)
            {
                builder.NewLine();
            }

            first = false;
            EmitFactoryRegistration(builder, factory, keyed: false, inlineMap);
        }

        foreach (var factory in keyedFactories)
        {
            if (!first)
            {
                builder.NewLine();
            }

            first = false;
            EmitFactoryRegistration(builder, factory, keyed: true, inlineMap);
        }

        foreach (var (elementServiceType, elements) in enumerableModels)
        {
            if (!first)
            {
                builder.NewLine();
            }

            first = false;
            EmitEnumerableRegistration(builder, elementServiceType, elements, inlineMap);
        }

        // [GenerateComponentFactory(PostConstruct = ...)] の初期化メソッド登録 (実行時経路との一致のため)
        // Initializer registrations of [GenerateComponentFactory(PostConstruct = ...)], keeping the runtime path consistent.
        foreach (var (implementationType, postConstruct) in generatedInitializers)
        {
            builder.NewLine();
            builder.Indent().Append("global::BunnyTail.DependencyInjection.Internal.GeneratedFactoryRegistry.RegisterInitializer(typeof(")
                .Append(implementationType)
                .Append("), \"")
                .Append(postConstruct)
                .Append("\");").NewLine();
        }

        builder.EndScope();

        // モジュール 1 個分の登録単位。集約側 (自アセンブリまたは参照元) から呼ばれる連携点で、利用者が呼ぶものではない。
        // 拡張メソッドにしないのは IServiceCollection の補完に出さないため。マーカーが指す型の契約名でもある
        // The registration unit of one module. It is the integration point invoked by the aggregator (this assembly or a
        // referencing one), not something users call. It is deliberately not an extension method so it stays out of
        // IServiceCollection completion, and its name is the contract of the type a marker points at.
        if (components.Length > 0)
        {
            builder.NewLine();
            builder.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
            builder.AppendLine("public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection RegisterComponents(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            builder.BeginScope();

            foreach (var component in components)
            {
                EmitComponentRegistration(builder, component);
            }

            builder.AppendLine("return services;");
            builder.EndScope();
        }

        // 利用者が呼ぶ唯一の登録メソッド。参照モジュールと自アセンブリの属性コンポーネントを 1 呼び出しで登録する。
        // 参照は推移的に見えるためフラットに列挙し、各モジュールは自分の分だけを登録する (連鎖させると二重登録になる)
        // The only registration method users call. It registers the referenced modules plus this assembly's attribute
        // components in one call. References are visible transitively, so the list is flat and each module registers
        // only its own components (chaining would register duplicates).
        if ((components.Length > 0) || (referencedModules.Count > 0))
        {
            builder.NewLine();
            builder.AppendLine("public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddGeneratedComponents(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            builder.BeginScope();

            foreach (var module in referencedModules)
            {
                builder.Indent().Append(module).Append(".RegisterComponents(services);").NewLine();
            }

            if (components.Length > 0)
            {
                builder.AppendLine("RegisterComponents(services);");
            }

            builder.AppendLine("return services;");
            builder.EndScope();
        }

        builder.EndScope();

        context.AddSource("GeneratedComponents.g.cs", builder);
    }

    // 依存解決は ServiceProviderScope への直接呼び出し (sealed) で出力する。
    // MEDI 拡張メソッド経由 (ISupportRequiredService 型テスト + 二重ディスパッチ) より約 0.8ns/件 短い
    // Dependency resolutions are emitted as direct calls on the sealed ServiceProviderScope,
    // about 0.8 ns per dependency shorter than the MEDI extension methods (type test + double dispatch).
    private static void EmitDependencyResolution(SourceBuilder builder, string typeName, int kind, string? keyLiteral, bool isValueType, Dictionary<string, (int Slot, bool Accessor)>? dependencyIndex)
    {
        switch (kind)
        {
            case DependencyKinds.ServiceKey:
                builder.Append('(').Append(typeName).Append(")key!");
                break;
            case DependencyKinds.KeyedExplicit:
                builder.Append("scope.GetRequiredKeyedService<").Append(typeName).Append(">(").Append(keyLiteral!).Append(')');
                break;
            case DependencyKinds.KeyedInherit:
                builder.Append("scope.GetRequiredKeyedService<").Append(typeName).Append(">(key)");
                break;
            default:
                if ((dependencyIndex is not null) && dependencyIndex.TryGetValue(typeName, out var dependencySlot))
                {
                    var slotLiteral = dependencySlot.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (dependencySlot.Accessor)
                    {
                        // アクセサスロット: 実現済み accessor の直接呼び出し (scoped / 不適格 transient など)。
                        // テーブル probe と GetRequiredService ラッパを飛ばす
                        // Accessor slot: direct call on the realized accessor (scoped, non-inlinable transients, ...),
                        // skipping the table probe and the GetRequiredService wrapper.
                        if (isValueType)
                        {
                            builder.Append('(').Append(typeName).Append(")global::System.Runtime.CompilerServices.Unsafe.As<global::BunnyTail.DependencyInjection.Internal.DependencyAccessor>(dependencies[").Append(slotLiteral).Append("])!.GetValue(scope)");
                        }
                        else
                        {
                            builder.Append("global::System.Runtime.CompilerServices.Unsafe.As<global::BunnyTail.DependencyInjection.Internal.DependencyAccessor>(dependencies[").Append(slotLiteral).Append("])!.GetValue<").Append(typeName).Append(">(scope)");
                        }
                    }
                    else if (isValueType)
                    {
                        // インスタンススロット: 解決済み 依存スロットの読み出しのみ。前提検証済みのため参照型は Unsafe.As
                        // Instance slot: just a resolved dependency slot read. Assumptions are validated, so reference types use Unsafe.As.
                        builder.Append('(').Append(typeName).Append(")dependencies[").Append(slotLiteral).Append("]!");
                    }
                    else
                    {
                        builder.Append("global::System.Runtime.CompilerServices.Unsafe.As<").Append(typeName).Append(">(dependencies[").Append(slotLiteral).Append("])!");
                    }
                }
                else
                {
                    builder.Append("scope.GetRequiredService<").Append(typeName).Append(">()");
                }

                break;
        }
    }

    // 出力される式の中に scope を使う解決が 1 つでもあるか (scope ローカルの要否)。
    // インスタンススロットだけが scope 不要で、アクセサスロットは GetValue(scope) を呼ぶ
    // Whether the emitted body contains any resolution that uses the scope (decides if the scope local is needed).
    // Only instance slots avoid the scope; accessor slots call GetValue(scope).
    private static bool NeedsScope(int kind, string typeName, InlineNode? node, Dictionary<string, (int Slot, bool Accessor)>? dependencyIndex)
    {
        if (node is null)
        {
            if (kind == DependencyKinds.ServiceKey)
            {
                return false;
            }

            if (kind != DependencyKinds.Service)
            {
                return true;
            }

            if ((dependencyIndex is null) || !dependencyIndex.TryGetValue(typeName, out var dependencySlot))
            {
                return true;
            }

            return dependencySlot.Accessor;
        }

        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var i = 0; i < node.Factory.Parameters.Count; i++)
        {
            if (NeedsScope(node.Factory.Parameters[i].Kind, node.Factory.Parameters[i].TypeName, node.Parameters[i], dependencyIndex))
            {
                return true;
            }
        }

        return false;
    }

    // 出力される式に現れる非インライン依存へ 依存スロットを割り当てる (ネストしたインライン展開の内側も含む)。
    // 一意な direct Singleton はインスタンススロット、それ以外の Service 依存はアクセサスロット
    // (解決可能なことだけを前提とするため、実装型の仮定は持たない)
    // Assigns dependency slots to the non-inlined dependencies appearing in the emitted body, including inside nested
    // inline expansions. Unambiguous direct singletons become instance slots; every other service dependency becomes
    // an accessor slot (which only assumes resolvability, so it carries no implementation assumption).
    private static void CollectDependencySlots(int kind, string typeName, InlineNode? node, InlineTargetMap map, Dictionary<string, (int Slot, bool Accessor)> dependencyIndex, List<(string Service, string? Implementation)> dependencyList)
    {
        if (node is null)
        {
            if ((kind == DependencyKinds.Service) && !dependencyIndex.ContainsKey(typeName))
            {
                var implementation = map.GetSingletonTarget(typeName);
                dependencyIndex[typeName] = (dependencyIndex.Count, implementation is null);
                dependencyList.Add((typeName, implementation));
            }

            return;
        }

        for (var i = 0; i < node.Factory.Parameters.Count; i++)
        {
            CollectDependencySlots(node.Factory.Parameters[i].Kind, node.Factory.Parameters[i].TypeName, node.Parameters[i], map, dependencyIndex, dependencyList);
        }
    }

    private static void EmitFactoryRegistration(SourceBuilder builder, FactoryModel factory, bool keyed, InlineTargetMap inlineMap)
    {
        // インライン展開の決定。前提 (InlinedDependency) として登録するのはトップレベルの展開のみ。
        // ネストした展開は、展開先コンポーネント自身の登録エントリが採用時に同じ前提を検証するため、
        // 直接依存の検証で推移的に全体が保証される
        // Decide inline expansion. Only top-level expansions are registered as assumptions (InlinedDependency).
        // Nested expansions are validated by the inlined component's own registry entry on adoption, so validating
        // direct dependencies transitively guarantees the whole graph.
        var stack = new List<string> { factory.ImplementationType };
        var parameterNodes = new InlineNode?[factory.Parameters.Count];
        for (var i = 0; i < factory.Parameters.Count; i++)
        {
            if (factory.Parameters[i].Kind == DependencyKinds.Service)
            {
                parameterNodes[i] = TryCreateInlineNode(factory.Parameters[i].TypeName, inlineMap, stack);
            }
        }

        var propertyNodes = new InlineNode?[factory.InjectProperties.Count];
        for (var i = 0; i < factory.InjectProperties.Count; i++)
        {
            if (factory.InjectProperties[i].Kind == DependencyKinds.Service)
            {
                propertyNodes[i] = TryCreateInlineNode(factory.InjectProperties[i].TypeName, inlineMap, stack);
            }
        }

        var assumptions = new List<InlineNode>();
        var assumed = new HashSet<string>(StringComparer.Ordinal);
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var node in parameterNodes.Concat(propertyNodes))
        {
            if ((node is not null) && assumed.Add(node.ServiceType))
            {
                assumptions.Add(node);
            }
        }

        // 依存スロット割り当て (unkeyed / keyed 共通。keyed 種別の依存はスロット対象外のまま key / scope 経由)
        // Dependency slot assignment (shared by unkeyed and keyed factories; keyed-kind dependencies stay on the key / scope path).
        var dependencyIndex = new Dictionary<string, (int Slot, bool Accessor)>(StringComparer.Ordinal);
        var dependencyList = new List<(string Service, string? Implementation)>();
        for (var i = 0; i < factory.Parameters.Count; i++)
        {
            CollectDependencySlots(factory.Parameters[i].Kind, factory.Parameters[i].TypeName, parameterNodes[i], inlineMap, dependencyIndex, dependencyList);
        }

        for (var i = 0; i < factory.InjectProperties.Count; i++)
        {
            CollectDependencySlots(factory.InjectProperties[i].Kind, factory.InjectProperties[i].TypeName, propertyNodes[i], inlineMap, dependencyIndex, dependencyList);
        }

        var emitDependencyIndex = dependencyList.Count > 0 ? dependencyIndex : null;

        builder.AppendLine(keyed
            ? "global::BunnyTail.DependencyInjection.Internal.GeneratedFactoryRegistry.RegisterKeyed("
            : "global::BunnyTail.DependencyInjection.Internal.GeneratedFactoryRegistry.Register(");
        builder.IndentLevel++;
        builder.Indent().Append("typeof(").Append(factory.ImplementationType).Append("),").NewLine();

        if (factory.Parameters.Count == 0)
        {
            builder.AppendLine("global::System.Type.EmptyTypes,");
        }
        else
        {
            builder.Indent().Append('[');
            for (var i = 0; i < factory.Parameters.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append("typeof(").Append(factory.Parameters[i].TypeName).Append(')');
            }

            builder.Append("],").NewLine();
        }

        if ((assumptions.Count > 0) || (dependencyList.Count > 0))
        {
            builder.Indent().Append('[');
            for (var i = 0; i < assumptions.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append("new global::BunnyTail.DependencyInjection.Internal.InlinedDependency(typeof(").Append(assumptions[i].ServiceType).Append("), typeof(").Append(assumptions[i].Factory.ImplementationType).Append("))");
            }

            builder.Append("],").NewLine();
        }

        if (dependencyList.Count > 0)
        {
            builder.Indent().Append('[');
            for (var i = 0; i < dependencyList.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                var (service, implementation) = dependencyList[i];
                if (implementation is null)
                {
                    builder.Append("new global::BunnyTail.DependencyInjection.Internal.DependencyPlan(typeof(").Append(service).Append("))");
                }
                else
                {
                    builder.Append("new global::BunnyTail.DependencyInjection.Internal.DependencyPlan(typeof(").Append(service).Append("), typeof(").Append(implementation).Append("))");
                }
            }

            builder.Append("],").NewLine();
        }

        var lambdaHeader = keyed
            ? (dependencyList.Count > 0 ? "static (provider, key, dependencies) => " : "static (provider, key) => ")
            : (dependencyList.Count > 0 ? "static (provider, dependencies) => " : "static provider => ");

        if ((factory.Parameters.Count == 0) && (factory.InjectProperties.Count == 0) && !factory.HasInitializer)
        {
            builder.Indent().Append(lambdaHeader).Append("new ").Append(factory.ImplementationType).Append("());").NewLine();
        }
        else
        {
            var needsScope = false;
            for (var i = 0; i < factory.Parameters.Count; i++)
            {
                needsScope = needsScope || NeedsScope(factory.Parameters[i].Kind, factory.Parameters[i].TypeName, parameterNodes[i], emitDependencyIndex);
            }

            for (var i = 0; i < factory.InjectProperties.Count; i++)
            {
                needsScope = needsScope || NeedsScope(factory.InjectProperties[i].Kind, factory.InjectProperties[i].TypeName, propertyNodes[i], emitDependencyIndex);
            }

            builder.Indent().Append(lambdaHeader.TrimEnd()).NewLine();
            builder.BeginScope();

            if (needsScope)
            {
                builder.AppendLine("var scope = (global::BunnyTail.DependencyInjection.ServiceProviderScope)provider;");
            }

            if (factory.Parameters.Count == 0)
            {
                builder.Indent().Append("var instance = new ").Append(factory.ImplementationType).Append("();").NewLine();
            }
            else
            {
                builder.Indent().Append("var instance = new ").Append(factory.ImplementationType).Append('(').NewLine();
                builder.IndentLevel++;
                for (var i = 0; i < factory.Parameters.Count; i++)
                {
                    var parameter = factory.Parameters[i];
                    builder.Indent();
                    EmitArgument(builder, parameterNodes[i], parameter.TypeName, parameter.Kind, parameter.KeyLiteral, parameter.IsValueType, emitDependencyIndex);
                    builder.Append(i < factory.Parameters.Count - 1 ? "," : ");").NewLine();
                }

                builder.IndentLevel--;
            }

            for (var i = 0; i < factory.InjectProperties.Count; i++)
            {
                var property = factory.InjectProperties[i];
                builder.Indent().Append("instance.").Append(property.Name).Append(" = ");
                EmitArgument(builder, propertyNodes[i], property.TypeName, property.Kind, property.KeyLiteral, property.IsValueType, emitDependencyIndex);
                builder.Append(';').NewLine();
            }

            // 初期化コールバック (プロパティ注入の後。PostConstruct 指定が IInitializable より優先)
            // Initialization callback (after property injection; PostConstruct takes precedence over IInitializable).
            if (factory.PostConstruct is not null)
            {
                if (!factory.InvalidPostConstruct)
                {
                    builder.Indent().Append("instance.").Append(factory.PostConstruct).Append("();").NewLine();
                }
            }
            else if (factory.InitializableInterface)
            {
                builder.AppendLine("((global::BunnyTail.DependencyInjection.IInitializable)instance).Initialize();");
            }

            builder.AppendLine("return instance;");

            // lambda ブロックを閉じる (BeginScope の +1 を戻して "});" を出力)
            // Closes the lambda block (undo BeginScope's +1 and emit "});").
            builder.IndentLevel--;
            builder.Indent().Append("});").NewLine();
        }

        builder.IndentLevel--;
    }

    // 生成 enumerable ファクトリ: 全要素 transient の実体化を配列リテラルへ畳む
    // Generated enumerable factory folding the all-transient materialization into an array literal.
    private static void EmitEnumerableRegistration(SourceBuilder builder, string elementServiceType, List<FactoryModel> elements, InlineTargetMap inlineMap)
    {
        var stack = new List<string>();
        var nodes = new InlineNode?[elements.Count];
        var needsScope = false;
        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            var children = new InlineNode?[element.Parameters.Count];
            for (var j = 0; j < element.Parameters.Count; j++)
            {
                if (element.Parameters[j].Kind == DependencyKinds.Service)
                {
                    children[j] = TryCreateInlineNode(element.Parameters[j].TypeName, inlineMap, stack);
                }

                needsScope = needsScope || NeedsScope(element.Parameters[j].Kind, element.Parameters[j].TypeName, children[j], null);
            }

            nodes[i] = new InlineNode(element.ImplementationType, element, children);
        }

        builder.AppendLine("global::BunnyTail.DependencyInjection.Internal.GeneratedFactoryRegistry.RegisterEnumerable(");
        builder.IndentLevel++;
        builder.Indent().Append("typeof(").Append(elementServiceType).Append("),").NewLine();

        builder.Indent().Append('[');
        for (var i = 0; i < elements.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append("typeof(").Append(elements[i].ImplementationType).Append(')');
        }

        builder.Append("],").NewLine();

        builder.AppendLine("static provider =>");
        builder.BeginScope();
        if (needsScope)
        {
            builder.AppendLine("var scope = (global::BunnyTail.DependencyInjection.ServiceProviderScope)provider;");
        }

        builder.Indent().Append("return new ").Append(elementServiceType).Append("[]").NewLine();
        builder.AppendLine("{");
        builder.IndentLevel++;
        for (var i = 0; i < elements.Count; i++)
        {
            builder.Indent();
            EmitInlineNew(builder, nodes[i]!, null);
            builder.Append(',').NewLine();
        }

        builder.IndentLevel--;
        builder.AppendLine("};");

        builder.IndentLevel--;
        builder.Indent().Append("});").NewLine();
        builder.IndentLevel--;
    }

    // インライン展開ノードがあればリテラル new を、なければ従来の解決式を出力する
    // Emits a literal new when an inline node exists, otherwise the ordinary resolution expression.
    private static void EmitArgument(SourceBuilder builder, InlineNode? node, string typeName, int kind, string? keyLiteral, bool isValueType, Dictionary<string, (int Slot, bool Accessor)>? dependencyIndex)
    {
        if (node is null)
        {
            EmitDependencyResolution(builder, typeName, kind, keyLiteral, isValueType, dependencyIndex);
        }
        else
        {
            EmitInlineNew(builder, node, dependencyIndex);
        }
    }

    // transient 依存のリテラル new 展開。同一依存も使用箇所ごとに new を出力する
    // (MEDI 互換: transient は都度新規生成。インスタンスを共有してはならない)
    // Literal new expansion of transient dependencies. The same dependency gets a fresh new at every use site
    // (MEDI compatible: transients are created per use and must never be shared).
    private static void EmitInlineNew(SourceBuilder builder, InlineNode node, Dictionary<string, (int Slot, bool Accessor)>? dependencyIndex)
    {
        builder.Append("new ").Append(node.Factory.ImplementationType).Append('(');
        for (var i = 0; i < node.Factory.Parameters.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            var parameter = node.Factory.Parameters[i];
            EmitArgument(builder, node.Parameters[i], parameter.TypeName, parameter.Kind, parameter.KeyLiteral, parameter.IsValueType, dependencyIndex);
        }

        builder.Append(')');
    }

    private static void EmitComponentRegistration(SourceBuilder builder, ComponentModel component)
    {
        var implementationType = component.Factory.ImplementationType;

        if (component.KeyLiteral is not null)
        {
            if (component.AsType is not null)
            {
                builder.Indent().Append("services.AddKeyed").Append(component.Lifetime).Append('<').Append(component.AsType).Append(", ").Append(implementationType).Append(">(").Append(component.KeyLiteral).Append(");").NewLine();
            }
            else
            {
                builder.Indent().Append("services.AddKeyed").Append(component.Lifetime).Append('<').Append(implementationType).Append(">(").Append(component.KeyLiteral).Append(");").NewLine();
            }

            return;
        }

        if (component.AsType is not null)
        {
            builder.Indent().Append("services.Add").Append(component.Lifetime).Append('<').Append(component.AsType).Append(", ").Append(implementationType).Append(">();").NewLine();
            return;
        }

        builder.Indent().Append("services.Add").Append(component.Lifetime).Append('<').Append(implementationType).Append(">();").NewLine();
        foreach (var interfaceType in component.Interfaces)
        {
            builder.Indent().Append("services.Add").Append(component.Lifetime).Append('<').Append(interfaceType).Append(">(static provider => ((global::BunnyTail.DependencyInjection.ServiceProviderScope)provider).GetRequiredService<").Append(implementationType).Append(">());").NewLine();
        }
    }

    private static void EmitConventionClass(
        SourceProductionContext context,
        string? classNamespace,
        string className,
        List<(MethodModel Method, List<(CandidateModel Candidate, string Lifetime)> Matches)> methods)
    {
        var builder = new SourceBuilder();
        builder.AutoGenerated();
        builder.EnableNullable();
        builder.NewLine();

        if (classNamespace is not null)
        {
            builder.Namespace(classNamespace);
            builder.NewLine();
        }

        builder.Using("Microsoft.Extensions.DependencyInjection");
        builder.NewLine();

        builder.Indent().Append("partial class ").Append(className).NewLine();
        builder.BeginScope();

        var first = true;
        foreach (var (method, matches) in methods)
        {
            if (!first)
            {
                builder.NewLine();
            }

            first = false;
            EmitConventionMethod(builder, method, matches);
        }

        builder.EndScope();

        var hintName = (classNamespace is null ? className : classNamespace.Replace('.', '_') + "_" + className) + ".g.cs";
        context.AddSource(hintName, builder);
    }

    private static void EmitConventionMethod(SourceBuilder builder, MethodModel method, List<(CandidateModel Candidate, string Lifetime)> matches)
    {
        builder.Indent().Append(method.MethodAccessibility.ToText()).Append(" static partial global::Microsoft.Extensions.DependencyInjection.IServiceCollection ").Append(method.MethodName).Append("(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)").NewLine();
        builder.BeginScope();

        foreach (var (candidate, lifetime) in matches)
        {
            var implementationType = candidate.Factory.ImplementationType;

            // ServiceRegistration 互換: 0 iface → 自己 / 1 iface → IFace,Impl / 2+ → 自己 + フォワーディング
            // ServiceRegistration compatible: 0 interfaces -> self / 1 -> IFace,Impl / 2+ -> self + forwarding.
            if (candidate.Interfaces.Count == 0)
            {
                builder.Indent().Append("services.Add").Append(lifetime).Append('<').Append(implementationType).Append(">();").NewLine();
            }
            else if (candidate.Interfaces.Count == 1)
            {
                builder.Indent().Append("services.Add").Append(lifetime).Append('<').Append(candidate.Interfaces[0]).Append(", ").Append(implementationType).Append(">();").NewLine();
            }
            else
            {
                builder.Indent().Append("services.Add").Append(lifetime).Append('<').Append(implementationType).Append(">();").NewLine();
                foreach (var interfaceType in candidate.Interfaces)
                {
                    builder.Indent().Append("services.Add").Append(lifetime).Append('<').Append(interfaceType).Append(">(static provider => ((global::BunnyTail.DependencyInjection.ServiceProviderScope)provider).GetRequiredService<").Append(implementationType).Append(">());").NewLine();
                }
            }
        }

        builder.AppendLine("return services;");
        builder.EndScope();
    }
}
