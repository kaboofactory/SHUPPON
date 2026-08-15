# StarRunner.Core

StarRunner StandardルールのUI非依存ランタイム。

- Target: `net10.0`
- Assembly / namespace: `StarRunner.Core`
- WinForms dependency: なし
- CPU dependency: なし
- File I/O: なし

主な公開API:

- `GameEngine`
- `GameStartConfiguration`
- `GameState`
- `PlayerId`, `Piece`, `Move`, `GameOutcome`, `EndReason`
- `IGameMovePolicy`（任意のホスト拡張）

CPUが必要な場合は別プロジェクト `StarRunner.AI` を参照する。
詳細はルートの `CORE_INTEGRATION.md`。
詳細な公開APIリファレンス: `../EMBEDDING_REFERENCE.md`
