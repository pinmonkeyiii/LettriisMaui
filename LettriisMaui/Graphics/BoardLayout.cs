using Microsoft.Maui.Graphics;

namespace LettriisMaui.Graphics;

public readonly record struct BoardLayout(
    RectF BoardRect,
    float CellSize,
    float Left,
    float Top)
{
    public RectF CellRect(int col, int row)
        => new RectF(Left + col * CellSize, Top + row * CellSize, CellSize, CellSize);
}

public static class BoardLayoutCalculator
{
    public static BoardLayout Calculate(RectF dirtyRect, int cols, int rows, float padding = 16f)
    {
        float availableW = Math.Max(0, dirtyRect.Width - padding * 2);
        float availableH = Math.Max(0, dirtyRect.Height - padding * 2);

        float cellSize = MathF.Min(availableW / cols, availableH / rows);
        cellSize = MathF.Max(1, cellSize);

        float boardW = cellSize * cols;
        float boardH = cellSize * rows;

        float left = dirtyRect.Left + (dirtyRect.Width - boardW) / 2f;
        float top = dirtyRect.Top + (dirtyRect.Height - boardH) / 2f;

        return new BoardLayout(new RectF(left, top, boardW, boardH), cellSize, left, top);
    }
}
