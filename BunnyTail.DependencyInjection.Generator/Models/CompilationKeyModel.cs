namespace BunnyTail.DependencyInjection.Generator.Models;

using Microsoft.CodeAnalysis;

internal sealed record CompilationKeyModel(
    string? AssemblyName,
    CompilationOptions Options);
