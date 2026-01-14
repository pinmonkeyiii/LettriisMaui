using LettriisMaui.Models.Rendering;
using Microsoft.Maui.Graphics;

namespace LettriisMaui.Graphics;

public sealed class PiecePreviewDrawable : IDrawable
{
    public RenderPiece? Piece { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = new Color(0.10f, 0.10f, 0.13f);
        canvas.FillRoundedRectangle(dirtyRect, 14);

        var piece = Piece;
        if (piece is null) return;

        // Compute bounds in “grid units”
        int maxCol = piece.Blocks.Max(b => b.Col);
        int maxRow = piece.Blocks.Max(b => b.Row);

        int cols = maxCol + 1;
        int rows = maxRow + 1;

        float padding = 12f;
        float availableW = Math.Max(1, dirtyRect.Width - padding * 2);
        float availableH = Math.Max(1, dirtyRect.Height - padding * 2);

        float cell = MathF.Min(availableW / cols, availableH / rows);
        cell = MathF.Max(6, cell);

        float boardW = cell * cols;
        float boardH = cell * rows;

        float left = dirtyRect.Left + (dirtyRect.Width - boardW) / 2f;
        float top = dirtyRect.Top + (dirtyRect.Height - boardH) / 2f;

        float inset = cell * 0.10f;
        float radius = cell * 0.20f;

        foreach (var (c, r, ch) in piece.Blocks)
        {
            var rect = new RectF(left + c * cell, top + r * cell, cell, cell).Inflate(-inset, -inset);

            canvas.FillColor = piece.Fill;
            canvas.FillRoundedRectangle(rect, radius);

            canvas.FontColor = Colors.White;
            canvas.FontSize = MathF.Max(10, cell * 0.55f);
            canvas.DrawString(ch.ToString(), rect, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}