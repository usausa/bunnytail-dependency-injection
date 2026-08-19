namespace BunnyTail.DependencyInjection.Generator.Models;

using SourceGenerateHelper;

internal sealed record ClosedGenericWarningModel(
    string DisplayName,
    LocationInfo? Location);
