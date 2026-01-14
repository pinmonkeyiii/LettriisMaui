using System.Collections.Generic;
using LettriisMaui.Models;

namespace LettriisMaui.Models.Session
{
    public sealed class PieceSnapshotDto
    {
        // Saved absolute position is derived from min cell
        public int MinX { get; set; }
        public int MinY { get; set; }

        // Offsets from (MinX, MinY) in local piece space
        public List<GridCell> Offsets { get; set; } = new();

        // Letters for blocks (same order used by your Piece constructor)
        public List<char> Letters { get; set; } = new();
    }
}