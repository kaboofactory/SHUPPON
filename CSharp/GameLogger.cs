using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StarRunnerPrototype;

public sealed class GameLogger : IDisposable
{
    public const int MaxLogFiles = 30;

    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public string GameId { get; }
    public string FilePath { get; }
    public string LogDirectory { get; }

    private GameLogger(string gameId, string filePath, string logDirectory)
    {
        GameId = gameId;
        FilePath = filePath;
        LogDirectory = logDirectory;

        var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }

    public static GameLogger Create(
        string mode,
        string p1CpuSkill,
        string p2CpuSkill,
        long p1MaxNodes,
        long p2MaxNodes,
        int searchParallelism,
        GameEngine initialState)
    {
        string directory = ResolveLogDirectory();
        Directory.CreateDirectory(directory);
        // Make room first so creating a new log never intentionally pushes retention past 30.
        RotateLogs(directory, MaxLogFiles - 1);

        string gameId = Guid.NewGuid().ToString("N");
        string fileName = $"game_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{gameId[..8]}.jsonl";
        string path = Path.Combine(directory, fileName);
        var logger = new GameLogger(gameId, path, directory);
        RotateLogs(directory, MaxLogFiles);

        logger.Write("game_started", new
        {
            appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
            mode,
            ai = new
            {
                skill = new { p1 = p1CpuSkill, p2 = p2CpuSkill },
                commonDepthCap = CpuSkillProfile.SearchDepthCap,
                maxNodes = new { p1 = p1MaxNodes, p2 = p2MaxNodes },
                timeLimitMs = 0,
                searchParallelism,
                threadPriority = "BelowNormal",
                strengthModel = "fixed MaxNodes table: 15kyu=N200, each higher level x1.80; D99 safety depth ceiling; no skill calibration/randomness",
                evaluationProfile = new { name = CpuEvaluationProfileProvider.Current.Name, source = CpuEvaluationProfileStorage.CurrentSource },
                engine = "iterative deepening alpha-beta + PVS + mate-distance pruning + StarRunner LMR + adaptive root only-survival deepening + budget-aware mate-distance scout + transposition table + killer/history move ordering + apply/undo search + parallel root split"
            },
            rulesId = RuleSet.Standard.Id,
            boardSize = GameEngine.BoardSize,
            coordinates = "A1 is top-left; P1 starts at bottom and moves toward row 1; P2 starts at top and moves toward row 8.",
            rules = new
            {
                blockerNormalMove = "8 adjacent directions, one square, empty destination only",
                runnerNormalMove = "4 orthogonal directions, one square, empty destination only",
                runnerSacrificeMove = "8 adjacent directions onto own blocker; blocker is removed",
                enemyCapture = false,
                goal = "Runner reaches opponent final row",
                immobilizedRunner = "Loss at start of own turn if Runner has no legal move",
                immediateBacktrack = "Normally forbidden for the piece moved on the previous own turn; allowed only when that piece has no other legal move",
                repetition = "Same physical board placement only, regardless of side to move or previous-move history, 4 occurrences = draw"
            },
            initialState = Snapshot(initialState)
        });

        logger.WriteTurnStart(initialState);
        return logger;
    }

    public void WriteTurnStart(GameEngine state)
    {
        if (state.Outcome != GameOutcome.Ongoing)
        {
            return;
        }

        Write("turn_start", new
        {
            ply = state.PlyCount,
            currentPlayer = state.CurrentPlayer.ShortName(),
            legalMoves = state.GetLegalMoves().Count,
            runnerLegalMoves = state.GetRunnerLegalMoves(state.CurrentPlayer).Count,
            repetitionCount = state.CurrentPositionRepetitionCount(),
            state = Snapshot(state)
        });
    }

    public void WriteHumanMove(PlayerId player, Move move, GameEngine before)
    {
        Write("human_move_selected", new
        {
            plyBefore = before.PlyCount,
            player = player.ShortName(),
            move = MoveData(move),
            stateBefore = Snapshot(before)
        });
    }

    public void WriteCpuDecision(PlayerId player, CpuSkillProfile skill, CpuDecision decision, GameEngine before)
    {
        Write("cpu_decision", new
        {
            plyBefore = before.PlyCount,
            player = player.ShortName(),
            skill = skill.Name,
            maxNodes = skill.MaxNodes,
            completedDepth = decision.Depth,
            requestedDepth = decision.RequestedDepth,
            chosenMove = MoveData(decision.Move),
            searchScore = decision.Score,
            nodes = decision.Nodes,
            transpositionHits = decision.TranspositionHits,
            betaCutoffs = decision.BetaCutoffs,
            searchSelectivity = decision.SearchTelemetry,
            timedOut = decision.TimedOut,
            nodeLimitReached = decision.NodeLimitReached,
            nodesPerSecond = decision.NodesPerSecond,
            elapsedMs = decision.ElapsedMilliseconds,
            rootPreference = new
            {
                scoreWindow = decision.PreferenceScoreWindow,
                cycleAvoidanceApplied = decision.CycleAvoidanceApplied,
                runnerOscillationAvoidanceApplied = decision.RunnerOscillationAvoidanceApplied,
                runnerAdvancePreferenceApplied = decision.RunnerAdvancePreferenceApplied,
                runnerReturnCandidatePresent = decision.RunnerReturnCandidatePresent,
                selectedRunnerReturnMove = decision.SelectedRunnerReturnMove,
                scoreConcession = decision.PreferenceScoreConcession,
                strictBestScore = decision.StrictBestScore,
                selectedPhysicalHistoryCount = decision.SelectedPhysicalHistoryCount,
                strictBestPhysicalHistoryCount = decision.StrictBestPhysicalHistoryCount,
                selectedRunnerForwardDelta = decision.SelectedRunnerForwardDelta,
                strictBestRunnerForwardDelta = decision.StrictBestRunnerForwardDelta
            },
            staticEvaluationAfterChosenMove = decision.StaticEvaluationAfterMove,
            candidates = decision.Candidates.Select(c => new
            {
                move = MoveData(c.Move),
                searchScore = c.SearchScore,
                bound = c.Bound
            }).ToArray(),
            stateBefore = Snapshot(before)
        });
    }

