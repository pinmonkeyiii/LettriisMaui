
namespace LettriisMaui.Models;

public static class GameConstants
{
    public const int BlockSize = 30;
    public const int BoardWidth = 500;
    public const int BoardHeight = 1000;

    // Python runtime uses board_height=1000, block_size=30 => rows=33; cols=10.
    public const int Cols = BoardWidth / BlockSize;
    public const int Rows = BoardHeight / BlockSize;

    public const int ScreenWidth = BoardWidth * 2;
    public const int ScreenHeight = BoardHeight;

    public static int MinWordLength(int level) => 3 + Math.Min(2, level / 10);
}
