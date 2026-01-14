using System;
using System.Collections.Generic;

namespace LettriisMaui.Models.Session
{
    public sealed class GameSessionDto
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;

        public string Username { get; set; } = "";

        // Progression
        public int Score { get; set; }
        public int Level { get; set; }
        public int GravityIntervalMs { get; set; }
        public int WordsFoundCount { get; set; }

        // Hold / cadence
        public bool HoldUsed { get; set; }

        // Board
        // '.' = empty, letters otherwise. Must be Rows lines, each Cols long.
        public string[] BoardRows { get; set; } = Array.Empty<string>();

        // No-repeats + quiz cadence safety
        public List<string> FoundWords { get; set; } = new();
        public List<string> RemovedWords { get; set; } = new();

        // Pieces
        public PieceSnapshotDto? Current { get; set; }
        public PieceSnapshotDto? Next { get; set; }
        public PieceSnapshotDto? Hold { get; set; }
    }
}