    public void WriteMoveApplied(PlayerId player, Move move, GameEngine after, string? actorDetail = null)
    {
        Write("move_applied", new
        {
            ply = after.PlyCount,
            player = player.ShortName(),
            move = MoveData(move),
            sacrifice = move.Kind == MoveKind.Sacrifice,
            actorDetail,
            blockersRemaining = new
            {
                p1 = after.CountBlockers(PlayerId.Player1),
                p2 = after.CountBlockers(PlayerId.Player2)
            },
            outcome = after.Outcome.ToString(),
            endReason = after.EndReason.ToString(),
            stateAfter = Snapshot(after)
        });

        if (after.Outcome == GameOutcome.Ongoing)
        {
            WriteTurnStart(after);
        }
        else
        {
            WriteGameEnded(after);
        }
    }

    public void WriteGameEnded(GameEngine state)
    {
        Write("game_ended", new
        {
            ply = state.PlyCount,
            outcome = state.Outcome.ToString(),
            endReason = state.EndReason.ToString(),
            blockersRemaining = new
            {
                p1 = state.CountBlockers(PlayerId.Player1),
                p2 = state.CountBlockers(PlayerId.Player2)
            },
            finalState = Snapshot(state)
        });
    }

    public void WriteSessionStopped(GameEngine state, string reason)
    {
        Write("session_stopped", new
        {
            reason,
            ply = state.PlyCount,
            outcome = state.Outcome.ToString(),
            state = Snapshot(state)
        });
    }


    public void SaveCopyAs(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("保存先ファイル名が空です。", nameof(destinationPath));
        }

        string fullDestination = Path.GetFullPath(destinationPath);
        string? directory = Path.GetDirectoryName(fullDestination);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        lock (_gate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GameLogger));
            }

            _writer.Flush();
            string source = Path.GetFullPath(FilePath);
            if (string.Equals(source, fullDestination, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            File.Copy(source, fullDestination, overwrite: true);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }

        RotateLogs(LogDirectory, MaxLogFiles);
    }

    private void Write(string eventType, object payload)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var envelope = new
            {
                schemaVersion = 3,
                timestampUtc = DateTimeOffset.UtcNow,
                timestampLocal = DateTimeOffset.Now,
                gameId = GameId,
                eventType,
                payload
            };

            _writer.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
        }
    }

    private static object Snapshot(GameEngine state)
    {
        Position p1Runner = state.FindRunner(PlayerId.Player1);
        Position p2Runner = state.FindRunner(PlayerId.Player2);
        return new
        {
            currentPlayer = state.CurrentPlayer.ShortName(),
            ply = state.PlyCount,
            outcome = state.Outcome.ToString(),
            endReason = state.EndReason.ToString(),
            runners = new
            {
                p1 = p1Runner.ToCoordinate(),
                p2 = p2Runner.ToCoordinate()
            },
            blockers = new
            {
                p1 = state.CountBlockers(PlayerId.Player1),
                p2 = state.CountBlockers(PlayerId.Player2)
            },
            board = state.ToLogBoard(),
            positionKey = state.GetPositionKey()
        };
    }

    private static object MoveData(Move move) => new
    {
        notation = move.ToNotation(),
        from = move.From.ToCoordinate(),
        to = move.To.ToCoordinate(),
        kind = move.Kind.ToString()
    };

    private static string ResolveLogDirectory()
    {
        string besideExe = Path.Combine(AppContext.BaseDirectory, "logs");
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
                "logs");
        }
    }

    private static void RotateLogs(string directory, int maxFiles)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            FileInfo[] files = new DirectoryInfo(directory)
                .GetFiles("game_*.jsonl", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.CreationTimeUtc)
                .ThenBy(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.Name, StringComparer.Ordinal)
                .ToArray();

            int excess = files.Length - Math.Max(0, maxFiles);
            for (int i = 0; i < excess; i++)
            {
                try
                {
                    files[i].Delete();
                }
                catch
                {
                    // Logging must never make the game unplayable.
                }
            }
        }
        catch
        {
            // Logging retention is best-effort; gameplay continues if cleanup fails.
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
