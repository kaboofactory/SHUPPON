using System.Text.Json;
using System.Text.RegularExpressions;

namespace StarRunnerPrototype;

public sealed record ReplayMove(
    int Ply,
    PlayerId Player,
    Move Move,
    string Detail);

public sealed class ReplayGame
{
    public required string DisplayName { get; init; }
    public required string SourcePath { get; init; }
    public required RuleSet Rules { get; init; }
    public required GameStartConfiguration StartConfiguration { get; init; }
    public ScenarioMovePolicy? MovePolicy { get; init; }
    public required IReadOnlyList<ReplayMove> Moves { get; init; }
    public required string RecordedOutcome { get; init; }
    public required string RecordedEndReason { get; init; }
    public int? Seed { get; init; }

    public IReadOnlyList<GameEngine> BuildSnapshots()
    {
        var snapshots = new List<GameEngine>(Moves.Count + 1);
        ScenarioMovePolicy? policy = MovePolicy?.IsActive == true ? MovePolicy : null;
        var game = new GameEngine(StartConfiguration, policy);
        snapshots.Add(game.Clone());

        foreach (ReplayMove replayMove in Moves)
        {
            if (game.Outcome != GameOutcome.Ongoing)
            {
                throw new InvalidDataException(
                    $"棋譜 {DisplayName}: {replayMove.Ply}手目より前に対局が終了しています ({game.Outcome}/{game.EndReason})。");
            }

            if (game.CurrentPlayer != replayMove.Player)
            {
                throw new InvalidDataException(
                    $"棋譜 {DisplayName}: {replayMove.Ply}手目の手番が一致しません。" +
                    $" ログ={replayMove.Player.ShortName()} / 再構築={game.CurrentPlayer.ShortName()}");
            }

            if (!game.TryApplyMove(replayMove.Move, out string? error))
            {
                throw new InvalidDataException(
                    $"棋譜 {DisplayName}: {replayMove.Ply}手目 {replayMove.Move.ToNotation()} を再現できません。{error}");
            }

            snapshots.Add(game.Clone());
        }

        return snapshots;
    }
}

