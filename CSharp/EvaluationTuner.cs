using System.Text;
using System.Text.Json;

namespace StarRunnerPrototype;

public sealed record EvaluationTuningOptions(
    int Generations,
    int CandidatesPerGeneration,
    int ShallowDepth,
    int ShallowGamesPerCandidate,
    int ValidationDepth,
    int ValidationGames,
    int Parallelism,
    int MaxPlies,
    int OpeningRandomPlies,
    int OpeningTopK,
    int OpeningScoreWindow,
    int InitialMutationStep,
    int Seed)
{
    public EvaluationTuningOptions Normalize() => this with
    {
        Generations = Math.Clamp(Generations, 0, 100_000),
        CandidatesPerGeneration = Math.Clamp(CandidatesPerGeneration, 2, 32),
        ShallowDepth = Math.Clamp(ShallowDepth, 2, 8),
        // Successive-halving shallow rounds are always executed as one candidate-P1
        // game plus one candidate-P2 game. Normalize the configured maximum to an
        // even number so every surviving candidate keeps exact colour balance.
        ShallowGamesPerCandidate = (int)Math.Clamp(
            (((long)ShallowGamesPerCandidate + 1) / 2) * 2,
            4L,
            1000L),
        ValidationDepth = Math.Clamp(ValidationDepth, ShallowDepth, 10),
        ValidationGames = ValidationGames <= 0 ? 0 : Math.Clamp(ValidationGames, 4, 500),
        Parallelism = Math.Clamp(Parallelism, 1, Math.Max(1, Environment.ProcessorCount * 2)),
        MaxPlies = Math.Clamp(MaxPlies, 40, 1000),
        OpeningRandomPlies = Math.Clamp(OpeningRandomPlies, 0, 20),
        OpeningTopK = Math.Clamp(OpeningTopK, 1, 8),
        OpeningScoreWindow = Math.Clamp(OpeningScoreWindow, 0, 10_000),
        InitialMutationStep = Math.Clamp(InitialMutationStep, 20, 600)
    };
}

public sealed record EvaluationTuningProgress(
    int Generation,
    int TotalGenerations,
    int Candidate,
    int CandidatesPerGeneration,
    string Stage,
    double? CandidateScore,
    double BestScore,
    CpuEvaluationProfile CurrentBest,
    string Message,
    long TotalGamesCompleted,
    int BestUpdateCount,
    int LastBestGeneration,
    int MutationStep,
    int Stagnation,
    long ElapsedMilliseconds);

public sealed record EvaluationMatchScore(
    int Games,
    int Wins,
    int Losses,
    int DrawsOrLimits,
    double Score,
    long ElapsedMilliseconds,
    int PlannedGames,
    bool StoppedEarly,
    string? StopReason);

public sealed record EvaluationShallowCandidateRoundResult(
    int CandidateIndex,
    string CandidateName,
    EvaluationMatchScore RoundScore,
    EvaluationMatchScore CumulativeScore,
    bool Advanced);

public sealed record EvaluationShallowRoundResult(
    int Round,
    int TargetGamesPerCandidate,
    int ActiveCandidates,
    int AdvancingCandidates,
    IReadOnlyList<EvaluationShallowCandidateRoundResult> Candidates);

public sealed record EvaluationTuningGenerationResult(
    int Generation,
    CpuEvaluationProfile IncumbentBefore,
    CpuEvaluationProfile BestCandidate,
    EvaluationMatchScore ShallowScore,
    IReadOnlyList<EvaluationShallowRoundResult> ShallowRounds,
    EvaluationMatchScore? ValidationScore,
    EvaluationMatchScore? ConfirmationScore,
    double CombinedScore,
    bool Accepted,
    int MutationStep,
    IReadOnlyList<string> ChangedParameters,
    bool LargeMutation);

public sealed record EvaluationTuningResult(
    CpuEvaluationProfile StartingProfile,
    CpuEvaluationProfile BestProfile,
    IReadOnlyList<EvaluationTuningGenerationResult> Generations,
    bool Cancelled,
    long ElapsedMilliseconds,
    string ReportPath,
    long TotalGamesCompleted,
    int BestUpdateCount,
    int LastBestGeneration,
    int Stagnation);

public sealed record EvaluationParameterScanOptions(
    int FeatureIndex,
    bool Endgame,
    int MinValue,
    int MaxValue,
    int Step,
    int Depth,
    int GamesPerValue,
    int Parallelism,
    int MaxPlies,
    int OpeningRandomPlies,
    int OpeningTopK,
    int OpeningScoreWindow,
    int Seed)
{
    public EvaluationParameterScanOptions Normalize(int featureCount)
    {
        int min = Math.Clamp(Math.Min(MinValue, MaxValue), 0, 3000);
        int max = Math.Clamp(Math.Max(MinValue, MaxValue), 0, 3000);
        int step = Math.Clamp(Step, 10, 3000);
        int games = (int)Math.Clamp((((long)GamesPerValue + 1) / 2) * 2, 4L, 2000L);
        return this with
        {
            FeatureIndex = Math.Clamp(FeatureIndex, 0, Math.Max(0, featureCount - 1)),
            MinValue = min,
            MaxValue = max,
            Step = step,
            Depth = Math.Clamp(Depth, 2, 10),
            GamesPerValue = games,
            Parallelism = Math.Clamp(Parallelism, 1, Math.Max(1, Environment.ProcessorCount * 2)),
            MaxPlies = Math.Clamp(MaxPlies, 40, 1000),
            OpeningRandomPlies = Math.Clamp(OpeningRandomPlies, 0, 20),
            OpeningTopK = Math.Clamp(OpeningTopK, 1, 8),
            OpeningScoreWindow = Math.Clamp(OpeningScoreWindow, 0, 10_000)
        };
    }
}

public sealed record EvaluationParameterScanProgress(
    int CompletedValues,
    int TotalValues,
    int CurrentValue,
    double? Score,
    long TotalGamesCompleted,
    long ElapsedMilliseconds,
    string Message);

public sealed record EvaluationParameterScanEntry(
    int Value,
    CpuEvaluationProfile Profile,
    EvaluationMatchScore Score);

public sealed record EvaluationParameterScanResult(
    CpuEvaluationProfile BaselineProfile,
    string FeatureName,
    bool Endgame,
    int BaselineValue,
    EvaluationParameterScanOptions Options,
    IReadOnlyList<EvaluationParameterScanEntry> Entries,
    EvaluationParameterScanEntry BestEntry,
    bool Cancelled,
    long ElapsedMilliseconds,
    string ReportPath,
    long TotalGamesCompleted);

