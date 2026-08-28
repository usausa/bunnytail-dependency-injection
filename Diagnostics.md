# Diagnostics

| ID | Severity | Description | How to fix |
|---|---|---|---|
| BTDI0001 | ❌ Error | `[ComponentRegistration]` method is not a static partial extension method with the required signature | Declare the method as `static partial`, take `IServiceCollection` as the first parameter, and return `IServiceCollection` |
| BTDI0002 | ⚠️ Warning | Registration pattern is not a valid regular expression | Fix the regular expression given as the registration pattern |
| BTDI0003 | ⚠️ Warning | Assembly named on `[ComponentRegistration]` is not referenced by the project | Add a reference to the assembly, or remove the `Assembly` specification |
| BTDI0004 | ⚠️ Warning | `[GenerateComponentFactory]` target is not a publicly accessible concrete class with a usable public constructor | Make the target a public concrete class and give it a usable public constructor |
| BTDI0005 | ❌ Error | Multiple public constructors share the same maximum parameter count | Leave a single public constructor with the largest parameter count |
| BTDI0006 | ❌ Error | `PostConstruct` method is not a public parameterless instance method returning void | Make the method public, parameterless, non-static, and returning `void` |
| BTDI0007 | ❌ Error | Conflicting `PostConstruct` specifications across lifetime attributes | Specify `PostConstruct` on only one lifetime attribute |
| BTDI0008 | ❌ Error | Circular dependency between components | Break the cycle reported in `chain` |
| BTDI0009 | ⚠️ Warning | Dependency cannot be resolved from the registrations visible at compile time | Register the dependency, or bring it into the range covered by the registration pattern |
| BTDI0010 | ⚠️ Warning | Captive dependency: a singleton depends on a scoped service | Do not take a scoped service as a dependency of a singleton component |
| BTDI0011 | ⚠️ Warning | Closed generic with value type arguments has no generated factory and resolves through the runtime path, which fails on NativeAOT | Register the closed generic explicitly so that a factory is generated |
| BTDI0012 | ⚠️ Warning | `As` and `WithInterfaces` are combined, so the interface delegate has no implementation registration to resolve | Specify either `As` or `WithInterfaces`, not both |
| BTDI0013 | ⚠️ Warning | Registration pattern matched no type, so the method registers nothing | Review the `Pattern`, `Namespace` and `Assembly` specifications |
