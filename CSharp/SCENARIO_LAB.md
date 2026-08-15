# Scenario Lab — v0.2.26.0

Headless CPU対戦の開始盤面・手番・探索条件・StrategyをJSONで再現する機能。**ゲームルールは製品版Standard固定**。Scenarioにルール切替フィールドはない。

## 主要項目
- `name`
- `board`: 8文字×8行。`.` 空、`S/O` P1 ★/○、`s/o` P2 ★/○。
- `currentPlayer`: `P1` / `P2`。
- `settings`: games, p1Depth, p2Depth, time/node limit, parallelism, searchParallelism, maxPlies, opening random設定, cycleBreakScoreWindow, seed, saveMoveSequences。
- `p1Strategy` / `p2Strategy`: 通常は `Free`。研究用 `RushOne` も利用可能。

同梱例は `scenarios/standard_smoke_20.json` と `scenarios/standard_d7_1000.json`。

Scenarioを読み込むと明示された共通設定をGUIへ反映し、複数caseを順次/並列実行する。ログにはscenarioName/source/hash/startPlayer/startBoard/Strategyを記録し、条件を追跡可能にする。
