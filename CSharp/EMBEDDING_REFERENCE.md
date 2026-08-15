> **v0.2.36.2 note:** evaluation raw logic was revised for FrontPressure, GoalDefense, BridgeheadConnection and root oscillation preference. See `NEXT_CHAT_HANDOFF.md` and `STATIC_AUDIT_v0.2.36.2.md`. Public game rules/API shape are otherwise unchanged.

# StarRunner 組込み用 API リファレンス — v0.2.36.1

この文書は `StarRunner.Core` と `StarRunner.AI` を別ゲーム／別アプリへ組み込むための公開 API リファレンスである。
導入手順だけを短く確認したい場合は `CORE_INTEGRATION.md`、正式なゲームルールは `STANDARD_RULES.md` を参照すること。

## 1. 対象と依存関係

```text
Embedding Host (別ゲーム / 別UI)
    ├─ StarRunner.Core   必須
    └─ StarRunner.AI     任意
            └─ StarRunner.Core
```

- `StarRunner.Core`: `net10.0`。UI、WinForms、ファイル I/O、CPU探索に依存しない。
- `StarRunner.AI`: `net10.0`。`StarRunner.Core` のみに依存する。
- `StarRunnerPrototype`: サンプル兼開発ホスト。組込み時の必須依存ではない。
- namespace は `StarRunner.Core` / `StarRunner.AI`。

ProjectReference 例:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/StarRunner.Core/StarRunner.Core.csproj" />
  <ProjectReference Include="path/to/StarRunner.AI/StarRunner.AI.csproj" />
</ItemGroup>
```

AIが不要なら `StarRunner.AI` の参照は不要。

---

## 2. 最小ライフサイクル

### 2.1 新規対局

```csharp
using StarRunner.Core;

var game = new GameEngine();
```

`new GameEngine()` は `GameStartConfiguration.Initial`、Standardルール、MovePolicyなしで開始する。

### 2.2 現在手番の合法手を取得

```csharp
IReadOnlyList<Move> legalMoves = game.GetLegalMoves();
```

特定プレイヤーについて問い合わせる場合:

```csharp
IReadOnlyList<Move> p1Moves = game.GetLegalMoves(PlayerId.Player1);
```

### 2.3 着手

```csharp
Move move = legalMoves[0];
if (!game.TryApplyMove(move, out string? error))
{
    // error は表示/ログ用。ゲーム状態は変更されない。
    Console.WriteLine(error);
}
```

着手成功時、`CurrentPlayer`、`Outcome`、`EndReason`、`PlyCount`、`LastMove`、反復履歴などは `GameEngine` が更新する。

### 2.4 終局判定

```csharp
if (game.Outcome != GameOutcome.Ongoing)
{
    Console.WriteLine($"{game.Outcome} / {game.EndReason}");
}
```

ホスト側で勝敗ルールを再実装しないこと。Coreの `Outcome` / `EndReason` を正とする。

---

# 3. StarRunner.Core

## 3.1 `GameEngine`

StarRunner 1局の完全な可変状態を保持する中心クラス。
UI状態やファイルパスは保持しない。

### 定数

| メンバー | 値 | 意味 |
|---|---:|---|
| `GameEngine.BoardSize` | `8` | 盤の縦横サイズ |
| `GameEngine.MaxLegalMoves` | `64` | 内部合法手バッファの最大値。通常の組込みでは意識不要 |

### コンストラクタ

```csharp
new GameEngine()
new GameEngine(GameStartConfiguration? startConfiguration)
new GameEngine(GameStartConfiguration? startConfiguration, IGameMovePolicy? movePolicy)
```

- `startConfiguration == null` は `GameStartConfiguration.Initial` と同義。
- `movePolicy == null` がStandardゲーム。
- `IGameMovePolicy` はStandardルールの合法手をさらに制限するホスト拡張。通常の製品組込みでは不要。

### 主要プロパティ

| プロパティ | 型 | 説明 |
|---|---|---|
| `CurrentPlayer` | `PlayerId` | 現在の手番 |
| `Outcome` | `GameOutcome` | `Ongoing` / P1勝ち / P2勝ち / 引分 |
| `EndReason` | `EndReason` | 終局理由 |
| `PlyCount` | `int` | 開始からの着手数（半手数） |
| `LastMove` | `Move?` | 直前の着手。開始直後は `null` |
| `Rules` | `RuleSet` | 現在は常に `RuleSet.Standard` |
| `StartConfiguration` | `GameStartConfiguration` | この局の開始局面 |
| `MovePolicy` | `IGameMovePolicy?` | 使用中の追加policy |
| `MovePolicyState` | `ulong` | policyが管理する局単位状態 |

`CurrentPlayer` 等の setter は外部公開されない。状態変更は合法な着手または `FromState` に限定する。

### `Clone()`

```csharp
GameEngine copy = game.Clone();
```

現在盤面だけでなく、即時戻り制限、四回同一局面、物理局面履歴、MovePolicy状態などを含む探索可能な複製を作る。
CPUに渡す局面や先読み用の分岐には `Clone()` を推奨。

### `GetPiece(Position)`

```csharp
Piece? piece = game.GetPiece(new Position(row, col));
```

- 盤内で駒があれば `Piece`。
- 空マスまたは盤外なら `null`。
- `Row`, `Col` は0始まり。

### `GetBoardRows()`

```csharp
string[] rows = game.GetBoardRows();
```

盤面の defensive copy を返す。文字コードは以下。

| 文字 | 意味 |
|---|---|
| `.` | 空 |
| `S` | Player1 Runner（★） |
| `O` | Player1 Blocker（○） |
| `s` | Player2 Runner（★） |
| `o` | Player2 Blocker（○） |

返された配列を書き換えても `GameEngine` は変化しない。

### `GetLegalMoves()` / `GetLegalMoves(PlayerId)`

```csharp
IReadOnlyList<Move> moves = game.GetLegalMoves();
IReadOnlyList<Move> p2Moves = game.GetLegalMoves(PlayerId.Player2);
```

Coreが強制退避、即時戻り制限、MovePolicyを含めて判定した合法手のみを返す。
終局後は空配列。

ホスト側で見た目上の候補を生成してから `IsLegalMove` で後判定するより、このAPIを入力元にする方が安全。

### `GetRunnerLegalMoves(PlayerId)`

```csharp
IReadOnlyList<Move> runnerMoves = game.GetRunnerLegalMoves(player);
```

指定プレイヤーのRunner（★）だけの合法手。

### Runner関連カウント

```csharp
int normal = game.CountRunnerNormalMoves(player);
int sacrifice = game.CountRunnerSacrificeMoves(player);
int immediateGoals = game.CountImmediateGoalMoves(player);
bool frontMarked = game.IsRunnerFrontMarked(player);
```

主に解析・表示用。ゲーム進行に必須ではない。

### `IsLegalMove(Move)`

```csharp
bool legal = game.IsLegalMove(move);
```

**現在手番**に対して判定する。
終局後は `false`。

### `TryApplyMove(Move, out string? error)`

```csharp
if (!game.TryApplyMove(move, out var error))
{
    // false の場合は局面不変
}
```

- 成功: `true`, `error == null`。
- 終局済み: `false`。
- 非合法手: `false`。
- `false` の場合はゲーム状態を変更しない。

UIからの通常着手ではこのメソッドを使うこと。探索用internal APIは外部向けではない。

### `FindRunner(PlayerId)`

```csharp
Position runner = game.FindRunner(PlayerId.Player1);
```

指定プレイヤーのRunner位置を返す。
有効なゲーム状態には各陣営ちょうど1個のRunnerが存在する。

### 駒数・行カウント

```csharp
int blockers = game.CountBlockers(player);
int piecesOnRow = game.CountPiecesOnRow(player, row);
int blockersOnRow = game.CountBlockersOnRow(player, row);
```

`row` は0～7。範囲外のrowは0を返す。

### `CurrentPositionRepetitionCount()`

```csharp
int count = game.CurrentPositionRepetitionCount();
```

ルール上の「完全同一局面」の現在回数を返す。
四回同一局面判定には盤面・手番だけでなく、即時戻り制限に影響する履歴を含む。
ホスト側で独自の千日手カウンタを持たないこと。

### `GetSearchHash()`

```csharp
ulong hash = game.GetSearchHash();
```

探索同一性用のハッシュ。盤面表示用IDや永続的な外部識別子としての安定性は保証しない。
保存形式には使わず `GameState` を使うこと。

### `ToLogBoard()`

```csharp
string[][] board = game.ToLogBoard();
```

ログ向け表現を返す。セルは `.`, `P1O`, `P1S`, `P2O`, `P2S`。
ゲーム保存用途には使用しない。

### `GetPositionKey()`

現在盤面 + 手番を文字列キー化する補助API。
**完全なルール状態を表さない**のでセーブ、四回同一局面、再開には使用しないこと。

---

## 3.2 `PlayerId`

```csharp
enum PlayerId
{
    Player1,
    Player2
}
```

拡張メソッド:

```csharp
PlayerId other = player.Opponent();
string shortName = player.ShortName(); // "P1" / "P2"
```

`ShortName()` は簡易表示用。ローカライズが必要ならホスト側で表示名を定義する。

---

## 3.3 `PieceType` / `Piece`

```csharp
enum PieceType
{
    Runner,
    Blocker
}

