using System;
using System.Collections.Generic;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.Rendering;
using TSMapEditor.UI;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// A mutation that fills a rectangular area with a specified tileset.
    /// Used by the AI system to apply terrain changes with undo/redo support.
    /// </summary>
    public class AITerrainMutation : Mutation
    {
        public AITerrainMutation(IMutationTarget mutationTarget, int x, int y, int width, int height, int tileIndex, string description)
            : base(mutationTarget)
        {
            this.startX = x;
            this.startY = y;
            this.width = width;
            this.height = height;
            this.tileIndex = tileIndex;
            this.description = description;
        }

        private readonly int startX;
        private readonly int startY;
        private readonly int width;
        private readonly int height;
        private readonly int tileIndex;
        private readonly string description;

        private OriginalCellTerrainData[] undoData;

        public override string GetDisplayString()
        {
            return string.IsNullOrEmpty(description)
                ? $"AI: Fill terrain at ({startX},{startY}) size {width}x{height}"
                : $"AI: {description}";
        }

        public override void Perform()
        {
            var originalData = new List<OriginalCellTerrainData>();

            for (int y = startY; y < startY + height; y++)
            {
                for (int x = startX; x < startX + width; x++)
                {
                    var cell = Map.GetTile(x, y);
                    if (cell == null)
                        continue;

                    originalData.Add(new OriginalCellTerrainData(
                        new Point2D(x, y), cell.TileIndex, cell.SubTileIndex, cell.Level));

                    cell.ChangeTileIndex(tileIndex, 0);
                }
            }

            undoData = originalData.ToArray();

            // Apply Auto-LAT to handle terrain transitions
            ApplyGenericAutoLAT(
                Math.Max(0, startX - 1),
                Math.Max(0, startY - 1),
                startX + width,
                startY + height);

            MutationTarget.InvalidateMap();
        }

        public override void Undo()
        {
            if (undoData == null)
                return;

            foreach (var data in undoData)
            {
                var cell = Map.GetTile(data.CellCoords);
                if (cell != null)
                {
                    cell.ChangeTileIndex(data.TileIndex, data.SubTileIndex);
                }
            }

            MutationTarget.InvalidateMap();
        }
    }
}