public static class EvaluationTuner
{
    private static readonly string[] FeatureNames =
    {
        "RunnerProgress",
        "RunnerMobility",
        "BlockerMaterial",
        "FriendlyRunnerSupport",
        "FrontPressure",
        "GoalDefense",
        "ImmediateGoalThreats",
        "BlockerAdvancement",
        "BridgeheadConnection",
        "RunnerGoalPath",
        "PreparedGoalThreat",
        "UnansweredGoalThreat",
        "ConnectedGoalThreat",
        "ViableRunnerProgress",
        "SacrificeDebt"
    };

    public static IReadOnlyList<string> TunableFeatureNames { get; } = Array.AsReadOnly(FeatureNames);

    public static EvaluationParameterScanResult RunParameterScan(
        CpuEvaluationProfile baselineProfile,
        EvaluationParameterScanOptions options,
        IProgress<EvaluationParameterScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        baselineProfile = baselineProfile.Normalize();
        options = options.Normalize(FeatureNames.Length);
        string featureName = FeatureNames[options.FeatureIndex];
        int baselineValue = GetParameterValue(baselineProfile, options.FeatureIndex, options.Endgame);
        List<int> values = BuildScanValues(options.MinValue, options.MaxValue, options.Step);
        var entries = new List<EvaluationParameterScanEntry>(values.Count);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        long totalGames = 0;
        bool cancelled = false;

        // Every value uses the exact same balanced seed set. This common-random-number
        // design makes adjacent values much easier to compare than giving each value
        // unrelated openings. No early rejection is used because the sensitivity curve
        // is useful only when every scanned point has the same planned sample size.
        EvaluationTuningOptions matchOptions = new EvaluationTuningOptions(
            Generations: 1,
            CandidatesPerGeneration: 2,
            ShallowDepth: options.Depth,
            ShallowGamesPerCandidate: options.GamesPerValue,
            ValidationDepth: options.Depth,
            ValidationGames: 0,
            Parallelism: options.Parallelism,
            MaxPlies: options.MaxPlies,
            OpeningRandomPlies: options.OpeningRandomPlies,
            OpeningTopK: options.OpeningTopK,
            OpeningScoreWindow: options.OpeningScoreWindow,
            InitialMutationStep: 100,
            Seed: options.Seed).Normalize();

        try
        {
            for (int i = 0; i < values.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int value = values[i];
                CpuEvaluationProfile candidate = SetParameterValue(
                    baselineProfile,
                    options.FeatureIndex,
                    options.Endgame,
                    value,
                    $"Scan-{(options.Endgame ? "E" : "O")}.{featureName}-{value}");

                progress?.Report(new EvaluationParameterScanProgress(
                    i, values.Count, value, null, totalGames, stopwatch.ElapsedMilliseconds,
                    $"{(options.Endgame ? "Endgame" : "Opening")} {featureName}={value}‰ を D{options.Depth} / {options.GamesPerValue}局で比較中"));

                EvaluationMatchScore score = RunBalancedMatchToCompletion(
                    candidate, baselineProfile, options.Depth, options.GamesPerValue,
                    matchOptions, options.Seed, cancellationToken);
                totalGames += score.Games;
                entries.Add(new EvaluationParameterScanEntry(value, candidate, score));

                progress?.Report(new EvaluationParameterScanProgress(
                    i + 1, values.Count, value, score.Score, totalGames, stopwatch.ElapsedMilliseconds,
                    $"{value}‰: score={score.Score:P1} ({score.Wins}-{score.Losses}-{score.DrawsOrLimits})"));
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        stopwatch.Stop();
        if (entries.Count == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("1パラメータ・スキャンの対局結果がありません。");
        }

        EvaluationParameterScanEntry best = entries
            .OrderByDescending(e => e.Score.Score)
            .ThenBy(e => Math.Abs(e.Value - baselineValue))
            .ThenBy(e => e.Value)
            .First();
        string reportPath = WriteParameterScanReport(
            baselineProfile, featureName, options.Endgame, baselineValue, options, entries, best,
            cancelled, stopwatch.ElapsedMilliseconds, totalGames);

        return new EvaluationParameterScanResult(
            baselineProfile, featureName, options.Endgame, baselineValue, options, entries, best,
            cancelled, stopwatch.ElapsedMilliseconds, reportPath, totalGames);
    }

    public static EvaluationTuningResult Run(
        CpuEvaluationProfile startingProfile,
        EvaluationTuningOptions options,
        IProgress<EvaluationTuningProgress>? progress,
        CancellationToken cancellationToken)
    {
        options = options.Normalize();
        startingProfile = startingProfile.Normalize();
        CpuEvaluationProfile incumbent = startingProfile;
        var generationResults = new List<EvaluationTuningGenerationResult>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        bool cancelled = false;
        double lastBestScore = 0.5;
        int acceptedCount = 0;
        int lastAcceptedGeneration = 0;
        int stagnation = 0;
        long totalGamesCompleted = 0;
        int generation = 1;

        try
        {
            while (options.Generations == 0 || generation <= options.Generations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int mutationStep = ComputeMutationStep(options.InitialMutationStep, stagnation);
                CpuEvaluationProfile incumbentBefore = incumbent;
                ShallowTournamentResult shallowTournament = RunShallowSuccessiveHalving(
                    incumbentBefore,
                    generation,
                    mutationStep,
                    options,
                    progress,
                    lastBestScore,
                    incumbent,
                    totalGamesCompleted,
                    acceptedCount,
                    lastAcceptedGeneration,
                    stagnation,
                    stopwatch,
                    cancellationToken);
                totalGamesCompleted += shallowTournament.GamesCompleted;
                (MutationOutcome Mutation, EvaluationMatchScore Score) bestShallow =
                    (shallowTournament.Winner.Mutation, shallowTournament.Winner.CumulativeScore);

                EvaluationMatchScore? validation = null;
                EvaluationMatchScore? confirmation = null;
                double combined = bestShallow.Score.Score;

                if (options.ValidationGames > 0)
                {
                    progress?.Report(new EvaluationTuningProgress(
                        generation,
                        options.Generations,
                        options.CandidatesPerGeneration,
                        options.CandidatesPerGeneration,
                        $"D{options.ValidationDepth} 検証",
                        bestShallow.Score.Score,
                        lastBestScore,
                        incumbent,
                        "世代最良候補を深い対局で検証中",
                        totalGamesCompleted, acceptedCount, lastAcceptedGeneration, mutationStep, stagnation, stopwatch.ElapsedMilliseconds));

                    int validationSeed = unchecked(options.Seed + generation * 15_485_863 + 7_919);
                    validation = RunBalancedMatch(
                        bestShallow.Mutation.Profile,
                        incumbentBefore,
                        options.ValidationDepth,
                        options.ValidationGames,
                        options,
                        validationSeed,
                        cancellationToken,
                        maxPossibleValidationScore => GetValidationEarlyRejectReason(
                            bestShallow.Score.Score,
                            maxPossibleValidationScore));
                    totalGamesCompleted += validation.Games;
                    combined = bestShallow.Score.Score * 0.65 + validation.Score * 0.35;

                    if (validation.StoppedEarly)
                    {
                        progress?.Report(new EvaluationTuningProgress(
                            generation,
                            options.Generations,
                            options.CandidatesPerGeneration,
                            options.CandidatesPerGeneration,
                            $"D{options.ValidationDepth} 検証 早期棄却",
                            validation.Score,
                            lastBestScore,
                            incumbent,
                            $"{validation.Games}/{validation.PlannedGames}局で打切り: {validation.StopReason}",
                            totalGamesCompleted, acceptedCount, lastAcceptedGeneration, mutationStep, stagnation, stopwatch.ElapsedMilliseconds));
                    }

                    // Promising candidates get a second, independent deeper match before promotion.
                    // This greatly reduces the chance that long unattended runs drift because of one lucky batch.
                    bool preliminaryPass = !validation.StoppedEarly &&
                                           combined >= 0.515 &&
                                           validation.Score >= 0.49;
                    if (preliminaryPass)
                    {
                        int confirmationGames = Math.Min(
                            500,
                            Math.Max(8, options.ValidationGames) * Math.Min(4, 1 + acceptedCount / 3));
                        progress?.Report(new EvaluationTuningProgress(
                            generation,
                            options.Generations,
                            options.CandidatesPerGeneration,
                            options.CandidatesPerGeneration,
                            $"D{options.ValidationDepth} 昇格決定戦",
                            combined,
                            lastBestScore,
                            incumbent,
                            $"別seedで {confirmationGames} 局の再確認中",
                            totalGamesCompleted, acceptedCount, lastAcceptedGeneration, mutationStep, stagnation, stopwatch.ElapsedMilliseconds));

                        int confirmationSeed = unchecked(options.Seed + generation * 32_452_843 + 104_729);
                        confirmation = RunBalancedMatch(
                            bestShallow.Mutation.Profile,
                            incumbentBefore,
                            options.ValidationDepth,
                            confirmationGames,
                            options,
                            confirmationSeed,
                            cancellationToken,
                            maxPossibleConfirmationScore => GetConfirmationEarlyRejectReason(
                                bestShallow.Score.Score,
                                validation.Score,
                                maxPossibleConfirmationScore));
                        totalGamesCompleted += confirmation.Games;
                        combined = bestShallow.Score.Score * 0.50 +
                                   validation.Score * 0.25 +
                                   confirmation.Score * 0.25;

                        if (confirmation.StoppedEarly)
                        {
                            progress?.Report(new EvaluationTuningProgress(
                                generation,
                                options.Generations,
                                options.CandidatesPerGeneration,
                                options.CandidatesPerGeneration,
                                $"D{options.ValidationDepth} 昇格決定戦 早期棄却",
                                confirmation.Score,
                                lastBestScore,
                                incumbent,
                                $"{confirmation.Games}/{confirmation.PlannedGames}局で打切り: {confirmation.StopReason}",
                                totalGamesCompleted, acceptedCount, lastAcceptedGeneration, mutationStep, stagnation, stopwatch.ElapsedMilliseconds));
                        }
                    }
                }

                bool accepted;
                if (options.ValidationGames <= 0)
                {
                    accepted = combined >= 0.53;
                }
                else
                {
                    accepted = confirmation is not null &&
                               !confirmation.StoppedEarly &&
                               combined >= 0.52 &&
                               validation!.Score >= 0.49 &&
                               confirmation.Score >= 0.50;
                }

                if (accepted)
                {
                    acceptedCount++;
                    lastAcceptedGeneration = generation;
                    stagnation = 0;
                    incumbent = bestShallow.Mutation.Profile with { Name = $"Tuned-G{generation:0000}" };
                    lastBestScore = combined;
                    WriteCheckpoint(startingProfile, incumbent, generation, acceptedCount, options, stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    stagnation++;
                    lastBestScore = 0.5;
                }

                generationResults.Add(new EvaluationTuningGenerationResult(
                    generation,
                    incumbentBefore,
                    bestShallow.Mutation.Profile,
                    bestShallow.Score,
                    shallowTournament.Rounds,
                    validation,
                    confirmation,
                    combined,
                    accepted,
                    mutationStep,
                    bestShallow.Mutation.ChangedParameters,
                    bestShallow.Mutation.LargeMutation));

                progress?.Report(new EvaluationTuningProgress(
                    generation,
                    options.Generations,
                    options.CandidatesPerGeneration,
                    options.CandidatesPerGeneration,
                    accepted ? "ベスト更新" : "据え置き",
                    combined,
                    lastBestScore,
                    incumbent,
                    accepted
                        ? $"世代 {generation}: チャンピオン更新 #{acceptedCount} ({combined:P1}) / checkpoint保存"
                        : $"世代 {generation}: 改善未確認。現チャンピオン維持 ({combined:P1}) / 停滞 {stagnation}",
                    totalGamesCompleted, acceptedCount, lastAcceptedGeneration, mutationStep, stagnation, stopwatch.ElapsedMilliseconds));

                generation++;
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        stopwatch.Stop();
        string reportPath = WriteReport(
            startingProfile, incumbent, generationResults, options, cancelled, stopwatch.ElapsedMilliseconds,
            totalGamesCompleted, acceptedCount, lastAcceptedGeneration, stagnation);
        return new EvaluationTuningResult(
            startingProfile,
            incumbent,
            generationResults,
            cancelled,
            stopwatch.ElapsedMilliseconds,
            reportPath,
            totalGamesCompleted,
            acceptedCount,
            lastAcceptedGeneration,
            stagnation);
    }

    // Endless tuning should not only shrink the search radius forever.  It cycles from
    // coarse to fine while the champion is unchanged; after an improvement the cycle
    // restarts around the new champion.  This keeps local refinement and occasional
    // larger escapes alive even during very long unattended runs.
    private static int ComputeMutationStep(int initialStep, int stagnation)
    {
        int[] permille = { 1000, 750, 550, 400, 300, 220, 160, 120, 90, 70, 55, 40 };
        int factor = permille[Math.Abs(stagnation) % permille.Length];
        return Math.Clamp((initialStep * factor + 500) / 1000, 20, 600);
    }

    private static int ComputeCandidateStep(int baseStep, int candidateIndex)
    {
        int[] factors = { 1000, 700, 1300, 500, 1600, 850 };
        int factor = factors[(candidateIndex - 1) % factors.Length];
        return Math.Clamp((baseStep * factor + 500) / 1000, 20, 600);
    }

    private sealed class ShallowCandidateState
    {
        public ShallowCandidateState(
            int candidateIndex,
            int candidateStep,
            int matchSeed,
            MutationOutcome mutation,
            int plannedGames)
        {
            CandidateIndex = candidateIndex;
            CandidateStep = candidateStep;
            MatchSeed = matchSeed;
            Mutation = mutation;
            CumulativeScore = EmptyMatchScore(plannedGames);
            LastRoundScore = EmptyMatchScore(0);
        }

        public int CandidateIndex { get; }
        public int CandidateStep { get; }
        public int MatchSeed { get; }
        public MutationOutcome Mutation { get; }
        public EvaluationMatchScore CumulativeScore { get; private set; }
        public EvaluationMatchScore LastRoundScore { get; private set; }

        public void AddRound(EvaluationMatchScore roundScore, int plannedGames)
        {
            LastRoundScore = roundScore;
            CumulativeScore = CombineMatchScores(CumulativeScore, roundScore, plannedGames);
        }
    }

    private sealed record ShallowTournamentResult(
        ShallowCandidateState Winner,
        IReadOnlyList<EvaluationShallowRoundResult> Rounds,
        long GamesCompleted);

    private static ShallowTournamentResult RunShallowSuccessiveHalving(
        CpuEvaluationProfile baseline,
        int generation,
        int mutationStep,
        EvaluationTuningOptions options,
        IProgress<EvaluationTuningProgress>? progress,
        double lastBestScore,
        CpuEvaluationProfile incumbent,
        long totalGamesBeforeTournament,
        int acceptedCount,
        int lastAcceptedGeneration,
        int stagnation,
        System.Diagnostics.Stopwatch sessionStopwatch,
        CancellationToken cancellationToken)
    {
        var allCandidates = new List<ShallowCandidateState>(options.CandidatesPerGeneration);
        for (int candidateIndex = 1; candidateIndex <= options.CandidatesPerGeneration; candidateIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int candidateSeed = unchecked(options.Seed + generation * 1_000_003 + candidateIndex * 65_537);
            int candidateStep = ComputeCandidateStep(mutationStep, candidateIndex);
            MutationOutcome mutation = Mutate(
                baseline,
                candidateStep,
                candidateSeed,
                $"Tune-G{generation:0000}-C{candidateIndex:00}",
                candidateIndex);
            allCandidates.Add(new ShallowCandidateState(
                candidateIndex,
                candidateStep,
                candidateSeed,
                mutation,
                options.ShallowGamesPerCandidate));
        }

        int[] roundTargets = BuildShallowRoundTargets(
            options.CandidatesPerGeneration,
            options.ShallowGamesPerCandidate);
        var roundResults = new List<EvaluationShallowRoundResult>(roundTargets.Length);
        List<ShallowCandidateState> active = allCandidates;
        long tournamentGames = 0;

        for (int roundIndex = 0; roundIndex < roundTargets.Length; roundIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int roundNumber = roundIndex + 1;
            int targetGames = roundTargets[roundIndex];
            int activeBefore = active.Count;

            foreach (ShallowCandidateState state in active.OrderBy(item => item.CandidateIndex))
            {
                cancellationToken.ThrowIfCancellationRequested();
                int additionalGames = targetGames - state.CumulativeScore.Games;
                if (additionalGames <= 0) continue;

                string mutationKind = state.Mutation.LargeMutation ? "大変異" : "少数変異";
                progress?.Report(new EvaluationTuningProgress(
                    generation,
                    options.Generations,
                    state.CandidateIndex,
                    options.CandidatesPerGeneration,
                    $"D{options.ShallowDepth} 選抜 R{roundNumber}/{roundTargets.Length}",
                    state.CumulativeScore.Games > 0 ? state.CumulativeScore.Score : null,
                    lastBestScore,
                    incumbent,
                    $"候補 {state.CandidateIndex}/{options.CandidatesPerGeneration} / {mutationKind} {state.Mutation.ChangedParameters.Count}項目 / 累計 {state.CumulativeScore.Games}->{targetGames}局",
                    totalGamesBeforeTournament + tournamentGames,
                    acceptedCount,
                    lastAcceptedGeneration,
                    state.CandidateStep,
                    stagnation,
                    sessionStopwatch.ElapsedMilliseconds));

                EvaluationMatchScore roundScore = RunBalancedPairSegment(
                    state.Mutation.Profile,
                    baseline,
                    options.ShallowDepth,
                    state.CumulativeScore.Games / 2,
                    additionalGames / 2,
                    options,
                    state.MatchSeed,
                    cancellationToken);
                state.AddRound(roundScore, targetGames);
                tournamentGames += roundScore.Games;

                progress?.Report(new EvaluationTuningProgress(
                    generation,
                    options.Generations,
                    state.CandidateIndex,
                    options.CandidatesPerGeneration,
                    $"D{options.ShallowDepth} 選抜 R{roundNumber}/{roundTargets.Length} 完了",
                    state.CumulativeScore.Score,
                    Math.Max(lastBestScore, state.CumulativeScore.Score),
                    incumbent,
                    $"累計 W{state.CumulativeScore.Wins} L{state.CumulativeScore.Losses} D{state.CumulativeScore.DrawsOrLimits} / {state.CumulativeScore.Games}局 / score {state.CumulativeScore.Score:P1}",
                    totalGamesBeforeTournament + tournamentGames,
                    acceptedCount,
                    lastAcceptedGeneration,
                    state.CandidateStep,
                    stagnation,
                    sessionStopwatch.ElapsedMilliseconds));
            }

            List<ShallowCandidateState> ranked = RankShallowCandidates(active).ToList();
            int advancing = roundIndex == roundTargets.Length - 1
                ? 1
                : Math.Max(1, (ranked.Count + 1) / 2);
            HashSet<int> advancingIds = ranked
                .Take(advancing)
                .Select(item => item.CandidateIndex)
                .ToHashSet();

            roundResults.Add(new EvaluationShallowRoundResult(
                roundNumber,
                targetGames,
                activeBefore,
                advancing,
                ranked.Select(item => new EvaluationShallowCandidateRoundResult(
                    item.CandidateIndex,
                    item.Mutation.Profile.Name,
                    item.LastRoundScore,
                    item.CumulativeScore,
                    advancingIds.Contains(item.CandidateIndex))).ToArray()));

            active = ranked.Take(advancing).ToList();
        }

        ShallowCandidateState winner = active[0];
        if (winner.CumulativeScore.Games != options.ShallowGamesPerCandidate)
        {
            throw new InvalidOperationException(
                $"Successive halving winner has {winner.CumulativeScore.Games} shallow games; expected {options.ShallowGamesPerCandidate}.");
        }

        return new ShallowTournamentResult(winner, roundResults, tournamentGames);
    }

    private static IEnumerable<ShallowCandidateState> RankShallowCandidates(
        IEnumerable<ShallowCandidateState> candidates) =>
        candidates
            .OrderByDescending(item => item.CumulativeScore.Score)
            .ThenByDescending(item => item.LastRoundScore.Score)
            .ThenByDescending(item => DecisiveWinRate(item.CumulativeScore))
            .ThenBy(item => item.CandidateIndex);

    private static double DecisiveWinRate(EvaluationMatchScore score)
    {
        int decisive = score.Wins + score.Losses;
        return decisive > 0 ? score.Wins / (double)decisive : 0.5;
    }

    private static int[] BuildShallowRoundTargets(int candidateCount, int totalGames)
    {
        int requiredRounds = 0;
        for (int capacity = 1; capacity < candidateCount; capacity *= 2)
        {
            requiredRounds++;
        }

        int maxPairRounds = Math.Max(1, totalGames / 2);
        int rounds = Math.Max(1, Math.Min(requiredRounds, maxPairRounds));
        var targets = new int[rounds];
        int previous = 0;

        for (int i = 0; i < rounds; i++)
        {
            int roundsRemainingAfterThis = rounds - i - 1;
            if (i == rounds - 1)
            {
                targets[i] = totalGames;
                break;
            }

            double ideal = totalGames * (i + 1) / (double)rounds;
            int target = (int)Math.Round(ideal / 2.0, MidpointRounding.AwayFromZero) * 2;
            int minimum = previous + 2;
            int maximum = totalGames - roundsRemainingAfterThis * 2;
            target = Math.Clamp(target, minimum, maximum);
            targets[i] = target;
            previous = target;
        }

        return targets;
    }

    private static EvaluationMatchScore RunBalancedPairSegment(
        CpuEvaluationProfile candidate,
        CpuEvaluationProfile baseline,
        int depth,
        int startPairIndex,
        int pairCount,
        EvaluationTuningOptions tuning,
        int seed,
        CancellationToken cancellationToken)
    {
        if (pairCount <= 0) return EmptyMatchScore(0);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        HeadlessBatchOptions p1Options = MakeBatchOptions(
            pairCount, depth, tuning, seed, candidate, baseline).Normalize();
        HeadlessBatchOptions p2Options = MakeBatchOptions(
            pairCount, depth, tuning, unchecked(seed ^ 0x5f3759df), baseline, candidate).Normalize();

        int wins = 0;
        int losses = 0;
        int drawish = 0;
        object aggregateLock = new();

        void CountCandidateOutcome(HeadlessGameResult result, bool candidateIsP1)
        {
            bool candidateWin = candidateIsP1
                ? result.Outcome == GameOutcome.Player1Win.ToString()
                : result.Outcome == GameOutcome.Player2Win.ToString();
            bool candidateLoss = candidateIsP1
                ? result.Outcome == GameOutcome.Player2Win.ToString()
                : result.Outcome == GameOutcome.Player1Win.ToString();

            if (candidateWin) wins++;
            else if (candidateLoss) losses++;
            else drawish++;
        }

        Parallel.ForEach(
            Enumerable.Range(startPairIndex, pairCount),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = tuning.Parallelism,
                CancellationToken = cancellationToken
            },
            pairIndex =>
            {
                HeadlessGameResult asP1 = HeadlessBatchRunner.RunSingleGameForTuning(
                    p1Options, pairIndex, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                HeadlessGameResult asP2 = HeadlessBatchRunner.RunSingleGameForTuning(
                    p2Options, pairIndex, cancellationToken);

                lock (aggregateLock)
                {
                    CountCandidateOutcome(asP1, candidateIsP1: true);
                    CountCandidateOutcome(asP2, candidateIsP1: false);
                }
            });

        stopwatch.Stop();
        int games = wins + losses + drawish;
        double score = games > 0 ? (wins + drawish * 0.5) / games : 0.5;
        return new EvaluationMatchScore(
            games,
            wins,
            losses,
            drawish,
            score,
            stopwatch.ElapsedMilliseconds,
            pairCount * 2,
            StoppedEarly: false,
            StopReason: null);
    }

    private static EvaluationMatchScore EmptyMatchScore(int plannedGames) =>
        new(
            Games: 0,
            Wins: 0,
            Losses: 0,
            DrawsOrLimits: 0,
            Score: 0.5,
            ElapsedMilliseconds: 0,
            PlannedGames: plannedGames,
            StoppedEarly: false,
            StopReason: null);

    private static EvaluationMatchScore CombineMatchScores(
        EvaluationMatchScore cumulative,
        EvaluationMatchScore addition,
        int plannedGames)
    {
        int games = cumulative.Games + addition.Games;
        int wins = cumulative.Wins + addition.Wins;
        int losses = cumulative.Losses + addition.Losses;
        int drawish = cumulative.DrawsOrLimits + addition.DrawsOrLimits;
        double score = games > 0 ? (wins + drawish * 0.5) / games : 0.5;
        return new EvaluationMatchScore(
            games,
            wins,
            losses,
            drawish,
            score,
            cumulative.ElapsedMilliseconds + addition.ElapsedMilliseconds,
            plannedGames,
            StoppedEarly: false,
            StopReason: null);
    }

    private static EvaluationMatchScore RunBalancedMatch(
        CpuEvaluationProfile candidate,
        CpuEvaluationProfile baseline,
        int depth,
        int totalGames,
        EvaluationTuningOptions tuning,
        int seed,
        CancellationToken cancellationToken,
        Func<double, string?>? earlyRejectReasonForMaxPossibleScore = null)
    {
        // A null predicate means a generic balanced match that must run to completion.
        // v0.2.34.2 shallow ranking itself uses RunShallowSuccessiveHalving instead;
        // this fallback remains useful for non-thresholded internal callers/tests.
        if (earlyRejectReasonForMaxPossibleScore is null)
        {
            return RunBalancedMatchToCompletion(
                candidate, baseline, depth, totalGames, tuning, seed, cancellationToken);
        }

        // Thresholded deep matches use balanced P1/P2 pairs. This allows a mathematically
        // safe early rejection without introducing colour bias.
        int p1GamesPlanned = Math.Max(2, totalGames / 2);
        int p2GamesPlanned = Math.Max(2, totalGames - p1GamesPlanned);
        int plannedGames = p1GamesPlanned + p2GamesPlanned;
        int pairedGames = Math.Min(p1GamesPlanned, p2GamesPlanned);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        HeadlessBatchOptions p1Options = MakeBatchOptions(
            p1GamesPlanned, depth, tuning, seed, candidate, baseline).Normalize();
        HeadlessBatchOptions p2Options = MakeBatchOptions(
            p2GamesPlanned, depth, tuning, unchecked(seed ^ 0x5f3759df), baseline, candidate).Normalize();

        int wins = 0;
        int losses = 0;
        int drawish = 0;
        int games = 0;
        int pointsTwice = 0; // win=2, draw/move-limit=1, loss=0
        bool stoppedEarly = false;
        string? stopReason = null;
        object aggregateLock = new();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        void AddCandidateResult(HeadlessGameResult result, bool candidateIsP1)
        {
            bool candidateWin = candidateIsP1
                ? result.Outcome == GameOutcome.Player1Win.ToString()
                : result.Outcome == GameOutcome.Player2Win.ToString();
            bool candidateLoss = candidateIsP1
                ? result.Outcome == GameOutcome.Player2Win.ToString()
                : result.Outcome == GameOutcome.Player1Win.ToString();

            games++;
            if (candidateWin)
            {
                wins++;
                pointsTwice += 2;
            }
            else if (candidateLoss)
            {
                losses++;
            }
            else
            {
                drawish++;
                pointsTwice += 1;
            }
        }

        void CheckEarlyRejectLocked()
        {
            if (stoppedEarly || earlyRejectReasonForMaxPossibleScore is null) return;
            int remainingGames = Math.Max(0, plannedGames - games);
            double maxPossibleScore = (pointsTwice + remainingGames * 2) / (2.0 * plannedGames);
            string? reason = earlyRejectReasonForMaxPossibleScore(maxPossibleScore);
            if (reason is null) return;

            stoppedEarly = true;
            stopReason = reason;
            linkedCts.Cancel();
        }

        lock (aggregateLock)
        {
            CheckEarlyRejectLocked();
        }

        try
        {
            if (!stoppedEarly)
            {
                Parallel.ForEach(
                Enumerable.Range(0, pairedGames),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = tuning.Parallelism,
                    CancellationToken = linkedCts.Token
                },
                gameIndex =>
                {
                    HeadlessGameResult candidateAsP1 = HeadlessBatchRunner.RunSingleGameForTuning(
                        p1Options, gameIndex, linkedCts.Token);
                    linkedCts.Token.ThrowIfCancellationRequested();
                    HeadlessGameResult candidateAsP2 = HeadlessBatchRunner.RunSingleGameForTuning(
                        p2Options, gameIndex, linkedCts.Token);

                    lock (aggregateLock)
                    {
                        // Another pair may already have proved rejection while this pair
                        // was finishing. Do not count work completed after the stop point.
                        if (stoppedEarly) return;
                        AddCandidateResult(candidateAsP1, candidateIsP1: true);
                        AddCandidateResult(candidateAsP2, candidateIsP1: false);
                        CheckEarlyRejectLocked();
                    }
                });
            }
        }
        catch (OperationCanceledException) when (stoppedEarly && !cancellationToken.IsCancellationRequested)
        {
            // Internal cancellation is the normal control path for mathematical early rejection.
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Odd requested game counts retain the old allocation (the second colour gets
        // the one extra game). These are only run if the paired phase did not already
        // prove that the target is unreachable.
        if (!stoppedEarly)
        {
            for (int gameIndex = pairedGames; gameIndex < p1GamesPlanned; gameIndex++)
            {
                HeadlessGameResult result = HeadlessBatchRunner.RunSingleGameForTuning(
                    p1Options, gameIndex, cancellationToken);
                lock (aggregateLock)
                {
                    AddCandidateResult(result, candidateIsP1: true);
                    CheckEarlyRejectLocked();
                }
                if (stoppedEarly) break;
            }
        }

        if (!stoppedEarly)
        {
            for (int gameIndex = pairedGames; gameIndex < p2GamesPlanned; gameIndex++)
            {
                HeadlessGameResult result = HeadlessBatchRunner.RunSingleGameForTuning(
                    p2Options, gameIndex, cancellationToken);
                lock (aggregateLock)
                {
                    AddCandidateResult(result, candidateIsP1: false);
                    CheckEarlyRejectLocked();
                }
                if (stoppedEarly) break;
            }
        }

        stopwatch.Stop();
        double score = games > 0 ? (wins + drawish * 0.5) / games : 0.5;
        return new EvaluationMatchScore(
            games, wins, losses, drawish, score, stopwatch.ElapsedMilliseconds,
            plannedGames, stoppedEarly, stopReason);
    }

    private static EvaluationMatchScore RunBalancedMatchToCompletion(
        CpuEvaluationProfile candidate,
        CpuEvaluationProfile baseline,
        int depth,
        int totalGames,
        EvaluationTuningOptions tuning,
        int seed,
        CancellationToken cancellationToken)
    {
        int firstHalf = Math.Max(2, totalGames / 2);
        int secondHalf = Math.Max(2, totalGames - firstHalf);
        int plannedGames = firstHalf + secondHalf;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        HeadlessBatchResult asP1 = HeadlessBatchRunner.Run(
            MakeBatchOptions(firstHalf, depth, tuning, seed, candidate, baseline),
            progress: null,
            cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        HeadlessBatchResult asP2 = HeadlessBatchRunner.Run(
            MakeBatchOptions(secondHalf, depth, tuning, unchecked(seed ^ 0x5f3759df), baseline, candidate),
            progress: null,
            cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        int wins = asP1.P1Wins + asP2.P2Wins;
        int losses = asP1.P2Wins + asP2.P1Wins;
        int games = asP1.CompletedGames + asP2.CompletedGames;
        int drawish = Math.Max(0, games - wins - losses);
        double score = games > 0 ? (wins + drawish * 0.5) / games : 0.5;
        stopwatch.Stop();
        return new EvaluationMatchScore(
            games, wins, losses, drawish, score, stopwatch.ElapsedMilliseconds,
            plannedGames, StoppedEarly: false, StopReason: null);
    }

    private static string? GetValidationEarlyRejectReason(
        double shallowScore,
        double maxPossibleValidationScore)
    {
        const double epsilon = 1e-12;
        if (maxPossibleValidationScore + epsilon < 0.49)
        {
            return $"残り全勝でも検証score最大 {maxPossibleValidationScore:P1} < 49.0%";
        }

        double maxPreliminaryCombined = shallowScore * 0.65 + maxPossibleValidationScore * 0.35;
        if (maxPreliminaryCombined + epsilon < 0.515)
        {
            return $"残り全勝でも予備combined最大 {maxPreliminaryCombined:P1} < 51.5%";
        }

        return null;
    }

    private static string? GetConfirmationEarlyRejectReason(
        double shallowScore,
        double validationScore,
        double maxPossibleConfirmationScore)
    {
        const double epsilon = 1e-12;
        if (maxPossibleConfirmationScore + epsilon < 0.50)
        {
            return $"残り全勝でも決定戦score最大 {maxPossibleConfirmationScore:P1} < 50.0%";
        }

        double maxFinalCombined = shallowScore * 0.50 +
                                  validationScore * 0.25 +
                                  maxPossibleConfirmationScore * 0.25;
        if (maxFinalCombined + epsilon < 0.52)
        {
            return $"残り全勝でも最終combined最大 {maxFinalCombined:P1} < 52.0%";
        }

        return null;
    }

    private static HeadlessBatchOptions MakeBatchOptions(
        int games,
        int depth,
        EvaluationTuningOptions tuning,
        int seed,
        CpuEvaluationProfile p1,
        CpuEvaluationProfile p2) =>
        new(
            Games: games,
            P1Depth: depth,
            P2Depth: depth,
            PerMoveTimeMilliseconds: 0,
            PerMoveNodeLimit: 0,
            Parallelism: tuning.Parallelism,
            SearchParallelism: 1,
            ProgressIntervalMilliseconds: 2000,
            MaxPlies: tuning.MaxPlies,
            OpeningRandomPlies: tuning.OpeningRandomPlies,
            OpeningTopK: tuning.OpeningTopK,
            OpeningScoreWindow: tuning.OpeningScoreWindow,
            // Tuning must measure the evaluation profile itself. Do not let the product
            // root preference choose a runner-advance move merely because it is within a
            // non-zero anti-cycle score concession of strict best. Exact ties may still
            // use the deterministic root preference.
            CycleBreakScoreWindow: 0,
            Seed: seed,
            SaveMoveSequences: false,
            StartConfiguration: GameStartConfiguration.Initial,
            WriteLogs: false,
            P1EvaluationProfile: p1,
            P2EvaluationProfile: p2);

    private sealed record MutationOutcome(
        CpuEvaluationProfile Profile,
        IReadOnlyList<string> ChangedParameters,
        bool LargeMutation);

    private static MutationOutcome Mutate(
        CpuEvaluationProfile source,
        int step,
        int seed,
        string name,
        int candidateIndex)
    {
        var random = new Random(seed);
        int[] opening = ScalesToArray(source.Opening);
        int[] endgame = ScalesToArray(source.Endgame);

        // Most candidates change only 1-3 of the 32 phase-specific parameters. Every
        // sixth candidate is an explicit 5-8 parameter escape mutation. This gives the
        // endless tuner interpretable local hill-climbing most of the time while still
        // retaining a route out of local optima.
        int slotInCycle = (candidateIndex - 1) % 6;
        bool largeMutation = slotInCycle == 5;
        int targetChanges;
        if (largeMutation)
        {
            targetChanges = random.Next(5, 9);
        }
        else
        {
            int[] localPattern = { 1, 1, 2, 2, 3 };
            targetChanges = localPattern[slotInCycle];
        }

        int totalDimensions = FeatureNames.Length * 2;
        targetChanges = Math.Clamp(targetChanges, 1, totalDimensions);
        int[] dimensions = Enumerable.Range(0, totalDimensions).ToArray();
        for (int i = dimensions.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (dimensions[i], dimensions[j]) = (dimensions[j], dimensions[i]);
        }

        var changed = new List<string>(targetChanges);
        for (int i = 0; i < targetChanges; i++)
        {
            int dimension = dimensions[i];
            bool isEndgame = dimension >= FeatureNames.Length;
            int featureIndex = dimension % FeatureNames.Length;
            int[] values = isEndgame ? endgame : opening;
            int oldValue = values[featureIndex];
            int newValue = MutateOneValue(oldValue, step, random);
            values[featureIndex] = newValue;
            changed.Add($"{(isEndgame ? "E" : "O")}.{FeatureNames[featureIndex]}:{oldValue}->{newValue}");
        }

        CpuEvaluationProfile profile = new CpuEvaluationProfile(
            name,
            ArrayToScales(opening),
            ArrayToScales(endgame)).Normalize();
        return new MutationOutcome(profile, changed, largeMutation);
    }

    private static int MutateOneValue(int value, int step, Random random)
    {
        int minMagnitude = Math.Max(10, step / 3);
        int magnitude = random.Next(minMagnitude, step + 1);
        magnitude = Math.Max(10, (int)Math.Round(magnitude / 10.0) * 10);
        int sign = random.Next(2) == 0 ? -1 : 1;
        int mutated = Math.Clamp(value + sign * magnitude, 0, 3000);
        if (mutated == value)
        {
            mutated = Math.Clamp(value - sign * magnitude, 0, 3000);
        }
        return mutated;
    }

    private static int[] ScalesToArray(CpuEvaluationFeatureScales s) => new[]
    {
        s.RunnerProgress,
        s.RunnerMobility,
        s.BlockerMaterial,
        s.FriendlyRunnerSupport,
        s.FrontPressure,
        s.GoalDefense,
        s.ImmediateGoalThreats,
        s.BlockerAdvancement,
        s.BridgeheadConnection,
        s.RunnerGoalPath,
        s.PreparedGoalThreat,
        s.UnansweredGoalThreat,
        s.ConnectedGoalThreat,
        s.ViableRunnerProgress,
        s.SacrificeDebt
    };

    private static CpuEvaluationFeatureScales ArrayToScales(int[] v) => new(
        v[0], v[1], v[2], v[3], v[4], v[5], v[6],
        v[7], v[8], v[9], v[10], v[11], v[12], v[13])
        { SacrificeDebt = v[14] };

    private static List<int> BuildScanValues(int minValue, int maxValue, int step)
    {
        var values = new List<int>();
        for (int value = minValue; value <= maxValue; value += step)
        {
            values.Add(value);
            if (value > maxValue - step) break;
        }
        if (values.Count == 0 || values[^1] != maxValue)
        {
            values.Add(maxValue);
        }
        return values.Distinct().OrderBy(v => v).ToList();
    }

    private static int GetParameterValue(CpuEvaluationProfile profile, int featureIndex, bool endgame)
    {
        int[] values = ScalesToArray(endgame ? profile.Endgame : profile.Opening);
        return values[featureIndex];
    }

    private static CpuEvaluationProfile SetParameterValue(
        CpuEvaluationProfile source, int featureIndex, bool endgame, int value, string name)
    {
        int[] opening = ScalesToArray(source.Opening);
        int[] endgameValues = ScalesToArray(source.Endgame);
        int[] target = endgame ? endgameValues : opening;
        target[featureIndex] = Math.Clamp(value, 0, 3000);
        return new CpuEvaluationProfile(name, ArrayToScales(opening), ArrayToScales(endgameValues)).Normalize();
    }

    private static string WriteParameterScanReport(
        CpuEvaluationProfile baselineProfile,
        string featureName,
        bool endgame,
        int baselineValue,
        EvaluationParameterScanOptions options,
        IReadOnlyList<EvaluationParameterScanEntry> entries,
        EvaluationParameterScanEntry best,
        bool cancelled,
        long elapsedMilliseconds,
        long totalGamesCompleted)
    {
        try
        {
            string directory = HeadlessBatchRunner.ResolveAnalysisDirectory();
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"evaluation_parameter_scan_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            string json = JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                evaluationModel = "NormalizedFeaturesV4SacrificeDebt17",
                scanMode = "SingleParameterCommonSeedsBalancedPairs",
                appVersion = typeof(EvaluationTuner).Assembly.GetName().Version?.ToString() ?? "unknown",
                rulesId = RuleSet.Standard.Id,
                featureName,
                phase = endgame ? "Endgame" : "Opening",
                baselineValue,
                options,
                cancelled,
                elapsedMilliseconds,
                totalGamesCompleted,
                baselineProfile,
                bestEntry = best,
                entries
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, new UTF8Encoding(false));
            return path;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static int DistanceFrom(CpuEvaluationProfile a, CpuEvaluationProfile b)
    {
        int D(int x, int y) => Math.Abs(x - y);
        CpuEvaluationFeatureScales ao = a.Opening;
        CpuEvaluationFeatureScales bo = b.Opening;
        CpuEvaluationFeatureScales ae = a.Endgame;
        CpuEvaluationFeatureScales be = b.Endgame;
        return
            D(ao.RunnerProgress, bo.RunnerProgress) + D(ae.RunnerProgress, be.RunnerProgress) +
            D(ao.RunnerMobility, bo.RunnerMobility) + D(ae.RunnerMobility, be.RunnerMobility) +
            D(ao.BlockerMaterial, bo.BlockerMaterial) + D(ae.BlockerMaterial, be.BlockerMaterial) +
            D(ao.FriendlyRunnerSupport, bo.FriendlyRunnerSupport) + D(ae.FriendlyRunnerSupport, be.FriendlyRunnerSupport) +
            D(ao.FrontPressure, bo.FrontPressure) + D(ae.FrontPressure, be.FrontPressure) +
            D(ao.GoalDefense, bo.GoalDefense) + D(ae.GoalDefense, be.GoalDefense) +
            D(ao.ImmediateGoalThreats, bo.ImmediateGoalThreats) + D(ae.ImmediateGoalThreats, be.ImmediateGoalThreats) +
            D(ao.BlockerAdvancement, bo.BlockerAdvancement) + D(ae.BlockerAdvancement, be.BlockerAdvancement) +
            D(ao.BridgeheadConnection, bo.BridgeheadConnection) + D(ae.BridgeheadConnection, be.BridgeheadConnection) +
            D(ao.RunnerGoalPath, bo.RunnerGoalPath) + D(ae.RunnerGoalPath, be.RunnerGoalPath) +
            D(ao.PreparedGoalThreat, bo.PreparedGoalThreat) + D(ae.PreparedGoalThreat, be.PreparedGoalThreat) +
            D(ao.UnansweredGoalThreat, bo.UnansweredGoalThreat) + D(ae.UnansweredGoalThreat, be.UnansweredGoalThreat) +
            D(ao.ConnectedGoalThreat, bo.ConnectedGoalThreat) + D(ae.ConnectedGoalThreat, be.ConnectedGoalThreat) +
            D(ao.ViableRunnerProgress, bo.ViableRunnerProgress) + D(ae.ViableRunnerProgress, be.ViableRunnerProgress) +
            D(ao.SacrificeDebt, bo.SacrificeDebt) + D(ae.SacrificeDebt, be.SacrificeDebt);
    }

    private static void WriteCheckpoint(
        CpuEvaluationProfile startingProfile,
        CpuEvaluationProfile bestProfile,
        int generation,
        int acceptedCount,
        EvaluationTuningOptions options,
        long elapsedMilliseconds)
    {
        try
        {
            string directory = HeadlessBatchRunner.ResolveAnalysisDirectory();
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "evaluation_tuning_checkpoint.json");
            string tempPath = path + ".tmp";
            string json = JsonSerializer.Serialize(new
            {
                schemaVersion = 5,
                evaluationModel = "NormalizedFeaturesV4SacrificeDebt17",
                shallowSelection = "SuccessiveHalvingBalancedPairs",
                updatedAt = DateTimeOffset.Now,
                generation,
                acceptedCount,
                elapsedMilliseconds,
                startingProfile,
                bestProfile,
                options
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            // Tuning itself must continue even if a checkpoint cannot be written.
        }
    }

    private static string WriteReport(
        CpuEvaluationProfile startingProfile,
        CpuEvaluationProfile bestProfile,
        IReadOnlyList<EvaluationTuningGenerationResult> generations,
        EvaluationTuningOptions options,
        bool cancelled,
        long elapsedMilliseconds,
        long totalGamesCompleted,
        int bestUpdateCount,
        int lastBestGeneration,
        int stagnation)
    {
        try
        {
            string directory = HeadlessBatchRunner.ResolveAnalysisDirectory();
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"evaluation_tuning_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            string json = JsonSerializer.Serialize(new
            {
                schemaVersion = 7,
                evaluationModel = "NormalizedFeaturesV4SacrificeDebt17",
                shallowSelection = "SuccessiveHalvingBalancedPairs",
                appVersion = typeof(EvaluationTuner).Assembly.GetName().Version?.ToString() ?? "unknown",
                rulesId = RuleSet.Standard.Id,
                options,
                cancelled,
                elapsedMilliseconds,
                totalGamesCompleted,
                bestUpdateCount,
                lastBestGeneration,
                stagnation,
                startingProfile,
                bestProfile,
                generations
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, new UTF8Encoding(false));
            return path;
        }
        catch
        {
            return string.Empty;
        }
    }
}
