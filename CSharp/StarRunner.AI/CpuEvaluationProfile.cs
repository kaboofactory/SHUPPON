
using StarRunner.Core;

namespace StarRunner.AI;

public readonly record struct CpuEvaluationFeatureScales(
    int RunnerProgress,
    int RunnerMobility,
    int BlockerMaterial,
    int FriendlyRunnerSupport,
    int FrontPressure,
    int GoalDefense,
    int ImmediateGoalThreats,
    int BlockerAdvancement,
    int BridgeheadConnection,
    int RunnerGoalPath = 1000,
    int PreparedGoalThreat = 0,
    int UnansweredGoalThreat = 0,
    int ConnectedGoalThreat = 0,
    int ViableRunnerProgress = 0)
{
    // v0.2.37.4 additive feature property. It remains outside the positional record signature;
    // v0.2.37.5 retired GoalBridgeheads and RunnerCentrality from the active vocabulary.
    public int SacrificeDebt { get; init; }

    public static CpuEvaluationFeatureScales Neutral => new(
        1000, 1000, 1000, 1000, 1000, 1000, 1000, 1000, 1000, 1000,
        0, 0, 0, 0);

    public static CpuEvaluationFeatureScales AllOff => new(
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public CpuEvaluationFeatureScales Normalize() => new(
        ClampScale(RunnerProgress),
        ClampScale(RunnerMobility),
        ClampScale(BlockerMaterial),
        ClampScale(FriendlyRunnerSupport),
        ClampScale(FrontPressure),
        ClampScale(GoalDefense),
        ClampScale(ImmediateGoalThreats),
        ClampScale(BlockerAdvancement),
        ClampScale(BridgeheadConnection),
        ClampScale(RunnerGoalPath),
        ClampScale(PreparedGoalThreat),
        ClampScale(UnansweredGoalThreat),
        ClampScale(ConnectedGoalThreat),
        ClampScale(ViableRunnerProgress))
        { SacrificeDebt = ClampScale(SacrificeDebt) };

    // 0 permits the tuner to switch a feature off completely. Negative scales are not
    // allowed because every feature is defined so that a positive raw value is good for
    // the perspective player.
    private static int ClampScale(int value) => Math.Clamp(value, 0, 3000);
}

public sealed record CpuEvaluationProfile(
    string Name,
    CpuEvaluationFeatureScales Opening,
    CpuEvaluationFeatureScales Endgame)
{
    // v0.2.26.0 baseline for the normalized features. The original core features are
    // deliberately flat at 1000‰. v0.2.36.0 interaction features remain 0‰ here so this
    // historical baseline and the current built-in profile do not silently change merely
    // because the feature vocabulary grew. Retired features are not represented. Opening and endgame start identical.
    public static CpuEvaluationProfile FlatNormalizedV1 => new(
        "FlatNormalizedV1",
        CpuEvaluationFeatureScales.Neutral,
        CpuEvaluationFeatureScales.Neutral);

    // Previous product default captured from the long-running self-tuner on 2026-08-12.
    // Kept as a named preset so older experiments can still be reproduced explicitly.
    public static CpuEvaluationProfile TunedG0042 => new(
        "Tuned-G0042",
        new CpuEvaluationFeatureScales(
            RunnerProgress: 1000,
            RunnerMobility: 840,
            BlockerMaterial: 1010,
            FriendlyRunnerSupport: 1090,
            FrontPressure: 1350,
            GoalDefense: 860,
            ImmediateGoalThreats: 1090,
            BlockerAdvancement: 1200,
            BridgeheadConnection: 1540,
            RunnerGoalPath: 1000),
        new CpuEvaluationFeatureScales(
            RunnerProgress: 1010,
            RunnerMobility: 1020,
            BlockerMaterial: 760,
            FriendlyRunnerSupport: 900,
            FrontPressure: 860,
            GoalDefense: 1100,
            ImmediateGoalThreats: 1300,
            BlockerAdvancement: 1140,
            BridgeheadConnection: 1180,
            RunnerGoalPath: 1000));

    // Historical product default selected from the deeper-search self-tuner run on 2026-08-12.
    // The values printed by the tuner are already the permille multipliers consumed by
    // the normalized evaluator, so they are stored verbatim here (no second normalization).
    public static CpuEvaluationProfile TunedG0015 => new(
        "Tuned-G0015",
        new CpuEvaluationFeatureScales(
            RunnerProgress: 1140,
            RunnerMobility: 400,
            BlockerMaterial: 1080,
            FriendlyRunnerSupport: 930,
            FrontPressure: 1770,
            GoalDefense: 1050,
            ImmediateGoalThreats: 1480,
            BlockerAdvancement: 1160,
            BridgeheadConnection: 2250,
            RunnerGoalPath: 1000),
        new CpuEvaluationFeatureScales(
            RunnerProgress: 940,
            RunnerMobility: 1160,
            BlockerMaterial: 390,
            FriendlyRunnerSupport: 990,
            FrontPressure: 1340,
            GoalDefense: 910,
            ImmediateGoalThreats: 2160,
            BlockerAdvancement: 960,
            BridgeheadConnection: 1680,
            RunnerGoalPath: 1000));

    // Previous product default from the 2026-08-13 long-running 24-parameter tuner.
    // RunnerGoalPath Opening moved to 1150‰ while Endgame remained at 1000‰.
    public static CpuEvaluationProfile TunedG0028 => new(
        "Tuned-G0028",
        new CpuEvaluationFeatureScales(
            RunnerProgress: 1400,
            RunnerMobility: 400,
            BlockerMaterial: 1110,
            FriendlyRunnerSupport: 1040,
            FrontPressure: 1830,
            GoalDefense: 1240,
            ImmediateGoalThreats: 1390,
            BlockerAdvancement: 1300,
            BridgeheadConnection: 2210,
            RunnerGoalPath: 1150),
        new CpuEvaluationFeatureScales(
            RunnerProgress: 900,
            RunnerMobility: 1180,
            BlockerMaterial: 390,
            FriendlyRunnerSupport: 930,
            FrontPressure: 1360,
            GoalDefense: 770,
            ImmediateGoalThreats: 2320,
            BlockerAdvancement: 870,
            BridgeheadConnection: 1710,
            RunnerGoalPath: 1000));

    // Previous product default selected from the user's one-parameter scan result on 2026-08-14.
    // The profile name is kept verbatim so the built-in default can be matched against the
    // scan result that was explicitly accepted as the new best parameter set.
    public static CpuEvaluationProfile ScanORunnerGoalPath1500 => new(
        "Scan-O.RunnerGoalPath-1500",
        new CpuEvaluationFeatureScales(
            RunnerProgress: 1400,
            RunnerMobility: 400,
            BlockerMaterial: 1110,
            FriendlyRunnerSupport: 1040,
            FrontPressure: 1830,
            GoalDefense: 1240,
            ImmediateGoalThreats: 1390,
            BlockerAdvancement: 1300,
            BridgeheadConnection: 2210,
            RunnerGoalPath: 1500),
        new CpuEvaluationFeatureScales(
            RunnerProgress: 780,
            RunnerMobility: 1180,
            BlockerMaterial: 390,
            FriendlyRunnerSupport: 930,
            FrontPressure: 1550,
            GoalDefense: 770,
            ImmediateGoalThreats: 2320,
            BlockerAdvancement: 870,
            BridgeheadConnection: 1710,
            RunnerGoalPath: 1600));

    // Accepted 32-weight tuner result reported on 2026-08-14 after the v0.2.36.2
    // evaluation-logic revision. Values are stored verbatim from the user's Tuned-G0004
    // output and are the product default from v0.2.36.4 onward.
    public static CpuEvaluationProfile TunedG0004 => new(
        "Tuned-G0004",
        new CpuEvaluationFeatureScales(
            RunnerProgress: 1520,
            RunnerMobility: 490,
            BlockerMaterial: 1250,
            FriendlyRunnerSupport: 1120,
            FrontPressure: 1830,
            GoalDefense: 1240,
            ImmediateGoalThreats: 1390,
            BlockerAdvancement: 1300,
            BridgeheadConnection: 2210,
            RunnerGoalPath: 1500,
            PreparedGoalThreat: 750,
            UnansweredGoalThreat: 470,
            ConnectedGoalThreat: 600,
            ViableRunnerProgress: 1220)
            { SacrificeDebt = 1800 },
        new CpuEvaluationFeatureScales(
            RunnerProgress: 780,
            RunnerMobility: 1390,
            BlockerMaterial: 560,
            FriendlyRunnerSupport: 930,
            FrontPressure: 1550,
            GoalDefense: 770,
            ImmediateGoalThreats: 2320,
            BlockerAdvancement: 870,
            BridgeheadConnection: 1710,
            RunnerGoalPath: 1510,
            PreparedGoalThreat: 1310,
            UnansweredGoalThreat: 960,
            ConnectedGoalThreat: 1250,
            ViableRunnerProgress: 780)
            { SacrificeDebt = 2600 });

    // Accepted active 15-feature profile from the user's 2026-08-15 one-parameter
    // scan after BlockerMaterial was redefined as effective offensive material.
    // Tuned-G0004 remains available as a historical preset; this profile is the
    // product built-in default from v0.2.37.8 onward.
    public static CpuEvaluationProfile ScanOBlockerMaterial1000 => new(
        "Scan-O.BlockerMaterial-1000",
        new CpuEvaluationFeatureScales(
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
            { SacrificeDebt = 1800 },
        new CpuEvaluationFeatureScales(
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
            { SacrificeDebt = 2600 });

    // Historical provisional best from the 32-weight self-tuner run reported on 2026-08-14.
    // This is the first built-in profile where the four v0.2.36 interaction features carry
    // learned non-zero weights. Values are stored verbatim from the tuner output.
    public static CpuEvaluationProfile TunedG0019 => new(
        "Tuned-G0019",
        new CpuEvaluationFeatureScales(
            RunnerProgress: 1400,
            RunnerMobility: 550,
            BlockerMaterial: 1110,
            FriendlyRunnerSupport: 1040,
            FrontPressure: 1830,
            GoalDefense: 1240,
            ImmediateGoalThreats: 1390,
            BlockerAdvancement: 1300,
            BridgeheadConnection: 2210,
            RunnerGoalPath: 1500,
            PreparedGoalThreat: 900,
            UnansweredGoalThreat: 600,
            ConnectedGoalThreat: 600,
            ViableRunnerProgress: 1220),
        new CpuEvaluationFeatureScales(
            RunnerProgress: 780,
            RunnerMobility: 1390,
            BlockerMaterial: 560,
            FriendlyRunnerSupport: 930,
            FrontPressure: 1550,
            GoalDefense: 770,
            ImmediateGoalThreats: 2320,
            BlockerAdvancement: 870,
            BridgeheadConnection: 1710,
            RunnerGoalPath: 1600,
            PreparedGoalThreat: 1310,
            UnansweredGoalThreat: 1080,
            ConnectedGoalThreat: 1100,
            ViableRunnerProgress: 780));

    public static CpuEvaluationProfile BuiltInDefault => ScanOBlockerMaterial1000;

    public CpuEvaluationProfile Normalize() => new(
        string.IsNullOrWhiteSpace(Name) ? "Custom" : Name.Trim(),
        Opening.Normalize(),
        Endgame.Normalize());

    public CpuEvaluationFeatureScales Blend(int phasePermille)
    {
        int phase = Math.Clamp(phasePermille, 0, 1000);
        int opening = 1000 - phase;

        int B(int a, int b) => (a * opening + b * phase + 500) / 1000;

        return new CpuEvaluationFeatureScales(
            B(Opening.RunnerProgress, Endgame.RunnerProgress),
            B(Opening.RunnerMobility, Endgame.RunnerMobility),
            B(Opening.BlockerMaterial, Endgame.BlockerMaterial),
            B(Opening.FriendlyRunnerSupport, Endgame.FriendlyRunnerSupport),
            B(Opening.FrontPressure, Endgame.FrontPressure),
            B(Opening.GoalDefense, Endgame.GoalDefense),
            B(Opening.ImmediateGoalThreats, Endgame.ImmediateGoalThreats),
            B(Opening.BlockerAdvancement, Endgame.BlockerAdvancement),
            B(Opening.BridgeheadConnection, Endgame.BridgeheadConnection),
            B(Opening.RunnerGoalPath, Endgame.RunnerGoalPath),
            B(Opening.PreparedGoalThreat, Endgame.PreparedGoalThreat),
            B(Opening.UnansweredGoalThreat, Endgame.UnansweredGoalThreat),
            B(Opening.ConnectedGoalThreat, Endgame.ConnectedGoalThreat),
            B(Opening.ViableRunnerProgress, Endgame.ViableRunnerProgress))
            { SacrificeDebt = B(Opening.SacrificeDebt, Endgame.SacrificeDebt) };
    }
}

public static class CpuEvaluationProfileProvider
{
    private static readonly object Gate = new();
    private static CpuEvaluationProfile _current = CpuEvaluationProfile.BuiltInDefault;
    private static string _source = "built-in";

    /// <summary>
    /// Process-local evaluation profile. The AI library never reads or writes files by itself.
    /// Embedding hosts may explicitly provide an override through SetCurrent.
    /// </summary>
    public static CpuEvaluationProfile Current
    {
        get { lock (Gate) return _current; }
    }

    public static string CurrentSource
    {
        get { lock (Gate) return _source; }
    }

    public static void SetCurrent(CpuEvaluationProfile profile, string source = "host")
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (Gate)
        {
            _current = profile.Normalize();
            _source = string.IsNullOrWhiteSpace(source) ? "host" : source.Trim();
        }
    }

    public static void ResetToBuiltIn()
    {
        lock (Gate)
        {
            _current = CpuEvaluationProfile.BuiltInDefault;
            _source = "built-in";
        }
    }
}
