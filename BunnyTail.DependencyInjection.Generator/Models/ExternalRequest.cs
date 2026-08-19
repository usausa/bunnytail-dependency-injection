namespace BunnyTail.DependencyInjection.Generator.Models;

// Assembly 指定つき規約パターンの外部走査要求 (値等価: 変化した時だけ下流が再実行される)
// External scan request for assembly-scoped convention patterns (value-equatable so downstream reruns only on change).
internal sealed record ExternalRequest(
    string Assembly,
    string Pattern,
    string? Namespace);
