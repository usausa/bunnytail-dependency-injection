# BunnyTail.Resolver.Sandbox

DI 固有の設計判断を検証するためのベンチマーク。ライブラリ本体には残っていない「採用しなかった候補」の実装を持ち、採用形状との比較を再現できる状態で保つ。

汎用的な性能パターン(型キー lookup、dispatch 形状、遅延生成、`Unsafe.As` など)は **[dotnet-performance](https://github.com/usausa/dotnet-performance) のカタログを参照する**。ここには置かない。

## 収録項目

| ベンチマーク | 検証内容 | なぜ DI 固有か |
|---|---|---|
| `KeyedLookupBenchmark` | keyed services の `(Type, key)` lookup 構造 | MEDI の keyed services は「同一サービス型 × 複数キー」という形状を持ち、単一 `Type` キーのテーブルでは答えが出ない |
| `DisposalTrackingBenchmark` | transient の disposal 追跡コスト | 「transient も追跡して破棄する」は MEDI 互換のための制約。追跡要否を生成時に型で確定する効果を測る |

## dotnet-performance を参照する項目

以下はここで検証したが、結論が dotnet-performance のカタログと一致したため実装ごと削除した。再検討が必要になったらカタログ側を見る。

| 元の検証内容 | 参照先 | 結論 |
|---|---|---|
| `Type → Entry` の主テーブル形状(identity hash / ノードリスト / Robin Hood / `FrozenDictionary`) | `CandidateVerification.Benchmarks` の `TypeIdentityHashBenchmark` / `NodeTypeHashMap` / `RobinHoodTypeTable`、TYP-01、R-08 | identity hash + 参照比較 + 2^n マスクのノードリストが最速。`FrozenDictionary` は `Type` キーで `Dictionary` に負ける |
| ジェネリック公開 API の解決経路(`typeof(T)` 分岐 / `TypeSlot<T>`) | TYP-01、JIT-03 | 型引数が静的に判る経路では分岐チェーンが約 0.23ns。実行時 `Type` を経由する二段 lookup は `Dictionary` より遅い |
| ファクトリの dispatch 形状(closed delegate / sealed 仮想 / interface / `delegate*`) | DSP-02、`GuardedDevirtBenchmark` | `delegate*` は `calli` がインライン・投機不可のため不採用。closed instance delegate を採用 |
| Singleton / Scoped の保持形状(型付きフィールド / `object[]` スロット / lazy) | STK-07、TYP-05、MEM-02 | 型付きフィールドを採用。スロットを使う箇所は `castclass` ではなく `Unsafe.As` |

## 実行

```bash
dotnet run -c Release -- --verify                    # 等価性検証のみ
dotnet run -c Release -- --filter "*Keyed*"          # keyed テーブル
dotnet run -c Release -- --filter "*"                # 全件
```

測定の作法(判定は速度・アロケーション・コードサイズの 3 軸、CI が重ならない場合のみ有意、測定前に等価性検証)は dotnet-performance の `docs/benchmark-methodology.md` に従う。

## 維持上の注意

`NodeCompositeTable` は本体の `FixedKeyedServiceTable` と同じレイアウトを写したもの。**本体側のレイアウトを変えたらここも合わせる**(でないと比較の意味がなくなる)。
