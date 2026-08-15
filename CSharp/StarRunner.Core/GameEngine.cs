using System.Numerics;
using System.Text;

namespace StarRunner.Core;

public sealed class GameEngine
{
    public const int BoardSize = 8;
    public const int MaxLegalMoves = 64;

    private static readonly (int dr, int dc)[] EightDirections =
    {
        (-1, -1), (-1, 0), (-1, 1),
        (0, -1),            (0, 1),
        (1, -1),  (1, 0),  (1, 1)
    };

    private static readonly (int dr, int dc)[] OrthogonalDirections =
    {
        (-1, 0), (0, -1), (0, 1), (1, 0)
    };

    private const ulong SideToMoveHash = 0xD1B54A32D192ED03UL;

    private readonly Piece?[,] _board;
    private readonly Dictionary<ulong, int> _positionCounts;
    // CPU cycle-breaking heuristic only: counts historical occurrences of the
    // physical board + side-to-move, intentionally ignoring immediate-backtrack history.
    // This is not part of the game rules and is never used for repetition draws.
    private readonly Dictionary<ulong, int> _physicalPositionCounts;
    // 8x8 = 64 squares, so the hot-path occupancy state fits exactly in ulong bitboards.
    // _board remains the authoritative piece-type view for UI/logging/replay compatibility;
    // these bitboards are maintained in lockstep for search/move-generation/evaluation speed.
    private ulong _p1BlockerBits;
    private ulong _p2BlockerBits;
    private ulong _p1RunnerBit;
    private ulong _p2RunnerBit;
    private ulong _positionHash;
    private ulong _historyHash;
    private ulong _backtrackStateHash;
    private readonly IGameMovePolicy? _movePolicy;
    private ulong _movePolicyState;
    private Position _p1RunnerPosition;
    private Position _p2RunnerPosition;
    private Move? _p1LastOwnMove;
    private Move? _p2LastOwnMove;
    // AI root-ordering heuristic only: last Runner move made in the real game.
    // Search moves deliberately do not mutate these fields. This lets the AI spot
    // D5-C5 ... C5-D5 style Runner oscillation even when blocker moves occur between them.
    private Move? _p1LastRealRunnerMove;
    private Move? _p2LastRealRunnerMove;

    public PlayerId CurrentPlayer { get; private set; }
    public GameOutcome Outcome { get; private set; }
    public EndReason EndReason { get; private set; }
    public int PlyCount { get; private set; }
    public Move? LastMove { get; private set; }
    public RuleSet Rules { get; }
    public GameStartConfiguration StartConfiguration { get; }
    public IGameMovePolicy? MovePolicy => _movePolicy;
    public ulong MovePolicyState => _movePolicyState;

    public GameEngine() : this(null, null)
    {
    }

    public GameEngine(GameStartConfiguration? startConfiguration) : this(startConfiguration, null)
    {
    }

    public GameEngine(GameStartConfiguration? startConfiguration, IGameMovePolicy? movePolicy)
    {
        Rules = RuleSet.Standard;
        StartConfiguration = startConfiguration ?? GameStartConfiguration.Initial;
        _movePolicy = movePolicy;
        _board = new Piece?[BoardSize, BoardSize];
        _positionCounts = new Dictionary<ulong, int>(256);
        _physicalPositionCounts = new Dictionary<ulong, int>(256);
        ResetToStartConfiguration();
    }

    private GameEngine(GameEngine other)
    {
        Rules = RuleSet.Standard;
        StartConfiguration = other.StartConfiguration;
        _movePolicy = other._movePolicy;
        _board = new Piece?[BoardSize, BoardSize];
        for (int row = 0; row < BoardSize; row++)
        {
            for (int col = 0; col < BoardSize; col++)
            {
                _board[row, col] = other._board[row, col];
            }
        }

        _positionCounts = new Dictionary<ulong, int>(other._positionCounts.Count + 64);
        foreach (KeyValuePair<ulong, int> pair in other._positionCounts)
        {
            _positionCounts.Add(pair.Key, pair.Value);
        }
        _physicalPositionCounts = new Dictionary<ulong, int>(other._physicalPositionCounts.Count + 64);
        foreach (KeyValuePair<ulong, int> pair in other._physicalPositionCounts)
        {
            _physicalPositionCounts.Add(pair.Key, pair.Value);
        }
        _p1BlockerBits = other._p1BlockerBits;
        _p2BlockerBits = other._p2BlockerBits;
        _p1RunnerBit = other._p1RunnerBit;
        _p2RunnerBit = other._p2RunnerBit;
        _positionHash = other._positionHash;
        _historyHash = other._historyHash;
        _backtrackStateHash = other._backtrackStateHash;
        _movePolicyState = other._movePolicyState;
        _p1RunnerPosition = other._p1RunnerPosition;
        _p2RunnerPosition = other._p2RunnerPosition;
        _p1LastOwnMove = other._p1LastOwnMove;
        _p2LastOwnMove = other._p2LastOwnMove;
        _p1LastRealRunnerMove = other._p1LastRealRunnerMove;
        _p2LastRealRunnerMove = other._p2LastRealRunnerMove;
        CurrentPlayer = other.CurrentPlayer;
        Outcome = other.Outcome;
        EndReason = other.EndReason;
        PlyCount = other.PlyCount;
        LastMove = other.LastMove;
    }

    public GameEngine Clone() => new(this);

    /// <summary>Returns a defensive copy of the current board in the standard . S O s o format.</summary>
    public string[] GetBoardRows()
    {
        var rows = new string[BoardSize];
        for (int row = 0; row < BoardSize; row++)
        {
            var chars = new char[BoardSize];
            for (int col = 0; col < BoardSize; col++)
            {
                chars[col] = _board[row, col] switch
                {
                    null => '.',
                    { Owner: PlayerId.Player1, Type: PieceType.Runner } => 'S',
                    { Owner: PlayerId.Player1, Type: PieceType.Blocker } => 'O',
                    { Owner: PlayerId.Player2, Type: PieceType.Runner } => 's',
                    { Owner: PlayerId.Player2, Type: PieceType.Blocker } => 'o',
                    _ => throw new InvalidOperationException("Unknown piece state.")
                };
            }
            rows[row] = new string(chars);
        }
        return rows;
    }

