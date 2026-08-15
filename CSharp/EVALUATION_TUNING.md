# v0.2.37.8 — latest accepted profile

実効 `BlockerMaterial` 導入・高速化後の1パラメータスキャンから、`Scan-O.BlockerMaterial-1000` を組込み既定へ採用。active vocabularyは15特徴/30重みのまま。

| feature | opening | endgame |
|---|---:|---:|
| RunnerProgress | 1520‰ | 780‰ |
| RunnerMobility | 490‰ | 1110‰ |
| BlockerMaterial | 1000‰ | 400‰ |
| FriendlyRunnerSupport | 1120‰ | 930‰ |
| FrontPressure | 1830‰ | 1550‰ |
| GoalDefense | 1240‰ | 770‰ |
| ImmediateGoalThreats | 1390‰ | 2320‰ |
| BlockerAdvancement | 1300‰ | 870‰ |
| BridgeheadConnection | 2480‰ | 1710‰ |
| RunnerGoalPath | 1500‰ | 1510‰ |
| PreparedGoalThreat | 750‰ | 1310‰ |
| UnansweredGoalThreat | 470‰ | 1120‰ |
| ConnectedGoalThreat | 600‰ | 1250‰ |
| ViableRunnerProgress | 1220‰ | 780‰ |
| SacrificeDebt | 1800‰ | 2600‰ |

局面76ユーザー実測: static=-313 / `○実効戦力=-39` / D9=-102 B4-C3。旧 `Tuned-G0004` は履歴再現用。

---

# v0.2.37.7 — Effective BlockerMaterial scan-speed fix

v0.2.37.6のraw定義・BOUND/FREE判定・1250/560‰は変更しない。leaf評価で各○ごとに行っていた最大7層BFSを、target側からのreverse bitboard waveへ置換し全○を並列判定する。独立10,000ランダム盤面で旧/新判定一致。局面76は `BlockerMaterial=-50 / static=-327` を回帰条件とする。

この版でbuild後、局面76の回帰を確認してからBlockerMaterial単独スキャンを再開する。

---

# v0.2.37.6 — BlockerMaterial = effective attacking material

`BlockerMaterial` のfeature次元と重みはそのまま、raw signalだけを「物理○枚数差」から「実効○戦力差」へ変更した。相手★の最短corridorへ迎撃可能で、かつ自分★の攻撃用犠牲stagingへ移るETAが防御開始までのslackを超える○を防御拘束と判定する。相手GoalDistance<=4では拘束○を1.0個、D=5では0.5個割引、D>=6では割引しない。

局面76の独立モデルでは P1=4物理→1実効、P2=5物理→4実効。旧raw -17 → 新raw -50。現在の `Tuned-G0004` 1250/560‰は旧raw定義で学習した値なので、動作確認後にBlockerMaterial単独スキャンを推奨する。active vocabularyは引き続き15特徴/30重み。

---

# v0.2.37.5 — GoalBridgeheads / RunnerCentrality retired

評価語彙から `GoalBridgeheads` と `RunnerCentrality` を完全に外した。重みを0にしただけではなく、raw signal計算、`CpuEvaluationFeatureScales`、`EvaluationBreakdown`、チューナー/1パラメータスキャン、Replay表示から削除している。

現在は **15特徴 × Opening/Endgame = 30重み**。組込み `Tuned-G0004` は旧G0004から廃止2項目だけを取り除いた値をそのまま使用し、残り重みは再チューニングしていない。

| feature | opening | endgame |
|---|---:|---:|
| RunnerProgress | 1520‰ | 780‰ |
| RunnerMobility | 490‰ | 1390‰ |
| BlockerMaterial | 1250‰ | 560‰ |
| FriendlyRunnerSupport | 1120‰ | 930‰ |
| FrontPressure | 1830‰ | 1550‰ |
| GoalDefense | 1240‰ | 770‰ |
| ImmediateGoalThreats | 1390‰ | 2320‰ |
| BlockerAdvancement | 1300‰ | 870‰ |
| BridgeheadConnection | 2210‰ | 1710‰ |
| RunnerGoalPath | 1500‰ | 1510‰ |
| PreparedGoalThreat | 750‰ | 1310‰ |
| UnansweredGoalThreat | 470‰ | 960‰ |
| ConnectedGoalThreat | 600‰ | 1250‰ |
| ViableRunnerProgress | 1220‰ | 780‰ |
| SacrificeDebt | 1800‰ | 2600‰ |

