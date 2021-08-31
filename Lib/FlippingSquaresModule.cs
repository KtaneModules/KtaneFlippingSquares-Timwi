using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FlippingSquares;
using Newtonsoft.Json.Linq;
using RT.Util.ExtensionMethods;
using UnityEngine;

using Rnd = UnityEngine.Random;

public class FlippingSquaresModule : MonoBehaviour
{
    public KMBombInfo Bomb;
    public KMBombModule Module;
    public KMAudio Audio;

    public GameObject ButtonParent;
    public GameObject[] ButtonObjects;
    public GameObject OuterFlip;
    public GameObject InnerFlip;
    public MeshRenderer[] ButtonFronts;
    public MeshRenderer[] ButtonBacks;
    public Transform[] ArrowFronts;
    public Transform[] ArrowBacks;
    public KMSelectable[] SquareSelectables;
    public GameObject StatusLight;

    public Color[] Colors = new Color[9];

    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private bool _moduleSolved = false;
    private FlipInfo[] _buttonFlips;
    private int[] _solutionFlipIxs;
    private readonly List<FlipInfo> _performedFlips = new List<FlipInfo>();
    private GameState _gameState;
    private GameState _solutionState;

    private static readonly FlipInfo[] _allFlips = FlipInfo.GetAll();
    private readonly Queue<IEnumerator> _animationQueue = new Queue<IEnumerator>();
    private float? _timeSquarePressed = null;

    private static readonly (int[] arrows, string snChars)[] _arrowConfigs = new (int[] arrows, string snChars)[] {
        (new[] { 0, 1, 2, 7, -1, 3, 6, 5, 4 }, "0O"),
        (new[] { 1, 2, 3, 0, -1, 4, 7, 6, 5 }, "1I"),
        (new[] { 2, 3, 4, 1, -1, 5, 0, 7, 6 }, "2G"),
        (new[] { 3, 4, 5, 2, -1, 6, 1, 0, 7 }, "3H"),
        (new[] { 4, 5, 6, 3, -1, 7, 2, 1, 0 }, "4JW"),
        (new[] { 5, 6, 7, 4, -1, 0, 3, 2, 1 }, "5K"),
        (new[] { 6, 7, 0, 5, -1, 1, 4, 3, 2 }, "6L"),
        (new[] { 7, 0, 1, 6, -1, 2, 5, 4, 3 }, "7M"),
        (new[] { 7, 6, 5, 0, -1, 4, 1, 2, 3 }, "8N"),
        (new[] { 0, 7, 6, 1, -1, 5, 2, 3, 4 }, "9PX"),
        (new[] { 1, 0, 7, 2, -1, 6, 3, 4, 5 }, "AQ"),
        (new[] { 2, 1, 0, 3, -1, 7, 4, 5, 6 }, "BRY"),
        (new[] { 3, 2, 1, 4, -1, 0, 5, 6, 7 }, "CS"),
        (new[] { 4, 3, 2, 5, -1, 1, 6, 7, 0 }, "DT"),
        (new[] { 5, 4, 3, 6, -1, 2, 7, 0, 1 }, "EU"),
        (new[] { 6, 5, 4, 7, -1, 3, 0, 1, 2 }, "FVZ")
    };

    private static readonly (int[] colors, string snChars)[] _colorConfigs = new (int[] colors, string snChars)[] {
        (new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, "08"),
        (new[] { 6, 3, 0, 7, 4, 1, 8, 5, 2 }, "19"),
        (new[] { 8, 7, 6, 5, 4, 3, 2, 1, 0 }, "2"),
        (new[] { 2, 5, 8, 1, 4, 7, 0, 3, 6 }, "3"),
        (new[] { 2, 1, 0, 5, 4, 3, 8, 7, 6 }, "4"),
        (new[] { 0, 3, 6, 1, 4, 7, 2, 5, 8 }, "5"),
        (new[] { 6, 7, 8, 3, 4, 5, 0, 1, 2 }, "6"),
        (new[] { 8, 5, 2, 7, 4, 1, 6, 3, 0 }, "7")
    };

