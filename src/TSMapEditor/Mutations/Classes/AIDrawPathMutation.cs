using System;
using System.Collections.Generic;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.Rendering;
using TSMapEditor.UI;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// A mutation that draws a path (line) from a start point to an end point with a specified width and tileset.
    /// Used by the AI system to draw roads, rivers, etc., with undo/redo support and AutoLAT integration.
    ///
    /// For water tiles: uses Map.PlaceTerrainTileAt() with proper 2x2/1x1 water tile images,
    /// matching how official RA2/MO maps handle water placement.
    /// For other terrain: uses cell.ChangeTileIndex() for simple 1x1 fills.
    /// </summary>
    public class AIDrawPathMutation : Mutation
    {
        private readonly int startX;
        private readonly int startY;
        private readonly int endX;
        private readonly int endY;
        private readonly int pathWidth;
        private readonly int tileIndex;
        private readonly string description;
        private readonly bool flattenHeight;
        private readonly byte targetHeight;

        // Undo data
        private OriginalCellTerrainData[] undoTerrainData;
        private List<RemovedTerrainObject> removedTerrainObjects;
        private List<RemovedOverlay> removedOverlays;
        private List<RemovedSmudge> removedSmudges;

        public AIDrawPathMutation(IMutationTarget mutationTarget, int startX, int startY, int endX, int endY,
            int pathWidth, int tileIndex, string description, bool flattenHeight = false, byte targetHeight = 0)
            : base(mutationTarget)
        {
            this.startX = startX;
            this.startY = startY;
            this.endX = endX;
            this.endY = endY;
            this.pathWidth = Math.Max(1, pathWidth);
            this.tileIndex = tileIndex;
            this.description = description;
            this.flattenHeight = flattenHeight;
            this.targetHeight = targetHeight;
        }

        public override string GetDisplayString()
        {
            return string.IsNullOrEmpty(description)
                ? $"AI: Draw path from ({startX},{startY}) to ({endX},{endY}) width {pathWidth}"
                : $"AI: {description}";
        }

        public override void Perform()
        {
            var originalTerrainData = new List<OriginalCellTerrainData>();
            removedTerrainObjects = new List<RemovedTerrainObject>();
            removedOverlays = new List<RemovedOverlay>();
            removedSmudges = new List<RemovedSmudge>();
            
            // Collect all cells along the path using Bresenham's line algorithm
            var processedCells = new HashSet<Point2D>();
            var pathCells = new List<Point2D>();

            int x0 = startX, y0 = startY;
            int x1 = endX, y1 = endY;
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = (dx > dy ? dx : -dy) / 2, e2;

            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;

            while (true)
            {
                // Plot with thickness
                int radius = pathWidth / 2;
                int r2 = radius * radius;
                
                for (int cy = y0 - radius; cy <= y0 + radius; cy++)
                {
                    for (int cx = x0 - radius; cx <= x0 + radius; cx++)
                    {
                        if (pathWidth > 2 && ((cx - x0) * (cx - x0) + (cy - y0) * (cy - y0)) > r2 + 1)
                            continue;

                        var pt = new Point2D(cx, cy);
                        if (processedCells.Contains(pt))
                            continue;
                            
                        processedCells.Add(pt);

                        var cell = Map.GetTile(cx, cy);
                        if (cell == null)
                            continue;

                        minX = Math.Min(minX, cx);
                        minY = Math.Min(minY, cy);
                        maxX = Math.Max(maxX, cx);
                        maxY = Math.Max(maxY, cy);

                        pathCells.Add(pt);

                        // Save original terrain data for undo
                        originalTerrainData.Add(new OriginalCellTerrainData(
                            pt, cell.TileIndex, cell.SubTileIndex, cell.Level));

                        // Remove terrain objects
                        if (cell.TerrainObject != null)
                        {
                            removedTerrainObjects.Add(new RemovedTerrainObject
                            {
                                Position = pt,
                                TerrainObject = cell.TerrainObject
                            });
                            Map.RemoveTerrainObject(pt);
                        }

                        // Remove overlays
                        if (cell.Overlay != null)
                        {
                            removedOverlays.Add(new RemovedOverlay
                            {
                                Position = pt,
                                Overlay = cell.Overlay
                            });
                            cell.Overlay = null;
                        }

                        // Remove smudges
                        if (cell.Smudge != null)
                        {
                            removedSmudges.Add(new RemovedSmudge
                            {
                                Position = pt,
                                Smudge = cell.Smudge
                            });
                            cell.Smudge = null;
                        }

                        // Flatten height if requested
                        if (flattenHeight)
                        {
                            cell.Level = targetHeight;
                        }
                    }
                }

                if (x0 == x1 && y0 == y1) break;
                e2 = err;
                if (e2 > -dx) { err -= dy; x0 += sx; }
                if (e2 < dy) { err += dx; y0 += sy; }
            }

            // Now place tiles using the correct method
            // For water tiles, use PlaceTerrainTileAt with proper multi-cell water tile images
            // For other terrain, use simple ChangeTileIndex
            var theaterGraphics = MutationTarget.TheaterGraphics;
            bool isMultiCellTile = false;

            // Check if this tile belongs to a tileset with multi-cell tiles (like Water)
            var tileGraphics = theaterGraphics.GetTileGraphics(tileIndex);
            if (tileGraphics != null && (tileGraphics.Width > 1 || tileGraphics.Height > 1))
            {
                isMultiCellTile = true;
            }

            if (isMultiCellTile)
            {
                // For multi-cell tiles (Water), we need to place complete tile images
                // Use 1x1 water tile as fallback (index offset 8 in water tileset is 1x1)
                // First try to find 1x1 tiles within the same tileset
                var tileSets = theaterGraphics.Theater.TileSets;
                int tileSetIndex = -1;
                for (int i = 0; i < tileSets.Count; i++)
                {
                    if (tileSets[i].ContainsTile(tileIndex))
                    {
                        tileSetIndex = i;
                        break;
                    }
                }

                // Find 1x1 tiles within this tileset for individual cell placement
                int oneByCOneTileIndex = tileIndex;
                if (tileSetIndex >= 0)
                {
                    var tileSet = tileSets[tileSetIndex];
                    // Search for a 1x1 tile in this tileset (water tileset has 1x1 tiles at offset 8+)
                    for (int offset = 0; offset < tileSet.LoadedTileCount; offset++)
                    {
                        var candidate = theaterGraphics.GetTileGraphics(tileSet.StartTileIndex + offset);
                        if (candidate != null && candidate.Width == 1 && candidate.Height == 1)
                        {
                            oneByCOneTileIndex = tileSet.StartTileIndex + offset;
                            break;
                        }
                    }
                }

                // Place 1x1 tiles on all path cells first
                foreach (var pt in pathCells)
                {
                    var cell = Map.GetTile(pt);
                    if (cell != null)
                    {
                        var singleTile = theaterGraphics.GetTileGraphics(oneByCOneTileIndex);
                        if (singleTile != null)
                        {
                            Map.PlaceTerrainTileAt(singleTile, pt);
                        }
                        else
                        {
                            cell.ChangeTileIndex(oneByCOneTileIndex, 0);
                        }
                    }
                }

                // Then try to upgrade to 2x2 tiles where possible (like the Smoothen Water script does)
                var placed2x2 = new HashSet<Point2D>();
                // 2x2 water tiles are at offsets 0-5 in the water tileset
                if (tileSetIndex >= 0)
                {
                    var tileSet = tileSets[tileSetIndex];
                    var random = new Random();

                    // Collect all 2x2 tile indices
                    var twoByTwoIndices = new List<int>();
                    for (int offset = 0; offset < tileSet.LoadedTileCount; offset++)
                    {
                        var candidate = theaterGraphics.GetTileGraphics(tileSet.StartTileIndex + offset);
                        if (candidate != null && candidate.Width == 2 && candidate.Height == 2)
                        {
                            twoByTwoIndices.Add(tileSet.StartTileIndex + offset);
                        }
                    }

                    if (twoByTwoIndices.Count > 0)
                    {
                        foreach (var pt in pathCells)
                        {
                            if (placed2x2.Contains(pt))
                                continue;

                            // Check if a 2x2 tile can fit here (all 4 cells must be path cells)
                            bool canFit = true;
                            for (int cy = 0; cy < 2 && canFit; cy++)
                            {
                                for (int cx = 0; cx < 2 && canFit; cx++)
                                {
                                    var check = new Point2D(pt.X + cx, pt.Y + cy);
                                    if (!processedCells.Contains(check) || placed2x2.Contains(check))
                                        canFit = false;
                                }
                            }

                            if (canFit)
                            {
                                int randomIdx = twoByTwoIndices[random.Next(twoByTwoIndices.Count)];
                                var bigTile = theaterGraphics.GetTileGraphics(randomIdx);
                                if (bigTile != null)
                                {
                                    Map.PlaceTerrainTileAt(bigTile, pt);
                                    for (int cy = 0; cy < 2; cy++)
                                        for (int cx = 0; cx < 2; cx++)
                                            placed2x2.Add(new Point2D(pt.X + cx, pt.Y + cy));
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                // Simple 1x1 terrain tiles (roads, dirt, etc.) - use ChangeTileIndex directly
                foreach (var pt in pathCells)
                {
                    var cell = Map.GetTile(pt);
                    if (cell != null)
                    {
                        cell.ChangeTileIndex(tileIndex, 0);
                    }
                }
            }

            undoTerrainData = originalTerrainData.ToArray();

            // Apply AutoLAT if enabled to blend edges
            if (MutationTarget.AutoLATEnabled && minX <= maxX)
            {
                ApplyGenericAutoLAT(minX - 1, minY - 1, maxX + 1, maxY + 1);
            }

            MutationTarget.InvalidateMap();
        }

        public override void Undo()
        {
            if (undoTerrainData == null)
                return;

            foreach (var data in undoTerrainData)
            {
                var cell = Map.GetTile(data.CellCoords);
                if (cell != null)
                {
                    cell.ChangeTileIndex(data.TileIndex, data.SubTileIndex);
                    cell.Level = data.HeightLevel;
                }
            }

            foreach (var removed in removedTerrainObjects)
            {
                var cell = Map.GetTile(removed.Position);
                if (cell != null && cell.TerrainObject == null)
                {
                    Map.AddTerrainObject(removed.TerrainObject);
                }
            }

            foreach (var removed in removedOverlays)
            {
                var cell = Map.GetTile(removed.Position);
                if (cell != null)
                {
                    cell.Overlay = removed.Overlay;
                }
            }

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
