using System;
using System.Collections.Generic;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.Mutations.Classes.HeightMutations;
using TSMapEditor.UI;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// A mutation that raises ground in a rectangular area to a target height,
    /// simulating repeated clicks of the editor's "Raise Ground" tool.
    /// 
    /// The editor's RaiseGroundMutation only raises cells that are at the same
    /// level as the origin cell, and only by 1 level per call. So to reach
    /// height N, we need to call it N times, each time on a cell that's at
    /// the current lowest level. Each level also shrinks the effective area
    /// to create a natural gradient slope.
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

            // Save original state for undo (with border for ramp effects)
            int border = 6;
            for (int y = startY - border; y < startY + height + border; y++)
            {
                for (int x = startX - border; x < startX + width + border; x++)
                {
                    var cell = Map.GetTile(x, y);
                    if (cell == null) continue;
                    undoData.Add(new OriginalCellData
                    {
                        Position = new Point2D(x, y),
                        OriginalHeight = cell.Level,
                        OriginalTileIndex = cell.TileIndex,
                        OriginalSubTileIndex = cell.SubTileIndex
                    });
                }
            }

            // Raise ground level by level, just like clicking the "Raise Ground" tool repeatedly
            // Each level shrinks the area by 1 on each side for a natural slope gradient
            for (int level = 0; level < targetHeight; level++)
            {
                int shrink = level;
                int sx = startX + shrink;
                int sy = startY + shrink;
                int w = Math.Max(1, width - shrink * 2);
                int h = Math.Max(1, height - shrink * 2);

                // Iterate through the area and raise each cell individually
                // using a 3x3 brush (same as editor's default)
                var brush = new BrushSize(3, 3);
                for (int y = sy; y < sy + h; y++)
                {
                    for (int x = sx; x < sx + w; x++)
                    {
                        var cell = Map.GetTile(x, y);
                        if (cell == null) continue;

                        // Only raise if at the expected level for this pass
                        if (cell.Level == level)
                        {
                            var mutation = new RaiseGroundMutation(MutationTarget, new Point2D(x, y), brush);
                            mutation.Perform();
                        }
                    }
                }
            }

            MutationTarget.InvalidateMap();
        }

        public override void Undo()
        {
            if (undoData == null) return;

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