readonly record struct Piece(PlayerId Owner, PieceType Type);
```

- `Runner`: ★
- `Blocker`: ○

---

## 3.4 `Position`

```csharp
readonly record struct Position(int Row, int Col);
```

### 座標系

- `Row`: 0～7、上から下。
- `Col`: 0～7、左から右。
- `IsInside`: 盤内なら `true`。
- `ToCoordinate()`: `A1`～`H8` 形式。

例:

```csharp
var p = new Position(0, 0);
Console.WriteLine(p.ToCoordinate()); // A1
```

---

## 3.5 `MoveKind` / `Move`

```csharp
enum MoveKind
{
    Normal,
    Sacrifice
}

readonly record struct Move(Position From, Position To, MoveKind Kind);
```

- `Normal`: 通常移動。
- `Sacrifice`: Runnerが自軍Blockerを犠牲にしてそのマスへ移る手。

```csharp
string notation = move.ToNotation();
```

`ToNotation()` は簡易ログ表記。ゲーム内部の永続IDには使わない。

合法な `Move` は原則 `GetLegalMoves()` から取得する。

---

## 3.6 `GameOutcome`

```csharp
enum GameOutcome
{
    Ongoing,
    Player1Win,
    Player2Win,
    Draw
}
```

---

## 3.7 `EndReason`

```csharp
enum EndReason
{
    None,
    GoalReached,
    RunnerImmobilized,
    MovePolicyNoMove,
    FourfoldRepetition
}
```

互換aliasとして `StrategyConstraintNoMove = MovePolicyNoMove` が残っている。
新規組込みコードでは `MovePolicyNoMove` を使用する。

| 値 | 意味 |
|---|---|
| `None` | 未終局 |
| `GoalReached` | Runnerがゴール条件を達成 |
| `RunnerImmobilized` | ルール上Runnerが動けず敗北となった |
| `MovePolicyNoMove` | ホストMovePolicyにより合法手がなくなった |
| `FourfoldRepetition` | 完全同一局面4回で引分 |

正式な細則は `STANDARD_RULES.md` を正とする。

---

## 3.8 `RuleSet`

```csharp
readonly record struct RuleSet
{
    static RuleSet Standard { get; }
    string Id { get; } // "Standard"
}
```

v0.2.28.0ではStandardのみ。
将来ルールセットが増える可能性を考慮し、ホスト側で `Id == "Standard"` をゲーム進行条件としてハードコードする必要はない。

---

## 3.9 `GameStartConfiguration`

ゲーム開始時のimmutable局面。

### 組込み初期局面

```csharp
GameStartConfiguration initial = GameStartConfiguration.Initial;
```

### 独自初期局面

```csharp
var start = GameStartConfiguration.Create(
    name: "Puzzle 01",
    sourceName: "my-game",
    boardRows: new[]
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
    currentPlayer: PlayerId.Player1);

