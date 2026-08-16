namespace BunnyTail.Resolver.Generator;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SourceGenerateHelper;

[Generator]
public sealed class ResolverGenerator : IIncrementalGenerator
{
    private const string SingletonAttributeName = "BunnyTail.Resolver.SingletonAttribute";
    private const string ScopedAttributeName = "BunnyTail.Resolver.ScopedAttribute";
    private const string TransientAttributeName = "BunnyTail.Resolver.TransientAttribute";
    private const string InjectAttributeName = "BunnyTail.Resolver.InjectAttribute";
    private const string InitializableInterfaceName = "global::BunnyTail.Resolver.IInitializable";
    private const string ComponentRegistrationAttributeName = "BunnyTail.Resolver.ComponentRegistrationAttribute";
    private const string FromKeyedServicesAttributeName = "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute";
    private const string ServiceKeyAttributeName = "Microsoft.Extensions.DependencyInjection.ServiceKeyAttribute";
    private const string ServiceCollectionExtensionsName = "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions";
    private const string ServiceCollectionDescriptorExtensionsName = "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions";
    private const string ServiceCollectionName = "Microsoft.Extensions.DependencyInjection.IServiceCollection";

    // ------------------------------------------------------------
    // Diagnostics
    // ------------------------------------------------------------

#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor InvalidMethodDefinition = new(
        "BTRS0001",
        "Invalid registration method",
        "Method '{0}' must be a static partial extension method with an IServiceCollection parameter and return type",
        "BunnyTail.Resolver",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor InvalidPattern = new(
        "BTRS0002",
        "Invalid registration pattern",
        "Pattern '{0}' is not a valid regular expression",
        "BunnyTail.Resolver",
        DiagnosticSeverity.Warning,
        true);

