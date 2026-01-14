using Microsoft.Maui.Graphics;

namespace LettriisMaui.Models.Rendering;

public sealed class GameRenderState
{
    public required int Columns { get; init; }
    public required int Rows { get; init; }
    public required char[,] BoardLetters { get; init; }

    public RenderPiece? ActivePiece { get; init; }
    public RenderPiece? GhostPiece { get; init; }
    public IReadOnlyList<(int Col, int Row)> FlashCells { get; init; } = Array.Empty<(int, int)>();
}

public sealed class RenderPiece
{
    public required Color Fill { get; init; }
    public required IReadOnlyList<(int Col, int Row, char Letter)> Blocks { get; init; }
}
