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
    // Parser : shared factory analysis
    // ------------------------------------------------------------

    private static FactoryModel CreateFactoryModel(INamedTypeSymbol symbol, IAssemblySymbol compilationAssembly)
    {
        var implementationType = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // コンストラクタ: MEDI 規則の前提 = 最大パラメータの public コンストラクタ
        var constructors = symbol.InstanceConstructors
            .Where(static x => x.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(static x => x.Parameters.Length)
            .ToArray();
        var constructor = constructors.Length > 0 ? constructors[0] : null;

        // 同数の最大コンストラクタが複数あり、互いに superset でない場合は曖昧 (BTRS0006)
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
                var (typeName, kind, keyLiteral, inCompilation) = CreateDependencyModel(parameter.Type, parameter.GetAttributes(), compilationAssembly);
                parameters[i] = new ParameterModel(typeName, kind, keyLiteral, inCompilation);

                // 既定値付き引数は生成ファクトリ不可 (GetRequiredService と挙動が変わるため互換経路へ)
                if (parameter.HasExplicitDefaultValue)
                {
                    eligibleUnkeyed = false;
                    eligibleKeyed = false;
                }

                // keyed 依存 ([ServiceKey]/[FromKeyedServices]) は keyed ファクトリでのみ扱える
                if (kind != DependencyKinds.Service)
                {
                    eligibleUnkeyed = false;
                }
            }
        }

        // [Inject] プロパティ
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

            var (typeName, kind, keyLiteral, inCompilation) = CreateDependencyModel(property.Type, property.GetAttributes(), compilationAssembly);
            if (kind == DependencyKinds.ServiceKey)
            {
                // プロパティへの [ServiceKey] は非対応
                eligibleUnkeyed = false;
                eligibleKeyed = false;
                continue;
            }

            if (kind != DependencyKinds.Service)
            {
                eligibleUnkeyed = false;
            }

            injectProperties.Add(new PropertyModel(property.Name, typeName, kind, keyLiteral, inCompilation));
        }

        return new FactoryModel(
            implementationType,
            new EquatableArray<ParameterModel>(parameters),
            new EquatableArray<PropertyModel>(injectProperties.ToArray()),
            eligibleUnkeyed,
            eligibleKeyed,
            ambiguous);
    }

    private static (string TypeName, int Kind, string? KeyLiteral, bool InCompilation) CreateDependencyModel(ITypeSymbol type, ImmutableArray<AttributeData> attributes, IAssemblySymbol compilationAssembly)
    {
        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var inCompilation = SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilationAssembly);

        foreach (var attribute in attributes)
        {
            var attributeName = attribute.AttributeClass?.ToDisplayString();
            if (attributeName == ServiceKeyAttributeName)
            {
                return (typeName, DependencyKinds.ServiceKey, null, inCompilation);
            }

            if (attributeName == FromKeyedServicesAttributeName)
            {
                if (attribute.ConstructorArguments.Length == 0)
                {
                    return (typeName, DependencyKinds.KeyedInherit, null, inCompilation);
                }

                var argument = attribute.ConstructorArguments[0];
                if (argument.IsNull)
                {
                    // [FromKeyedServices(null)] = 非 keyed 解決
                    return (typeName, DependencyKinds.Service, null, inCompilation);
                }

                return (typeName, DependencyKinds.KeyedExplicit, SymbolDisplay.FormatPrimitive(argument.Value!, quoteStrings: true, useHexadecimalNumbers: false), inCompilation);
            }
        }

        return (typeName, DependencyKinds.Service, null, inCompilation);
    }

    private static EquatableArray<string> CollectInterfaces(INamedTypeSymbol symbol)
    {
        var interfaces = symbol.AllInterfaces
            .Where(static x => x.SpecialType != SpecialType.System_IDisposable)
            .Select(static x => x.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .Where(static x => x != "global::System.IAsyncDisposable")
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
    // Parser : attribute components
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
    // Parser : Add* invocation collection
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

        // MEDI の登録拡張メソッドのみ対象 (keyed は生成ファクトリ側が keyed 登録の場合に非対応のため対象外)
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

        // closed generic は対象 (typeof(Foo<int>) は生成可能)。型パラメータを含む場合は対象外
        if (implementationSymbol.IsAbstract
            || (implementationSymbol.TypeKind != TypeKind.Class)
            || ContainsTypeParameter(implementationSymbol))
        {
            return null;
        }

        // 生成ファクトリ (new 直書き) が現在のアセンブリからアクセスできること
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
    // Parser : convention registration method
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
    // Parser : convention candidates
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
        ReportAnalysisDiagnostics(context, components, sortedCollected, conventionMatches);

        // ---- GeneratedComponents.g.cs (登録メソッド + 生成ファクトリ) ----

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
            EmitGeneratedComponents(context, assemblyName, components, unkeyedFactories, keyedFactories);
        }

        // ---- 規約登録メソッドの本体 ----

        foreach (var (method, matches) in conventionMatches)
        {
            EmitConventionMethod(context, method, matches);
        }
    }

    // ------------------------------------------------------------
    // Diagnostics (compile-time analysis)
    // ------------------------------------------------------------

    private static void ReportAnalysisDiagnostics(
        SourceProductionContext context,
        ComponentModel[] components,
        CollectedModel[] collected,
        List<(MethodModel Method, List<(CandidateModel Candidate, string Lifetime)> Matches)> conventionMatches)
    {
        // 登録マップ: サービス型 → (実装型, lifetime)。登録順で last-wins
        var serviceMap = new Dictionary<string, (string Impl, string Lifetime)>(StringComparer.Ordinal);
        var nodes = new Dictionary<string, (FactoryModel Factory, string Lifetime, LocationInfo? Location)>(StringComparer.Ordinal);

        foreach (var component in components)
        {
            if (component.KeyLiteral is not null)
            {
                continue;   // keyed は M4 の解析対象外
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
        foreach (var component in components)
        {
            if (component.Factory.AmbiguousConstructor)
            {
                context.ReportDiagnostic(new DiagnosticInfo(AmbiguousConstructor, component.Location, Display(component.Factory.ImplementationType)).ToDiagnostic());
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
                    context.ReportDiagnostic(new DiagnosticInfo(UnresolvedDependency, component.Location, Display(typeName), Display(component.Factory.ImplementationType)).ToDiagnostic());
                }
            }
        }

        // BTRS0003 (循環) — 生成対象ノード全体で DFS
        var state = new Dictionary<string, int>(StringComparer.Ordinal);   // 0=未訪問 1=探索中 2=完了
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
                        // 循環検出
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
    // Emit
    // ------------------------------------------------------------

    private static void EmitGeneratedComponents(SourceProductionContext context, string assemblyName, ComponentModel[] components, List<FactoryModel> unkeyedFactories, List<FactoryModel> keyedFactories)
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
            EmitFactoryRegistration(builder, factory, keyed: false);
        }

        foreach (var factory in keyedFactories)
        {
            if (!first)
            {
                builder.NewLine();
            }

            first = false;
            EmitFactoryRegistration(builder, factory, keyed: true);
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

    private static void EmitDependencyResolution(SourceBuilder builder, string typeName, int kind, string? keyLiteral)
    {
        switch (kind)
        {
            case DependencyKinds.ServiceKey:
                builder.Append('(').Append(typeName).Append(")key!");
                break;
            case DependencyKinds.KeyedExplicit:
                builder.Append("provider.GetRequiredKeyedService<").Append(typeName).Append(">(").Append(keyLiteral!).Append(')');
                break;
            case DependencyKinds.KeyedInherit:
                builder.Append("provider.GetRequiredKeyedService<").Append(typeName).Append(">(key)");
                break;
            default:
                builder.Append("provider.GetRequiredService<").Append(typeName).Append(">()");
                break;
        }
    }

    private static void EmitFactoryRegistration(SourceBuilder builder, FactoryModel factory, bool keyed)
    {
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

        var lambdaHeader = keyed ? "static (provider, key) => " : "static provider => ";

        if ((factory.Parameters.Count == 0) && (factory.InjectProperties.Count == 0))
        {
            builder.Indent().Append(lambdaHeader).Append("new ").Append(factory.ImplementationType).Append("());").NewLine();
        }
        else
        {
            builder.Indent().Append(lambdaHeader.TrimEnd()).NewLine();
            builder.BeginScope();

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
                    EmitDependencyResolution(builder, parameter.TypeName, parameter.Kind, parameter.KeyLiteral);
                    builder.Append(i < factory.Parameters.Count - 1 ? "," : ");").NewLine();
                }

                builder.IndentLevel--;
            }

            foreach (var property in factory.InjectProperties)
            {
                builder.Indent().Append("instance.").Append(property.Name).Append(" = ");
                EmitDependencyResolution(builder, property.TypeName, property.Kind, property.KeyLiteral);
                builder.Append(';').NewLine();
            }

            builder.AppendLine("return instance;");

            // lambda ブロックを閉じる (BeginScope の +1 を戻して "});" を出力)
            builder.IndentLevel--;
            builder.Indent().Append("});").NewLine();
        }

        builder.IndentLevel--;
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
            builder.Indent().Append("services.Add").Append(component.Lifetime).Append('<').Append(interfaceType).Append(">(static provider => provider.GetRequiredService<").Append(implementationType).Append(">());").NewLine();
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
                    builder.Indent().Append("services.Add").Append(lifetime).Append('<').Append(interfaceType).Append(">(static provider => provider.GetRequiredService<").Append(implementationType).Append(">());").NewLine();
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