    /// <summary>
    /// Exports every rule-relevant history component required for exact resume.
    /// This is intentionally more complete than a board-only snapshot.
    /// </summary>
    public GameState ExportState() => new(
        GameState.CurrentSchemaVersion,
        StartConfiguration.Name,
        StartConfiguration.SourceName,
        StartConfiguration.CopyBoardRows(),
        StartConfiguration.CurrentPlayer,
        GetBoardRows(),
        CurrentPlayer,
        Outcome,
        EndReason,
        PlyCount,
        LastMove,
        _p1LastOwnMove,
        _p2LastOwnMove,
        _p1LastRealRunnerMove,
        _p2LastRealRunnerMove,
        new Dictionary<ulong, int>(_positionCounts),
        new Dictionary<ulong, int>(_physicalPositionCounts),
        _movePolicy?.Id,
        _movePolicyState);

    /// <summary>
    /// Restores a previously exported game. If the snapshot used a host move policy,
    /// the caller must supply a policy with the same Id.
    /// </summary>
    public static GameEngine FromState(GameState state, IGameMovePolicy? movePolicy = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion < 1 || state.SchemaVersion > GameState.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported game-state schema {state.SchemaVersion}; supported range is 1..{GameState.CurrentSchemaVersion}.");
        }

        string? suppliedPolicyId = movePolicy?.Id;
        if (!string.Equals(state.MovePolicyId, suppliedPolicyId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Move policy mismatch. Snapshot='{state.MovePolicyId ?? "<none>"}', supplied='{suppliedPolicyId ?? "<none>"}'.");
        }
        if (state.MovePolicyId is null && state.MovePolicyState != 0)
        {
            throw new InvalidDataException("MovePolicyState must be zero when no move policy is present.");
        }
        if (state.PlyCount < 0)
        {
            throw new InvalidDataException("PlyCount must not be negative.");
        }
        if (!Enum.IsDefined(typeof(GameOutcome), state.Outcome) || !Enum.IsDefined(typeof(EndReason), state.EndReason))
        {
            throw new InvalidDataException("GameState contains an unsupported outcome/end-reason value.");
        }

        var start = GameStartConfiguration.Create(
            state.StartName,
            state.StartSourceName,
            state.StartBoardRows ?? throw new InvalidDataException("StartBoardRows is missing."),
            state.StartCurrentPlayer);

        var game = new GameEngine(start, movePolicy);
        game.RestoreExportedState(state);
        return game;
    }

    private void RestoreExportedState(GameState state)
    {
        GameStartConfiguration currentBoard = GameStartConfiguration.Create(
            "Restored current board",
            "game-state",
            state.BoardRows ?? throw new InvalidDataException("BoardRows is missing."),
            state.CurrentPlayer);

        Array.Clear(_board, 0, _board.Length);
        _p1BlockerBits = 0;
        _p2BlockerBits = 0;
        _p1RunnerBit = 0;
        _p2RunnerBit = 0;
        Position? p1Runner = null;
        Position? p2Runner = null;

        for (int row = 0; row < BoardSize; row++)
        {
            for (int col = 0; col < BoardSize; col++)
            {
                var position = new Position(row, col);
                Piece? piece = currentBoard.GetPiece(position);
                _board[row, col] = piece;
                if (piece is not { } value) continue;

                ulong bit = SquareBit(position);
                if (value.Type == PieceType.Runner)
                {
                    if (value.Owner == PlayerId.Player1)
                    {
                        p1Runner = position;
                        _p1RunnerBit = bit;
                    }
                    else
                    {
                        p2Runner = position;
                        _p2RunnerBit = bit;
                    }
                }
                else if (value.Owner == PlayerId.Player1)
                {
                    _p1BlockerBits |= bit;
                }
                else
                {
                    _p2BlockerBits |= bit;
                }
            }
        }

        _p1RunnerPosition = p1Runner ?? throw new InvalidDataException("Restored state has no P1 runner.");
        _p2RunnerPosition = p2Runner ?? throw new InvalidDataException("Restored state has no P2 runner.");
        CurrentPlayer = state.CurrentPlayer;
        Outcome = state.Outcome;
        EndReason = state.EndReason;
        PlyCount = state.PlyCount;
        LastMove = state.LastMove;
        _p1LastOwnMove = state.Player1LastOwnMove;
        _p2LastOwnMove = state.Player2LastOwnMove;
        // Schema v1 snapshots predate this AI-only history and therefore restore it as null.
        _p1LastRealRunnerMove = state.Player1LastRealRunnerMove;
        _p2LastRealRunnerMove = state.Player2LastRealRunnerMove;
        _positionHash = ComputePositionHash();
        _backtrackStateHash = ComputeBacktrackStateHash();
        _movePolicyState = state.MovePolicyState;

        _positionCounts.Clear();
        foreach (KeyValuePair<ulong, int> pair in state.RepetitionCounts ?? new Dictionary<ulong, int>())
        {
            if (pair.Value <= 0) throw new InvalidDataException("Repetition counts must be positive.");
            _positionCounts[pair.Key] = pair.Value;
        }
        if (_positionCounts.Count == 0)
        {
            throw new InvalidDataException("RepetitionCounts is empty; exact resume would be unsafe.");
        }

        _historyHash = 0;
        foreach (KeyValuePair<ulong, int> pair in _positionCounts)
        {
            _historyHash ^= OccurrenceHash(pair.Key, pair.Value);
        }

        _physicalPositionCounts.Clear();
        foreach (KeyValuePair<ulong, int> pair in state.PhysicalPositionCounts ?? new Dictionary<ulong, int>())
        {
            if (pair.Value <= 0) throw new InvalidDataException("Physical position counts must be positive.");
            _physicalPositionCounts[pair.Key] = pair.Value;
        }
        if (_physicalPositionCounts.Count == 0)
        {
            throw new InvalidDataException("PhysicalPositionCounts is empty; exact AI resume would be unsafe.");
        }
    }

