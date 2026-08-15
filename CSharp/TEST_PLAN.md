# v0.2.37.8 追加確認 — Scan-O.BlockerMaterial-1000 default

1. override無しで `CpuEvaluationProfileProvider.Current.Name == "Scan-O.BlockerMaterial-1000"`。
2. Opening/Endgame各15値が2026-08-15受理表と一致すること。
3. 「組込み既定へ戻す」で `Scan-O.BlockerMaterial-1000` に戻ること。
4. 局面76の静的評価は最新重みで **-313**（`○実効戦力=-39`）を目安に回帰確認すること。
5. 旧 `Tuned-G0004` プリセットは再現用として参照可能なままであること。

---

# v0.2.36.8 追加確認 — adaptive root deepening

1. Release buildが通り、assembly/file versionが0.2.36.8になること。
2. 棋譜74手目を最大D14 / Node 50,000,000で解析し、探索選択性に `adaptive-root ... / +...ply` が表示されること。
3. 74手目はv0.2.36.7同様、G7-F7以外がforced-lossになった後はdead root枝を再探索せず、G7-F7だけを追加deepeningすること。
4. 既に+2以内でMateを発見する局面では、それ以上無駄にdeepeningしないこと。
5. Mate未確定かつ唯一生存が継続するテスト局面では、+3～+6plyまでadaptive-rootが進めること。
6. Node上限を小さくした場合、adaptive deepening中に上限へ達しても直前のcompleted nominal-depth結果が返ること。
7. survivorがforced-lossへ落ちて他候補upper boundとの順位が不明になった場合、full-root safety verificationが走ること。
8. correctness verifierの既存PVS/mate-distance baseline一致、LMR smoke、TT/cache検証が引き続き通ること。

# v0.2.36.7 追加確認 — selective search

1. Release buildが通り、assembly/file versionが0.2.36.7になること。
2. CPU search correctness verifierがPASSすること。
   - baseline alpha-beta と `PVS + mate-distance` のD5 move/score一致。
   - shipping smokeで `PvsNullWindowProbes > 0`。
   - shipping smokeで `LmrReducedSearches > 0`。
3. 棋譜並べ→局面解析の最終結果に `探索選択性:` 行が出ること。
4. game JSONLの `cpu_decision.searchSelectivity` に6カウンタが出ること。
5. `analysis_samples/StarRunner_20260814_212236.jsonl` の局面74(P1手番)を 1 thread / 50,000,000 Nodes で解析。
   - v0.2.36.6: D14 = G7-F7 +622 / 5,394,457 nodes が比較基準。
   - 新版D14のNodesと経過時間を比較する。
   - PVS probe / LMR reduced が非0であること。
   - `G7-F7` はP2★F6の前方F7へ○を置くため、LMRで削減されない設計。
6. 局面74を最大D14で実行した時点でも、28手がforced-loss帯に入った後のsame-iteration only-survival extensionが発火することを確認する。`G7-F7`だけD15/D16相当へ深掘りされるため、`OnlySurvivalExtensions > 0` と詰み検出有無を記録する。
7. TT ON/OFF correctness verifierがmate score差で失敗しないこと（v0.2.36.7でmate scoreのTT ply正規化を追加）。
8. `Tuned-G0004` が組込み既定、`NodeMultiplier=1.80`、五段14,164,707 Nodesのままであること。
9. 評価関数チューナーが起動・対局できること。新探索は既定ONだが、評価Profileの32値やチューナーの固定Depth設定自体は変更していない。

---

# v0.2.36.6 追加確認 — NodeMultiplier 1.80

1. Release buildが通り、assembly versionが0.2.36.6になること。
2. `CpuSkillProfiles.NodeMultiplier == 1.80m` であること。
3. 15級が200 Nodes、五段が14,164,707 Nodesになること。
4. CPU search correctness verifierがPASSすること。
5. Tuned-G0004およびv0.2.36.5のstatic evaluation cache/hot-path最適化に変更がないこと。

