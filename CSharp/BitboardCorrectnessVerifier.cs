using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace StarRunnerPrototype;

internal sealed record BitboardVerifierOptions(
    int Seeds = 24,
    int MaxPliesPerSeed = 220,
    int BaseSeed = 0x51A7_2026,
    int ApplyUndoStride = 4,
    int CloneStride = 8);

internal sealed record BitboardVerifierResult(
    bool Passed,
    int CheckedPositions,
    int MoveSetChecks,
    int ApplyUndoChecks,
    int CloneChecks,
    int ImmediateBacktrackChecks,
    int ForcedRetreatChecks,
    int RepetitionChecks,
    int StateRoundtripChecks,
    TimeSpan Elapsed,
    string Details)
{
    public string ToUserSummary()
    {
        string result = Passed ? "PASS" : "FAIL";
        return $"Bitboard correctness verifier: {result}\r\n\r\n" +
               $"局面照合: {CheckedPositions:N0}\r\n" +
               $"合法手集合照合: {MoveSetChecks:N0}\r\n" +
               $"Apply→Undo: {ApplyUndoChecks:N0}\r\n" +
               $"Clone: {CloneChecks:N0}\r\n" +
               $"即時戻り専用: {ImmediateBacktrackChecks:N0}\r\n" +
               $"包囲時強制退避専用: {ForcedRetreatChecks:N0}\r\n" +
               $"千日手専用: {RepetitionChecks:N0}\r\n" +
               $"状態保存/復元: {StateRoundtripChecks:N0}\r\n" +
               $"経過: {Elapsed.TotalSeconds:F3} 秒\r\n\r\n" +
               Details;
    }
}

internal static class BitboardCorrectnessVerifier
{
    private static readonly (int dr, int dc)[] EightDirections =
    {
        (-1, -1), (-1, 0), (-1, 1),
        (0, -1),            (0, 1),
        (1, -1),  (1, 0),  (1, 1)
    };

    private static readonly (int dr, int dc)[] OrthogonalDirections =
    {
        (-1, 0), (0, -1), (0, 1), (1, 0)
    };

    public static BitboardVerifierResult Run(BitboardVerifierOptions? options = null)
    {
        options ??= new BitboardVerifierOptions();
        var stopwatch = Stopwatch.StartNew();
        int checkedPositions = 0;
        int moveSetChecks = 0;
        int applyUndoChecks = 0;
        int cloneChecks = 0;
        int immediateBacktrackChecks = 0;
        int forcedRetreatChecks = 0;
        int repetitionChecks = 0;
        int stateRoundtripChecks = 0;

        try
        {
            immediateBacktrackChecks += VerifyImmediateBacktrackRule(
                ref checkedPositions,
                ref moveSetChecks,
                ref applyUndoChecks,
                ref cloneChecks);
            forcedRetreatChecks += VerifyForcedRetreatRule(ref checkedPositions, ref moveSetChecks);
            repetitionChecks += VerifyFourfoldBoardRepetition(ref checkedPositions, ref moveSetChecks);
            stateRoundtripChecks += VerifyStateExportImport(ref checkedPositions, ref moveSetChecks);
            stateRoundtripChecks += VerifyRealRunnerHistoryRoundtrip(ref checkedPositions, ref moveSetChecks);
            stateRoundtripChecks += VerifyMovePolicyExportImport(ref checkedPositions, ref moveSetChecks);

            for (int seedIndex = 0; seedIndex < options.Seeds; seedIndex++)
            {
                int seed = unchecked(options.BaseSeed + seedIndex * 7_919);
                var random = new Random(seed);
                var game = new GameEngine();

                for (int localPly = 0; localPly <= options.MaxPliesPerSeed; localPly++)
                {
                    VerifyPosition(game, $"standard seed={seed} localPly={localPly}");
                    checkedPositions++;
                    moveSetChecks += 2;

                    if (game.Outcome != GameOutcome.Ongoing || localPly == options.MaxPliesPerSeed)
                    {
                        break;
                    }

                    IReadOnlyList<Move> legal = game.GetLegalMoves();
                    if (legal.Count == 0)
                    {
                        throw Failure(game, $"Ongoing position unexpectedly has zero legal moves. seed={seed}, localPly={localPly}");
                    }

                    Move move = legal[random.Next(legal.Count)];

                    if (options.ApplyUndoStride > 0 && checkedPositions % options.ApplyUndoStride == 0)
                    {
                        GameEngineVerificationSnapshot before = game.GetVerificationSnapshot();
                        GameEngine.SearchUndo undo = game.ApplyGeneratedMoveForSearch(move);
                        VerifyPosition(game, $"after search-apply seed={seed} localPly={localPly} move={move.ToNotation()}");
                        checkedPositions++;
                        moveSetChecks += 2;
                        game.UndoSearchMove(undo);
                        GameEngineVerificationSnapshot afterUndo = game.GetVerificationSnapshot();
                        if (before != afterUndo)
                        {
                            throw Failure(game,
                                "Apply→Undo did not restore the complete engine state.\n" +
                                DescribeSnapshotDifference(before, afterUndo));
                        }
                        applyUndoChecks++;
                    }

                    if (options.CloneStride > 0 && checkedPositions % options.CloneStride == 0)
                    {
                        GameEngine clone = game.Clone();
                        GameEngineVerificationSnapshot originalSnapshot = game.GetVerificationSnapshot();
                        GameEngineVerificationSnapshot cloneSnapshot = clone.GetVerificationSnapshot();
                        if (originalSnapshot != cloneSnapshot)
                        {
                            throw Failure(game,
                                "Clone state differs from original.\n" +
                                DescribeSnapshotDifference(originalSnapshot, cloneSnapshot));
                        }
                        VerifyPosition(clone, $"clone seed={seed} localPly={localPly}");
                        cloneChecks++;
                    }

                    if (!game.TryApplyMove(move, out string? error))
                    {
                        throw Failure(game, $"Reference-selected random legal move could not be applied: {move.ToNotation()} / {error}");
                    }
                }
            }

            stopwatch.Stop();
            return new BitboardVerifierResult(
                true,
                checkedPositions,
                moveSetChecks,
                applyUndoChecks,
                cloneChecks,
                immediateBacktrackChecks,
                forcedRetreatChecks,
                repetitionChecks,
                stateRoundtripChecks,
                stopwatch.Elapsed,
                "Standard固定ルールで旧64マス走査ReferenceとBitboard版の差異は検出されませんでした。");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new BitboardVerifierResult(
                false,
                checkedPositions,
                moveSetChecks,
                applyUndoChecks,
                cloneChecks,
                immediateBacktrackChecks,
                forcedRetreatChecks,
                repetitionChecks,
                stateRoundtripChecks,
                stopwatch.Elapsed,
                ex.ToString());
        }
    }

