using System.Text;
using System.Text.Json;

namespace StarRunnerPrototype;

public sealed class HeadlessScenarioFile
{
    public int SchemaVersion { get; set; } = 1;
    public string? SuiteName { get; set; }
    public HeadlessScenarioCase? Scenario { get; set; }
    public List<HeadlessScenarioCase>? Scenarios { get; set; }

    public static IReadOnlyList<HeadlessScenarioCase> Load(string path)
    {
        string json = File.ReadAllText(path, Encoding.UTF8);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        HeadlessScenarioFile? doc = JsonSerializer.Deserialize<HeadlessScenarioFile>(json, options);
        if (doc is null)
        {
            throw new InvalidDataException("Scenario JSON is empty or invalid.");
        }
        if (doc.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported scenario schemaVersion {doc.SchemaVersion}. Expected 1.");
        }

        var cases = new List<HeadlessScenarioCase>();
        if (doc.Scenario is not null) cases.Add(doc.Scenario);
        if (doc.Scenarios is not null) cases.AddRange(doc.Scenarios);
        if (cases.Count == 0)
        {
            using JsonDocument root = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            if (root.RootElement.ValueKind == JsonValueKind.Object && root.RootElement.TryGetProperty("board", out _))
            {
                HeadlessScenarioCase? direct = JsonSerializer.Deserialize<HeadlessScenarioCase>(json, options);
                if (direct is not null) cases.Add(direct);
            }
        }
        if (cases.Count == 0)
        {
            throw new InvalidDataException("Scenario JSON must contain 'scenario', 'scenarios', or a direct scenario object with 'board'.");
        }

        string sourceName = Path.GetFileName(path);
        for (int i = 0; i < cases.Count; i++)
        {
            cases[i].ValidateAndPrepare(sourceName, i);
        }
        return cases;
    }
}

public sealed class HeadlessScenarioCase
{
    public string? Name { get; set; }
    public string[]? Board { get; set; }
    public string? CurrentPlayer { get; set; }
    public ScenarioBatchSettings? Settings { get; set; }
    public ScenarioPlayerStrategy? P1Strategy { get; set; }
    public ScenarioPlayerStrategy? P2Strategy { get; set; }

    public GameStartConfiguration? StartConfiguration { get; private set; }
    public ScenarioMovePolicy? MovePolicy { get; private set; }

    internal void ValidateAndPrepare(string sourceName, int index)
    {
        string[] board = Board ?? GameStartConfiguration.Initial.CopyBoardRows();
        PlayerId current = ParsePlayer(CurrentPlayer ?? "P1");
        PlayerStrategyConstraint p1 = (P1Strategy ?? new ScenarioPlayerStrategy()).ToConstraint(PlayerId.Player1);
        PlayerStrategyConstraint p2 = (P2Strategy ?? new ScenarioPlayerStrategy()).ToConstraint(PlayerId.Player2);
        StartConfiguration = GameStartConfiguration.Create(Name ?? $"Scenario {index + 1}", sourceName, board, current);
        MovePolicy = new ScenarioMovePolicy(p1, p2);
        _ = new GameEngine(StartConfiguration, MovePolicy.IsActive ? MovePolicy : null); // validate policy against board

    }

    public HeadlessBatchOptions ApplyTo(HeadlessBatchOptions baseline)
    {
        if (StartConfiguration is null)
        {
            throw new InvalidOperationException("Scenario was not prepared.");
        }

        ScenarioBatchSettings s = Settings ?? new ScenarioBatchSettings();
        return baseline with
        {
            Games = s.Games ?? baseline.Games,
            P1Depth = s.P1Depth ?? baseline.P1Depth,
            P2Depth = s.P2Depth ?? baseline.P2Depth,
            PerMoveTimeMilliseconds = s.PerMoveTimeMilliseconds ?? baseline.PerMoveTimeMilliseconds,
            PerMoveNodeLimit = s.PerMoveNodeLimit ?? baseline.PerMoveNodeLimit,
            Parallelism = s.Parallelism ?? baseline.Parallelism,
            SearchParallelism = s.SearchParallelism ?? baseline.SearchParallelism,
            ProgressIntervalMilliseconds = s.ProgressIntervalMilliseconds ?? baseline.ProgressIntervalMilliseconds,
            MaxPlies = s.MaxPlies ?? baseline.MaxPlies,
            OpeningRandomPlies = s.OpeningRandomPlies ?? 0,
            OpeningTopK = s.OpeningTopK ?? baseline.OpeningTopK,
            OpeningScoreWindow = s.OpeningScoreWindow ?? baseline.OpeningScoreWindow,
            CycleBreakScoreWindow = s.CycleBreakScoreWindow ?? baseline.CycleBreakScoreWindow,
            Seed = s.Seed ?? baseline.Seed,
            SaveMoveSequences = s.SaveMoveSequences ?? baseline.SaveMoveSequences,
            StartConfiguration = StartConfiguration,
            MovePolicy = MovePolicy
        };
    }

    private static PlayerId ParsePlayer(string value)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "P1" or "PLAYER1" or "1" => PlayerId.Player1,
            "P2" or "PLAYER2" or "2" => PlayerId.Player2,
            _ => throw new InvalidDataException($"currentPlayer must be P1 or P2, got '{value}'.")
        };
    }
}

public sealed class ScenarioBatchSettings
{
    public int? Games { get; set; }
    public int? P1Depth { get; set; }
    public int? P2Depth { get; set; }
    public int? PerMoveTimeMilliseconds { get; set; }
    public long? PerMoveNodeLimit { get; set; }
    public int? Parallelism { get; set; }
    public int? SearchParallelism { get; set; }
    public int? ProgressIntervalMilliseconds { get; set; }
    public int? MaxPlies { get; set; }
    public int? OpeningRandomPlies { get; set; }
    public int? OpeningTopK { get; set; }
    public int? OpeningScoreWindow { get; set; }
    public int? CycleBreakScoreWindow { get; set; }
    public int? Seed { get; set; }
    public bool? SaveMoveSequences { get; set; }
}

public sealed class ScenarioPlayerStrategy
{
    public string? Mode { get; set; }
    public string? AttackBlocker { get; set; }
    public int? DeviationBudget { get; set; }

    public PlayerStrategyConstraint ToConstraint(PlayerId player)
    {
        string mode = (Mode ?? "Free").Trim();
        if (mode.Equals("Free", StringComparison.OrdinalIgnoreCase))
        {
            return PlayerStrategyConstraint.Free;
        }
        if (!mode.Equals("RushOne", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{player.ShortName()} strategy mode must be Free or RushOne.");
        }

        Position attack = ParseCoordinate(AttackBlocker
            ?? throw new InvalidDataException($"{player.ShortName()} RushOne requires attackBlocker."));
        return new PlayerStrategyConstraint(StrategyMode.RushOne, attack, DeviationBudget ?? 0);
    }

    public static Position ParseCoordinate(string value)
    {
        string text = value.Trim().ToUpperInvariant();
        if (text.Length != 2 || text[0] < 'A' || text[0] > 'H' || text[1] < '1' || text[1] > '8')
        {
            throw new InvalidDataException($"Invalid coordinate '{value}'. Expected A1..H8.");
        }
        return new Position(text[1] - '1', text[0] - 'A');
    }
}
