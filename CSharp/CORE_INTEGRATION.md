> **v0.2.37.4 note:** `SacrificeDebt` 評価特徴を追加。StartConfigurationから実消費○数を取り、残り資源圧力×「前進/ゴール経路/FrontPressureを踏まえた攻撃回収不足」で負債化。

> **v0.2.37.1 note:** Mate Scoutをdirection-only + terminal parity方式へ変更。通常D9で負評価ならloss-onlyでD10→D12→D14…をprobeし、逆方向probeとD1からの総当たりを廃止。最初のcoarse probeで既にmateならfresh TTの距離確定probeで最短Mを求める。Replay telemetryは各probe Nodesと「完了最大D / 着手最大D」を表示。

> **v0.2.37.0 note:** Node予算終盤で次の通常Depth完走が困難な場合に、D1から強制勝敗だけを証明するbudget-aware Mate-distance Scoutを追加。Scoutは専用TT・LMR/only-survival OFF・非終局depth0=unknown(0)で動き、未証明なら通常結果を壊さない。`CpuSearchOptions` の末尾optional設定と `CpuSearchTelemetry` のadditive init診断値のみ追加。

> **v0.2.36.8 note:** `CpuSearchOptions.MaxAdaptiveRootDeepeningPly`（既定6）を追加。recursive only-survival budget（既定2）とは分離し、rootの唯一生存枝だけ最大+6plyまでadaptive deepeningする。telemetryにadaptive root pass数と最大追加plyを追加。

> **v0.2.36.7 note:** `StarRunner.AI.CpuPlayer` にPVS / mate-distance pruning / StarRunner専用verification付きLMR / same-iteration対応only-survival extensionを追加。`CpuSearchOptions` に4機能のON/OFFとextension budgetを追加し、既定ON。`CpuDecision.SearchTelemetry` で発火回数を取得できる。既存評価API・Tuned-G0004・CPU Node表は変更なし。

> **v0.2.36.6 note:** 通常CPUの固定棋力表だけ変更。`NodeMultiplier=1.80`、五段14,164,707 Nodes。評価API・Tuned-G0004・v0.2.36.5 hot-path最適化は変更なし。

> **v0.2.36.5 note:** static evaluation cacheと同値hot-path短縮を追加。公開ルール/API互換は維持（`CpuSearchOptions` に末尾optional boolを追加）。Tuned-G0004 / 五段49,978,316 Nodesは変更なし。

> **v0.2.36.4 note:** 組込み既定評価Profileは `Tuned-G0004`。GoalDefenseは同値なmulti-source BFS fast pathへ変更。公開ゲームルール/API shapeは変更なし。通常CPUは `NodeMultiplier=1.9235`、五段49,978,316 Nodes。

# v0.2.36.4 note

- 組込み既定評価Profileは `Scan-O.BlockerMaterial-1000`。15特徴30重みは2026-08-15受理の最新値。旧 `Tuned-G0004` は履歴再現用。
- GoalDefenseの候補ごとの重複BFSを1回のmulti-source BFSへ置換。評価意味は維持。
- `Tuned-G0019` は再現用プリセットとして保持。
- MaxNodes駆動/SearchDepthCap=99、公開API、標準ルールは変更なし。

---


# v0.2.36.0 note

- 評価profileを16特徴/32重みへ拡張。
- 新4相互作用 (`PreparedGoalThreat`, `UnansweredGoalThreat`, `ConnectedGoalThreat`, `ViableRunnerProgress`) は組込み既定0‰。
- 旧12特徴24重み、MaxNodes駆動/SearchDepthCap=99、標準ルールは変更なし。
- tuner / 1パラメータscan / profile persistence / replay breakdown は16特徴対応。

---

# StarRunner 組み込みガイド — v0.2.36.0

> 公開APIの引数・戻り値・例外・保存/復元・AI threadingまで含む詳細リファレンスは `EMBEDDING_REFERENCE.md`。

## 構成

v0.2.28.0で再利用部分を2層に分離し、v0.2.31.0でもこの境界を維持する。

- `StarRunner.Core` (`net10.0`)
  - 盤面モデル
  - Standardルール
  - 合法手生成 / 着手 / 勝敗判定
  - 強制退避 / 即時戻り制限 / 四回同一局面
  - `GameState` Export / Import
- `StarRunner.AI` (`net10.0`)
  - CPU探索
  - 15級～五段の20段階
  - 評価関数
  - 組込み既定 `Scan-O.BlockerMaterial-1000`

WinForms、棋譜ファイル、ログ、Scenario Lab、RushOne、評価チューナー、ベンチマーク、override用ファイルI/Oは `StarRunnerPrototype` ホスト側に残す。

`StarRunner.Core` と `StarRunner.AI` は WinForms に依存しない。AIが不要な組込み先は Core だけを参照できる。