    void Start()
    {
        _moduleId = _moduleIdCounter++;

        var availableFlips = _allFlips.ToList();
        _buttonFlips = new FlipInfo[9];

        foreach (var sq in Enumerable.Range(0, 9).ToArray().Shuffle())
        {
            var eligibleFlips = Enumerable.Range(0, availableFlips.Count).Where(ix => availableFlips[ix].Squares.Any(s => s.Index == sq)).ToArray();
            var flipIx = eligibleFlips[Rnd.Range(0, eligibleFlips.Length)];
            _buttonFlips[sq] = availableFlips[flipIx];
            availableFlips.RemoveAt(flipIx);
            SquareSelectables[sq].OnInteract += SquarePress;
            SquareSelectables[sq].OnInteractEnded += SquareRelease(sq);
        }

        // Determine the goal arrows and colors from the serial number
        var serialNumber = Bomb.QueryWidgets(KMBombInfo.QUERYKEY_GET_SERIAL_NUMBER, null).Where(str => str != null).Select(str => JObject.Parse(str)).First()["serial"].ToString();
        var initialArrowsTop = _arrowConfigs.First(tup => tup.snChars.Contains(serialNumber[0])).arrows.Select(ar => ar < 0 ? ar : (ar + 7) % 8).ToArray();
        var initialColorsTop = _colorConfigs.First(tup => tup.snChars.Contains(serialNumber[5])).colors;

        // Determine bottom colors at random
        var initialColorsBottom = Enumerable.Range(0, 9).ToArray().Shuffle();
        while (Enumerable.Range(0, 9).Any(ix => initialColorsTop[ix] == initialColorsBottom[ix]))
            initialColorsBottom.Shuffle();

        // Place arrows randomly on the bottom faces, but with the opposite parities
        var initialArrowsBottom = new int[9];
        var even = Enumerable.Range(0, 4).Select(i => i * 2).ToList().Shuffle();
        var odd = Enumerable.Range(0, 4).Select(i => i * 2 + 1).ToList().Shuffle();

        for (var i = 0; i < 9; i++)
        {
            var counterpart = initialArrowsTop[Array.IndexOf(initialColorsTop, initialColorsBottom[i])];
            if (counterpart == -1)
                initialArrowsBottom[i] = -2;
            else
            {
                var lst = counterpart % 2 != 0 ? even : odd;
                initialArrowsBottom[i] = lst[0];
                lst.RemoveAt(0);
            }
        }

        _solutionState = new GameState(initialArrowsTop, initialColorsTop, initialArrowsBottom, initialColorsBottom);
        _solutionFlipIxs = Enumerable.Range(0, 9).ToArray().Shuffle().Take(4).ToArray();

        _gameState = _solutionState;
        foreach (var flipIx in _solutionFlipIxs)
            _gameState = _gameState.PerformFlip(_buttonFlips[flipIx]);
        Array.Reverse(_solutionFlipIxs);

        Debug.Log($"[Flipping Squares #{_moduleId}] Initial state:\n{_gameState}");
        Debug.Log($"[Flipping Squares #{_moduleId}] Intended solution:\n{_solutionState}");
        Debug.Log($"[Flipping Squares #{_moduleId}] Possible sequence: {_solutionFlipIxs.Select(sq => sq + 1).JoinString(", ")}");
        Debug.Log($"[Flipping Squares #{_moduleId}] Flips:\n{_buttonFlips.JoinString("\n")}");

        UpdateVisuals(_gameState);
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
            else
                yield return null;
        }
    }

    private bool SquarePress()
    {
        if (_moduleSolved)
            return false;
        _timeSquarePressed = Time.time;
        return false;
    }

    private Action SquareRelease(int sq)
    {
        return delegate
        {
            if (_moduleSolved)
                return;

            if (Time.time - _timeSquarePressed < .3f)
            {
                // short press: perform a flip
                if (_performedFlips.Count > 0 && _performedFlips[_performedFlips.Count - 1] == _buttonFlips[sq])
                    _performedFlips.RemoveAt(_performedFlips.Count - 1);
                else
                    _performedFlips.Add(_buttonFlips[sq]);

                _gameState = _gameState.PerformFlip(_buttonFlips[sq]);
                if (_gameState.Equals(_solutionState))
                    _moduleSolved = true;
                _animationQueue.Enqueue(FlipSquares(_buttonFlips[sq], _gameState, _moduleSolved, fast: false));
            }
            else
            {
                // long press: reset
                for (int i = _performedFlips.Count - 1; i >= 0; i--)
                {
                    _gameState = _gameState.PerformFlip(_performedFlips[i]);
                    _animationQueue.Enqueue(FlipSquares(_performedFlips[i], _gameState, solveAtEnd: false, fast: true));
                }
                _performedFlips.Clear();
            }
            _timeSquarePressed = null;
        };
    }

    private IEnumerator FlipSquares(FlipInfo flip, GameState finalState, bool solveAtEnd, bool fast)
    {
        yield return null;

        Audio.PlaySoundAtTransform("SquareTurn", SquareSelectables[flip.Squares[flip.Squares.Length / 2].Index].transform);
        OuterFlip.transform.localPosition = new Vector3(-.045f + .0225f * flip.Center.X, 0f, .045f - .0225f * flip.Center.Y);
        OuterFlip.transform.localEulerAngles = new Vector3(0f, 45 * (int) flip.Direction, 0f);
        InnerFlip.transform.localRotation = Quaternion.identity;
        foreach (var square in flip.Squares)
            ButtonObjects[square.Index].transform.parent = InnerFlip.transform;

        var duration = fast ? .1f : .5f;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            InnerFlip.transform.localEulerAngles = new Vector3(fast ? Mathf.Lerp(0f, -180f, elapsed / duration) : Easing.InOutQuad(elapsed, 0f, -180f, duration), 0f, 0f);
            yield return null;
            elapsed += Time.deltaTime;
        }
        InnerFlip.transform.localEulerAngles = new Vector3(-180f, 0f, 0f);
        foreach (var square in flip.Squares)
        {
            ButtonObjects[square.Index].transform.parent = ButtonParent.transform;
            ButtonObjects[square.Index].transform.localPosition = new Vector3(-0.045f + .045f * square.X, 0, 0.045f - .045f * square.Y);
            ButtonObjects[square.Index].transform.localRotation = Quaternion.identity;
        }
        UpdateVisuals(finalState);

        if (solveAtEnd)
        {
            Debug.Log($"[Flipping Squares #{_moduleId}] Module solved.");
            Module.HandlePass();
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.CorrectChime, transform);
        }
    }

    private void UpdateVisuals(GameState state)
    {
        for (var i = 0; i < 9; i++)
        {
            ButtonFronts[i].material.color = Colors[state.TopColors[i]];
            ButtonBacks[i].material.color = Colors[state.BottomColors[i]];

            void setArrow(Transform arrow, Transform button, int dir, bool bottom)
            {
                if (dir < 0)
                {
                    arrow.gameObject.SetActive(false);
                    if (dir == -1)
                    {
                        StatusLight.transform.parent = button;
                        StatusLight.transform.localPosition = new Vector3(0, .125f, 0);
                        StatusLight.transform.localRotation = Quaternion.identity;
                        StatusLight.transform.localScale = new Vector3(3f, 12.6f, 3f);
                    }
                }
                else
                {
                    arrow.gameObject.SetActive(true);
                    arrow.localRotation = Quaternion.Euler(90, 45 * (bottom ? (8 - dir) % 8 : dir), 0);
                }
            }

            setArrow(ArrowFronts[i], ButtonFronts[i].transform, state.TopArrows[i], false);
            setArrow(ArrowBacks[i], ButtonBacks[i].transform, state.BottomArrows[i], true);
        }
    }