    public Piece? GetPiece(Position position)
    {
        if (!position.IsInside)
        {
            return null;
        }

        return _board[position.Row, position.Col];
    }

    public IReadOnlyList<Move> GetLegalMoves() => GetLegalMoves(CurrentPlayer);

    public IReadOnlyList<Move> GetLegalMoves(PlayerId player)
    {
        Span<Move> buffer = stackalloc Move[MaxLegalMoves];
        int count = GenerateLegalMoves(player, buffer);
        return count == 0 ? Array.Empty<Move>() : buffer[..count].ToArray();
    }

    // Allocation-free legal move generation for the recursive CPU search hot path.
    // The public GetLegalMoves API remains available for UI/logging/replay callers.
    internal int GenerateLegalMoves(PlayerId player, Span<Move> destination)
    {
        if (Outcome != GameOutcome.Ongoing)
        {
            return 0;
        }
        if (destination.Length < MaxLegalMoves)
        {
            throw new ArgumentException($"Move buffer must have room for at least {MaxLegalMoves} moves.", nameof(destination));
        }

        int count = 0;

        // If the Runner is orthogonally surrounded by four opponent pieces, this turn is
        // a mandatory retreat: only Runner moves are legal. Normal Runner moves are
        // physically blocked in this state, so AddRunnerMoves yields only sacrifice retreats.
        if (IsRunnerForcedToRetreat(player))
        {
            AddRunnerMoves(FindRunner(player), player, destination, ref count, applyMovePolicy: true);
            return count;
        }

        ulong remaining = GetPlayerPieceBits(player);
        ulong runnerBit = GetRunnerBit(player);

        // TrailingZeroCount enumerates 0..63, matching the previous row-major board scan.
        // Preserving this order keeps deterministic tie-break/fallback behavior stable.
        while (remaining != 0)
        {
            int square = BitOperations.TrailingZeroCount(remaining);
            ulong bit = 1UL << square;
            remaining &= remaining - 1;

            var from = new Position(square >> 3, square & 7);
            if ((runnerBit & bit) != 0)
            {
                AddRunnerMoves(from, player, destination, ref count, applyMovePolicy: true);
            }
            else
            {
                AddBlockerMoves(from, player, destination, ref count);
            }
        }

        return count;
    }

    public IReadOnlyList<Move> GetRunnerLegalMoves(PlayerId player) =>
        GetRunnerLegalMovesCore(player, applyMovePolicy: true);

    private IReadOnlyList<Move> GetRunnerRuleLegalMoves(PlayerId player) =>
        GetRunnerLegalMovesCore(player, applyMovePolicy: false);

    private IReadOnlyList<Move> GetRunnerLegalMovesCore(PlayerId player, bool applyMovePolicy)
    {
        if (Outcome != GameOutcome.Ongoing)
        {
            return Array.Empty<Move>();
        }

        Span<Move> buffer = stackalloc Move[MaxLegalMoves];
        int count = 0;
        Position runner = FindRunner(player);
        AddRunnerMoves(runner, player, buffer, ref count, applyMovePolicy);
        return count == 0 ? Array.Empty<Move>() : buffer[..count].ToArray();
    }

    public int CountRunnerNormalMoves(PlayerId player) =>
        CountRunnerMovesAggregate(player, applyMovePolicy: true).Normal;

    public int CountRunnerSacrificeMoves(PlayerId player) =>
        CountRunnerMovesAggregate(player, applyMovePolicy: true).Sacrifice;

    public int CountImmediateGoalMoves(PlayerId player) =>
        CountRunnerMovesAggregate(player, applyMovePolicy: true).ImmediateGoal;

    internal RunnerMoveCounts GetRunnerMoveCountsForEvaluation(PlayerId player) =>
        CountRunnerMovesAggregate(player, applyMovePolicy: true);

    public bool IsRunnerFrontMarked(PlayerId runnerOwner)
    {
        Position runner = FindRunner(runnerOwner);
        int dr = runnerOwner == PlayerId.Player1 ? -1 : 1;
        var front = new Position(runner.Row + dr, runner.Col);
        return front.IsInside && IsBlockerAt(runnerOwner.Opponent(), front);
    }

    // A Runner is in mandatory-retreat state only when all four orthogonal adjacent
    // squares exist on the board and are occupied by opponent pieces. Board edges do
    // not count as opponent pieces; this matches the rule wording "surrounded on four sides".
    internal bool IsRunnerForcedToRetreat(PlayerId player)
    {
        Position runner = FindRunner(player);
        ulong opponentPieces = GetPlayerPieceBits(player.Opponent());

        foreach (var (dr, dc) in OrthogonalDirections)
        {
            var adjacent = new Position(runner.Row + dr, runner.Col + dc);
            if (!adjacent.IsInside || !IsBitSet(opponentPieces, adjacent))
            {
                return false;
            }
        }

        return true;
    }

    public bool IsLegalMove(Move move)
    {
        Span<Move> buffer = stackalloc Move[MaxLegalMoves];
        int count = GenerateLegalMoves(CurrentPlayer, buffer);
        for (int i = 0; i < count; i++)
        {
            if (buffer[i] == move) return true;
        }
        return false;
    }

    public bool TryApplyMove(Move move, out string? error)
    {
        error = null;
        if (Outcome != GameOutcome.Ongoing)
        {
            error = "Game is already over.";
            return false;
        }

        if (!IsLegalMove(move))
        {
            error = $"Illegal move: {move.ToNotation()}";
            return false;
        }

        ApplyGeneratedMove(move);
        return true;
    }

