using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("BunnyTail.Resolver.Tests")]

#pragma warning disable IDE0130
#pragma warning disable CA1812
#pragma warning disable IDE0161
namespace System.Runtime.CompilerServices
{
    // For compatibility (netstandard2.0 で record を使うためのポリフィル)
    internal sealed class IsExternalInit
    {
    }
}
#pragma warning restore IDE0161
#pragma warning restore IDE0130