旧 `evaluation_profile_v2.json` に `GoalBridgeheads` / `RunnerCentrality` が残っていても `System.Text.Json` の未知プロパティとして無視される。次回保存時には新しい15特徴だけが出力される。

---

> **v0.2.36.2 note:** evaluation raw logic was revised for FrontPressure, GoalDefense, BridgeheadConnection and root oscillation preference. See `NEXT_CHAT_HANDOFF.md` and `STATIC_AUDIT_v0.2.36.2.md`. Public game rules/API shape are otherwise unchanged.

# v0.2.36.1 — Tuned-G0019 adopted as provisional best

16特徴×Opening/Endgame=32重みの共同チューニング結果 `Tuned-G0019` を現時点の組込み最善として採用した。4相互作用特徴は0‰ではなく、通常CPU評価へ実際に寄与する。

| feature | opening | endgame |
|---|---:|---:|
| RunnerProgress | 1400‰ | 780‰ |
| RunnerMobility | 550‰ | 1390‰ |
| BlockerMaterial | 1110‰ | 560‰ |
| FriendlyRunnerSupport | 1040‰ | 930‰ |
| FrontPressure | 1830‰ | 1550‰ |
| GoalBridgeheads | 1290‰ | 560‰ |
| GoalDefense | 1240‰ | 770‰ |
| RunnerCentrality | 580‰ | 680‰ |
| ImmediateGoalThreats | 1390‰ | 2320‰ |
| BlockerAdvancement | 1300‰ | 870‰ |
| BridgeheadConnection | 2210‰ | 1710‰ |
| RunnerGoalPath | 1500‰ | 1600‰ |
| PreparedGoalThreat | 900‰ | 1310‰ |
| UnansweredGoalThreat | 600‰ | 1080‰ |
| ConnectedGoalThreat | 600‰ | 1100‰ |
| ViableRunnerProgress | 1220‰ | 780‰ |

`Scan-O.RunnerGoalPath-1500` は相互作用4特徴=0‰の旧基準として保持する。1パラメータ・スキャン、Successive Halving、深いvalidation/confirmation、0～3000‰ clampは変更していない。

---

# v0.2.36.0 — Interaction Features / 32-weight tuner

現在の評価モデルは **既存12特徴 + 相互作用4特徴 = 16特徴**、Opening/Endgame合わせて **32重み**。
既存24重みは `Scan-O.RunnerGoalPath-1500` の値を維持し、新4特徴8重みはすべて0‰から開始する。

新特徴:

- `PreparedGoalThreat` = GoalPathUrgency × SacrificeChainReadiness
- `UnansweredGoalThreat` = GoalPathUrgency × (1 - opponent GoalDefenseStrength)
- `ConnectedGoalThreat` = GoalPathUrgency × BridgeheadConnectionSignal
- `ViableRunnerProgress` = RunnerProgressSignal × GoalPathUrgency

各項はP1/P2を別々に0..100で計算してから差を取る。既存の符号付き評価値同士を掛けない。
組込み既定では4特徴が0‰なので相互作用計算をスキップし、既存CPUの評価・速度を維持する。
通常チューナーは32次元を変異し、1パラメータ・スキャンは他31重みを固定する。

---

# v0.2.33.0 product default / RunnerGoalPath / Successive Halving note

`FlatNormalizedV1` は今後も「全24重み=1000‰」の中立な研究基準として保持するが、製品CPUの組込み既定はv0.2.30.0で深い高速探索のセルフチューニング結果 `Tuned-G0015` へ更新した（旧既定 `Tuned-G0042` は明示プリセットとして保持）。`evaluation_profile_v2.json` のoverride機構はStarRunnerPrototype開発アプリ側で従来通り維持する。StarRunner.AI単体はファイルを自動探索しない。

# 評価関数チューナー v0.2.26.0

## 目的

評価関数の重要度を人間が先に決めすぎず、低深度セルフプレイを長時間回して改善するための開発用チューナー。
通常CPUへは「最良結果を採用」を押すまで反映しない。