    internal SearchUndo ApplyGeneratedMoveForSearch(Move move)
    {
        if (Outcome != GameOutcome.Ongoing)
        {
            throw new InvalidOperationException("Cannot search-move after game end.");
        }

        PlayerId mover = CurrentPlayer;
        Piece movingPiece = new(
            mover,
            move.From == FindRunner(mover) ? PieceType.Runner : PieceType.Blocker);
        Piece? targetPiece = move.Kind == MoveKind.Sacrifice
            ? new Piece(mover, PieceType.Blocker)
            : null;

        var undo = new SearchUndo(
            move,
            movingPiece,
            targetPiece,
            CurrentPlayer,
            Outcome,
            EndReason,
            PlyCount,
            LastMove,
            _p1BlockerBits,
            _p2BlockerBits,
            _p1RunnerBit,
            _p2RunnerBit,
            _positionHash,
            _historyHash,
            _backtrackStateHash,
            _movePolicyState,
            _p1LastOwnMove,
            _p2LastOwnMove,
            RepetitionUpdated: false,
            RepetitionHash: 0,
            RepetitionPreviousCount: 0);

        ApplyGeneratedMoveCore(move, movingPiece, targetPiece, ref undo);
        return undo;
    }

    internal void UndoSearchMove(SearchUndo undo)
    {
        if (undo.RepetitionUpdated)
        {
            if (undo.RepetitionPreviousCount <= 0)
            {
                _positionCounts.Remove(undo.RepetitionHash);
            }
            else
            {
                _positionCounts[undo.RepetitionHash] = undo.RepetitionPreviousCount;
            }
        }

        _board[undo.Move.From.Row, undo.Move.From.Col] = undo.MovingPiece;
        _board[undo.Move.To.Row, undo.Move.To.Col] = undo.TargetPiece;
        if (undo.MovingPiece.Type == PieceType.Runner)
        {
            if (undo.MovingPiece.Owner == PlayerId.Player1) _p1RunnerPosition = undo.Move.From;
            else _p2RunnerPosition = undo.Move.From;
        }
        CurrentPlayer = undo.PreviousPlayer;
        Outcome = undo.PreviousOutcome;
        EndReason = undo.PreviousEndReason;
        PlyCount = undo.PreviousPlyCount;
        LastMove = undo.PreviousLastMove;
        _p1BlockerBits = undo.PreviousP1BlockerBits;
        _p2BlockerBits = undo.PreviousP2BlockerBits;
        _p1RunnerBit = undo.PreviousP1RunnerBit;
        _p2RunnerBit = undo.PreviousP2RunnerBit;
        _positionHash = undo.PreviousPositionHash;
        _historyHash = undo.PreviousHistoryHash;
        _backtrackStateHash = undo.PreviousBacktrackStateHash;
        _movePolicyState = undo.PreviousMovePolicyState;
        _p1LastOwnMove = undo.PreviousP1LastOwnMove;
        _p2LastOwnMove = undo.PreviousP2LastOwnMove;
    }

    internal void ApplyGeneratedMove(Move move)
    {
        Piece movingPiece = _board[move.From.Row, move.From.Col]
            ?? throw new InvalidOperationException("Generated move has no source piece.");
        Piece? targetPiece = _board[move.To.Row, move.To.Col];
        SearchUndo ignored = default;
        ApplyGeneratedMoveCore(move, movingPiece, targetPiece, ref ignored, captureUndo: false);
    }

    public Position FindRunner(PlayerId player) =>
        player == PlayerId.Player1 ? _p1RunnerPosition : _p2RunnerPosition;

    /// <summary>
    /// AI helper: true when <paramref name="move"/> reverses the player's most recent
    /// Runner move from the real game. Blocker moves in between do not erase this history.
    /// Search moves never update the remembered real-game Runner move.
    /// </summary>
    internal bool IsRealRunnerReturnMove(PlayerId player, Move move)
    {
        if (move.From != FindRunner(player))
        {
            return false;
        }

        Move? previous = player == PlayerId.Player1
            ? _p1LastRealRunnerMove
            : _p2LastRealRunnerMove;
        return previous is { } last && last.To == move.From && last.From == move.To;
    }

    internal Move? GetLastRealRunnerMove(PlayerId player) =>
        player == PlayerId.Player1 ? _p1LastRealRunnerMove : _p2LastRealRunnerMove;

    public int CountBlockers(PlayerId player) => BitOperations.PopCount(GetBlockerBits(player));

    public int CountPiecesOnRow(PlayerId player, int row)
    {
        if ((uint)row >= BoardSize) return 0;
        return BitOperations.PopCount(GetPlayerPieceBits(player) & RowMask(row));
    }

    public int CountBlockersOnRow(PlayerId player, int row)
    {
        if ((uint)row >= BoardSize) return 0;
        return BitOperations.PopCount(GetBlockerBits(player) & RowMask(row));
    }

    internal ulong GetBlockerBits(PlayerId player) =>
        player == PlayerId.Player1 ? _p1BlockerBits : _p2BlockerBits;

    internal ulong GetRunnerBit(PlayerId player) =>
        player == PlayerId.Player1 ? _p1RunnerBit : _p2RunnerBit;

    internal ulong GetPlayerPieceBits(PlayerId player) => GetBlockerBits(player) | GetRunnerBit(player);

    internal ulong GetOccupiedBits() => _p1BlockerBits | _p2BlockerBits | _p1RunnerBit | _p2RunnerBit;

    internal static ulong RowMask(int row) => 0xFFUL << (row * BoardSize);

    public int CurrentPositionRepetitionCount()
    {
        ulong stateHash = GetRepetitionStateHash();
        return _positionCounts.TryGetValue(stateHash, out int count) ? count : 0;
    }

    // Number of times the current physical board + side-to-move occurred in the
    // actual game history before/including the current real position. Search moves
    // do not mutate this table, so a root candidate can be applied temporarily and
    // queried without polluting the real-history signal.
    internal int CurrentPhysicalPositionHistoricalCount()
    {
        return _physicalPositionCounts.TryGetValue(_positionHash, out int count) ? count : 0;
    }

    public ulong GetSearchHash() => ComputeSearchHash(includeBacktrackState: true);

