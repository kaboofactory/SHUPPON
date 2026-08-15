using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StarRunnerPrototype;

public sealed record HeadlessBatchOptions(
    int Games,
    int P1Depth,
    int P2Depth,
    int PerMoveTimeMilliseconds,
    long PerMoveNodeLimit,
    int Parallelism,
    int SearchParallelism,
    int ProgressIntervalMilliseconds,
    int MaxPlies,
    int OpeningRandomPlies,
    int OpeningTopK,
    int OpeningScoreWindow,
    int CycleBreakScoreWindow,
    int Seed,
    bool SaveMoveSequences,
    GameStartConfiguration? StartConfiguration = null,
    CpuSkillProfile? P1Skill = null,
    CpuSkillProfile? P2Skill = null,
    bool WriteLogs = true,
    CpuEvaluationProfile? P1EvaluationProfile = null,
    CpuEvaluationProfile? P2EvaluationProfile = null,
    ScenarioMovePolicy? MovePolicy = null)
{
    public HeadlessBatchOptions Normalize() => this with
    {
        Games = Math.Clamp(Games, 1, 100_000),
        P1Depth = Math.Clamp(P1Depth, 1, 10),
        P2Depth = Math.Clamp(P2Depth, 1, 10),
        PerMoveTimeMilliseconds = Math.Clamp(PerMoveTimeMilliseconds, 0, 120_000),
        PerMoveNodeLimit = Math.Clamp(PerMoveNodeLimit, 0L, 2_000_000_000L),
        Parallelism = Math.Clamp(Parallelism, 1, Math.Max(1, Environment.ProcessorCount * 2)),
        SearchParallelism = Math.Clamp(SearchParallelism, 1, Math.Max(1, Environment.ProcessorCount)),
        ProgressIntervalMilliseconds = Math.Clamp(ProgressIntervalMilliseconds, 500, 60_000),
        MaxPlies = Math.Clamp(MaxPlies, 20, 5000),
        OpeningRandomPlies = Math.Clamp(OpeningRandomPlies, 0, 40),
        OpeningTopK = Math.Clamp(OpeningTopK, 1, 12),
        OpeningScoreWindow = Math.Clamp(OpeningScoreWindow, 0, 100_000),
        CycleBreakScoreWindow = Math.Clamp(CycleBreakScoreWindow, 0, 1_000),
        StartConfiguration = StartConfiguration ?? GameStartConfiguration.Initial,
        P1Skill = P1Skill?.Normalize(),
        P2Skill = P2Skill?.Normalize(),
        P1EvaluationProfile = P1EvaluationProfile?.Normalize(),
        P2EvaluationProfile = P2EvaluationProfile?.Normalize()
    };
}

public sealed record HeadlessActiveGameProgress(int GameIndex, int Ply);

public sealed record HeadlessProgress(
    int CompletedGames,
    int TotalGames,
    double GamesPerSecond,
    double ElapsedSeconds,
    long ObservedPlies,
    int ActiveGames,
    IReadOnlyList<HeadlessActiveGameProgress> ActiveGamePlies,
    int P1Wins,
    int P2Wins,
    int Draws);

public sealed record HeadlessGameResult(
    int GameIndex,
    int Seed,
    string Outcome,
    string EndReason,
    int Plies,
    int P1Sacrifices,
    int P2Sacrifices,
    int P1RunnerMoves,
    int P2RunnerMoves,
    int P1BlockerMoves,
    int P2BlockerMoves,
    int P1BridgeheadEntries,
    int P2BridgeheadEntries,
    int P1FrontMarkedStates,
    int P2FrontMarkedStates,
    int P1BlockersRemaining,
    int P2BlockersRemaining,
    long P1Nodes,
    long P2Nodes,
    long P1TranspositionHits,
    long P2TranspositionHits,
    long P1SearchMilliseconds,
    long P2SearchMilliseconds,
    int P1NodeLimitStops,
    int P2NodeLimitStops,
    int P1TimeLimitStops,
    int P2TimeLimitStops,
    double P1AverageCompletedDepth,
    double P2AverageCompletedDepth,
    int P1SacrificeGoalEntries,
    int P2SacrificeGoalEntries,
    int P1StrategyDeviationMoves,
    int P2StrategyDeviationMoves,
    int P1CycleAvoidanceChoices,
    int P2CycleAvoidanceChoices,
    int P1RunnerAdvancePreferenceChoices,
    int P2RunnerAdvancePreferenceChoices,
    int P1PreferenceScoreConcession,
    int P2PreferenceScoreConcession,
    IReadOnlyList<string>? Moves);