    private static readonly DiagnosticDescriptor CircularDependency = new(
        "BTRS0003",
        "Circular dependency",
        "A circular dependency was detected: {0}",
        "BunnyTail.Resolver",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor UnresolvedDependency = new(
        "BTRS0004",
        "Unresolved dependency",
        "Unable to resolve dependency '{0}' required by '{1}' from the registrations visible at compile time",
        "BunnyTail.Resolver",
        DiagnosticSeverity.Warning,
        true);

    private static readonly DiagnosticDescriptor CaptiveDependency = new(
        "BTRS0005",
        "Captive dependency",
        "Singleton component '{0}' depends on scoped service '{1}'",
        "BunnyTail.Resolver",
        DiagnosticSeverity.Warning,
        true);

    private static readonly DiagnosticDescriptor AmbiguousConstructor = new(
        "BTRS0006",
        "Ambiguous constructor",
        "Type '{0}' has multiple public constructors with the same maximum parameter count",
        "BunnyTail.Resolver",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor InvalidPostConstruct = new(
        "BTRS0007",
        "Invalid PostConstruct method",
        "The PostConstruct method '{0}' on type '{1}' must be a public parameterless instance method returning void",
        "BunnyTail.Resolver",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor ConflictingPostConstruct = new(
        "BTRS0008",
        "Conflicting PostConstruct specifications",
        "Type '{0}' has conflicting PostConstruct specifications across its lifetime attributes",
        "BunnyTail.Resolver",
        DiagnosticSeverity.Error,
        true);
#pragma warning restore RS2008

    // ------------------------------------------------------------
    // Initialize
    // ------------------------------------------------------------

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
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

        var assemblyNameProvider = context.CompilationProvider
            .Select(static (compilation, _) => compilation.AssemblyName ?? "Generated");

        var source = singletonProvider.Collect()
            .Combine(scopedProvider.Collect())
            .Combine(transientProvider.Collect())
            .Combine(collectedProvider.Collect())
            .Combine(methodProvider.Collect())
            .Combine(candidateProvider.Collect())
            .Combine(assemblyNameProvider);

        context.RegisterSourceOutput(source, static (context, source) =>
            Execute(
                context,
                source.Left.Left.Left.Left.Left.Left,
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

        // 同数の最大コンストラクタが複数あり、互いに superset でない場合は曖昧 (BTRS0006)
        // Ambiguous when multiple constructors share the maximum parameter count and are not supersets of each other (BTRS0006).
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
                parameters[i] = new ParameterModel(typeName, kind, keyLiteral, inCompilation, isValueType);

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

            injectProperties.Add(new PropertyModel(property.Name, typeName, kind, keyLiteral, inCompilation, isValueType));
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

        // 初期化コールバック: ライフタイム属性の PostConstruct 指定を収集する (相違があれば BTRS0008)
        // Initialization callback: collect PostConstruct from lifetime attributes (BTRS0008 when they disagree).
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
            new EquatableArray<ParameterModel>(parameters),
            new EquatableArray<PropertyModel>(injectProperties.ToArray()),
            eligibleUnkeyed,
            eligibleKeyed,
            ambiguous,
            disposable,
            postConstruct,
            initializableInterface,
            invalidPostConstruct,
            conflictingPostConstruct);
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
                        MethodKind: MethodKind.Ordinary,
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
        return new EquatableArray<string>(interfaces);
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

        // MEDI の登録拡張メソッドのみ対象 (keyed 登録 API は対象外)
        // Only MEDI registration extension methods are collected (keyed registration APIs are excluded).
        var methodName = method.Name;
        var lifetime = methodName switch
        {
            "AddSingleton" or "TryAddSingleton" => "Singleton",
            "AddScoped" or "TryAddScoped" => "Scoped",
            "AddTransient" or "TryAddTransient" => "Transient",
            _ => null,
        };
        if (lifetime is null)
        {
            return null;
        }

        var containingType = method.ContainingType?.ToDisplayString();
        if (containingType is not (ServiceCollectionExtensionsName or ServiceCollectionDescriptorExtensionsName))
        {
            return null;
        }

        // ジェネリックオーバーロードのみ対象。factory/instance オーバーロード (delegate / 型引数の実引数) は
        // コンテナが型をインスタンス化しないため対象外
        // Only generic overloads are collected. Factory/instance overloads (delegates or instance arguments) are
        // excluded because the container does not instantiate the type.
        if (method.TypeArguments.Length == 0)
        {
            return null;
        }

        foreach (var parameter in method.Parameters)
        {
            if ((parameter.Type.TypeKind == TypeKind.Delegate) || (parameter.Type is ITypeParameterSymbol))
            {
                return null;
            }
        }

        var implementationType = method.TypeArguments[method.TypeArguments.Length - 1];
        if (implementationType is not INamedTypeSymbol implementationSymbol)
        {
            return null;
        }

        // closed generic は対象 (new Foo<int>() は生成可能)。型パラメータを含む場合は対象外
        // Closed generics are eligible (new Foo<int>() can be generated); types containing type parameters are not.
        if (implementationSymbol.IsAbstract
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
        if (!factory.EligibleUnkeyed)
        {
            return null;
        }

        var serviceType = method.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new CollectedModel(factory, serviceType, lifetime, invocation.SyntaxTree.FilePath, invocation.SpanStart);
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
            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key == "Namespace")
                {
                    ns = argument.Value.Value as string;
                }
            }

            patterns.Add(new PatternModel(lifetime, pattern, ns));
        }

        var containingNamespace = symbol.ContainingType.ContainingNamespace.IsGlobalNamespace
            ? null
            : symbol.ContainingType.ContainingNamespace.ToDisplayString();

        return Results.Success(new MethodModel(
            containingNamespace,
            symbol.ContainingType.Name,
            symbol.Name,
            symbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
            new EquatableArray<PatternModel>(patterns.ToArray()),
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
        if (context.SemanticModel.GetDeclaredSymbol(syntax) is not INamedTypeSymbol symbol)
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
            symbol.Name,
            symbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : symbol.ContainingNamespace.ToDisplayString(),
            CreateFactoryModel(symbol, context.SemanticModel.Compilation.Assembly),
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
        string assemblyName)
    {
        foreach (var method in methods)
        {
            foreach (var info in method.Diagnostics)
            {
                context.ReportDiagnostic(info.ToDiagnostic());
            }
        }

        var components = singletons.Concat(scopeds).Concat(transients)
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

                foreach (var candidate in sortedCandidates)
                {
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
        ReportAnalysisDiagnostics(context, components, sortedCollected, conventionMatches);

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
            if (emittedUnkeyed.Add(model.Factory.ImplementationType))
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

        if ((components.Length > 0) || (unkeyedFactories.Count > 0) || (keyedFactories.Count > 0))
        {
            var inlineMap = BuildInlineTargetMap(components, sortedCollected, conventionMatches);
            EmitGeneratedComponents(context, assemblyName, components, unkeyedFactories, keyedFactories, inlineMap);
        }

        // ---- 規約登録メソッドの本体 / convention registration method bodies ----

        foreach (var (method, matches) in conventionMatches)
        {
            EmitConventionMethod(context, method, matches);
        }
    }

    // ------------------------------------------------------------
    // Diagnostics (compile-time analysis / コンパイル時解析)
    // ------------------------------------------------------------

    private static void ReportAnalysisDiagnostics(
        SourceProductionContext context,
        ComponentModel[] components,
        CollectedModel[] collected,
        List<(MethodModel Method, List<(CandidateModel Candidate, string Lifetime)> Matches)> conventionMatches)
    {
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

        // BTRS0004 (未解決) / BTRS0005 (captive) / BTRS0006 (曖昧 ctor) — 属性コンポーネント起点
        // BTRS0004 (unresolved) / BTRS0005 (captive) / BTRS0006 (ambiguous ctor) reported from attribute components.
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
                else if (inCompilation && !typeName.StartsWith("global::System.", StringComparison.Ordinal))
                {
                    // コンパイル対象アセンブリ内の型で、コンパイル時に見える登録に無いもののみ警告 (実行時登録は見えないため Warning)
                    // Warns only for types inside the compiling assembly missing from compile-time visible registrations (runtime registrations are invisible, hence Warning).
                    context.ReportDiagnostic(new DiagnosticInfo(UnresolvedDependency, component.Location, Display(typeName), Display(component.Factory.ImplementationType)).ToDiagnostic());
                }
            }
        }

        // BTRS0003 (循環) — 生成対象ノード全体で DFS
        // BTRS0003 (cycles) — DFS over all generation target nodes.
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

        // 一意な direct Singleton 登録 (サービス型 → 実装型)。deps 配列渡しの対象
        // Unambiguous direct singleton registrations (service type -> implementation type), eligible for the deps array.
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

        // 属性コンポーネント (AddComponents の登録形と一致させる)
        // Attribute components (kept consistent with the registration shape of AddComponents).
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

        // Add* 呼び出し収集 (ジェネリックオーバーロードのみ = 全て direct)
        // Add* invocation collection (generic overloads only = all direct).
        foreach (var model in collected)
        {
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

            // Singleton: deps 配列渡しの対象 (disposal/初期化/[Inject] は実装側の accessor が担うため制限なし)
            // Singletons are deps-array candidates (disposal/initialization/[Inject] are handled by the dependency's own accessor).
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

    private static InlineNode? TryCreateInlineNode(string serviceTypeName, InlineTargetMap map, List<string> stack)
    {
        var factory = map.GetTarget(serviceTypeName);
        if ((factory is null) || stack.Contains(factory.ImplementationType))
        {
            return null;   // 展開不可 or 循環 / not expandable or cyclic (cycles themselves error separately via BTRS0003)
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

    private static void EmitGeneratedComponents(SourceProductionContext context, string assemblyName, ComponentModel[] components, List<FactoryModel> unkeyedFactories, List<FactoryModel> keyedFactories, InlineTargetMap inlineMap)
    {
        var builder = new SourceBuilder();
        builder.AutoGenerated();
        builder.EnableNullable();
        builder.NewLine();

        builder.Namespace(assemblyName);
        builder.NewLine();

        builder.Using("System.Runtime.CompilerServices");
        builder.NewLine();
        builder.Using("BunnyTail.Resolver");
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

        builder.EndScope();

        if (components.Length > 0)
        {
            builder.NewLine();
            builder.AppendLine("public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddComponents(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            builder.BeginScope();

            foreach (var component in components)
            {
                EmitComponentRegistration(builder, component);
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
    private static void EmitDependencyResolution(SourceBuilder builder, string typeName, int kind, string? keyLiteral, bool isValueType, Dictionary<string, int>? depsIndex)
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
                if ((depsIndex is not null) && depsIndex.TryGetValue(typeName, out var depSlot))
                {
                    // singleton 依存: 解決済み deps スロットの読み出しのみ。前提検証済みのため参照型は Unsafe.As
                    // Singleton dependency: just a resolved deps slot read. Assumptions are validated, so reference types use Unsafe.As.
                    if (isValueType)
                    {
                        builder.Append('(').Append(typeName).Append(")deps[").Append(depSlot.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("]!");
                    }
                    else
                    {
                        builder.Append("global::System.Runtime.CompilerServices.Unsafe.As<").Append(typeName).Append(">(deps[").Append(depSlot.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("])!");
                    }
                }
                else
                {
                    builder.Append("scope.GetRequiredService<").Append(typeName).Append(">()");
                }

                break;
        }
    }

    // 出力される式の中に scope 経由の解決が 1 つでもあるか (scope ローカルの要否)。deps スロットは scope を使わない
    // Whether the emitted body contains any scope-based resolution (decides if the scope local is needed). Deps slots never use the scope.
    private static bool NeedsScope(int kind, string typeName, InlineNode? node, Dictionary<string, int>? depsIndex)
    {
        if (node is null)
        {
            if (kind == DependencyKinds.ServiceKey)
            {
                return false;
            }

            return (kind != DependencyKinds.Service) || (depsIndex is null) || !depsIndex.ContainsKey(typeName);
        }

        for (var i = 0; i < node.Factory.Parameters.Count; i++)
        {
            if (NeedsScope(node.Factory.Parameters[i].Kind, node.Factory.Parameters[i].TypeName, node.Parameters[i], depsIndex))
            {
                return true;
            }
        }

        return false;
    }

    // 出力される式に現れる singleton 依存へ deps スロットを割り当てる (ネストしたインライン展開の内側も含む)
    // Assigns deps slots to the singleton dependencies appearing in the emitted body, including inside nested inline expansions.
    private static void CollectSingletonSlots(int kind, string typeName, InlineNode? node, InlineTargetMap map, Dictionary<string, int> depsIndex, List<(string Service, string Implementation)> depsList)
    {
        if (node is null)
        {
            if (kind == DependencyKinds.Service)
            {
                var implementation = map.GetSingletonTarget(typeName);
                if ((implementation is not null) && !depsIndex.ContainsKey(typeName))
                {
                    depsIndex[typeName] = depsIndex.Count;
                    depsList.Add((typeName, implementation));
                }
            }

            return;
        }

        for (var i = 0; i < node.Factory.Parameters.Count; i++)
        {
            CollectSingletonSlots(node.Factory.Parameters[i].Kind, node.Factory.Parameters[i].TypeName, node.Parameters[i], map, depsIndex, depsList);
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
        foreach (var node in parameterNodes.Concat(propertyNodes))
        {
            if ((node is not null) && assumed.Add(node.ServiceType))
            {
                assumptions.Add(node);
            }
        }

        // singleton 依存の deps スロット割り当て (unkeyed のみ。keyed は従来経路)
        // Deps slot assignment for singleton dependencies (unkeyed only; keyed factories keep the scope path).
        var depsIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var depsList = new List<(string Service, string Implementation)>();
        if (!keyed)
        {
            for (var i = 0; i < factory.Parameters.Count; i++)
            {
                CollectSingletonSlots(factory.Parameters[i].Kind, factory.Parameters[i].TypeName, parameterNodes[i], inlineMap, depsIndex, depsList);
            }

            for (var i = 0; i < factory.InjectProperties.Count; i++)
            {
                CollectSingletonSlots(factory.InjectProperties[i].Kind, factory.InjectProperties[i].TypeName, propertyNodes[i], inlineMap, depsIndex, depsList);
            }
        }

        var emitDepsIndex = depsList.Count > 0 ? depsIndex : null;

        builder.AppendLine(keyed
            ? "global::BunnyTail.Resolver.GeneratedComponentRegistry.RegisterKeyed("
            : "global::BunnyTail.Resolver.GeneratedComponentRegistry.Register(");
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

        if ((assumptions.Count > 0) || (depsList.Count > 0))
        {
            builder.Indent().Append('[');
            for (var i = 0; i < assumptions.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append("new global::BunnyTail.Resolver.InlinedDependency(typeof(").Append(assumptions[i].ServiceType).Append("), typeof(").Append(assumptions[i].Factory.ImplementationType).Append("))");
            }

            builder.Append("],").NewLine();
        }

        if (depsList.Count > 0)
        {
            builder.Indent().Append('[');
            for (var i = 0; i < depsList.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append("new global::BunnyTail.Resolver.InlinedDependency(typeof(").Append(depsList[i].Service).Append("), typeof(").Append(depsList[i].Implementation).Append("))");
            }

            builder.Append("],").NewLine();
        }

        var lambdaHeader = keyed
            ? "static (provider, key) => "
            : (depsList.Count > 0 ? "static (provider, deps) => " : "static provider => ");

        if ((factory.Parameters.Count == 0) && (factory.InjectProperties.Count == 0) && !factory.HasInitializer)
        {
            builder.Indent().Append(lambdaHeader).Append("new ").Append(factory.ImplementationType).Append("());").NewLine();
        }
        else
        {
            var needsScope = false;
            for (var i = 0; i < factory.Parameters.Count; i++)
            {
                needsScope = needsScope || NeedsScope(factory.Parameters[i].Kind, factory.Parameters[i].TypeName, parameterNodes[i], emitDepsIndex);
            }

            for (var i = 0; i < factory.InjectProperties.Count; i++)
            {
                needsScope = needsScope || NeedsScope(factory.InjectProperties[i].Kind, factory.InjectProperties[i].TypeName, propertyNodes[i], emitDepsIndex);
            }

            builder.Indent().Append(lambdaHeader.TrimEnd()).NewLine();
            builder.BeginScope();

            if (needsScope)
            {
                builder.AppendLine("var scope = (global::BunnyTail.Resolver.ServiceProviderScope)provider;");
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
                    EmitArgument(builder, parameterNodes[i], parameter.TypeName, parameter.Kind, parameter.KeyLiteral, parameter.IsValueType, emitDepsIndex);
                    builder.Append(i < factory.Parameters.Count - 1 ? "," : ");").NewLine();
                }

                builder.IndentLevel--;
            }

            for (var i = 0; i < factory.InjectProperties.Count; i++)
            {
                var property = factory.InjectProperties[i];
                builder.Indent().Append("instance.").Append(property.Name).Append(" = ");
                EmitArgument(builder, propertyNodes[i], property.TypeName, property.Kind, property.KeyLiteral, property.IsValueType, emitDepsIndex);
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
                builder.AppendLine("((global::BunnyTail.Resolver.IInitializable)instance).Initialize();");
            }

            builder.AppendLine("return instance;");

            // lambda ブロックを閉じる (BeginScope の +1 を戻して "});" を出力)
            // Closes the lambda block (undo BeginScope's +1 and emit "});").
            builder.IndentLevel--;
            builder.Indent().Append("});").NewLine();
        }

        builder.IndentLevel--;
    }

    // インライン展開ノードがあればリテラル new を、なければ従来の解決式を出力する
    // Emits a literal new when an inline node exists, otherwise the ordinary resolution expression.
    private static void EmitArgument(SourceBuilder builder, InlineNode? node, string typeName, int kind, string? keyLiteral, bool isValueType, Dictionary<string, int>? depsIndex)
    {
        if (node is null)
        {
            EmitDependencyResolution(builder, typeName, kind, keyLiteral, isValueType, depsIndex);
        }
        else
        {
            EmitInlineNew(builder, node, depsIndex);
        }
    }

    // transient 依存のリテラル new 展開。同一依存も使用箇所ごとに new を出力する
    // (MEDI 互換: transient は都度新規生成。インスタンスを共有してはならない)
    // Literal new expansion of transient dependencies. The same dependency gets a fresh new at every use site
    // (MEDI compatible: transients are created per use and must never be shared).
    private static void EmitInlineNew(SourceBuilder builder, InlineNode node, Dictionary<string, int>? depsIndex)
    {
        builder.Append("new ").Append(node.Factory.ImplementationType).Append('(');
        for (var i = 0; i < node.Factory.Parameters.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            var parameter = node.Factory.Parameters[i];
            EmitArgument(builder, node.Parameters[i], parameter.TypeName, parameter.Kind, parameter.KeyLiteral, parameter.IsValueType, depsIndex);
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
            builder.Indent().Append("services.Add").Append(component.Lifetime).Append('<').Append(interfaceType).Append(">(static provider => ((global::BunnyTail.Resolver.ServiceProviderScope)provider).GetRequiredService<").Append(implementationType).Append(">());").NewLine();
        }
    }

    private static void EmitConventionMethod(SourceProductionContext context, MethodModel method, List<(CandidateModel Candidate, string Lifetime)> matches)
    {
        var builder = new SourceBuilder();
        builder.AutoGenerated();
        builder.EnableNullable();
        builder.NewLine();

        if (method.Namespace is not null)
        {
            builder.Namespace(method.Namespace);
            builder.NewLine();
        }

        builder.Using("Microsoft.Extensions.DependencyInjection");
        builder.NewLine();

        builder.Indent().Append("partial class ").Append(method.ClassName).NewLine();
        builder.BeginScope();

        builder.Indent().Append(method.Accessibility).Append(" static partial global::Microsoft.Extensions.DependencyInjection.IServiceCollection ").Append(method.MethodName).Append("(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)").NewLine();
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
                    builder.Indent().Append("services.Add").Append(lifetime).Append('<').Append(interfaceType).Append(">(static provider => ((global::BunnyTail.Resolver.ServiceProviderScope)provider).GetRequiredService<").Append(implementationType).Append(">());").NewLine();
                }
            }
        }

        builder.AppendLine("return services;");
        builder.EndScope();

        builder.EndScope();

        var hintName = (method.Namespace is null ? method.ClassName : method.Namespace.Replace('.', '_') + "_" + method.ClassName) + ".g.cs";
        context.AddSource(hintName, builder);
    }
}