var game = new GameEngine(start);
```

### 公開プロパティ

| プロパティ | 説明 |
|---|---|
| `Name` | 局面名 |
| `SourceName` | 提供元識別名 |
| `BoardRows` | 読み取り専用盤面行 |
| `CurrentPlayer` | 初期手番 |
| `Hash` | 初期局面内容のSHA-256由来短縮hash |
| `BoardSignature` | `/` 区切り盤面文字列 |

`CopyBoardRows()` は変更可能なコピーを返す。
`GetPiece(Position)` は初期局面上の駒を取得する。

### `Create()` の検証

不正な初期局面は `InvalidDataException` 等を送出する。

必須条件:

- 8行。
- 各行8文字。
- 使用可能文字は `. S O s o` のみ。
- `S` と `s` はそれぞれちょうど1個。
- 各陣営Blockerは最大6個。
- `currentPlayer` は有効な `PlayerId`。

外部JSON等から局面を受ける場合は例外を捕捉し、信頼できない入力として扱うこと。

---

# 4. 保存 / 復元

## 4.1 `GameState`

`GameState` は正確な途中再開用DTO。

```csharp
public sealed record GameState(...)
```

主要フィールド:

| フィールド | 用途 |
|---|---|
| `SchemaVersion` | 保存形式バージョン |
| `Start*` | 元の開始局面 |
| `BoardRows` | 現在盤面 |
| `CurrentPlayer` | 現在手番 |
| `Outcome` / `EndReason` | 終局状態 |
| `PlyCount` | 手数 |
| `LastMove` | 直前手 |
| `Player1LastOwnMove` / `Player2LastOwnMove` | 即時戻り制限の復元 |
| `Player1LastRealRunnerMove` / `Player2LastRealRunnerMove` | CPUの★往復回避用・最後の実局Runner移動 |
| `RepetitionCounts` | 四回同一局面履歴 |
| `PhysicalPositionCounts` | AI循環回避履歴 |
| `MovePolicyId` / `MovePolicyState` | policy利用時の復元 |

現在の形式:

```csharp
GameState.CurrentSchemaVersion == 2
```

`FromState()` はschema 1も後方互換で受け入れる。schema 1には「最後の実局Runner移動」が存在しないため、その履歴だけ `null` から再開する。

### 保存例

```csharp
using System.Text.Json;
using StarRunner.Core;

GameState state = game.ExportState();
string json = JsonSerializer.Serialize(state);
```

### 復元例

```csharp
GameState loaded = JsonSerializer.Deserialize<GameState>(json)
    ?? throw new InvalidDataException("Invalid StarRunner state.");

GameEngine resumed = GameEngine.FromState(loaded);
```

### 重要: 盤面だけ保存しない

StarRunnerには盤面だけでは復元できないルール状態がある。

- 各プレイヤーの直前自手に依存する即時戻り制限。
- 四回同一局面の出現回数。
- AIの循環回避に使う実局履歴。
- AIの★往復回避に使う最後の実局Runner移動。
- MovePolicyを利用する場合のpolicy状態。

したがって正式な途中保存は必ず `ExportState()` / `FromState()` を使う。
`GetBoardRows()` や `GetPositionKey()` だけを保存形式にしてはいけない。

### `FromState()` の主な失敗条件

`InvalidDataException` になる代表例:

- `SchemaVersion` が未対応。
- 保存時と復元時の `MovePolicy.Id` が不一致。
- policyなしなのに `MovePolicyState != 0`。
- 負の `PlyCount`。
- 不正なenum値。
- 不正な盤面。
- 必須履歴が欠落している。

セーブデータは将来のバージョンでmigrationが必要になる可能性があるため、ホストのセーブ形式にもStarRunner schema/versionを保持することを推奨。

---

# 5. `IGameMovePolicy`

Standardゲームを変更せず、ホスト固有の「合法手の追加制限」を注入する高度な拡張点。
通常の対局組込みでは `null` のまま使用する。

```csharp
public interface IGameMovePolicy
{
    string Id { get; }
    ulong CreateInitialState(GameEngine game);

    bool IsMoveAllowed(
        GameEngine game,
        PlayerId player,
        Piece movingPiece,
        Move move,
        ulong policyState);

    ulong ApplyMove(
        GameEngine game,
        Piece movingPiece,
        Piece? replacedPiece,
        Move move,
        ulong policyState);

    ulong GetSearchHash(ulong policyState);
}
```

### 契約

- `Id` は**合法手に影響する設定をすべて含めた安定識別子**にする。
- policy本体は immutable / thread-safe にする。
- 局ごとの可変情報は `ulong policyState` だけに保持する。
- `IsMoveAllowed` はCoreが生成したbase-rule合法手をさらに許可/拒否する。
- `ApplyMove` は許可された手の適用後policy stateを返す。
- `GetSearchHash` はAI探索同一性に寄与する値を返す。
- 四回同一局面ルールそのものにはpolicy search hashは使われない。

### 保存したpolicy局面の復元

```csharp
GameEngine resumed = GameEngine.FromState(loadedState, myPolicy);
```

`loadedState.MovePolicyId` と `myPolicy.Id` が完全一致しなければ復元は拒否される。

---

# 6. StarRunner.AI

## 6.1 既定CPUの最小例

```csharp
using StarRunner.AI;
using StarRunner.Core;

