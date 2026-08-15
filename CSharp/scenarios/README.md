# Scenario Lab — v0.2.24.0

Scenario JSONは盤面・手番・探索条件・Strategyだけを指定します。**対局ルールは製品版Standardで固定**され、ルール切替項目はありません。

同梱:
- `standard_smoke_20.json`: D5、P1開始10局 + P2開始10局。
- `standard_d7_1000.json`: D7、P1開始500局 + P2開始500局。

共通の主な条件は `openingRandomPlies=4`, `openingTopK=3`, `openingScoreWindow=120`, `cycleBreakScoreWindow=10`, `searchParallelism=1`。

盤面文字は `S/O`=P1の★/○、`s/o`=P2の★/○、`.`=空き。
