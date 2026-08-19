namespace BunnyTail.DependencyInjection.Generator.Models;

// CollectedModel.Kind の値。Direct はインライン/enumerable の前提に参加し、FactoryOnly (TryAddEnumerable 由来)
// はファクトリ生成のみ + 前提の毒化、Keyed は keyed ファクトリ生成のみ
// Values of CollectedModel.Kind. Direct participates in inline and enumerable assumptions; FactoryOnly
// (from TryAddEnumerable) only generates factories and poisons assumptions; Keyed only generates keyed factories.
internal static class CollectedKinds
{
    public const int Direct = 0;
    public const int FactoryOnly = 1;
    public const int Keyed = 2;
}