CpuSkillProfile skill = CpuSkillProfiles.BuiltInStandard[10]; // 5級

CpuSearchOptions options = skill.ToSearchOptions(
    randomSeed: 12345,
    maxParallelism: Math.Max(1, Environment.ProcessorCount / 2));

CpuDecision decision = await CpuPlayer.DecideMoveAsync(
    game.Clone(),
    game.CurrentPlayer,
    options);

if (!game.TryApplyMove(decision.Move, out string? error))
{
    throw new InvalidOperationException(error);
}
```

### なぜ `game.Clone()` を渡すのか

AIは受け取った `GameEngine` を探索ルートとして扱う。
ホストUIが同じインスタンスを別スレッドから変更する構成を避けるため、非同期探索では `Clone()` を渡すことを推奨する。

対局進行側ではAI完了後、元の `game` に `decision.Move` を適用する。
AI思考中に人間側が局面を変更できるUIなら、思考開始時の `GetSearchHash()` 等を保持して結果適用前に局面一致を確認するか、CancellationTokenで旧探索を破棄すること。

---

## 6.2 `CpuSkillProfile`

```csharp
sealed record CpuSkillProfile(
    int Index,
    string Name,
    long MaxNodes)
```

標準棋力は20段階。

```csharp
IReadOnlyList<CpuSkillProfile> levels = CpuSkillProfiles.BuiltInStandard;
```

index:

```text
0..14 = 15級 .. 1級
15    = 初段
16    = 二段
17    = 三段
18    = 四段
19    = 五段
```

標準表は15級 `N200` から1段階ごとに `×1.75`、五段は `N8,293,788`。
通常棋力のDepth上限は `CpuSkillProfile.SearchDepthCap == 10`。

### `ToSearchOptions()`

```csharp
CpuSearchOptions ToSearchOptions(
    int randomSeed,
    int maxParallelism,
    int timeLimitMilliseconds = 0,
    long maxNodes = 0,
    int cycleBreakScoreWindow = 10,
    bool useBelowNormalThreadPriority = false)
```

- `randomSeed`: 再現性のため指定。標準20段階は意図的ランダム手を使わないが、探索補助情報の再現性確保として渡してよい。
- `maxParallelism`: CPU探索並列数。
- `timeLimitMilliseconds`: 0なら時間制限なし。
- `maxNodes`: 0ならprofile本来のNode数。正値は**追加の安全上限**で、profileより強くはならない。
- `cycleBreakScoreWindow`: 同程度の評価なら循環を避けるroot preference幅。
- `useBelowNormalThreadPriority`: 明示true時だけ探索スレッド優先度を下げる。

---

## 6.3 `CpuSearchOptions`

```csharp
sealed record CpuSearchOptions(
    int MaxDepth = 6,
    int TimeLimitMilliseconds = 1000,
    long MaxNodes = 0,
    bool UseTranspositionTable = true,
    bool CollectExactRootScores = false,
    int RandomTopK = 1,
    int RandomScoreWindow = 0,
    double RandomSelectionTemperature = 0,
    double RandomMoveProbability = 1.0,
    int? RandomSeed = null,
    int CycleBreakScoreWindow = 10,
    int MaxParallelism = 1,
    bool UseBelowNormalThreadPriority = false,
    CpuEvaluationProfile? EvaluationProfile = null,
    bool UseStaticEvaluationCache = true,
    bool UsePrincipalVariationSearch = true,
    bool UseMateDistancePruning = true,
    bool UseLateMoveReductions = true,
    bool UseOnlySurvivalExtension = true,
    int MaxOnlySurvivalExtensionsPerLine = 2,
    int MaxAdaptiveRootDeepeningPly = 6,
    bool UseMateDistanceScout = true,
    int MateDistanceScoutMinCompletedDepth = 6,
    int MaxMateDistanceScoutExtraPly = 8)
```

### 主な設定

| 設定 | 説明 |
|---|---|
| `MaxDepth` | 探索深度。`Normalize()` で1～99 |
| `TimeLimitMilliseconds` | 0で無制限。最大120,000msへclamp |
| `MaxNodes` | 0でNode上限なし。標準棋力はprofile側から設定 |
| `UseTranspositionTable` | TT利用 |
| `CollectExactRootScores` | root候補のexact score収集。解析向け |
| `RandomTopK` 等 | 研究/特殊CPU用ランダム選択設定 |
| `RandomSeed` | 乱数seed |
| `CycleBreakScoreWindow` | 実局循環回避の許容評価幅 |
| `MaxParallelism` | root探索並列度 |
| `UseBelowNormalThreadPriority` | スレッド優先度低下をopt-in |
| `EvaluationProfile` | `null` なら `CpuEvaluationProfileProvider.Current` |
| `UseStaticEvaluationCache` | depth=0静的評価cache。評価値は変えずLeaf再計算を削減 |
| `UsePrincipalVariationSearch` | PVS。PV以外をzero-windowでprobeし、必要時だけfull-window再検索 |
| `UseMateDistancePruning` | mate距離から不可能なscore範囲をalpha/betaから除外 |
| `UseLateMoveReductions` | StarRunner専用LMR。後順位の静かな○だけ浅く読み、bound改善時はfull-depth verification |
| `UseOnlySurvivalExtension` | 他候補がforced-lossで1手だけ生存する枝を選択的に延長 |
| `MaxOnlySurvivalExtensionsPerLine` | 内部ノードのonly-survival延長budget。0～4、既定2 |
| `MaxAdaptiveRootDeepeningPly` | rootで唯一生存が証明された場合のadaptive追加深さ。0～12、既定6 |
| `UseMateDistanceScout` | Node予算終盤で次の通常Depth完走が困難な場合、強制勝敗だけをD1から証明するMate Scout |
| `MateDistanceScoutMinCompletedDepth` | Scout発火判定を始める通常完了Depth。1～32、既定6 |
| `MaxMateDistanceScoutExtraPly` | Scoutが通常`MaxDepth`を越えて証明できる追加ply。0～32、既定8 |

```csharp
CpuSearchOptions safe = options.Normalize();
```

通常プレイヤー向け20段階は手書き `CpuSearchOptions` より `CpuSkillProfile.ToSearchOptions()` を推奨。

---

## 6.4 `CpuPlayer`

### 非同期探索（推奨）

```csharp
Task<CpuDecision> DecideMoveAsync(
    GameEngine position,
    PlayerId cpuPlayer,
    CpuSearchOptions options,
    CancellationToken cancellationToken = default,
    CpuSearchMonitor? searchMonitor = null)