---

# v0.2.36.5 追加確認 — static evaluation cache / hot-path optimization

1. Release buildが通り、assembly versionが0.2.36.5になること。
2. CPU search correctness verifierがPASSすること。特に static evaluation cache ON/OFF で move / score / nodes が一致すること。
3. 評価関数チューナーをv0.2.36.4と同一Depth/局数/Parallelismで実測し、局/秒または同一世代の経過時間を比較すること。
4. 通常CPUの五段が49,978,316 Nodesのままであること。

---

# v0.2.36.4 追加確認 — GoalDefense fast path + Tuned-G0004 default

1. Release buildが通り、タイトル/assembly versionが0.2.36.4になること。
2. override無しで `CpuEvaluationProfileProvider.Current.Name == "Tuned-G0004"`。
3. `CpuEvaluationProfile.BuiltInDefault` が `TunedG0004` を返すこと。
4. Opening/Endgame各16値がユーザー指定のTuned-G0004表と一致すること。
5. 「組込み既定へ戻す」で `Tuned-G0004` に戻ること。
6. CPU search correctness verifierがPASSし、v0.2.36.2のFrontPressure/GoalDefense/BridgeheadConnection固定局面も維持されること。
7. 五段がSearchDepthCap=99 / MaxNodes=49,978,316で動作すること。
8. 評価関数チューナーの1世代所要時間またはgames/secがv0.2.36.3より改善していること。
9. `Tuned-G0019` が履歴再現用プリセットとして残っていること。

---

# v0.2.36.0 追加確認 — Interaction Features

1. Release buildが通ること。
2. チューナーのfeature選択に16項目が表示され、新4項目が選べること。
3. 組込み既定 `Scan-O.RunnerGoalPath-1500` の既存24値がv0.2.35.0と一致し、新4特徴8値だけ0‰であること。
4. 新4特徴=0‰の通常CPUで、同一局面・同一探索条件の静的Totalがv0.2.35.0と一致すること。
5. `PreparedGoalThreat` を1パラメータ・スキャンし、0以外の候補で対局が実行されること。
6. スキャン採用後、棋譜/局面解析の静的評価内訳に新特徴の非0寄与が出ること。
7. 旧 `evaluation_profile_v2.json` を読み込んだ場合、新4特徴が0‰へ移行すること。

---

# v0.2.30.0 組込み基盤 + Tuned-G0015 追加確認

1. Solution Releaseビルドで `StarRunner.Core.dll` / `StarRunner.AI.dll` / `StarRunnerPrototype.exe` が生成される。
2. `StarRunner.Core`単体をビルドでき、WinForms/System.Drawing/AIへの参照がない。
3. `StarRunner.AI`単体をCore参照込みでビルドでき、WinFormsおよびFile/Directory/AppContextによるprofile自動探索がない。
4. override無し起動時 `CpuEvaluationProfileProvider.Current.Name == "Tuned-G0015"`。
5. 開発アプリでは既存 `evaluation_profile_v2.json` overrideが `CpuEvaluationProfileStorage` 経由で反映される。
6. `GameStartConfiguration.Initial.BoardRows` を外部から書換えできず、`CopyBoardRows()` を変更しても共有初期局面へ影響しない。
7. Bitboard correctness verifierがPASSし、`状態保存/復元` が1以上になる。
8. Exportした `GameState` をJSON serialize/deserializeして `GameEngine.FromState` した局面が、合法手・hash・即時戻り・千日手履歴まで一致する。
9. 復元後に同じ次手を適用して元エンジンと復元エンジンが一致する。
10. Scenario LabのFree/RushOne双方が従来通り起動し、RushOneがCore assembly内に存在しない。
11. CPU 15級～五段、CPU vs CPU、棋譜読込、D14局面解析、評価チューナーが従来通り動作する。
12. `DecideMoveAsync` が合法手を返し、通常設定では呼出スレッド優先度を変更しない。

