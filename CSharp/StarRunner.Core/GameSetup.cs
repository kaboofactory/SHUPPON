using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace StarRunner.Core;

/// <summary>
/// Immutable start position for the standard StarRunner rules.
/// Scenario/test-only move restrictions are intentionally not part of this type.
/// </summary>
public sealed class GameStartConfiguration
{
    private readonly string[] _boardRows;
    private readonly ReadOnlyCollection<string> _boardRowsView;

    public string Name { get; }
    public string SourceName { get; }
    public IReadOnlyList<string> BoardRows => _boardRowsView;
    public PlayerId CurrentPlayer { get; }
    public string Hash { get; }
    public string BoardSignature => string.Join("/", _boardRows);
    public int Player1BlockerCount { get; }
    public int Player2BlockerCount { get; }

    private GameStartConfiguration(
        string name,
        string sourceName,
        string[] boardRows,
        PlayerId currentPlayer,
        string hash)
    {
        Name = name;
        SourceName = sourceName;
        _boardRows = boardRows;
        _boardRowsView = Array.AsReadOnly(_boardRows);
        CurrentPlayer = currentPlayer;
        Hash = hash;
        Player1BlockerCount = _boardRows.Sum(row => row.Count(c => c == 'O'));
        Player2BlockerCount = _boardRows.Sum(row => row.Count(c => c == 'o'));
    }

    private static readonly GameStartConfiguration InitialValue = Create(
        "Initial",
        "built-in",
        new[]
        {
            "ooosooo.",
            "........",
            "........",
            "........",
            "........",
            "........",
            "........",
            ".OOOSOOO"
        },
        PlayerId.Player1);

    public static GameStartConfiguration Initial => InitialValue;

    public static GameStartConfiguration Create(
        string name,
        string sourceName,
        IReadOnlyList<string> boardRows,
        PlayerId currentPlayer)
    {
        ArgumentNullException.ThrowIfNull(boardRows);
        if (!Enum.IsDefined(typeof(PlayerId), currentPlayer))
        {
            throw new InvalidDataException($"Unsupported currentPlayer value: {currentPlayer}.");
        }
        string[] rows = boardRows.Select(row => row?.Trim() ?? string.Empty).ToArray();
        ValidateRows(rows);
        ValidatePieces(rows);

        string canonical = string.Join("/", rows) + "|" + currentPlayer;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16].ToLowerInvariant();
        return new GameStartConfiguration(
            string.IsNullOrWhiteSpace(name) ? "Scenario" : name.Trim(),
            string.IsNullOrWhiteSpace(sourceName) ? "scenario" : sourceName.Trim(),
            rows,
            currentPlayer,
            hash);
    }

    public string[] CopyBoardRows() => (string[])_boardRows.Clone();

    public int CountBlockers(PlayerId owner) =>
        owner == PlayerId.Player1 ? Player1BlockerCount : Player2BlockerCount;

    public Piece? GetPiece(Position position)
    {
        if (!position.IsInside) return null;
        char c = _boardRows[position.Row][position.Col];
        return c switch
        {
            'S' => new Piece(PlayerId.Player1, PieceType.Runner),
            'O' => new Piece(PlayerId.Player1, PieceType.Blocker),
            's' => new Piece(PlayerId.Player2, PieceType.Runner),
            'o' => new Piece(PlayerId.Player2, PieceType.Blocker),
            '.' => null,
            _ => throw new InvalidDataException($"Unsupported board character '{c}'.")
        };
    }

    private static void ValidateRows(string[] rows)
    {
        if (rows.Length != GameEngine.BoardSize)
        {
            throw new InvalidDataException($"board must contain exactly {GameEngine.BoardSize} rows.");
        }

        for (int row = 0; row < rows.Length; row++)
        {
            if (rows[row].Length != GameEngine.BoardSize)
            {
                throw new InvalidDataException($"board[{row}] must be exactly {GameEngine.BoardSize} characters.");
            }

            foreach (char c in rows[row])
            {
                if (c is not ('.' or 'S' or 'O' or 's' or 'o'))
                {
                    throw new InvalidDataException($"board[{row}] contains invalid character '{c}'. Use . S O s o only.");
                }
            }
        }
    }

    private static void ValidatePieces(string[] rows)
    {
        int p1Runner = rows.Sum(row => row.Count(c => c == 'S'));
        int p2Runner = rows.Sum(row => row.Count(c => c == 's'));
        int p1Blockers = rows.Sum(row => row.Count(c => c == 'O'));
        int p2Blockers = rows.Sum(row => row.Count(c => c == 'o'));

        if (p1Runner != 1 || p2Runner != 1)
        {
            throw new InvalidDataException($"board must contain exactly one S and one s (found S={p1Runner}, s={p2Runner}).");
        }

        if (p1Blockers > 6 || p2Blockers > 6)
        {
            throw new InvalidDataException($"board may contain at most six blockers per side (found O={p1Blockers}, o={p2Blockers}).");
        }
    }
}