## 参照方法

ゲームルールだけ利用する場合:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/StarRunner.Core/StarRunner.Core.csproj" />
</ItemGroup>
```

CPUも利用する場合:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/StarRunner.Core/StarRunner.Core.csproj" />
  <ProjectReference Include="path/to/StarRunner.AI/StarRunner.AI.csproj" />
</ItemGroup>
```

namespace はそれぞれ `StarRunner.Core` / `StarRunner.AI`。

## 最小ゲーム利用例

```csharp
using StarRunner.Core;

var game = new GameEngine();
IReadOnlyList<Move> legalMoves = game.GetLegalMoves();

Move move = legalMoves[0];
if (!game.TryApplyMove(move, out string? error))
{
    throw new InvalidOperationException(error);
}
```

`GameEngine` はUI・描画・ファイルパスを知らない。

## CPU利用例

```csharp
using StarRunner.AI;
using StarRunner.Core;

var game = new GameEngine();
CpuSkillProfile skill = CpuSkillProfiles.BuiltInStandard[10];
CpuSearchOptions options = skill.ToSearchOptions(
    randomSeed: 12345,
    maxParallelism: Math.Max(1, Environment.ProcessorCount / 2));

CpuDecision decision = await CpuPlayer.DecideMoveAsync(
    game.Clone(),
    game.CurrentPlayer,
    options);

game.TryApplyMove(decision.Move, out _);
```

同期版 `DecideMove` も残している。UIアプリでは `DecideMoveAsync` またはホスト側バックグラウンド処理を推奨する。

### スレッド優先度

`StarRunner.AI` は組込み先のスレッド優先度を標準では変更しない。
`CpuSearchOptions.UseBelowNormalThreadPriority = true` を明示した場合だけ探索スレッドを BelowNormal にし、終了時に元へ戻す。

## CPU評価プロファイル

組込み既定は常に:

```csharp
CpuEvaluationProfile.BuiltInDefault // Scan-O.BlockerMaterial-1000
```

`StarRunner.AI` 自体は `evaluation_profile_v2.json` を探さず、ファイルを一切読み書きしない。そのため、別ゲームのユーザーPCに偶然残っているファイルでCPU挙動が変わることはない。

ホストが意図的に変更する場合だけ:

```csharp
CpuEvaluationProfileProvider.SetCurrent(customProfile, "my-host");
```

または個々の探索で `CpuSearchOptions.EvaluationProfile` を指定する。

StarRunnerPrototype開発アプリだけは、従来互換の `evaluation_profile_v2.json` 読み書きを `CpuEvaluationProfileStorage` で行う。

## 保存 / 復元

StarRunnerは盤面だけ保存すると不十分。即時戻り制限、四回同一局面、CPUの循環回避履歴も継続する必要がある。

```csharp
using System.Text.Json;
using StarRunner.Core;

GameState state = game.ExportState();
string json = JsonSerializer.Serialize(state);

GameState loaded = JsonSerializer.Deserialize<GameState>(json)!;
GameEngine resumed = GameEngine.FromState(loaded);
```

`GameState.SchemaVersion` により将来の形式変更を検出する。

ホスト独自の `IGameMovePolicy` を利用している局面では、復元時に同じ `Id` のpolicyを渡す必要がある。

```csharp
GameEngine resumed = GameEngine.FromState(loaded, myPolicy);
```

## 初期局面

`GameStartConfiguration` は immutable。共有される `GameStartConfiguration.Initial` の盤面を外部コードから書き換えることはできない。

独自局面:

```csharp
var start = GameStartConfiguration.Create(
    "My position",
    "host",
    new[]
    {
        "ooosooo.",
        "........",
        "........",
        "........",
        "........",
        "........",
        "........",
        ".OOOSOOO"
    },
    PlayerId.Player1);

var game = new GameEngine(start);
```

## `IGameMovePolicy`

Standardルールは policy を使わない (`null`)。

開発ツールや特殊な組込み先だけ、base-rule合法手を追加制限する `IGameMovePolicy` を渡せる。

```csharp
var game = new GameEngine(start, myPolicy);
```

これはStarRunnerのStandardルールそのものではない。Scenario Lab の `RushOne` 実装も v0.2.28.0 からホスト側 `ScenarioMovePolicy.cs` へ移した。

policy実装は immutable / thread-safe とし、ゲームごとの可変状態は `ulong policyState` に保持する。これにより `Clone`、CPU探索、Export/Importでも状態を追跡できる。

## 公開APIと開発用internal API

外部ゲームは公開APIだけを利用する。

既存の高速探索・correctness verifierは性能上の理由でCore内部APIを使うため、開発ホストとAIにだけ `InternalsVisibleTo` を設定している。

- `StarRunner.AI`
- `StarRunnerPrototype`

外部組込み先にはinternal APIを公開しない。
