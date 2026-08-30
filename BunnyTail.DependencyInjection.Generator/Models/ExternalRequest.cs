namespace BunnyTail.DependencyInjection.Generator.Models;

internal sealed record ExternalRequest(
    string Assembly,
    string Pattern,
    string? Namespace);
