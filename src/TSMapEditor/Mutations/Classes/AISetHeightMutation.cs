using System.Collections.Generic;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.UI;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// A mutation that sets the height level of all cells in a rectangular area.
    /// Used by the AI system's "set_height" operation.
    /// Height levels in TS/RA2 range from 0 (lowest) to 14 (highest).
    /// </summary>
    public class AISetHeightMutation : Mutation
    {
        public AISetHeightMutation(IMutationTarget mutationTarget,
            int startX, int startY, int width, int height,
            byte targetHeight, string description)
            : base(mutationTarget)
        {
            this.startX = startX;
            this.startY = startY;
            this.width = width;
            this.height = height;
            this.targetHeight = targetHeight;
            this.description = description;
        }

        private readonly int startX;
        private readonly int startY;
        private readonly int width;
        private readonly int height;
        private readonly byte targetHeight;
        private readonly string description;

        // Undo data
        private List<OriginalHeightData> undoData;

        public override string GetDisplayString()
        {
            return string.IsNullOrEmpty(description)
                ? $"AI: Set height to {targetHeight} at ({startX},{startY}) {width}x{height}"
                : $"AI: {description}";
        }

        public override void Perform()
        {
            undoData = new List<OriginalHeightData>();

            for (int y = startY; y < startY + height; y++)
            {
                for (int x = startX; x < startX + width; x++)
                {
                    var cell = Map.GetTile(x, y);
                    if (cell == null)
                        continue;

                    // Save original height for undo
                    undoData.Add(new OriginalHeightData
                    {
                        Position = new Point2D(x, y),
                        OriginalHeight = cell.Level
                    });

                    cell.Level = targetHeight;
                }
            }

            MutationTarget.InvalidateMap();
        }

        public override void Undo()
        {
            if (undoData == null)
                return;

            foreach (var data in undoData)
            {
                var cell = Map.GetTile(data.Position);
                if (cell != null)
                    cell.Level = data.OriginalHeight;
            }

            MutationTarget.InvalidateMap();
        }

        private struct OriginalHeightData
        {
            public Point2D Position;
            public byte OriginalHeight;
        }
    }
}