    // Static evaluation depends on the physical position/side-to-move, Runner immediate-
    // backtrack state, and optional move-policy state, but not on repetition counts or
    // absolute ply number.  Keeping a separate hash lets depth-0 evaluation reuse exact
    // values across move-order transpositions without weakening the history-sensitive
    // search TT key.
    internal ulong GetStaticEvaluationHash()
    {
        ulong policySalt = MovePolicyStateHash();
        return _positionHash ^ RotateLeft(policySalt, 7) ^ RotateLeft(BacktrackStateHash(), 31);
    }

    internal ulong GetSearchHashIgnoringBacktrackForVerification() =>
        ComputeSearchHash(includeBacktrackState: false);

    private ulong ComputeSearchHash(bool includeBacktrackState)
    {
        ulong policySalt = MovePolicyStateHash();
        ulong backtrackSalt = includeBacktrackState ? BacktrackStateHash() : 0UL;
        ulong combined = _positionHash ^ RotateLeft(_historyHash, 19) ^ ((ulong)PlyCount * 0x9E3779B97F4A7C15UL) ^ RotateLeft(policySalt, 7) ^ RotateLeft(backtrackSalt, 31);
        return Mix64(combined);
    }

    public string[][] ToLogBoard()
    {
        var result = new string[BoardSize][];
        for (int row = 0; row < BoardSize; row++)
        {
            result[row] = new string[BoardSize];
            for (int col = 0; col < BoardSize; col++)
            {
                result[row][col] = _board[row, col] switch
                {
                    null => ".",
                    { Owner: PlayerId.Player1, Type: PieceType.Blocker } => "P1O",
                    { Owner: PlayerId.Player1, Type: PieceType.Runner } => "P1S",
                    { Owner: PlayerId.Player2, Type: PieceType.Blocker } => "P2O",
                    { Owner: PlayerId.Player2, Type: PieceType.Runner } => "P2S",
                    _ => "?"
                };
            }
        }

        return result;
    }

    internal GameEngineVerificationSnapshot GetVerificationSnapshot()
    {
        var repetition = string.Join(
            ";",
            _positionCounts
                .OrderBy(pair => pair.Key)
                .Select(pair => $"{pair.Key:X16}:{pair.Value}"));

        return new GameEngineVerificationSnapshot(
            BoardSignatureForVerification(),
            CurrentPlayer,
            Outcome,
            EndReason,
            PlyCount,
            LastMove,
            _p1BlockerBits,
            _p2BlockerBits,
            _p1RunnerBit,
            _p2RunnerBit,
            _p1RunnerPosition,
            _p2RunnerPosition,
            _movePolicy?.Id,
            _movePolicyState,
            _p1LastOwnMove,
            _p2LastOwnMove,
            _p1LastRealRunnerMove,
            _p2LastRealRunnerMove,
            _positionHash,
            _historyHash,
            BacktrackStateHash(),
            GetRepetitionStateHash(),
            GetSearchHash(),
            repetition);
    }

    private string BoardSignatureForVerification()
    {
        var sb = new StringBuilder(BoardSize * BoardSize);
        for (int row = 0; row < BoardSize; row++)
        {
            for (int col = 0; col < BoardSize; col++)
            {
                sb.Append(_board[row, col] switch
                {
                    null => '.',
                    { Owner: PlayerId.Player1, Type: PieceType.Blocker } => 'O',
                    { Owner: PlayerId.Player1, Type: PieceType.Runner } => 'S',
                    { Owner: PlayerId.Player2, Type: PieceType.Blocker } => 'o',
                    { Owner: PlayerId.Player2, Type: PieceType.Runner } => 's',
                    _ => '?'
                });
            }
        }
        return sb.ToString();
    }

    public string GetPositionKey()
    {
        var sb = new StringBuilder(BoardSize * BoardSize + 2);
        for (int row = 0; row < BoardSize; row++)
        {
            for (int col = 0; col < BoardSize; col++)
            {
                sb.Append(_board[row, col] switch
                {
                    null => '.',
                    { Owner: PlayerId.Player1, Type: PieceType.Blocker } => 'A',
                    { Owner: PlayerId.Player1, Type: PieceType.Runner } => 'S',
                    { Owner: PlayerId.Player2, Type: PieceType.Blocker } => 'a',
                    { Owner: PlayerId.Player2, Type: PieceType.Runner } => 's',
                    _ => '?'
                });
            }
        }

        sb.Append(CurrentPlayer == PlayerId.Player1 ? '1' : '2');
        return sb.ToString();
    }