```

簡易Depth版:

```csharp
Task<CpuDecision> DecideMoveAsync(
    GameEngine position,
    PlayerId cpuPlayer,
    int depth,
    CancellationToken cancellationToken = default)
```

`DecideMoveAsync` は内部で `Task.Run` を使う。
UIスレッドから直接呼び出しても探索本体でUIをブロックしないが、同一 `GameEngine` を並行変更しないこと。

### 同期探索

```csharp
CpuDecision DecideMove(
    GameEngine position,
    PlayerId cpuPlayer,
    CpuSearchOptions options,
    CancellationToken cancellationToken,
    CpuSearchMonitor? searchMonitor = null)
```

簡易Depth版もある。

### 代表的な例外

- `position == null`: `ArgumentNullException`
- `options == null`: `ArgumentNullException`
- 終局後の思考要求: `InvalidOperationException`
- `cpuPlayer != position.CurrentPlayer`: `InvalidOperationException`
- 合法手なしの不整合状態: `InvalidOperationException`
- cancellation: `OperationCanceledException` が伝播し得る

---

## 6.5 `CpuDecision`

CPU探索結果。

主要プロパティ:

| プロパティ | 意味 |
|---|---|
| `Move` | 選択手 |
| `Score` | CPU手番側から見た探索評価 |
| `Nodes` | 探索Node数 |
| `ElapsedMilliseconds` | 探索時間 |
| `Depth` | 完了した最深Depth |
| `RequestedDepth` | 要求Depth |
| `TranspositionHits` | TT hit数 |
| `BetaCutoffs` | β cutoff数 |
| `TimedOut` | 時間上限到達 |
| `NodeLimitReached` | Node上限到達 |
| `NodesPerSecond` | NPS |
| `Candidates` | root候補情報 |
| `CycleAvoidanceApplied` | 循環回避preferが働いたか |
| `RunnerOscillationAvoidanceApplied` | root preferenceで★の逆戻りを避けたか |
| `RunnerAdvancePreferenceApplied` | Runner前進preferが働いたか |
| `RunnerReturnCandidatePresent` | 最後の実局★移動を逆向きに戻す候補があったか |
| `SelectedRunnerReturnMove` | 最終選択手がその逆戻り手か |
| `SearchTelemetry` | PVS/LMR/mate-prune/only-survival/adaptive-root/Mate Scoutの診断値 |
| `StaticEvaluationAfterMove` | 選択手後の静的評価内訳 |


### v0.2.36.7 選択的探索

既定ではPVS / mate-distance pruning / StarRunner専用LMR / only-survival extensionが有効。
LMRは★・自己犠牲・TT/killer/history手・両★周辺・前方圧力に関与する○・ゴール列の○などを削減しない。削減した枝がalpha/betaを脅かした場合は元depthで再検索する。

rootで他候補がすべてmate-loss帯に入り1手だけ生存した場合、証明済みの兄弟枝は再検索せず、その唯一手だけ最大2ply追加探索する。深掘りした唯一手自身もforced-lossへ落ち、既存loss upper boundとのmate距離比較が必要になった場合のみroot全体を再検証する。

`SearchTelemetry` の既存位置引数は次の8項目。v0.2.37.1でもconstructor / Deconstruct形は維持。

- `PvsNullWindowProbes`
- `PvsResearches`
- `LmrReducedSearches`
- `LmrVerificationResearches`
- `MateDistancePrunes`
- `OnlySurvivalExtensions`
- `AdaptiveRootDeepeningPasses`
- `MaxAdaptiveRootDeepeningPlyReached`

v0.2.37.0以降、Scout診断はadditive init propertyとして追加。v0.2.37.1では完了Depth/方向/probe明細も追加。

- `MateDistanceScoutProbes`
- `MateDistanceScoutNodes`
- `MateDistanceScoutMaxDepthReached` — 着手最大D
- `MateDistanceScoutMaxCompletedDepth` — 完了最大D
- `MateDistanceScoutDirection` — +1 win-first / -1 loss-first
- `MateDistanceScoutProofDepth`
- `MateDistanceScoutProofScore`
- `MateDistanceScoutProbeDetails` — 各probeのDepth/方向/距離確定phase/完了/証明/Nodes

Mate ScoutはMaxNodes駆動時のみ、次の通常全幅Depthが残予算に収まりにくいと予測した場合に発火する。
LMR/only-survivalを無効にした専用TTのproof searchで、非終局depth=0は`0`（unknown）を返し静的評価を使わない。
v0.2.37.1では通常完了scoreの符号で一方向だけを選び、root勝ち=奇数ply / root負け=偶数plyの終局parityに沿って、通常完了Depthより深い最初の該当Dから+2plyずつmate閾値zero-windowを検査する。最初のcoarse probeですでに証明された場合だけfresh TTでより短いexact mate distanceを確定する。

`Score` は「Player1固定基準」ではなく、呼び出した `cpuPlayer` 視点。
UIで常にP1基準へ表示したい場合はPlayer2思考時に符号反転する。

探索内の勝敗値は概ね±1,000,000近傍を使用するため、通常の静的評価と区別しやすい。

v0.2.30.0では、最後の実局Runner移動を逆向きに戻す手はrootの**探索順だけ**降格する。評価値/minimax値そのものは変更しないため、逆戻り手が真に優れていれば通常どおり選択される。

---

## 6.6 `CpuCandidate`

```csharp
readonly record struct CpuCandidate(
    Move Move,
    int SearchScore,
    string Bound);
