using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KModkit;

using Rnd = UnityEngine.Random;

public class FlippingSquaresScript : MonoBehaviour
{
    public KMBombInfo Info;
    public KMBombModule Module;
    public KMAudio Audio;

    public GameObject ButtonParent;
    public GameObject[] ButtonObjects;
    public GameObject OuterFlip;
    public GameObject InnerFlip;
    public MeshRenderer[] ButtonFronts;
    public MeshRenderer[] ButtonBacks;
    public KMSelectable[] SquareSelectables;

    public Color[] Colors = new Color[9];

    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private bool _moduleSolved;
    private FlipInfo[] _flips;
    private GameState _gameState;

    private static readonly FlipInfo[] _allFlips = FlipInfo.GetAll();
    private readonly Queue<IEnumerator> _animationQueue = new Queue<IEnumerator>();

    private struct Coord
    {
        public int Value;
        public int Width;
        public int X { get { return Value % Width; } }
        public int Y { get { return Value / Width; } }
        public Coord(int width, int value) { Value = value; Width = width; }
        public Coord(int width, int x, int y) { Value = x + width * y; Width = width; }

        public override string ToString()
        {
            return string.Format("{2}=({0}, {1})", X, Y, Value);
        }
    }

    private struct GameState
    {
        // Null means it’s the status light
        public int?[] TopArrows { get; private set; }
        public int?[] BottomArrows { get; private set; }
        public GameState(int?[] topArrows, int?[] bottomArrows)
        {
            TopArrows = topArrows;
            BottomArrows = bottomArrows;
        }

        public GameState PerformFlip(FlipInfo flip)
        {
            var top = TopArrows.ToArray();
            var bottom = BottomArrows.ToArray();

            foreach (var sq in flip.Squares)
            {
                var nc = flip.TranslateSquare(sq);
                top[nc.Value] = flip.TranslateArrow(BottomArrows[sq.Value]);
                bottom[nc.Value] = flip.TranslateArrow(TopArrows[sq.Value]);
            }

            return new GameState(top, bottom);
        }
    }

    private struct FlipInfo
    {
        public FlipDirection Direction { get; private set; }
        public Coord[] Squares { get; private set; }
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
            return string.Format("Squares [{0}], Dir {1}, Center {2}", Squares.Join(", "), Direction, Center);
        }
    }

    private enum FlipDirection
    {
        TopToBottom,
        TopRightToBottomLeft,
        RightToLeft,
        BottomRightToTopLeft,
        BottomToTop,
        BottomLeftToTopRight,
        LeftToRight,
        TopLeftToBottomRight
    }

    void Start()
    {
        _moduleId = _moduleIdCounter++;

        var availableFlips = _allFlips.ToList();
        _flips = new FlipInfo[9];

        foreach (var sq in Enumerable.Range(0, 9).ToArray().Shuffle())
        {
            var eligibleFlips = Enumerable.Range(0, availableFlips.Count).Where(ix => availableFlips[ix].Squares.Any(s => s.Value == sq)).ToArray();
            var flipIx = eligibleFlips[Rnd.Range(0, eligibleFlips.Length)];
            _flips[sq] = availableFlips[flipIx];
            availableFlips.RemoveAt(flipIx);
            SquareSelectables[sq].OnInteract += SquarePress(sq);
        }

        var initialTop = Enumerable.Range(0, 9).Select(i => i == 4 ? null : (int?) Rnd.Range(0, 8)).ToArray();
        var initialBottom = Enumerable.Range(0, 9).Select(i => (int?) Rnd.Range(0, 8)).ToArray();
        _gameState = new GameState(initialTop, initialBottom);

        StartCoroutine(AnimationQueue());
    }

    private IEnumerator AnimationQueue()
    {
        while (!_moduleSolved || _animationQueue.Count > 0)
        {
            if (_animationQueue.Count > 0)
            {
                var item = _animationQueue.Dequeue();
                while (item.MoveNext())
                    yield return item.Current;
            }
            yield return null;
        }
    }

    private KMSelectable.OnInteractHandler SquarePress(int sq)
    {
        return delegate
        {
            if (_moduleSolved)
                return false;

            _animationQueue.Enqueue(FlipSquares(_flips[sq]));
            _gameState = _gameState.PerformFlip(_flips[sq]);
            return false;
        };
    }

    private IEnumerator FlipSquares(FlipInfo flip)
    {
        OuterFlip.transform.localPosition = new Vector3(-.045f + .0225f * flip.Center.X, 0f, .045f - .0225f * flip.Center.Y);
        OuterFlip.transform.localEulerAngles = new Vector3(0f, 45 * (int) flip.Direction, 0f);
        InnerFlip.transform.localRotation = Quaternion.identity;
        foreach (var square in flip.Squares)
            ButtonObjects[square.Value].transform.parent = InnerFlip.transform;

        var duration = 0.5f;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            InnerFlip.transform.localEulerAngles = new Vector3(Easing.InOutQuad(elapsed, 0f, 180f, duration), 0f, 0f);
            yield return null;
            elapsed += Time.deltaTime;
        }
        InnerFlip.transform.localEulerAngles = new Vector3(180f, 0f, 0f);
        foreach (var square in flip.Squares)
        {
            ButtonObjects[square.Value].transform.parent = ButtonParent.transform;
            ButtonObjects[square.Value].transform.localPosition = new Vector3(-0.045f + .045f * square.X, 0, 0.045f - .045f * square.Y);
            ButtonObjects[square.Value].transform.localRotation = Quaternion.identity;
        }
    }
}
