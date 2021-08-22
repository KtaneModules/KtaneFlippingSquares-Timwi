using System;
using System.Collections.Generic;
using System.Linq;
using RT.Util.ExtensionMethods;

struct FlipInfo
{
    public FlipDirection Direction { get; private set; }
    public Coord[] Squares { get; private set; }

    // Centers are on a 5×5 grid; 0/2/4 are on the buttons, 1/3 are between them
    public Coord Center { get; private set; }

    private FlipInfo(FlipDirection direction, Coord[] squares, Coord center)
    {
        Direction = direction;
        Squares = squares;
        Center = center;
    }

    public static FlipInfo? Generate(FlipDirection direction, Coord[] squares)
    {
        // Centers are on a 5×5 grid; 0/2/4 are on the buttons, 1/3 are between them
        var center =
            /* 1 */
            CenterFrom(squares, "", 0, 0) ??
            /* 2 */
            CenterFromOrthogonal(direction, squares, "→", 1, 0) ??
            CenterFromOrthogonal(direction, squares, "↓", 0, 1) ??
            CenterFromDiagonal(direction, squares, "↘", 1, 1) ??
            CenterFromDiagonal(direction, squares, "↙", -1, 1) ??

            /* 3 */
            CenterFromOrthogonal(direction, squares, "→→", 2, 0) ??
            CenterFromOrthogonal(direction, squares, "↓↓", 0, 2) ??
            CenterFromForwardDiagonal(direction, squares, "→↙", 1, 1) ??
            CenterFromBackwardDiagonal(direction, squares, "→↓", 1, 1) ??
            CenterFromBackwardDiagonal(direction, squares, "↓→", 1, 1) ??
            CenterFromForwardDiagonal(direction, squares, "↙→", -1, 1) ??

            /* 4 */
            CenterFrom(squares, "→↙→", 1, 1) ??

            /* 6 */
            CenterFromOrthogonal(direction, squares, "↓↗↓↗↓", 2, 1) ??
            CenterFromOrthogonal(direction, squares, "→↙→↙→", 1, 2) ??
            null;
        if (center == null)
            return null;
        return new FlipInfo(direction, squares, center.Value);
    }

    // Flipping like this: ↕ or ↔
    private static Coord? CenterFromOrthogonal(FlipDirection direction, Coord[] squares, string repr, int relX, int relY)
    {
        switch (direction)
        {
            case FlipDirection.TopToBottom:
            case FlipDirection.RightToLeft:
            case FlipDirection.BottomToTop:
            case FlipDirection.LeftToRight:
                return CenterFrom(squares, repr, relX, relY);
        }
        return null;
    }

    // Flipping like this: ⤢ or ⤡
    private static Coord? CenterFromDiagonal(FlipDirection direction, Coord[] squares, string repr, int relX, int relY)
    {
        if (direction == FlipDirection.TopRightToBottomLeft || direction == FlipDirection.BottomLeftToTopRight ||
            direction == FlipDirection.TopLeftToBottomRight || direction == FlipDirection.BottomRightToTopLeft)
            return CenterFrom(squares, repr, relX, relY);
        return null;
    }

    // Flipping like this: ⤢
    private static Coord? CenterFromForwardDiagonal(FlipDirection direction, Coord[] squares, string repr, int relX, int relY)
    {
        if (direction == FlipDirection.TopRightToBottomLeft || direction == FlipDirection.BottomLeftToTopRight)
            return CenterFrom(squares, repr, relX, relY);
        return null;
    }

    // Flipping like this: ⤡
    private static Coord? CenterFromBackwardDiagonal(FlipDirection direction, Coord[] squares, string repr, int relX, int relY)
    {
        if (direction == FlipDirection.TopLeftToBottomRight || direction == FlipDirection.BottomRightToTopLeft)
            return CenterFrom(squares, repr, relX, relY);
        return null;
    }

    // Calculates the center of the flip (or null if the set of squares is not valid).
    private static Coord? CenterFrom(Coord[] squares, string repr, int relX, int relY)
    {
        if (repr.Length + 1 != squares.Length)
            return null;
        for (var i = 0; i < squares.Length; i++)
        {
            var x = squares[i].X;
            var y = squares[i].Y;
            for (var j = 0; j < repr.Length; j++)
            {
                switch (repr[j])
                {
                    case '→': x++; break;
                    case '←': x--; break;
                    case '↑': y--; break;
                    case '↓': y++; break;
                    case '↗': x++; y--; break;
                    case '↘': x++; y++; break;
                    case '↙': x--; y++; break;
                    case '↖': x--; y--; break;
                }
                if (!squares.Any(s => s.X == x && s.Y == y))
                    goto busted;
            }
            return new Coord(5, 2 * squares[i].X + relX, 2 * squares[i].Y + relY);

            busted:;
        }
        return null;
    }

    /// <summary>Given an arrow direction, calculates what the new arrow direction is after performing the flip.</summary>
    public int? TranslateArrow(int? arrow)
    {
        // Most ridiculously optimized formula — don’t ask why it works
        return arrow == null ? (int?) null : ((int) Direction * 2 + 12 - arrow.Value) % 8;
    }

    /// <summary>Given a square on the board, calculates where the square ends up after performing the flip.</summary>
    public Coord TranslateSquare(Coord square)
    {
        return
            Direction == FlipDirection.TopToBottom || Direction == FlipDirection.BottomToTop ? new Coord(square.Width, square.X, Center.Y - square.Y) :
            Direction == FlipDirection.TopRightToBottomLeft || Direction == FlipDirection.BottomLeftToTopRight ? new Coord(square.Width, (Center.X + Center.Y) / 2 - square.Y, (Center.Y + Center.X) / 2 - square.X) :
            Direction == FlipDirection.RightToLeft || Direction == FlipDirection.LeftToRight ? new Coord(square.Width, Center.X - square.X, square.Y) :
            new Coord(square.Width, (Center.X - Center.Y) / 2 + square.Y, (Center.Y - Center.X) / 2 + square.X);
    }

    public static FlipInfo[] GetAll()
    {
        var allFlips = new List<FlipInfo>();
        var allDirections = (FlipDirection[]) Enum.GetValues(typeof(FlipDirection));
        for (var i = 1; i < (1 << 9); i++)
        {
            var squares = Enumerable.Range(0, 9).Where(bit => (i & (1 << bit)) != 0).Select(sq => new Coord(3, sq)).ToArray();
            foreach (var dir in allDirections)
            {
                var flip = Generate(dir, squares);
                if (flip != null)
                    allFlips.Add(flip.Value);
            }
        }
        return allFlips.ToArray();
    }

    public override string ToString()
    {
        return string.Format("Squares [{0}], Dir {1}, Center {2}", Squares.JoinString(", "), Direction, Center);
    }
}