```

root候補の探索結果。`Bound` は探索上のbound情報／fallback情報を表す内部寄りの表示文字列であり、ゲームロジックには使用しない。

---

## 6.7 `CpuSearchMonitor`

思考中の評価表示に使う。

```csharp
var monitor = new CpuSearchMonitor();

Task<CpuDecision> thinking = CpuPlayer.DecideMoveAsync(
    game.Clone(),
    game.CurrentPlayer,
    options,
    cancellationToken,
    monitor);

if (monitor.TryGetSnapshot(out CpuSearchProgress p))
{
    Console.WriteLine($"D{p.ScoreDepth}: {p.Score}");
}

IReadOnlyList<CpuCompletedDepth> completed = monitor.GetCompletedDepths();
```

### `CpuSearchProgress`

```csharp
readonly record struct CpuSearchProgress(
    int Score,
    Move BestMove,
    int ScoreDepth,
    int TargetDepth,
    bool IsProvisional);
```

`IsProvisional == true` は現在Depthの探索途中値。

### `CpuCompletedDepth`

```csharp
readonly record struct CpuCompletedDepth(
    int Depth,
    int Score,
    Move BestMove,
    long Nodes,
    long ElapsedMilliseconds);
```

UIは `TryGetSnapshot()` をタイマー等でpollしてよい。

---

# 7. 評価関数

## 7.1 組込み既定

```csharp
CpuEvaluationProfile.BuiltInDefault
```

は現在必ず:

```text
Scan-O.BlockerMaterial-1000
```

AIライブラリはファイルを自動探索・自動読込しない。
したがってホストが何もしなければ、ユーザー環境の外部ファイルでCPU挙動が変化しない。開発WinFormsホストだけは従来どおり `evaluation_profile_v2.json` overrideを明示的に適用できる。

## 7.2 Scan-O.BlockerMaterial-1000（v0.2.37.8 active vocabulary）

| Feature | Opening ‰ | Endgame ‰ |
|---|---:|---:|
| RunnerProgress | 1520 | 780 |
| RunnerMobility | 490 | 1110 |
| BlockerMaterial | 1000 | 400 |
| FriendlyRunnerSupport | 1120 | 930 |
| FrontPressure | 1830 | 1550 |
| GoalDefense | 1240 | 770 |
| ImmediateGoalThreats | 1390 | 2320 |
| BlockerAdvancement | 1300 | 870 |
| BridgeheadConnection | 2480 | 1710 |
| RunnerGoalPath | 1500 | 1510 |
| PreparedGoalThreat | 750 | 1310 |
| UnansweredGoalThreat | 470 | 1120 |
| ConnectedGoalThreat | 600 | 1250 |
| ViableRunnerProgress | 1220 | 780 |
| SacrificeDebt | 1800 | 2600 |

これらはチューナー画面に出る‰値をそのまま設定する。raw特徴は評価器側で正規化され、評価時に `raw * permille / 1000` で適用される。

**v0.2.37.6:** `BlockerMaterial` のrawは単純な物理○枚数差ではなく実効○戦力差。相手★の最短corridorに対する防御締切へ拘束され、攻撃用犠牲stagingへのETAがそのslackを超える○をD<=4で1.0個、D=5で0.5個割引する。feature/API名は維持。旧 `Tuned-G0004` の1250/560‰は履歴値。

**v0.2.37.7:** 上記意味は不変。per-blocker reachability BFSをreverse bitboard waveへ置換し、全○を並列判定してleaf/tuner hot pathを高速化。

**v0.2.37.8:** 再スキャン済み最新15特徴を `Scan-O.BlockerMaterial-1000` として組込み既定化。BlockerMaterial重みは1000/400‰。

`GoalBridgeheads` と `RunnerCentrality` はv0.2.37.5で廃止され、公開scale/breakdownからも削除された。旧JSONに残る同名フィールドは未知プロパティとして無視される。過去プリセット名は残るが、廃止2項目を除いたactive subsetとして扱う。

## 7.3 `CpuEvaluationProfile`

```csharp
sealed record CpuEvaluationProfile(
    string Name,
    CpuEvaluationFeatureScales Opening,
    CpuEvaluationFeatureScales Endgame)
