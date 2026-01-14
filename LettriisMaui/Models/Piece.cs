
using LettriisMaui.Services;

namespace LettriisMaui.Models;

public sealed class Piece
{
    public List<GridCell> ShapeOffsets { get; }
    public List<char> Letters { get; }
    public List<GridCell> Cells { get; private set; } = new();

    public Piece(IEnumerable<GridCell> shapeOffsets, IEnumerable<char> letters)
    {
        ShapeOffsets = shapeOffsets.ToList();
        Letters = letters.ToList();
        ResetToSpawn();
    }

    public void ResetToSpawn()
    {
        int spawnX = GameConstants.Cols / 2;
        int spawnY = 0;
        Cells = ShapeOffsets.Select(o => new GridCell(spawnX + o.X, spawnY + o.Y)).ToList();
    }

    public bool CanMove(GameState state, int dx = 0, int dy = 0)
    {
        var candidate = Cells.Select(c => new GridCell(c.X + dx, c.Y + dy)).ToList();
        return !WouldCollide(state, candidate);
    }

    public bool Move(GameState state, int dx = 0, int dy = 0)
    {
        if (!CanMove(state, dx, dy)) return false;
        Cells = Cells.Select(c => new GridCell(c.X + dx, c.Y + dy)).ToList();
        return true;
    }

    public bool TryRotate(GameState state)
    {
        // Rotate around first cell (pivot), matching Python logic.
        var pivot = Cells[0];
        int px = pivot.X, py = pivot.Y;

        var rotated = new List<GridCell>();
        foreach (var c in Cells)
        {
            int rx = px - (c.Y - py);
            int ry = py + (c.X - px);
            rotated.Add(new GridCell(rx, ry));
        }

        foreach (var (kx, ky) in new (int, int)[] { (0, 0), (1, 0), (-1, 0), (0, -1) })
        {
            var candidate = rotated.Select(c => new GridCell(c.X + kx, c.Y + ky)).ToList();
            if (!WouldCollide(state, candidate))
            {
                Cells = candidate;
                return true;
            }
        }
        return false;
    }

    public int HardDrop(GameState state)
    {
        int dropped = 0;
        while (CanMove(state, 0, 1))
        {
            Move(state, 0, 1);
            dropped++;
        }
        return dropped;
    }

    public void LockToBoard(GameState state)
    {
        for (int i = 0; i < Cells.Count; i++)
        {
            var c = Cells[i];
            state.Board[c.Y, c.X] = Letters[i];
        }
    }

    private static bool WouldCollide(GameState state, List<GridCell> cells)
    {
        foreach (var c in cells)
        {
            if (c.X < 0 || c.X >= GameConstants.Cols || c.Y < 0 || c.Y >= GameConstants.Rows)
                return true;
            if (state.Board[c.Y, c.X] != '\0')
                return true;
        }
        return false;
    }
}