public static class ReplayLoader
{
    private static readonly Regex MoveNotationRegex = new(
        @"^(?<from>[A-H][1-8])(?<sep>[-x])(?<to>[A-H][1-8])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static IReadOnlyList<ReplayGame> Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("棋譜JSONLが見つかりません。", path);
        }

        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            throw new InvalidDataException("JSONLファイルが空です。");
        }

        var documents = new List<JsonDocument>(lines.Length);
        try
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                try
                {
                    documents.Add(JsonDocument.Parse(lines[i]));
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException($"JSONL {i + 1}行目を解析できません。", ex);
                }
            }

            if (documents.Count == 0)
            {
                throw new InvalidDataException("JSONLファイルに有効なJSON行がありません。");
            }

            bool isBatch = documents.Any(doc => EventType(doc.RootElement).Equals("batch_started", StringComparison.OrdinalIgnoreCase));
            return isBatch
                ? LoadHeadlessBatch(path, documents)
                : LoadGuiGameLog(path, documents);
        }
        finally
        {
            foreach (JsonDocument document in documents)
            {
                document.Dispose();
            }
        }
    }

    private static IReadOnlyList<ReplayGame> LoadHeadlessBatch(string path, IReadOnlyList<JsonDocument> documents)
    {
        JsonElement batchStarted = documents
            .Select(doc => doc.RootElement)
            .First(root => EventType(root).Equals("batch_started", StringComparison.OrdinalIgnoreCase));

        JsonElement batchPayload = RequiredProperty(batchStarted, "payload");
        JsonElement options = batchPayload.TryGetProperty("options", out JsonElement optionsElement)
            ? optionsElement
            : default;

        int? legacyRuleMask = ReadInt(batchPayload, "ruleMask")
            ?? (options.ValueKind == JsonValueKind.Object && options.TryGetProperty("rules", out JsonElement rulesElement)
                ? ReadInt(rulesElement, "mask")
                : null);
        ValidateLegacyRuleMask(legacyRuleMask, "batch JSONL");
        RuleSet rules = RuleSet.Standard;

        ScenarioMovePolicy? movePolicy = null;
        GameStartConfiguration startConfiguration;
        if (options.ValueKind == JsonValueKind.Object &&
            options.TryGetProperty("startConfiguration", out JsonElement startElement))
        {
            startConfiguration = ParseStartConfiguration(startElement, Path.GetFileName(path), out movePolicy);
            if (options.TryGetProperty("movePolicy", out JsonElement policyElement) && policyElement.ValueKind == JsonValueKind.Object)
            {
                movePolicy = ParseMovePolicy(policyElement);
            }
        }
        else
        {
            startConfiguration = GameStartConfiguration.Initial;
        }

        var games = new List<ReplayGame>();
        foreach (JsonDocument document in documents)
        {
            JsonElement root = document.RootElement;
            if (!EventType(root).Equals("game_result", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            JsonElement payload = RequiredProperty(root, "payload");
            int gameIndex = ReadInt(payload, "gameIndex") ?? games.Count;
            int? seed = ReadInt(payload, "seed");
            string outcome = ReadString(payload, "outcome") ?? "Unknown";
            string endReason = ReadString(payload, "endReason") ?? "Unknown";
            int plies = ReadInt(payload, "plies") ?? 0;

            if (!payload.TryGetProperty("moves", out JsonElement movesElement) || movesElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    $"game_result #{gameIndex} にmovesがありません。ヘッドレス実行時に「全着手列保存」をONにしたJSONLが必要です。");
            }

            var moves = new List<ReplayMove>(Math.Max(plies, 0));
            foreach (JsonElement moveElement in movesElement.EnumerateArray())
            {
                string raw = moveElement.GetString()
                    ?? throw new InvalidDataException($"game_result #{gameIndex} のmovesに文字列以外の要素があります。");
                moves.Add(ParseHeadlessMove(raw));
            }

            string seedText = seed.HasValue ? $" seed={seed.Value}" : string.Empty;
            games.Add(new ReplayGame
            {
                DisplayName = $"#{gameIndex + 1}{seedText} / {outcome} / {endReason} / {moves.Count}手",
                SourcePath = path,
                Rules = rules,
                StartConfiguration = startConfiguration,
                MovePolicy = movePolicy,
                Moves = moves,
                RecordedOutcome = outcome,
                RecordedEndReason = endReason,
                Seed = seed
            });
        }

        if (games.Count == 0)
        {
            throw new InvalidDataException("batch JSONLにgame_resultがありません。");
        }

        return games;
    }

    private static IReadOnlyList<ReplayGame> LoadGuiGameLog(string path, IReadOnlyList<JsonDocument> documents)
    {
        JsonElement? gameStarted = null;
        foreach (JsonDocument document in documents)
        {
            if (EventType(document.RootElement).Equals("game_started", StringComparison.OrdinalIgnoreCase))
            {
                gameStarted = document.RootElement;
                break;
            }
        }

        GameStartConfiguration startConfiguration = GameStartConfiguration.Initial;
        RuleSet rules = RuleSet.Standard;
        if (gameStarted.HasValue)
        {
            JsonElement payload = RequiredProperty(gameStarted.Value, "payload");
            ValidateLegacyRuleMask(ReadInt(payload, "ruleMask"), "通常対局JSONL");

            if (payload.TryGetProperty("initialState", out JsonElement initialState))
            {
                startConfiguration = ParseSnapshotStartConfiguration(initialState, Path.GetFileName(path));
            }
        }

        var cpuDetails = new Dictionary<int, string>();
        foreach (JsonDocument document in documents)
        {
            JsonElement root = document.RootElement;
            if (!EventType(root).Equals("cpu_decision", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            JsonElement payload = RequiredProperty(root, "payload");
            int? plyBefore = ReadInt(payload, "plyBefore");
            if (!plyBefore.HasValue)
            {
                continue;
            }

            int depth = ReadInt(payload, "completedDepth") ?? 0;
            int requested = ReadInt(payload, "requestedDepth") ?? depth;
            int score = ReadInt(payload, "searchScore") ?? 0;
            long nodes = ReadLong(payload, "nodes") ?? 0;
            cpuDetails[plyBefore.Value + 1] = $"CPU d{depth}/{requested} s{score} n{nodes:N0}";
        }

        var moves = new List<ReplayMove>();
        string outcome = "Ongoing";
        string endReason = "None";
        foreach (JsonDocument document in documents)
        {
            JsonElement root = document.RootElement;
            string eventType = EventType(root);
            JsonElement payload = RequiredProperty(root, "payload");

            if (eventType.Equals("move_applied", StringComparison.OrdinalIgnoreCase))
            {
                int ply = ReadInt(payload, "ply") ?? moves.Count + 1;
                PlayerId player = ParsePlayer(ReadString(payload, "player") ?? throw new InvalidDataException("move_applied.playerがありません。"));
                JsonElement moveObject = RequiredProperty(payload, "move");
                Move move = ParseMoveObject(moveObject);
                string detail = ReadString(payload, "actorDetail")
                    ?? (cpuDetails.TryGetValue(ply, out string? cpu) ? cpu : "HUMAN");
                moves.Add(new ReplayMove(ply, player, move, detail));

                outcome = ReadString(payload, "outcome") ?? outcome;
                endReason = ReadString(payload, "endReason") ?? endReason;
            }
            else if (eventType.Equals("game_ended", StringComparison.OrdinalIgnoreCase))
            {
                outcome = ReadString(payload, "outcome") ?? outcome;
                endReason = ReadString(payload, "endReason") ?? endReason;
            }
        }

        if (moves.Count == 0 && !gameStarted.HasValue)
        {
            throw new InvalidDataException("通常対局JSONLにgame_startedまたはmove_appliedがありません。");
        }

        return new[]
        {
            new ReplayGame
            {
                DisplayName = $"{Path.GetFileName(path)} / {outcome} / {endReason} / {moves.Count}手",
                SourcePath = path,
                Rules = rules,
                StartConfiguration = startConfiguration,
                MovePolicy = null,
                Moves = moves,
                RecordedOutcome = outcome,
                RecordedEndReason = endReason
            }
        };
    }

    private static void ValidateLegacyRuleMask(int? legacyRuleMask, string sourceLabel)
    {
        if (!legacyRuleMask.HasValue)
        {
            return; // v0.2.18+ fixed Standard logs do not contain ruleMask.
        }

        // v0.2.14.1+ Standard-compatible logs used legacy numeric value 24. Other values belong to
        // retired experiments and cannot be reconstructed by the product engine.
        if (legacyRuleMask.Value != 24)
        {
            throw new InvalidDataException(
                $"{sourceLabel} は廃止済み実験ルールの棋譜です (legacy ruleMask={legacyRuleMask.Value})。" +
                " 現行版ではStandardルールの棋譜だけを再構築できます。");
        }
    }

    private static ReplayMove ParseHeadlessMove(string raw)
    {
        string[] parts = raw.Split(':');
        if (parts.Length < 3 || !int.TryParse(parts[0], out int ply))
        {
            throw new InvalidDataException($"ヘッドレス着手形式を解析できません: {raw}");
        }

        PlayerId player = ParsePlayer(parts[1]);
        Move move = ParseMoveNotation(parts[2]);
        string detail = parts.Length > 3 ? string.Join(" ", parts.Skip(3)) : string.Empty;
        return new ReplayMove(ply, player, move, detail);
    }

    private static Move ParseMoveObject(JsonElement moveObject)
    {
        string? from = ReadString(moveObject, "from");
        string? to = ReadString(moveObject, "to");
        string? kind = ReadString(moveObject, "kind");
        if (from is not null && to is not null)
        {
            MoveKind moveKind = kind?.Equals("Sacrifice", StringComparison.OrdinalIgnoreCase) == true
                ? MoveKind.Sacrifice
                : MoveKind.Normal;
            return new Move(ParseCoordinate(from), ParseCoordinate(to), moveKind);
        }

        string notation = ReadString(moveObject, "notation")
            ?? throw new InvalidDataException("moveにfrom/toまたはnotationがありません。");
        return ParseMoveNotation(notation);
    }

    private static Move ParseMoveNotation(string notation)
    {
        Match match = MoveNotationRegex.Match(notation.Trim());
        if (!match.Success)
        {
            throw new InvalidDataException($"着手表記を解析できません: {notation}");
        }

        Position from = ParseCoordinate(match.Groups["from"].Value);
        Position to = ParseCoordinate(match.Groups["to"].Value);
        MoveKind kind = match.Groups["sep"].Value.Equals("x", StringComparison.OrdinalIgnoreCase)
            ? MoveKind.Sacrifice
            : MoveKind.Normal;
        return new Move(from, to, kind);
    }

    private static GameStartConfiguration ParseStartConfiguration(
        JsonElement element,
        string sourceName,
        out ScenarioMovePolicy? movePolicy)
    {
        string name = ReadString(element, "name") ?? "Replay";
        string source = ReadString(element, "sourceName") ?? sourceName;
        string[] rows = ReadBoardRows(element, "boardRows") ?? GameStartConfiguration.Initial.CopyBoardRows();
        PlayerId current = ParsePlayer(ReadString(element, "currentPlayer") ?? "P1");
        PlayerStrategyConstraint p1 = element.TryGetProperty("p1Strategy", out JsonElement p1Element)
            ? ParseStrategy(p1Element)
            : PlayerStrategyConstraint.Free;
        PlayerStrategyConstraint p2 = element.TryGetProperty("p2Strategy", out JsonElement p2Element)
            ? ParseStrategy(p2Element)
            : PlayerStrategyConstraint.Free;
        movePolicy = new ScenarioMovePolicy(p1, p2);
        if (!movePolicy.IsActive) movePolicy = null;
        return GameStartConfiguration.Create(name, source, rows, current);
    }

    private static ScenarioMovePolicy? ParseMovePolicy(JsonElement element)
    {
        PlayerStrategyConstraint p1 = element.TryGetProperty("p1", out JsonElement p1Element)
            ? ParseStrategy(p1Element)
            : PlayerStrategyConstraint.Free;
        PlayerStrategyConstraint p2 = element.TryGetProperty("p2", out JsonElement p2Element)
            ? ParseStrategy(p2Element)
            : PlayerStrategyConstraint.Free;
        var policy = new ScenarioMovePolicy(p1, p2);
        return policy.IsActive ? policy : null;
    }

    private static GameStartConfiguration ParseSnapshotStartConfiguration(JsonElement initialState, string sourceName)
    {
        string[] rows = ParseSnapshotBoard(initialState);
        PlayerId current = ParsePlayer(ReadString(initialState, "currentPlayer") ?? "P1");
        return GameStartConfiguration.Create(
            "GUI log replay",
            sourceName,
            rows,
            current);
    }

    private static string[] ParseSnapshotBoard(JsonElement state)
    {
        if (!state.TryGetProperty("board", out JsonElement board) || board.ValueKind != JsonValueKind.Array)
        {
            return GameStartConfiguration.Initial.CopyBoardRows();
        }

        var rows = new List<string>(GameEngine.BoardSize);
        foreach (JsonElement rowElement in board.EnumerateArray())
        {
            if (rowElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("initialState.boardの行形式が不正です。");
            }

            var chars = new List<char>(GameEngine.BoardSize);
            foreach (JsonElement cell in rowElement.EnumerateArray())
            {
                string token = cell.GetString() ?? ".";
                chars.Add(token switch
                {
                    "P1S" => 'S',
                    "P1O" => 'O',
                    "P2S" => 's',
                    "P2O" => 'o',
                    "." => '.',
                    _ => throw new InvalidDataException($"initialState.boardの駒表記が不明です: {token}")
                });
            }
            rows.Add(new string(chars.ToArray()));
        }

        return rows.ToArray();
    }

    private static PlayerStrategyConstraint ParseStrategy(JsonElement element)
    {
        string mode = ReadString(element, "mode") ?? "Free";
        if (mode.Equals("Free", StringComparison.OrdinalIgnoreCase))
        {
            return PlayerStrategyConstraint.Free;
        }
        if (!mode.Equals("RushOne", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"未対応のStrategyです: {mode}");
        }

        if (!element.TryGetProperty("attackBlockerPosition", out JsonElement attack) || attack.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("RushOne棋譜にattackBlockerPositionがありません。");
        }

        int row = ReadInt(attack, "row") ?? throw new InvalidDataException("attackBlockerPosition.rowがありません。");
        int col = ReadInt(attack, "col") ?? throw new InvalidDataException("attackBlockerPosition.colがありません。");
        int budget = ReadInt(element, "deviationBudget") ?? 0;
        return new PlayerStrategyConstraint(StrategyMode.RushOne, new Position(row, col), budget);
    }

    private static string[]? ReadBoardRows(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement rowsElement) || rowsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        return rowsElement.EnumerateArray().Select(row => row.GetString() ?? string.Empty).ToArray();
    }

    private static Position ParseCoordinate(string value) => ScenarioPlayerStrategy.ParseCoordinate(value);

    private static PlayerId ParsePlayer(string value)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "P1" or "PLAYER1" or "1" => PlayerId.Player1,
            "P2" or "PLAYER2" or "2" => PlayerId.Player2,
            _ => throw new InvalidDataException($"P1/P2として解析できません: {value}")
        };
    }

    private static string EventType(JsonElement root) => ReadString(root, "eventType") ?? string.Empty;

    private static JsonElement RequiredProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            throw new InvalidDataException($"JSONLに必須プロパティ'{name}'がありません。");
        }
        return value;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
        {
            return number;
        }
        return int.TryParse(value.ToString(), out number) ? number : null;
    }

    private static long? ReadLong(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
        {
            return number;
        }
        return long.TryParse(value.ToString(), out number) ? number : null;
    }
}
