namespace TSMapEditor.AI
{
    /// <summary>
    /// Represents a rectangular selection on the map made by the AI selection tool.
    /// </summary>
    public class AISelection
    {
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        public AISelection(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public override string ToString() => $"({X},{Y}) {Width}x{Height}";
    }
}