# TEST PLAN — v0.2.26.0

## A. ビルド・起動

1. Release / `.NET 10.0-windows` でビルド。
2. 起動タイトルが `v0.2.26.0`。
3. `ツール` メニューに `CPU棋力 標準校正` が存在しない。
4. 例外なく初期局面が表示される。

## B. メイン画面CPU棋力UI

1. `P1 CPU棋力` / `P2 CPU棋力` の2つの選択欄が表示される。
2. それぞれ15級～五段の20項目を選べる。
3. 既定は両方5級。
4. 人間(P1) vs CPU(P2): P1欄無効、P2欄有効。
5. CPU(P1) vs 人間(P2): P1欄有効、P2欄無効。
6. CPU vs CPU: 両方有効。
7. 人間 vs 人間: 両方無効。
8. 従来のP1/P2探索深度・時間上限・Node上限欄がメイン画面から消えている。

## C. 固定Node表

ログまたはデバッガで代表値を確認:

- 15級 N200
- 10級 N3,283
- 5級 N53,878
- 1級 N505,316
- 初段 N884,302
- 三段 N2,708,176
- 四段 N4,739,307
- 五段 N8,293,788

通常CPUでRandomnessを使わないこと。

## D. 人間 vs CPU

1. `人間(P1) vs CPU(P2)`、P2=15級で新規対局。
2. 人間が1手指すとCPUが応手する。
3. 棋譜欄のCPU手に `CPU 15級` が表示される。
4. 同様に五段を選択し、CPU手が合法であること。
5. `CPU(P1) vs 人間(P2)` でも選んだP1棋力で初手を自動実行する。

## E. CPU vs CPU

1. P1=15級、P2=五段を選択。
2. `CPU開始` で双方が自動着手。
3. 棋譜欄でP1は `CPU 15級`、P2は `CPU 五段` と表示される。
4. `CPU一時停止` / `CPU 1手` が従来通り動く。

## F. ログ・保存・再開

1. `game_started.ai.skill` にP1/P2級段名が記録される。
2. `game_started.ai.maxNodes` に選択した固定Node値が記録される。
3. 名前をつけて保存→開く→途中再開が従来通り動く。

## G. ライブ評価値表示

1. 人間 vs CPUで、CPU思考開始時に棋譜欄上の `評価値(P1基準)` が表示更新される。
2. 1秒程度以上かかる棋力で、思考中に表示が継続して更新される。
3. P1 CPUではCPU探索スコアと同符号、P2 CPUでは符号反転し、常に `+ = P1有利 / - = P2有利` になる。
4. 深度途中では `D○暫定`、前深度完了後に次深度へ入った場合は `D○完了 / D△探索中` の表示になる。
5. CPU思考終了後も、その手で確定した最終評価値が表示されたままになる。
6. 15級など思考が1秒未満で終わるCPUでも、着手後に最終評価値を確認できる。
7. 次のCPU思考開始時はライブ評価へ切り替わり、新規対局/棋譜読込時は `---` に戻る。
8. 評価表示追加後も棋譜欄・ログ欄が欠けず、右ペイン全体が画面内に収まる。

## H. 回帰

1. `CPU探索整合性検証` PASS。
2. `Bitboard整合性検証` PASS。
3. `高速ヘッドレス` が開く。
4. `現在局面 深度ベンチ` が開く。
5. Standard smokeシナリオが完走する。

## I. 包囲時の強制退避

1. ★の上下左右4マスを相手駒で塞ぎ、斜めに自分の○を1個残す。
2. 包囲された側の手番では、その★の犠牲退避だけが合法手になる。
3. 包囲された側の別の○を選んでも着手できない。
4. ★を斜めの自分○へ退避すると、その○が盤上から取り除かれて対局が続く。
5. 退避できる自分○がない状態で四方包囲が完成した場合、包囲された側が敗北する。
6. `Bitboard整合性検証` の `包囲時強制退避専用` がPASSする。

