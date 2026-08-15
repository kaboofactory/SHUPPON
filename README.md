# SHUPPON

ブラウザですぐに遊べる、1人用のボードゲームです。
青い「★」を相手側の最上段まで進めて勝利を目指します。対戦相手は、15級から五段までの20段階から選べるCPUです。

## プレイする

インストールやサーバーは必要ありません。

1. [SHUPPON.html](./SHUPPON.html) をダウンロードする
2. ブラウザでファイルを開く
3. CPUの棋力を選び、「新規対局」から開始する

GitHub上では、ファイルを開いて表示された `Raw` ページをブラウザで開いてもプレイできます。Web Workerが利用できない環境では、互換モードでCPUが動作します。

## ルール

- 盤面は8×8マスです。
- プレイヤー1（青）は下段、プレイヤー2（赤）は上段から開始します。
- 各プレイヤーは、★1個と○6個を持ちます。
- ○は、空いている隣接8方向へ1マス移動できます。
- ★は、空いている上下左右へ1マス移動できます。
- ★は、隣接する自分の○へ移動し、その○を犠牲にすることもできます。
- ★を相手側の最終列まで到達させると勝利です。
- ★が相手の駒に四方を囲まれ、退避できない場合は敗北です。
- 盤面配置が同じ状態を4回繰り返すと引き分けです。

詳細なルールは [CSharp/STANDARD_RULES.md](./CSharp/STANDARD_RULES.md) を参照してください。

## ブラウザ版の特徴

- HTMLファイル1つで動作
- フレームワーク、npm、TypeScript、外部ライブラリ不要
- 人間（P1・青）対CPU（P2・赤）
- CPU棋力は15級から五段までの20レベル
- CPU探索の深さ、評価値、ノード数、探索速度を表示
- CPU思考をWeb Workerで実行
- 棋譜表示、局面評価、対局のリセットに対応
- オフラインでプレイ可能

## リポジトリ構成

```text
.
├── SHUPPON.html                  # すぐに遊べるスタンドアロン版
├── CSharp/                       # 元実装と開発用ドキュメント
│   ├── StarRunnerPrototype.sln   # C#ソリューション
│   ├── StarRunnerPrototype.csproj
│   ├── StarRunner.Core/           # ゲームルール・盤面モデル
│   ├── StarRunner.AI/             # CPU探索・評価エンジン
│   ├── scenarios/                 # シナリオ検証用JSON
│   ├── regression/                # 回帰検証結果
│   └── *.md                       # ルール、設計、テスト等の資料
├── NEXT_CHAT_HANDOFF.md           # 開発状況の引き継ぎメモ
└── .gitignore
```

C#版の内部プロジェクト名や名前空間には、移植元の都合で `StarRunner` が残っています。プレイヤー向けのゲーム名は **SHUPPON** です。

## C#版をビルドする

C#版はWindows Formsによる開発・検証用の実装です。`.NET 10 SDK` が必要です。

```powershell
cd CSharp
dotnet build StarRunnerPrototype.sln -c Release
```

または、Windowsでは次のスクリプトを実行できます。

```powershell
cd CSharp
.\build.cmd
```

C#版の構成は次のとおりです。

- `StarRunner.Core` — UIに依存しないゲームルールと盤面処理（`.NET 10`）
- `StarRunner.AI` — CPU探索と評価処理（`.NET 10`）
- `StarRunnerPrototype` — Windows Formsの対局画面と検証ツール（`.NET 10-windows`）

## 開発資料

- [標準ルール](./CSharp/STANDARD_RULES.md)
- [CPU棋力レベル](./CSharp/CPU_SKILL_LEVELS.md)
- [テスト計画](./CSharp/TEST_PLAN.md)
- [シナリオ検証](./CSharp/SCENARIO_LAB.md)
- [Core/AIの統合情報](./CSharp/CORE_INTEGRATION.md)
- [C#版のREADME](./CSharp/README.md)

## 技術メモ

ブラウザ版は、C#版のゲームロジックとCPUエンジンをJavaScriptへ移植したものです。検索処理はWeb Worker内で実行し、CPU思考中も画面操作を止めにくい構成にしています。

## ライセンス

このプロジェクトは [MIT License](./LICENSE) のもとで公開しています。
