using System.Collections.Generic;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.Mutations.Classes.HeightMutations;
using TSMapEditor.UI;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// A mutation that raises ground in a rectangular area to a target height,
    /// using the editor's built-in RaiseGroundMutation for proper ramp transitions.
    /// Each height level is raised one at a time with automatic ramp tile placement.
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

        // Store sub-mutations for undo
        private List<RaiseGroundMutation> subMutations = new List<RaiseGroundMutation>();

        // Store original state for undo fallback
        private List<OriginalCellData> undoData;

        public override string GetDisplayString()
        {
            return string.IsNullOrEmpty(description)
                ? $"AI: Set height to {targetHeight} at ({startX},{startY}) {width}x{height}"
                : $"AI: {description}";
        }

        public override void Perform()
        {
            undoData = new List<OriginalCellData>();

            // Save original state of all cells in the area + border for undo
            int border = 3; // extra border for ramp effects
            for (int y = startY - border; y < startY + height + border; y++)
            {
                for (int x = startX - border; x < startX + width + border; x++)
                {
                    var cell = Map.GetTile(x, y);
                    if (cell == null)
                        continue;

                    undoData.Add(new OriginalCellData
                    {
                        Position = new Point2D(x, y),
                        OriginalHeight = cell.Level,
                        OriginalTileIndex = cell.TileIndex,
                        OriginalSubTileIndex = cell.SubTileIndex
                    });
                }
            }

            // Raise ground one level at a time using the editor's smart mutation
            // which automatically handles ramp transitions
            for (int level = 0; level < targetHeight; level++)
            {
                // For each cell in the area, if it needs raising, use RaiseGroundMutation
                for (int y = startY; y < startY + height; y++)
                {
                    for (int x = startX; x < startX + width; x++)
                    {
                        var cell = Map.GetTile(x, y);
                        if (cell == null)
                            continue;

                        if (cell.Level <= level)
                        {
                            // Use a brush size that covers just this cell plus neighbors for context
                            var brushSize = new BrushSize(3, 3);
                            var mutation = new RaiseGroundMutation(MutationTarget, new Point2D(x, y), brushSize);
                            mutation.Perform();
                            subMutations.Add(mutation);
                        }
                    }
                }
            }

            MutationTarget.InvalidateMap();
        }

        public override void Undo()
        {
            if (undoData == null)
                return;

            // Restore all cells to their original state
            foreach (var data in undoData)
            {
                var cell = Map.GetTile(data.Position);
                if (cell != null)
                {
                    cell.Level = data.OriginalHeight;
                    cell.ChangeTileIndex(data.OriginalTileIndex, data.OriginalSubTileIndex);
                }
            }

            MutationTarget.InvalidateMap();
        }

        private struct OriginalCellData
        {
            public Point2D Position;
            public byte OriginalHeight;
            public int OriginalTileIndex;
            public byte OriginalSubTileIndex;
        }
    }
}
