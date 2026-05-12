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

                    // Skip ramp/cliff tiles — overwriting these produces black artifacts
                    bool isRamp = false;
                    if (cell.TileImage != null && cell.TileImage.TMPImages != null &&
                        cell.SubTileIndex < cell.TileImage.TMPImages.Length)
                    {
                        var tmpImage = cell.TileImage.TMPImages[cell.SubTileIndex]?.TmpImage;
                        if (tmpImage != null && tmpImage.RampType != TSMapEditor.CCEngine.RampType.None)
                            isRamp = true;
                    }

                    // Also check via TileIndex range for ramp tileset
                    var rampTileSet = MutationTarget.TheaterGraphics.Theater.RampTileSet;
                    if (rampTileSet != null && rampTileSet.ContainsTile(cell.TileIndex))
                        isRamp = true;

                    if (!isRamp)
                    {
                        // Change terrain tile
                        cell.ChangeTileIndex(tileIndex, 0);
                    }

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
