using System.Diagnostics;
using System.Text;

namespace StarRunnerPrototype;

internal sealed record CpuSearchVerifierResult(
    bool Passed,
    int Positions,
    int SerialTtChecks,
    int RootBoundChecks,
    int ParallelChecks,
    int Depth,
    TimeSpan Elapsed,
    string Details)
{
    public string ToUserSummary()
    {
        string result = Passed ? "PASS" : "FAIL";
        return $"CPU search correctness verifier: {result}\r\n\r\n" +
               $"局面: {Positions:N0}\r\n" +
               $"TT OFF↔固定長TT: {SerialTtChecks:N0}\r\n" +
               $"serial root bound検証: {RootBoundChecks:N0}\r\n" +
               $"1thread↔root並列: {ParallelChecks:N0}\r\n" +
               $"検証Depth: D{Depth}\r\n" +
               $"経過: {Elapsed.TotalSeconds:F3} 秒\r\n\r\n" +
               Details;
    }
}

internal static class CpuSearchCorrectnessVerifier
{
    private const int VerificationDepth = 4;

    public static CpuSearchVerifierResult Run()
    {
        var stopwatch = Stopwatch.StartNew();
        int positions = 0;
        int serialTtChecks = 0;
        int rootBoundChecks = 0;
        int parallelChecks = 0;

        try
        {
            VerifyBuiltInDefaultProfileV2378();
            VerifyEvaluationVocabularyV2375();
            VerifyRunnerGoalPathFeature();
            VerifyEvaluationLogicV2362();
            VerifySearchSelectivityV2371();

            foreach ((GameEngine position, string label) in BuildPositions())
            {
                positions++;
                PlayerId player = position.CurrentPlayer;

                CpuDecision reference = Search(position, player, useTt: false, maxParallelism: 1);
                CpuDecision fixedTt = Search(position, player, useTt: true, maxParallelism: 1);
                CpuDecision noStaticCache = Search(
                    position, player, useTt: true, maxParallelism: 1, useStaticEvaluationCache: false);

                EnsureCompleted(reference, label, "TT OFF");
                EnsureCompleted(fixedTt, label, "固定長TT");
                EnsureCompleted(noStaticCache, label, "静的評価cache OFF");
                if (reference.Score != fixedTt.Score || reference.Move != fixedTt.Move)
                {
                    throw new InvalidOperationException(
                        $"Serial TT mismatch at {label}.\n" +
                        $"TT OFF: move={reference.Move.ToNotation()} score={reference.Score} nodes={reference.Nodes:N0}\n" +
                        $"TT ON : move={fixedTt.Move.ToNotation()} score={fixedTt.Score} nodes={fixedTt.Nodes:N0}");
                }
                serialTtChecks++;

                if (fixedTt.Score != noStaticCache.Score ||
                    fixedTt.Move != noStaticCache.Move ||
                    fixedTt.Nodes != noStaticCache.Nodes)
                {
                    throw new InvalidOperationException(
                        $"Static evaluation cache mismatch at {label}.\n" +
                        $"Cache ON : move={fixedTt.Move.ToNotation()} score={fixedTt.Score} nodes={fixedTt.Nodes:N0}\n" +
                        $"Cache OFF: move={noStaticCache.Move.ToNotation()} score={noStaticCache.Score} nodes={noStaticCache.Nodes:N0}");
                }

                // Oracle search: force every root move to be exact, then verify that the
                // normally pruned serial search selected a move whose exact score is truly
                // equal to the root optimum.  This specifically catches the historical bug
                // where a fail-low upper bound tied bestScore and replaced the proven move.
                CpuDecision exactRoot = Search(
                    position,
                    player,
                    useTt: true,
                    maxParallelism: 1,
                    collectExactRootScores: true);
                EnsureCompleted(exactRoot, label, "serial exact-root oracle");
                CpuCandidate selectedExact = default;
                bool foundSelectedExact = false;
                foreach (CpuCandidate candidate in exactRoot.Candidates)
                {
                    if (candidate.Move != fixedTt.Move) continue;
                    selectedExact = candidate;
                    foundSelectedExact = true;
                    break;
                }
                if (!foundSelectedExact ||
                    fixedTt.Score != exactRoot.Score ||
                    selectedExact.SearchScore != exactRoot.Score)
                {
                    string selectedScore = foundSelectedExact ? selectedExact.SearchScore.ToString() : "<missing>";
                    throw new InvalidOperationException(
                        $"Serial root bound mismatch at {label}.\n" +
                        $"Normal: move={fixedTt.Move.ToNotation()} score={fixedTt.Score} nodes={fixedTt.Nodes:N0}\n" +
                        $"Exact : best={exactRoot.Move.ToNotation()} score={exactRoot.Score}; " +
                        $"normal-move exact score={selectedScore} nodes={exactRoot.Nodes:N0}");
                }
                rootBoundChecks++;

                int parallelism = Math.Min(4, Math.Max(1, Environment.ProcessorCount));
                if (parallelism > 1)
                {
                    CpuDecision parallel = Search(position, player, useTt: true, maxParallelism: parallelism);
                    EnsureCompleted(parallel, label, $"root parallel x{parallelism}");

                    // Root split may choose a different equally-good move because fail-low
                    // siblings are intentionally left as upper bounds. The exact minimax
                    // score must still match the serial search.
                    if (parallel.Score != fixedTt.Score)
                    {
                        throw new InvalidOperationException(
                            $"Parallel score mismatch at {label}.\n" +
                            $"Serial: move={fixedTt.Move.ToNotation()} score={fixedTt.Score} nodes={fixedTt.Nodes:N0}\n" +
                            $"Parallel: move={parallel.Move.ToNotation()} score={parallel.Score} nodes={parallel.Nodes:N0}");
                    }
                    parallelChecks++;
                }
            }

            stopwatch.Stop();
            return new CpuSearchVerifierResult(
                true,
                positions,
                serialTtChecks,
                rootBoundChecks,
                parallelChecks,
                VerificationDepth,
                stopwatch.Elapsed,
                "RunnerGoalPath固定局面、v0.2.36.2評価ロジック、v0.2.37.3探索既定、PVS+mate-distance baseline一致、shipping PVS/LMR発火、静的評価cache ON↔OFF、TT無効↔固定長TT、serial exact-root oracle、root並列を検証しました。");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new CpuSearchVerifierResult(
                false,
                positions,
                serialTtChecks,
                rootBoundChecks,
                parallelChecks,
                VerificationDepth,
                stopwatch.Elapsed,
                ex.ToString());
        }
    }