## 新しい評価モデル: FlatNormalizedV1

v0.2.26.0では評価関数の素点を全面的に正規化した。
12特徴はいずれも、理論上または構造上の最大差を基準におおむね `-100 .. +100` へ変換してから重みを掛ける。

- RunnerProgress: ★の進行差。旧 `0,48,102,...590` の加速曲線は廃止し、0..7の線形差を±100へ正規化。
- RunnerMobility: ★の通常移動+犠牲移動の合法選択肢差を±100へ正規化。
- BlockerMaterial: **v0.2.37.6以降は実効○戦力差**を±100へ正規化（この箇所の旧v0.2.26.0説明では当時は単純○枚数差）。
- FriendlyRunnerSupport: ★隣接味方○数差を±100へ正規化。
- FrontPressure: ★真正面を敵○に塞がれているかの差を±100へ正規化。
- GoalBridgeheads: 敵ゴール列の味方○数差を±100へ正規化。
- GoalDefense: 自ゴール列の自駒数差を±100へ正規化。
- RunnerCentrality: ★の中央寄り度差を±100へ正規化。
- ImmediateGoalThreats: 1手ゴール可能数差を±100へ正規化。
- BlockerAdvancement: 全○の前進量合計差を±100へ正規化。
- BridgeheadConnection: 敵ゴール列○と★の接続度差を±100へ正規化。
- RunnerGoalPath: 相手が動かない静的仮定で、★から敵ゴール列までの最短経路を評価。空きマスは上下左右、自分の○は8方向から犠牲移動で進め、敵駒は壁。8手以内の経路を1手=8～8手=1の経路価値へ変換し、その差を±100へ正規化。

中立チューニング基準 `FlatNormalizedV1` は Opening / Endgame の24パラメータを**すべて1000‰**にする。通常CPUの組込み既定はv0.2.33.0現在 `Tuned-G0015`（RunnerGoalPathは未チューニング初期値1000‰）（v0.2.27.0～v0.2.28.0は `Tuned-G0042`）。
したがって開始時はフェーズによる評価重み差もなく、序盤・終盤差はセルフプレイが発見する。

旧評価モデルとは素点の意味が変わるため、overrideファイル名も `evaluation_profile_v2.json` に変更した。旧 `evaluation_profile.json` は自動読込しない。

## 変異戦略

1候補で多数のパラメータを同時変更する旧方式を廃止。
24個（12特徴×Opening/Endgame）から重複なしで選ぶ。

通常候補は次の周期。

- C1: 1項目
- C2: 1項目
- C3: 2項目
- C4: 2項目
- C5: 3項目
- C6: 5～8項目の大変異
- C7以降は同じ周期を繰り返す

項目選択は全24項目を同率で抽選する。特定特徴を「重要」とみなす人手バイアスは入れない。
重みは0～3000‰。0まで下げられるので、チューナーが不要と判断した特徴は無効化できる。負値は、全特徴を「正なら自分に有利」と定義しているため許可しない。


## v0.2.33.0 浅い候補戦のSuccessive Halving

浅い候補戦は「全候補へ同じ局数を最後まで配る」方式を廃止し、**有望候補へ後半の対局資源を集中するSuccessive Halving**へ変更した。純粋なスイス式の候補同士対戦ではなく、全候補が現Championと対戦する基準は維持する。

- `ShallowGamesPerCandidate` は「全候補の固定局数」ではなく、**勝ち残った最終候補が到達する最大累積局数**。
- 1ラウンドの追加対局は必ず candidate=P1 / candidate=P2 の2局1ペア。浅い最大局数は偶数へ正規化する。
- ラウンド数は `ceil(log2(候補数))` を基本とし、使える先後ペア数より多くならないよう制限する。
- 各ラウンドの累積局数目標を最大局数までほぼ等間隔に配分し、ラウンドごとに上位約半数だけを残す。最終ラウンドだけは必ず1候補へ絞る。
- 既定 `6候補 / 最大24局` では `6×8局 → 上位3`, `3×追加8局 → 上位2`, `2×追加8局 → 上位1`。浅い対局数は従来144局から88局へ減る一方、最終候補のshallow scoreは従来同様24局分。
- 順位は `累積Score` → `直近ラウンドScore` → `決着局だけの勝率` → `candidate番号` の順で決める。同点処理を完全に決定論化する。
- 同じ候補ではラウンドを跨いでゲームindexを継続するため、同じseedの同一局を重複実行しない。
- 最終勝者が最大局数まで到達した時のP1/P2 game index集合は旧「最初から最大局数を完走」方式と同じ。したがって同じ候補/seedなら、最終24局などのseed集合自体は変更しない。
- 深い検証戦・昇格決定戦・Champion採用条件はv0.2.32.0から変更しない。
- report schema 6では各generationに `ShallowRounds` を保存し、各ラウンドのactive数、累積局数目標、候補ごとのround/cumulative score、進出可否を追跡できる。