    private void ApplyGeneratedMoveCore(
        Move move,
        Piece movingPiece,
        Piece? targetPiece,
        ref SearchUndo undo,
        bool captureUndo = true)
    {
        if (move.Kind == MoveKind.Sacrifice &&
            (movingPiece.Type != PieceType.Runner ||
             targetPiece is null ||
             targetPiece.Value.Owner != movingPiece.Owner ||
             targetPiece.Value.Type != PieceType.Blocker))
        {
            throw new InvalidOperationException("Invalid generated sacrifice move.");
        }

        _positionHash ^= PieceSquareHash(movingPiece, move.From);
        if (targetPiece is { } target)
        {
            _positionHash ^= PieceSquareHash(target, move.To);
        }

        UpdateBitboardsForMove(movingPiece, targetPiece, move);
        _board[move.From.Row, move.From.Col] = null;
        _board[move.To.Row, move.To.Col] = movingPiece;
        if (movingPiece.Type == PieceType.Runner)
        {
            if (movingPiece.Owner == PlayerId.Player1) _p1RunnerPosition = move.To;
            else _p2RunnerPosition = move.To;
        }
        if (_movePolicy is not null)
        {
            _movePolicyState = _movePolicy.ApplyMove(this, movingPiece, targetPiece, move, _movePolicyState);
        }
        _positionHash ^= PieceSquareHash(movingPiece, move.To);
        SetLastOwnMove(movingPiece.Owner, move);
        if (!captureUndo && movingPiece.Type == PieceType.Runner)
        {
            if (movingPiece.Owner == PlayerId.Player1) _p1LastRealRunnerMove = move;
            else _p2LastRealRunnerMove = move;
        }
        LastMove = move;
        PlyCount++;

        if (movingPiece.Type == PieceType.Runner && IsGoalRow(movingPiece.Owner, move.To.Row))
        {
            Outcome = movingPiece.Owner == PlayerId.Player1 ? GameOutcome.Player1Win : GameOutcome.Player2Win;
            EndReason = EndReason.GoalReached;
            return;
        }

        CurrentPlayer = CurrentPlayer.Opponent();
        _positionHash ^= SideToMoveHash;

        // Keep a lightweight real-game history for the CPU cycle-breaking heuristic.
        // Recursive search moves use captureUndo=true and deliberately do not touch it.
        if (!captureUndo)
        {
            _physicalPositionCounts[_positionHash] =
                (_physicalPositionCounts.TryGetValue(_positionHash, out int physicalCount) ? physicalCount : 0) + 1;
        }

        // The physical game rule is evaluated without any host move policy.
        // If only a host policy removes all continuations, record that separately
        // instead of pretending the base game produced an immobilization.
        if (CountRunnerMovesAggregate(CurrentPlayer, applyMovePolicy: false).Total == 0)
        {
            PlayerId winner = CurrentPlayer.Opponent();
            Outcome = winner == PlayerId.Player1 ? GameOutcome.Player1Win : GameOutcome.Player2Win;
            EndReason = EndReason.RunnerImmobilized;
            return;
        }
        ulong repetitionHash = GetRepetitionStateHash();
        int previousCount = _positionCounts.TryGetValue(repetitionHash, out int previous) ? previous : 0;
        int newCount = previousCount + 1;
        if (previousCount > 0)
        {
            _historyHash ^= OccurrenceHash(repetitionHash, previousCount);
        }
        _historyHash ^= OccurrenceHash(repetitionHash, newCount);
        _positionCounts[repetitionHash] = newCount;

        if (captureUndo)
        {
            undo = undo with
            {
                RepetitionUpdated = true,
                RepetitionHash = repetitionHash,
                RepetitionPreviousCount = previousCount
            };
        }

        if (newCount >= 4)
        {
            Outcome = GameOutcome.Draw;
            EndReason = EndReason.FourfoldRepetition;
            return;
        }

        // In Free mode, a physical Runner continuation already guarantees at least one legal move,
        // so avoid a full move-list allocation on every searched ply. Only constrained test
        // strategies need this extra whole-position no-move check.
        if (_movePolicy is not null && !HasAnyLegalMove(CurrentPlayer))
        {
            PlayerId winner = CurrentPlayer.Opponent();
            Outcome = winner == PlayerId.Player1 ? GameOutcome.Player1Win : GameOutcome.Player2Win;
            EndReason = EndReason.MovePolicyNoMove;
        }
    }

    private void ResetToStartConfiguration()
    {
        Array.Clear(_board, 0, _board.Length);
        _positionCounts.Clear();
        _physicalPositionCounts.Clear();
        _historyHash = 0;
        _p1BlockerBits = 0;
        _p2BlockerBits = 0;
        _p1RunnerBit = 0;
        _p2RunnerBit = 0;

        Position? p1Runner = null;
        Position? p2Runner = null;
        for (int row = 0; row < BoardSize; row++)
        {
            for (int col = 0; col < BoardSize; col++)
            {
                var position = new Position(row, col);
                Piece? piece = StartConfiguration.GetPiece(position);
                _board[row, col] = piece;
                if (piece is { } value)
                {
                    ulong bit = SquareBit(position);
                    if (value.Type == PieceType.Runner)
                    {
                        if (value.Owner == PlayerId.Player1)
                        {
                            p1Runner = position;
                            _p1RunnerBit |= bit;
                        }
                        else
                        {
                            p2Runner = position;
                            _p2RunnerBit |= bit;
                        }
                    }
                    else if (value.Owner == PlayerId.Player1)
                    {
                        _p1BlockerBits |= bit;
                    }
                    else
                    {
                        _p2BlockerBits |= bit;
                    }
                }
            }
        }

        _p1RunnerPosition = p1Runner ?? throw new InvalidDataException("Start position has no P1 runner.");
        _p2RunnerPosition = p2Runner ?? throw new InvalidDataException("Start position has no P2 runner.");
        _p1LastOwnMove = null;
        _p2LastOwnMove = null;
        _p1LastRealRunnerMove = null;
        _p2LastRealRunnerMove = null;
        _backtrackStateHash = ComputeBacktrackStateHash();
        _movePolicyState = 0;
        CurrentPlayer = StartConfiguration.CurrentPlayer;
        Outcome = GameOutcome.Ongoing;
        EndReason = EndReason.None;
        PlyCount = 0;
        LastMove = null;
        _positionHash = ComputePositionHash();
        _movePolicyState = _movePolicy?.CreateInitialState(this) ?? 0UL;
        ulong initialRepetitionHash = GetRepetitionStateHash();
        _positionCounts[initialRepetitionHash] = 1;
        _physicalPositionCounts[_positionHash] = 1;
        _historyHash = OccurrenceHash(initialRepetitionHash, 1);

        if (IsGoalRow(PlayerId.Player1, _p1RunnerPosition.Row))
        {
            Outcome = GameOutcome.Player1Win;
            EndReason = EndReason.GoalReached;
        }
        else if (IsGoalRow(PlayerId.Player2, _p2RunnerPosition.Row))
        {
            Outcome = GameOutcome.Player2Win;
            EndReason = EndReason.GoalReached;
        }
        else if (CountRunnerMovesAggregate(CurrentPlayer, applyMovePolicy: false).Total == 0)
        {
            PlayerId winner = CurrentPlayer.Opponent();
            Outcome = winner == PlayerId.Player1 ? GameOutcome.Player1Win : GameOutcome.Player2Win;
            EndReason = EndReason.RunnerImmobilized;
        }
        else if (_movePolicy is not null && !HasAnyLegalMove(CurrentPlayer))
        {
            PlayerId winner = CurrentPlayer.Opponent();
            Outcome = winner == PlayerId.Player1 ? GameOutcome.Player1Win : GameOutcome.Player2Win;
            EndReason = EndReason.MovePolicyNoMove;
        }
    }