## J. 棋譜並べ / 局面解析

1. JSONL棋譜を `開く` → 棋譜並べ画面を開く。
2. 右下に `棋譜` / `局面解析` タブがあり、棋譜ListBoxが従来同様に十分な高さを持つ。
3. 任意の手数へ移動し `局面解析` → `この局面を解析`。
4. 解析中もUIが固まらず、`中止`が効く。
5. 深度完了ごとにD1, D2...の `評価(P1基準) / 最善手 / 累計Nodes / 経過` が増える。
6. P2手番の局面でも符号が反転され、常に `+ = P1有利 / - = P2有利`。
7. 最大Dを11～14に設定できる。通常対局CPUの棋力プロフィールはD10のまま。
8. Node上限に達した場合、最後に完了した深度までの結果を保持し `node-limit` と表示する。
9. 完了後に候補手一覧・bound・読み範囲内の勝ち確定/負け確定候補数・静的評価内訳が表示される。
10. 解析中に別の手数へ移動すると旧解析はキャンセルされ、新局面を未解析として扱う。
11. 既知の棋譜 `StarRunner_20260811_220522.jsonl` の94～98手付近を解析し、D10より深いところで±100万へ到達するか確認する。

## K. フェーズ評価

1. 初期局面の局面解析で、overrideなしなら `profile=Tuned-G0015`（override採用時はそのprofile名）と `phase=0.0%` が表示される。
2. 両★を進める、または○を犠牲消費した局面ではphaseが増える。
3. 片側★だけを単身で深く進めても、phaseが急に100%近くへ飛ばない。
4. `CPU探索整合性検証` が新評価関数でもPASSする。
5. 旧棋譜94手目をD14解析し、強制負け値が評価関数変更後も探索で検出される。

## L. 評価関数チューナー

1. `ツール → 高速ヘッドレス` に `評価関数チューナー` ボタンがある。
2. チューナーを開くと現在profile/sourceと序盤/終盤倍率が表示される。
3. 小テストとして `1世代 / 2候補 / D3 / 4局 / 検証4局` 程度で完走する。
4. UIが固まらず進捗が更新され、`中止` が効く。
5. 各候補で先後入替対局が行われ、世代結果に shallow / validation / ACCEPT or KEEP が表示される。
6. 完了後 `analysis_logs/evaluation_tuning_*.json` が生成される。
7. `最良結果を採用` で `evaluation_profile_v2.json` が保存され、次のCPU思考からそのprofileがログ・局面解析に表示される。
8. アプリ再起動後も採用profileを読み込む。
9. `組込み既定へ戻す` でoverrideが消え `Tuned-G0015` に戻る。
10. 通常CPUの15級～五段MaxNodes表とD10上限が変化していない。

## v0.2.26.0 評価チューナーUI
1. 評価関数チューナーを開き、左右に「開始時パラメータ」「現在の最善パラメータ」が表示されること。
2. 開始直後は左右が同値であること。
3. ベスト更新が発生した時だけ右側のprofile名・数値が更新され、左側は変化しないこと。
4. ダッシュボードの経過時間が1秒ごとに増えること。
5. D5候補完了ごとに完了対局数が増え、D7検証/昇格決定戦の完了分も加算されること。
6. ベスト更新回数・最終更新世代・停滞・変異幅・Champion名がログと整合すること。
7. 停止ボタン押下後、最後の昇格確定Championが右側に残ること。
8. 「最良結果を採用」後も直前セッションの左右比較は保持され、上部「現在の標準評価」だけ採用profileへ変わること。

