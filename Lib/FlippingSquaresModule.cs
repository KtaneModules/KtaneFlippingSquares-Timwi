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
    private FlipInfo[] _solutionFlips;
    private readonly List<FlipInfo> _performedFlips = new List<FlipInfo>();
    private GameState _gameState;

    private static readonly FlipInfo[] _allFlips = FlipInfo.GetAll();
    private readonly Queue<IEnumerator> _animationQueue = new Queue<IEnumerator>();

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
            if (initialArrowsTop[i] == -1)
                initialArrowsBottom[i] = -2;
            else
            {
                var lst = initialArrowsTop[i] % 2 != 0 ? even : odd;
                initialArrowsBottom[i] = lst[0];
                lst.RemoveAt(0);
            }

        var iterations = 0;
        tryAgain:
        if (iterations > 100)
            throw new InvalidOperationException();
        iterations++;

        _solutionFlips = Enumerable.Range(0, 9).Select(i => _buttonFlips[Rnd.Range(0, 9)]).ToArray();
        for (var i = 0; i < _solutionFlips.Length; i++)
            for (var j = i + 2; j < _solutionFlips.Length; j++)
                if (_solutionFlips[j].Equals(_solutionFlips[i]) && Enumerable.Range(i, j - i - 1).All(ix => !_solutionFlips[ix].AnyIntersection(_solutionFlips[ix])))
                    goto tryAgain;

        _gameState = new GameState(initialArrowsTop.ToArray(), initialColorsTop, initialArrowsBottom, initialColorsBottom);
        foreach (var flip in _solutionFlips)
            _gameState = _gameState.PerformFlip(flip);
        Array.Reverse(_solutionFlips);

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
            yield return null;
        }
    }

    private KMSelectable.OnInteractHandler SquarePress(int sq)
    {
        return delegate
        {
            if (_moduleSolved)
                return false;

            _gameState = _gameState.PerformFlip(_buttonFlips[sq]);
            _animationQueue.Enqueue(FlipSquares(_buttonFlips[sq], _gameState));
            return false;
        };
    }

    private IEnumerator FlipSquares(FlipInfo flip, GameState finalState)
    {
        OuterFlip.transform.localPosition = new Vector3(-.045f + .0225f * flip.Center.X, 0f, .045f - .0225f * flip.Center.Y);
        OuterFlip.transform.localEulerAngles = new Vector3(0f, 45 * (int) flip.Direction, 0f);
        InnerFlip.transform.localRotation = Quaternion.identity;
        foreach (var square in flip.Squares)
            ButtonObjects[square.Index].transform.parent = InnerFlip.transform;

        var duration = 0.5f;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            InnerFlip.transform.localEulerAngles = new Vector3(Easing.InOutQuad(elapsed, 0f, -180f, duration), 0f, 0f);
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
    }

    private void UpdateVisuals(GameState state)
    {
        for (var i = 0; i < 9; i++)
        {
            ButtonFronts[i].material.color = Colors[_gameState.TopColors[i]];
            ButtonBacks[i].material.color = Colors[_gameState.BottomColors[i]];

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
