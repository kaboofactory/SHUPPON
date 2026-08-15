namespace StarRunner.Core;

public enum PlayerId
{
    Player1,
    Player2
}

public enum PieceType
{
    Runner,
    Blocker
}

public enum MoveKind
{
    Normal,
    Sacrifice
}

public enum GameOutcome
{
    Ongoing,
    Player1Win,
    Player2Win,
    Draw
}

public enum EndReason
{
    None,
    GoalReached,
    RunnerImmobilized,
    MovePolicyNoMove,
    StrategyConstraintNoMove = MovePolicyNoMove,
    FourfoldRepetition
}

public readonly record struct RuleSet
{
    public static RuleSet Standard => new();
    public string Id => "Standard";
}

public readonly record struct Position(int Row, int Col)
{
    public bool IsInside => Row is >= 0 and < GameEngine.BoardSize && Col is >= 0 and < GameEngine.BoardSize;

    public string ToCoordinate()
    {
        char file = (char)('A' + Col);
        int rank = Row + 1;
        return $"{file}{rank}";
    }
}

public readonly record struct Piece(PlayerId Owner, PieceType Type);

public readonly record struct Move(Position From, Position To, MoveKind Kind)
{
    public string ToNotation()
    {
        string separator = Kind == MoveKind.Sacrifice ? "x" : "-";
        string suffix = Kind == MoveKind.Sacrifice ? "(own O)" : string.Empty;
        return $"{From.ToCoordinate()}{separator}{To.ToCoordinate()}{suffix}";
    }
}

public static class PlayerIdExtensions
{
    public static PlayerId Opponent(this PlayerId player) =>
        player == PlayerId.Player1 ? PlayerId.Player2 : PlayerId.Player1;

    public static string ShortName(this PlayerId player) =>
        player == PlayerId.Player1 ? "P1" : "P2";

}
