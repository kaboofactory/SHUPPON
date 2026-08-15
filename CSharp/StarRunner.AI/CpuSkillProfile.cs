using StarRunner.Core;

namespace StarRunner.AI;

/// <summary>
/// Product-facing CPU strength profile.
/// Strength is defined only by a fixed MaxNodes budget; no calibration or deliberate
/// randomness is used by the standard 20-level table.
/// </summary>
public sealed record CpuSkillProfile(
    int Index,
    string Name,
    long MaxNodes)
{
    public const int SearchDepthCap = 99;

    public int MaxDepth => SearchDepthCap; // safety ceiling; standard strength is controlled by MaxNodes

    public CpuSkillProfile Normalize() => this with
    {
        Index = Math.Clamp(Index, 0, 19),
        MaxNodes = Math.Clamp(MaxNodes, 1L, 2_000_000_000L)
    };

    public CpuSearchOptions ToSearchOptions(
        int randomSeed,
        int maxParallelism,
        int timeLimitMilliseconds = 0,
        long maxNodes = 0,
        int cycleBreakScoreWindow = 10,
        bool useBelowNormalThreadPriority = false)
    {
        CpuSkillProfile p = Normalize();

        // A caller-provided node limit is an additional safety cap, never a way to make a
        // built-in skill search more nodes than its profile allows.
        long effectiveMaxNodes = maxNodes > 0 ? Math.Min(p.MaxNodes, maxNodes) : p.MaxNodes;

        return new CpuSearchOptions(
            MaxDepth: SearchDepthCap,
            TimeLimitMilliseconds: timeLimitMilliseconds,
            MaxNodes: effectiveMaxNodes,
            UseTranspositionTable: true,
            CollectExactRootScores: false,
            RandomTopK: 1,
            RandomScoreWindow: 0,
            RandomSelectionTemperature: 0,
            RandomMoveProbability: 0,
            RandomSeed: randomSeed,
            CycleBreakScoreWindow: cycleBreakScoreWindow,
            MaxParallelism: maxParallelism,
            UseBelowNormalThreadPriority: useBelowNormalThreadPriority);
    }

    public override string ToString() => Name;
}

public static class CpuSkillProfiles
{
    /// <summary>15級の探索局面数。</summary>
    public const long BaseMaxNodes = 200;

    /// <summary>1段階上がるごとのMaxNodes倍率（+80%）。</summary>
    public const decimal NodeMultiplier = 1.80m;

    public static readonly string[] ExpectedNames =
    {
        "15級", "14級", "13級", "12級", "11級", "10級", "9級", "8級", "7級", "6級",
        "5級", "4級", "3級", "2級", "1級", "初段", "二段", "三段", "四段", "五段"
    };

    /// <summary>
    /// Shipping standard. 15級=N200を起点に、1段階ごとに正確に×1.80した値を
    /// 四捨五入してMaxNodesへ設定する。五段はN14,164,707。
    /// </summary>
    public static IReadOnlyList<CpuSkillProfile> BuiltInStandard { get; } = BuildStandardProfiles();

    private static IReadOnlyList<CpuSkillProfile> BuildStandardProfiles()
    {
        var profiles = new CpuSkillProfile[ExpectedNames.Length];
        decimal nodes = BaseMaxNodes;
        for (int i = 0; i < profiles.Length; i++)
        {
            long rounded = decimal.ToInt64(decimal.Round(nodes, 0, MidpointRounding.AwayFromZero));
            profiles[i] = new CpuSkillProfile(i, ExpectedNames[i], rounded);
            nodes *= NodeMultiplier;
        }

        CpuSkillProfileValidation.Validate(profiles);
        return profiles;
    }
}

public static class CpuSkillProfileValidation
{
    public static void Validate(IReadOnlyList<CpuSkillProfile> profiles)
    {
        if (profiles.Count != 20)
        {
            throw new InvalidDataException("CPU棋力プロフィールは20段階である必要があります。");
        }

        CpuSkillProfile[] ordered = profiles.OrderBy(p => p.Index).ToArray();
        for (int i = 0; i < ordered.Length; i++)
        {
            if (ordered[i].Index != i || ordered[i].Name != CpuSkillProfiles.ExpectedNames[i])
            {
                throw new InvalidDataException($"CPU棋力プロフィール{i}の名前または順序が不正です。");
            }

            if (i == 0)
            {
                if (ordered[i].MaxNodes != CpuSkillProfiles.BaseMaxNodes)
                {
                    throw new InvalidDataException($"15級はN{CpuSkillProfiles.BaseMaxNodes:N0}である必要があります。");
                }
                continue;
            }

            if (ordered[i].MaxNodes <= ordered[i - 1].MaxNodes)
            {
                throw new InvalidDataException(
                    $"MaxNodesの強度順が逆転しています: {ordered[i - 1].Name}=N{ordered[i - 1].MaxNodes:N0}, " +
                    $"{ordered[i].Name}=N{ordered[i].MaxNodes:N0}。");
            }
        }
    }
}
