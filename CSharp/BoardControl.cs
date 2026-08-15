using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace StarRunnerPrototype;

public sealed class BoardControl : Control
{
    private GameEngine? _game;
    private Position? _selected;
    private IReadOnlyList<Move> _selectedMoves = Array.Empty<Move>();
    private Move? _lastMoveHighlight;

    public event EventHandler<Position>? CellClicked;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public GameEngine? Game
    {
        get => _game;
        set
        {
            _game = value;
            ClearSelection();
            _lastMoveHighlight = null;
            Invalidate();
        }
    }

    public BoardControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(238, 232, 214);
        MinimumSize = new Size(420, 420);
    }

    public void SetSelection(Position? selected, IReadOnlyList<Move>? moves = null)
    {
        _selected = selected;
        _selectedMoves = moves ?? Array.Empty<Move>();
        Invalidate();
    }

    public void ClearSelection()
    {
        _selected = null;
        _selectedMoves = Array.Empty<Move>();
        Invalidate();
    }

    public void SetLastMoveHighlight(Move? move)
    {
        _lastMoveHighlight = move;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        RectangleF boardRect = GetBoardRectangle();
        if (boardRect.Width <= 0 || boardRect.Height <= 0)
        {
            return;
        }

        float cell = boardRect.Width / GameEngine.BoardSize;
        using var lightBrush = new SolidBrush(Color.FromArgb(242, 220, 177));
        using var darkBrush = new SolidBrush(Color.FromArgb(190, 145, 92));
        using var borderPen = new Pen(Color.FromArgb(70, 55, 40), 2f);
        using var gridPen = new Pen(Color.FromArgb(120, 85, 55), 1f);
        using var selectedBrush = new SolidBrush(Color.FromArgb(95, 255, 235, 59));
        using var normalTargetBrush = new SolidBrush(Color.FromArgb(160, 46, 125, 50));
        using var sacrificeTargetPen = new Pen(Color.FromArgb(245, 230, 126, 34), Math.Max(3f, cell * 0.055f));

        for (int row = 0; row < GameEngine.BoardSize; row++)
        {
            for (int col = 0; col < GameEngine.BoardSize; col++)
            {
                var cellRect = new RectangleF(boardRect.Left + col * cell, boardRect.Top + row * cell, cell, cell);
                e.Graphics.FillRectangle(((row + col) & 1) == 0 ? lightBrush : darkBrush, cellRect);
                e.Graphics.DrawRectangle(gridPen, cellRect.X, cellRect.Y, cellRect.Width, cellRect.Height);
            }
        }

        if (_lastMoveHighlight is { } lastMove)
        {
            using var fromBrush = new SolidBrush(Color.FromArgb(115, 255, 235, 59));
            using var toBrush = new SolidBrush(Color.FromArgb(125, 38, 198, 218));
            e.Graphics.FillRectangle(fromBrush, CellRect(boardRect, cell, lastMove.From));
            e.Graphics.FillRectangle(toBrush, CellRect(boardRect, cell, lastMove.To));
        }

        if (_selected is { } selected)
        {
            RectangleF rect = CellRect(boardRect, cell, selected);
            e.Graphics.FillRectangle(selectedBrush, rect);
        }

        if (_game is not null)
        {
            for (int row = 0; row < GameEngine.BoardSize; row++)
            {
                for (int col = 0; col < GameEngine.BoardSize; col++)
                {
                    Piece? piece = _game.GetPiece(new Position(row, col));
                    if (piece is null)
                    {
                        continue;
                    }

                    DrawPiece(e.Graphics, CellRect(boardRect, cell, new Position(row, col)), piece.Value);
                }
            }
        }


        // Draw legal-target markers after pieces so sacrifice targets remain visible on occupied friendly blockers.
        foreach (Move move in _selectedMoves)
        {
            RectangleF rect = CellRect(boardRect, cell, move.To);
            if (move.Kind == MoveKind.Sacrifice)
            {
                float inset = cell * 0.075f;
                var ring = RectangleF.Inflate(rect, -inset, -inset);
                e.Graphics.DrawEllipse(sacrificeTargetPen, ring);
            }
            else
            {
                float d = cell * 0.22f;
                var marker = new RectangleF(
                    rect.Left + (rect.Width - d) / 2,
                    rect.Top + (rect.Height - d) / 2,
                    d,
                    d);
                e.Graphics.FillEllipse(normalTargetBrush, marker);
            }
        }

        e.Graphics.DrawRectangle(borderPen, boardRect.X, boardRect.Y, boardRect.Width, boardRect.Height);
        DrawCoordinates(e.Graphics, boardRect, cell);
        DrawGoalLabels(e.Graphics, boardRect);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        RectangleF boardRect = GetBoardRectangle();
        if (!boardRect.Contains(e.Location))
        {
            return;
        }

        float cell = boardRect.Width / GameEngine.BoardSize;
        int col = Math.Clamp((int)((e.X - boardRect.Left) / cell), 0, GameEngine.BoardSize - 1);
        int row = Math.Clamp((int)((e.Y - boardRect.Top) / cell), 0, GameEngine.BoardSize - 1);
        CellClicked?.Invoke(this, new Position(row, col));
    }

    private RectangleF GetBoardRectangle()
    {
        const float margin = 34f;
        float size = Math.Min(ClientSize.Width - margin * 2, ClientSize.Height - margin * 2);
        size = Math.Max(0, size);
        float left = (ClientSize.Width - size) / 2f;
        float top = (ClientSize.Height - size) / 2f;
        return new RectangleF(left, top, size, size);
    }

    private static RectangleF CellRect(RectangleF boardRect, float cell, Position position) =>
        new(boardRect.Left + position.Col * cell, boardRect.Top + position.Row * cell, cell, cell);

    private static void DrawPiece(Graphics graphics, RectangleF cellRect, Piece piece)
    {
        Color color = piece.Owner == PlayerId.Player1
            ? Color.FromArgb(37, 88, 175)
            : Color.FromArgb(174, 50, 55);

        float inset = cellRect.Width * 0.13f;
        var pieceRect = RectangleF.Inflate(cellRect, -inset, -inset);
        using var fill = new SolidBrush(color);
        using var outline = new Pen(Color.FromArgb(45, 45, 45), Math.Max(1.5f, cellRect.Width * 0.025f));
        graphics.FillEllipse(fill, pieceRect);
        graphics.DrawEllipse(outline, pieceRect);

        if (piece.Type == PieceType.Runner)
        {
            PointF[] star = CreateStarPoints(
                pieceRect.Left + pieceRect.Width / 2,
                pieceRect.Top + pieceRect.Height / 2,
                pieceRect.Width * 0.31f,
                pieceRect.Width * 0.14f);
            using var starBrush = new SolidBrush(Color.White);
            graphics.FillPolygon(starBrush, star);
        }
        else
        {
            float innerInset = pieceRect.Width * 0.27f;
            var inner = RectangleF.Inflate(pieceRect, -innerInset, -innerInset);
            using var innerBrush = new SolidBrush(Color.FromArgb(230, 245, 245, 245));
            graphics.FillEllipse(innerBrush, inner);
        }
    }

    private static PointF[] CreateStarPoints(float cx, float cy, float outerRadius, float innerRadius)
    {
        var points = new PointF[10];
        for (int i = 0; i < points.Length; i++)
        {
            double angle = -Math.PI / 2 + i * Math.PI / 5;
            float radius = (i & 1) == 0 ? outerRadius : innerRadius;
            points[i] = new PointF(
                cx + (float)Math.Cos(angle) * radius,
                cy + (float)Math.Sin(angle) * radius);
        }

        return points;
    }

    private static void DrawCoordinates(Graphics graphics, RectangleF boardRect, float cell)
    {
        using var font = new Font(SystemFonts.DefaultFont.FontFamily, 9f, FontStyle.Bold);
        using var brush = new SolidBrush(Color.FromArgb(65, 55, 45));
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        for (int col = 0; col < GameEngine.BoardSize; col++)
        {
            string file = ((char)('A' + col)).ToString();
            var rect = new RectangleF(boardRect.Left + col * cell, boardRect.Bottom + 3, cell, 20);
            graphics.DrawString(file, font, brush, rect, centered);
        }

        for (int row = 0; row < GameEngine.BoardSize; row++)
        {
            string rank = (row + 1).ToString();
            var rect = new RectangleF(boardRect.Left - 25, boardRect.Top + row * cell, 20, cell);
            graphics.DrawString(rank, font, brush, rect, centered);
        }
    }

    private static void DrawGoalLabels(Graphics graphics, RectangleF boardRect)
    {
        using var font = new Font(SystemFonts.DefaultFont.FontFamily, 8.5f, FontStyle.Bold);
        using var p1Brush = new SolidBrush(Color.FromArgb(37, 88, 175));
        using var p2Brush = new SolidBrush(Color.FromArgb(174, 50, 55));
        graphics.DrawString("P1 GOAL ↑", font, p1Brush, boardRect.Left, boardRect.Top - 25);
        graphics.DrawString("P2 GOAL ↓", font, p2Brush, boardRect.Left, boardRect.Bottom + 20);
    }
}
