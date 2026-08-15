# Log format — v0.2.36.0

## 方針
現行製品の対局ルールは固定。**現行ログにルール組合せやルールマスクを記録しない。** `rulesId` は `Standard`。

## 通常対局 JSONL
`logs/game_*.jsonl`。主なeventType:
- `game_started`: appVersion、mode、AI設定、`rulesId=Standard`、ルール説明、初期状態。AI設定にはP1/P2の級・段名、固定MaxNodes、共通Depth上限、探索スレッド数、effective evaluation profile名/sourceを含む。
- `turn_start`: ply、手番、合法手数、現在盤面の出現回数。
- `human_move_selected` / `cpu_decision`。`cpu_decision` はその手で使ったskill名とMaxNodesも記録する。v0.2.30.0からrootPreferenceに `runnerOscillationAvoidanceApplied` / `runnerReturnCandidatePresent` / `selectedRunnerReturnMove` を記録する。 v0.2.36.7から `searchSelectivity` にPVS probe/re-search、LMR reduced/verify、mate-distance prune、only-survival extensionの各カウンタを記録する。v0.2.36.8ではadaptive root deepeningのpass数と最大追加plyも同telemetryに含む。
- `move_applied`。
- 通常対局envelope schemaは3のまま。v0.2.36.0では `cpu_decision.staticEvaluationAfterChosenMove` に4相互作用寄与も追加。
- `game_ended`。

`game_started.rules.repetition` は「盤面配置だけ4回」。`immediateBacktrack` は「直前に動かした自駒の即戻りは原則不可、他手なしなら可」。

## Headless JSONL
`analysis_logs/batch_*.jsonl`。`batch_started.payload.schemaVersion = 12`。
- `batch_started`: `rulesId=Standard`, `options`、P1/P2のeffective evaluation profile名。schema 10ではScenario Lab制約を`options.movePolicy`に分離。schema 11ではmoves短縮表現に★往復回避フラグ `ro` を追加。schema 12では評価profileが12特徴（RunnerGoalPath追加）へ拡張。v0.2.36.0ではprofile JSON自体が16特徴へ拡張（新4特徴は旧profile読込時0‰移行）。
- `game_result`: 勝敗、終了理由、手数、探索統計、Strategy統計、cycleBreak統計、必要ならmoves。
- `batch_summary`: 集約値。

終了理由は `GoalReached`, `RunnerImmobilized`, `MovePolicyNoMove`, `FourfoldRepetition`。`StrategyConstraintNoMove` は旧名称の互換alias。Headlessの独自打切りはOutcome=`MoveLimit`。

## Headless CSV
`analysis_logs/headless_latest.csv`。v0.2.18.1では `rulesId` のみを持ち、実験ルール列は持たない。v0.2.25.0で `p1EvaluationProfile` / `p2EvaluationProfile` 列を追加。既存CSVのヘッダが現行と違う場合は削除せず `headless_latest_legacy_*.csv` へ退避し、新しいCSVを開始する。

## 評価チューナー JSON

`analysis_logs/evaluation_tuning_*.json`。v0.2.36.0のreport schemaは7（16特徴/32重み）。

- `shallowSelection = SuccessiveHalvingBalancedPairs`。
- 各generationの `ShallowRounds` に、round番号、active候補数、進出候補数、累積局数目標、各候補のround score / cumulative score / Advancedを保存する。
- 深いmatchの `PlannedGames`, `StoppedEarly`, `StopReason` はv0.2.32.0から継続。

## 棋譜保存
「名前をつけて保存」は通常対局JSONLのスナップショットコピー。棋譜並べとメイン画面の「開く」で読める。

## 旧棋譜互換
`ReplayLoader` は過去の標準相当棋譜を読むためだけ、旧ログに存在した数値フィールドを判定する互換コードを持つ。現在の製品エンジンで再現できない廃止済み実験ルールの棋譜は明示エラーにする。v0.2.18.1以降はそのフィールドを生成しない。


## 1パラメータ・スキャン

`analysis_logs/evaluation_parameter_scan_*.json`。v0.2.36.0はschema 2。対象feature/phase、baseline値、全走査値のW-L-D/score、bestEntry、共通seed設定を保存する。
