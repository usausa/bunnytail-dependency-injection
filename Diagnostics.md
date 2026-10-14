# Diagnostics

## Declaration

| ID | Severity | Description | How to fix |
|---|---|---|---|
| BTDI0001 | ❌ Error | `[ComponentRegistration]` method is not a static partial extension method with the required signature | Declare the method as `static partial`, take `IServiceCollection` as the first parameter, and return `IServiceCollection` |
| BTDI0002 | ❌ Error | Component cannot be referred to from the generated code (`private` or `protected` nested, `file`-local, nested in a generic type, or marked `[Obsolete]` as an error), so it is not registered | Make the class accessible from the assembly and not nested in a generic type, or remove the lifetime attribute |
| BTDI0003 | ❌ Error | `Lifetime` of `[ComponentRegistration]` or `Tracking` of `[Transient]` is a value the enum does not define (a cast number), so the registration is not generated | Use a defined value |
| BTDI0004 | ❌ Error | The name of a class with `[ComponentRegistration]` methods differs only in case from another in the same namespace, so its methods are not generated (generated file names are compared ignoring case) | Rename one of the classes |
| BTDI0005 | ⚠️ Warning | Registration pattern is not a valid regular expression | Fix the regular expression given as the registration pattern |
| BTDI0006 | ⚠️ Warning | Assembly named on `[ComponentRegistration]` is not referenced by the project | Add a reference to the assembly, or remove the `Assembly` specification |
| BTDI0007 | ⚠️ Warning | `As` and `WithInterfaces` are combined, so the interface delegate has no implementation registration to resolve | Specify either `As` or `WithInterfaces`, not both |
| BTDI0008 | ⚠️ Warning | `[GenerateComponentFactory]` target is not a publicly accessible concrete class with a usable public constructor | Make the target a public concrete class and give it a usable public constructor |

## Convention matching

| ID | Severity | Description | How to fix |
|---|---|---|---|
| BTDI0101 | ⚠️ Warning | A class matched by the pattern cannot be referred to from the generated code (`private` or `protected` nested, `file`-local, or nested in a generic type), so it is not registered | Make the class accessible from the assembly, or narrow the pattern |
| BTDI0102 | ⚠️ Warning | Registration pattern matched no type, so the method registers nothing | Review the `Pattern`, `Namespace` and `Assembly` specifications |

## Component factory

| ID | Severity | Description | How to fix |
|---|---|---|---|
| BTDI0201 | ❌ Error | `PostConstruct` method is not a public parameterless instance method returning void | Make the method public, parameterless, non-static, and returning `void` |
| BTDI0202 | ❌ Error | Conflicting `PostConstruct` specifications across lifetime attributes | Specify `PostConstruct` on only one lifetime attribute |
| BTDI0203 | ❌ Error | `[Inject]` property has no public setter (the runtime path throws for it too) | Give the property a public `set` or `init` accessor |
| BTDI0204 | ⚠️ Warning | Multiple public constructors share the same maximum parameter count, so the type gets no generated factory and the runtime path selects the constructor by the MEDI rules | Leave a single public constructor with the largest parameter count |

## Dependency graph

| ID | Severity | Description | How to fix |
|---|---|---|---|
| BTDI0301 | ❌ Error | Circular dependency between components | Break the cycle reported in `chain` |
| BTDI0302 | ⚠️ Warning | Dependency cannot be resolved from the registrations visible at compile time (factory and instance registrations count, and a parameter with a default value is not reported) | Register the dependency, or bring it into the range covered by the registration pattern |
| BTDI0303 | ⚠️ Warning | Captive dependency: a singleton depends on a scoped service | Do not take a scoped service as a dependency of a singleton component |

## Generation limit

| ID | Severity | Description | How to fix |
|---|---|---|---|
| BTDI0401 | ⚠️ Warning | Closed generic with value type arguments has no generated factory and resolves through the runtime path, which fails on NativeAOT | Register the closed generic explicitly so that a factory is generated |
