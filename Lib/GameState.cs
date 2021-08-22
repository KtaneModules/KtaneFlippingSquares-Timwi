using System;
using System.Linq;
using RT.Util.ExtensionMethods;

namespace FlippingSquares
{
    struct GameState : IEquatable<GameState>
    {
        // -1 = status light; -2 = empty
        public int[] TopArrows { get; private set; }
        public int[] TopColors { get; private set; }
        public int[] BottomArrows { get; private set; }
        public int[] BottomColors { get; private set; }
        public GameState(int[] topArrows, int[] topColors, int[] bottomArrows, int[] bottomColors)
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
                topArr[nc.Index] = flip.TranslateArrow(BottomArrows[sq.Index]);
                bottomArr[nc.Index] = flip.TranslateArrow(TopArrows[sq.Index]);
                topCol[nc.Index] = BottomColors[sq.Index];
                bottomCol[nc.Index] = TopColors[sq.Index];
            }

            return new GameState(topArr, topCol, bottomArr, bottomCol);
        }

        public override string ToString()
        {
            var arr = new (int[] arrs, int[] colors)[] { (TopArrows, TopColors), (BottomArrows, BottomColors) };
            const string arrows = "·•↑↗→↘↓↙←↖";
            const string colors = "ROYGHCBPI";
            return Enumerable.Range(0, 3).Select(row => arr.Select(tup => Enumerable.Range(0, 3).Select(col => $"{colors[tup.colors[col + 3 * row]]}{arrows[tup.arrs[col + 3 * row] + 2]}").JoinString(" ")).JoinString("    ")).JoinString("\n");
        }

        public bool Equals(GameState other) => TopArrows.SequenceEqual(other.TopArrows) && TopColors.SequenceEqual(other.TopColors);
        public override bool Equals(object obj) => obj is GameState other && Equals(other);
        public override int GetHashCode() => TopArrows.Aggregate(0, (p, n) => unchecked(p * 18341683 + n));
    }
}