## v0.2.26.0 フラット評価 / 少数変異チューナー（研究基準の回帰確認）
1. `CpuEvaluationProfile.FlatNormalizedV1` を明示的に開始profileとして使用し、開始時の24重みが全て1000‰であること。
2. `FlatNormalizedV1` を明示した探索/評価ではprofile名が `FlatNormalizedV1` であること。通常製品CPUのoverrideなし既定はv0.2.30.0では `Tuned-G0015`。旧 `Tuned-G0042` は明示指定時のみ使用する。
3. 旧 `evaluation_profile.json` が存在しても自動適用されず、`evaluation_profile_v2.json` のみがoverrideとして使われること。
4. C1,C2は1項目、C3,C4は2項目、C5は3項目、C6は5～8項目の変異と進捗/結果JSONに出ること。
5. 1候補内で同じOpening/Endgameパラメータが重複選択されないこと。
6. 変更対象が特定特徴へ偏る固定優先ロジックを持たず、全24次元から同率抽選されること。
7. `CPU探索整合性検証` がPASSし、局面解析D14の終端勝敗値は従来通り±999xxxで検出できること。
8. 旧棋譜序盤を解析し、★前進素点が旧非線形曲線ではなく線形化されていることを評価内訳で確認すること。


## v0.2.27.0 追加確認

1. `evaluation_profile_v2.json` が無い状態で `CpuEvaluationProfileProvider.Current.Name == "Tuned-G0015"`。
2. 組込み既定へ戻した後も `Tuned-G0015`。
3. `FlatNormalizedV1` はAPI上利用可能で、全24重み1000‰。
4. SolutionのReleaseビルドで `StarRunner.Core.dll` と `StarRunnerPrototype.exe` が生成される。
5. `StarRunner.Core` 単体がWinForms参照なしでビルドできる。
6. 標準初期局面で合法手生成・1手適用・Clone・CPU探索が従来通り動作する。
7. ヘッドレス/verifierがCoreのfriend internal APIを利用して従来通り動作する。


## v0.2.30.0 ★往復回避の追加確認

1. 実局で★を `D5-C5` と動かし、その後に同じ側が○を1手以上動かしても、`C5-D5` が「最後の実局Runner移動の逆戻り」として検出されること。
2. 探索中の仮想Runner移動では、この実局Runner履歴が変化しないこと。
3. 同一minimax値のroot候補に「★逆戻り」と非逆戻りがある場合、非逆戻り側が先にExactとなり選ばれること。
4. ★逆戻りの真のminimax値が非逆戻りより高い場合は、後からfail-highしてExactとなり逆戻りが選ばれること（強制禁止ではない）。
5. `GameState` schema 2で最後の実局Runner移動がJSON往復後も一致すること。schema 1は読み込み可能で、当該履歴はnullとして扱うこと。
6. CPUログrootPreferenceに `runnerReturnCandidatePresent` / `selectedRunnerReturnMove` が出力されること。

## v0.2.31.0 RunnerGoalPath 追加確認

1. 評価関数チューナーの開始/最善profile表示に `RunnerGoalPath` のOpening/Endgameが表示されること。
2. BuiltIn `Tuned-G0015` ではRunnerGoalPathが1000‰/1000‰であること。
3. v0.2.30以前の11特徴 `evaluation_profile_v2.json` を置いて起動した場合、既存22値を保ったままRunnerGoalPath=1000‰として読み込むこと。
4. RunnerGoalPath=0を明示した新profileは0のまま読み込むこと。
5. CPU search correctness verifierがRunnerGoalPath固定テスト込みでPASSすること。
6. 棋譜の局面解析で静的評価内訳に「★ゴール経路」が表示されること。
7. 長時間チューナーで変更パラメータ名に `O.RunnerGoalPath` / `E.RunnerGoalPath` が出現し得ること。


## v0.2.32.0 評価チューナー早期棄却確認（deep match回帰）

