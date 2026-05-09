using System;
using System.Collections.Generic;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.Mutations.Classes.HeightMutations;
using TSMapEditor.UI;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// A mutation that raises ground in a rectangular area to a target height level.
    /// 
    /// Key insight: The editor's RaiseGroundMutation works by raising cells by 1 level,
    /// then calling Process() which does:
    ///   1) ProcessCells() - recursively fixes height gaps > 1
    ///   2) CellHeightFixes() - special case fixes
    ///   3) ApplyRamps() - places correct ramp tiles based on neighbor heights
    /// 
    /// The problem with calling RaiseGroundMutation per-cell is that each instance
    /// has its own totalProcessedCells list, so edge ramps get overwritten.
    /// 
    /// Solution: Inherit from RaiseGroundMutation to reuse its ramp tables and
    /// CheckCell logic, but override Perform() to batch-raise ALL cells in the area
    /// for each level, then call Process() ONCE per level for the entire area.
    /// This ensures all edge transitions are computed together.
    /// </summary>
    public class AISetHeightMutation : RaiseGroundMutation
    {
        public AISetHeightMutation(IMutationTarget mutationTarget,
            int startX, int startY, int width, int height,
            byte targetHeight, string description)
            : base(mutationTarget,
                   new Point2D(startX + width / 2, startY + height / 2),
                   new BrushSize(3, 3))
        {
            this.startX = startX;
            this.startY = startY;
            this.areaWidth = width;
            this.areaHeight = height;
            this.targetHeight = targetHeight;
            this.displayDescription = description;
        }

        private readonly int startX;
        private readonly int startY;
        private readonly int areaWidth;
        private readonly int areaHeight;
        private readonly byte targetHeight;
        private readonly string displayDescription;

        public override string GetDisplayString()
        {
            return string.IsNullOrEmpty(displayDescription)
                ? $"AI: Set height to {targetHeight} at ({startX},{startY}) {areaWidth}x{areaHeight}"
                : $"AI: {displayDescription}";
        }

        public override void Perform()
        {
            // Raise ground one level at a time, with shrinking area for natural gradient
            for (int level = 0; level < targetHeight; level++)
            {
                // Shrink area by 1 on each side per level for natural slope
                int shrink = level;
                int sx = startX + shrink;
                int sy = startY + shrink;
                int w = Math.Max(1, areaWidth - shrink * 2);
                int h = Math.Max(1, areaHeight - shrink * 2);

                // If area is too small, stop
                if (w <= 0 || h <= 0)
                    break;

                // Clear processing lists for this level (but keep undoData!)
                cellsToProcess.Clear();
                processedCellsThisIteration.Clear();
                totalProcessedCells.Clear();

                bool anyRaised = false;

                // Batch-raise ALL cells in the area that are at current level
                for (int y = sy; y < sy + h; y++)
                {
                    for (int x = sx; x < sx + w; x++)
                    {
                        var cellCoords = new Point2D(x, y);
                        var cell = Map.GetTile(cellCoords);
                        if (cell == null) continue;
                        if (cell.Level != level) continue;
                        if (!IsCellMorphable(cell)) continue;

                        // Save undo data and raise by 1
                        AddCellToUndoData(cellCoords);
                        cell.Level++;
                        cell.ChangeTileIndex(0, 0);

                        // Register all 8 surrounding cells for ramp processing
                        foreach (var offset in SurroundingTiles)
                        {
                            RegisterCell(cellCoords + offset);
                        }

                        MarkCellAsProcessed(cellCoords);
                        anyRaised = true;
                    }
                }

                // Process() does the magic: fixes height gaps, then applies ramp tiles
                // for ALL registered cells at once — this is the key difference from
                // calling RaiseGroundMutation per-cell!
                if (anyRaised)
                {
                    Process();
                }
            }

            MutationTarget.InvalidateMap();
        }
    }
}
