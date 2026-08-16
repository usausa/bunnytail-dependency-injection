using System.Runtime.CompilerServices;

// 一時的: 解決経路の層ごとの分解測定 (P-7) のため Sandbox から internal を参照する。
// 調査が終わったら削除すること
// Temporary: lets the sandbox reach internals for the layer-by-layer decomposition of the resolution path (P-7).
// Remove once the investigation is finished.
[assembly: InternalsVisibleTo("BunnyTail.Resolver.Sandbox")]