1. 旧v0.2.32.0の「全shallow候補が最大局数を完走」はv0.2.33.0で廃止。現行shallow動作は下のSuccessive Halving確認を正とする。
2. 検証戦で残り全勝でもvalidation 49%未満となるケースは、予定局数前に `StoppedEarly=true` となること。
3. validation単体は49%へ届き得ても、残り全勝時のpreliminary combinedが51.5%未満なら早期棄却されること。
4. 昇格決定戦で残り全勝でもconfirmation 50%未満、またはfinal combined 52%未満なら早期棄却されること。
5. EARLY matchのGamesはPlannedGames未満、TotalGamesCompletedは実際に採用した完了ペア分だけ増えること。
6. 候補P1/P2の完了局数は偶数打切り時に同数であり、先後ペアが崩れないこと。奇数予定局数の追加1局はペア相が完走した場合のみ従来どおり第2色へ割り当てる。
7. 早期棄却なしのmatchはv0.2.31.0と同一seed列（P1 seed + i*104729 / P2 (seed^0x5f3759df)+i*104729）を使用すること。

## v0.2.33.0 浅い候補戦 Successive Halving 確認

1. 既定 `6候補 / shallow最大24局` で、浅い選抜が `R1: 6候補×8局 -> 3候補`, `R2: 3候補×追加8局 -> 2候補`, `R3: 2候補×追加8局 -> 1候補` になること。
2. 既定設定の浅い実施局数が合計88局であること（旧固定方式は144局）。
3. 最終勝者の `ShallowScore.Games == 24` であり、深い検証へ渡すshallow scoreのサンプル数は旧方式から減っていないこと。
4. 各追加ラウンドはcandidate=P1/P2の2局1ペアで実行され、各候補の累積局数が常に偶数で先後同数になること。
5. 同じ候補のR2/R3ではgameIndexがR1から連続し、同一seed・同一gameIndexの局を重複して再実行しないこと。
6. ラウンド順位は `累積Score -> 直近ラウンドScore -> 決着局勝率 -> candidate番号` の順で決まり、完全同点でも同じseedなら進出者が再現すること。
7. 候補数2～32、shallow最大局数4～1000の範囲で、round targetが単調増加・偶数・最終値=設定最大局数になること。
8. shallow最大局数へ奇数値をプログラム経由で渡した場合は、先後ペア維持のため次の偶数へNormalizeされること（上限1000）。
9. report schema 6に `shallowSelection=SuccessiveHalvingBalancedPairs` と各generationの `ShallowRounds` が出力されること。
10. `ShallowRounds[*].Candidates` にround score / cumulative score / Advancedが入り、UI結果行の `halving=6→3→2→1@8/16/24` と整合すること。
11. v0.2.32.0で追加したvalidation/confirmationの数学的EARLY rejectが引き続き動作し、浅いhalving変更で採用条件（49%, 51.5%, 50%, 52%）が変化していないこと。
12. `Tuned-G0015` の従来22値と `RunnerGoalPath=1000/1000` が一切変化していないこと。


## v0.2.34.2 追加確認

1. override無しで `CpuEvaluationProfileProvider.Current.Name == "Tuned-G0028"`。
2. Tuned-G0028の24値がNEXT_CHAT_HANDOFF.mdの表と一致する。
3. 評価関数チューナーに1パラメータ・スキャン欄が表示され、既定はRunnerGoalPath / Endgame / 600..1400 / step100 / D5 / 24局/値。
4. 600..1400 step100なら9値が完走し、各値24局なら総対局216局。
5. 各値のcandidate=P1/P2が均等で、同じscan seed集合が値を跨いで再利用される。
6. スキャン中に通常自動調整開始ボタンは無効、停止ボタンは有効。
7. 完了後に値/score/W-L-D/BEST/CURRENTが表示され、JSONが開ける。
8. 「結果を採用」でスキャンBestの1重みだけが変わったoverrideが保存される。
9. 「組込み既定へ戻す」でTuned-G0028へ戻る。


## v0.2.34.3 追加確認 — 新しい組込み既定

