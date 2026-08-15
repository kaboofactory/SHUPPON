namespace StarRunner.Core;

/// <summary>
/// Optional host-supplied move policy used by development tools and custom embeddings.
/// The standard game uses no policy. Implementations must be immutable/thread-safe;
/// per-game mutable state is carried by the engine as a single ulong.
/// </summary>
public interface IGameMovePolicy
{
    /// <summary>A stable identifier that includes any configuration affecting legal moves.</summary>
    string Id { get; }

    /// <summary>Creates the initial per-game policy state.</summary>
    ulong CreateInitialState(GameEngine game);

    /// <summary>Returns whether a base-rule legal move remains allowed by this policy.</summary>
    bool IsMoveAllowed(
        GameEngine game,
        PlayerId player,
        Piece movingPiece,
        Move move,
        ulong policyState);

    /// <summary>Returns the next policy state after an allowed move is applied.</summary>
    ulong ApplyMove(
        GameEngine game,
        Piece movingPiece,
        Piece? replacedPiece,
        Move move,
        ulong policyState);

    /// <summary>Hash contribution used only by AI/search identity, not by repetition rules.</summary>
    ulong GetSearchHash(ulong policyState);
}
