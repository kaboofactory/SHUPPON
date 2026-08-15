namespace StarRunner.Core;

/// <summary>
/// Serializable snapshot required to resume a game without losing the immediate-backtrack
/// or fourfold-repetition history. Arrays/dictionaries are defensive copies on export/import.
/// </summary>
public sealed record GameState(
    int SchemaVersion,
    string StartName,
    string StartSourceName,
    string[] StartBoardRows,
    PlayerId StartCurrentPlayer,
    string[] BoardRows,
    PlayerId CurrentPlayer,
    GameOutcome Outcome,
    EndReason EndReason,
    int PlyCount,
    Move? LastMove,
    Move? Player1LastOwnMove,
    Move? Player2LastOwnMove,
    Move? Player1LastRealRunnerMove,
    Move? Player2LastRealRunnerMove,
    Dictionary<ulong, int> RepetitionCounts,
    Dictionary<ulong, int> PhysicalPositionCounts,
    string? MovePolicyId,
    ulong MovePolicyState)
{
    public const int CurrentSchemaVersion = 2;
}