    private bool HasAnyLegalMove(PlayerId player)
    {
        Span<Move> buffer = stackalloc Move[MaxLegalMoves];
        return GenerateLegalMoves(player, buffer) > 0;
    }

    private bool IsAllowedByMovePolicy(
        PlayerId player,
        Piece movingPiece,
        Move move,
        bool applyMovePolicy = true) =>
        !applyMovePolicy || _movePolicy is null ||
        _movePolicy.IsMoveAllowed(this, player, movingPiece, move, _movePolicyState);

    private void AddBlockerMoves(Position from, PlayerId player, Span<Move> moves, ref int count)
    {
        ulong occupied = GetOccupiedBits();
        int pieceStartCount = count;
        Move? backtrackCandidate = null;
        var movingPiece = new Piece(player, PieceType.Blocker);

        foreach (var (dr, dc) in EightDirections)
        {
            var to = new Position(from.Row + dr, from.Col + dc);
            if (!to.IsInside || IsOccupied(occupied, to))
            {
                continue;
            }

            var move = new Move(from, to, MoveKind.Normal);
            if (!IsAllowedByMovePolicy(player, movingPiece, move))
            {
                continue;
            }

            if (IsImmediateBacktrack(player, from, to))
            {
                // The immediate return move is held back and allowed only when this piece has no other legal move.
                backtrackCandidate = move;
                continue;
            }

            moves[count++] = move;
        }

        if (backtrackCandidate is { } forcedBacktrack && count == pieceStartCount)
        {
            moves[count++] = forcedBacktrack;
        }
    }

    private void AddRunnerMoves(Position from, PlayerId player, Span<Move> moves, ref int count, bool applyMovePolicy)
    {
        ulong occupied = GetOccupiedBits();
        ulong friendlyBlockers = GetBlockerBits(player);
        int pieceStartCount = count;
        Move? backtrackCandidate = null;
        var movingPiece = new Piece(player, PieceType.Runner);

        // Baseline A-plan: normal Runner movement is orthogonal only.
        foreach (var (dr, dc) in OrthogonalDirections)
        {
            var to = new Position(from.Row + dr, from.Col + dc);
            if (!to.IsInside || IsOccupied(occupied, to))
            {
                continue;
            }

            var move = new Move(from, to, MoveKind.Normal);
            if (!IsAllowedByMovePolicy(player, movingPiece, move, applyMovePolicy))
            {
                continue;
            }

            if (IsImmediateBacktrack(player, from, to))
            {
                backtrackCandidate = move;
                continue;
            }

            moves[count++] = move;
        }

        // Sacrifice movement may use all 8 adjacent directions, including goal entry.
        foreach (var (dr, dc) in EightDirections)
        {
            var to = new Position(from.Row + dr, from.Col + dc);
            if (!to.IsInside || !IsBitSet(friendlyBlockers, to))
            {
                continue;
            }

            var move = new Move(from, to, MoveKind.Sacrifice);
            if (!IsAllowedByMovePolicy(player, movingPiece, move, applyMovePolicy))
            {
                continue;
            }

            moves[count++] = move;
        }

        // If this piece has no other legal move, allow the otherwise-forbidden immediate return.
        if (backtrackCandidate is { } forcedBacktrack && count == pieceStartCount)
        {
            moves[count++] = forcedBacktrack;
        }
    }

    private void UpdateBitboardsForMove(Piece movingPiece, Piece? targetPiece, Move move)
    {
        ulong fromBit = SquareBit(move.From);
        ulong toBit = SquareBit(move.To);

        if (movingPiece.Type == PieceType.Runner)
        {
            if (movingPiece.Owner == PlayerId.Player1) _p1RunnerBit = toBit;
            else _p2RunnerBit = toBit;

            if (targetPiece is { Type: PieceType.Blocker } target)
            {
                if (target.Owner == PlayerId.Player1) _p1BlockerBits &= ~toBit;
                else _p2BlockerBits &= ~toBit;
            }
            return;
        }

        if (movingPiece.Owner == PlayerId.Player1)
        {
            _p1BlockerBits = (_p1BlockerBits & ~fromBit) | toBit;
        }
        else
        {
            _p2BlockerBits = (_p2BlockerBits & ~fromBit) | toBit;
        }
    }

    private static ulong SquareBit(Position position) =>
        1UL << (position.Row * BoardSize + position.Col);

    private static bool IsBitSet(ulong bits, Position position) =>
        (bits & SquareBit(position)) != 0;

    private static bool IsOccupied(ulong occupied, Position position) =>
        (occupied & SquareBit(position)) != 0;

    private bool IsBlockerAt(PlayerId player, Position position) =>
        position.IsInside && IsBitSet(GetBlockerBits(player), position);

    private RunnerMoveCounts CountRunnerMovesAggregate(PlayerId player, bool applyMovePolicy)
    {
        if (Outcome != GameOutcome.Ongoing)
        {
            return default;
        }

        Position from = FindRunner(player);
        int goalRow = player == PlayerId.Player1 ? 0 : BoardSize - 1;
        ulong occupied = GetOccupiedBits();
        ulong friendlyBlockers = GetBlockerBits(player);
        int normal = 0;
        int sacrifice = 0;
        int immediateGoal = 0;
        bool backtrackCandidate = false;
        bool backtrackImmediateGoal = false;
        var movingPiece = new Piece(player, PieceType.Runner);

        foreach (var (dr, dc) in OrthogonalDirections)
        {
            var to = new Position(from.Row + dr, from.Col + dc);
            if (!to.IsInside || IsOccupied(occupied, to))
            {
                continue;
            }

            var move = new Move(from, to, MoveKind.Normal);
            if (!IsAllowedByMovePolicy(player, movingPiece, move, applyMovePolicy))
            {
                continue;
            }

            if (IsImmediateBacktrack(player, from, to))
            {
                backtrackCandidate = true;
                backtrackImmediateGoal = to.Row == goalRow;
                continue;
            }

            normal++;
            if (to.Row == goalRow) immediateGoal++;
        }

        foreach (var (dr, dc) in EightDirections)
        {
            var to = new Position(from.Row + dr, from.Col + dc);
            if (!to.IsInside || !IsBitSet(friendlyBlockers, to))
            {
                continue;
            }

            var move = new Move(from, to, MoveKind.Sacrifice);
            if (!IsAllowedByMovePolicy(player, movingPiece, move, applyMovePolicy))
            {
                continue;
            }

            sacrifice++;
            if (to.Row == goalRow) immediateGoal++;
        }

        if (backtrackCandidate && normal + sacrifice == 0)
        {
            normal++;
            if (backtrackImmediateGoal) immediateGoal++;
        }

        return new RunnerMoveCounts(normal, sacrifice, immediateGoal);
    }