1. override無しで `CpuEvaluationProfileProvider.Current.Name == "Scan-O.RunnerGoalPath-1500"`。
2. `CpuEvaluationProfile.BuiltInDefault == CpuEvaluationProfile.ScanORunnerGoalPath1500` 相当の参照になっている。
3. 24値がNEXT_CHAT_HANDOFF.md先頭の採用表と完全一致する。
4. 「組込み既定へ戻す」で `Scan-O.RunnerGoalPath-1500` に戻る。
5. `Tuned-G0028` は旧プリセットとして引き続き参照可能。
6. 通常チューナーと1パラメータ・スキャンの挙動はv0.2.34.2から不変。

## v0.2.35.0 追加確認 — MaxNodes駆動CPU

1. 通常対局で五段を選び、棋譜 `game_started.ai.commonDepthCap` が `99` になること。
2. 終盤局面で五段がD10を越えてD11以上へ進めること。
3. 強制勝敗が未確定の局面では、`cpu_decision.nodes` が原則MaxNodes近傍まで到達し、`nodeLimitReached=true` になり得ること。
4. Node上限で途中Depthが中断された場合、`completedDepth < requestedDepth(99)` でも合法手が選ばれ、最後の完了Depthの結果が採用されること。
5. 強制勝敗値（±999xxx）を証明した局面では、MaxNodes未満でも正常終了すること。
6. 15級～五段のMaxNodes表が従来値から変わっていないこと。
7. 棋譜並べ局面解析のUI最大D14、評価関数チューナーのDepth上限D10は従来どおりであること。


## v0.2.36.9 Replay CTS regression

1. 棋譜並べで局面74を解析し、完了後に局面70→71→74と連続移動する。例外が出ないこと。
2. 局面74の解析中に局面70へ移動し、直後に別局面で再解析する。旧解析の完了/キャンセル後も新解析が継続すること。
3. 解析中に「中止」を押した直後、複数局面を連続選択する。`ObjectDisposedException`が出ないこと。
4. 解析中に棋譜並べフォームを閉じる。終了後のcontinuationがUIを更新せず例外も出ないこと。
5. 上記後、棋譜並べを再度開き通常解析が可能なこと。

## v0.2.37.0 Mate-distance Scout regression

### A. Build / baseline

1. Build `Release` on Windows.
2. Run the existing correctness verifier and previous v0.2.36.x smoke tests.
3. Confirm initial/fixed-depth searches with `MaxNodes=0` behave exactly as before; the
   Scout is node-budget-only and must not activate.

### B. User-reported position 76

1. Load `analysis_samples/StarRunner_20260815_102749.jsonl` in Replay.
2. Move to **position 76 / P1 to move**.
3. Analyze with `最大D14 / Node上限50,000,000 / 1 thread`.
4. Confirm normal iterative deepening completes D9 near the previous baseline before the
   Scout begins (exact node count may change slightly due code layout/runtime).
5. Confirm the status changes to `Mate Scout D... 勝ち/負け証明中` instead of immediately
   consuming the remainder on a normal all-move D10. Scout starts from D1; the early
   proof horizons may pass too quickly to see in the UI.
6. Confirm the final report contains a `Mate Scout:` telemetry line.
7. If a forced loss is proved, confirm:
   - score enters the negative mate band,
   - `強制負け M<N>` is displayed,
   - chosen move is the longest-delay root move from the preceding failed loss horizon.
8. If 50M is still insufficient, confirm the final result remains the last completed
   normal-depth score/move and is marked `未証明`; no synthetic mate score is allowed.
9. If Scout reaches its max proof depth before consuming the node budget, confirm normal
   iterative deepening resumes rather than returning early with unused nodes.

### C. Non-mate budget exhaustion

Use a middlegame position with no short forced result and a node cap that causes Scout
activation. Confirm Scout may exhaust the remaining budget; if it does, the returned
move/score remain exactly those of the last completed normal depth. If it reaches its
depth cap with nodes remaining, normal iterative deepening must resume.

### D. Mate already found normally

