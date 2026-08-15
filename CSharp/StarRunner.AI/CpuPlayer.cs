using System.Buffers;
using System.Diagnostics;
using System.Numerics;

using StarRunner.Core;

namespace StarRunner.AI;

public sealed record CpuSearchOptions(
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
{
    public CpuSearchOptions Normalize() => this with
    {
        MaxDepth = Math.Clamp(MaxDepth, 1, 99),
        TimeLimitMilliseconds = Math.Clamp(TimeLimitMilliseconds, 0, 120_000),
        MaxNodes = Math.Clamp(MaxNodes, 0L, 2_000_000_000L),
        RandomTopK = Math.Clamp(RandomTopK, 1, 16),
        RandomScoreWindow = Math.Clamp(RandomScoreWindow, 0, 100_000),
        RandomSelectionTemperature = Math.Clamp(RandomSelectionTemperature, 0, 100_000),
        RandomMoveProbability = Math.Clamp(RandomMoveProbability, 0.0, 1.0),
        CycleBreakScoreWindow = Math.Clamp(CycleBreakScoreWindow, 0, 1_000),
        MaxParallelism = Math.Clamp(MaxParallelism, 1, Math.Max(1, Environment.ProcessorCount)),
        MaxOnlySurvivalExtensionsPerLine = Math.Clamp(MaxOnlySurvivalExtensionsPerLine, 0, 4),
        MaxAdaptiveRootDeepeningPly = Math.Clamp(MaxAdaptiveRootDeepeningPly, 0, 12),
        MateDistanceScoutMinCompletedDepth = Math.Clamp(MateDistanceScoutMinCompletedDepth, 1, 32),
        MaxMateDistanceScoutExtraPly = Math.Clamp(MaxMateDistanceScoutExtraPly, 0, 32)
    };
}

public readonly record struct CpuCandidate(Move Move, int SearchScore, string Bound);

public readonly record struct CpuSearchTelemetry(
    long PvsNullWindowProbes,
    long PvsResearches,
    long LmrReducedSearches,
    long LmrVerificationResearches,
    long MateDistancePrunes,
    long OnlySurvivalExtensions,
    long AdaptiveRootDeepeningPasses,
    int MaxAdaptiveRootDeepeningPlyReached)
{
    // Keep the existing positional constructor / Deconstruct shape source-compatible.
    // Scout diagnostics are additive init properties so the existing positional API remains stable.
    public long MateDistanceScoutProbes { get; init; }
    public long MateDistanceScoutNodes { get; init; }
    // Highest depth whose probe was started. Kept for source compatibility with v0.2.37.0.
    public int MateDistanceScoutMaxDepthReached { get; init; }
    // Highest depth whose probe completed without hitting the shared node/time/cancel limit.
    public int MateDistanceScoutMaxCompletedDepth { get; init; }
    // +1 = hand-side forced-win probe, -1 = hand-side forced-loss probe, 0 = Scout unused.
    public int MateDistanceScoutDirection { get; init; }
    public int MateDistanceScoutProofDepth { get; init; }
    public int MateDistanceScoutProofScore { get; init; }
    // false means the Scout proved a forced result within ProofDepth but ran out of
    // budget before proving that distance minimal/exact.
    public bool MateDistanceScoutProofExact { get; init; }
    public IReadOnlyList<CpuMateScoutProbeTelemetry>? MateDistanceScoutProbeDetails { get; init; }
}


public readonly record struct CpuSearchProgress(
    int Score,
    Move BestMove,
    int ScoreDepth,
    int TargetDepth,
    bool IsProvisional);

public readonly record struct CpuCompletedDepth(
    int Depth,
    int Score,
    Move BestMove,
    long Nodes,
    long ElapsedMilliseconds);

public readonly record struct CpuMateScoutProbeTelemetry(
    int Depth,
    bool ProbingWin,
    bool Refining,
    bool Completed,
    bool Proven,
    long Nodes);

public readonly record struct CpuMateScoutProgress(
    int Depth,
    bool ProbingWin)
{
    public bool Refining { get; init; }
}

public sealed class CpuSearchMonitor
{
    private readonly object _gate = new();
    private bool _hasScore;
    private int _score;
    private Move _bestMove;
    private int _scoreDepth;
    private int _targetDepth;
    private bool _isProvisional;
    private bool _mateScoutActive;
    private int _mateScoutDepth;
    private bool _mateScoutProbingWin;
    private bool _mateScoutRefining;
    private readonly List<CpuCompletedDepth> _completedDepths = new();

    public IReadOnlyList<CpuCompletedDepth> GetCompletedDepths()
    {
        lock (_gate)
        {
            return _completedDepths.ToArray();
        }
    }

    public bool TryGetSnapshot(out CpuSearchProgress progress)
    {
        lock (_gate)
        {
            if (!_hasScore)
            {
                progress = default;
                return false;
            }

            progress = new CpuSearchProgress(_score, _bestMove, _scoreDepth, _targetDepth, _isProvisional);
            return true;
        }
    }

    public bool TryGetMateScoutSnapshot(out CpuMateScoutProgress progress)
    {
        lock (_gate)
        {
            if (!_mateScoutActive)
            {
                progress = default;
                return false;
            }

            progress = new CpuMateScoutProgress(_mateScoutDepth, _mateScoutProbingWin) { Refining = _mateScoutRefining };
            return true;
        }
    }

    internal void BeginMateScoutProbe(int depth, bool probingWin, bool refining = false)
    {
        lock (_gate)
        {
            _mateScoutActive = true;
            _mateScoutDepth = depth;
            _mateScoutProbingWin = probingWin;
            _mateScoutRefining = refining;
            _isProvisional = false;
        }
    }

    internal void EndMateScout()
    {
        lock (_gate)
        {
            _mateScoutActive = false;
        }
    }

    internal void SetFallback(Move move, int score)
    {
        lock (_gate)
        {
            _hasScore = true;
            _score = score;
            _bestMove = move;
            _scoreDepth = 0;
            _targetDepth = 1;
            _isProvisional = true;
        }
    }

    internal void BeginDepth(int depth)
    {
        lock (_gate)
        {
            _targetDepth = depth;
        }
    }

    internal void ReportRootCandidate(int depth, Move move, int score)
    {
        lock (_gate)
        {
            if (!_hasScore || _scoreDepth != depth || !_isProvisional)
            {
                _hasScore = true;
                _score = score;
                _bestMove = move;
                _scoreDepth = depth;
                _targetDepth = depth;
                _isProvisional = true;
                return;
            }

            // The root is maximizing for the CPU perspective. During one depth,
            // only publish monotonic improvements so parallel workers cannot make
            // the visible score jump backwards merely because they finish out of order.
            if (score > _score)
            {
                _score = score;
                _bestMove = move;
            }
        }
    }

    internal void CompleteDepth(int depth, Move move, int score, long nodes, long elapsedMilliseconds)
    {
        lock (_gate)
        {
            _hasScore = true;
            _score = score;
            _bestMove = move;
            _scoreDepth = depth;
            _targetDepth = depth;
            _isProvisional = false;
            _completedDepths.Add(new CpuCompletedDepth(depth, score, move, nodes, elapsedMilliseconds));
        }
    }
}

public readonly record struct EvaluationBreakdown(
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
    // SacrificeDebt remains additive; v0.2.37.5 intentionally removes the two retired breakdown fields.
    public int SacrificeDebt { get; init; }
}

public sealed record CpuDecision(
    Move Move,
    int Score,
    long Nodes,
    long ElapsedMilliseconds,
    int Depth,
    int RequestedDepth,
    long TranspositionHits,
    long BetaCutoffs,
    bool TimedOut,
    bool NodeLimitReached,
    long NodesPerSecond,
    IReadOnlyList<CpuCandidate> Candidates,
    bool CycleAvoidanceApplied,
    bool RunnerOscillationAvoidanceApplied,
    bool RunnerAdvancePreferenceApplied,
    bool RunnerReturnCandidatePresent,
    bool SelectedRunnerReturnMove,
    int PreferenceScoreConcession,
    int PreferenceScoreWindow,
    int StrictBestScore,
    int SelectedPhysicalHistoryCount,
    int StrictBestPhysicalHistoryCount,
    int SelectedRunnerForwardDelta,
    int StrictBestRunnerForwardDelta,
    CpuSearchTelemetry SearchTelemetry,
    EvaluationBreakdown StaticEvaluationAfterMove);

public static class CpuPlayer
{
    private const int WinScore = 1_000_000;
    private const int Infinity = 1_100_000;
    private const int MateScoreThreshold = WinScore - 2_048;
    private const int LmrHistoryProtectionThreshold = 1_000;
    // Root-only anti-cycle / anti-oscillation preference uses CpuSearchOptions.CycleBreakScoreWindow.
    // The game rules and static evaluation itself remain unchanged.
    // A real-game Runner return is demoted only in root move ordering. On a completed depth,
    // alpha-beta still proves and selects it whenever it is genuinely better; the penalty
    // merely stops an equal minimax tie from defaulting to a visually pointless reversal.
    private const int RootRunnerReturnOrderingPenalty = 50_000;
    private static readonly ulong[] AdjacentMasks = BuildAdjacentMasks();

    public static CpuDecision DecideMove(
        GameEngine position,
        PlayerId cpuPlayer,
        int depth,
        CancellationToken cancellationToken) =>
        DecideMove(
            position,
            cpuPlayer,
            new CpuSearchOptions(MaxDepth: depth, TimeLimitMilliseconds: 0),
            cancellationToken);

    public static Task<CpuDecision> DecideMoveAsync(
        GameEngine position,
        PlayerId cpuPlayer,
        int depth,
        CancellationToken cancellationToken = default) =>
        DecideMoveAsync(
            position,
            cpuPlayer,
            new CpuSearchOptions(MaxDepth: depth, TimeLimitMilliseconds: 0),
            cancellationToken);

    public static Task<CpuDecision> DecideMoveAsync(
        GameEngine position,
        PlayerId cpuPlayer,
        CpuSearchOptions options,
        CancellationToken cancellationToken = default,
        CpuSearchMonitor? searchMonitor = null)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(options);
        return Task.Run(
            () => DecideMove(position, cpuPlayer, options, cancellationToken, searchMonitor),
            cancellationToken);
    }

    public static CpuDecision DecideMove(
        GameEngine position,
        PlayerId cpuPlayer,
        CpuSearchOptions options,
        CancellationToken cancellationToken,
        CpuSearchMonitor? searchMonitor = null)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(options);
        options = options.Normalize();
        using var priorityScope = CpuWorkPriority.Enter(options.UseBelowNormalThreadPriority);

        if (position.Outcome != GameOutcome.Ongoing)
        {
            throw new InvalidOperationException("CPU cannot move after game end.");
        }

        if (position.CurrentPlayer != cpuPlayer)
        {
            throw new InvalidOperationException("CPU was asked to move out of turn.");
        }

        var stopwatch = Stopwatch.StartNew();
        using var context = new SearchContext(cpuPlayer, options, stopwatch, cancellationToken);
        var root = position.Clone();

        // Always have a legal fallback even if a very small time limit expires immediately.
        // The hot move generator writes into stack memory; no List/array is needed here.
        Span<Move> fallbackBuffer = stackalloc Move[GameEngine.MaxLegalMoves];
        int fallbackCount = root.GenerateLegalMoves(cpuPlayer, fallbackBuffer);
        if (fallbackCount == 0)
        {
            throw new InvalidOperationException("No legal moves available.");
        }
        Span<Move> fallbackMoves = fallbackBuffer[..fallbackCount];
        OrderMovesInPlace(root, fallbackMoves, null, context, plyFromRoot: 0, previousRootScores: null);
        Move bestMove = fallbackMoves[0];
        int bestScore = EvaluateMoveOnePly(root, bestMove, cpuPlayer, context.EvaluationProfile);
        searchMonitor?.SetFallback(bestMove, bestScore);
        int completedDepth = 0;
        IReadOnlyList<CpuCandidate> finalCandidates = new[] { new CpuCandidate(bestMove, bestScore, "fallback") };
        IReadOnlyList<CpuCandidate>? previousRootScores = null;
        bool timedOut = false;
        bool nodeLimitReached = false;
        long previousDepthCost = 0;
        long nodesAtPreviousCompletedDepth = 0;
        bool stoppedForMateScout = false;
        bool mateScoutProofApplied = false;
        bool mateScoutAttempted = false;

        for (int depth = 1; depth <= options.MaxDepth; depth++)
        {
            searchMonitor?.BeginDepth(depth);
            try
            {
                RootSearchResult result = SearchRoot(root, depth, context, previousRootScores, searchMonitor);
                bestMove = result.BestMove;
                bestScore = result.BestScore;
                finalCandidates = result.Candidates;
                completedDepth = depth;
                searchMonitor?.CompleteDepth(depth, bestMove, bestScore, context.Nodes, stopwatch.ElapsedMilliseconds);
                previousRootScores = result.Candidates;

                long completedNodes = context.Nodes;
                long currentDepthCost = Math.Max(1, completedNodes - nodesAtPreviousCompletedDepth);
                long priorDepthCost = previousDepthCost;
                previousDepthCost = currentDepthCost;
                nodesAtPreviousCompletedDepth = completedNodes;

                if (Math.Abs(bestScore) >= WinScore - 2048)
                {
                    break;
                }

                if (!mateScoutAttempted &&
                    ShouldStartMateDistanceScout(
                        options,
                        depth,
                        context.Nodes,
                        priorDepthCost,
                        currentDepthCost))
                {
                    mateScoutAttempted = true;

                    // v0.2.37.3: use the completed normal score only to choose the result
                    // direction, then probe a high mate horizon first instead of paying for
                    // D10/D12/... non-mate proofs in order. Once a forced result exists, keep
                    // that proof even if exact-distance refinement later exhausts the budget.
                    MateDistanceScoutResult scout = RunMateDistanceScout(
                        root,
                        depth,
                        Math.Min(99, options.MaxDepth + options.MaxMateDistanceScoutExtraPly),
                        context,
                        result.Candidates,
                        bestMove,
                        bestScore,
                        searchMonitor);

                    if (scout.ProofScore != 0)
                    {
                        bestMove = scout.Move;
                        bestScore = scout.ProofScore;
                        finalCandidates = ApplyMateScoutProofToCandidates(
                            result.Candidates,
                            scout,
                            cpuPlayer);
                        mateScoutProofApplied = true;
                        stoppedForMateScout = true;
                        break;
                    }

                    // If the proof search exhausted the shared node budget, there is no
                    // useful standard work left. If it reached its Scout depth cap cheaply
                    // without proving mate, resume normal iterative deepening so an unused
                    // remainder is not discarded.
                    if (options.MaxNodes > 0 && context.Nodes >= options.MaxNodes)
                    {
                        stoppedForMateScout = true;
                        break;
                    }
                }
            }
            catch (SearchAbortedException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                nodeLimitReached = options.MaxNodes > 0 && context.Nodes >= options.MaxNodes;
                timedOut = !nodeLimitReached && options.TimeLimitMilliseconds > 0 && stopwatch.ElapsedMilliseconds >= options.TimeLimitMilliseconds;
                break;
            }
        }

        if (completedDepth == 0)
        {
            nodeLimitReached = options.MaxNodes > 0 && context.Nodes >= options.MaxNodes;
            timedOut = !nodeLimitReached && options.TimeLimitMilliseconds > 0 && stopwatch.ElapsedMilliseconds >= options.TimeLimitMilliseconds;
        }
        else if (stoppedForMateScout)
        {
            nodeLimitReached = options.MaxNodes > 0 && context.Nodes >= options.MaxNodes;
            timedOut = !nodeLimitReached && options.TimeLimitMilliseconds > 0 && stopwatch.ElapsedMilliseconds >= options.TimeLimitMilliseconds;
        }
        else if (context.SelectiveExtensionAborted)
        {
            // A nominal depth may have completed successfully even though the optional
            // survivor-only re-search then hit the node/time budget. Preserve that depth,
            // but report the stop reason accurately to logs/UI.
            nodeLimitReached = options.MaxNodes > 0 && context.Nodes >= options.MaxNodes;
            timedOut = !nodeLimitReached && options.TimeLimitMilliseconds > 0 && stopwatch.ElapsedMilliseconds >= options.TimeLimitMilliseconds;
        }

        RootPreferenceSelection preference = RootPreferenceSelection.Unchanged(bestMove, bestScore);
        // Keep the opening-randomization experiment independent. Outside that opening,
        // use the requested anti-cycle policy only at the root: if a candidate can be
        // proven within the configured score window of the strict best, prefer the least-seen physical
        // board+turn; if that ties, prefer Runner forward progress.
        if (!mateScoutProofApplied && completedDepth > 0 && options.RandomTopK <= 1 && finalCandidates.Count > 1)
        {
            bool canRescoreBoundsExactly = options.TimeLimitMilliseconds == 0 && options.MaxNodes == 0;
            preference = SelectPreferredRootMove(
                root,
                cpuPlayer,
                completedDepth,
                context,
                finalCandidates,
                bestMove,
                bestScore,
                options.CycleBreakScoreWindow,
                canRescoreBoundsExactly);
            bestMove = preference.Move;
            bestScore = preference.Score;
            finalCandidates = preference.Candidates;
        }

        // Opening diversification is deterministic for a supplied seed and only happens at the root.
        if (!mateScoutProofApplied && options.RandomTopK > 1 && finalCandidates.Count > 1)
        {
            CpuCandidate[] ordered = finalCandidates
                .OrderByDescending(c => c.SearchScore)
                .ThenBy(c => CanonicalMoveSortKey(c.Move, cpuPlayer))
                .ToArray();
            int topScore = ordered[0].SearchScore;
            CpuCandidate[] eligible = ordered
                .Take(options.RandomTopK)
                .Where(c => options.RandomScoreWindow <= 0 || c.SearchScore >= topScore - options.RandomScoreWindow)
                .ToArray();

            if (eligible.Length > 1)
            {
                var random = new Random(options.RandomSeed ?? 0);
                CpuCandidate chosen = eligible[0];
                if (options.RandomSelectionTemperature > 0)
                {
                    // Product skill randomness is deliberately simple: RandomMoveProbability
                    // is the chance of choosing a near-best alternative instead of the strict
                    // best. The particular alternative is weighted toward smaller score loss.
                    if (random.NextDouble() < options.RandomMoveProbability)
                    {
                        CpuCandidate[] alternatives = eligible.Skip(1).ToArray();
                        if (alternatives.Length > 0)
                        {
                            double temperature = options.RandomSelectionTemperature;
                            double[] weights = new double[alternatives.Length];
                            double totalWeight = 0;
                            for (int i = 0; i < alternatives.Length; i++)
                            {
                                int scoreLoss = Math.Max(0, topScore - alternatives[i].SearchScore);
                                double weight = Math.Exp(-scoreLoss / temperature);
                                weights[i] = weight;
                                totalWeight += weight;
                            }

                            double ticket = random.NextDouble() * totalWeight;
                            int selectedIndex = alternatives.Length - 1;
                            for (int i = 0; i < weights.Length; i++)
                            {
                                ticket -= weights[i];
                                if (ticket <= 0)
                                {
                                    selectedIndex = i;
                                    break;
                                }
                            }
                            chosen = alternatives[selectedIndex];
                        }
                    }
                }
                else
                {
                    // Preserve the opening-randomization experiment when temperature=0.
                    chosen = eligible[random.Next(eligible.Length)];
                }

                bestMove = chosen.Move;
                bestScore = chosen.SearchScore;
            }
        }

        // Human-facing skill randomness (temperature > 0) should not disable the common
        // anti-cycle personality. Apply the same root cycle-break policy after the weighted
        // choice. Opening experiments keep temperature=0 and therefore remain unchanged.
        if (!mateScoutProofApplied && completedDepth > 0 && options.RandomSelectionTemperature > 0 && finalCandidates.Count > 1)
        {
            bool canRescoreBoundsExactly = options.TimeLimitMilliseconds == 0 && options.MaxNodes == 0;
            preference = SelectPreferredRootMove(
                root,
                cpuPlayer,
                completedDepth,
                context,
                finalCandidates,
                bestMove,
                bestScore,
                options.CycleBreakScoreWindow,
                canRescoreBoundsExactly);
            bestMove = preference.Move;
            bestScore = preference.Score;
            finalCandidates = preference.Candidates;
        }

        GameEngine.SearchUndo selectedUndo = root.ApplyGeneratedMoveForSearch(bestMove);
        EvaluationBreakdown breakdown = EvaluateDetailed(root, cpuPlayer, context.EvaluationProfile);
        root.UndoSearchMove(selectedUndo);

        stopwatch.Stop();
        long elapsedMs = stopwatch.ElapsedMilliseconds;
        long nps = elapsedMs > 0 ? context.Nodes * 1000L / elapsedMs : context.Nodes;

        return new CpuDecision(
            bestMove,
            bestScore,
            context.Nodes,
            elapsedMs,
            completedDepth,
            options.MaxDepth,
            context.TranspositionHits,
            context.BetaCutoffs,
            timedOut,
            nodeLimitReached,
            nps,
            finalCandidates
                .OrderByDescending(c => c.SearchScore)
                .ThenBy(c => CanonicalMoveSortKey(c.Move, cpuPlayer))
                .ToArray(),
            preference.CycleAvoidanceApplied,
            preference.RunnerOscillationAvoidanceApplied,
            preference.RunnerAdvancePreferenceApplied,
            finalCandidates.Any(c => root.IsRealRunnerReturnMove(cpuPlayer, c.Move)),
            root.IsRealRunnerReturnMove(cpuPlayer, bestMove),
            preference.ScoreConcession,
            options.CycleBreakScoreWindow,
            preference.StrictBestScore,
            preference.SelectedPhysicalHistoryCount,
            preference.StrictBestPhysicalHistoryCount,
            preference.SelectedRunnerForwardDelta,
            preference.StrictBestRunnerForwardDelta,
            new CpuSearchTelemetry(
                context.PvsNullWindowProbes,
                context.PvsResearches,
                context.LmrReducedSearches,
                context.LmrVerificationResearches,
                context.MateDistancePrunes,
                context.OnlySurvivalExtensions,
                context.AdaptiveRootDeepeningPasses,
                context.MaxAdaptiveRootDeepeningPlyReached)
            {
                MateDistanceScoutProbes = context.MateDistanceScoutProbes,
                MateDistanceScoutNodes = context.MateDistanceScoutNodes,
                MateDistanceScoutMaxDepthReached = context.MateDistanceScoutMaxDepthReached,
                MateDistanceScoutMaxCompletedDepth = context.MateDistanceScoutMaxCompletedDepth,
                MateDistanceScoutDirection = context.MateDistanceScoutDirection,
                MateDistanceScoutProofDepth = context.MateDistanceScoutProofDepth,
                MateDistanceScoutProofScore = context.MateDistanceScoutProofScore,
                MateDistanceScoutProofExact = context.MateDistanceScoutProofExact,
                MateDistanceScoutProbeDetails = context.GetMateDistanceScoutProbeDetails()
            },
            breakdown);
    }

    private static RootPreferenceSelection SelectPreferredRootMove(
        GameEngine root,
        PlayerId player,
        int completedDepth,
        SearchContext context,
        IReadOnlyList<CpuCandidate> candidates,
        Move originalBestMove,
        int originalBestScore,
        int scoreWindow,
        bool canRescoreBoundsExactly)
    {
        var updated = candidates.ToArray();
        MovePreferenceMetrics strictMetrics = MeasureRootPreference(root, originalBestMove, player);

        // The expensive "prove near-best alternatives exactly" step is only needed once
        // the strict best move would either revisit a physical board+turn or reverse the
        // most recent real-game Runner move. Root move ordering already resolves exact
        // minimax ties cheaply; this second stage matters mainly for unlimited/exact-score
        // analysis where a small configured concession can be proven safe.
        if (strictMetrics.PhysicalHistoryCount <= 0 && !strictMetrics.RunnerReturnMove)
        {
            return new RootPreferenceSelection(
                originalBestMove,
                originalBestScore,
                updated,
                false,
                false,
                false,
                0,
                originalBestScore,
                strictMetrics.PhysicalHistoryCount,
                strictMetrics.PhysicalHistoryCount,
                strictMetrics.RunnerReturnMove,
                strictMetrics.RunnerReturnMove,
                strictMetrics.RunnerForwardDelta,
                strictMetrics.RunnerForwardDelta);
        }

        int lowerGate = originalBestScore - scoreWindow;
        var eligible = new List<PreferredCandidate>(candidates.Count)
        {
            new(
                originalBestMove,
                originalBestScore,
                strictMetrics.PhysicalHistoryCount,
                strictMetrics.RunnerReturnMove,
                strictMetrics.RunnerForwardDelta)
        };

        for (int i = 0; i < candidates.Count; i++)
        {
            CpuCandidate candidate = candidates[i];
            if (candidate.Move == originalBestMove)
            {
                continue;
            }

            // For an upper-bound candidate, true minimax is <= SearchScore. If even
            // that upper bound is outside the window, it cannot become eligible.
            if (candidate.SearchScore < lowerGate)
            {
                continue;
            }

            MovePreferenceMetrics metrics = MeasureRootPreference(root, candidate.Move, player);

            // A candidate that neither reduces cycle history nor improves Runner
            // progress at the same cycle-history level can never beat the strict best
            // under the secondary preference ordering, so do not spend nodes proving it.
            bool meaningfulOscillationAlternative = !metrics.IsRunnerMove || metrics.RunnerForwardDelta > 0;
            bool canImprovePreference =
                metrics.PhysicalHistoryCount < strictMetrics.PhysicalHistoryCount ||
                (metrics.PhysicalHistoryCount == strictMetrics.PhysicalHistoryCount &&
                 strictMetrics.RunnerReturnMove && !metrics.RunnerReturnMove && meaningfulOscillationAlternative) ||
                (metrics.PhysicalHistoryCount == strictMetrics.PhysicalHistoryCount &&
                 metrics.RunnerReturnMove == strictMetrics.RunnerReturnMove &&
                 metrics.RunnerForwardDelta > strictMetrics.RunnerForwardDelta);
            if (!canImprovePreference)
            {
                continue;
            }

            int score = candidate.SearchScore;
            bool isExact = string.Equals(candidate.Bound, "exact", StringComparison.Ordinal);
            if (!isExact)
            {
                if (!canRescoreBoundsExactly)
                {
                    continue;
                }

                score = SearchRootMoveExactly(root, candidate.Move, completedDepth, context);
                updated[i] = new CpuCandidate(candidate.Move, score, "exact-preference");
            }

            if (score >= lowerGate)
            {
                eligible.Add(new PreferredCandidate(
                    candidate.Move,
                    score,
                    metrics.PhysicalHistoryCount,
                    metrics.RunnerReturnMove,
                    metrics.RunnerForwardDelta));
            }
        }

        PreferredCandidate strict = eligible[0];
        PreferredCandidate selected = eligible
            .OrderBy(c => c.PhysicalHistoryCount)
            .ThenBy(c => c.RunnerReturnMove ? 1 : 0)
            .ThenByDescending(c => c.RunnerForwardDelta)
            .ThenByDescending(c => c.Score)
            .ThenBy(c => CanonicalMoveSortKey(c.Move, player))
            .First();

        bool changed = selected.Move != strict.Move;
        bool cycleApplied = changed && selected.PhysicalHistoryCount < strict.PhysicalHistoryCount;
        bool oscillationApplied = changed &&
                                  selected.PhysicalHistoryCount == strict.PhysicalHistoryCount &&
                                  strict.RunnerReturnMove && !selected.RunnerReturnMove;
        bool runnerApplied = changed &&
                             selected.PhysicalHistoryCount == strict.PhysicalHistoryCount &&
                             selected.RunnerReturnMove == strict.RunnerReturnMove &&
                             selected.RunnerForwardDelta > strict.RunnerForwardDelta;

        return new RootPreferenceSelection(
            selected.Move,
            selected.Score,
            updated,
            cycleApplied,
            oscillationApplied,
            runnerApplied,
            Math.Max(0, strict.Score - selected.Score),
            strict.Score,
            selected.PhysicalHistoryCount,
            strict.PhysicalHistoryCount,
            selected.RunnerReturnMove,
            strict.RunnerReturnMove,
            selected.RunnerForwardDelta,
            strict.RunnerForwardDelta);
    }

    private static int SearchRootMoveExactly(
        GameEngine root,
        Move move,
        int completedDepth,
        SearchContext context)
    {
        GameEngine.SearchUndo undo = root.ApplyGeneratedMoveForSearch(move);
        try
        {
            return Search(
                root,
                Math.Max(0, completedDepth - 1),
                context,
                -Infinity,
                Infinity,
                plyFromRoot: 1);
        }
        finally
        {
            root.UndoSearchMove(undo);
        }
    }

    private static MovePreferenceMetrics MeasureRootPreference(GameEngine root, Move move, PlayerId player)
    {
        Position runnerBefore = root.FindRunner(player);
        bool isRunnerMove = move.From == runnerBefore;
        bool runnerReturnMove = root.IsRealRunnerReturnMove(player, move);
        int runnerForwardDelta = 0;
        if (isRunnerMove)
        {
            runnerForwardDelta = player == PlayerId.Player1
                ? move.From.Row - move.To.Row
                : move.To.Row - move.From.Row;
        }

        GameEngine.SearchUndo undo = root.ApplyGeneratedMoveForSearch(move);
        try
        {
            // Search moves do not mutate the real-game physical-history table. Thus
            // zero means genuinely unseen in the actual game; 1 means it occurred once
            // before, etc. Terminal goal moves are effectively always acceptable due to
            // their decisive raw search score, so this secondary value cannot override them.
            int physicalHistoryCount = root.CurrentPhysicalPositionHistoricalCount();
            return new MovePreferenceMetrics(physicalHistoryCount, runnerReturnMove, runnerForwardDelta, isRunnerMove);
        }
        finally
        {
            root.UndoSearchMove(undo);
        }
    }

    public static EvaluationBreakdown EvaluateDetailed(GameEngine state, PlayerId perspective) =>
        EvaluateDetailed(state, perspective, CpuEvaluationProfileProvider.Current);

    public static EvaluationBreakdown EvaluateDetailed(
        GameEngine state,
        PlayerId perspective,
        CpuEvaluationProfile evaluationProfile)
    {
        if (state.Outcome != GameOutcome.Ongoing)
        {
            int terminal = state.Outcome switch
            {
                GameOutcome.Draw => 0,
                GameOutcome.Player1Win when perspective == PlayerId.Player1 => WinScore - state.PlyCount,
                GameOutcome.Player2Win when perspective == PlayerId.Player2 => WinScore - state.PlyCount,
                _ => -WinScore + state.PlyCount
            };

            return new EvaluationBreakdown(terminal, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1000);
        }

        PlayerId opponent = perspective.Opponent();
        Position ownRunner = state.FindRunner(perspective);
        Position enemyRunner = state.FindRunner(opponent);

        int ownProgress = Progress(perspective, ownRunner);
        int enemyProgress = Progress(opponent, enemyRunner);

        // v0.2.26.0 feature model: every raw feature is normalized to roughly
        // [-100,+100] before the tunable opening/endgame scale is applied. This makes a
        // flat 1000‰/1000‰ profile genuinely neutral instead of hiding large hand-made
        // priorities inside the raw feature coefficients. Runner progress is deliberately
        // linear here; any preference for early/late progress must be learned through the
        // phase-specific profile weights.
        int runnerProgressRaw = NormalizeFeatureDifference(ownProgress - enemyProgress, 7);

        GameEngine.RunnerMoveCounts ownRunnerMoves = state.GetRunnerMoveCountsForEvaluation(perspective);
        GameEngine.RunnerMoveCounts enemyRunnerMoves = state.GetRunnerMoveCountsForEvaluation(opponent);
        int ownMobility = ownRunnerMoves.Normal + ownRunnerMoves.Sacrifice;
        int enemyMobility = enemyRunnerMoves.Normal + enemyRunnerMoves.Sacrifice;
        int runnerMobilityRaw = NormalizeFeatureDifference(ownMobility - enemyMobility, 8);

        // Bitboard-backed aggregate features avoid the previous 64-square scan at every leaf.
        int ownBlockers = state.CountBlockers(perspective);
        int enemyBlockers = state.CountBlockers(opponent);
        // Goal defense is evaluated against the opponent's concrete near-term goal destinations
        // below, rather than by blindly counting every piece parked somewhere on the home row.
        int ownBlockerAdvance = BlockerAdvanceSum(state.GetBlockerBits(perspective), perspective);
        int enemyBlockerAdvance = BlockerAdvanceSum(state.GetBlockerBits(opponent), opponent);

        // Continuous game phase. A lone Runner rush must not by itself switch the whole
        // evaluator to endgame weights, so the Runner component is driven mainly by the
        // less-advanced Runner; blocker consumption contributes the remaining 30%.
        int sharedRunnerProgress = Math.Min(ownProgress, enemyProgress);
        int runnerPhase = sharedRunnerProgress * 1000 / 7;
        int remainingBlockers = ownBlockers + enemyBlockers;
        int depletionPhase = Math.Clamp((12 - remainingBlockers) * 1000 / 12, 0, 1000);
        int phasePermille = Math.Clamp((runnerPhase * 7 + depletionPhase * 3 + 5) / 10, 0, 1000);
        CpuEvaluationFeatureScales scales = evaluationProfile.Blend(phasePermille);

        int Scale(int raw, int permille) => (raw * permille + Math.Sign(raw) * 500) / 1000;

        int runnerProgress = Scale(runnerProgressRaw, scales.RunnerProgress);
        int runnerMobility = Scale(runnerMobilityRaw, scales.RunnerMobility);

        // v0.2.37.6 semantics / v0.2.37.7 parallel implementation: BlockerMaterial is no longer a literal piece-count difference.
        // A remaining blocker is discounted when an urgent enemy Runner route ties it to
        // defense so tightly that it cannot even reach an offensive sacrifice-staging square
        // before it must begin the defensive interception.  The same BlockerMaterial weight
        // is retained; only the raw meaning changes from physical count to effective count.
        Span<ulong> ownShortestCorridor = stackalloc ulong[9];
        Span<ulong> enemyShortestCorridor = stackalloc ulong[9];
        RunnerRouteAnalysis ownRoute = AnalyzeRunnerRoute(state, perspective, ownRunner, ownShortestCorridor);
        RunnerRouteAnalysis enemyRoute = AnalyzeRunnerRoute(state, opponent, enemyRunner, enemyShortestCorridor);
        int ownEffectiveBlockers100 = EffectiveBlockerMaterial100(
            state, perspective, ownBlockers, ownRoute, ownShortestCorridor, enemyRoute, enemyShortestCorridor);
        int enemyEffectiveBlockers100 = EffectiveBlockerMaterial100(
            state, opponent, enemyBlockers, enemyRoute, enemyShortestCorridor, ownRoute, ownShortestCorridor);
        int blockerMaterialRaw = NormalizeFeatureDifference(
            ownEffectiveBlockers100 - enemyEffectiveBlockers100, 600);
        int blockerMaterial = Scale(blockerMaterialRaw, scales.BlockerMaterial);

        int ownSupport = CountFriendlyBlockersAdjacent(state, ownRunner, perspective);
        int enemySupport = CountFriendlyBlockersAdjacent(state, enemyRunner, opponent);
        int friendlyRunnerSupportRaw = NormalizeFeatureDifference(ownSupport - enemySupport, 6);
        int friendlyRunnerSupport = Scale(friendlyRunnerSupportRaw, scales.FriendlyRunnerSupport);

        // Front pressure is a local containment signal, not merely a one-square occupancy test.
        // A direct marker is strongest, but a nearby blocker that can step into the Runner's
        // forward square on the next turn still exerts "shadow" pressure. Conversely, an
        // already-placed friendly blocker in a forward sacrifice square gives the Runner a
        // protected advance that the marker cannot occupy, so the pressure is discounted.
        int ownFrontPressure = RunnerFrontPressureSignal(state, perspective, ownRunner);
        int enemyFrontPressure = RunnerFrontPressureSignal(state, opponent, enemyRunner);
        int frontPressureRaw = enemyFrontPressure - ownFrontPressure;
        int frontPressure = Scale(frontPressureRaw, scales.FrontPressure);

        // A runner can have at most three distinct one-move goal destinations on the goal
        // row (one normal square and/or adjacent sacrifice squares). Search still owns
        // actual terminal proof; this feature merely reports the static threat on the same
        // normalized scale as the other features.
        int immediateGoalThreatsRaw = NormalizeFeatureDifference(
            ownRunnerMoves.ImmediateGoal - enemyRunnerMoves.ImmediateGoal, 3);
        int immediateGoalThreats = Scale(immediateGoalThreatsRaw, scales.ImmediateGoalThreats);

        int blockerAdvancementRaw = NormalizeFeatureDifference(ownBlockerAdvance - enemyBlockerAdvance, 42);
        int blockerAdvancement = Scale(blockerAdvancementRaw, scales.BlockerAdvancement);

        // The route analyses above also supply BridgeheadConnection and GoalPath.
        int ownConnection = ownRoute.BridgeheadConnectionValue;
        int enemyConnection = enemyRoute.BridgeheadConnectionValue;
        int bridgeheadConnectionRaw = NormalizeFeatureDifference(ownConnection - enemyConnection, 70);
        int bridgeheadConnection = Scale(bridgeheadConnectionRaw, scales.BridgeheadConnection);

        // GoalDefense is target-aware. A defender scores only to the extent that its blockers
        // can reach the attacker's currently-shortest goal destinations before the Runner.
        // A far-away home-row blocker therefore no longer counts as full defense merely for
        // being parked on row 1/8, while a blocker that can actually cover the threatened
        // goal square remains valuable.
        int ownGoalDefenseStrength = GoalDefenseStrengthSignal(state, perspective, opponent, enemyRoute);
        int enemyGoalDefenseStrength = GoalDefenseStrengthSignal(state, opponent, perspective, ownRoute);
        int goalDefenseRaw = ownGoalDefenseStrength - enemyGoalDefenseStrength;
        int goalDefense = Scale(goalDefenseRaw, scales.GoalDefense);

        // RunnerGoalPath estimates the shortest static route to the goal if the opponent
        // makes no further moves. Orthogonal entry into an empty square is a normal move;
        // entry into a friendly blocker is a sacrifice and may come from any of 8 adjacent
        // squares. Enemy pieces remain walls. The score deliberately saturates beyond 8
        // Runner moves so this feature measures concrete near/mid-term route quality rather
        // than duplicating RunnerProgress across the whole board.
        int ownGoalPath = ownRoute.GoalPathValue;
        int enemyGoalPath = enemyRoute.GoalPathValue;
        int runnerGoalPathRaw = NormalizeFeatureDifference(ownGoalPath - enemyGoalPath, 8);
        int runnerGoalPath = Scale(runnerGoalPathRaw, scales.RunnerGoalPath);

        // v0.2.36.0 interaction features. Each side is first converted to independent
        // [0,100] signals and only then combined. This avoids the sign inversion that
        // would result from multiplying already-differenced evaluation terms. In v0.2.36.2
        // the underlying defense/connection signals are more concrete. v0.2.37.5 retires
        // GoalBridgeheads and RunnerCentrality; SacrificeDebt remains the additive resource feature.
        int preparedGoalThreat = 0;
        int unansweredGoalThreat = 0;
        int connectedGoalThreat = 0;
        int viableRunnerProgress = 0;
        int sacrificeDebt = 0;

        bool anyInteractionEnabled = scales.PreparedGoalThreat != 0 ||
                                     scales.UnansweredGoalThreat != 0 ||
                                     scales.ConnectedGoalThreat != 0 ||
                                     scales.ViableRunnerProgress != 0 ||
                                     scales.SacrificeDebt != 0;
        if (anyInteractionEnabled)
        {
            int ownGoalPathUrgency = GoalPathUrgencySignal(ownGoalPath);
            int enemyGoalPathUrgency = GoalPathUrgencySignal(enemyGoalPath);

            if (scales.PreparedGoalThreat != 0)
            {
                int ownPreparedChain = SacrificeChainReadinessSignal(state, perspective, ownRunner);
                int enemyPreparedChain = SacrificeChainReadinessSignal(state, opponent, enemyRunner);
                int raw = InteractionProductSignal(ownGoalPathUrgency, ownPreparedChain) -
                          InteractionProductSignal(enemyGoalPathUrgency, enemyPreparedChain);
                preparedGoalThreat = Scale(raw, scales.PreparedGoalThreat);
            }

            if (scales.UnansweredGoalThreat != 0)
            {
                int raw = InteractionProductSignal(ownGoalPathUrgency, 100 - enemyGoalDefenseStrength) -
                          InteractionProductSignal(enemyGoalPathUrgency, 100 - ownGoalDefenseStrength);
                unansweredGoalThreat = Scale(raw, scales.UnansweredGoalThreat);
            }

            if (scales.ConnectedGoalThreat != 0)
            {
                int ownConnectionSignal = BridgeheadConnectionSignal(ownConnection);
                int enemyConnectionSignal = BridgeheadConnectionSignal(enemyConnection);
                int raw = InteractionProductSignal(ownGoalPathUrgency, ownConnectionSignal) -
                          InteractionProductSignal(enemyGoalPathUrgency, enemyConnectionSignal);
                connectedGoalThreat = Scale(raw, scales.ConnectedGoalThreat);
            }

            if (scales.ViableRunnerProgress != 0)
            {
                int ownProgressSignal = RunnerProgressSignal(ownProgress);
                int enemyProgressSignal = RunnerProgressSignal(enemyProgress);
                int raw = InteractionProductSignal(ownProgressSignal, ownGoalPathUrgency) -
                          InteractionProductSignal(enemyProgressSignal, enemyGoalPathUrgency);
                viableRunnerProgress = Scale(raw, scales.ViableRunnerProgress);
            }

            if (scales.SacrificeDebt != 0)
            {
                // v0.2.37.4: irreversible blocker spending is not equivalent to a simple
                // current material difference. A spent blocker becomes strategic debt when
                // the remaining reserve is thin *and* the Runner has not converted that
                // investment into a viable, uncontained route toward goal.
                //
                // This intentionally reuses the already-computed progress / goal-path /
                // front-pressure signals. In position 76, P1 has spent two blockers and its
                // Runner is directly contained (front pressure 100), so those sacrifices
                // receive almost no attacking credit; P2 has spent only one and retains
                // attacking headroom. Search remains responsible for actual mate proof.
                int ownStartingBlockers = state.StartConfiguration.CountBlockers(perspective);
                int enemyStartingBlockers = state.StartConfiguration.CountBlockers(opponent);
                int ownDebt = SacrificeDebtSignal(
                    ownStartingBlockers, ownBlockers, ownProgress, ownGoalPath, ownFrontPressure);
                int enemyDebt = SacrificeDebtSignal(
                    enemyStartingBlockers, enemyBlockers, enemyProgress, enemyGoalPath, enemyFrontPressure);
                int raw = enemyDebt - ownDebt; // positive is good for the perspective side
                sacrificeDebt = Scale(raw, scales.SacrificeDebt);
            }
        }

        int total = runnerProgress + runnerMobility + blockerMaterial + friendlyRunnerSupport +
                    frontPressure + goalDefense + immediateGoalThreats + blockerAdvancement + bridgeheadConnection + runnerGoalPath +
                    preparedGoalThreat + unansweredGoalThreat + connectedGoalThreat + viableRunnerProgress +
                    sacrificeDebt;

        return new EvaluationBreakdown(
            total,
            runnerProgress,
            runnerMobility,
            blockerMaterial,
            friendlyRunnerSupport,
            frontPressure,
            goalDefense,
            immediateGoalThreats,
            blockerAdvancement,
            bridgeheadConnection,
            runnerGoalPath,
            preparedGoalThreat,
            unansweredGoalThreat,
            connectedGoalThreat,
            viableRunnerProgress,
            phasePermille)
            { SacrificeDebt = sacrificeDebt };
    }

    private static bool ShouldStartMateDistanceScout(
        CpuSearchOptions options,
        int completedDepth,
        long nodesUsed,
        long previousDepthCost,
        long currentDepthCost)
    {
        if (!options.UseMateDistanceScout ||
            options.MaxNodes < 100_000 ||
            options.TimeLimitMilliseconds != 0 ||
            completedDepth < options.MateDistanceScoutMinCompletedDepth ||
            completedDepth >= options.MaxDepth ||
            options.MaxMateDistanceScoutExtraPly <= 0 ||
            previousDepthCost <= 0 ||
            currentDepthCost <= 0)
        {
            return false;
        }

        long remaining = options.MaxNodes - nodesUsed;
        if (remaining <= 0)
        {
            return false;
        }

        // Predict the incremental cost of the next *completed* normal depth from the
        // last two completed depth costs. The trigger is deliberately conservative:
        // Mate Scout only takes over when the next all-move iteration is very unlikely
        // to fit in the remaining node budget. This preserves normal iterative deepening
        // whenever another full depth still looks affordable.
        double observedGrowth = (double)currentDepthCost / previousDepthCost;
        double boundedGrowth = Math.Clamp(observedGrowth, 1.25, 8.0);
        double predictedNextDepthCost = currentDepthCost * boundedGrowth;
        return predictedNextDepthCost > remaining * 1.20;
    }

    private static MateDistanceScoutResult RunMateDistanceScout(
        GameEngine root,
        int completedNormalDepth,
        int maxDepth,
        SearchContext parentContext,
        IReadOnlyList<CpuCandidate> rootOrdering,
        Move normalBestMove,
        int normalBestScore,
        CpuSearchMonitor? searchMonitor)
    {
        // Terminal results are created immediately after a move. From the root side-to-move
        // perspective, wins therefore occur only on odd plies and losses only on even plies.
        // v0.2.37.3 keeps the one-direction/parity proof safety from v0.2.37.1, but changes
        // the *order*: first jump to an upper-middle legal horizon (D16 for position 76 after
        // normal D9 with Scout cap D22). This avoids spending most of the remainder proving
        // that D10 and D12 are non-mates before ever reaching the likely mate horizon.
        bool proveWin = normalBestScore >= 0;
        int direction = proveWin ? 1 : -1;
        int firstDepth = FirstMateScoutDepthAfter(completedNormalDepth, proveWin);
        int maxLegalDepth = LastMateScoutDepthAtOrBefore(maxDepth, proveWin);
        if (firstDepth > maxLegalDepth)
        {
            return default;
        }

        int legalDepthCount = ((maxLegalDepth - firstDepth) / 2) + 1;
        int firstTargetIndex = legalDepthCount / 2; // upper-middle when the count is even
        int firstTargetDepth = firstDepth + firstTargetIndex * 2;

        long nodesBefore = parentContext.Nodes;
        long probes = 0;
        int maxDepthStarted = 0;
        int maxDepthCompleted = 0;
        Move lastLossEscapeMove = normalBestMove;
        MateDistanceScoutResult result = default;
        var probeDetails = new List<CpuMateScoutProbeTelemetry>();

        int minimumMateDepth = proveWin ? 1 : 2;
        int DepthToIndex(int depth) => (depth - minimumMateDepth) / 2;
        int IndexToDepth(int index) => minimumMateDepth + index * 2;

        // All completed probe results used for exact-distance bracketing. Refinement probes
        // get fresh proof-only TTs; a deeper Scout TT must not be reused at a shallower
        // horizon because non-terminal depth-0 means "unknown", not a static evaluation.
        var completedProbeCache = new Dictionary<int, MateScoutProbeResult>();

        MateScoutProbeResult RunCoarseProbe(int depth, SearchContext context)
        {
            maxDepthStarted = Math.Max(maxDepthStarted, depth);
            long probeNodesBefore = parentContext.Nodes;
            searchMonitor?.BeginMateScoutProbe(depth, proveWin, refining: false);
            probes++;

            try
            {
                Move preferred = proveWin ? normalBestMove : lastLossEscapeMove;
                MateScoutProbeResult probe = ProbeMateWithinDepth(
                    root,
                    depth,
                    proveWin,
                    context,
                    rootOrdering,
                    preferred);

                long probeNodes = Math.Max(0, parentContext.Nodes - probeNodesBefore);
                maxDepthCompleted = Math.Max(maxDepthCompleted, depth);
                probeDetails.Add(new CpuMateScoutProbeTelemetry(
                    depth,
                    proveWin,
                    Refining: false,
                    Completed: true,
                    Proven: probe.Proven,
                    Nodes: probeNodes));
                completedProbeCache[DepthToIndex(depth)] = probe;

                if (!proveWin && !probe.Proven && probe.WitnessMove != default)
                {
                    lastLossEscapeMove = probe.WitnessMove;
                }

                return probe;
            }
            catch (SearchAbortedException)
            {
                probeDetails.Add(new CpuMateScoutProbeTelemetry(
                    depth,
                    proveWin,
                    Refining: false,
                    Completed: false,
                    Proven: false,
                    Nodes: Math.Max(0, parentContext.Nodes - probeNodesBefore)));
                throw;
            }
        }

        MateScoutProbeResult RunRefinementProbe(int depth, Move preferred)
        {
            maxDepthStarted = Math.Max(maxDepthStarted, depth);
            long probeNodesBefore = parentContext.Nodes;
            searchMonitor?.BeginMateScoutProbe(depth, proveWin, refining: true);
            probes++;

            using SearchContext refineContext = parentContext.CreateMateScoutChild();
            try
            {
                MateScoutProbeResult probe = ProbeMateWithinDepth(
                    root,
                    depth,
                    proveWin,
                    refineContext,
                    rootOrdering,
                    preferred);

                long probeNodes = Math.Max(0, parentContext.Nodes - probeNodesBefore);
                maxDepthCompleted = Math.Max(maxDepthCompleted, depth);
                probeDetails.Add(new CpuMateScoutProbeTelemetry(
                    depth,
                    proveWin,
                    Refining: true,
                    Completed: true,
                    Proven: probe.Proven,
                    Nodes: probeNodes));
                completedProbeCache[DepthToIndex(depth)] = probe;

                if (!proveWin && !probe.Proven && probe.WitnessMove != default)
                {
                    lastLossEscapeMove = probe.WitnessMove;
                }

                return probe;
            }
            catch (SearchAbortedException)
            {
                probeDetails.Add(new CpuMateScoutProbeTelemetry(
                    depth,
                    proveWin,
                    Refining: true,
                    Completed: false,
                    Proven: false,
                    Nodes: Math.Max(0, parentContext.Nodes - probeNodesBefore)));
                throw;
            }
            finally
            {
                parentContext.MergeCountersFrom(refineContext);
            }
        }

        // A coarse proof is already valuable even before its exact distance is known. Keep
        // a conservative <=M bound so that a later node-limit during refinement does not
        // throw away a genuine forced-win/loss proof and fall back to a small static score.
        MateDistanceScoutResult MakeBoundResult(int depth, MateScoutProbeResult proof)
        {
            Move proofMove = proveWin
                ? proof.WitnessMove
                : lastLossEscapeMove;
            if (proofMove == default)
            {
                proofMove = normalBestMove;
            }

            return new MateDistanceScoutResult(
                proofMove,
                proveWin ? WinScore - depth : -WinScore + depth,
                depth,
                Exact: false);
        }

        using SearchContext coarseContext = parentContext.CreateMateScoutChild();
        try
        {
            int lowIndex;
            int highIndex;

            // Phase 1: existence/high-first. Position 76 becomes Loss D16 first. If that
            // horizon is still unproven, jump directly to the highest legal Scout horizon
            // (D22 there) instead of paying for D18/D20 in sequence.
            MateScoutProbeResult firstProbe = RunCoarseProbe(firstTargetDepth, coarseContext);
            int firstIndex = DepthToIndex(firstTargetDepth);

            if (firstProbe.Proven)
            {
                highIndex = firstIndex;
                lowIndex = -1; // no proof-safe lower bound from normal LMR search
                result = MakeBoundResult(firstTargetDepth, firstProbe);
            }
            else
            {
                lowIndex = firstIndex;
                if (firstTargetDepth >= maxLegalDepth)
                {
                    return default;
                }

                MateScoutProbeResult capProbe = RunCoarseProbe(maxLegalDepth, coarseContext);
                int capIndex = DepthToIndex(maxLegalDepth);
                if (!capProbe.Proven)
                {
                    return default;
                }

                highIndex = capIndex;
                result = MakeBoundResult(maxLegalDepth, capProbe);
            }

            // Phase 2: exact distance. We now know a forced result exists. Binary-search only
            // the legal mate parity. If this phase hits the node limit, 'result' retains the
            // best proven <=M upper horizon instead of discarding the proof.
            while (highIndex - lowIndex > 1)
            {
                int midIndex = (lowIndex + highIndex) / 2;
                int refineDepth = IndexToDepth(midIndex);

                if (completedProbeCache.TryGetValue(midIndex, out MateScoutProbeResult cached))
                {
                    if (cached.Proven)
                    {
                        highIndex = midIndex;
                        result = MakeBoundResult(refineDepth, cached);
                    }
                    else
                    {
                        lowIndex = midIndex;
                        if (!proveWin && cached.WitnessMove != default)
                        {
                            lastLossEscapeMove = cached.WitnessMove;
                        }
                    }
                    continue;
                }

                Move preferred;
                if (proveWin && completedProbeCache.TryGetValue(highIndex, out MateScoutProbeResult highProof) &&
                    highProof.WitnessMove != default)
                {
                    preferred = highProof.WitnessMove;
                }
                else
                {
                    preferred = lastLossEscapeMove;
                }

                MateScoutProbeResult refineProbe = RunRefinementProbe(refineDepth, preferred);
                if (refineProbe.Proven)
                {
                    highIndex = midIndex;
                    result = MakeBoundResult(refineDepth, refineProbe);
                }
                else
                {
                    lowIndex = midIndex;
                }
            }

            int exactDepth = IndexToDepth(highIndex);
            MateScoutProbeResult exactProof = completedProbeCache[highIndex];
            Move exactMateMove;
            if (proveWin)
            {
                exactMateMove = exactProof.WitnessMove;
            }
            else if (lowIndex >= 0 &&
                     completedProbeCache.TryGetValue(lowIndex, out MateScoutProbeResult escapeProbe) &&
                     escapeProbe.WitnessMove != default)
            {
                // Immediately preceding legal loss horizon is unproven. Its witness is the
                // root move that survives longest and therefore realizes the exact mate distance.
                exactMateMove = escapeProbe.WitnessMove;
            }
            else
            {
                // M2 has no preceding legal loss horizon. When refinement proved a longer
                // mate without a completed preceding horizon this branch is not reachable.
                exactMateMove = normalBestMove;
            }

            result = new MateDistanceScoutResult(
                exactMateMove,
                proveWin ? WinScore - exactDepth : -WinScore + exactDepth,
                exactDepth,
                Exact: true);
        }
        catch (SearchAbortedException)
        {
            // Opportunistic proof search. Preserve any already completed high-first proof;
            // only an entirely unproved/incomplete Scout falls back to normal search.
        }
        finally
        {
            searchMonitor?.EndMateScout();
            parentContext.MergeCountersFrom(coarseContext);
            long scoutNodes = Math.Max(0, parentContext.Nodes - nodesBefore);
            parentContext.RecordMateDistanceScout(
                probes,
                scoutNodes,
                maxDepthStarted,
                maxDepthCompleted,
                direction,
                result.ProofDepth,
                result.ProofScore,
                result.Exact,
                probeDetails);
        }

        return result;
    }

    private static int LastMateScoutDepthAtOrBefore(int maxDepth, bool proveWin)
    {
        int depth = Math.Max(1, maxDepth);
        bool needsOdd = proveWin;
        if (((depth & 1) != 0) != needsOdd)
        {
            depth--;
        }
        return depth;
    }

    private static int FirstMateScoutDepthAfter(int completedNormalDepth, bool proveWin)
    {
        int depth = Math.Max(1, completedNormalDepth + 1);
        bool needsOdd = proveWin;
        if (((depth & 1) != 0) != needsOdd)
        {
            depth++;
        }
        return depth;
    }

    private static MateScoutProbeResult ProbeMateWithinDepth(
        GameEngine root,
        int depth,
        bool proveWin,
        SearchContext context,
        IReadOnlyList<CpuCandidate> rootOrdering,
        Move preferredMove)
    {
        context.CheckAbort(force: true);
        Span<Move> moveBuffer = stackalloc Move[GameEngine.MaxLegalMoves];
        int moveCount = root.GenerateLegalMoves(root.CurrentPlayer, moveBuffer);
        if (moveCount == 0)
        {
            return default;
        }

        Span<Move> moves = moveBuffer[..moveCount];
        OrderMovesInPlace(root, moves, preferredMove, context, 0, rootOrdering);

        int threshold = proveWin
            ? WinScore - depth
            : -WinScore + depth;
        int alpha = proveWin ? PreviousScore(threshold) : threshold;
        int beta = proveWin ? threshold : NextScore(threshold);
        Move firstMove = moves[0];

        for (int i = 0; i < moves.Length; i++)
        {
            Move move = moves[i];
            GameEngine.SearchUndo undo = root.ApplyGeneratedMoveForSearch(move);
            int score;
            try
            {
                score = Search(
                    root,
                    Math.Max(0, depth - 1),
                    context,
                    alpha,
                    beta,
                    plyFromRoot: 1,
                    extensionBudget: 0);
            }
            finally
            {
                root.UndoSearchMove(undo);
            }

            if (proveWin)
            {
                if (score >= threshold)
                {
                    return new MateScoutProbeResult(true, move);
                }
            }
            else
            {
                if (score > threshold)
                {
                    // This move survives beyond the requested horizon, so a forced loss
                    // within 'depth' is not proved. On the next loss probe, search this
                    // escape first; if depth+1 finally proves loss, the last escape is the
                    // root move that maximizes mate distance.
                    return new MateScoutProbeResult(false, move);
                }
            }
        }

        return proveWin
            ? new MateScoutProbeResult(false, firstMove)
            : new MateScoutProbeResult(true, firstMove);
    }

    private static IReadOnlyList<CpuCandidate> ApplyMateScoutProofToCandidates(
        IReadOnlyList<CpuCandidate> candidates,
        MateDistanceScoutResult scout,
        PlayerId perspective)
    {
        CpuCandidate[] updated = candidates.ToArray();
        if (scout.ProofScore > 0)
        {
            for (int i = 0; i < updated.Length; i++)
            {
                if (updated[i].Move == scout.Move)
                {
                    // A high-first existence proof may know only "mate within D".
                    // Keep that as a lower bound until exact-distance refinement finishes.
                    updated[i] = new CpuCandidate(
                        scout.Move,
                        scout.ProofScore,
                        scout.Exact ? "exact" : "lower");
                    break;
                }
            }
        }
        else
        {
            // Forced loss within D proves every root move <= -M(D). Exact refinement also
            // identifies the move that maximizes survival; before then keep every candidate
            // as an upper bound and preserve the normal best/witness move as the selection.
            for (int i = 0; i < updated.Length; i++)
            {
                bool selected = updated[i].Move == scout.Move;
                updated[i] = new CpuCandidate(
                    updated[i].Move,
                    scout.ProofScore,
                    scout.Exact && selected ? "exact" : "upper");
            }
        }

        return updated
            .OrderByDescending(c => c.SearchScore)
            .ThenBy(c => CanonicalMoveSortKey(c.Move, perspective))
            .ToArray();
    }

    private static RootSearchResult SearchRoot(
        GameEngine state,
        int depth,
        SearchContext context,
        IReadOnlyList<CpuCandidate>? previousRootScores,
        CpuSearchMonitor? searchMonitor)
    {
        context.CheckAbort(force: true);
        Span<Move> moveBuffer = stackalloc Move[GameEngine.MaxLegalMoves];
        int moveCount = state.GenerateLegalMoves(state.CurrentPlayer, moveBuffer);
        if (moveCount == 0)
        {
            throw new InvalidOperationException("Root search has no legal moves.");
        }

        Move? ttMove = null;
        ulong rootPositionKey = state.GetSearchHash();
        ulong rootTtKey = context.GetTranspositionKey(
            rootPositionKey,
            context.Options.MaxOnlySurvivalExtensionsPerLine);
        if (context.Options.UseTranspositionTable &&
            context.Transposition.TryGetValue(rootTtKey, out TranspositionEntry rootEntry))
        {
            ttMove = rootEntry.BestMove;
        }

        Span<Move> rootMoves = moveBuffer[..moveCount];
        OrderMovesInPlace(state, rootMoves, ttMove, context, 0, previousRootScores);

        // v0.2.36.8 adaptive only-survival deepening:
        // (1) recursive/internal only-survival hints retain the conservative per-line
        //     extension budget (default 2 ply), while
        // (2) the ROOT may deepen the sole proven survivor one ply at a time up to a
        //     separate adaptive cap (default +6 ply). Dead root alternatives are not
        //     reopened unless the survivor's deeper exact score falls below one of their
        //     stored upper bounds and mate-distance ordering must be re-verified.
        Move? rootOnlySurvivalMove = null;
        int rootExtensionPly = 0;
        if (context.Options.UseOnlySurvivalExtension &&
            context.Options.MaxOnlySurvivalExtensionsPerLine > 0 &&
            TryFindOnlySurvivalRootMove(previousRootScores, out Move survivor))
        {
            rootOnlySurvivalMove = survivor;
            rootExtensionPly = 1;
            PromoteMoveInPlace(rootMoves, survivor);
        }

        bool exactAllRootScores = context.Options.CollectExactRootScores || context.Options.RandomTopK > 1;

        // Do one normal root pass. If a previous iteration already identified the sole
        // survivor, that move receives its first extension here while every alternative
        // is still checked at the nominal depth.
        RootSearchResult result = context.Options.MaxParallelism > 1 && depth >= 3 && rootMoves.Length > 1
            ? SearchRootParallel(
                state,
                depth,
                context,
                rootMoves.ToArray(),
                searchMonitor,
                rootOnlySurvivalMove,
                rootExtensionPly,
                exactAllRootScores)
            : SearchRootSerial(
                state,
                depth,
                context,
                rootMoves,
                searchMonitor,
                rootOnlySurvivalMove,
                rootExtensionPly,
                exactAllRootScores);

        // Same-iteration adaptive only-survival deepening. Once every other root move is
        // already proved to be in the mate-loss band, do NOT search those dead branches
        // again. Their upper bounds remain valid proofs at all greater depths. Search only
        // the survivor one ply deeper at a time, up to MaxAdaptiveRootDeepeningPly (default
        // +6). Stop immediately on mate, node/time budget exhaustion, or when the survivor
        // can no longer be proved better than the stored losing upper bounds. In the last
        // case, reopen the root once to compare mate distances safely.
        while (context.Options.UseOnlySurvivalExtension &&
               rootExtensionPly < context.Options.MaxAdaptiveRootDeepeningPly &&
               Math.Abs(result.BestScore) < MateScoreThreshold &&
               TryFindOnlySurvivalRootMove(result.Candidates, out Move currentSurvivor))
        {
            rootOnlySurvivalMove = currentSurvivor;
            rootExtensionPly++;
            PromoteMoveInPlace(rootMoves, currentSurvivor);
            RootSearchResult completedBeforeExtension = result;

            try
            {
                int extendedScore = SearchOnlySurvivalRootMoveExactly(
                    state,
                    currentSurvivor,
                    depth,
                    rootExtensionPly,
                    context);
                context.OnlySurvivalExtensions++;
                context.RecordAdaptiveRootDeepening(rootExtensionPly);

                int maxOtherUpperBound = -Infinity;
                CpuCandidate[] updated = result.Candidates.ToArray();
                for (int i = 0; i < updated.Length; i++)
                {
                    CpuCandidate candidate = updated[i];
                    if (candidate.Move == currentSurvivor)
                    {
                        updated[i] = new CpuCandidate(currentSurvivor, extendedScore, "exact");
                    }
                    else
                    {
                        maxOtherUpperBound = Math.Max(maxOtherUpperBound, candidate.SearchScore);
                    }
                }

                if (extendedScore > maxOtherUpperBound)
                {
                    // Every other move has trueScore <= its stored upper bound, so the
                    // deeper survivor is still provably best without reopening any branch.
                    result = new RootSearchResult(currentSurvivor, extendedScore, updated);
                    searchMonitor?.ReportRootCandidate(depth, currentSurvivor, extendedScore);
                    continue;
                }

                // The survivor was revealed as a sufficiently fast forced loss that an
                // older losing upper bound could now be better. Re-open the root once so
                // alpha-beta can compare mate distances correctly. This is intentionally
                // the exceptional path; normal non-losing survivors never reach it.
                result = context.Options.MaxParallelism > 1 && depth >= 3 && rootMoves.Length > 1
                    ? SearchRootParallel(
                        state,
                        depth,
                        context,
                        rootMoves.ToArray(),
                        searchMonitor,
                        currentSurvivor,
                        rootExtensionPly,
                        exactAllRootScores)
                    : SearchRootSerial(
                        state,
                        depth,
                        context,
                        rootMoves,
                        searchMonitor,
                        currentSurvivor,
                        rootExtensionPly,
                        exactAllRootScores);
            }
            catch (SearchAbortedException)
            {
                // Selective deepening is optional work. If the node/time budget expires
                // during the extra survivor-only pass, retain the already completed result
                // at this nominal depth instead of throwing the entire iteration away.
                context.MarkSelectiveExtensionAborted();
                return completedBeforeExtension;
            }
        }

        return result;
    }

    private static int SearchOnlySurvivalRootMoveExactly(
        GameEngine state,
        Move move,
        int nominalRootDepth,
        int rootExtensionPly,
        SearchContext context)
    {
        int childDepth = nominalRootDepth - 1 + rootExtensionPly;
        // Root adaptive deepening has its own cap. Recursive selective-extension budget
        // stays conservative and is simply exhausted once root deepening reaches it.
        int extensionBudget = Math.Max(
            0,
            context.Options.MaxOnlySurvivalExtensionsPerLine - rootExtensionPly);

        GameEngine.SearchUndo undo = state.ApplyGeneratedMoveForSearch(move);
        try
        {
            return Search(
                state,
                childDepth,
                context,
                -Infinity,
                Infinity,
                plyFromRoot: 1,
                extensionBudget: extensionBudget);
        }
        finally
        {
            state.UndoSearchMove(undo);
        }
    }

    private static RootSearchResult SearchRootSerial(
        GameEngine state,
        int depth,
        SearchContext context,
        ReadOnlySpan<Move> ordered,
        CpuSearchMonitor? searchMonitor,
        Move? rootOnlySurvivalMove,
        int rootExtensionPly,
        bool exactAllRootScores)
    {
        int alpha = -Infinity;
        const int beta = Infinity;
        Move bestMove = ordered[0];
        int bestScore = -Infinity;
        var candidates = new CpuCandidate[ordered.Length];

        for (int moveIndex = 0; moveIndex < ordered.Length; moveIndex++)
        {
            Move move = ordered[moveIndex];
            context.CheckAbort(force: false);
            int alphaBefore = alpha;

            bool extend = rootOnlySurvivalMove is { } survivor &&
                          move == survivor &&
                          rootExtensionPly > 0;
            if (extend)
            {
                context.OnlySurvivalExtensions++;
                context.RecordAdaptiveRootDeepening(rootExtensionPly);
            }
            int childDepth = depth - 1 + (extend ? rootExtensionPly : 0);
            int extensionBudget = Math.Max(
                0,
                context.Options.MaxOnlySurvivalExtensionsPerLine - (extend ? rootExtensionPly : 0));

            GameEngine.SearchUndo undo = state.ApplyGeneratedMoveForSearch(move);
            int score;
            try
            {
                if (exactAllRootScores ||
                    !context.Options.UsePrincipalVariationSearch ||
                    moveIndex == 0)
                {
                    score = Search(
                        state,
                        childDepth,
                        context,
                        exactAllRootScores ? -Infinity : alpha,
                        beta,
                        plyFromRoot: 1,
                        extensionBudget: extensionBudget);
                }
                else
                {
                    // PVS: after the principal candidate has established alpha, ask each
                    // later root move only whether it can beat alpha. A fail-high is then
                    // re-searched with the normal window to recover an exact score.
                    int probeBeta = NextScore(alpha);
                    context.PvsNullWindowProbes++;
                    score = Search(
                        state,
                        childDepth,
                        context,
                        alpha,
                        probeBeta,
                        plyFromRoot: 1,
                        extensionBudget: extensionBudget);
                    if (score > alpha)
                    {
                        context.PvsResearches++;
                        score = Search(
                            state,
                            childDepth,
                            context,
                            alpha,
                            beta,
                            plyFromRoot: 1,
                            extensionBudget: extensionBudget);
                    }
                }
            }
            finally
            {
                state.UndoSearchMove(undo);
            }

            bool exact = exactAllRootScores || score > alphaBefore || moveIndex == 0;
            string bound = exact ? "exact" : "upper";
            candidates[moveIndex] = new CpuCandidate(move, score, bound);

            // A fail-low root result is only an upper bound. It must never replace a
            // proven exact best move merely because the returned bound ties bestScore.
            if (exact &&
                (score > bestScore ||
                 (score == bestScore && CompareMoves(move, bestMove, state.CurrentPlayer) < 0)))
            {
                bestScore = score;
                bestMove = move;
                searchMonitor?.ReportRootCandidate(depth, bestMove, bestScore);
            }

            if (!exactAllRootScores)
            {
                alpha = Math.Max(alpha, bestScore);
            }
        }

        return new RootSearchResult(bestMove, bestScore, candidates);
    }

    private static RootSearchResult SearchRootParallel(
        GameEngine state,
        int depth,
        SearchContext context,
        IReadOnlyList<Move> ordered,
        CpuSearchMonitor? searchMonitor,
        Move? rootOnlySurvivalMove,
        int rootExtensionPly,
        bool exactAllRootScores)
    {
        // Young Brothers Wait Concept style root split:
        // search the best-ordered move first to establish alpha, then fan out the
        // remaining root moves. v0.2.36.7 adds a PVS zero-window probe before any
        // expensive full-window re-search of a sibling that appears able to beat alpha.
        var scores = new int[ordered.Count];
        var bounds = new string?[ordered.Count];
        var completed = new int[ordered.Count];

        context.CheckAbort(force: false);
        bool extendFirst = rootOnlySurvivalMove is { } survivor &&
                           ordered[0] == survivor &&
                           rootExtensionPly > 0;
        if (extendFirst)
        {
            context.OnlySurvivalExtensions++;
            context.RecordAdaptiveRootDeepening(rootExtensionPly);
        }
        int firstDepth = depth - 1 + (extendFirst ? rootExtensionPly : 0);
        int firstBudget = Math.Max(
            0,
            context.Options.MaxOnlySurvivalExtensionsPerLine - (extendFirst ? rootExtensionPly : 0));

        GameEngine.SearchUndo firstUndo = state.ApplyGeneratedMoveForSearch(ordered[0]);
        try
        {
            scores[0] = Search(
                state,
                firstDepth,
                context,
                -Infinity,
                Infinity,
                plyFromRoot: 1,
                extensionBudget: firstBudget);
            bounds[0] = "exact";
            completed[0] = 1;
            searchMonitor?.ReportRootCandidate(depth, ordered[0], scores[0]);
        }
        finally
        {
            state.UndoSearchMove(firstUndo);
        }

        if (ordered.Count == 1)
        {
            return new RootSearchResult(
                ordered[0],
                scores[0],
                new[] { new CpuCandidate(ordered[0], scores[0], "exact") });
        }

        int sharedAlpha = scores[0];
        int aborted = 0;
        int cancelled = 0;

        Parallel.For<ParallelRootWorker>(
            1,
            ordered.Count,
            new ParallelOptions { MaxDegreeOfParallelism = context.Options.MaxParallelism },
            () => new ParallelRootWorker(state.Clone(), context.CreateParallelChild()),
            (i, _, worker) =>
            {
                if (Volatile.Read(ref aborted) != 0 || Volatile.Read(ref cancelled) != 0)
                {
                    return worker;
                }

                using var priorityScope = CpuWorkPriority.Enter(context.Options.UseBelowNormalThreadPriority);
                try
                {
                    Move move = ordered[i];
                    bool extend = rootOnlySurvivalMove is { } rootSurvivor &&
                                  move == rootSurvivor &&
                                  rootExtensionPly > 0;
                    if (extend)
                    {
                        worker.Context.OnlySurvivalExtensions++;
                        worker.Context.RecordAdaptiveRootDeepening(rootExtensionPly);
                    }
                    int childDepth = depth - 1 + (extend ? rootExtensionPly : 0);
                    int extensionBudget = Math.Max(
                        0,
                        context.Options.MaxOnlySurvivalExtensionsPerLine - (extend ? rootExtensionPly : 0));

                    GameEngine.SearchUndo undo = worker.State.ApplyGeneratedMoveForSearch(move);
                    try
                    {
                        int alphaForMove = Volatile.Read(ref sharedAlpha);
                        int score;
                        if (exactAllRootScores || !context.Options.UsePrincipalVariationSearch)
                        {
                            score = Search(
                                worker.State,
                                childDepth,
                                worker.Context,
                                exactAllRootScores ? -Infinity : alphaForMove,
                                Infinity,
                                plyFromRoot: 1,
                                extensionBudget: extensionBudget);
                        }
                        else
                        {
                            worker.Context.PvsNullWindowProbes++;
                            score = Search(
                                worker.State,
                                childDepth,
                                worker.Context,
                                alphaForMove,
                                NextScore(alphaForMove),
                                plyFromRoot: 1,
                                extensionBudget: extensionBudget);
                            if (score > alphaForMove)
                            {
                                worker.Context.PvsResearches++;
                                score = Search(
                                    worker.State,
                                    childDepth,
                                    worker.Context,
                                    alphaForMove,
                                    Infinity,
                                    plyFromRoot: 1,
                                    extensionBudget: extensionBudget);
                            }
                        }

                        scores[i] = score;
                        bool exact = exactAllRootScores || score > alphaForMove;
                        bounds[i] = exact ? "exact" : "upper";
                        if (exact)
                        {
                            AtomicMax(ref sharedAlpha, score);
                            searchMonitor?.ReportRootCandidate(depth, move, score);
                        }
                        Volatile.Write(ref completed[i], 1);
                    }
                    finally
                    {
                        worker.State.UndoSearchMove(undo);
                    }
                }
                catch (SearchAbortedException)
                {
                    Interlocked.Exchange(ref aborted, 1);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Exchange(ref cancelled, 1);
                }

                return worker;
            },
            worker =>
            {
                context.MergeCountersFrom(worker.Context);
                worker.Dispose();
            });

        if (Volatile.Read(ref cancelled) != 0)
        {
            context.ThrowIfCancelled();
        }

        bool hasIncomplete = false;
        for (int i = 0; i < completed.Length; i++)
        {
            if (Volatile.Read(ref completed[i]) == 0)
            {
                hasIncomplete = true;
                break;
            }
        }
        if (Volatile.Read(ref aborted) != 0 || hasIncomplete)
        {
            throw new SearchAbortedException();
        }

        Move bestMove = ordered[0];
        int bestScore = scores[0];
        var candidates = new CpuCandidate[ordered.Count];
        candidates[0] = new CpuCandidate(ordered[0], scores[0], "exact");

        for (int i = 1; i < ordered.Count; i++)
        {
            Move move = ordered[i];
            int score = scores[i];
            string bound = bounds[i] ?? "upper";
            candidates[i] = new CpuCandidate(move, score, bound);

            bool exact = bound == "exact";
            if (exact &&
                (score > bestScore ||
                 (score == bestScore && CompareMoves(move, bestMove, state.CurrentPlayer) < 0)))
            {
                bestScore = score;
                bestMove = move;
            }
        }

        return new RootSearchResult(bestMove, bestScore, candidates);
    }

    private static bool TryFindOnlySurvivalRootMove(
        IReadOnlyList<CpuCandidate>? previousRootScores,
        out Move survivor)
    {
        survivor = default;
        if (previousRootScores is null || previousRootScores.Count < 2)
        {
            return false;
        }

        int survivingCount = 0;
        for (int i = 0; i < previousRootScores.Count; i++)
        {
            CpuCandidate candidate = previousRootScores[i];
            // At the CPU root, fail-low entries are upper bounds. Therefore an upper
            // bound already inside the forced-loss band is sufficient proof that the
            // move is losing; anything else is conservatively treated as a survivor.
            if (candidate.SearchScore <= -MateScoreThreshold)
            {
                continue;
            }

            survivingCount++;
            survivor = candidate.Move;
            if (survivingCount > 1)
            {
                return false;
            }
        }

        return survivingCount == 1;
    }

    private static void PromoteMoveInPlace(Span<Move> moves, Move move)
    {
        for (int i = 0; i < moves.Length; i++)
        {
            if (moves[i] != move) continue;
            if (i > 0)
            {
                Move first = moves[0];
                moves[0] = moves[i];
                moves[i] = first;
            }
            return;
        }
    }

    private static int NextScore(int value) =>
        value >= Infinity ? Infinity : value + 1;

    private static int PreviousScore(int value) =>
        value <= -Infinity ? -Infinity : value - 1;

    private static void AtomicMax(ref int target, int value)
    {
        int current = Volatile.Read(ref target);
        while (value > current)
        {
            int observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current)
            {
                return;
            }
            current = observed;
        }
    }

    private static int Search(
        GameEngine state,
        int depth,
        SearchContext context,
        int alpha,
        int beta,
        int plyFromRoot,
        int extensionBudget = -1)
    {
        if (extensionBudget < 0)
        {
            extensionBudget = context.Options.MaxOnlySurvivalExtensionsPerLine;
        }

        context.VisitNode();
        context.CheckAbort(force: false);

        if (state.Outcome != GameOutcome.Ongoing)
        {
            return TerminalScore(state, context.Perspective, plyFromRoot);
        }

        int callerAlpha = alpha;
        int callerBeta = beta;

        // v0.2.36.7 mate-distance pruning. The terminal state above has already been
        // handled, so any new win/loss reachable from this node must be at least one
        // ply farther away. Scores outside this interval are impossible and can tighten
        // an inherited alpha/beta window without touching any real move.
        if (context.Options.UseMateDistancePruning)
        {
            int nextPly = Math.Min(WinScore - 1, plyFromRoot + 1);
            int lowestPossible = -WinScore + nextPly;
            int highestPossible = WinScore - nextPly;
            alpha = Math.Max(alpha, lowestPossible);
            beta = Math.Min(beta, highestPossible);
            if (alpha >= beta)
            {
                context.MateDistancePrunes++;
                return alpha;
            }
        }

        if (depth <= 0)
        {
            // Mate-distance Scout is a pure proof search: a nonterminal horizon is
            // explicitly "unknown" (0), never a heuristic static score. This makes
            // mate proof independent of evaluation weights and guarantees that only
            // terminal results can enter the mate band.
            return context.IsMateDistanceScout ? 0 : context.EvaluateStatic(state);
        }

        int originalAlpha = alpha;
        int originalBeta = beta;
        ulong positionKey = state.GetSearchHash();
        ulong ttKey = context.GetTranspositionKey(positionKey, extensionBudget);
        Move? ttMove = null;

        if (context.Options.UseTranspositionTable &&
            context.Transposition.TryGetValue(ttKey, out TranspositionEntry entry))
        {
            // Count any usable TT lookup, including shallower entries used only
            // for move ordering during iterative deepening.
            context.TranspositionHits++;
            ttMove = entry.BestMove;
            int ttScore = FromTranspositionScore(entry.Score, plyFromRoot);
            if (entry.Depth >= depth)
            {
                switch (entry.Bound)
                {
                    case BoundType.Exact:
                        return ttScore;
                    case BoundType.Lower:
                        alpha = Math.Max(alpha, ttScore);
                        break;
                    case BoundType.Upper:
                        beta = Math.Min(beta, ttScore);
                        break;
                }

                if (alpha >= beta)
                {
                    return ttScore;
                }
            }
        }

        Span<Move> moveBuffer = stackalloc Move[GameEngine.MaxLegalMoves];
        int moveCount = state.GenerateLegalMoves(state.CurrentPlayer, moveBuffer);
        if (moveCount == 0)
        {
            // Keep Scout semantics purely proof-based even for a rule-edge position that
            // is still marked Ongoing but happens to expose no generated move.
            return context.IsMateDistanceScout ? 0 : context.EvaluateStatic(state);
        }

        bool maximizing = state.CurrentPlayer == context.Perspective;
        int bestScore = maximizing ? -Infinity : Infinity;
        Span<Move> ordered = moveBuffer[..moveCount];
        OrderMovesInPlace(state, ordered, ttMove, context, plyFromRoot, null);

        // Only-survival hints are learned conservatively from a previously completed
        // full-window search of the same node. On the next iterative-deepening pass the
        // survivor is promoted to PV position and receives one extra ply. A per-line
        // budget prevents extension chains from running away.
        Move? extensionMove = null;
        if (context.Options.UseOnlySurvivalExtension &&
            extensionBudget > 0 &&
            context.TryGetOnlySurvivalHint(positionKey, Math.Max(1, depth - 1), out Move hintedMove))
        {
            extensionMove = hintedMove;
            PromoteMoveInPlace(ordered, hintedMove);
        }

        Move bestMove = ordered[0];
        int nonLosingMoveCount = 0;
        Move soleSurvivor = default;
        bool cutOff = false;
        bool fullWindowNode = callerAlpha == -Infinity && callerBeta == Infinity;

        for (int moveIndex = 0; moveIndex < ordered.Length; moveIndex++)
        {
            Move move = ordered[moveIndex];
            bool extend = extensionMove is { } survivor && move == survivor;
            if (extend) context.OnlySurvivalExtensions++;
            int childDepth = depth - 1 + (extend ? 1 : 0);
            int childExtensionBudget = Math.Max(0, extensionBudget - (extend ? 1 : 0));

            int reduction = GetLateMoveReduction(
                state,
                move,
                ttMove,
                extensionMove,
                context,
                depth,
                moveIndex,
                plyFromRoot);
            if (extend)
            {
                reduction = 0;
            }
            reduction = Math.Min(reduction, Math.Max(0, childDepth));

            GameEngine.SearchUndo undo = state.ApplyGeneratedMoveForSearch(move);
            int score;
            try
            {
                score = SearchChildWithPvsAndLmr(
                    state,
                    childDepth,
                    reduction,
                    context,
                    alpha,
                    beta,
                    plyFromRoot + 1,
                    childExtensionBudget,
                    moveIndex,
                    maximizing);
            }
            finally
            {
                state.UndoSearchMove(undo);
            }

            bool moverForcedLoss = maximizing
                ? score <= -MateScoreThreshold
                : score >= MateScoreThreshold;
            if (!moverForcedLoss)
            {
                nonLosingMoveCount++;
                soleSurvivor = move;
            }

            if (maximizing)
            {
                if (score > bestScore || (score == bestScore && CompareMoves(move, bestMove, state.CurrentPlayer) < 0))
                {
                    bestScore = score;
                    bestMove = move;
                }
                alpha = Math.Max(alpha, bestScore);
            }
            else
            {
                if (score < bestScore || (score == bestScore && CompareMoves(move, bestMove, state.CurrentPlayer) < 0))
                {
                    bestScore = score;
                    bestMove = move;
                }
                beta = Math.Min(beta, bestScore);
            }

            if (beta <= alpha)
            {
                cutOff = true;
                context.BetaCutoffs++;
                context.RecordCutoff(move, depth, plyFromRoot, state.CurrentPlayer);
                break;
            }
        }

        // A hint is recorded only after a complete full-window node. In that case, later
        // PVS fail-lows are safe upper bounds for a maximizing node (and fail-highs are
        // safe lower bounds for a minimizing node), so a score already inside the mate
        // loss band proves that alternative cannot survive. Narrow-window/cutoff nodes
        // are deliberately excluded from hint generation.
        if (context.Options.UseOnlySurvivalExtension &&
            !cutOff &&
            fullWindowNode &&
            moveCount > 1 &&
            nonLosingMoveCount == 1)
        {
            context.RecordOnlySurvivalHint(positionKey, depth, soleSurvivor);
        }

        if (context.Options.UseTranspositionTable)
        {
            BoundType bound = bestScore <= originalAlpha
                ? BoundType.Upper
                : bestScore >= originalBeta
                    ? BoundType.Lower
                    : BoundType.Exact;

            context.Transposition.Store(
                ttKey,
                depth,
                ToTranspositionScore(bestScore, plyFromRoot),
                bound,
                bestMove);
        }

        return bestScore;
    }

    private static int SearchChildWithPvsAndLmr(
        GameEngine state,
        int childDepth,
        int reduction,
        SearchContext context,
        int alpha,
        int beta,
        int plyFromRoot,
        int extensionBudget,
        int moveIndex,
        bool maximizing)
    {
        int reducedDepth = Math.Max(0, childDepth - reduction);
        bool usePvs = context.Options.UsePrincipalVariationSearch && moveIndex > 0;
        if (reduction > 0) context.LmrReducedSearches++;

        if (!usePvs)
        {
            int score = Search(
                state,
                reducedDepth,
                context,
                alpha,
                beta,
                plyFromRoot,
                extensionBudget);

            // Verification search: a reduced late move is never allowed to change alpha
            // or beta without first being searched again at the unreduced depth.
            if (reduction > 0 &&
                (maximizing ? score > alpha : score < beta))
            {
                context.LmrVerificationResearches++;
                score = Search(
                    state,
                    childDepth,
                    context,
                    alpha,
                    beta,
                    plyFromRoot,
                    extensionBudget);
            }

            return score;
        }

        if (maximizing)
        {
            int probeBeta = Math.Min(beta, NextScore(alpha));
            context.PvsNullWindowProbes++;
            int score = Search(
                state,
                reducedDepth,
                context,
                alpha,
                probeBeta,
                plyFromRoot,
                extensionBudget);

            if (reduction > 0 && score > alpha)
            {
                context.LmrVerificationResearches++;
                score = Search(
                    state,
                    childDepth,
                    context,
                    alpha,
                    probeBeta,
                    plyFromRoot,
                    extensionBudget);
            }

            if (score > alpha && score < beta)
            {
                context.PvsResearches++;
                score = Search(
                    state,
                    childDepth,
                    context,
                    alpha,
                    beta,
                    plyFromRoot,
                    extensionBudget);
            }

            return score;
        }
        else
        {
            int probeAlpha = Math.Max(alpha, PreviousScore(beta));
            context.PvsNullWindowProbes++;
            int score = Search(
                state,
                reducedDepth,
                context,
                probeAlpha,
                beta,
                plyFromRoot,
                extensionBudget);

            if (reduction > 0 && score < beta)
            {
                context.LmrVerificationResearches++;
                score = Search(
                    state,
                    childDepth,
                    context,
                    probeAlpha,
                    beta,
                    plyFromRoot,
                    extensionBudget);
            }

            if (score < beta && score > alpha)
            {
                context.PvsResearches++;
                score = Search(
                    state,
                    childDepth,
                    context,
                    alpha,
                    beta,
                    plyFromRoot,
                    extensionBudget);
            }

            return score;
        }
    }

    private static int GetLateMoveReduction(
        GameEngine state,
        Move move,
        Move? ttMove,
        Move? extensionMove,
        SearchContext context,
        int depth,
        int moveIndex,
        int plyFromRoot)
    {
        if (!context.Options.UseLateMoveReductions ||
            depth < 4 ||
            moveIndex < 3)
        {
            return 0;
        }

        if ((ttMove is { } transpositionMove && move == transpositionMove) ||
            (extensionMove is { } survivor && move == survivor))
        {
            return 0;
        }

        PlayerId mover = state.CurrentPlayer;
        Position ownRunner = state.FindRunner(mover);
        if (move.From == ownRunner || move.Kind == MoveKind.Sacrifice)
        {
            return 0;
        }

        // Killer/history moves have already demonstrated tactical relevance in this
        // search. Do not reduce them merely because they appear late in this node.
        if (context.KillerScore(move, plyFromRoot) > 0 ||
            context.HistoryScore(move, mover) >= LmrHistoryProtectionThreshold)
        {
            return 0;
        }

        if (IsStarRunnerCriticalBlockerMove(state, move, mover))
        {
            return 0;
        }

        // Deliberately conservative schedule. The 4th+ quiet move loses one ply; only
        // very late moves in a reasonably deep node lose two. Any reduced move that
        // threatens alpha/beta is immediately verified at full depth above.
        int reduction = 1;
        if (depth >= 8 && moveIndex >= 10)
        {
            reduction = 2;
        }

        return Math.Min(reduction, Math.Max(0, depth - 2));
    }

    private static bool IsStarRunnerCriticalBlockerMove(
        GameEngine state,
        Move move,
        PlayerId mover)
    {
        Position ownRunner = state.FindRunner(mover);
        Position enemyRunner = state.FindRunner(mover.Opponent());

        // Any blocker touching either Runner can create/remove sacrifice support,
        // containment or an escape lane.
        if (IsAdjacentOrSame(move.From, ownRunner) ||
            IsAdjacentOrSame(move.To, ownRunner) ||
            IsAdjacentOrSame(move.From, enemyRunner) ||
            IsAdjacentOrSame(move.To, enemyRunner))
        {
            return true;
        }

        Position ownFront = ForwardSquare(mover, ownRunner);
        Position enemyFront = ForwardSquare(mover.Opponent(), enemyRunner);

        // Direct markers and their one-step shadow network are exactly the kind of
        // defensive moves that looked "quiet" in the 2026-08-14 loss. G7-F7 in the
        // ply-74 analysis lands on P2 Runner F6's front square F7 and is protected here.
        if ((ownFront.IsInside &&
             (IsAdjacentOrSame(move.From, ownFront) || IsAdjacentOrSame(move.To, ownFront))) ||
            (enemyFront.IsInside &&
             (IsAdjacentOrSame(move.From, enemyFront) || IsAdjacentOrSame(move.To, enemyFront))))
        {
            return true;
        }

        // Home-row defense and enemy-goal bridgehead changes are strategically volatile.
        if (move.From.Row == 0 ||
            move.From.Row == GameEngine.BoardSize - 1 ||
            move.To.Row == 0 ||
            move.To.Row == GameEngine.BoardSize - 1)
        {
            return true;
        }

        // Existing ordering gives large bonuses to immediate front blocks / goal-row
        // actions. Treat those as tactical even if future ordering constants change.
        return MoveOrderingScore(state, move, mover) >= 80;
    }

    private static Position ForwardSquare(PlayerId owner, Position runner)
    {
        int dr = owner == PlayerId.Player1 ? -1 : 1;
        return new Position(runner.Row + dr, runner.Col);
    }

    private static bool IsAdjacentOrSame(Position a, Position b)
    {
        if (!a.IsInside || !b.IsInside) return false;
        return Math.Abs(a.Row - b.Row) <= 1 && Math.Abs(a.Col - b.Col) <= 1;
    }

    private static int ToTranspositionScore(int score, int plyFromRoot)
    {
        if (score >= MateScoreThreshold) return score + plyFromRoot;
        if (score <= -MateScoreThreshold) return score - plyFromRoot;
        return score;
    }

    private static int FromTranspositionScore(int score, int plyFromRoot)
    {
        if (score >= MateScoreThreshold) return score - plyFromRoot;
        if (score <= -MateScoreThreshold) return score + plyFromRoot;
        return score;
    }

    private static void OrderMovesInPlace(
        GameEngine state,
        Span<Move> moves,
        Move? ttMove,
        SearchContext context,
        int plyFromRoot,
        IReadOnlyList<CpuCandidate>? previousRootScores)
    {
        PlayerId mover = state.CurrentPlayer;
        Span<int> scores = stackalloc int[moves.Length];
        Span<int> canonicalKeys = stackalloc int[moves.Length];

        for (int i = 0; i < moves.Length; i++)
        {
            Move move = moves[i];
            int score = MoveOrderingScore(state, move, mover);
            bool realRunnerReturn = plyFromRoot == 0 && state.IsRealRunnerReturnMove(mover, move);
            // TT/root-history ordering must not lock an equal-score Runner reversal into
            // the first (therefore exact) root slot forever across iterative deepening.
            // If the return is actually stronger, searching it later still produces a
            // fail-high exact score and it wins normally. No minimax score is changed.
            if (ttMove is { } transpositionMove && move == transpositionMove && !realRunnerReturn)
            {
                score += 1_000_000;
            }
            if (previousRootScores is not null && TryGetPreviousRootScore(previousRootScores, move, out int previousScore))
            {
                score += Math.Clamp(previousScore, -100_000, 100_000) * 2;
            }
            if (realRunnerReturn)
            {
                score -= RootRunnerReturnOrderingPenalty;
            }
            score += context.KillerScore(move, plyFromRoot);
            score += context.HistoryScore(move, mover);
            scores[i] = score;
            canonicalKeys[i] = CanonicalMoveSortKey(move, mover);
        }

        // Legal move counts are at most 60 in this ruleset. Insertion sort is a good
        // fit for these tiny, often nearly ordered lists and avoids LINQ/List allocations.
        for (int i = 1; i < moves.Length; i++)
        {
            Move move = moves[i];
            int score = scores[i];
            int key = canonicalKeys[i];
            int j = i - 1;
            while (j >= 0 && (scores[j] < score || (scores[j] == score && canonicalKeys[j] > key)))
            {
                moves[j + 1] = moves[j];
                scores[j + 1] = scores[j];
                canonicalKeys[j + 1] = canonicalKeys[j];
                j--;
            }
            moves[j + 1] = move;
            scores[j + 1] = score;
            canonicalKeys[j + 1] = key;
        }
    }

    private static bool TryGetPreviousRootScore(IReadOnlyList<CpuCandidate> candidates, Move move, out int score)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            CpuCandidate candidate = candidates[i];
            if (candidate.Move == move)
            {
                score = candidate.SearchScore;
                return true;
            }
        }

        score = 0;
        return false;
    }

    private static int MoveOrderingScore(GameEngine state, Move move, PlayerId mover)
    {
        int score = 0;
        if (move.From == state.FindRunner(mover))
        {
            int before = Progress(mover, move.From);
            int after = Progress(mover, move.To);
            score += (after - before) * 160;

            int goalRow = mover == PlayerId.Player1 ? 0 : 7;
            if (move.To.Row == goalRow)
            {
                score += 500_000;
            }

            if (move.Kind == MoveKind.Sacrifice)
            {
                score -= 30;
                if (after > before)
                {
                    score += 85;
                }
            }

            score += Centrality(move.To.Col) * 3;
        }
        else
        {
            Position enemyRunner = state.FindRunner(mover.Opponent());
            int targetFrontRow = enemyRunner.Row + (mover.Opponent() == PlayerId.Player1 ? -1 : 1);
            if (move.To.Row == targetFrontRow && move.To.Col == enemyRunner.Col)
            {
                score += 180;
            }

            int enemyGoalRow = mover == PlayerId.Player1 ? 0 : 7;
            if (move.To.Row == enemyGoalRow)
            {
                score += 95;
            }

            int before = Progress(mover, move.From);
            int after = Progress(mover, move.To);
            score += (after - before) * 10;
        }

        return score;
    }

    private static int EvaluateMoveOnePly(GameEngine state, Move move, PlayerId perspective, CpuEvaluationProfile evaluationProfile)
    {
        GameEngine.SearchUndo undo = state.ApplyGeneratedMoveForSearch(move);
        try
        {
            return state.Outcome == GameOutcome.Ongoing
                ? EvaluateDetailed(state, perspective, evaluationProfile).Total
                : TerminalScore(state, perspective, 1);
        }
        finally
        {
            state.UndoSearchMove(undo);
        }
    }

    private static int TerminalScore(GameEngine state, PlayerId perspective, int plyFromRoot)
    {
        if (state.Outcome == GameOutcome.Draw)
        {
            return 0;
        }

        bool won = (state.Outcome == GameOutcome.Player1Win && perspective == PlayerId.Player1) ||
                   (state.Outcome == GameOutcome.Player2Win && perspective == PlayerId.Player2);
        return won ? WinScore - plyFromRoot : -WinScore + plyFromRoot;
    }

    private static int Progress(PlayerId player, Position position) =>
        player == PlayerId.Player1 ? 7 - position.Row : position.Row;

    private static int CountFriendlyBlockersAdjacent(GameEngine state, Position center, PlayerId owner)
    {
        int square = center.Row * GameEngine.BoardSize + center.Col;
        return BitOperations.PopCount(state.GetBlockerBits(owner) & AdjacentMasks[square]);
    }

    private static int RunnerFrontPressureSignal(GameEngine state, PlayerId runnerOwner, Position runner)
    {
        int dr = runnerOwner == PlayerId.Player1 ? -1 : 1;
        var front = new Position(runner.Row + dr, runner.Col);
        if (!front.IsInside) return 0;

        PlayerId markerOwner = runnerOwner.Opponent();
        ulong friendlyBlockers = state.GetBlockerBits(runnerOwner);
        ulong markerBlockers = state.GetBlockerBits(markerOwner);
        ulong frontBit = 1UL << (front.Row * GameEngine.BoardSize + front.Col);

        // A friendly blocker directly ahead is a protected sacrifice advance. It cannot
        // simultaneously be occupied by the opponent, so there is no front containment.
        if ((friendlyBlockers & frontBit) != 0) return 0;

        bool protectedForwardAdvance = HasForwardSacrificeAdvance(state, runnerOwner, runner);
        bool directMarker = (markerBlockers & frontBit) != 0;
        if (directMarker)
        {
            // A diagonal/forward friendly sacrifice square gives a guaranteed way around
            // the marker. Keep a residual penalty because the direct square is still lost.
            return protectedForwardAdvance ? 35 : 100;
        }

        // Shadow pressure: if a marker blocker is one king-step from the empty forward
        // square, it can normally follow a sideways Runner and re-establish the direct mark.
        // Tempo matters: the shadow is more dangerous when the marker side moves next.
        int frontSquare = front.Row * GameEngine.BoardSize + front.Col;
        bool canShadow = (markerBlockers & AdjacentMasks[frontSquare]) != 0;
        if (!canShadow) return 0;

        bool markerCanActNow = state.CurrentPlayer == markerOwner && !state.IsRunnerForcedToRetreat(markerOwner);
        int pressure = markerCanActNow ? 65 : 40;
        if (protectedForwardAdvance)
        {
            pressure = Math.Min(pressure, 20);
        }
        return pressure;
    }

    private static bool HasForwardSacrificeAdvance(GameEngine state, PlayerId owner, Position runner)
    {
        int dr = owner == PlayerId.Player1 ? -1 : 1;
        int row = runner.Row + dr;
        if ((uint)row >= GameEngine.BoardSize) return false;

        int runnerSquare = runner.Row * GameEngine.BoardSize + runner.Col;
        ulong forwardAdjacent = AdjacentMasks[runnerSquare] & GameEngine.RowMask(row);
        return (state.GetBlockerBits(owner) & forwardAdjacent) != 0;
    }

    private static int SacrificeDebtSignal(
        int startingBlockers,
        int remainingBlockers,
        int runnerProgress,
        int runnerGoalPathValue,
        int frontPressure)
    {
        startingBlockers = Math.Clamp(startingBlockers, 0, 6);
        remainingBlockers = Math.Clamp(remainingBlockers, 0, startingBlockers);
        int spent = startingBlockers - remainingBlockers;
        if (spent <= 0 || startingBlockers <= 0) return 0;

        // Resource pressure rises as irreversible spending consumes a larger fraction of
        // the reserve still available. 2 spent / 4 remaining => 50; 1 / 5 => 20.
        int depletionPressure = remainingBlockers == 0
            ? 100
            : Math.Clamp((spent * 100 + remainingBlockers / 2) / remainingBlockers, 0, 100);

        // Spending is forgiven only when it bought *usable* attacking progress. A route
        // that looks geometrically short but is under direct/shadow containment is poor
        // compensation for permanently losing blockers.
        int progress = RunnerProgressSignal(runnerProgress);
        int path = GoalPathUrgencySignal(runnerGoalPathValue);
        int viableAttack = InteractionProductSignal(progress, path);
        viableAttack = InteractionProductSignal(viableAttack, 100 - Math.Clamp(frontPressure, 0, 100));

        return InteractionProductSignal(depletionPressure, 100 - viableAttack);
    }

    private static int NormalizeFeatureDifference(int difference, int maxAbsoluteDifference)
    {
        if (maxAbsoluteDifference <= 0) return 0;
        int clamped = Math.Clamp(difference, -maxAbsoluteDifference, maxAbsoluteDifference);
        int numerator = clamped * 100;
        int rounding = Math.Sign(numerator) * (maxAbsoluteDifference / 2);
        return (numerator + rounding) / maxAbsoluteDifference;
    }


    private static int GoalPathUrgencySignal(int runnerGoalPathValue) =>
        Math.Clamp((runnerGoalPathValue * 100 + 4) / 8, 0, 100);

    private static int RunnerProgressSignal(int progress) =>
        Math.Clamp((progress * 100 + 3) / 7, 0, 100);

    private static int BridgeheadConnectionSignal(int connectionValue) =>
        Math.Clamp((connectionValue * 100 + 35) / 70, 0, 100);

    private static int InteractionProductSignal(int a, int b) =>
        (Math.Clamp(a, 0, 100) * Math.Clamp(b, 0, 100) + 50) / 100;

    private static int SacrificeChainReadinessSignal(GameEngine state, PlayerId owner, Position runner)
    {
        int startProgress = Progress(owner, runner);
        int remainingToGoal = 7 - startProgress;
        if (remainingToGoal <= 0) return 100;

        // Only already-placed friendly blockers are traversed here. In other words, this
        // measures how much of the Runner's future route is physically prepared as a
        // sacrifice chain *now*, without assuming that empty squares can later be filled.
        // A blocker chain may connect through any of the 8 adjacent squares, matching the
        // sacrifice geometry used by the game. There are at most six blockers per side,
        // so this flood is tiny compared with the main search.
        ulong friendly = state.GetBlockerBits(owner);
        int runnerSquare = runner.Row * GameEngine.BoardSize + runner.Col;
        ulong frontier = friendly & AdjacentMasks[runnerSquare];
        ulong visited = 0;
        int bestProgress = startProgress;

        while (frontier != 0)
        {
            ulong current = frontier & ~visited;
            if (current == 0) break;
            visited |= current;

            ulong scan = current;
            ulong next = 0;
            while (scan != 0)
            {
                int square = BitOperations.TrailingZeroCount(scan);
                scan &= scan - 1;
                int row = square >> 3;
                int col = square & 7;
                bestProgress = Math.Max(bestProgress, Progress(owner, new Position(row, col)));
                next |= AdjacentMasks[square] & friendly;
            }

            frontier = next & ~visited;
        }

        int preparedAdvance = Math.Clamp(bestProgress - startProgress, 0, remainingToGoal);
        return (preparedAdvance * 100 + remainingToGoal / 2) / remainingToGoal;
    }

    private static RunnerRouteAnalysis AnalyzeRunnerRoute(
        GameEngine state,
        PlayerId owner,
        Position runner,
        Span<ulong> shortestCorridor)
    {
        shortestCorridor.Clear();

        int goalRow = owner == PlayerId.Player1 ? 0 : GameEngine.BoardSize - 1;
        ulong goalMask = GameEngine.RowMask(goalRow);
        ulong friendlyBlockers = state.GetBlockerBits(owner);
        ulong enemyPieces = state.GetPlayerPieceBits(owner.Opponent());
        ulong bridgeheads = friendlyBlockers & goalMask;

        ulong start = 1UL << (runner.Row * GameEngine.BoardSize + runner.Col);
        ulong frontier = start;
        ulong visited = frontier;
        Span<ulong> frontiers = stackalloc ulong[9];
        frontiers.Clear();
        frontiers[0] = start;

        int goalDistance = 0;
        int goalPathValue = 0;
        ulong shortestGoalMask = 0;
        int bridgeheadConnectionValue = 0;

        for (int distance = 1; distance <= 8; distance++)
        {
            ulong orthogonal = OrthogonalNeighbors(frontier) & ~enemyPieces;
            ulong sacrifices = DiagonalNeighbors(frontier) & friendlyBlockers;
            ulong next = (orthogonal | sacrifices) & ~visited;
            if (next == 0) break;

            frontiers[distance] = next;

            if (bridgeheadConnectionValue == 0 && (next & bridgeheads) != 0)
            {
                // Keep the historical 0..70 scale: one Runner move to a reachable
                // bridgehead => 70, two => 60, ... seven => 10, eight => 0.
                bridgeheadConnectionValue = Math.Max(0, 8 - distance) * 10;
            }

            ulong reachedGoal = next & goalMask;
            if (goalDistance == 0 && reachedGoal != 0)
            {
                goalDistance = distance;
                goalPathValue = 9 - distance;
                shortestGoalMask = reachedGoal;

                // Recover only nodes that belong to at least one shortest Runner route.
                // A normal orthogonal step depends only on the destination not being an
                // enemy piece.  A diagonal predecessor is valid only when the destination
                // itself is a friendly blocker (the sacrifice step used by the forward BFS).
                shortestCorridor[distance] = reachedGoal;
                for (int d = distance; d > 0; d--)
                {
                    ulong targets = shortestCorridor[d];
                    ulong predecessors = OrthogonalNeighbors(targets);
                    ulong sacrificeTargets = targets & friendlyBlockers;
                    if (sacrificeTargets != 0)
                    {
                        predecessors |= DiagonalNeighbors(sacrificeTargets);
                    }

                    shortestCorridor[d - 1] = frontiers[d - 1] & predecessors;
                }
            }

            visited |= next;
            frontier = next;

            // Once the shortest goal layer is known, there is no reason to continue
            // unless BridgeheadConnection still needs an answer.  A bridgehead first
            // reached at distance 8 is worth 0 by definition, so distance 7 is the
            // last layer that can change the connection feature.
            if (goalDistance != 0 &&
                (bridgeheads == 0 || bridgeheadConnectionValue != 0 || distance >= 7))
            {
                break;
            }
        }

        return new RunnerRouteAnalysis(goalPathValue, goalDistance, shortestGoalMask, bridgeheadConnectionValue);
    }

    // v0.2.37.7: convert physical blocker count into effective attacking material.
    // Semantics are intentionally unchanged from v0.2.37.6, but the expensive
    // "one BFS per blocker" implementation has been replaced by reverse bitboard
    // waves that classify every blocker in parallel.  Because blocker movement is
    // undirected eight-neighbour movement on the current static occupancy graph,
    // reversing the search from an empty target is exact: the source blocker is
    // treated as the final occupied endpoint while every other occupied square
    // remains an obstacle, exactly as in the old source-by-source BFS.
    // Values are hundredths of a blocker so the D=5 transition can apply a 50%
    // discount without introducing floating point into the leaf evaluator.
    private static int EffectiveBlockerMaterial100(
        GameEngine state,
        PlayerId owner,
        int physicalBlockers,
        RunnerRouteAnalysis ownRoute,
        ReadOnlySpan<ulong> ownShortestCorridor,
        RunnerRouteAnalysis enemyRoute,
        ReadOnlySpan<ulong> enemyShortestCorridor)
    {
        if (physicalBlockers <= 0) return 0;

        int threatFactor = enemyRoute.GoalDistance switch
        {
            <= 0 => 0,
            <= 4 => 100,
            5 => 50,
            _ => 0
        };
        if (threatFactor == 0) return physicalBlockers * 100;

        PlayerId attacker = owner.Opponent();
        ulong blockers = state.GetBlockerBits(owner);
        ulong occupied = state.GetOccupiedBits();
        ulong attackerBlockers = state.GetBlockerBits(attacker);
        int defensiveTurnPenalty = state.CurrentPlayer == attacker ? 1 : 0;
        if (state.CurrentPlayer == owner && state.IsRunnerForcedToRetreat(owner))
        {
            defensiveTurnPenalty++;
        }

        // Each bucket contains blockers whose *minimum* concrete defensive slack is
        // exactly the bucket index.  Building these buckets from the attacker's route
        // targets costs at most 1+2+3+4+5 small bitboard waves when GoalDistance=5,
        // independent of the number of remaining blockers.
        Span<ulong> minimumSlackBuckets = stackalloc ulong[8];
        BuildMinimumDefensiveSlackBuckets(
            occupied,
            blockers,
            attackerBlockers,
            defensiveTurnPenalty,
            enemyRoute,
            enemyShortestCorridor,
            minimumSlackBuckets);

        ulong committedBlockers = 0;
        int maxSlack = -1;
        for (int slack = 0; slack < minimumSlackBuckets.Length; slack++)
        {
            ulong bucket = minimumSlackBuckets[slack];
            if (bucket == 0) continue;
            committedBlockers |= bucket;
            maxSlack = slack;
        }

        if (committedBlockers == 0)
        {
            return physicalBlockers * 100;
        }

        ulong offensiveTargets = OffensiveBlockerTargetMask(
            state,
            owner,
            ownRoute,
            ownShortestCorridor);

        if (offensiveTargets == 0)
        {
            int allCommitted = BitOperations.PopCount(committedBlockers);
            return physicalBlockers * 100 - allCommitted * threatFactor;
        }

        // A source blocker that is already on an offensive staging target has ETA=0.
        // Other occupied offensive targets are not traversable, so only empty targets
        // seed the reverse wave.  As the wave expands, all source blockers adjacent to
        // it become attack-capable at the corresponding ETA.  This answers all source
        // blockers simultaneously and preserves the v0.2.37.6 static-board semantics.
        ulong attackCapableWithinSlack = blockers & offensiveTargets;
        ulong attackCapableCommitted = minimumSlackBuckets[0] & attackCapableWithinSlack;

        ulong frontier = offensiveTargets & ~occupied;
        ulong visited = frontier;
        for (int slack = 1; slack <= maxSlack; slack++)
        {
            if (frontier != 0)
            {
                ulong neighbors = AdjacentNeighbors(frontier);
                attackCapableWithinSlack |= neighbors & blockers;

                ulong next = neighbors & ~occupied & ~visited;
                visited |= next;
                frontier = next;
            }

            attackCapableCommitted |=
                minimumSlackBuckets[slack] & attackCapableWithinSlack;
        }

        ulong boundBlockerBits = committedBlockers & ~attackCapableCommitted;
        int boundBlockers = BitOperations.PopCount(boundBlockerBits);
        return physicalBlockers * 100 - boundBlockers * threatFactor;
    }

    private static ulong OffensiveBlockerTargetMask(
        GameEngine state,
        PlayerId owner,
        RunnerRouteAnalysis ownRoute,
        ReadOnlySpan<ulong> ownShortestCorridor)
    {
        if (ownRoute.GoalDistance <= 0) return 0;

        ulong targets = 0;
        for (int distance = 0; distance < ownRoute.GoalDistance; distance++)
        {
            targets |= ForwardDiagonalNeighbors(ownShortestCorridor[distance], owner);
        }

        // A blocker can only become an attacking sacrifice on a square that is not
        // occupied by an enemy piece. Other friendly pieces remain in the mask; the
        // reverse reachability logic treats them as occupied unless the target is the
        // source blocker's own current square (e.g. an already-prepared bridgehead).
        return targets & ~state.GetPlayerPieceBits(owner.Opponent());
    }

    // Exact parallel replacement for v0.2.37.6 DefensiveInterceptionSlack +
    // BuildBlockerReachabilityLayers. For one shortest-route layer, the reverse BFS
    // records a source blocker only on the first wave that reaches it, so the travel
    // distance is the same shortest distance that the old per-source BFS returned.
    // A blocker may have jobs on several route layers; collapsing candidate buckets
    // from slack 0 upward reproduces the old minimum-slack rule.
    private static void BuildMinimumDefensiveSlackBuckets(
        ulong occupied,
        ulong defenderBlockers,
        ulong attackerBlockers,
        int turnPenalty,
        RunnerRouteAnalysis attackerRoute,
        ReadOnlySpan<ulong> attackerShortestCorridor,
        Span<ulong> minimumSlackBuckets)
    {
        minimumSlackBuckets.Clear();
        if (defenderBlockers == 0 || attackerRoute.GoalDistance <= 0) return;

        Span<ulong> slackCandidates = stackalloc ulong[8];
        slackCandidates.Clear();

        int lastDistance = Math.Min(
            Math.Min(attackerRoute.GoalDistance, attackerShortestCorridor.Length - 1),
            7);

        for (int distance = 1; distance <= lastDistance; distance++)
        {
            int defenderMovesAvailable = distance - turnPenalty;
            if (defenderMovesAvailable <= 0) continue;

            // Shortest Runner corridor cells occupied by attacker's blockers are
            // prepared sacrifice squares and cannot be interception destinations.
            // Any remaining legal corridor destination is empty under AnalyzeRunnerRoute;
            // the explicit ~occupied mask keeps that invariant safe and makes the
            // reverse graph identical to blocker movement through current empty cells.
            ulong targets = attackerShortestCorridor[distance] &
                            ~attackerBlockers &
                            ~occupied;
            if (targets == 0) continue;

            ulong frontier = targets;
            ulong visited = targets;
            ulong reachedBlockersForLayer = 0;
            int maxTravel = Math.Min(7, defenderMovesAvailable);

            for (int travel = 1; travel <= maxTravel; travel++)
            {
                ulong neighbors = AdjacentNeighbors(frontier);

                // First contact for this route layer is the shortest travel distance
                // from each source blocker. Do not let a longer alternate path create
                // an artificially tighter slack for the same interception target set.
                ulong newlyReachedBlockers =
                    (neighbors & defenderBlockers) & ~reachedBlockersForLayer;
                if (newlyReachedBlockers != 0)
                {
                    reachedBlockersForLayer |= newlyReachedBlockers;
                    int slack = defenderMovesAvailable - travel;
                    slackCandidates[slack] |= newlyReachedBlockers;
                }

                ulong next = neighbors & ~occupied & ~visited;
                if (next == 0) break;

                visited |= next;
                frontier = next;
            }
        }

        ulong assigned = 0;
        for (int slack = 0; slack < minimumSlackBuckets.Length; slack++)
        {
            ulong exactMinimum = slackCandidates[slack] & ~assigned;
            minimumSlackBuckets[slack] = exactMinimum;
            assigned |= exactMinimum;
        }
    }

    private static ulong ForwardDiagonalNeighbors(ulong bits, PlayerId owner)
    {
        const ulong FileA = 0x0101010101010101UL;
        const ulong FileH = 0x8080808080808080UL;

        return owner == PlayerId.Player1
            ? ((bits & ~FileH) >> 7) | ((bits & ~FileA) >> 9)
            : ((bits & ~FileH) << 9) | ((bits & ~FileA) << 7);
    }

    private static int GoalDefenseStrengthSignal(
        GameEngine state,
        PlayerId defender,
        PlayerId attacker,
        RunnerRouteAnalysis attackerRoute)
    {
        if (attackerRoute.GoalDistance <= 0 || attackerRoute.ShortestGoalMask == 0)
        {
            return 0;
        }

        ulong defenderBlockers = state.GetBlockerBits(defender);
        if (defenderBlockers == 0) return 0;

        // If the attacker moves next, the defender gets one fewer move before the
        // Runner's final goal step. If the defender moves next, both sides have the
        // same number of turns before that deadline.
        int defenseMovesAvailable = attackerRoute.GoalDistance -
                                    (state.CurrentPlayer == attacker ? 1 : 0);
        if (state.CurrentPlayer == defender && state.IsRunnerForcedToRetreat(defender))
        {
            // Mandatory Runner retreat consumes the defender's next turn, so no blocker
            // can spend that tempo covering the threatened goal square.
            defenseMovesAvailable--;
        }
        defenseMovesAvailable = Math.Max(0, defenseMovesAvailable);

        ulong targets = attackerRoute.ShortestGoalMask;
        ulong attackerBlockers = state.GetBlockerBits(attacker);

        // A shortest goal destination already occupied by the attacker's blocker is a
        // prepared sacrifice square. The defender can never occupy it first, so the
        // weakest coverage is immediately zero. This is identical to the old per-target
        // BFS result, but lets us avoid searching the remaining destinations needlessly.
        if ((targets & attackerBlockers) != 0)
        {
            return 0;
        }

        // v0.2.36.4: one multi-source BFS replaces one BFS per candidate goal square.
        // Every defender blocker is a source. Because coverage monotonically decreases
        // with distance, the old "minimum coverage over targets" is exactly determined
        // by the farthest reachable shortest-goal target. If even one target is
        // unreachable within the same seven-step horizon, the old implementation also
        // returned zero.
        ulong remainingTargets = targets & ~defenderBlockers;
        int farthestDistance = 0;
        if (remainingTargets != 0)
        {
            // Coverage is clamp(70 + (available-distance)*25, 0, 100).  At
            // distance >= available+3 it is guaranteed to be zero, so searching
            // farther cannot change the final GoalDefense signal.
            int maxUsefulDistance = Math.Min(7, defenseMovesAvailable + 2);
            if (maxUsefulDistance <= 0) return 0;

            ulong occupied = state.GetOccupiedBits();
            ulong frontier = defenderBlockers;
            ulong visited = defenderBlockers;

            for (int distance = 1; distance <= maxUsefulDistance; distance++)
            {
                // Blocker movement is eight-neighbour movement into currently empty
                // squares. Starting blocker squares remain blocked/visited, matching the
                // previous static-board per-target search exactly.
                ulong next = AdjacentNeighbors(frontier) & ~occupied & ~visited;
                if (next == 0) break;

                ulong reachedTargets = next & remainingTargets;
                if (reachedTargets != 0)
                {
                    remainingTargets &= ~reachedTargets;
                    farthestDistance = distance;
                    if (remainingTargets == 0) break;
                }

                visited |= next;
                frontier = next;
            }

            if (remainingTargets != 0)
            {
                return 0;
            }
        }

        int margin = defenseMovesAvailable - farthestDistance;
        return Math.Clamp(70 + margin * 25, 0, 100);
    }

    private static ulong AdjacentNeighbors(ulong bits) =>
        OrthogonalNeighbors(bits) | DiagonalNeighbors(bits);

    private static ulong OrthogonalNeighbors(ulong bits)
    {
        const ulong FileA = 0x0101010101010101UL;
        const ulong FileH = 0x8080808080808080UL;
        return (bits >> 8) |
               (bits << 8) |
               ((bits & ~FileH) << 1) |
               ((bits & ~FileA) >> 1);
    }

    private static ulong DiagonalNeighbors(ulong bits)
    {
        const ulong FileA = 0x0101010101010101UL;
        const ulong FileH = 0x8080808080808080UL;
        return ((bits & ~FileH) >> 7) |
               ((bits & ~FileA) >> 9) |
               ((bits & ~FileH) << 9) |
               ((bits & ~FileA) << 7);
    }

    private static int BlockerAdvanceSum(ulong blockerBits, PlayerId owner)
    {
        int total = 0;
        while (blockerBits != 0)
        {
            int square = BitOperations.TrailingZeroCount(blockerBits);
            blockerBits &= blockerBits - 1;
            int row = square >> 3;
            total += owner == PlayerId.Player1 ? 7 - row : row;
        }
        return total;
    }

    private static ulong[] BuildAdjacentMasks()
    {
        var masks = new ulong[GameEngine.BoardSize * GameEngine.BoardSize];
        for (int row = 0; row < GameEngine.BoardSize; row++)
        {
            for (int col = 0; col < GameEngine.BoardSize; col++)
            {
                ulong mask = 0;
                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (dr == 0 && dc == 0) continue;
                        int r = row + dr;
                        int c = col + dc;
                        if ((uint)r < GameEngine.BoardSize && (uint)c < GameEngine.BoardSize)
                        {
                            mask |= 1UL << (r * GameEngine.BoardSize + c);
                        }
                    }
                }
                masks[row * GameEngine.BoardSize + col] = mask;
            }
        }
        return masks;
    }

    private static int Centrality(int col) => col switch
    {
        3 or 4 => 3,
        2 or 5 => 2,
        1 or 6 => 1,
        _ => 0
    };

    private static int CompareMoves(Move left, Move right, PlayerId mover)
    {
        int leftKey = CanonicalMoveSortKey(left, mover);
        int rightKey = CanonicalMoveSortKey(right, mover);
        return leftKey.CompareTo(rightKey);
    }

    // Tie-breaking must be invariant under a 180-degree rotation + player swap.
    // Otherwise absolute row ordering favors P1 (who advances toward row 0) and
    // disfavors P2 (who advances toward row 7) whenever two moves have equal scores.
    private static int CanonicalMoveSortKey(Move move, PlayerId mover)
    {
        static int CanonicalCoord(int value, PlayerId player) =>
            player == PlayerId.Player1 ? value : 7 - value;

        int fromRow = CanonicalCoord(move.From.Row, mover);
        int fromCol = CanonicalCoord(move.From.Col, mover);
        int toRow = CanonicalCoord(move.To.Row, mover);
        int toCol = CanonicalCoord(move.To.Col, mover);

        int value = fromRow;
        value |= fromCol << 3;
        value |= toRow << 6;
        value |= toCol << 9;
        value |= (int)move.Kind << 12;
        return value;
    }

    private static int MoveKey(Move move, PlayerId mover)
    {
        int value = move.From.Row * 8 + move.From.Col;
        value |= (move.To.Row * 8 + move.To.Col) << 6;
        value |= (int)move.Kind << 12;
        value |= (int)mover << 14;
        return value;
    }

    private static ushort PackMove(Move move)
    {
        int value = move.From.Row * 8 + move.From.Col;
        value |= (move.To.Row * 8 + move.To.Col) << 6;
        value |= (int)move.Kind << 12;
        return (ushort)(value + 1);
    }

    private static Move UnpackMove(ushort packed)
    {
        int value = packed - 1;
        int from = value & 63;
        int to = (value >> 6) & 63;
        MoveKind kind = (MoveKind)((value >> 12) & 1);
        return new Move(
            new Position(from >> 3, from & 7),
            new Position(to >> 3, to & 7),
            kind);
    }

    private sealed class ParallelRootWorker : IDisposable
    {
        public GameEngine State { get; }
        public SearchContext Context { get; }

        public ParallelRootWorker(GameEngine state, SearchContext context)
        {
            State = state;
            Context = context;
        }

        public void Dispose() => Context.Dispose();
    }

    private sealed class SearchContext : IDisposable
    {
        private readonly Stopwatch _stopwatch;
        private readonly CancellationToken _cancellationToken;
        private readonly SharedNodeCounter? _nodeCounter;
        private long _localNodes;
        private const int MaxKillerPly = 64;
        private const int KillerSlots = MaxKillerPly * 2;
        private const int HistorySize = 1 << 15;
        private readonly ushort[] _killers;
        private readonly int[] _history;
        private readonly StaticEvaluationCache _staticEvaluationCache;
        private readonly OnlySurvivalHintTable _onlySurvivalHints;
        private readonly bool _mateScoutMode;
        private bool _disposed;

        public PlayerId Perspective { get; }
        public bool IsMateDistanceScout => _mateScoutMode;
        public CpuSearchOptions Options { get; }
        public CpuEvaluationProfile EvaluationProfile { get; }
        public TranspositionTable Transposition { get; }
        public long Nodes
        {
            get
            {
                SharedNodeCounter? counter = _nodeCounter;
                return counter is null
                    ? _localNodes
                    : Interlocked.Read(ref counter.Value);
            }
        }
        public long TranspositionHits;
        public long BetaCutoffs;
        public long PvsNullWindowProbes;
        public long PvsResearches;
        public long LmrReducedSearches;
        public long LmrVerificationResearches;
        public long MateDistancePrunes;
        public long OnlySurvivalExtensions;
        public long AdaptiveRootDeepeningPasses;
        public int MaxAdaptiveRootDeepeningPlyReached;
        public long MateDistanceScoutProbes;
        public long MateDistanceScoutNodes;
        public int MateDistanceScoutMaxDepthReached;
        public int MateDistanceScoutMaxCompletedDepth;
        public int MateDistanceScoutDirection;
        public int MateDistanceScoutProofDepth;
        public int MateDistanceScoutProofScore;
        public bool MateDistanceScoutProofExact;
        private readonly List<CpuMateScoutProbeTelemetry> _mateDistanceScoutProbeDetails = new();
        public bool SelectiveExtensionAborted { get; private set; }

        public SearchContext(
            PlayerId perspective,
            CpuSearchOptions options,
            Stopwatch stopwatch,
            CancellationToken cancellationToken,
            SharedNodeCounter? sharedNodeCounter = null,
            bool compactTransposition = false,
            bool mateScoutMode = false)
        {
            Perspective = perspective;
            Options = options;
            EvaluationProfile = options.EvaluationProfile ?? CpuEvaluationProfileProvider.Current;
            _stopwatch = stopwatch;
            _cancellationToken = cancellationToken;
            _mateScoutMode = mateScoutMode;
            _nodeCounter = options.MaxNodes > 0 ? sharedNodeCounter ?? new SharedNodeCounter() : null;
            _killers = ArrayPool<ushort>.Shared.Rent(KillerSlots);
            Array.Clear(_killers, 0, KillerSlots);
            _history = ArrayPool<int>.Shared.Rent(HistorySize);
            Array.Clear(_history, 0, HistorySize);
            _staticEvaluationCache = new StaticEvaluationCache(options.UseStaticEvaluationCache);
            _onlySurvivalHints = new OnlySurvivalHintTable(
                options.UseOnlySurvivalExtension && options.MaxOnlySurvivalExtensionsPerLine > 0);
            Transposition = new TranspositionTable(
                options.UseTranspositionTable
                    ? compactTransposition ? 1 << 15 : 1 << 18
                    : 0);
        }

        public SearchContext CreateParallelChild() =>
            new(
                Perspective,
                Options with { MaxParallelism = 1 },
                _stopwatch,
                _cancellationToken,
                _nodeCounter,
                compactTransposition: true);

        public SearchContext CreateMateScoutChild() =>
            new(
                Perspective,
                Options with
                {
                    MaxParallelism = 1,
                    CollectExactRootScores = false,
                    UseLateMoveReductions = false,
                    UseOnlySurvivalExtension = false,
                    MaxOnlySurvivalExtensionsPerLine = 0,
                    MaxAdaptiveRootDeepeningPly = 0,
                    UseMateDistanceScout = false
                },
                _stopwatch,
                _cancellationToken,
                _nodeCounter,
                compactTransposition: false,
                mateScoutMode: true);

        public void MergeCountersFrom(SearchContext child)
        {
            if (_nodeCounter is null)
            {
                Interlocked.Add(ref _localNodes, child._localNodes);
            }
            Interlocked.Add(ref TranspositionHits, child.TranspositionHits);
            Interlocked.Add(ref BetaCutoffs, child.BetaCutoffs);
            Interlocked.Add(ref PvsNullWindowProbes, child.PvsNullWindowProbes);
            Interlocked.Add(ref PvsResearches, child.PvsResearches);
            Interlocked.Add(ref LmrReducedSearches, child.LmrReducedSearches);
            Interlocked.Add(ref LmrVerificationResearches, child.LmrVerificationResearches);
            Interlocked.Add(ref MateDistancePrunes, child.MateDistancePrunes);
            Interlocked.Add(ref OnlySurvivalExtensions, child.OnlySurvivalExtensions);
            Interlocked.Add(ref AdaptiveRootDeepeningPasses, child.AdaptiveRootDeepeningPasses);
            AtomicMax(ref MaxAdaptiveRootDeepeningPlyReached, child.MaxAdaptiveRootDeepeningPlyReached);
        }

        public void VisitNode()
        {
            SharedNodeCounter? counter = _nodeCounter;
            if (counter is null)
            {
                _localNodes++;
                return;
            }

            while (true)
            {
                long current = Interlocked.Read(ref counter.Value);
                if (current >= Options.MaxNodes)
                {
                    throw new SearchAbortedException();
                }

                if (Interlocked.CompareExchange(ref counter.Value, current + 1, current) == current)
                {
                    return;
                }
            }
        }

        public void ThrowIfCancelled() => _cancellationToken.ThrowIfCancellationRequested();

        public void RecordAdaptiveRootDeepening(int rootExtensionPly)
        {
            Interlocked.Increment(ref AdaptiveRootDeepeningPasses);
            AtomicMax(ref MaxAdaptiveRootDeepeningPlyReached, rootExtensionPly);
        }

        public void RecordMateDistanceScout(
            long probes,
            long nodes,
            int maxDepthStarted,
            int maxDepthCompleted,
            int direction,
            int proofDepth,
            int proofScore,
            bool proofExact,
            IReadOnlyList<CpuMateScoutProbeTelemetry> probeDetails)
        {
            Interlocked.Add(ref MateDistanceScoutProbes, probes);
            Interlocked.Add(ref MateDistanceScoutNodes, nodes);
            AtomicMax(ref MateDistanceScoutMaxDepthReached, maxDepthStarted);
            AtomicMax(ref MateDistanceScoutMaxCompletedDepth, maxDepthCompleted);
            MateDistanceScoutDirection = direction;
            _mateDistanceScoutProbeDetails.AddRange(probeDetails);
            if (proofScore != 0)
            {
                MateDistanceScoutProofDepth = proofDepth;
                MateDistanceScoutProofScore = proofScore;
                MateDistanceScoutProofExact = proofExact;
            }
        }

        public IReadOnlyList<CpuMateScoutProbeTelemetry> GetMateDistanceScoutProbeDetails() =>
            _mateDistanceScoutProbeDetails.Count == 0
                ? Array.Empty<CpuMateScoutProbeTelemetry>()
                : _mateDistanceScoutProbeDetails.ToArray();

        public void MarkSelectiveExtensionAborted() => SelectiveExtensionAborted = true;

        public void CheckAbort(bool force)
        {
            if (!force && (Nodes & 511) != 0)
            {
                return;
            }

            _cancellationToken.ThrowIfCancellationRequested();
            if (force && Options.MaxNodes > 0 && Nodes >= Options.MaxNodes)
            {
                throw new SearchAbortedException();
            }
            if (Options.TimeLimitMilliseconds > 0 && _stopwatch.ElapsedMilliseconds >= Options.TimeLimitMilliseconds)
            {
                throw new SearchAbortedException();
            }
        }

        public ulong GetTranspositionKey(ulong positionKey, int extensionBudget)
        {
            if (!Options.UseOnlySurvivalExtension ||
                Options.MaxOnlySurvivalExtensionsPerLine <= 0)
            {
                return positionKey;
            }

            // Remaining extension budget changes the effective horizon, so it must be
            // part of the TT identity. A score searched with budget=0 must not suppress
            // a later budget=2 selective extension of the same board.
            ulong budget = (ulong)(uint)Math.Clamp(extensionBudget, 0, 15);
            ulong mixed = budget * 0x9E3779B97F4A7C15UL;
            mixed ^= mixed >> 29;
            mixed *= 0xBF58476D1CE4E5B9UL;
            mixed ^= mixed >> 32;
            return positionKey ^ mixed;
        }

        public int EvaluateStatic(GameEngine state)
        {
            if (!Options.UseStaticEvaluationCache)
            {
                return EvaluateDetailed(state, Perspective, EvaluationProfile).Total;
            }

            ulong key = state.GetStaticEvaluationHash();
            if (_staticEvaluationCache.TryGetValue(key, out int cached))
            {
                return cached;
            }

            int score = EvaluateDetailed(state, Perspective, EvaluationProfile).Total;
            _staticEvaluationCache.Store(key, score);
            return score;
        }

        public bool TryGetOnlySurvivalHint(ulong key, int minimumDepth, out Move move) =>
            _onlySurvivalHints.TryGetValue(key, minimumDepth, out move);

        public void RecordOnlySurvivalHint(ulong key, int depth, Move move) =>
            _onlySurvivalHints.Store(key, depth, move);

        public int KillerScore(Move move, int ply)
        {
            if ((uint)ply >= MaxKillerPly)
            {
                return 0;
            }

            ushort packed = PackMove(move);
            int index = ply << 1;
            if (_killers[index] == packed) return 20_000;
            if (_killers[index + 1] == packed) return 10_000;
            return 0;
        }

        public int HistoryScore(Move move, PlayerId mover)
        {
            int value = _history[MoveKey(move, mover)];
            return Math.Min(value, 15_000);
        }

        public void RecordCutoff(Move move, int depth, int ply, PlayerId mover)
        {
            if ((uint)ply < MaxKillerPly)
            {
                ushort packed = PackMove(move);
                int index = ply << 1;
                if (_killers[index] != packed)
                {
                    _killers[index + 1] = _killers[index];
                    _killers[index] = packed;
                }
            }

            int key = MoveKey(move, mover);
            int bonus = depth * depth * 8;
            _history[key] = Math.Min(50_000, _history[key] + bonus);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Transposition.Dispose();
            _staticEvaluationCache.Dispose();
            _onlySurvivalHints.Dispose();
            ArrayPool<ushort>.Shared.Return(_killers, clearArray: false);
            ArrayPool<int>.Shared.Return(_history, clearArray: false);
        }
    }

    private sealed class OnlySurvivalHintTable : IDisposable
    {
        // Sparse, direct-mapped hints learned from completed full-window searches.
        // Unlike the TT, these entries never provide a score or cutoff: they only
        // identify the one move worth extending on a later iterative-deepening pass.
        private const int Capacity = 1 << 14;
        private const int Mask = Capacity - 1;
        private static int _nextStamp;

        private readonly OnlySurvivalHintSlot[] _slots;
        private readonly int _stamp;
        private readonly bool _pooled;

        public OnlySurvivalHintTable(bool enabled)
        {
            if (!enabled)
            {
                _slots = Array.Empty<OnlySurvivalHintSlot>();
                _stamp = 0;
                _pooled = false;
                return;
            }

            _slots = ArrayPool<OnlySurvivalHintSlot>.Shared.Rent(Capacity);
            int stamp = Interlocked.Increment(ref _nextStamp);
            if (stamp == 0) stamp = Interlocked.Increment(ref _nextStamp);
            _stamp = stamp;
            _pooled = true;
        }

        public bool TryGetValue(ulong key, int minimumDepth, out Move move)
        {
            if (!_pooled)
            {
                move = default;
                return false;
            }

            int index = (int)(key ^ (key >> 32)) & Mask;
            ref OnlySurvivalHintSlot slot = ref _slots[index];
            if (slot.Stamp == _stamp &&
                slot.Key == key &&
                slot.PackedMove != 0 &&
                slot.Depth >= minimumDepth)
            {
                move = UnpackMove(slot.PackedMove);
                return true;
            }

            move = default;
            return false;
        }

        public void Store(ulong key, int depth, Move move)
        {
            if (!_pooled) return;

            int index = (int)(key ^ (key >> 32)) & Mask;
            ref OnlySurvivalHintSlot slot = ref _slots[index];

            // Preserve a deeper hint for the same position during this iterative search.
            if (slot.Stamp == _stamp && slot.Key == key && slot.Depth > depth)
            {
                return;
            }

            slot.Key = key;
            slot.PackedMove = PackMove(move);
            slot.Depth = (byte)Math.Clamp(depth, 0, byte.MaxValue);
            slot.Stamp = _stamp;
        }

        public void Dispose()
        {
            if (_pooled)
            {
                ArrayPool<OnlySurvivalHintSlot>.Shared.Return(_slots, clearArray: false);
            }
        }

        private struct OnlySurvivalHintSlot
        {
            public ulong Key;
            public ushort PackedMove;
            public byte Depth;
            public int Stamp;
        }
    }

    private sealed class StaticEvaluationCache : IDisposable
    {
        // Direct-mapped and deliberately small: a miss only costs a normal evaluation,
        // while a hit avoids all route/defense interaction work at a transposed leaf.
        // A per-context stamp avoids clearing pooled memory on every CPU move.
        private const int Capacity = 1 << 14;
        private const int Mask = Capacity - 1;
        private static int _nextStamp;

        private readonly StaticEvaluationSlot[] _slots;
        private readonly int _stamp;
        private readonly bool _pooled;

        public StaticEvaluationCache(bool enabled)
        {
            if (!enabled)
            {
                _slots = Array.Empty<StaticEvaluationSlot>();
                _stamp = 0;
                _pooled = false;
                return;
            }

            _slots = ArrayPool<StaticEvaluationSlot>.Shared.Rent(Capacity);
            int stamp = Interlocked.Increment(ref _nextStamp);
            if (stamp == 0) stamp = Interlocked.Increment(ref _nextStamp);
            _stamp = stamp;
            _pooled = true;
        }

        public bool TryGetValue(ulong key, out int score)
        {
            if (!_pooled)
            {
                score = 0;
                return false;
            }

            int index = (int)(key ^ (key >> 32)) & Mask;
            ref StaticEvaluationSlot slot = ref _slots[index];
            if (slot.Stamp == _stamp && slot.Key == key)
            {
                score = slot.Score;
                return true;
            }

            score = 0;
            return false;
        }

        public void Store(ulong key, int score)
        {
            if (!_pooled) return;
            int index = (int)(key ^ (key >> 32)) & Mask;
            ref StaticEvaluationSlot slot = ref _slots[index];
            slot.Key = key;
            slot.Score = score;
            slot.Stamp = _stamp;
        }

        public void Dispose()
        {
            if (_pooled)
            {
                ArrayPool<StaticEvaluationSlot>.Shared.Return(_slots, clearArray: false);
            }
        }

        private struct StaticEvaluationSlot
        {
            public ulong Key;
            public int Score;
            public int Stamp;
        }
    }

    private sealed class TranspositionTable : IDisposable
    {
        // Two-way set associative, fixed-size and compact. This replaces the former
        // Dictionary<ulong, TranspositionEntry> hot path and caps memory deterministically.
        private readonly TTSlot[] _slots;
        private readonly int _capacityEntries;
        private readonly int _bucketMask;
        private readonly bool _pooled;

        public TranspositionTable(int capacityEntries)
        {
            if (capacityEntries <= 0)
            {
                _slots = Array.Empty<TTSlot>();
                _capacityEntries = 0;
                _bucketMask = 0;
                _pooled = false;
                return;
            }

            if ((capacityEntries & (capacityEntries - 1)) != 0 || capacityEntries < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(capacityEntries), "TT capacity must be a power of two >= 2.");
            }

            _slots = ArrayPool<TTSlot>.Shared.Rent(capacityEntries);
            _capacityEntries = capacityEntries;
            _bucketMask = (capacityEntries >> 1) - 1;
            _pooled = true;
            Array.Clear(_slots, 0, capacityEntries);
        }

        public bool TryGetValue(ulong key, out TranspositionEntry entry)
        {
            if (_capacityEntries == 0)
            {
                entry = default;
                return false;
            }

            int first = ((int)(key ^ (key >> 32)) & _bucketMask) << 1;
            ref TTSlot a = ref _slots[first];
            if (a.PackedMove != 0 && a.Key == key)
            {
                entry = a.ToEntry();
                return true;
            }

            ref TTSlot b = ref _slots[first + 1];
            if (b.PackedMove != 0 && b.Key == key)
            {
                entry = b.ToEntry();
                return true;
            }

            entry = default;
            return false;
        }

        public void Store(ulong key, int depth, int score, BoundType bound, Move bestMove)
        {
            if (_capacityEntries == 0) return;

            int first = ((int)(key ^ (key >> 32)) & _bucketMask) << 1;
            ref TTSlot a = ref _slots[first];
            ref TTSlot b = ref _slots[first + 1];

            if (a.PackedMove != 0 && a.Key == key)
            {
                if (a.Depth <= depth) a.Set(key, depth, score, bound, bestMove);
                return;
            }
            if (b.PackedMove != 0 && b.Key == key)
            {
                if (b.Depth <= depth) b.Set(key, depth, score, bound, bestMove);
                return;
            }

            if (a.PackedMove == 0)
            {
                a.Set(key, depth, score, bound, bestMove);
                return;
            }
            if (b.PackedMove == 0)
            {
                b.Set(key, depth, score, bound, bestMove);
                return;
            }

            // Prefer keeping deeper colliding entries from the same iterative-deepening
            // search. Equal depth may be replaced so new parts of the tree are admitted.
            if (a.Depth <= b.Depth)
            {
                if (a.Depth <= depth) a.Set(key, depth, score, bound, bestMove);
            }
            else if (b.Depth <= depth)
            {
                b.Set(key, depth, score, bound, bestMove);
            }
        }

        public void Dispose()
        {
            if (_pooled)
            {
                ArrayPool<TTSlot>.Shared.Return(_slots, clearArray: false);
            }
        }

        private struct TTSlot
        {
            public ulong Key;
            public int Score;
            public ushort PackedMove;
            public byte Depth;
            public byte Bound;

            public void Set(ulong key, int depth, int score, BoundType bound, Move bestMove)
            {
                Key = key;
                Score = score;
                PackedMove = PackMove(bestMove);
                Depth = (byte)Math.Clamp(depth, 0, byte.MaxValue);
                Bound = (byte)bound;
            }

            public readonly TranspositionEntry ToEntry() =>
                new(Depth, Score, (BoundType)Bound, UnpackMove(PackedMove));
        }
    }

    private sealed class SharedNodeCounter
    {
        public long Value;
    }

    private sealed class SearchAbortedException : Exception
    {
    }

    private enum BoundType
    {
        Exact,
        Lower,
        Upper
    }

    private readonly record struct TranspositionEntry(int Depth, int Score, BoundType Bound, Move BestMove);
    private readonly record struct MovePreferenceMetrics(
        int PhysicalHistoryCount,
        bool RunnerReturnMove,
        int RunnerForwardDelta,
        bool IsRunnerMove);

    private readonly record struct RunnerRouteAnalysis(
        int GoalPathValue,
        int GoalDistance,
        ulong ShortestGoalMask,
        int BridgeheadConnectionValue);

    private readonly record struct PreferredCandidate(
        Move Move,
        int Score,
        int PhysicalHistoryCount,
        bool RunnerReturnMove,
        int RunnerForwardDelta);

    private readonly record struct RootPreferenceSelection(
        Move Move,
        int Score,
        IReadOnlyList<CpuCandidate> Candidates,
        bool CycleAvoidanceApplied,
        bool RunnerOscillationAvoidanceApplied,
        bool RunnerAdvancePreferenceApplied,
        int ScoreConcession,
        int StrictBestScore,
        int SelectedPhysicalHistoryCount,
        int StrictBestPhysicalHistoryCount,
        bool SelectedRunnerReturnMove,
        bool StrictBestRunnerReturnMove,
        int SelectedRunnerForwardDelta,
        int StrictBestRunnerForwardDelta)
    {
        public static RootPreferenceSelection Unchanged(Move move, int score) =>
            new(
                move,
                score,
                Array.Empty<CpuCandidate>(),
                false,
                false,
                false,
                0,
                score,
                0,
                0,
                false,
                false,
                0,
                0);
    }

    private readonly record struct MateScoutProbeResult(bool Proven, Move WitnessMove);

    private readonly record struct MateDistanceScoutResult(Move Move, int ProofScore, int ProofDepth, bool Exact);

    private readonly record struct RootSearchResult(Move BestMove, int BestScore, IReadOnlyList<CpuCandidate> Candidates);
}
