using System;
using System.Collections.Generic;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.Mutations.Classes.HeightMutations;
using TSMapEditor.UI;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// A mutation that sets the height level of all cells in a rectangular area,
    /// then uses the editor's RaiseGroundMutation to fix ramp transitions.
    /// 
    /// Strategy: Set heights directly, then apply ramp-fixing by "raising" each
    /// border cell (which triggers the ramp transition logic).
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

            // 1. Save original state for the entire affected region (with generous border for ramp effects)
            int border = 5;
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

            // 2. Gradually raise cells level by level using RaiseGroundMutation
            //    This ensures proper ramp generation at each step
            for (byte level = 1; level <= targetHeight; level++)
            {
                // Calculate the area for this level (inner area gets raised, creating natural gradient)
                // Each level shrinks the area by 1 on each side for a natural slope
                int shrink = level - 1;
                int areaStartX = startX + shrink;
                int areaStartY = startY + shrink;
                int areaWidth = Math.Max(1, width - shrink * 2);
                int areaHeight = Math.Max(1, height - shrink * 2);

                // Use a brush large enough to cover the area
                int brushW = areaWidth + 2;
                int brushH = areaHeight + 2;
                var brushSize = new BrushSize(brushW, brushH);

                // Center point of the area
                int centerX = areaStartX + areaWidth / 2;
                int centerY = areaStartY + areaHeight / 2;

                var raiseMutation = new RaiseGroundMutation(MutationTarget, new Point2D(centerX, centerY), brushSize);
                raiseMutation.Perform();
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
