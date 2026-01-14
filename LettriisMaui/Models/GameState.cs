
namespace LettriisMaui.Models;

public sealed class GameState
{
    public char[,] Board { get; } = new char[GameConstants.Rows, GameConstants.Cols];

    public int Score { get; set; } = 0;
    public int Level { get; set; } = 1;
    public int WordsFoundCount { get; set; } = 0;

    public double ScoreMultiplier { get; set; } = 1.0;
    public int GravityIntervalMs { get; set; } = 600;

    public bool NoRepeatsActive { get; set; } = false;
    public HashSet<string> FoundWords { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> RemovedWords { get; } = new();

    public Piece? CurrentPiece { get; set; }
    public Piece? NextPiece { get; set; }
    public Piece? HeldPiece { get; set; }
    public bool HoldUsed { get; set; } = false;

    public void ClearBoard()
    {
        Array.Clear(Board, 0, Board.Length);
        Score = 0;
        Level = 1;
        WordsFoundCount = 0;
        ScoreMultiplier = 1.0;
        GravityIntervalMs = 600;
        NoRepeatsActive = false;
        FoundWords.Clear();
        RemovedWords.Clear();
        CurrentPiece = null;
        NextPiece = null;
        HeldPiece = null;
        HoldUsed = false;
    }
}