#pragma warning disable 414
    private readonly string TwitchHelpMessage = @"!{0} a1 c2 | !{0} 1 6 | !{0} reset";
#pragma warning restore 414

    IEnumerator ProcessTwitchCommand(string command)
    {
        if (Regex.IsMatch(command, @"^\s*reset\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            yield return null;
            yield return SquareSelectables[0];
            yield return new WaitForSeconds(.5f);
            yield return SquareSelectables[0];
            yield break;
        }

        var coords = command.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (coords[0] == "press")
            coords = coords.Skip(1).ToArray();
        var btns = new List<KMSelectable>();
        foreach (var coord in coords)
        {
            var m = Regex.Match(coord, @"^\s*([a-c])\s*([1-3])\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
                btns.Add(SquareSelectables[m.Groups[1].Value.ToUpperInvariant()[0] - 'A' + 3 * (m.Groups[2].Value[0] - '1')]);
            else
            {
                m = Regex.Match(coord, @"^\s*([1-9])\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (m.Success)
                    btns.Add(SquareSelectables[m.Groups[1].Value[0] - '1']);
                else
                    yield break;
            }
        }
        yield return null;
        yield return btns;
    }

    IEnumerator TwitchHandleForcedSolve()
    {
        if (_moduleSolved)
            yield break;

        if (_performedFlips.Count > 0)
        {
            SquareSelectables[0].OnInteract();
            yield return new WaitForSeconds(.5f);
            SquareSelectables[0].OnInteractEnded();
            yield return new WaitForSeconds(.1f);
        }

        foreach (var flipIx in _solutionFlipIxs)
        {
            SquareSelectables[flipIx].OnInteract();
            yield return new WaitForSeconds(.1f);
            SquareSelectables[flipIx].OnInteractEnded();
            yield return new WaitForSeconds(.1f);
        }

        while (!_moduleSolved)
            yield return true;
    }
}