```

主要API:

```csharp
CpuEvaluationProfile profile = CpuEvaluationProfile.TunedG0015;
CpuEvaluationProfile normalized = profile.Normalize();
CpuEvaluationFeatureScales atPhase = profile.Blend(phasePermille);
```

`phasePermille` は0=opening、1000=endgameとして補間される。

## 7.4 `CpuEvaluationFeatureScales`

現在は15特徴の‰スケール。v0.2.37.5で `GoalBridgeheads` / `RunnerCentrality` を廃止し、`SacrificeDebt` を含む15特徴になった。
`Normalize()` は各値を0～3000へclampする。
0は特徴を無効化、1000が基準、負値は不可。

`RunnerGoalPath` は相手が以後動かない静的仮定の最短★ゴール経路推定。空きマスへの通常移動は上下左右、自分の○への犠牲移動は8方向、敵駒は通過不可。8手以内を評価し、経路が短い側を正値とする。

相互作用4特徴は、各プレイヤー側で0..100信号を作ってから積を取り、最後に自分側-相手側の差へする。`Tuned-G0004` では4特徴すべてに非0重みが入り、通常CPU評価へ寄与する。旧profileにこれらのフィールドが無い場合だけ0‰として互換読込する。

- `PreparedGoalThreat`: GoalPathUrgency × SacrificeChainReadiness
- `UnansweredGoalThreat`: GoalPathUrgency × (1 - opponent GoalDefenseStrength)
- `ConnectedGoalThreat`: GoalPathUrgency × BridgeheadConnectionSignal
- `ViableRunnerProgress`: RunnerProgressSignal × GoalPathUrgency

## 7.5 Process-local既定の変更

```csharp
CpuEvaluationProfileProvider.SetCurrent(customProfile, "my-host");
```

確認:

```csharp
CpuEvaluationProfile current = CpuEvaluationProfileProvider.Current;
string source = CpuEvaluationProfileProvider.CurrentSource;
```

組込み既定へ戻す:

```csharp
CpuEvaluationProfileProvider.ResetToBuiltIn();
```

`CpuEvaluationProfileProvider` は**プロセス全体で共有**される。
複数のStarRunnerインスタンスで異なる評価を同時利用したい場合はProviderを書き換えず、各 `CpuSearchOptions.EvaluationProfile` に明示指定すること。

---

## 7.6 静的評価の取得

```csharp
EvaluationBreakdown breakdown = CpuPlayer.EvaluateDetailed(
    game,
    PlayerId.Player1);
```

個別profile指定:

```csharp
EvaluationBreakdown breakdown = CpuPlayer.EvaluateDetailed(
    game,
    PlayerId.Player1,
    CpuEvaluationProfile.TunedG0015);
```

`EvaluationBreakdown`:

```csharp
readonly record struct EvaluationBreakdown(
    int Total,
    int RunnerProgress,
    int RunnerMobility,
    int BlockerMaterial,
    int FriendlyRunnerSupport,
    int FrontPressure,
    int GoalDefense,
    int ImmediateGoalThreats,
    int BlockerAdvancement,
    int BridgeheadConnection,
    int RunnerGoalPath,
    int PreparedGoalThreat,
    int UnansweredGoalThreat,
    int ConnectedGoalThreat,
    int ViableRunnerProgress,
    int PhasePermille)
{
    public int SacrificeDebt { get; init; }
}
```

`perspective` に指定したプレイヤーから見た値。
終局局面では `Total` が勝敗値になり、個別特徴は0になる。

---

# 8. Cancellation / threading

## CancellationToken

```csharp
using var cts = new CancellationTokenSource();

Task<CpuDecision> task = CpuPlayer.DecideMoveAsync(
    game.Clone(),
    game.CurrentPlayer,
    options,
    cts.Token);

// 新規対局、画面終了、局面変更など
cts.Cancel();
```

古い思考結果を新しい局面へ適用しないこと。

## 並列数

`MaxParallelism` は `CpuSearchOptions.Normalize()` で1～`Environment.ProcessorCount`へclampされる。

バックグラウンドで他処理と共存させるゲームでは全コア使用を避け、例えば:

```csharp
int aiThreads = Math.Max(1, Environment.ProcessorCount / 2);
```

程度から調整する。

## スレッド優先度

ライブラリ既定:

```csharp
UseBelowNormalThreadPriority = false
```

つまり組込み先のスレッド優先度を勝手に変更しない。
StarRunnerPrototypeホストは従来挙動維持のため明示的にtrueを指定している。

---

# 9. UIへ組み込む典型パターン

```csharp
private GameEngine _game = new();
private CancellationTokenSource? _aiCts;

private void RefreshBoard()
{
    for (int row = 0; row < GameEngine.BoardSize; row++)
    for (int col = 0; col < GameEngine.BoardSize; col++)
    {
        Piece? piece = _game.GetPiece(new Position(row, col));
        // ホスト側のSprite/UIへ変換
    }
}

private bool PlayHumanMove(Move move)
{
    if (!_game.TryApplyMove(move, out string? error))
    {
        ShowMessage(error ?? "Illegal move");
        return false;
    }

    RefreshBoard();
    HandleOutcome();
    return true;
}

private async Task PlayCpuAsync(CpuSkillProfile skill)
{
    _aiCts?.Cancel();
    _aiCts?.Dispose();
    _aiCts = new CancellationTokenSource();

    GameEngine searchPosition = _game.Clone();
    ulong rootHash = _game.GetSearchHash();

    CpuSearchOptions options = skill.ToSearchOptions(
        randomSeed: Environment.TickCount,
        maxParallelism: Math.Max(1, Environment.ProcessorCount / 2));

    try
    {
        CpuDecision decision = await CpuPlayer.DecideMoveAsync(
            searchPosition,
            searchPosition.CurrentPlayer,
            options,
            _aiCts.Token);

        // 思考中に新規対局/棋譜移動などが起きていないか確認
        if (_game.GetSearchHash() != rootHash)
            return;

        if (_game.TryApplyMove(decision.Move, out _))
        {
            RefreshBoard();
            HandleOutcome();
        }
    }
    catch (OperationCanceledException)
    {
        // 正常なキャンセルとして扱う
    }
}
```

上記 `ShowMessage`, `HandleOutcome`, 描画処理はホスト固有実装。
Core/AIはUIスレッドへ直接アクセスしない。

---

# 10. セーブデータへ組み込む典型パターン

ホスト側セーブDTO例:

```csharp
public sealed record MyGameSave(
    int SaveVersion,
    GameState StarRunner,
    string HostScene,
    int HostScore);
```

保存:

```csharp
var save = new MyGameSave(
    SaveVersion: 1,
    StarRunner: game.ExportState(),
    HostScene: currentScene,
    HostScore: score);
