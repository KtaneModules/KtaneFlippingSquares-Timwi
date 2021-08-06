using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Rnd = UnityEngine.Random;
using KModkit;

public class FlippingSquaresScript : MonoBehaviour
{

    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private bool _moduleSolved;

    public KMBombInfo Info;
    public KMBombModule Module;
    public KMAudio Audio;

    public GameObject ButtonParent;
    public GameObject[] ButtonObjects;
    public GameObject OuterFlip;
    public GameObject InnerFlip;

    public MeshRenderer[] ButtonFronts;
    public MeshRenderer[] ButtonBacks;

    public GameObject[] selectionObjs;
    public GameObject posLocator;

    public Color[] colors = new Color[9];

    public KMSelectable[] squareSelectables;

    private bool isFlipping = false;
    private List<int> chosenSquares = new List<int>();

    private float[] xPos = { -0.045f, 0f, 0.045f };
    private float[] zPos = { 0.045f, 0f, -0.045f };
    private float[] twoWideCols = { -0.0225f, 0.0225f };
    private float[] twoWideRows = { 0.0225f, -0.0225f };

    private float flipXPos;
    private float flipZPos;
    private bool validFlip = true;

    private int[] squareColors = new int[9];
    private int[] squareArrows = new int[9];
    private bool[] squareFlippedDown = new bool[9];

    private enum FlipDirection
    {
        BottomToTop, BottomLeftToTopRight, LeftToRight, TopLeftToBottomRight, TopToBottom, TopRightToBottomLeft, RightToLeft, BottomRightToTopLeft
    }

    void Start()
    {
        _moduleId = _moduleIdCounter++;
        for (int i = 0; i < squareSelectables.Length; i++)
            squareSelectables[i].OnInteract += SquarePress(i);
        AssignColors();
        DoRandomFlips();
        //StartCoroutine(FlipSquares(new[] { 0, 1, 3, 4 }, twoBytwoXPos[0], twoBytwoZPos[0]));
    }

    private KMSelectable.OnInteractHandler SquarePress(int i)
    {
        return delegate
        {
            if (!isFlipping)
            {
                if (!chosenSquares.Contains(i))
                {
                    chosenSquares.Add(i);
                    selectionObjs[i].SetActive(true);
                }
                else
                {
                    //flip something
                    for (int so = 0; so < 9; so++)
                        selectionObjs[so].SetActive(false);
                    PerformFlip(chosenSquares);
                    chosenSquares = new List<int>();
                }
            }
            return false;
        };
    }

    private void PerformFlip(List<int> squares)
    {
        if (squares.Count == 1) // ONE SQUARE
        {
            OuterFlip.transform.localPosition = new Vector3(xPos[chosenSquares[0] % 3], 0f, zPos[chosenSquares[0] / 3]);
            flipXPos = xPos[chosenSquares[0] % 3];
            flipZPos = zPos[chosenSquares[0] / 3];
        }
        else if (squares.Count() == 2) // TWO SQUARES
        {
            if (squares[0] / 3 == squares[1] / 3) // are they in the same row
            {
                if (Math.Abs(squares[0] + squares[1]) % 3 == 1) // are they in columns AB
                    flipXPos = twoWideCols[0];
                else if (Math.Abs(squares[0] + squares[1]) % 3 == 0) // are they in columns BC
                    flipXPos = twoWideCols[1];
                else // are they in columns AC
                    flipXPos = xPos[1];
                flipZPos = zPos[squares[0] / 3];
            }
            else if (squares[0] % 3 == squares[1] % 3) // are they in the same column
            {
                if (Math.Abs(squares[0] - squares[1]) == 3 && squares[0] + squares[1] < 8) // are they in rows 12
                    flipZPos = twoWideRows[0];
                else if (Math.Abs(squares[0] - squares[1]) == 3 && squares[0] + squares[1] > 8) // are they in rows 23
                    flipZPos = twoWideRows[1];
                else // are they in rows 13
                    flipZPos = zPos[1];
                flipXPos = xPos[squares[0] % 3];
            }
            else if (Math.Abs(squares[0] - squares[1]) == 2 || Math.Abs(squares[0] - squares[1]) == 4) // are they at corners TR BL or TL BR (in 2 by 2 area)
            {
                if ((squares[0] + squares[1]) % 6 == 0) // are they in columns BC
                    flipXPos = twoWideCols[1];
                else // are they in columns AB
                    flipXPos = twoWideCols[0];
                if (squares[0] + squares[1] < 8) // are they in rows 12
                    flipZPos = twoWideRows[0];
                else // are they in rows 23
                    flipZPos = twoWideRows[1];
            }
            else if (squares[0] + squares[1] == 8) // are they in opposite corners in 3 by 3 area
            {
                flipXPos = xPos[1];
                flipZPos = zPos[1];
            }
            else
            {
                Debug.LogFormat("not a valid flip");
                validFlip = false;
            }
        }
        if (validFlip)
        {
            posLocator.transform.localPosition = new Vector3(flipXPos, 0.02f, flipZPos);
            posLocator.SetActive(true);
            StartCoroutine(FlipSquares(squares, flipXPos, flipZPos, FlipDirection.TopToBottom));
        }
        validFlip = true;
    }

    private void DoRandomFlips()
    {

    }

    private void AssignColors()
    {
        for (int i = 0; i < 9; i++)
            ButtonObjects[i].transform.GetChild(0).GetComponent<MeshRenderer>().material.color = colors[i];
    }

    private IEnumerator FlipSquares(IEnumerable<int> squares, float x, float z, FlipDirection f)
    {
        isFlipping = true;
        //yield return new WaitForSeconds(1f);
        OuterFlip.transform.localPosition = new Vector3(x, 0f, z);
        OuterFlip.transform.localEulerAngles = new Vector3(0f, 45 * (int)f, 0f);
        foreach (var square in squares)
            ButtonObjects[square].transform.parent = InnerFlip.transform;
        var duration = 0.5f;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            InnerFlip.transform.localEulerAngles = new Vector3(Easing.InOutQuad(elapsed, 0f, 180f, duration), 0f, 0f);
            yield return null;
            elapsed += Time.deltaTime;
        }
        InnerFlip.transform.localEulerAngles = new Vector3(180f, 0f, 0f);
        foreach (var square in squares)
        {
            ButtonObjects[square].transform.parent = ButtonParent.transform;
            ButtonObjects[square].transform.localPosition = new Vector3(-0.045f + .045f * (square % 3), 0, 0.045f - .045f * (square / 3));
            ButtonObjects[square].transform.localRotation = Quaternion.identity;
        }
        InnerFlip.transform.localRotation = Quaternion.identity;
        OuterFlip.transform.localRotation = Quaternion.identity;
        posLocator.SetActive(false);
        isFlipping = false;
    }
}