    public static string WriteReport(BitboardVerifierResult result)
    {
        string directory = ResolveReportDirectory();
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"bitboard_verifier_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt");
        File.WriteAllText(path, result.ToUserSummary(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static int VerifyStateExportImport(ref int checkedPositions, ref int moveSetChecks)
    {
        var original = new GameEngine();
        for (int ply = 0; ply < 10 && original.Outcome == GameOutcome.Ongoing; ply++)
        {
            IReadOnlyList<Move> legal = original.GetLegalMoves();
            if (legal.Count == 0) throw Failure(original, "State roundtrip setup unexpectedly had no legal moves.");
            Move selected = legal[(ply * 5 + 1) % legal.Count];
            if (!original.TryApplyMove(selected, out string? error))
            {
                throw Failure(original, $"State roundtrip setup move failed: {selected.ToNotation()} / {error}");
            }
        }

        GameState exported = original.ExportState();
        string json = JsonSerializer.Serialize(exported);
        GameState deserialized = JsonSerializer.Deserialize<GameState>(json)
            ?? throw Failure(original, "GameState JSON roundtrip returned null.");
        GameEngine restored = GameEngine.FromState(deserialized);

        GameEngineVerificationSnapshot before = original.GetVerificationSnapshot();
        GameEngineVerificationSnapshot after = restored.GetVerificationSnapshot();
        if (before != after)
        {
            throw Failure(restored,
                "ExportState -> JSON -> FromState did not restore the complete rule/search identity.\n" +
                DescribeSnapshotDifference(before, after));
        }

        GameState restoredExport = restored.ExportState();
        if (!DictionaryEqual(exported.RepetitionCounts, restoredExport.RepetitionCounts) ||
            !DictionaryEqual(exported.PhysicalPositionCounts, restoredExport.PhysicalPositionCounts) ||
            exported.Player1LastRealRunnerMove != restoredExport.Player1LastRealRunnerMove ||
            exported.Player2LastRealRunnerMove != restoredExport.Player2LastRealRunnerMove)
        {
            throw Failure(restored, "Export/import did not preserve repetition, AI physical-position, or real Runner-move history.");
        }

        VerifyPosition(restored, "state export/import roundtrip");
        checkedPositions++;
        moveSetChecks += 2;

        if (original.Outcome == GameOutcome.Ongoing)
        {
            IReadOnlyList<Move> legalOriginal = original.GetLegalMoves();
            IReadOnlyList<Move> legalRestored = restored.GetLegalMoves();
            CompareMoves(restored, legalOriginal, legalRestored, "post-restore continuation");
            Move continuation = legalOriginal[0];
            bool originalApplied = original.TryApplyMove(continuation, out string? originalError);
            bool restoredApplied = restored.TryApplyMove(continuation, out string? restoredError);
            if (!originalApplied || !restoredApplied)
            {
                throw Failure(restored,
                    $"Post-restore continuation failed. original={originalError ?? "ok"}, restored={restoredError ?? "ok"}");
            }
            GameEngineVerificationSnapshot continuedOriginal = original.GetVerificationSnapshot();
            GameEngineVerificationSnapshot continuedRestored = restored.GetVerificationSnapshot();
            if (continuedOriginal != continuedRestored)
            {
                throw Failure(restored,
                    "Restored engine diverged after one continuation move.\n" +
                    DescribeSnapshotDifference(continuedOriginal, continuedRestored));
            }
            checkedPositions++;
            moveSetChecks += 2;
        }

        return 1;
    }

    private static int VerifyRealRunnerHistoryRoundtrip(ref int checkedPositions, ref int moveSetChecks)
    {
        var start = GameStartConfiguration.Create(
            "Runner real-history verifier",
            "correctness-verifier",
            new[]
            {
                "o.......",
                "........",
                "........",
                "........",
                "...s....",
                "........",
                "....S...",
                ".......O"
            },
            PlayerId.Player2);
        var game = new GameEngine(start);

        static void ApplyOrThrow(GameEngine g, Move move, string label)
        {
            if (!g.TryApplyMove(move, out string? error))
            {
                throw Failure(g, $"Runner history setup failed at {label}: {move.ToNotation()} / {error}");
            }
        }

        Move firstRunnerMove = new(new Position(4, 3), new Position(4, 2), MoveKind.Normal); // D5-C5
        ApplyOrThrow(game, firstRunnerMove, "P2 Runner D5-C5");
        ApplyOrThrow(game, new Move(new Position(6, 4), new Position(5, 4), MoveKind.Normal), "P1 Runner E7-E6");
        ApplyOrThrow(game, new Move(new Position(0, 0), new Position(1, 0), MoveKind.Normal), "P2 blocker A1-A2");
        ApplyOrThrow(game, new Move(new Position(5, 4), new Position(4, 4), MoveKind.Normal), "P1 Runner E6-E5");

        Move returnMove = new(new Position(4, 2), new Position(4, 3), MoveKind.Normal); // C5-D5
        if (!game.IsLegalMove(returnMove))
        {
            throw Failure(game, "Runner return verifier expected C5-D5 to be legal after an intervening P2 blocker move.");
        }
        if (!game.IsRealRunnerReturnMove(PlayerId.Player2, returnMove))
        {
            throw Failure(game, "C5-D5 was not recognized as reversing the last real P2 Runner move D5-C5.");
        }
        if (game.GetLastRealRunnerMove(PlayerId.Player2) != firstRunnerMove)
        {
            throw Failure(game, "Intervening blocker move erased the remembered real P2 Runner move.");
        }

        Move searchOnlyRunnerMove = new(new Position(4, 2), new Position(5, 2), MoveKind.Normal); // C5-C6
        if (game.IsLegalMove(searchOnlyRunnerMove))
        {
            Move? beforeSearchHistory = game.GetLastRealRunnerMove(PlayerId.Player2);
            GameEngine.SearchUndo undo = game.ApplyGeneratedMoveForSearch(searchOnlyRunnerMove);
            if (game.GetLastRealRunnerMove(PlayerId.Player2) != beforeSearchHistory)
            {
                throw Failure(game, "Search-only Runner move polluted the real-game Runner history.");
            }
            game.UndoSearchMove(undo);
            if (game.GetLastRealRunnerMove(PlayerId.Player2) != beforeSearchHistory)
            {
                throw Failure(game, "Undo changed the real-game Runner history.");
            }
        }

        GameState exported = game.ExportState();
        string json = JsonSerializer.Serialize(exported);
        GameState deserialized = JsonSerializer.Deserialize<GameState>(json)
            ?? throw Failure(game, "Runner history GameState JSON roundtrip returned null.");
        GameEngine restored = GameEngine.FromState(deserialized);
        if (restored.GetLastRealRunnerMove(PlayerId.Player2) != firstRunnerMove ||
            !restored.IsRealRunnerReturnMove(PlayerId.Player2, returnMove))
        {
            throw Failure(restored, "GameState roundtrip did not preserve the real Runner history / return detection.");
        }

        VerifyPosition(restored, "real Runner history roundtrip");
        checkedPositions++;
        moveSetChecks += 2;
        return 1;
    }

    private static int VerifyMovePolicyExportImport(ref int checkedPositions, ref int moveSetChecks)
    {
        var policy = new ScenarioMovePolicy(
            new PlayerStrategyConstraint(StrategyMode.RushOne, new Position(7, 3), 1),
            PlayerStrategyConstraint.Free);
        var original = new GameEngine(GameStartConfiguration.Initial, policy);
        var attackMove = new Move(new Position(7, 3), new Position(6, 3), MoveKind.Normal);
        if (!original.TryApplyMove(attackMove, out string? attackError))
        {
            throw Failure(original, $"Policy roundtrip setup could not move attack blocker: {attackError}");
        }

        if (original.Outcome == GameOutcome.Ongoing)
        {
            IReadOnlyList<Move> reply = original.GetLegalMoves();
            if (reply.Count == 0) throw Failure(original, "Policy roundtrip setup unexpectedly had no P2 reply.");
            if (!original.TryApplyMove(reply[0], out string? replyError))
            {
                throw Failure(original, $"Policy roundtrip setup P2 reply failed: {replyError}");
            }
        }

        GameState exported = original.ExportState();
        string json = JsonSerializer.Serialize(exported);
        GameState deserialized = JsonSerializer.Deserialize<GameState>(json)
            ?? throw Failure(original, "Policy GameState JSON roundtrip returned null.");
        GameEngine restored = GameEngine.FromState(deserialized, policy);

        GameEngineVerificationSnapshot before = original.GetVerificationSnapshot();
        GameEngineVerificationSnapshot after = restored.GetVerificationSnapshot();
        if (before != after)
        {
            throw Failure(restored,
                "MovePolicy ExportState -> JSON -> FromState did not restore engine identity.\n" +
                DescribeSnapshotDifference(before, after));
        }
        CompareMoves(restored, original.GetLegalMoves(), restored.GetLegalMoves(), "policy post-restore legal moves");
        if (policy.GetAttackBlockerPosition(restored, PlayerId.Player1) != new Position(6, 3))
        {
            throw Failure(restored, "MovePolicyState did not preserve the moved RushOne attack blocker.");
        }

        checkedPositions++;
        moveSetChecks++;
        return 1;
    }

    private static bool DictionaryEqual(
        IReadOnlyDictionary<ulong, int> left,
        IReadOnlyDictionary<ulong, int> right)
    {
        if (left.Count != right.Count) return false;
        foreach (KeyValuePair<ulong, int> pair in left)
        {
            if (!right.TryGetValue(pair.Key, out int value) || value != pair.Value) return false;
        }
        return true;
    }

    private static int VerifyForcedRetreatRule(ref int checkedPositions, ref int moveSetChecks)
    {
        GameStartConfiguration retreatStart = GameStartConfiguration.Create(
            "forced retreat from four-side surround",
            "built-in verifier",
            new[]
            {
                "....s...",
                "........",
                "...o....",
                "..oSo...",
                "...oO...",
                "........",
                "........",
                "........"
            },
            PlayerId.Player1);
        var retreat = new GameEngine(retreatStart);

        Move expectedRetreat = new(new Position(3, 3), new Position(4, 4), MoveKind.Sacrifice); // D4xE5
        Move forbiddenBlockerMove = new(new Position(4, 4), new Position(5, 5), MoveKind.Normal); // E5-F6
        IReadOnlyList<Move> legal = retreat.GetLegalMoves(PlayerId.Player1);
        if (retreat.Outcome != GameOutcome.Ongoing ||
            !retreat.IsRunnerForcedToRetreat(PlayerId.Player1) ||
            legal.Count != 1 || legal[0] != expectedRetreat ||
            retreat.IsLegalMove(forbiddenBlockerMove))
        {
            throw Failure(retreat,
                $"Forced-retreat move restriction failed. outcome={retreat.Outcome}/{retreat.EndReason}, " +
                $"forced={retreat.IsRunnerForcedToRetreat(PlayerId.Player1)}, " +
                $"legal=[{string.Join(", ", legal.Select(m => m.ToNotation()))}]");
        }

        VerifyPosition(retreat, "dedicated forced-retreat state");
        checkedPositions++;
        moveSetChecks += 2;

        if (!retreat.TryApplyMove(expectedRetreat, out string? retreatError))
        {
            throw Failure(retreat, $"Could not apply mandatory retreat: {retreatError}");
        }
        if (retreat.CountBlockers(PlayerId.Player1) != 0 || retreat.FindRunner(PlayerId.Player1) != new Position(4, 4))
        {
            throw Failure(retreat, "Mandatory retreat did not sacrifice the destination blocker correctly.");
        }

        GameStartConfiguration noRetreatStart = GameStartConfiguration.Create(
            "four-side surround without retreat",
            "built-in verifier",
            new[]
            {
                "....s...",
                "........",
                "...o....",
                "..oSo...",
                "...o....",
                "........",
                "........",
                "........"
            },
            PlayerId.Player1);
        var noRetreat = new GameEngine(noRetreatStart);
        if (noRetreat.Outcome != GameOutcome.Player2Win || noRetreat.EndReason != EndReason.RunnerImmobilized)
        {
            throw Failure(noRetreat,
                $"Surrounded Runner without retreat should lose. actual={noRetreat.Outcome}/{noRetreat.EndReason}");
        }
        checkedPositions++;
        moveSetChecks += 2;

        return 2;
    }

    private static int VerifyImmediateBacktrackRule(
        ref int checkedPositions,
        ref int moveSetChecks,
        ref int applyUndoChecks,
        ref int cloneChecks)
    {
        var game = new GameEngine();

        Move p1Forward = new(new Position(7, 1), new Position(6, 1), MoveKind.Normal); // B8-B7
        Move p2Forward = new(new Position(0, 0), new Position(1, 0), MoveKind.Normal); // A1-A2
        if (!game.TryApplyMove(p1Forward, out string? p1Error))
        {
            throw Failure(game, $"Could not construct immediate-backtrack state: {p1Forward.ToNotation()} / {p1Error}");
        }
        if (!game.TryApplyMove(p2Forward, out string? p2Error))
        {
            throw Failure(game, $"Could not construct immediate-backtrack state: {p2Forward.ToNotation()} / {p2Error}");
        }

        VerifyPosition(game, "dedicated immediate-backtrack state");
        checkedPositions++;
        moveSetChecks += 2;

        Move forbiddenReverse = new(new Position(6, 1), new Position(7, 1), MoveKind.Normal); // B7-B8
        ReferenceMoveResult reference = GenerateReferenceMoves(game, PlayerId.Player1, applyStrategyRestrictions: true);
        if (!reference.ImmediateBacktrackRejectedMoves.Contains(forbiddenReverse))
        {
            throw Failure(game, "Reference implementation did not identify expected immediate reverse B7-B8.");
        }
        if (game.GetLegalMoves(PlayerId.Player1).Contains(forbiddenReverse))
        {
            throw Failure(game, "Bitboard move generation incorrectly allowed immediate reverse B7-B8.");
        }

        // Same board + same side, but no previous-own-move state. The repetition key must
        // stay equal because repetition compares board placement only; search hash must differ.
        GameStartConfiguration freshStart = GameStartConfiguration.Create(
            "immediate-backtrack hash comparison",
            "built-in verifier",
            BoardRows(game),
            game.CurrentPlayer);
        var freshSameBoard = new GameEngine(freshStart);
        GameEngineVerificationSnapshot progressed = game.GetVerificationSnapshot();
        GameEngineVerificationSnapshot fresh = freshSameBoard.GetVerificationSnapshot();

        if (!freshSameBoard.GetLegalMoves(PlayerId.Player1).Contains(forbiddenReverse))
        {
            throw Failure(game, "Comparison setup failed: B7-B8 should be legal without previous-own-move state.");
        }
        if (progressed.PositionHash != fresh.PositionHash)
        {
            throw Failure(game, "Comparison setup failed: identical board/current player produced different PositionHash.");
        }
        if (progressed.BacktrackStateHash == fresh.BacktrackStateHash)
        {
            throw Failure(game, "Previous-own-move state did not change BacktrackStateHash.");
        }
        if (progressed.RepetitionStateHash != fresh.RepetitionStateHash)
        {
            throw Failure(game, "Repetition key incorrectly depends on previous-own-move state.");
        }
        if (progressed.SearchHash == game.GetSearchHashIgnoringBacktrackForVerification())
        {
            throw Failure(game, "Search hash does not include immediate-backtrack history.");
        }

        GameEngineVerificationSnapshot before = game.GetVerificationSnapshot();
        Move sample = game.GetLegalMoves()[0];
        GameEngine.SearchUndo undo = game.ApplyGeneratedMoveForSearch(sample);
        game.UndoSearchMove(undo);
        GameEngineVerificationSnapshot after = game.GetVerificationSnapshot();
        if (before != after)
        {
            throw Failure(game, "Dedicated Apply→Undo failed to restore previous-own-move/hash state.\n" + DescribeSnapshotDifference(before, after));
        }
        applyUndoChecks++;

        GameEngine clone = game.Clone();
        if (game.GetVerificationSnapshot() != clone.GetVerificationSnapshot())
        {
            throw Failure(game, "Dedicated Clone failed to preserve previous-own-move/hash state.");
        }
        cloneChecks++;

        // Exception: if the moved piece has no legal alternative, immediate return is allowed.
        GameStartConfiguration forcedStart = GameStartConfiguration.Create(
            "forced immediate-backtrack exception",
            "built-in verifier",
            new[]
            {
                "...s....",
                "o.......",
                "........",
                "........",
                "........",
                ".......o",
                "......S.",
                ".......o"
            },
            PlayerId.Player1);
        var forced = new GameEngine(forcedStart);
        Move runnerOut = new(new Position(6, 6), new Position(6, 7), MoveKind.Normal); // G7-H7
        Move p2Waiting = new(new Position(1, 0), new Position(2, 0), MoveKind.Normal);  // A2-A3
        if (!forced.TryApplyMove(runnerOut, out string? forcedP1Error))
        {
            throw Failure(forced, $"Could not construct forced-backtrack state (P1): {forcedP1Error}");
        }
        if (!forced.TryApplyMove(p2Waiting, out string? forcedP2Error))
        {
            throw Failure(forced, $"Could not construct forced-backtrack state (P2): {forcedP2Error}");
        }

        Move forcedReverse = new(new Position(6, 7), new Position(6, 6), MoveKind.Normal); // H7-G7
        IReadOnlyList<Move> forcedRunnerMoves = forced.GetRunnerLegalMoves(PlayerId.Player1);
        if (forced.Outcome != GameOutcome.Ongoing || forcedRunnerMoves.Count != 1 || forcedRunnerMoves[0] != forcedReverse)
        {
            throw Failure(forced,
                $"Forced-backtrack exception failed. outcome={forced.Outcome}/{forced.EndReason}, " +
                $"runnerMoves=[{string.Join(", ", forcedRunnerMoves.Select(m => m.ToNotation()))}]");
        }
        VerifyPosition(forced, "dedicated forced-backtrack exception");
        checkedPositions++;
        moveSetChecks += 2;

        return 2;
    }

    private static int VerifyFourfoldBoardRepetition(ref int checkedPositions, ref int moveSetChecks)
    {
        string[] boardRows =
        {
            "OO......",
            ".sO.....",
            "OO......",
            "........",
            "........",
            "......oo",
            ".....oS.",
            "......oo"
        };
        GameStartConfiguration start = GameStartConfiguration.Create(
            "board-only fourfold forced cycle",
            "built-in verifier",
            boardRows,
            PlayerId.Player1);
        var game = new GameEngine(start);

        var oppositeTurn = new GameEngine(GameStartConfiguration.Create(
            "board-only opposite-turn comparison",
            "built-in verifier",
            boardRows,
            PlayerId.Player2));
        GameEngineVerificationSnapshot p1Turn = game.GetVerificationSnapshot();
        GameEngineVerificationSnapshot p2Turn = oppositeTurn.GetVerificationSnapshot();
        if (p1Turn.PositionHash == p2Turn.PositionHash)
        {
            throw Failure(game, "Turn-independence setup failed: PositionHash did not distinguish side-to-move.");
        }
        if (p1Turn.RepetitionStateHash != p2Turn.RepetitionStateHash)
        {
            throw Failure(game, "Repetition key incorrectly depends on side-to-move.");
        }

        Move[] cycle =
        {
            new(new Position(6, 6), new Position(6, 7), MoveKind.Normal),
            new(new Position(1, 1), new Position(1, 0), MoveKind.Normal),
            new(new Position(6, 7), new Position(6, 6), MoveKind.Normal),
            new(new Position(1, 0), new Position(1, 1), MoveKind.Normal)
        };

        ulong initialRepetitionHash = p1Turn.RepetitionStateHash;
        if (game.CurrentPositionRepetitionCount() != 1)
        {
            throw Failure(game, $"Initial board occurrence must count as 1, got {game.CurrentPositionRepetitionCount()}.");
        }

        for (int cycleNumber = 1; cycleNumber <= 3; cycleNumber++)
        {
            foreach (Move move in cycle)
            {
                if (!game.TryApplyMove(move, out string? error))
                {
                    throw Failure(game, $"Forced-cycle move failed in cycle {cycleNumber}: {move.ToNotation()} / {error}");
                }
                if (game.Outcome != GameOutcome.Ongoing && !(cycleNumber == 3 && move == cycle[^1]))
                {
                    throw Failure(game, $"Repetition ended too early at ply {game.PlyCount}: {game.Outcome}/{game.EndReason}");
                }
            }

            if (game.GetVerificationSnapshot().RepetitionStateHash != initialRepetitionHash)
            {
                throw Failure(game, "Repetition key changed despite identical board placement.");
            }

            if (cycleNumber < 3)
            {
                int expectedCount = cycleNumber + 1;
                if (game.Outcome != GameOutcome.Ongoing || game.CurrentPositionRepetitionCount() != expectedCount)
                {
                    throw Failure(game,
                        $"Fourfold count mismatch after cycle {cycleNumber}: expected={expectedCount}, " +
                        $"actual={game.CurrentPositionRepetitionCount()}, outcome={game.Outcome}/{game.EndReason}");
                }
            }
        }

        if (game.Outcome != GameOutcome.Draw || game.EndReason != EndReason.FourfoldRepetition || game.PlyCount != 12)
        {
            throw Failure(game,
                $"Fourfold draw mismatch: ply={game.PlyCount}, outcome={game.Outcome}, reason={game.EndReason}, " +
                $"count={game.CurrentPositionRepetitionCount()}");
        }

        checkedPositions++;
        moveSetChecks += 2;
        return 1;
    }

    private static void VerifyPosition(GameEngine game, string context)
    {
        VerifyBoardAndBitboards(game, context);

        if (game.Outcome != GameOutcome.Ongoing)
        {
            if (game.GetLegalMoves(PlayerId.Player1).Count != 0 || game.GetLegalMoves(PlayerId.Player2).Count != 0)
            {
                throw Failure(game, $"Terminal position returned legal moves. {context}");
            }
            return;
        }

        foreach (PlayerId player in new[] { PlayerId.Player1, PlayerId.Player2 })
        {
            ReferenceMoveResult reference = GenerateReferenceMoves(game, player, applyStrategyRestrictions: true);
            IReadOnlyList<Move> actual = game.GetLegalMoves(player);
            CompareMoves(game, reference.AllMoves, actual, $"all legal moves / {player.ShortName()} / {context}");

            IReadOnlyList<Move> actualRunner = game.GetRunnerLegalMoves(player);
            CompareMoves(game, reference.RunnerMoves, actualRunner, $"runner legal moves / {player.ShortName()} / {context}");

            List<Move> actualBlocker = actual
                .Where(move => game.GetPiece(move.From) is { Type: PieceType.Blocker })
                .ToList();
            CompareMoves(game, reference.BlockerMoves, actualBlocker, $"blocker legal moves / {player.ShortName()} / {context}");

            List<Move> actualRunnerNormal = actualRunner.Where(move => move.Kind == MoveKind.Normal).ToList();
            List<Move> actualRunnerSacrifice = actualRunner.Where(move => move.Kind == MoveKind.Sacrifice).ToList();
            CompareMoves(game, reference.RunnerNormalMoves, actualRunnerNormal, $"runner normal moves / {player.ShortName()} / {context}");
            CompareMoves(game, reference.RunnerSacrificeMoves, actualRunnerSacrifice, $"runner sacrifice moves / {player.ShortName()} / {context}");

            if (game.CountRunnerNormalMoves(player) != reference.RunnerNormalMoves.Count)
            {
                throw Failure(game, $"CountRunnerNormalMoves mismatch for {player.ShortName()} / {context}.");
            }
            if (game.CountRunnerSacrificeMoves(player) != reference.RunnerSacrificeMoves.Count)
            {
                throw Failure(game, $"CountRunnerSacrificeMoves mismatch for {player.ShortName()} / {context}.");
            }
            int immediateGoals = reference.RunnerMoves.Count(move => IsGoalRow(player, move.To.Row));
            if (game.CountImmediateGoalMoves(player) != immediateGoals)
            {
                throw Failure(game, $"CountImmediateGoalMoves mismatch for {player.ShortName()} / {context}.");
            }

            foreach (Move forbidden in reference.ImmediateBacktrackRejectedMoves)
            {
                if (actual.Contains(forbidden))
                {
                    throw Failure(game, $"Immediate-backtrack move leaked into legal moves for {player.ShortName()} / {context}: {forbidden.ToNotation()}");
                }
            }
        }
    }

    private static void VerifyBoardAndBitboards(GameEngine game, string context)
    {
        ulong p1Blockers = 0;
        ulong p2Blockers = 0;
        ulong p1Runner = 0;
        ulong p2Runner = 0;
        Position? p1RunnerPosition = null;
        Position? p2RunnerPosition = null;

        for (int row = 0; row < GameEngine.BoardSize; row++)
        {
            for (int col = 0; col < GameEngine.BoardSize; col++)
            {
                var position = new Position(row, col);
                Piece? piece = game.GetPiece(position);
                if (piece is not { } value) continue;
                ulong bit = 1UL << (row * GameEngine.BoardSize + col);
                if (value.Owner == PlayerId.Player1 && value.Type == PieceType.Blocker) p1Blockers |= bit;
                else if (value.Owner == PlayerId.Player2 && value.Type == PieceType.Blocker) p2Blockers |= bit;
                else if (value.Owner == PlayerId.Player1)
                {
                    p1Runner |= bit;
                    p1RunnerPosition = position;
                }
                else
                {
                    p2Runner |= bit;
                    p2RunnerPosition = position;
                }
            }
        }

        GameEngineVerificationSnapshot snapshot = game.GetVerificationSnapshot();
        if (snapshot.P1BlockerBits != p1Blockers || snapshot.P2BlockerBits != p2Blockers ||
            snapshot.P1RunnerBit != p1Runner || snapshot.P2RunnerBit != p2Runner)
        {
            throw Failure(game, $"Board/bitboard occupancy mismatch. {context}");
        }
        if (p1RunnerPosition is null || p2RunnerPosition is null ||
            game.FindRunner(PlayerId.Player1) != p1RunnerPosition.Value ||
            game.FindRunner(PlayerId.Player2) != p2RunnerPosition.Value)
        {
            throw Failure(game, $"Runner-position cache mismatch. {context}");
        }
        if (game.CountBlockers(PlayerId.Player1) != PopCount(p1Blockers) ||
            game.CountBlockers(PlayerId.Player2) != PopCount(p2Blockers))
        {
            throw Failure(game, $"Blocker count mismatch. {context}");
        }
    }

    private static ReferenceMoveResult GenerateReferenceMoves(GameEngine game, PlayerId player, bool applyStrategyRestrictions)
    {
        var all = new List<Move>(64);
        var runner = new List<Move>(16);
        var blocker = new List<Move>(56);
        var runnerNormal = new List<Move>(8);
        var runnerSacrifice = new List<Move>(8);
        var rejected = new List<Move>(8);
        GameEngineVerificationSnapshot snapshot = game.GetVerificationSnapshot();
        bool forcedRetreat = IsReferenceRunnerForcedToRetreat(game, player);

        for (int row = 0; row < GameEngine.BoardSize; row++)
        {
            for (int col = 0; col < GameEngine.BoardSize; col++)
            {
                var from = new Position(row, col);
                Piece? piece = game.GetPiece(from);
                if (piece is not { } value || value.Owner != player) continue;

                if (value.Type == PieceType.Runner)
                {
                    AddReferenceRunnerMoves(game, snapshot, from, player, applyStrategyRestrictions,
                        all, runner, runnerNormal, runnerSacrifice, rejected);
                }
                else if (!forcedRetreat)
                {
                    AddReferenceBlockerMoves(game, snapshot, from, player, all, blocker, rejected);
                }
            }
        }

        return new ReferenceMoveResult(all, runner, blocker, runnerNormal, runnerSacrifice, rejected);
    }

    private static void AddReferenceBlockerMoves(
        GameEngine game,
        GameEngineVerificationSnapshot snapshot,
        Position from,
        PlayerId player,
        List<Move> all,
        List<Move> blocker,
        List<Move> rejected)
    {
        Move? backtrackCandidate = null;
        int alternativeCount = 0;

        foreach (var (dr, dc) in EightDirections)
        {
            var to = new Position(from.Row + dr, from.Col + dc);
            if (!to.IsInside || game.GetPiece(to) is not null) continue;

            var move = new Move(from, to, MoveKind.Normal);
            if (IsImmediateBacktrack(snapshot, player, from, to))
            {
                backtrackCandidate = move;
                continue;
            }
            alternativeCount++;
            all.Add(move);
            blocker.Add(move);
        }

        if (backtrackCandidate is { } reverse)
        {
            if (alternativeCount == 0)
            {
                all.Add(reverse);
                blocker.Add(reverse);
            }
            else
            {
                rejected.Add(reverse);
            }
        }
    }

    private static void AddReferenceRunnerMoves(
        GameEngine game,
        GameEngineVerificationSnapshot snapshot,
        Position from,
        PlayerId player,
        bool applyStrategyRestrictions,
        List<Move> all,
        List<Move> runner,
        List<Move> runnerNormal,
        List<Move> runnerSacrifice,
        List<Move> rejected)
    {
        Move? backtrackCandidate = null;
        int alternativeCount = 0;

        foreach (var (dr, dc) in OrthogonalDirections)
        {
            var to = new Position(from.Row + dr, from.Col + dc);
            if (!to.IsInside || game.GetPiece(to) is not null) continue;

            var move = new Move(from, to, MoveKind.Normal);
            if (IsImmediateBacktrack(snapshot, player, from, to))
            {
                backtrackCandidate = move;
                continue;
            }
            alternativeCount++;
            all.Add(move);
            runner.Add(move);
            runnerNormal.Add(move);
        }

        foreach (var (dr, dc) in EightDirections)
        {
            var to = new Position(from.Row + dr, from.Col + dc);
            if (!to.IsInside) continue;
            Piece? target = game.GetPiece(to);
            if (target is null || target.Value.Type != PieceType.Blocker || target.Value.Owner != player) continue;

            alternativeCount++;
            var move = new Move(from, to, MoveKind.Sacrifice);
            all.Add(move);
            runner.Add(move);
            runnerSacrifice.Add(move);
        }

        if (backtrackCandidate is { } reverse)
        {
            if (alternativeCount == 0)
            {
                all.Add(reverse);
                runner.Add(reverse);
                runnerNormal.Add(reverse);
            }
            else
            {
                rejected.Add(reverse);
            }
        }
    }

    private static bool IsReferenceRunnerForcedToRetreat(GameEngine game, PlayerId player)
    {
        Position runner = game.FindRunner(player);
        PlayerId opponent = player.Opponent();
        foreach (var (dr, dc) in OrthogonalDirections)
        {
            var adjacent = new Position(runner.Row + dr, runner.Col + dc);
            if (!adjacent.IsInside || game.GetPiece(adjacent) is not { } piece || piece.Owner != opponent)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsImmediateBacktrack(GameEngineVerificationSnapshot snapshot, PlayerId player, Position from, Position to)
    {
        Move? previous = player == PlayerId.Player1 ? snapshot.P1LastOwnMove : snapshot.P2LastOwnMove;
        return previous is { } last && last.To == from && last.From == to;
    }

    private static void CompareMoves(GameEngine game, IReadOnlyList<Move> expected, IReadOnlyList<Move> actual, string label)
    {
        if (expected.SequenceEqual(actual)) return;

        var expectedSet = expected.ToHashSet();
        var actualSet = actual.ToHashSet();
        throw Failure(game,
            $"Move mismatch: {label}\n" +
            $"sameSet={expectedSet.SetEquals(actualSet)}\n" +
            $"reference[{expected.Count}]={string.Join(", ", expected.Select(m => m.ToNotation()))}\n" +
            $"bitboard [{actual.Count}]={string.Join(", ", actual.Select(m => m.ToNotation()))}\n" +
            $"onlyReference={string.Join(", ", expectedSet.Except(actualSet).Select(m => m.ToNotation()))}\n" +
            $"onlyBitboard={string.Join(", ", actualSet.Except(expectedSet).Select(m => m.ToNotation()))}");
    }

    private static string[] BoardRows(GameEngine game)
    {
        var rows = new string[GameEngine.BoardSize];
        for (int row = 0; row < GameEngine.BoardSize; row++)
        {
            var chars = new char[GameEngine.BoardSize];
            for (int col = 0; col < GameEngine.BoardSize; col++)
            {
                chars[col] = game.GetPiece(new Position(row, col)) switch
                {
                    null => '.',
                    { Owner: PlayerId.Player1, Type: PieceType.Blocker } => 'O',
                    { Owner: PlayerId.Player1, Type: PieceType.Runner } => 'S',
                    { Owner: PlayerId.Player2, Type: PieceType.Blocker } => 'o',
                    { Owner: PlayerId.Player2, Type: PieceType.Runner } => 's',
                    _ => '?'
                };
            }
            rows[row] = new string(chars);
        }
        return rows;
    }

    private static InvalidOperationException Failure(GameEngine game, string message)
    {
        var sb = new StringBuilder();
        sb.AppendLine(message);
        sb.AppendLine($"Rules=Standard Current={game.CurrentPlayer.ShortName()} Ply={game.PlyCount} Outcome={game.Outcome} End={game.EndReason}");
        GameEngineVerificationSnapshot snapshot = game.GetVerificationSnapshot();
        sb.AppendLine($"P1LastOwn={FormatMove(snapshot.P1LastOwnMove)} P2LastOwn={FormatMove(snapshot.P2LastOwnMove)}");
        sb.AppendLine($"PositionHash={snapshot.PositionHash:X16} BacktrackHash={snapshot.BacktrackStateHash:X16} RepetitionHash={snapshot.RepetitionStateHash:X16} SearchHash={snapshot.SearchHash:X16}");
        sb.AppendLine("Board:");
        foreach (string row in BoardRows(game)) sb.AppendLine(row);
        return new InvalidOperationException(sb.ToString());
    }

    private static string DescribeSnapshotDifference(GameEngineVerificationSnapshot before, GameEngineVerificationSnapshot after)
    {
        var sb = new StringBuilder();
        void Add<T>(string name, T a, T b)
        {
            if (!EqualityComparer<T>.Default.Equals(a, b)) sb.AppendLine($"{name}: before={a} after={b}");
        }

        Add(nameof(before.BoardSignature), before.BoardSignature, after.BoardSignature);
        Add(nameof(before.CurrentPlayer), before.CurrentPlayer, after.CurrentPlayer);
        Add(nameof(before.Outcome), before.Outcome, after.Outcome);
        Add(nameof(before.EndReason), before.EndReason, after.EndReason);
        Add(nameof(before.PlyCount), before.PlyCount, after.PlyCount);
        Add(nameof(before.LastMove), before.LastMove, after.LastMove);
        Add(nameof(before.P1BlockerBits), before.P1BlockerBits, after.P1BlockerBits);
        Add(nameof(before.P2BlockerBits), before.P2BlockerBits, after.P2BlockerBits);
        Add(nameof(before.P1RunnerBit), before.P1RunnerBit, after.P1RunnerBit);
        Add(nameof(before.P2RunnerBit), before.P2RunnerBit, after.P2RunnerBit);
        Add(nameof(before.P1RunnerPosition), before.P1RunnerPosition, after.P1RunnerPosition);
        Add(nameof(before.P2RunnerPosition), before.P2RunnerPosition, after.P2RunnerPosition);
        Add(nameof(before.MovePolicyId), before.MovePolicyId, after.MovePolicyId);
        Add(nameof(before.MovePolicyState), before.MovePolicyState, after.MovePolicyState);
        Add(nameof(before.P1LastOwnMove), before.P1LastOwnMove, after.P1LastOwnMove);
        Add(nameof(before.P2LastOwnMove), before.P2LastOwnMove, after.P2LastOwnMove);
        Add(nameof(before.PositionHash), before.PositionHash, after.PositionHash);
        Add(nameof(before.HistoryHash), before.HistoryHash, after.HistoryHash);
        Add(nameof(before.BacktrackStateHash), before.BacktrackStateHash, after.BacktrackStateHash);
        Add(nameof(before.RepetitionStateHash), before.RepetitionStateHash, after.RepetitionStateHash);
        Add(nameof(before.SearchHash), before.SearchHash, after.SearchHash);
        Add(nameof(before.RepetitionCountsSignature), before.RepetitionCountsSignature, after.RepetitionCountsSignature);
        return sb.Length == 0 ? "(record inequality with no field difference found)" : sb.ToString();
    }

    private static int PopCount(ulong value)
    {
        int count = 0;
        while (value != 0)
        {
            value &= value - 1;
            count++;
        }
        return count;
    }

    private static bool IsGoalRow(PlayerId player, int row) =>
        player == PlayerId.Player1 ? row == 0 : row == GameEngine.BoardSize - 1;

    private static string FormatMove(Move? move) => move is { } value ? value.ToNotation() : "-";

    private static string ResolveReportDirectory()
    {
        string besideExe = Path.Combine(AppContext.BaseDirectory, "verification_logs");
        try
        {
            Directory.CreateDirectory(besideExe);
            return besideExe;
        }
        catch
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StarRunnerPrototype",
                "verification_logs");
        }
    }

    private sealed record ReferenceMoveResult(
        IReadOnlyList<Move> AllMoves,
        IReadOnlyList<Move> RunnerMoves,
        IReadOnlyList<Move> BlockerMoves,
        IReadOnlyList<Move> RunnerNormalMoves,
        IReadOnlyList<Move> RunnerSacrificeMoves,
        IReadOnlyList<Move> ImmediateBacktrackRejectedMoves);
}