public sealed record HeadlessBatchResult(
    string BatchLogPath,
    string LatestCsvPath,
    int CompletedGames,
    int RequestedGames,
    bool Cancelled,
    long ElapsedMilliseconds,
    int P1Wins,
    int P2Wins,
    int Draws,
    int MoveLimits,
    int GoalReached,
    int RunnerImmobilized,
    int StrategyConstraintNoMoves,
    int FourfoldRepetitions,
    double AveragePlies,
    double GamesPerSecond,
    int NodeLimitStops,
    int TimeLimitStops,
    double P1AverageCompletedDepth,
    double P2AverageCompletedDepth);

public static class HeadlessBatchRunner
{
    public const int MaxBatchLogFiles = 500;
    private static readonly object LogWriteLock = new();

    public static HeadlessBatchResult Run(
        HeadlessBatchOptions options,
        IProgress<HeadlessProgress>? progress,
        CancellationToken cancellationToken)
    {
        options = options.Normalize();
        DateTimeOffset batchStartedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var results = new ConcurrentBag<HeadlessGameResult>();
        var activePlies = new ConcurrentDictionary<int, int>();
        int completed = 0;
        long completedPlies = 0;
        int liveP1Wins = 0;
        int liveP2Wins = 0;
        int liveDraws = 0;
        object progressStateLock = new();
        bool cancelled = false;

        void ReportProgress()
        {
            try
            {
                int done;
                long donePlies;
                int p1WinsSnapshot;
                int p2WinsSnapshot;
                int drawsSnapshot;
                lock (progressStateLock)
                {
                    done = completed;
                    donePlies = completedPlies;
                    p1WinsSnapshot = liveP1Wins;
                    p2WinsSnapshot = liveP2Wins;
                    drawsSnapshot = liveDraws;
                }
                HeadlessActiveGameProgress[] active = activePlies
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new HeadlessActiveGameProgress(pair.Key, pair.Value))
                    .ToArray();
                long observed = donePlies + activePlies.Values.Sum(value => (long)value);
                double gps = stopwatch.Elapsed.TotalSeconds > 0
                    ? done / stopwatch.Elapsed.TotalSeconds
                    : 0;

                progress?.Report(new HeadlessProgress(
                    done,
                    options.Games,
                    gps,
                    stopwatch.Elapsed.TotalSeconds,
                    observed,
                    activePlies.Count,
                    active,
                    p1WinsSnapshot,
                    p2WinsSnapshot,
                    drawsSnapshot));
            }
            catch
            {
                // Progress reporting must never stop a batch.
            }
        }

        using var progressTimer = new System.Threading.Timer(
            _ => ReportProgress(),
            null,
            options.ProgressIntervalMilliseconds,
            options.ProgressIntervalMilliseconds);

        try
        {
            Parallel.ForEach(
                Enumerable.Range(0, options.Games),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = options.Parallelism,
                    CancellationToken = cancellationToken
                },
                gameIndex =>
                {
                    activePlies[gameIndex] = 0;

                    try
                    {
                        HeadlessGameResult result = RunSingleGame(
                            options,
                            gameIndex,
                            ply => activePlies[gameIndex] = ply,
                            cancellationToken);
                        lock (progressStateLock)
                        {
                            results.Add(result);
                            if (result.Outcome == GameOutcome.Player1Win.ToString())
                            {
                                liveP1Wins++;
                            }
                            else if (result.Outcome == GameOutcome.Player2Win.ToString())
                            {
                                liveP2Wins++;
                            }
                            else if (result.Outcome == GameOutcome.Draw.ToString())
                            {
                                liveDraws++;
                            }
                            completedPlies += result.Plies;
                            completed++;
                        }
                    }
                    finally
                    {
                        activePlies.TryRemove(gameIndex, out _);
                    }
                });
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        progressTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        ReportProgress();
        stopwatch.Stop();
        HeadlessGameResult[] ordered = results.OrderBy(r => r.GameIndex).ToArray();
        BatchSummary summary = BuildSummary(
            options,
            ordered,
            cancelled,
            batchStartedAt,
            stopwatch.ElapsedMilliseconds);
        string batchPath = string.Empty;
        string latestCsvPath = string.Empty;

