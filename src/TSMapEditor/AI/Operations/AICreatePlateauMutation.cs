using System;
using System.Collections.Generic;
using TSMapEditor.CCEngine;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.Mutations.Classes.HeightMutations;
using TSMapEditor.UI;

namespace TSMapEditor.AI.Operations
{
    /// <summary>
    /// A mutation that executes the AI's "create_plateau" intent.
    /// It creates a circular raised plateau of a specific radius and height,
    /// automatically generating the correct cliff and ramp edge tiles by leveraging
    /// the game engine's native RaiseGround logic.
    /// </summary>
    public class AICreatePlateauMutation : FSRaiseGroundMutation
    {
        private readonly int centerX;
        private readonly int centerY;
        private readonly int radius;
        private readonly int heightLevel;

        public AICreatePlateauMutation(IMutationTarget mutationTarget, int centerX, int centerY, int radius, int heightLevel)
            // Initialize the base with a dummy 1x1 brush, since we override the area generation logic
            : base(mutationTarget, new Point2D(centerX, centerY), new BrushSize(1, 1))
        {
            this.centerX = centerX;
            this.centerY = centerY;
            this.radius = Math.Max(1, radius);
            this.heightLevel = Math.Max(1, heightLevel);
        }

        public override string GetDisplayString()
        {
            return $"AI: 生成半径 {radius} 级数 {heightLevel} 的高地位于 ({centerX},{centerY})";
        }

        public override void Perform()
        {
            Clear(); // Clear undo data and cell tracking lists

            int maxRadius = radius + heightLevel - 1;

            // 1. Mathematically generate the terraced diamond
            // A diamond shape (|x| + |y| <= r) aligns perfectly with the isometric engine's cliff graphics.
            // By pre-calculating the tiered heights, we avoid the engine's multi-layer spreading bugs.
            for (int y = centerY - maxRadius; y <= centerY + maxRadius; y++)
            {
                for (int x = centerX - maxRadius; x <= centerX + maxRadius; x++)
                {
                    int dist = Math.Abs(x - centerX) + Math.Abs(y - centerY);
                    if (dist <= maxRadius)
                    {
                        var cellCoords = new Point2D(x, y);
                        var targetCell = Map.GetTile(cellCoords);
                        if (targetCell == null || targetCell.Level >= Constants.MaxMapHeightLevel || !IsCellMorphable(targetCell))
                            continue;

                        int cellHeightBoost = heightLevel;
                        if (dist > radius)
                        {
                            cellHeightBoost = heightLevel - (dist - radius);
                        }

                        // Prevent exceeding max level
                        if (targetCell.Level + cellHeightBoost >= Constants.MaxMapHeightLevel)
                        {
                            cellHeightBoost = Constants.MaxMapHeightLevel - targetCell.Level - 1;
                        }

                        if (cellHeightBoost <= 0) continue;

                        AddCellToUndoData(cellCoords);
                        targetCell.Level += (byte)cellHeightBoost;
                        targetCell.ChangeTileIndex(0, 0);

                        MarkCellAsProcessed(cellCoords);

                        foreach (Point2D offset in SurroundingTiles)
                        {
                            RegisterCell(cellCoords + offset);
                        }
                    }
                }
            }

            // 2. Trigger the native Engine Edge Smoothing and Ramp Application
            // Because we generated a perfect terraced diamond and added all to totalProcessedCells,
            // this will seamlessly apply ramps to all layers!
            Process();

            // 3. Invalidate Map graphics so the new cliffs are drawn
            MutationTarget.InvalidateMap();
        }
    }
}
