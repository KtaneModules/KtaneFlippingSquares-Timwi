using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
            SquareSelectables[sq].OnInteract += SquarePress(sq);
            SquareSelectables[sq].OnInteractEnded += SquareRelease(sq);
        }

        IEnumerable<JObject> edgework(string key) => Bomb.QueryWidgets(key, null).Where(str => str != null).Select(str => JObject.Parse(str));

        // Determine the orientations of the top-face arrows from the serial number and indicators
        var serialNumber = edgework(KMBombInfo.QUERYKEY_GET_SERIAL_NUMBER).First()["serial"].ToString();
        var initialArrowsTop = serialNumber.Select(ch => (ch >= '0' && ch <= '9' ? ch - '0' : ch - 'A' + 1) % 8).Concat(new int[] { 0, 0 }).ToList();
        for (var i = 0; i < 8; i++)
            while (initialArrowsTop.Take(i).Contains(initialArrowsTop[i]))
                initialArrowsTop[i] = (initialArrowsTop[i] + 1) % 8;

        var indicators = edgework(KMBombInfo.QUERYKEY_GET_INDICATOR).Select(obj => (label: (string) obj["label"], lit: (string) obj["on"] == "True")).ToArray();
        if (indicators.Count(ind => ind.lit) > indicators.Count(ind => !ind.lit))
            (initialArrowsTop[6], initialArrowsTop[7]) = (initialArrowsTop[7], initialArrowsTop[6]);
        initialArrowsTop.Insert(4, -1); // status light

        // Determine the goal colors from the serial number and batteries
        var initialColorsTop = serialNumber.Reverse().Select(ch => (ch >= '0' && ch <= '9' ? ch - '0' : ch - 'A' + 1) % 9).Concat(new int[] { 0, 0, 0 }).ToArray();
        for (var i = 0; i < 9; i++)
            while (initialColorsTop.Take(i).Contains(initialColorsTop[i]))
                initialColorsTop[i] = (initialColorsTop[i] + 1) % 9;

        var batteries = edgework(KMBombInfo.QUERYKEY_GET_BATTERIES).Sum(obj => (int) obj["numbatteries"]);
        (initialColorsTop[6], initialColorsTop[7], initialColorsTop[8]) = batteries switch
        {
            0 => (initialColorsTop[6], initialColorsTop[7], initialColorsTop[8]),
            1 => (initialColorsTop[6], initialColorsTop[8], initialColorsTop[7]),
            2 => (initialColorsTop[7], initialColorsTop[6], initialColorsTop[8]),
            3 => (initialColorsTop[8], initialColorsTop[6], initialColorsTop[7]),
            4 => (initialColorsTop[7], initialColorsTop[8], initialColorsTop[6]),
            _ => (initialColorsTop[8], initialColorsTop[7], initialColorsTop[6])
        };

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

        var iterations = 0;
        tryAgain:
        if (iterations > 100)
            throw new InvalidOperationException();
        iterations++;

        _solutionFlipIxs = Enumerable.Range(0, 5).Select(i => Rnd.Range(0, 9)).ToArray();
        for (var i = 0; i < _solutionFlipIxs.Length; i++)
            for (var j = i + 1; j < _solutionFlipIxs.Length; j++)
                if (_solutionFlipIxs[j].Equals(_solutionFlipIxs[i]) && Enumerable.Range(i, j - i - 1).All(ix => !_buttonFlips[_solutionFlipIxs[ix]].AnyIntersection(_buttonFlips[_solutionFlipIxs[i]])))
                    goto tryAgain;

        _solutionState = new GameState(initialArrowsTop.ToArray(), initialColorsTop, initialArrowsBottom, initialColorsBottom);

        Debug.Log($"[Flipping Squares #{_moduleId}] Intended solution:\n{_solutionState}");
        //Debug.Log($"[Flipping Squares #{_moduleId}] Possible sequence: {_solutionFlipIxs.JoinString(", ")}");

        _gameState = _solutionState;
        foreach (var flipIx in _solutionFlipIxs)
            _gameState = _gameState.PerformFlip(_buttonFlips[flipIx]);
        Array.Reverse(_solutionFlipIxs);

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

    private KMSelectable.OnInteractHandler SquarePress(int sq)
    {
        return delegate
        {
            if (_moduleSolved)
                return false;
            _timeSquarePressed = Time.time;
            return false;
        };
    }

    private Action SquareRelease(int sq)
    {
        return delegate
        {
            if (_moduleSolved)
                return;

            if (Time.time - _timeSquarePressed < 1)
            {
                // short press: perform a flip
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
}