        if (options.WriteLogs)
        {
            // Scenario suites may execute multiple one-game batches concurrently. Keep
            // only the short file-write/migration section serialized; CPU search stays parallel.
            lock (LogWriteLock)
            {
                string directory = ResolveAnalysisDirectory();
                Directory.CreateDirectory(directory);
                RotateBatchLogs(directory, MaxBatchLogFiles - 1);

                string batchId = Guid.NewGuid().ToString("N");
                string scenarioTag = MakeSafeFileTag((options.StartConfiguration ?? GameStartConfiguration.Initial).Name);
                batchPath = Path.Combine(
                    directory,
                    $"batch_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{scenarioTag}_{batchId[..8]}.jsonl");
                latestCsvPath = Path.Combine(directory, "headless_latest.csv");

                WriteBatchLog(batchPath, options, ordered, summary, batchStartedAt);
                AppendHistoryCsv(latestCsvPath, batchId, summary, ordered);
                RotateBatchLogs(directory, MaxBatchLogFiles);
            }
        }

        return new HeadlessBatchResult(
            batchPath,
            latestCsvPath,
            ordered.Length,
            options.Games,
            cancelled,
            stopwatch.ElapsedMilliseconds,
            summary.P1Wins,
            summary.P2Wins,
            summary.Draws,
            summary.MoveLimits,
            CountEndReason(summary, EndReason.GoalReached.ToString()),
            CountEndReason(summary, EndReason.RunnerImmobilized.ToString()),
            CountEndReason(summary, EndReason.MovePolicyNoMove.ToString()),
            CountEndReason(summary, EndReason.FourfoldRepetition.ToString()),
            summary.AveragePlies,
            summary.GamesPerSecond,
            ordered.Sum(r => r.P1NodeLimitStops + r.P2NodeLimitStops),
            ordered.Sum(r => r.P1TimeLimitStops + r.P2TimeLimitStops),
            ordered.Length > 0 ? ordered.Average(r => r.P1AverageCompletedDepth) : 0,
            ordered.Length > 0 ? ordered.Average(r => r.P2AverageCompletedDepth) : 0);
    }

    private static int CountEndReason(BatchSummary summary, string endReason) =>
        summary.EndReasons.TryGetValue(endReason, out int count) ? count : 0;

    public static string ResolveAnalysisDirectory()
    {
        string besideExe = Path.Combine(AppContext.BaseDirectory, "analysis_logs");
        try
        {
            Directory.CreateDirectory(besideExe);
            string probe = Path.Combine(besideExe, $".write_test_{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return besideExe;
        }
        catch
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StarRunnerPrototype",
                "analysis_logs");
        }
    }

    // EvaluationTuner needs pair-level control so it can stop a deep match as soon
    // as the promotion threshold becomes mathematically unreachable. Keep this
    // internal: it is not part of the embedding API.
    internal static HeadlessGameResult RunSingleGameForTuning(
        HeadlessBatchOptions options,
        int gameIndex,
        CancellationToken cancellationToken)
    {
        return RunSingleGame(options.Normalize(), gameIndex, onPlyProgress: null, cancellationToken: cancellationToken);
    }

    private static HeadlessGameResult RunSingleGame(
        HeadlessBatchOptions options,
        int gameIndex,
        Action<int>? onPlyProgress,
        CancellationToken cancellationToken)
    {
        GameStartConfiguration startConfiguration = options.StartConfiguration ?? GameStartConfiguration.Initial;
        ScenarioMovePolicy? movePolicy = options.MovePolicy?.IsActive == true ? options.MovePolicy : null;
        var game = new GameEngine(startConfiguration, movePolicy);
        int gameSeed = unchecked(options.Seed + gameIndex * 104729);
        int p1InitialDeviationBudget = movePolicy?.P1.DeviationBudget ?? 0;
        int p2InitialDeviationBudget = movePolicy?.P2.DeviationBudget ?? 0;
        int p1Sacrifices = 0;
        int p2Sacrifices = 0;
        int p1RunnerMoves = 0;
        int p2RunnerMoves = 0;
        int p1BlockerMoves = 0;
        int p2BlockerMoves = 0;
        int p1Bridgeheads = 0;
        int p2Bridgeheads = 0;
        int p1FrontMarked = 0;
        int p2FrontMarked = 0;
        long p1Nodes = 0;
        long p2Nodes = 0;
        long p1TtHits = 0;
        long p2TtHits = 0;
        long p1Ms = 0;
        long p2Ms = 0;
        long p1DepthSum = 0;
        long p2DepthSum = 0;
        int p1NodeLimitStops = 0;
        int p2NodeLimitStops = 0;
        int p1TimeLimitStops = 0;
        int p2TimeLimitStops = 0;
        int p1Decisions = 0;
        int p2Decisions = 0;
        int p1SacrificeGoalEntries = 0;
        int p2SacrificeGoalEntries = 0;
        int p1CycleAvoidanceChoices = 0;
        int p2CycleAvoidanceChoices = 0;
        int p1RunnerAdvancePreferenceChoices = 0;
        int p2RunnerAdvancePreferenceChoices = 0;
        int p1PreferenceScoreConcession = 0;
        int p2PreferenceScoreConcession = 0;
        List<string>? moves = options.SaveMoveSequences ? new List<string>(Math.Min(options.MaxPlies, 256)) : null;

        while (game.Outcome == GameOutcome.Ongoing && game.PlyCount < options.MaxPlies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlayerId player = game.CurrentPlayer;
            CpuSkillProfile? skill = player == PlayerId.Player1 ? options.P1Skill : options.P2Skill;
            int requestedDepth = skill?.MaxDepth ?? (player == PlayerId.Player1 ? options.P1Depth : options.P2Depth);
            bool openingRandom = game.PlyCount < options.OpeningRandomPlies && options.OpeningTopK > 1;
            int searchDepth = openingRandom ? (skill is null ? Math.Min(2, requestedDepth) : 2) : requestedDepth;
            int moveSeed = unchecked(gameSeed ^ (game.PlyCount * 7919) ^ ((int)player * 0x45d9f3b));

            CpuSearchOptions searchOptions;
            if (openingRandom)
            {
                searchOptions = new CpuSearchOptions(
                    MaxDepth: searchDepth,
                    TimeLimitMilliseconds: options.PerMoveTimeMilliseconds,
                    MaxNodes: options.PerMoveNodeLimit,
                    UseTranspositionTable: false,
                    CollectExactRootScores: true,
                    RandomTopK: options.OpeningTopK,
                    RandomScoreWindow: options.OpeningScoreWindow,
                    RandomSelectionTemperature: 0,
                    RandomSeed: moveSeed,
                    CycleBreakScoreWindow: options.CycleBreakScoreWindow,
                    MaxParallelism: options.SearchParallelism,
                    UseBelowNormalThreadPriority: true);
            }
            else if (skill is not null)
            {
                searchOptions = skill.ToSearchOptions(
                    moveSeed,
                    options.SearchParallelism,
                    options.PerMoveTimeMilliseconds,
                    options.PerMoveNodeLimit,
                    options.CycleBreakScoreWindow,
                    useBelowNormalThreadPriority: true);
            }
            else
            {
                searchOptions = new CpuSearchOptions(
                    MaxDepth: searchDepth,
                    TimeLimitMilliseconds: options.PerMoveTimeMilliseconds,
                    MaxNodes: options.PerMoveNodeLimit,
                    UseTranspositionTable: true,
                    CollectExactRootScores: false,
                    RandomTopK: 1,
                    RandomScoreWindow: 0,
                    RandomSeed: moveSeed,
                    CycleBreakScoreWindow: options.CycleBreakScoreWindow,
                    MaxParallelism: options.SearchParallelism,
                    UseBelowNormalThreadPriority: true);
            }

            CpuEvaluationProfile evaluationProfile = player == PlayerId.Player1
                ? options.P1EvaluationProfile ?? CpuEvaluationProfileProvider.Current
                : options.P2EvaluationProfile ?? CpuEvaluationProfileProvider.Current;
            searchOptions = searchOptions with { EvaluationProfile = evaluationProfile };

            CpuDecision decision = CpuPlayer.DecideMove(game, player, searchOptions, cancellationToken);
            Piece movingPiece = game.GetPiece(decision.Move.From)
                ?? throw new InvalidOperationException("CPU selected a move from an empty square.");

            if (movingPiece.Type == PieceType.Runner && decision.Move.Kind == MoveKind.Sacrifice)
            {
                int goalRow = player == PlayerId.Player1 ? 0 : GameEngine.BoardSize - 1;
                if (decision.Move.To.Row == goalRow)
                {
                    if (player == PlayerId.Player1) p1SacrificeGoalEntries++; else p2SacrificeGoalEntries++;
                }
            }

            if (player == PlayerId.Player1)
            {
                p1Nodes += decision.Nodes;
                p1TtHits += decision.TranspositionHits;
                p1Ms += decision.ElapsedMilliseconds;
                p1DepthSum += decision.Depth;
                if (decision.NodeLimitReached) p1NodeLimitStops++;
                if (decision.TimedOut) p1TimeLimitStops++;
                if (decision.CycleAvoidanceApplied) p1CycleAvoidanceChoices++;
                if (decision.RunnerAdvancePreferenceApplied) p1RunnerAdvancePreferenceChoices++;
                p1PreferenceScoreConcession += decision.PreferenceScoreConcession;
                p1Decisions++;
            }
            else
            {
                p2Nodes += decision.Nodes;
                p2TtHits += decision.TranspositionHits;
                p2Ms += decision.ElapsedMilliseconds;
                p2DepthSum += decision.Depth;
                if (decision.NodeLimitReached) p2NodeLimitStops++;
                if (decision.TimedOut) p2TimeLimitStops++;
                if (decision.CycleAvoidanceApplied) p2CycleAvoidanceChoices++;
                if (decision.RunnerAdvancePreferenceApplied) p2RunnerAdvancePreferenceChoices++;
                p2PreferenceScoreConcession += decision.PreferenceScoreConcession;
                p2Decisions++;
            }

            if (decision.Move.Kind == MoveKind.Sacrifice)
            {
                if (player == PlayerId.Player1) p1Sacrifices++; else p2Sacrifices++;
            }

            if (movingPiece.Type == PieceType.Runner)
            {
                if (player == PlayerId.Player1) p1RunnerMoves++; else p2RunnerMoves++;
            }
            else
            {
                if (player == PlayerId.Player1) p1BlockerMoves++; else p2BlockerMoves++;
                int enemyGoalRow = player == PlayerId.Player1 ? 0 : 7;
                if (decision.Move.To.Row == enemyGoalRow && decision.Move.From.Row != enemyGoalRow)
                {
                    if (player == PlayerId.Player1) p1Bridgeheads++; else p2Bridgeheads++;
                }
            }

            int plyBefore = game.PlyCount;
            game.ApplyGeneratedMove(decision.Move);
            onPlyProgress?.Invoke(game.PlyCount);
            moves?.Add(
                $"{plyBefore + 1}:{player.ShortName()}:{decision.Move.ToNotation()}:d{decision.Depth}:s{decision.Score}:n{decision.Nodes}" +
                $":cb{(decision.CycleAvoidanceApplied ? 1 : 0)}:ro{(decision.RunnerOscillationAvoidanceApplied ? 1 : 0)}:ra{(decision.RunnerAdvancePreferenceApplied ? 1 : 0)}:loss{decision.PreferenceScoreConcession}");

            if (game.Outcome == GameOutcome.Ongoing)
            {
                if (game.IsRunnerFrontMarked(PlayerId.Player1)) p1FrontMarked++;
                if (game.IsRunnerFrontMarked(PlayerId.Player2)) p2FrontMarked++;
            }
        }

        string outcome;
        string endReason;
        if (game.Outcome == GameOutcome.Ongoing)
        {
            outcome = "MoveLimit";
            endReason = "MoveLimit";
        }
        else
        {
            outcome = game.Outcome.ToString();
            endReason = game.EndReason.ToString();
        }

        return new HeadlessGameResult(
            gameIndex,
            gameSeed,
            outcome,
            endReason,
            game.PlyCount,
            p1Sacrifices,
            p2Sacrifices,
            p1RunnerMoves,
            p2RunnerMoves,
            p1BlockerMoves,
            p2BlockerMoves,
            p1Bridgeheads,
            p2Bridgeheads,
            p1FrontMarked,
            p2FrontMarked,
            game.CountBlockers(PlayerId.Player1),
            game.CountBlockers(PlayerId.Player2),
            p1Nodes,
            p2Nodes,
            p1TtHits,
            p2TtHits,
            p1Ms,
            p2Ms,
            p1NodeLimitStops,
            p2NodeLimitStops,
            p1TimeLimitStops,
            p2TimeLimitStops,
            p1Decisions > 0 ? (double)p1DepthSum / p1Decisions : 0,
            p2Decisions > 0 ? (double)p2DepthSum / p2Decisions : 0,
            p1SacrificeGoalEntries,
            p2SacrificeGoalEntries,
            Math.Max(0, p1InitialDeviationBudget - (movePolicy?.GetDeviationBudgetRemaining(game, PlayerId.Player1) ?? 0)),
            Math.Max(0, p2InitialDeviationBudget - (movePolicy?.GetDeviationBudgetRemaining(game, PlayerId.Player2) ?? 0)),
            p1CycleAvoidanceChoices,
            p2CycleAvoidanceChoices,
            p1RunnerAdvancePreferenceChoices,
            p2RunnerAdvancePreferenceChoices,
            p1PreferenceScoreConcession,
            p2PreferenceScoreConcession,
            moves);
    }

    private static BatchSummary BuildSummary(
        HeadlessBatchOptions options,
        IReadOnlyList<HeadlessGameResult> results,
        bool cancelled,
        DateTimeOffset startedAt,
        long elapsedMs)
    {
        int p1Wins = results.Count(r => r.Outcome == GameOutcome.Player1Win.ToString());
        int p2Wins = results.Count(r => r.Outcome == GameOutcome.Player2Win.ToString());
        int draws = results.Count(r => r.Outcome == GameOutcome.Draw.ToString());
        int moveLimits = results.Count(r => r.Outcome == "MoveLimit");
        double averagePlies = results.Count > 0 ? results.Average(r => r.Plies) : 0;
        double gps = elapsedMs > 0 ? results.Count * 1000.0 / elapsedMs : 0;
        long totalNodes = results.Sum(r => r.P1Nodes + r.P2Nodes);
        long ttHits = results.Sum(r => r.P1TranspositionHits + r.P2TranspositionHits);

        return new BatchSummary(
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
            startedAt + TimeSpan.FromMilliseconds(elapsedMs),
            options,
            results.Count,
            cancelled,
            elapsedMs,
            gps,
            p1Wins,
            p2Wins,
            draws,
            moveLimits,
            averagePlies,
            results.Count > 0 ? results.Average(r => r.P1Sacrifices) : 0,
            results.Count > 0 ? results.Average(r => r.P2Sacrifices) : 0,
            results.Count > 0 ? results.Average(r => r.P1BridgeheadEntries) : 0,
            results.Count > 0 ? results.Average(r => r.P2BridgeheadEntries) : 0,
            totalNodes,
            ttHits,
            elapsedMs > 0 ? totalNodes * 1000.0 / elapsedMs : 0,
            results.Sum(r => r.P1CycleAvoidanceChoices + r.P2CycleAvoidanceChoices),
            results.Sum(r => r.P1RunnerAdvancePreferenceChoices + r.P2RunnerAdvancePreferenceChoices),
            results.Sum(r => r.P1PreferenceScoreConcession + r.P2PreferenceScoreConcession),
            results.GroupBy(r => r.EndReason).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal));
    }

    private static void WriteBatchLog(
        string path,
        HeadlessBatchOptions options,
        IReadOnlyList<HeadlessGameResult> results,
        BatchSummary summary,
        DateTimeOffset batchStartedAt)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        WriteJsonLine(writer, "batch_started", new
        {
            schemaVersion = 12,
            rulesId = RuleSet.Standard.Id,
            options,
            evaluationProfiles = new
            {
                p1 = (options.P1EvaluationProfile ?? CpuEvaluationProfileProvider.Current).Name,
                p2 = (options.P2EvaluationProfile ?? CpuEvaluationProfileProvider.Current).Name
            },
            note = "Headless games do not create one game_*.jsonl per game. This batch file contains all per-game results; optional move sequences are embedded in each game_result."
        }, batchStartedAt.ToUniversalTime());

        foreach (HeadlessGameResult result in results)
        {
            WriteJsonLine(writer, "game_result", result);
        }

        WriteJsonLine(writer, "batch_summary", summary);
    }

    private static void WriteJsonLine(
        StreamWriter writer,
        string eventType,
        object payload,
        DateTimeOffset? timestampUtc = null)
    {
        writer.WriteLine(JsonSerializer.Serialize(new
        {
            timestampUtc = (timestampUtc ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            eventType,
            payload
        }, JsonOptions));
    }

    private const string HistoryCsvHeader =
        "appVersion,batchId,rulesId,batchCompletedAt,games,p1Depth,p2Depth,perMoveTimeMilliseconds,perMoveNodeLimit,parallelism,searchParallelism,progressIntervalMilliseconds,maxPlies,openingRandomPlies,openingTopK,openingScoreWindow,batchSeed,saveMoveSequences,gameIndex,seed,outcome,endReason,plies,p1Sacrifices,p2Sacrifices,p1RunnerMoves,p2RunnerMoves,p1BlockerMoves,p2BlockerMoves,p1BridgeheadEntries,p2BridgeheadEntries,p1FrontMarkedStates,p2FrontMarkedStates,p1BlockersRemaining,p2BlockersRemaining,p1Nodes,p2Nodes,p1TranspositionHits,p2TranspositionHits,p1SearchMs,p2SearchMs,p1NodeLimitStops,p2NodeLimitStops,p1TimeLimitStops,p2TimeLimitStops,p1AvgDepth,p2AvgDepth,p1SacrificeGoalEntries,p2SacrificeGoalEntries,p1EvaluationProfile,p2EvaluationProfile,scenarioName,scenarioSource,scenarioHash,startPlayer,startBoard,p1Strategy,p1AttackBlockerStart,p1DeviationBudget,p2Strategy,p2AttackBlockerStart,p2DeviationBudget,p1StrategyDeviationMoves,p2StrategyDeviationMoves";

    private static void AppendHistoryCsv(
        string path,
        string batchId,
        BatchSummary summary,
        IReadOnlyList<HeadlessGameResult> results)
    {
        PrepareHistoryCsv(path);

        bool writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: writeHeader));

        if (writeHeader)
        {
            writer.WriteLine(HistoryCsvHeader);
        }

        HeadlessBatchOptions options = summary.Options;
        GameStartConfiguration start = options.StartConfiguration ?? GameStartConfiguration.Initial;
        string completedAt = summary.CompletedAt.ToString("O", CultureInfo.InvariantCulture);

        foreach (HeadlessGameResult r in results)
        {
            writer.WriteLine(string.Join(",", new string[]
            {
                Csv(summary.AppVersion),
                Csv(batchId),
                Csv(RuleSet.Standard.Id),
                Csv(completedAt),
                options.Games.ToString(CultureInfo.InvariantCulture),
                options.P1Depth.ToString(CultureInfo.InvariantCulture),
                options.P2Depth.ToString(CultureInfo.InvariantCulture),
                options.PerMoveTimeMilliseconds.ToString(CultureInfo.InvariantCulture),
                options.PerMoveNodeLimit.ToString(CultureInfo.InvariantCulture),
                options.Parallelism.ToString(CultureInfo.InvariantCulture),
                options.SearchParallelism.ToString(CultureInfo.InvariantCulture),
                options.ProgressIntervalMilliseconds.ToString(CultureInfo.InvariantCulture),
                options.MaxPlies.ToString(CultureInfo.InvariantCulture),
                options.OpeningRandomPlies.ToString(CultureInfo.InvariantCulture),
                options.OpeningTopK.ToString(CultureInfo.InvariantCulture),
                options.OpeningScoreWindow.ToString(CultureInfo.InvariantCulture),
                options.Seed.ToString(CultureInfo.InvariantCulture),
                options.SaveMoveSequences ? "true" : "false",
                r.GameIndex.ToString(CultureInfo.InvariantCulture),
                r.Seed.ToString(CultureInfo.InvariantCulture),
                Csv(r.Outcome),
                Csv(r.EndReason),
                r.Plies.ToString(CultureInfo.InvariantCulture),
                r.P1Sacrifices.ToString(CultureInfo.InvariantCulture),
                r.P2Sacrifices.ToString(CultureInfo.InvariantCulture),
                r.P1RunnerMoves.ToString(CultureInfo.InvariantCulture),
                r.P2RunnerMoves.ToString(CultureInfo.InvariantCulture),
                r.P1BlockerMoves.ToString(CultureInfo.InvariantCulture),
                r.P2BlockerMoves.ToString(CultureInfo.InvariantCulture),
                r.P1BridgeheadEntries.ToString(CultureInfo.InvariantCulture),
                r.P2BridgeheadEntries.ToString(CultureInfo.InvariantCulture),
                r.P1FrontMarkedStates.ToString(CultureInfo.InvariantCulture),
                r.P2FrontMarkedStates.ToString(CultureInfo.InvariantCulture),
                r.P1BlockersRemaining.ToString(CultureInfo.InvariantCulture),
                r.P2BlockersRemaining.ToString(CultureInfo.InvariantCulture),
                r.P1Nodes.ToString(CultureInfo.InvariantCulture),
                r.P2Nodes.ToString(CultureInfo.InvariantCulture),
                r.P1TranspositionHits.ToString(CultureInfo.InvariantCulture),
                r.P2TranspositionHits.ToString(CultureInfo.InvariantCulture),
                r.P1SearchMilliseconds.ToString(CultureInfo.InvariantCulture),
                r.P2SearchMilliseconds.ToString(CultureInfo.InvariantCulture),
                r.P1NodeLimitStops.ToString(CultureInfo.InvariantCulture),
                r.P2NodeLimitStops.ToString(CultureInfo.InvariantCulture),
                r.P1TimeLimitStops.ToString(CultureInfo.InvariantCulture),
                r.P2TimeLimitStops.ToString(CultureInfo.InvariantCulture),
                r.P1AverageCompletedDepth.ToString("0.###", CultureInfo.InvariantCulture),
                r.P2AverageCompletedDepth.ToString("0.###", CultureInfo.InvariantCulture),
                r.P1SacrificeGoalEntries.ToString(CultureInfo.InvariantCulture),
                r.P2SacrificeGoalEntries.ToString(CultureInfo.InvariantCulture),
                Csv((options.P1EvaluationProfile ?? CpuEvaluationProfileProvider.Current).Name),
                Csv((options.P2EvaluationProfile ?? CpuEvaluationProfileProvider.Current).Name),
                Csv(start.Name),
                Csv(start.SourceName),
                Csv(start.Hash),
                start.CurrentPlayer.ShortName(),
                Csv(start.BoardSignature),
                Csv((options.MovePolicy?.P1.Mode ?? StrategyMode.Free).ToString()),
                Csv(options.MovePolicy?.P1.AttackBlockerPosition?.ToCoordinate() ?? string.Empty),
                (options.MovePolicy?.P1.DeviationBudget ?? 0).ToString(CultureInfo.InvariantCulture),
                Csv((options.MovePolicy?.P2.Mode ?? StrategyMode.Free).ToString()),
                Csv(options.MovePolicy?.P2.AttackBlockerPosition?.ToCoordinate() ?? string.Empty),
                (options.MovePolicy?.P2.DeviationBudget ?? 0).ToString(CultureInfo.InvariantCulture),
                r.P1StrategyDeviationMoves.ToString(CultureInfo.InvariantCulture),
                r.P2StrategyDeviationMoves.ToString(CultureInfo.InvariantCulture)
            }));
        }
    }

    private static void PrepareHistoryCsv(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            return;
        }

        string? firstLine;
        using (var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            firstLine = reader.ReadLine();
        }

        firstLine = firstLine?.TrimStart('\uFEFF');
        if (string.Equals(firstLine, HistoryCsvHeader, StringComparison.Ordinal))
        {
            return;
        }

        // v0.2.17以前のCSVは旧実験ルール列を含むため混在させない。
        // データは削除せず別名で保存し、新しいStandard固定スキーマを開始する。
        string backupPath = Path.Combine(
            Path.GetDirectoryName(path) ?? ".",
            $"headless_latest_legacy_{DateTime.Now:yyyyMMdd_HHmmss_fff}.csv");
        File.Move(path, backupPath, overwrite: false);
    }

    private static string Csv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }
        return '"' + value.Replace("\"", "\"\"") + '"';
    }

    private static string MakeSafeFileTag(string value)
    {
        var sb = new StringBuilder();
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c is '-' or '_') sb.Append(c);
            else if (char.IsWhiteSpace(c) && (sb.Length == 0 || sb[^1] != '_')) sb.Append('_');
            if (sb.Length >= 28) break;
        }
        string result = sb.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(result) ? "scenario" : result;
    }

    private static void RotateBatchLogs(string directory, int maxFiles)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            FileInfo[] files = new DirectoryInfo(directory)
                .GetFiles("batch_*.jsonl", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.CreationTimeUtc)
                .ThenBy(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.Name, StringComparer.Ordinal)
                .ToArray();

            int excess = files.Length - Math.Max(0, maxFiles);
            for (int i = 0; i < excess; i++)
            {
                try { files[i].Delete(); } catch { }
            }
        }
        catch
        {
            // Analysis logging must never break the game.
        }
    }

    private sealed record BatchSummary(
        string AppVersion,
        DateTimeOffset CompletedAt,
        HeadlessBatchOptions Options,
        int CompletedGames,
        bool Cancelled,
        long ElapsedMilliseconds,
        double GamesPerSecond,
        int P1Wins,
        int P2Wins,
        int Draws,
        int MoveLimits,
        double AveragePlies,
        double AverageP1Sacrifices,
        double AverageP2Sacrifices,
        double AverageP1BridgeheadEntries,
        double AverageP2BridgeheadEntries,
        long TotalNodes,
        long TranspositionHits,
        double NodesPerSecond,
        int CycleAvoidanceChoices,
        int RunnerAdvancePreferenceChoices,
        int PreferenceScoreConcessionTotal,
        IReadOnlyDictionary<string, int> EndReasons);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
