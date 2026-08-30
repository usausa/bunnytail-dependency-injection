namespace BunnyTail.DependencyInjection.Generator.Models;

using SourceGenerateHelper;

internal sealed record FactoryModel(
    string ImplementationType,
    bool EligibleUnkeyed,
    bool EligibleKeyed,
    bool AmbiguousConstructor,
    bool Disposable,
    string? PostConstruct,
    bool InitializableInterface,
    bool InvalidPostConstruct,
    bool ConflictingPostConstruct,
    EquatableArray<ParameterModel> Parameters,
    EquatableArray<PropertyModel> InjectProperties)
{
    public bool HasInitializer => (PostConstruct is not null) || InitializableInterface;
}