Use a position with an immediate/short forced goal. Confirm normal search stops on its
mate score and Mate Scout telemetry remains zero.

## v0.2.37.1 directed-parity Mate Scout regression

### A. Position 76 — primary

1. Build Release on Windows and run existing correctness verifiers.
2. Load `analysis_samples/StarRunner_20260815_102749.jsonl`, position 76 / P1 to move.
3. Analyze `最大D14 / Node上限50,000,000 / 1 thread`.
4. Normal search should still finish around D9 before Scout takeover.
5. Because the completed normal score is negative, Scout must report `mode loss-first` and **must not run any Win probe**.
6. Coarse probes must follow only even terminal parity after D9: `Loss D10`, then if completed/unproved `Loss D12`, then D14, D16... up to the configured cap/budget.
7. Final telemetry must distinguish:
   - `完了最大D`: highest fully completed probe,
   - `着手最大D`: highest started probe.
   If D14 is interrupted by MaxNodes, expected shape is `完了最大D12 / 着手最大D14`, not the ambiguous old `maxD14`.
8. Each probe line must show its own Node cost and one of `未証明 / 証明 / node-limit`.
9. If a loss is proved at D after D-2 completed unproved, final score must be `-1,000,000 + D` and the selected root move must be the escape witness from D-2.
10. If the first coarse probe D10 is already proved, Scout may enter `距離確定`; those shallower refinement probes must use fresh proof TT contexts and produce the exact legal-parity mate distance.
11. If no proof fits in 50M, final move/score must remain the completed normal D9 result unchanged.

### B. Positive-score direction

Use a nonterminal position whose last completed normal score is positive and forces Scout takeover. Confirm Scout reports `mode win-first`, probes odd depths only, and emits no Loss probe.

### C. Immediate shorter mate hidden by normal selectivity

Use/create a position where the first coarse Scout depth proves mate. Confirm distance refinement finds the shortest legal-parity M rather than blindly reporting the coarse D.

## v0.2.37.3 High-first Mate Scout regression

### Position 76 / 50M / 1 thread
1. Open `StarRunner_20260815_102749.jsonl` and navigate to position 76.
2. Analyze with normal MaxD=14, Node limit=50M, 1 thread.
3. Normal iterative deepening should still complete around D9 before Scout takeover under the same budget conditions.
4. Scout must **not** start at Loss D10. With Scout cap D22 it should start at **Loss D16**.
5. If Loss D16 proves mate, report either exact `Mxx` after refinement or `<=M16 (distance unconfirmed)` if refinement later hits the node limit.
6. If Loss D16 completes unproven, the next existence probe should jump to D22, not D18.
7. An incomplete existence probe must not create a forced result.

### Replay analysis UI
1. Open the `局面解析` tab at the narrowest supported replay-window width / normal Windows scaling.
2. Confirm `この局面を解析` and `中止` are both visible; wrapping to a second row is acceptable.


## v0.2.37.4 SacrificeDebt regression

1. Release build and run the normal correctness verifier.
2. Replay the supplied 2026-08-15 game and analyze position 76 with D14 / 50M / 1 thread.
3. Static breakdown must include `犠牲負債`; with built-in/migrated Tuned-G0004 expect approximately `-71` and TOTAL approximately `-275` (small differences imply a formula/state mismatch and should be investigated).
4. Confirm the active profile printout contains `SacrificeDebt 1800‰ / 2600‰`.
5. Confirm a fresh initial position has `SacrificeDebt = 0`.
6. Confirm a custom scenario that starts with fewer than six blockers does not count the absent start pieces as consumed.
7. Re-run position 76 search and compare D1..D9 scores / best moves against v0.2.37.3; the objective is a meaningfully more pessimistic P1 evaluation even when mate is beyond the node horizon.
8. Later: run a one-parameter scan for `SacrificeDebt` Opening/Endgame before treating 1800/2600 as tuned values.