    private bool IsImmediateBacktrack(PlayerId player, Position from, Position to)
    {
        Move? previous = player == PlayerId.Player1 ? _p1LastOwnMove : _p2LastOwnMove;
        return previous is { } last && last.To == from && last.From == to;
    }

    private void SetLastOwnMove(PlayerId player, Move move)
    {
        if (player == PlayerId.Player1) _p1LastOwnMove = move;
        else _p2LastOwnMove = move;
        _backtrackStateHash = ComputeBacktrackStateHash();
    }

    // Repetition identity is the physical board only. Side-to-move, immediate-backtrack
    // history, move-policy state, and search state are intentionally excluded.
    private ulong GetRepetitionStateHash() =>
        CurrentPlayer == PlayerId.Player2 ? _positionHash ^ SideToMoveHash : _positionHash;

    private ulong BacktrackStateHash() => _backtrackStateHash;

    private ulong ComputeBacktrackStateHash()
    {
        ulong p1 = MoveStateHash(_p1LastOwnMove, 0x243F6A8885A308D3UL);
        ulong p2 = MoveStateHash(_p2LastOwnMove, 0x13198A2E03707344UL);
        return Mix64(p1 ^ RotateLeft(p2, 17));
    }

    private static ulong MoveStateHash(Move? move, ulong salt)
    {
        if (move is not { } value)
        {
            return Mix64(salt);
        }

        ulong from = (ulong)(value.From.Row * BoardSize + value.From.Col + 1);
        ulong to = (ulong)(value.To.Row * BoardSize + value.To.Col + 1);
        return Mix64(salt ^ from ^ (to << 7));
    }

    private ulong ComputePositionHash()
    {
        ulong hash = CurrentPlayer == PlayerId.Player2 ? SideToMoveHash : 0UL;
        for (int row = 0; row < BoardSize; row++)
        {
            for (int col = 0; col < BoardSize; col++)
            {
                if (_board[row, col] is { } piece)
                {
                    hash ^= PieceSquareHash(piece, new Position(row, col));
                }
            }
        }
        return hash;
    }

    private ulong MovePolicyStateHash() =>
        _movePolicy?.GetSearchHash(_movePolicyState) ?? 0UL;

    private static ulong PieceSquareHash(Piece piece, Position position)
    {
        ulong square = (ulong)(position.Row * BoardSize + position.Col + 1);
        ulong owner = piece.Owner == PlayerId.Player1 ? 0xA24BAED4963EE407UL : 0x9FB21C651E98DF25UL;
        ulong type = piece.Type == PieceType.Runner ? 0xC13FA9A902A6328FUL : 0x91E10DA5C79E7B1DUL;
        return Mix64(square * 0x9E3779B97F4A7C15UL ^ owner ^ type);
    }

    private static ulong OccurrenceHash(ulong positionHash, int count) =>
        Mix64(positionHash ^ ((ulong)count * 0x94D049BB133111EBUL) ^ 0xBF58476D1CE4E5B9UL);

    private static ulong RotateLeft(ulong value, int count) =>
        (value << count) | (value >> (64 - count));

    private static ulong Mix64(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static bool IsGoalRow(PlayerId player, int row) =>
        player == PlayerId.Player1 ? row == 0 : row == BoardSize - 1;

    internal readonly record struct RunnerMoveCounts(int Normal, int Sacrifice, int ImmediateGoal)
    {
        public int Total => Normal + Sacrifice;
    }

    internal readonly record struct SearchUndo(
        Move Move,
        Piece MovingPiece,
        Piece? TargetPiece,
        PlayerId PreviousPlayer,
        GameOutcome PreviousOutcome,
        EndReason PreviousEndReason,
        int PreviousPlyCount,
        Move? PreviousLastMove,
        ulong PreviousP1BlockerBits,
        ulong PreviousP2BlockerBits,
        ulong PreviousP1RunnerBit,
        ulong PreviousP2RunnerBit,
        ulong PreviousPositionHash,
        ulong PreviousHistoryHash,
        ulong PreviousBacktrackStateHash,
        ulong PreviousMovePolicyState,
        Move? PreviousP1LastOwnMove,
        Move? PreviousP2LastOwnMove,
        bool RepetitionUpdated,
        ulong RepetitionHash,
        int RepetitionPreviousCount);
}

internal sealed record GameEngineVerificationSnapshot(
    string BoardSignature,
    PlayerId CurrentPlayer,
    GameOutcome Outcome,
    EndReason EndReason,
    int PlyCount,
    Move? LastMove,
    ulong P1BlockerBits,
    ulong P2BlockerBits,
    ulong P1RunnerBit,
    ulong P2RunnerBit,
    Position P1RunnerPosition,
    Position P2RunnerPosition,
    string? MovePolicyId,
    ulong MovePolicyState,
    Move? P1LastOwnMove,
    Move? P2LastOwnMove,
    Move? P1LastRealRunnerMove,
    Move? P2LastRealRunnerMove,
    ulong PositionHash,
    ulong HistoryHash,
    ulong BacktrackStateHash,
    ulong RepetitionStateHash,
    ulong SearchHash,
    string RepetitionCountsSignature);

