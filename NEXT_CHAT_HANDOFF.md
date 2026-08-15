# SHUPPON — NEXT CHAT HANDOFF

## Current package

This package contains both implementations:

1. `SHUPPON.html` — current standalone vanilla-JavaScript browser build.
2. `CSharp/` — frozen original C# v0.2.37.8 reference implementation.

The user-facing game name is **SHUPPON**. The C# project and namespaces remain `StarRunner` intentionally so the frozen reference source is not needlessly renamed.

## Web/JavaScript build

Scope is deliberately limited to the main-screen game mode:

- Human = P1 / blue, fixed.
- CPU = P2 / red, fixed.
- CPU levels 15級 through 五段 (20 levels) are retained.
- Vanilla JavaScript only: no npm, TypeScript, React, framework, or server dependency.
- One standalone `SHUPPON.html`; double-click/open directly with `file://`.
- CPU search runs in a Web Worker created from the HTML so UI rendering remains responsive.
- Single search worker only. Multicore root split was not added because a faithful shared node-budget/TT implementation would materially increase complexity and can require cross-origin-isolation facilities for efficient shared memory.
- Responsive mobile layout is implemented.
- Board and every cell remain square at all viewport sizes.
- Circle pieces are intentionally larger than the star pieces.
- Rules are shown in the page.
- Move-list autoscroll is confined to the move-list element and must not move the whole page.

## JavaScript CPU engine

The first faithful port used BigInt heavily and had low NPS. The current engine was substantially rewritten for JavaScript performance.

Hot search paths use paired 32-bit integers rather than BigInt for the important 64-bit data structures:

- blocker bitboards
- Zobrist position hashes
- transposition-table keys
- repetition/history keys
- route/wave calculations used by evaluation

Move generation, runner-mobility terminal checks, path evaluation, and move ordering were also specialized for the 32-bit-pair representation.

BigInt may still exist in non-hot initialization/compatibility helpers; it is not intended to be used per-node in the main PVS search path.

## Faithfulness/regression status

Known C# reference position 76 was used repeatedly during the port.

Expected reference values:

- legal moves: 25
- static evaluation (P1): -313
- D1: -104, `C6-D7`, 26 cumulative nodes
- D2: -475, `C6-D7`, 114
- D3: -93, `B4-C3`, 984
- D4: -190, `B4-C3`, 3,096
- D5: -78, `B4-C3`, 26,678
- D6: -164, `C6-D7`, 119,456
- D7: -74, `B4-C3`, 501,503

During the 32-bit rewrite, these remained identical in best move, score, and cumulative node count. Random-state comparisons were also run between the previous faithful JS engine and the 32-bit engine for legal moves/evaluation/history, plus D4 search comparisons on multiple positions.

The retained C# reference snapshot is `CSharp/regression/position76_v0.2.37.8_latest_profile_result.txt`.

## C# reference

Do not simplify or change C# behavior merely to make the JavaScript implementation easier. The C# version is the behavioral reference.

Important locations:

- `CSharp/StarRunner.Core/GameEngine.cs` — standard game rules/state behavior
- `CSharp/StarRunner.AI/CpuPlayer.cs` — CPU search
- `CSharp/StarRunner.AI/CpuEvaluationProfile.cs` — evaluator profile
- `CSharp/StarRunner.AI/CpuSkillProfile.cs` — 20 skill levels
- `CSharp/MainForm.cs` — original Human-vs-CPU host behavior/UI reference
- `CSharp/STANDARD_RULES.md` — rules
- `CSharp/TEST_PLAN.md` — regression/testing reference

Current built-in evaluation profile is `Scan-O.BlockerMaterial-1000`.

## Package cleanup performed

The original source ZIP accumulated many historical artifacts. This package intentionally removes:

- all per-version `CHANGELOG_v*.md`
- all historical `STATIC_AUDIT*.md`
- old `HANDOFF_STATUS*` / `HANDOFF_UPDATE*`
- superseded build-fix and old web-handoff notes
- obsolete baseline/handoff/package-list markdown files
- large historical `analysis_samples/*.jsonl`
- old one-off regression/log snapshots

Useful current technical documentation, source code, scenarios, and two current regression snapshots remain.

## Next-development rule

Before changing rules, CPU evaluation/search semantics, skill node budgets, or draw/repetition behavior, compare against the C# reference. Performance-only JavaScript changes should preserve best move, score, and node count on deterministic regression cases whenever possible.
