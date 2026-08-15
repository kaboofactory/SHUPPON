# SHUPPON — original C# implementation (v0.2.37.8)

This directory contains the original Windows/C# implementation that the standalone JavaScript version was ported from.

## Build

- Host: Windows Forms, `.NET 10.0-windows`
- Reusable game logic: `StarRunner.Core` (`.NET 10.0`)
- CPU engine: `StarRunner.AI` (`.NET 10.0`)
- Solution: `StarRunnerPrototype.sln`
- Project: `StarRunnerPrototype.csproj`

The internal C# project/namespace names remain `StarRunner` to preserve the frozen v0.2.37.8 source. The current user-facing game name is **SHUPPON**.

## Current CPU baseline

- Built-in evaluation profile: `Scan-O.BlockerMaterial-1000`
- CPU levels: 15級 through 五段 (20 levels)
- Standard search remains MaxNodes-driven.
- The original C# implementation is the reference source for game rules and CPU behavior.

## Useful documents retained

- `STANDARD_RULES.md` — rules
- `CPU_SKILL_LEVELS.md` — CPU levels / node budgets
- `EVALUATION_TUNING.md` — evaluator and tuner
- `TEST_PLAN.md` — tests and regression procedure
- `CORE_INTEGRATION.md` / `EMBEDDING_REFERENCE.md` — Core/AI API and embedding details
- `LOG_FORMAT.md` / `SCENARIO_LAB.md` — diagnostics and scenarios
- `SAVE_OPEN_RESUME_IMPLEMENTATION.md` — state persistence notes
- `regression/` — only the latest useful regression snapshots retained

Historical per-version changelogs, old static-audit reports, superseded handoff notes, and large analysis logs were intentionally removed from this package because they are no longer required for active development.
