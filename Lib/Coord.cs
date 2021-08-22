namespace FlippingSquares
{
    struct Coord
    {
        public int Index;
        public int Width;
        public int X => Index % Width;
        public int Y => Index / Width;
        public Coord(int width, int index) { Width = width; Index = index; }
        public Coord(int width, int x, int y) : this(width, x + width * y) { }
        public override string ToString() => $"({X}, {Y})/{Width}";
    }
}