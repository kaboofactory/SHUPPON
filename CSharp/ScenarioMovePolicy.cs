namespace StarRunnerPrototype;

public enum StrategyMode
{
    Free,
    RushOne
}

public sealed record PlayerStrategyConstraint(
    StrategyMode Mode,
    Position? AttackBlockerPosition,
    int DeviationBudget)
{
    public static PlayerStrategyConstraint Free => new(StrategyMode.Free, null, 0);

    public string Id => Mode == StrategyMode.Free
        ? "Free"
        : $"RushOne:{AttackBlockerPosition?.ToCoordinate() ?? "?"}:K{DeviationBudget}";
}

/// <summary>
/// Development-only Scenario Lab restriction. The StarRunner.Core assembly knows only
/// the generic IGameMovePolicy abstraction; RushOne itself lives in the host app.
/// </summary>
public sealed class ScenarioMovePolicy : IGameMovePolicy
{
    private const int NoneSquare = 64;
    private const ulong SquareMask = 0x7FUL;
    private const ulong BudgetMask = 0x3FFUL;

    public PlayerStrategyConstraint P1 { get; }
    public PlayerStrategyConstraint P2 { get; }
    public string Id => $"ScenarioPolicy:P1={P1.Id};P2={P2.Id}";

    public bool IsActive => P1.Mode != StrategyMode.Free || P2.Mode != StrategyMode.Free;

    public ScenarioMovePolicy(PlayerStrategyConstraint p1, PlayerStrategyConstraint p2)
    {
        P1 = p1 ?? PlayerStrategyConstraint.Free;
        P2 = p2 ?? PlayerStrategyConstraint.Free;
    }

    public PlayerStrategyConstraint GetStrategy(PlayerId player) =>
        player == PlayerId.Player1 ? P1 : P2;

    public ulong CreateInitialState(GameEngine game)
    {
        Validate(game, PlayerId.Player1, P1);
        Validate(game, PlayerId.Player2, P2);
        return Encode(
            P1.Mode == StrategyMode.RushOne ? P1.AttackBlockerPosition : null,
            P2.Mode == StrategyMode.RushOne ? P2.AttackBlockerPosition : null,
            P1.Mode == StrategyMode.RushOne ? P1.DeviationBudget : 0,
            P2.Mode == StrategyMode.RushOne ? P2.DeviationBudget : 0);
    }

    public bool IsMoveAllowed(
        GameEngine game,
        PlayerId player,
        Piece movingPiece,
        Move move,
        ulong policyState)
    {
        PlayerStrategyConstraint strategy = GetStrategy(player);
        if (strategy.Mode == StrategyMode.Free) return true;

        Position? attack = GetAttackBlockerPosition(policyState, player);
        if (movingPiece.Type == PieceType.Runner)
        {
            return move.Kind != MoveKind.Sacrifice || attack == move.To;
        }

        bool isAttackBlocker = attack == move.From;
        int homeRow = player == PlayerId.Player1 ? GameEngine.BoardSize - 1 : 0;
        int remaining = GetDeviationBudgetRemaining(policyState, player);
        return isAttackBlocker || move.To.Row == homeRow || remaining > 0;
    }

    public ulong ApplyMove(
        GameEngine game,
        Piece movingPiece,
        Piece? replacedPiece,
        Move move,
        ulong policyState)
    {
        PlayerId player = movingPiece.Owner;
        PlayerStrategyConstraint strategy = GetStrategy(player);
        if (strategy.Mode == StrategyMode.Free) return policyState;

        Position? attack = GetAttackBlockerPosition(policyState, player);
        if (movingPiece.Type == PieceType.Blocker && attack == move.From)
        {
            return SetAttack(policyState, player, move.To);
        }

        if (movingPiece.Type == PieceType.Runner && move.Kind == MoveKind.Sacrifice && attack == move.To)
        {
            return SetAttack(policyState, player, null);
        }

        if (movingPiece.Type == PieceType.Blocker)
        {
            int homeRow = player == PlayerId.Player1 ? GameEngine.BoardSize - 1 : 0;
            if (move.To.Row != homeRow)
            {
                int remaining = GetDeviationBudgetRemaining(policyState, player);
                if (remaining <= 0)
                {
                    throw new InvalidOperationException("RushOne guard move exceeded deviation budget.");
                }
                return SetBudget(policyState, player, remaining - 1);
            }
        }

        return policyState;
    }

    public ulong GetSearchHash(ulong policyState)
    {
        ulong z = policyState ^ 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public Position? GetAttackBlockerPosition(GameEngine game, PlayerId player) =>
        GetAttackBlockerPosition(game.MovePolicyState, player);

    public int GetDeviationBudgetRemaining(GameEngine game, PlayerId player) =>
        GetDeviationBudgetRemaining(game.MovePolicyState, player);

    private static void Validate(GameEngine game, PlayerId player, PlayerStrategyConstraint strategy)
    {
        if (strategy.Mode == StrategyMode.Free) return;
        if (strategy.DeviationBudget < 0 || strategy.DeviationBudget > 1000)
        {
            throw new InvalidDataException("deviationBudget must be between 0 and 1000.");
        }

        Position attack = strategy.AttackBlockerPosition
            ?? throw new InvalidDataException($"{player.ShortName()} RushOne requires attackBlocker.");
        if (!attack.IsInside)
        {
            throw new InvalidDataException($"{player.ShortName()} attackBlocker is outside the board.");
        }

        Piece expected = new(player, PieceType.Blocker);
        if (game.GetPiece(attack) != expected)
        {
            throw new InvalidDataException($"{player.ShortName()} attackBlocker {attack.ToCoordinate()} must contain its blocker.");
        }
    }

    private static ulong Encode(Position? p1Attack, Position? p2Attack, int p1Budget, int p2Budget)
    {
        ulong a1 = (ulong)SquareCode(p1Attack);
        ulong a2 = (ulong)SquareCode(p2Attack);
        return a1 |
               (a2 << 7) |
               ((ulong)Math.Clamp(p1Budget, 0, 1000) << 14) |
               ((ulong)Math.Clamp(p2Budget, 0, 1000) << 24);
    }

    private static int SquareCode(Position? position) =>
        position is { } p ? p.Row * GameEngine.BoardSize + p.Col : NoneSquare;

    private static Position? DecodeSquare(ulong value)
    {
        int code = (int)(value & SquareMask);
        return code == NoneSquare ? null : new Position(code / GameEngine.BoardSize, code % GameEngine.BoardSize);
    }

    private static Position? GetAttackBlockerPosition(ulong state, PlayerId player) =>
        player == PlayerId.Player1 ? DecodeSquare(state) : DecodeSquare(state >> 7);

    private static int GetDeviationBudgetRemaining(ulong state, PlayerId player) =>
        player == PlayerId.Player1
            ? (int)((state >> 14) & BudgetMask)
            : (int)((state >> 24) & BudgetMask);

    private static ulong SetAttack(ulong state, PlayerId player, Position? position)
    {
        int shift = player == PlayerId.Player1 ? 0 : 7;
        ulong mask = SquareMask << shift;
        return (state & ~mask) | ((ulong)SquareCode(position) << shift);
    }

    private static ulong SetBudget(ulong state, PlayerId player, int budget)
    {
        int shift = player == PlayerId.Player1 ? 14 : 24;
        ulong mask = BudgetMask << shift;
        return (state & ~mask) | ((ulong)Math.Clamp(budget, 0, 1000) << shift);
    }
}
