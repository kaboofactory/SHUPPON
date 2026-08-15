using System.Text;
using System.Text.Json;

namespace StarRunnerPrototype;

/// <summary>
/// Development-app persistence for AI evaluation overrides. File I/O deliberately lives
/// outside StarRunner.AI so embedding hosts are never affected by ambient files.
/// </summary>
internal static class CpuEvaluationProfileStorage
{
    private const string FileName = "evaluation_profile_v2.json";

    public static string CurrentSource => CpuEvaluationProfileProvider.CurrentSource;
    public static string PreferredOverridePath => ResolveWritableProfilePath();

    public static void LoadIntoProvider()
    {
        string[] paths = CandidateProfilePaths()
            .Where(File.Exists)
            .OrderByDescending(path =>
            {
                try { return File.GetLastWriteTimeUtc(path); }
                catch { return DateTime.MinValue; }
            })
            .ToArray();

        foreach (string path in paths)
        {
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                CpuEvaluationProfile? profile = DeserializeProfileCompatible(json);
                if (profile is not null)
                {
                    CpuEvaluationProfileProvider.SetCurrent(profile, path);
                    return;
                }
            }
            catch
            {
                // An invalid developer override must never prevent the game from starting.
            }
        }

        CpuEvaluationProfileProvider.ResetToBuiltIn();
    }

    private static CpuEvaluationProfile? DeserializeProfileCompatible(string json)
    {
        CpuEvaluationProfile? profile = JsonSerializer.Deserialize<CpuEvaluationProfile>(json);
        if (profile is null) return null;

        // Compatibility migration:
        // - v0.2.30.0 and earlier omit RunnerGoalPath; that old missing field means the
        //   neutral 1000‰ value, not 0‰ (disabled).
        // - v0.2.35.0 and earlier omit the four interaction features. They intentionally
        //   migrate to 0‰ so loading an old override cannot silently change playing style.
        using JsonDocument document = JsonDocument.Parse(json);
        bool openingHasGoalPath = HasNestedProperty(document.RootElement, "Opening", "RunnerGoalPath");
        bool endgameHasGoalPath = HasNestedProperty(document.RootElement, "Endgame", "RunnerGoalPath");

        CpuEvaluationFeatureScales opening = profile.Opening;
        CpuEvaluationFeatureScales endgame = profile.Endgame;
        if (!openingHasGoalPath) opening = opening with { RunnerGoalPath = 1000 };
        if (!endgameHasGoalPath) endgame = endgame with { RunnerGoalPath = 1000 };

        opening = MigrateInteractionFields(document.RootElement, "Opening", opening);
        endgame = MigrateInteractionFields(document.RootElement, "Endgame", endgame);
        if (!HasNestedProperty(document.RootElement, "Opening", "SacrificeDebt"))
            opening = opening with { SacrificeDebt = CpuEvaluationProfile.BuiltInDefault.Opening.SacrificeDebt };
        if (!HasNestedProperty(document.RootElement, "Endgame", "SacrificeDebt"))
            endgame = endgame with { SacrificeDebt = CpuEvaluationProfile.BuiltInDefault.Endgame.SacrificeDebt };
        return profile with { Opening = opening, Endgame = endgame };
    }


    private static CpuEvaluationFeatureScales MigrateInteractionFields(
        JsonElement root,
        string phaseName,
        CpuEvaluationFeatureScales scales)
    {
        if (!HasNestedProperty(root, phaseName, "PreparedGoalThreat"))
            scales = scales with { PreparedGoalThreat = 0 };
        if (!HasNestedProperty(root, phaseName, "UnansweredGoalThreat"))
            scales = scales with { UnansweredGoalThreat = 0 };
        if (!HasNestedProperty(root, phaseName, "ConnectedGoalThreat"))
            scales = scales with { ConnectedGoalThreat = 0 };
        if (!HasNestedProperty(root, phaseName, "ViableRunnerProgress"))
            scales = scales with { ViableRunnerProgress = 0 };
        return scales;
    }

    private static bool HasNestedProperty(JsonElement root, string objectName, string propertyName)
    {
        foreach (JsonProperty outer in root.EnumerateObject())
        {
            if (!string.Equals(outer.Name, objectName, StringComparison.OrdinalIgnoreCase) ||
                outer.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (JsonProperty inner in outer.Value.EnumerateObject())
            {
                if (string.Equals(inner.Name, propertyName, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    public static void SaveOverride(CpuEvaluationProfile profile)
    {
        profile = profile.Normalize();
        string path = ResolveWritableProfilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, new UTF8Encoding(false));
        CpuEvaluationProfileProvider.SetCurrent(profile, path);
    }

    public static bool DeleteOverride()
    {
        bool failed = false;
        foreach (string path in CandidateProfilePaths())
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                failed = true;
            }
        }

        LoadIntoProvider();
        return !failed && string.Equals(
            CpuEvaluationProfileProvider.Current.Name,
            CpuEvaluationProfile.BuiltInDefault.Name,
            StringComparison.Ordinal);
    }

    private static IEnumerable<string> CandidateProfilePaths()
    {
        yield return Path.Combine(AppContext.BaseDirectory, FileName);
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StarRunnerPrototype",
            FileName);
    }

    private static string ResolveWritableProfilePath()
    {
        string besideExe = Path.Combine(AppContext.BaseDirectory, FileName);
        try
        {
            string directory = Path.GetDirectoryName(besideExe)!;
            Directory.CreateDirectory(directory);
            string probe = Path.Combine(directory, $".eval_write_test_{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return besideExe;
        }
        catch
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StarRunnerPrototype",
                FileName);
        }
    }
}
