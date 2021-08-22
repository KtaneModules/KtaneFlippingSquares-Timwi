using System.Linq;

struct GameState
{
    // Null means it’s the status light
    public int?[] TopArrows { get; private set; }
    public int[] TopColors { get; private set; }
    public int?[] BottomArrows { get; private set; }
    public int[] BottomColors { get; private set; }
    public GameState(int?[] topArrows, int[] topColors, int?[] bottomArrows, int[] bottomColors)
    {
        TopArrows = topArrows;
        TopColors = topColors;
        BottomArrows = bottomArrows;
        BottomColors = bottomColors;
    }

    public GameState PerformFlip(FlipInfo flip)
    {
        var topArr = TopArrows.ToArray();
        var topCol = TopColors.ToArray();
        var bottomArr = BottomArrows.ToArray();
        var bottomCol = BottomColors.ToArray();

        foreach (var sq in flip.Squares)
        {
            var nc = flip.TranslateSquare(sq);
            topArr[nc.Value] = flip.TranslateArrow(BottomArrows[sq.Value]);
            bottomArr[nc.Value] = flip.TranslateArrow(TopArrows[sq.Value]);
            topCol[nc.Value] = BottomColors[sq.Value];
            bottomCol[nc.Value] = TopColors[sq.Value];
        }

        return new GameState(topArr, topCol, bottomArr, bottomCol);
    }
}
