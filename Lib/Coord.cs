struct Coord
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
