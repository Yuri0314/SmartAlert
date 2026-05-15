using System;
using System.Collections.Generic;
using TSMapEditor.CCEngine;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.Mutations.Classes.HeightMutations;
using TSMapEditor.Rendering;
using TSMapEditor.UI;
using HCT = TSMapEditor.Mutations.Classes.HeightMutations.HeightComparisonType;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// A mutation that fills a rectangular area with a specified tileset.
    /// Used by the AI system to apply terrain changes with undo/redo support.
    /// 
    /// This mutation properly handles:
    /// - Terrain tile replacement
    /// - Clearing terrain objects (trees, rocks) in the area
    /// - Clearing overlays (tiberium, etc.) in the area
    /// - Clearing smudges in the area
    /// - Flattening height levels when needed (e.g., for water)
    /// </summary>
    public class AITerrainMutation : Mutation
    {
        public AITerrainMutation(IMutationTarget mutationTarget, int x, int y, int width, int height,
            int tileIndex, string description, bool flattenHeight = false, byte targetHeight = 0)
            : base(mutationTarget)
        {
            this.startX = x;
            this.startY = y;
            this.width = width;
            this.height = height;
            this.tileIndex = tileIndex;
            this.description = description;
            this.flattenHeight = flattenHeight;
            this.targetHeight = targetHeight;
        }

        private readonly int startX;
        private readonly int startY;
        private readonly int width;
        private readonly int height;
        private readonly int tileIndex;
        private readonly string description;
        private readonly bool flattenHeight;
        private readonly byte targetHeight;

        // Undo data
        private OriginalCellTerrainData[] undoTerrainData;
        private List<RemovedTerrainObject> removedTerrainObjects;
        private List<RemovedOverlay> removedOverlays;
        private List<RemovedSmudge> removedSmudges;

        public override string GetDisplayString()
        {
            return string.IsNullOrEmpty(description)
                ? $"AI: Fill terrain at ({startX},{startY}) size {width}x{height}"
                : $"AI: {description}";
        }

        public override void Perform()
        {
            var originalTerrainData = new List<OriginalCellTerrainData>();
            removedTerrainObjects = new List<RemovedTerrainObject>();
            removedOverlays = new List<RemovedOverlay>();
            removedSmudges = new List<RemovedSmudge>();

            for (int y = startY; y < startY + height; y++)
            {
                for (int x = startX; x < startX + width; x++)
                {
                    var cell = Map.GetTile(x, y);
                    if (cell == null)
                        continue;

                    // Save original terrain data for undo
                    originalTerrainData.Add(new OriginalCellTerrainData(
                        new Point2D(x, y), cell.TileIndex, cell.SubTileIndex, cell.Level));

                    // Remove terrain objects (trees, rocks, etc.)
                    if (cell.TerrainObject != null)
                    {
                        removedTerrainObjects.Add(new RemovedTerrainObject
                        {
                            Position = new Point2D(x, y),
                            TerrainObject = cell.TerrainObject
                        });
                        Map.RemoveTerrainObject(new Point2D(x, y));
                    }

                    // Remove overlays (tiberium, etc.)
                    if (cell.Overlay != null)
                    {
                        removedOverlays.Add(new RemovedOverlay
                        {
                            Position = new Point2D(x, y),
                            Overlay = cell.Overlay
                        });
                        cell.Overlay = null;
                    }

                    // Remove smudges
                    if (cell.Smudge != null)
                    {
                        removedSmudges.Add(new RemovedSmudge
                        {
                            Position = new Point2D(x, y),
                            Smudge = cell.Smudge
                        });
                        cell.Smudge = null;
                    }

                    // Change terrain tile (paint freely, ramp repair happens after)
                    cell.ChangeTileIndex(tileIndex, 0);

                    // Flatten height if requested (e.g., water should be at level 0)
                    if (flattenHeight)
                    {
                        cell.Level = targetHeight;
                    }
                }
            }

            undoTerrainData = originalTerrainData.ToArray();

            if (MutationTarget.AutoLATEnabled)
            {
                ApplyGenericAutoLAT(startX, startY, startX + width, startY + height);
            }

            // Systematic height transition repair:
            // After painting, re-apply correct ramp tiles for any cell that has
            // height differences with its neighbors. This uses the same engine logic
            // as create_plateau's Process() → ApplyRamps(), so it's always correct.
            RepairHeightTransitions(startX - 1, startY - 1, startX + width + 1, startY + height + 1);

            MutationTarget.InvalidateMap();
        }

        public override void Undo()
        {
            if (undoTerrainData == null)
                return;

            // Restore terrain tiles and height
            foreach (var data in undoTerrainData)
            {
                var cell = Map.GetTile(data.CellCoords);
                if (cell != null)
                {
                    cell.ChangeTileIndex(data.TileIndex, data.SubTileIndex);
                    cell.Level = data.HeightLevel;
                }
            }

            // Restore terrain objects
            foreach (var removed in removedTerrainObjects)
            {
                var cell = Map.GetTile(removed.Position);
                if (cell != null && cell.TerrainObject == null)
                {
                    Map.AddTerrainObject(removed.TerrainObject);
                }
            }

            // Restore overlays
            foreach (var removed in removedOverlays)
            {
                var cell = Map.GetTile(removed.Position);
                if (cell != null)
                {
                    cell.Overlay = removed.Overlay;
                }
            }

            // Restore smudges
            foreach (var removed in removedSmudges)
            {
                var cell = Map.GetTile(removed.Position);
                if (cell != null)
                {
                    cell.Smudge = removed.Smudge;
                }
            }

            MutationTarget.InvalidateMap();
        }

        /// <summary>
        /// Systematically repairs height transition tiles in the given area.
        /// After fill_terrain paints over ramp/cliff tiles, this method re-applies
        /// the correct ramp tiles by using the engine's native TransitionRampInfo matching —
        /// the same logic used by create_plateau and the manual height tools.
        /// 
        /// This is the systematic solution to the "black line" problem: instead of trying
        /// to predict which cells to skip (fragile), we paint freely and then fix up.
        /// </summary>
        private void RepairHeightTransitions(int minX, int minY, int maxX, int maxY)
        {
            var rampTileSet = MutationTarget.TheaterGraphics.Theater.RampTileSet;
            if (rampTileSet == null)
                return;

            // Use the same transition ramp info table as the engine's FSRaiseGroundMutation
            var transitionInfos = GetRaiseGroundTransitionRampInfos();

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var cell = Map.GetTile(x, y);
                    if (cell == null)
                        continue;

                    // Only process cells that have height differences with neighbors
                    bool hasHeightDiff = false;
                    for (int dir = 0; dir < (int)Direction.Count; dir++)
                    {
                        var neighbor = Map.GetTile(new Point2D(x, y) + Helpers.VisualDirectionToPoint((Direction)dir));
                        if (neighbor != null && neighbor.Level != cell.Level)
                        {
                            hasHeightDiff = true;
                            break;
                        }
                    }

                    if (!hasHeightDiff)
                        continue;

                    // Try to match against the engine's ramp transition table
                    var cellCoords = new Point2D(x, y);
                    foreach (var tri in transitionInfos)
                    {
                        if (tri.Matches(Map, cellCoords, cell.Level))
                        {
                            if (tri.RampType == CCEngine.RampType.None)
                            {
                                cell.ChangeTileIndex(0, 0);
                            }
                            else
                            {
                                cell.ChangeTileIndex(rampTileSet.StartTileIndex + ((int)tri.RampType - 1), 0);
                            }
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns the same TransitionRampInfo table used by FSRaiseGroundMutation.
        /// This is the engine's complete knowledge of which ramp tile to use for
        /// each possible height configuration around a cell.
        /// </summary>
        private static TransitionRampInfo[] GetRaiseGroundTransitionRampInfos()
        {
            return new[]
            {
                new TransitionRampInfo(RampType.West, new() { HCT.LowerOrEqual, HCT.HigherOrEqual, HCT.Higher, HCT.HigherOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual }),
                new TransitionRampInfo(RampType.North, new() { HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.HigherOrEqual, HCT.Higher, HCT.HigherOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual }),
                new TransitionRampInfo(RampType.East, new() { HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.HigherOrEqual, HCT.Higher, HCT.HigherOrEqual }),
                new TransitionRampInfo(RampType.South, new() { HCT.Higher, HCT.HigherOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.HigherOrEqual }),

                new TransitionRampInfo(RampType.CornerNW, new() { HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.Equal, HCT.Higher, HCT.Equal, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual }),
                new TransitionRampInfo(RampType.CornerNE, new() { HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.Equal, HCT.Higher, HCT.Equal, HCT.LowerOrEqual }),
                new TransitionRampInfo(RampType.CornerSE, new() { HCT.Equal, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.Equal, HCT.Higher }),
                new TransitionRampInfo(RampType.CornerSW, new() { HCT.Equal, HCT.Higher, HCT.Equal, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual }),

                new TransitionRampInfo(RampType.MidNW, new() { HCT.LowerOrEqual, HCT.HigherOrEqual, HCT.HigherOrEqual, HCT.Higher, HCT.HigherOrEqual, HCT.HigherOrEqual, HCT.Equal, HCT.LowerOrEqual }),
                new TransitionRampInfo(RampType.MidNE, new() { HCT.Equal, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.HigherOrEqual, HCT.HigherOrEqual, HCT.Higher, HCT.HigherOrEqual, HCT.HigherOrEqual }),
                new TransitionRampInfo(RampType.MidSE, new() { HCT.HigherOrEqual, HCT.HigherOrEqual, HCT.Equal, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.HigherOrEqual, HCT.HigherOrEqual, HCT.Higher }),
                new TransitionRampInfo(RampType.MidSW, new() { HCT.HigherOrEqual, HCT.Higher, HCT.HigherOrEqual, HCT.HigherOrEqual, HCT.Equal, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.HigherOrEqual }),

                new TransitionRampInfo(RampType.DoubleUpSWNE, new() { HCT.Equal, HCT.Higher, HCT.Equal, HCT.Irrelevant, HCT.Equal, HCT.Higher, HCT.Equal, HCT.Irrelevant }),
                new TransitionRampInfo(RampType.DoubleDownSWNE, new() { HCT.Equal, HCT.Irrelevant, HCT.Equal, HCT.Higher, HCT.Equal, HCT.Irrelevant, HCT.Equal, HCT.Higher }),

                // Extended mid-ramp patterns
                new TransitionRampInfo(RampType.MidNE, new() { HCT.Equal, HCT.LowerOrEqual, HCT.Equal, HCT.Higher, HCT.Equal, HCT.Irrelevant, HCT.Higher, HCT.Irrelevant }),
                new TransitionRampInfo(RampType.MidSW, new() { HCT.Equal, HCT.Equal, HCT.Higher, HCT.Irrelevant, HCT.Equal, HCT.Equal, HCT.Equal, HCT.Higher }),
                new TransitionRampInfo(RampType.MidNW, new() { HCT.Equal, HCT.Higher, HCT.Equal, HCT.Equal, HCT.Higher, HCT.Equal, HCT.Equal, HCT.LowerOrEqual }),
                new TransitionRampInfo(RampType.MidSE, new() { HCT.Higher, HCT.Equal, HCT.Equal, HCT.LowerOrEqual, HCT.Equal, HCT.Higher, HCT.Equal, HCT.Equal }),
                new TransitionRampInfo(RampType.MidSE, new() { HCT.Equal, HCT.Higher, HCT.Equal, HCT.LowerOrEqual, HCT.Equal, HCT.Irrelevant, HCT.Higher, HCT.Equal }),
                new TransitionRampInfo(RampType.MidNW, new() { HCT.Equal, HCT.Irrelevant, HCT.Higher, HCT.Equal, HCT.Equal, HCT.Higher, HCT.Equal, HCT.LowerOrEqual }),
                new TransitionRampInfo(RampType.MidNE, new() { HCT.Equal, HCT.LowerOrEqual, HCT.Equal, HCT.Equal, HCT.Higher, HCT.Equal, HCT.Equal, HCT.Higher }),
                new TransitionRampInfo(RampType.MidSW, new() { HCT.Higher, HCT.Equal, HCT.Equal, HCT.Higher, HCT.Equal, HCT.Equal, HCT.LowerOrEqual, HCT.Equal }),

                // Less likely mid-ramp cases
                new TransitionRampInfo(RampType.MidNW, new() { HCT.LowerOrEqual, HCT.HigherOrEqual, HCT.HigherOrEqual, HCT.HigherOrEqual, HCT.Higher, HCT.HigherOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual }),
                new TransitionRampInfo(RampType.MidNW, new() { HCT.LowerOrEqual, HCT.HigherOrEqual, HCT.Higher, HCT.HigherOrEqual, HCT.HigherOrEqual, HCT.HigherOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual }),
                new TransitionRampInfo(RampType.MidNE, new() { HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.HigherOrEqual, HCT.HigherOrEqual, HCT.HigherOrEqual, HCT.Higher, HCT.HigherOrEqual }),
                new TransitionRampInfo(RampType.MidNE, new() { HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.HigherOrEqual, HCT.Higher, HCT.HigherOrEqual, HCT.HigherOrEqual, HCT.HigherOrEqual }),
                new TransitionRampInfo(RampType.MidSE, new() { HCT.HigherOrEqual, HCT.HigherOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.HigherOrEqual, HCT.Higher, HCT.HigherOrEqual }),
                new TransitionRampInfo(RampType.MidSE, new() { HCT.Higher, HCT.HigherOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.HigherOrEqual, HCT.HigherOrEqual, HCT.HigherOrEqual }),
                new TransitionRampInfo(RampType.MidSW, new() { HCT.HigherOrEqual, HCT.HigherOrEqual, HCT.Higher, HCT.HigherOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.HigherOrEqual }),
                new TransitionRampInfo(RampType.MidSW, new() { HCT.Higher, HCT.HigherOrEqual, HCT.HigherOrEqual, HCT.HigherOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.LowerOrEqual, HCT.HigherOrEqual }),
            };
        }

        // --- Undo data structs ---

        private struct RemovedTerrainObject
        {
            public Point2D Position;
            public TerrainObject TerrainObject;
        }

        private struct RemovedOverlay
        {
            public Point2D Position;
            public Overlay Overlay;
        }

        private struct RemovedSmudge
        {
            public Point2D Position;
            public Smudge Smudge;
        }
    }
}
