v0.2.37.4: `SacrificeDebt`を追加。現行評価は17特徴×2フェーズ=34重み。既存16-field positional scale APIは維持。

v0.2.36.8: root only-survivalはadaptive deepening（既定最大+6ply）。内部ノードのonly-survival budgetは既定2plyを維持。

# StarRunner.AI

StarRunner.Core向けのUI非依存CPUライブラリ。

- Target: `net10.0`
- Assembly / namespace: `StarRunner.AI`
- Depends on: `StarRunner.Core`
- WinForms dependency: なし
- File I/O: なし
- Built-in evaluation default: `Scan-O.BlockerMaterial-1000`（15特徴×2フェーズ=30重み）
- Reproducible presets: `Tuned-G0019` / `Scan-O.RunnerGoalPath-1500` / ほか履歴preset
- CPU skill table: 15級=200 Nodes、`NodeMultiplier=1.80`、五段=14,164,707 Nodes

v0.2.37.4の探索既定:

- iterative deepening alpha-beta
- PVS
- mate-distance pruning
- StarRunner専用verification付きLMR
- same-iteration対応only-survival extension（1 line最大+2ply）
- root adaptive only-survival deepening（最大+6ply）
- budget-aware Mate-distance Scout（D1からpure proof、通常MaxDepth+8plyまで）
- transposition table
- killer/history ordering
- static evaluation cache
- parallel root split

主な公開API:

- `CpuPlayer.DecideMove` / `DecideMoveAsync`
- `CpuSearchOptions`
- `CpuSkillProfiles`（15級～五段）
- `CpuEvaluationProfile`
- `CpuEvaluationProfileProvider`

AIライブラリは評価overrideファイルを自動探索しない。必要ならホストが明示的にprofileを設定する。
詳細な公開APIリファレンス: `../EMBEDDING_REFERENCE.md`