## v0.2.32.0 深い対局の数学的早期棄却

浅い候補戦は世代内ランキングに使用するため予定局数を最後まで実行する。
一方、世代最良候補の深い検証戦と昇格決定戦では、残り全局を勝っても必要条件へ届かないことが確定した時点で停止する。

- 検証戦: `validation >= 49%` かつ `0.65*shallow + 0.35*validation >= 51.5%` が必要。現在得点に残り全勝を加えた `maxValidation` でもどちらかを満たせないならEARLY reject。
- 昇格決定戦: `confirmation >= 50%` かつ `0.50*shallow + 0.25*validation + 0.25*confirmation >= 52%` が必要。残り全勝時の `maxConfirmation` でもどちらかを満たせないならEARLY reject。
- 候補P1 / 候補P2を1局ずつのペアとして並列実行し、ペア完了時だけ判定する。途中停止が先後偏りを作らない。
- 上限は勝ち=1、引分/手数上限=0.5、負け=0として計算する。未実施局はすべて勝ちと仮定するため、棄却は必ず安全側。
- report schema 5では各matchに `PlannedGames`, `StoppedEarly`, `StopReason` を保存する。

## 長時間運転

`終了世代=0` で停止ボタンを押すまで継続。

各世代:

1. D5等の低深度でSuccessive Halving候補選抜
2. 世代最良をD7等で検証
3. 有望候補のみ別seedで昇格決定戦
4. 全条件を通過した候補だけChampion更新
5. Champion更新時にcheckpoint保存

探索幅は停滞数に応じて粗→細を循環する。通常の1～3項目局所変異と、6候補ごとの大変異を併用する。

## UI

上段に「開始時パラメータ」と「現在の最善パラメータ」を並べて表示する。
開始時は固定、右側は昇格確定Championのみ更新する。

ダッシュボード表示:

- 経過時間
- 現在世代
- 総対局数
- ベスト更新回数
- 最終更新世代
- 現在変異幅
- 停滞世代数
- Champion名

進捗行には候補ごとに `少数変異/大変異` と変更項目数も表示する。
停止後の結果ログ/JSONには、各世代の世代最良候補が変更した具体的パラメータ名と旧値→新値も保存する。

## 注意

これは確率的最適化であり、無限時間で数学的な大域最適を保証するものではない。
ただし長時間放置時に、未検証候補をChampionへ混ぜず、局所改善と大きめの脱出探索を継続する設計としている。

## チューニング対局だけのroot preference

通常CPUの千日手回避/root preferenceは変更しない。ただしチューナー内の対局では `CycleBreakScoreWindow=0` とし、非ゼロの評価差を「★前進 preference」で上書きしない。評価関数そのものの優劣を測るための隔離措置。

## v0.2.34.2: 1パラメータ・スキャン

通常チューナーとは別に、他23重みを固定して指定1重みだけを範囲走査できる。
各値は開始時Profileと先後均等で直接比較し、全値で同一seed集合を使う。
UI既定は RunnerGoalPath / Endgame / 600..1400‰ / step 100 / D5 / 24局/値。
スキャン結果は `evaluation_parameter_scan_*.json` に保存され、結果を確認してから手動採用できる。


## v0.2.34.3: スキャン結果を組込み既定へ採用

ユーザー判断により `Scan-O.RunnerGoalPath-1500` を現時点の最善Profileとして組込み既定に採用。スキャン機能そのもののアルゴリズムは変更していない。