    public static string WriteReport(CpuSearchVerifierResult result)
    {
        string directory = ResolveReportDirectory();
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"cpu_search_verifier_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt");
        File.WriteAllText(path, result.ToUserSummary(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static CpuDecision Search(
        GameEngine position,
        PlayerId player,
        bool useTt,
        int maxParallelism,
        bool collectExactRootScores = false,
        bool useStaticEvaluationCache = true,
        int? verificationDepth = null,
        bool usePvs = true,
        bool useMateDistancePruning = true,
        bool useLmr = false,
        bool useOnlySurvivalExtension = false)
    {
        var options = new CpuSearchOptions(
            MaxDepth: verificationDepth ?? VerificationDepth,
            TimeLimitMilliseconds: 0,
            MaxNodes: 0,
            UseTranspositionTable: useTt,
            CollectExactRootScores: collectExactRootScores,
            RandomTopK: 1,
            RandomScoreWindow: 0,
            RandomSeed: 0,
            CycleBreakScoreWindow: 0,
            MaxParallelism: maxParallelism,
            UseBelowNormalThreadPriority: true,
            UseStaticEvaluationCache: useStaticEvaluationCache,
            UsePrincipalVariationSearch: usePvs,
            UseMateDistancePruning: useMateDistancePruning,
            UseLateMoveReductions: useLmr,
            UseOnlySurvivalExtension: useOnlySurvivalExtension,
            MaxOnlySurvivalExtensionsPerLine: useOnlySurvivalExtension ? 2 : 0);
        return CpuPlayer.DecideMove(position.Clone(), player, options, CancellationToken.None);
    }

    private static void EnsureCompleted(CpuDecision decision, string label, string mode)
    {
        if (decision.Depth != VerificationDepth || decision.TimedOut || decision.NodeLimitReached)
        {
            throw new InvalidOperationException(
                $"Search did not complete at {label} / {mode}: completed D{decision.Depth}, " +
                $"timeout={decision.TimedOut}, nodeLimit={decision.NodeLimitReached}.");
        }
    }

    private static void VerifyBuiltInDefaultProfileV2378()
    {
        CpuEvaluationProfile profile = CpuEvaluationProfile.BuiltInDefault;
        if (profile.Name != "Scan-O.BlockerMaterial-1000")
        {
            throw new InvalidOperationException(
                $"BuiltInDefault profile mismatch: expected Scan-O.BlockerMaterial-1000, actual {profile.Name}.");
        }

        var expectedOpening = new CpuEvaluationFeatureScales(
            RunnerProgress: 1520,
            RunnerMobility: 490,
            BlockerMaterial: 1000,
            FriendlyRunnerSupport: 1120,
            FrontPressure: 1830,
            GoalDefense: 1240,
            ImmediateGoalThreats: 1390,
            BlockerAdvancement: 1300,
            BridgeheadConnection: 2480,
            RunnerGoalPath: 1500,
            PreparedGoalThreat: 750,
            UnansweredGoalThreat: 470,
            ConnectedGoalThreat: 600,
            ViableRunnerProgress: 1220)
            { SacrificeDebt = 1800 };
        var expectedEndgame = new CpuEvaluationFeatureScales(
            RunnerProgress: 780,
            RunnerMobility: 1110,
            BlockerMaterial: 400,
            FriendlyRunnerSupport: 930,
            FrontPressure: 1550,
            GoalDefense: 770,
            ImmediateGoalThreats: 2320,
            BlockerAdvancement: 870,
            BridgeheadConnection: 1710,
            RunnerGoalPath: 1510,
            PreparedGoalThreat: 1310,
            UnansweredGoalThreat: 1120,
            ConnectedGoalThreat: 1250,
            ViableRunnerProgress: 780)
            { SacrificeDebt = 2600 };

        if (profile.Opening != expectedOpening || profile.Endgame != expectedEndgame)
        {
            throw new InvalidOperationException(
                "Scan-O.BlockerMaterial-1000 built-in weights do not match the accepted 2026-08-15 active 15-feature table.");
        }
    }

    private static void VerifySearchSelectivityV2371()
    {
        CpuSearchOptions defaults = new CpuSearchOptions().Normalize();
        if (defaults.MaxOnlySurvivalExtensionsPerLine != 2 ||
            defaults.MaxAdaptiveRootDeepeningPly != 6 ||
            !defaults.UseMateDistanceScout ||
            defaults.MateDistanceScoutMinCompletedDepth != 6 ||
            defaults.MaxMateDistanceScoutExtraPly != 8)
        {
            throw new InvalidOperationException(
                $"v0.2.37.4 search defaults mismatch: recursive={defaults.MaxOnlySurvivalExtensionsPerLine}, " +
                $"root={defaults.MaxAdaptiveRootDeepeningPly}, scout={defaults.UseMateDistanceScout}, " +
                $"scoutMinD={defaults.MateDistanceScoutMinCompletedDepth}, scoutExtra={defaults.MaxMateDistanceScoutExtraPly}.");
        }

        var start = new GameEngine();
        PlayerId player = start.CurrentPlayer;

        // PVS + mate-distance pruning are exact alpha-beta optimizations. With LMR and
        // selective extensions disabled they must preserve the baseline fixed-depth result.
        CpuDecision baseline = Search(
            start,
            player,
            useTt: false,
            maxParallelism: 1,
            verificationDepth: 5,
            usePvs: false,
            useMateDistancePruning: false,
            useLmr: false,
            useOnlySurvivalExtension: false);
        CpuDecision safeOptimized = Search(
            start,
            player,
            useTt: false,
            maxParallelism: 1,
            verificationDepth: 5,
            usePvs: true,
            useMateDistancePruning: true,
            useLmr: false,
            useOnlySurvivalExtension: false);

        if (baseline.Score != safeOptimized.Score || baseline.Move != safeOptimized.Move)
        {
            throw new InvalidOperationException(
                "v0.2.37.3 PVS/mate-distance verifier mismatch.\n" +
                $"baseline: move={baseline.Move.ToNotation()} score={baseline.Score} nodes={baseline.Nodes:N0}\n" +
                $"optimized: move={safeOptimized.Move.ToNotation()} score={safeOptimized.Score} nodes={safeOptimized.Nodes:N0}");
        }

        // D5 from the standard initial position has enough branching for the conservative
        // late-move schedule to execute. This is a smoke check for the shipping path; LMR
        // is selective by design, so it is not required to equal the fixed-depth baseline.
        GameEngine lmrPosition = BuildStaticPosition(
            "v0.2.37.3 LMR smoke",
            new[]
            {
                "........",
                "o......o",
                ".o....o.",
                "...S.s..",
                "o......o",
                "O......O",
                ".O....O.",
                "O......O"
            },
            PlayerId.Player1);

        CpuDecision selective = Search(
            lmrPosition,
            lmrPosition.CurrentPlayer,
            useTt: true,
            maxParallelism: 1,
            verificationDepth: 5,
            usePvs: true,
            useMateDistancePruning: true,
            useLmr: true,
            useOnlySurvivalExtension: true);

        EnsureCompletedAtDepth(selective, 5, "v0.2.37.3 selectivity smoke");
        if (selective.SearchTelemetry.PvsNullWindowProbes <= 0)
        {
            throw new InvalidOperationException("v0.2.37.3 verifier: PVS probe counter did not advance.");
        }
        if (selective.SearchTelemetry.LmrReducedSearches <= 0)
        {
            throw new InvalidOperationException("v0.2.37.3 verifier: LMR reduction counter did not advance at D5.");
        }

        // The exact user-reported ply-74 defensive move G7-F7 places a blocker directly
        // in front of the P2 Runner on F6. The shipping LMR geometry explicitly protects
        // all moves to/from that front-square shadow zone from reduction; this invariant is
        // also documented in TEST_PLAN.md for a replay-level check.
    }

    private static void EnsureCompletedAtDepth(CpuDecision decision, int depth, string label)
    {
        if (decision.Depth != depth || decision.TimedOut || decision.NodeLimitReached)
        {
            throw new InvalidOperationException(
                $"Search did not complete at {label}: completed D{decision.Depth}/{depth}, " +
                $"timedOut={decision.TimedOut}, nodeLimit={decision.NodeLimitReached}.");
        }
    }


    private static void VerifyEvaluationVocabularyV2375()
    {
        string[] names = EvaluationTuner.TunableFeatureNames.ToArray();
        if (names.Length != 15)
        {
            throw new InvalidOperationException(
                $"Evaluation vocabulary mismatch: expected 15 active features, actual {names.Length}.");
        }

        if (names.Contains("GoalBridgeheads", StringComparer.Ordinal) ||
            names.Contains("RunnerCentrality", StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Retired evaluation features are still exposed by the tuner vocabulary.");
        }
    }

    private static void VerifyRunnerGoalPathFeature()
    {
        // Reproduces the key geometry from the 2026-08-13 human-vs-5dan loss:
        // P1 E4 cannot move straight through E3 (enemy blocker), but has the concrete
        // sacrifice chain E4xF3 -> F3xG2 -> goal row. P2 has a longer static route.
        var start = GameStartConfiguration.Create(
            "RunnerGoalPath verifier",
            "cpu-search-verifier",
            new[]
            {
                "o.o.oo..",
                "......OO",
                "....oO.O",
                "....S...",
                "...s....",
                "...OO...",
                ".....O..",
                ".....o.."
            },
            PlayerId.Player1);
        var game = new GameEngine(start);
        EvaluationBreakdown breakdown = CpuPlayer.EvaluateDetailed(game, PlayerId.Player1, CpuEvaluationProfile.BuiltInDefault);
        if (breakdown.RunnerGoalPath <= 0)
        {
            throw new InvalidOperationException(
                $"RunnerGoalPath verifier expected P1's 3-step sacrifice route to outrun P2, but contribution was {breakdown.RunnerGoalPath}.");
        }
    }


    private static void VerifyEvaluationLogicV2362()
    {
        CpuEvaluationProfile frontOnly = FeatureOnlyProfile(frontPressure: 1000);

        GameEngine direct = BuildStaticPosition(
            "FrontPressure direct",
            new[]
            {
                "........",
                "........",
                "........",
                "...s....",
                "...O....",
                "........",
                ".......S",
                "........"
            },
            PlayerId.Player1);
        GameEngine shadow = BuildStaticPosition(
            "FrontPressure shadow",
            new[]
            {
                "........",
                "........",
                "........",
                "..s.....",
                "...O....",
                "........",
                ".......S",
                "........"
            },
            PlayerId.Player1);
        GameEngine protectedAdvance = BuildStaticPosition(
            "FrontPressure protected advance",
            new[]
            {
                "........",
                "........",
                "........",
                "..s.....",
                ".o.O....",
                "........",
                ".......S",
                "........"
            },
            PlayerId.Player1);

        int directPressure = CpuPlayer.EvaluateDetailed(direct, PlayerId.Player2, frontOnly).FrontPressure;
        int shadowPressure = CpuPlayer.EvaluateDetailed(shadow, PlayerId.Player2, frontOnly).FrontPressure;
        int protectedPressure = CpuPlayer.EvaluateDetailed(protectedAdvance, PlayerId.Player2, frontOnly).FrontPressure;
        if (!(directPressure < shadowPressure && shadowPressure < protectedPressure && protectedPressure < 0))
        {
            throw new InvalidOperationException(
                $"FrontPressure v0.2.36.2 verifier failed: direct={directPressure}, shadow={shadowPressure}, protected={protectedPressure}.");
        }

        CpuEvaluationProfile defenseOnly = FeatureOnlyProfile(goalDefense: 1000);
        GameEngine homeRowB1 = BuildStaticPosition(
            "GoalDefense B1",
            new[]
            {
                ".o...o..",
                "s.......",
                "........",
                "......S.",
                "........",
                "........",
                "........",
                "........"
            },
            PlayerId.Player1);
        GameEngine advancedB2 = BuildStaticPosition(
            "GoalDefense B2",
            new[]
            {
                ".....o..",
                "so......",
                "........",
                "......S.",
                "........",
                "........",
                "........",
                "........"
            },
            PlayerId.Player1);

        int defenseB1 = CpuPlayer.EvaluateDetailed(homeRowB1, PlayerId.Player2, defenseOnly).GoalDefense;
        int defenseB2 = CpuPlayer.EvaluateDetailed(advancedB2, PlayerId.Player2, defenseOnly).GoalDefense;
        if (defenseB1 <= 0 || defenseB1 != defenseB2)
        {
            throw new InvalidOperationException(
                $"GoalDefense target-aware verifier failed: B1={defenseB1}, B2={defenseB2}. " +
                "Advancing the irrelevant B-file blocker must not reduce coverage of the G1 threat while F1 still covers it.");
        }

        CpuEvaluationProfile connectionOnly = FeatureOnlyProfile(bridgeheadConnection: 1000);
        GameEngine disconnected = BuildStaticPosition(
            "Bridgehead disconnected",
            new[]
            {
                "........",
                "........",
                "...O....",
                "..OsO...",
                "...O....",
                "........",
                ".......S",
                "...o...."
            },
            PlayerId.Player2);
        GameEngine reachable = BuildStaticPosition(
            "Bridgehead reachable",
            new[]
            {
                "........",
                "........",
                "...O....",
                "...sO...",
                "...O....",
                "........",
                ".......S",
                "...o...."
            },
            PlayerId.Player2);

        int disconnectedConnection = CpuPlayer.EvaluateDetailed(disconnected, PlayerId.Player2, connectionOnly).BridgeheadConnection;
        int reachableConnection = CpuPlayer.EvaluateDetailed(reachable, PlayerId.Player2, connectionOnly).BridgeheadConnection;
        if (disconnectedConnection != 0 || reachableConnection <= disconnectedConnection)
        {
            throw new InvalidOperationException(
                $"BridgeheadConnection reachability verifier failed: disconnected={disconnectedConnection}, reachable={reachableConnection}.");
        }
    }

    private static CpuEvaluationProfile FeatureOnlyProfile(
        int frontPressure = 0,
        int goalDefense = 0,
        int bridgeheadConnection = 0)
    {
        var scales = CpuEvaluationFeatureScales.AllOff with
        {
            FrontPressure = frontPressure,
            GoalDefense = goalDefense,
            BridgeheadConnection = bridgeheadConnection
        };
        return new CpuEvaluationProfile("Verifier feature-only", scales, scales);
    }

    private static GameEngine BuildStaticPosition(string name, string[] rows, PlayerId currentPlayer) =>
        new(GameStartConfiguration.Create(name, "cpu-search-verifier", rows, currentPlayer));

    private static IReadOnlyList<(GameEngine Position, string Label)> BuildPositions()
    {
        var result = new List<(GameEngine, string)>();
        for (int seedIndex = 0; seedIndex < 4; seedIndex++)
        {
            int seed = 0x5A17 + seedIndex * 7919;
            var random = new Random(seed);
            var game = new GameEngine();
            result.Add((game.Clone(), $"standard seed={seed} ply=0"));

            for (int ply = 0; ply < 14 && game.Outcome == GameOutcome.Ongoing; ply++)
            {
                IReadOnlyList<Move> legal = game.GetLegalMoves();
                if (legal.Count == 0) break;
                Move move = legal[random.Next(legal.Count)];
                if (!game.TryApplyMove(move, out string? error))
                {
                    throw new InvalidOperationException($"Could not build verifier position seed={seed} ply={ply}: {error}");
                }

                if (ply is 4 or 9 && game.Outcome == GameOutcome.Ongoing)
                {
                    result.Add((game.Clone(), $"standard seed={seed} ply={ply + 1}"));
                }
            }
        }

        return result;
    }

    private static string ResolveReportDirectory()
    {
        string local = Path.Combine(AppContext.BaseDirectory, "verification_logs");
        try
        {
            Directory.CreateDirectory(local);
            string probe = Path.Combine(local, ".write_test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return local;
        }
        catch
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StarRunnerPrototype",
                "verification_logs");
        }
    }
}