```

復元:

```csharp
GameEngine game = GameEngine.FromState(save.StarRunner);
```

`GameState` の内部フィールドをホスト側で手編集・再構築するより、CoreのExport/Importをそのまま保持することを推奨。

---

# 11. 組込み時にやってはいけないこと

1. **盤面文字列だけで途中局面を復元する。** 反復・即時戻り履歴を失う。
2. **ホスト側で合法手/勝敗を独自再実装する。** Coreとの不一致原因になる。
3. **AI思考中の同じ `GameEngine` を別スレッドから変更する。** 非同期AIには `Clone()` を渡す。
4. **`GetSearchHash()` を永続セーブIDとして扱う。** 探索用であり永続形式ではない。
5. **`CpuEvaluationProfileProvider.SetCurrent()` を局ごとの設定として乱用する。** process-global。局ごとなら `CpuSearchOptions.EvaluationProfile`。
6. **internal APIへ依存する。** `InternalsVisibleTo` は開発ホスト/AI向けだけで、外部互換性を保証しない。
7. **MovePolicyをStandardルールの一部として扱う。** policyはホスト固有追加制約。

---

# 12. 推奨の責務分担

| 処理 | 担当 |
|---|---|
| 盤面・手番・合法手 | `StarRunner.Core` |
| 強制退避・即時戻り・千日手 | `StarRunner.Core` |
| 勝敗判定 | `StarRunner.Core` |
| CPU探索 | `StarRunner.AI` |
| CPU棋力表 | `StarRunner.AI` |
| 評価プロファイル | `StarRunner.AI` |
| 描画・入力 | 組込みホスト |
| SE/BGM/演出 | 組込みホスト |
| セーブファイル自体の配置/I/O | 組込みホスト |
| StarRunner `GameState` の生成/復元 | `StarRunner.Core` |
| CPU profile外部JSON I/O | 必要なら組込みホスト |
| ローカライズ | 組込みホスト |

---

# 13. 最低限の組込み受入テスト

別ゲームへ接続したら、少なくとも以下を確認する。

1. `new GameEngine()` で初期盤面が表示できる。
2. `GetLegalMoves()` の手をすべてUIで選択可能。
3. 非合法なUI入力が `TryApplyMove` で拒否される。
4. 1手後に手番が正しく交代する。
5. `Clone()` 側へ着手しても元局面が変化しない。
6. `ExportState()` → JSON → `FromState()` 後に盤面、手番、合法手が一致する。
7. 保存復元後も即時戻り制限が一致する。
8. 四回同一局面が保存復元をまたいでも成立する。
9. 強制退避局面でRunner以外を選べない。
10. `CpuPlayer.DecideMoveAsync()` の返した手を元局面へ合法に適用できる。
11. CPU思考中キャンセルが新局面へ漏れない。
12. `CpuEvaluationProfile.BuiltInDefault.Name == "Scan-O.BlockerMaterial-1000"`。

開発パッケージ内のより詳細な検証は `TEST_PLAN.md` とCorrectness Verifierを参照。

---

# 14. 関連文書

- `CORE_INTEGRATION.md` — 最短の導入ガイド。
- `STANDARD_RULES.md` — ゲームルールの正本。
- `CPU_SKILL_LEVELS.md` — 20段階CPU強度。
- `EVALUATION_TUNING.md` — 評価特徴とチューニング。
- `TEST_PLAN.md` — 実機・回帰テスト。
- `StarRunner.Core/README.md` — Core概要。
- `StarRunner.AI/README.md` — AI概要。

このリファレンスは **v0.2.37.1 の公開API** に対応する。


### v0.2.36.8 adaptive root deepening

rootで他候補がすべてforced-loss帯と証明された場合、dead枝のupper boundを再利用し、唯一生存枝のみを1plyずつ最大+6plyまで深掘りする。内部ノードのextension budgetは2plyのまま。Mate確定・予算枯渇・優位崩れで停止する。`CpuSearchTelemetry.AdaptiveRootDeepeningPasses` / `MaxAdaptiveRootDeepeningPlyReached` で診断可能。


### v0.2.37.0 budget-aware Mate-distance Scout

Node予算駆動で次の通常Depth完走が残予算に収まりにくいと予測した場合、残Nodeを強制勝敗のproof searchへ回す。ScoutはD1から1plyずつ検査し、非終局depth=0を`0`（unknown）として静的評価を使わない。LMR / only-survival / adaptive extensionは無効、専用TTを使用する。最初に証明したDをmate distanceとして返し、未証明なら通常探索の最後の完了Depthを維持する。Scoutが設定深さ上限まで安価に走り切ってNodeが残れば通常反復深化へ復帰する。


### v0.2.37.1 directed parity Mate-distance Scout

v0.2.37.0のD1から勝ち/負け双方を毎depth probeする方式を廃止。通常探索の最終score符号から一方向だけを選び、Standard ruleの終局parity（root勝ち=奇数ply / root負け=偶数ply）に従って、通常完了Depthの次の該当parityから+2plyずつ深掘りする。例: 通常D9・負評価ならLoss D10→D12→D14→…。最初のcoarse probeで既に証明された場合だけ、より短いmateを取りこぼさないためlegal parity上で距離確定probeを行う。距離確定probeは深いTTを浅いhorizonへ流用しないようfresh Scout TTを使う。

追加telemetry: `MateDistanceScoutMaxCompletedDepth`, `MateDistanceScoutDirection`, `MateDistanceScoutProbeDetails`。既存 `MateDistanceScoutMaxDepthReached` は「着手最大D」の意味で維持。`CpuMateScoutProbeTelemetry` は各probeのDepth/方向/距離確定phase/完了/証明/Nodesを保持する。
