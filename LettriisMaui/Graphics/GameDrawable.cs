using LettriisMaui.Graphics;
using LettriisMaui.Models.Rendering;
using Microsoft.Maui.Graphics;

namespace LettriisMaui.Graphics;

public sealed class GameDrawable : IDrawable
{
    public GameRenderState? RenderState { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = new Color(0f, 0f, 0f, 0.20f);
        canvas.FillRectangle(dirtyRect);

        var state = RenderState;
        if (state is null) return;

        var layout = BoardLayoutCalculator.Calculate(dirtyRect, state.Columns, state.Rows);

        canvas.FillColor = new Color(0.08f, 0.08f, 0.10f);
        canvas.FillRoundedRectangle(layout.BoardRect, 12);

        DrawGrid(canvas, layout, state.Columns, state.Rows);
        DrawLocked(canvas, layout, state);
        DrawFlash(canvas, layout, state);
        DrawGhost(canvas, layout, state);
        DrawActive(canvas, layout, state);

        canvas.StrokeColor = new Color(0.3f, 0.3f, 0.35f);
        canvas.StrokeSize = MathF.Max(1, layout.CellSize * 0.06f);
        canvas.DrawRoundedRectangle(layout.BoardRect, 12);
    }

    private static void DrawGrid(ICanvas canvas, BoardLayout layout, int cols, int rows)
    {
        canvas.StrokeColor = new Color(0.15f, 0.15f, 0.18f);
        canvas.StrokeSize = MathF.Max(1, layout.CellSize * 0.03f);

        for (int c = 1; c < cols; c++)
        {
            float x = layout.Left + c * layout.CellSize;
            canvas.DrawLine(x, layout.Top, x, layout.Top + rows * layout.CellSize);
        }

        for (int r = 1; r < rows; r++)
        {
            float y = layout.Top + r * layout.CellSize;
            canvas.DrawLine(layout.Left, y, layout.Left + cols * layout.CellSize, y);
        }
    }

    private static void DrawLocked(ICanvas canvas, BoardLayout layout, GameRenderState state)
    {
        float inset = layout.CellSize * 0.08f;

        for (int row = 0; row < state.Rows; row++)
            for (int col = 0; col < state.Columns; col++)
            {
                char ch = state.BoardLetters[row, col];
                if (ch == '\0') continue;

                DrawTile(canvas, layout, col, row, ch, Colors.SteelBlue, inset);
            }
    }

    private static void DrawActive(ICanvas canvas, BoardLayout layout, GameRenderState state)
    {
        var p = state.ActivePiece;
        if (p is null) return;

        float inset = layout.CellSize * 0.06f;

        foreach (var (col, row, ch) in p.Blocks)
            DrawTile(canvas, layout, col, row, ch, p.Fill, inset);
    }

    private static void DrawFlash(ICanvas canvas, BoardLayout layout, GameRenderState state)
    {
        if (state.FlashCells.Count == 0) return;

        // A bright overlay that reads as a “clear”
        canvas.FillColor = Colors.Gold.WithAlpha(0.45f);

        float inset = layout.CellSize * 0.10f;
        float radius = layout.CellSize * 0.18f;

        foreach (var (col, row) in state.FlashCells)
        {
            if (col < 0 || col >= state.Columns || row < 0 || row >= state.Rows)
                continue;

            var rect = layout.CellRect(col, row).Inflate(-inset, -inset);
            canvas.FillRoundedRectangle(rect, radius);
        }
    }


    private static void DrawGhost(ICanvas canvas, BoardLayout layout, GameRenderState state)
    {
        var p = state.GhostPiece;
        if (p is null) return;

        float inset = layout.CellSize * 0.06f;

        foreach (var (col, row, ch) in p.Blocks)
            DrawTile(canvas, layout, col, row, ch, p.Fill, inset);
    }

    private static void DrawTile(ICanvas canvas, BoardLayout layout, int col, int row, char letter, Color fill, float inset)
    {
        var rect = layout.CellRect(col, row).Inflate(-inset, -inset);
        float radius = layout.CellSize * 0.18f;

        canvas.FillColor = fill;
        canvas.FillRoundedRectangle(rect, radius);

        canvas.StrokeColor = Colors.White.WithAlpha(0.25f);
        canvas.StrokeSize = MathF.Max(1, layout.CellSize * 0.04f);
        canvas.DrawRoundedRectangle(rect, radius);

        canvas.FontColor = Colors.White;
        canvas.FontSize = MathF.Max(10, layout.CellSize * 0.55f);
        canvas.DrawString(letter.ToString(), rect, HorizontalAlignment.Center, VerticalAlignment.Center);
    }
}
