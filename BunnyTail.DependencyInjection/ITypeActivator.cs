namespace BunnyTail.DependencyInjection;

using System.Diagnostics.CodeAnalysis;

public interface ITypeActivator
{
    object Activate(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type);

    T Activate<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class;
}
