namespace BunnyTail.DependencyInjection.Generator.Models;

// CollectedModel.Kind の値。Direct はインライン/enumerable の前提に参加し、FactoryOnly (TryAddEnumerable 由来)
// はファクトリ生成のみ + 前提の毒化、Keyed は keyed ファクトリ生成のみ。ActivationOnly (Activate 呼び出し由来)
// はファクトリ生成のみで、登録ではないため前提にも解析にも一切参加しない
// Values of CollectedModel.Kind. Direct participates in inline and enumerable assumptions; FactoryOnly
// (from TryAddEnumerable) only generates factories and poisons assumptions; Keyed only generates keyed factories.
// ActivationOnly (from Activate invocations) only generates factories and, not being a registration, never joins
// assumptions or analysis.
internal static class CollectedKinds
{
    public const int Direct = 0;
    public const int FactoryOnly = 1;
    public const int Keyed = 2;
    public const int ActivationOnly = 3;
}
