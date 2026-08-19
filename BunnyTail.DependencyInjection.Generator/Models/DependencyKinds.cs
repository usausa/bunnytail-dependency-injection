namespace BunnyTail.DependencyInjection.Generator.Models;

// 依存の解決方法
// How a dependency is resolved.
internal static class DependencyKinds
{
    public const int Service = 0;        // 非 keyed サービス解決 / non-keyed service resolution
    public const int ServiceKey = 1;     // [ServiceKey] : 解決中のキーを注入 / injects the key being resolved
    public const int KeyedExplicit = 2;  // [FromKeyedServices(key)] : 明示キー / explicit key
    public const int KeyedInherit = 3;   // [FromKeyedServices] : キー継承 / inherits the key
}
