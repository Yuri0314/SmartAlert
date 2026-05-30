using System;
using System.Collections.Generic;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.UI;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// A mutation that places overlay (e.g., tiberium/ore) in a circular area with density control.
    /// Automatically excludes spawn point zones to prevent base deployment issues.
    /// </summary>
    public class AIPlaceOverlayMutation : Mutation
    {
        /// <summary>
        /// Circular placement constructor (preferred).
        /// </summary>
        public AIPlaceOverlayMutation(IMutationTarget mutationTarget,
            OverlayType overlayType, int centerX, int centerY,
            int radius, float density, string description)
            : base(mutationTarget)
        {
            this.overlayType = overlayType;
            this.centerX = centerX;
            this.centerY = centerY;
            this.radius = Math.Max(1, radius);
            this.density = Math.Max(0.3f, Math.Min(1.0f, density));
            this.description = description;
        }

        /// <summary>
        /// Legacy rectangular constructor — converts to circular parameters.
        /// </summary>
        public AIPlaceOverlayMutation(IMutationTarget mutationTarget,
            OverlayType overlayType, int startX, int startY,
            int width, int height, string description)
            : this(mutationTarget, overlayType,
                  startX + width / 2, startY + height / 2,
                  Math.Max(width, height) / 2, 0.7f, description)
        {
        }

        private readonly OverlayType overlayType;
        private readonly int centerX;
        private readonly int centerY;
        private readonly int radius;
        private readonly float density;
        private readonly string description;

        // Undo data: stores original overlay state for each modified cell
        private List<OriginalOverlayData> undoData;

        public override string GetDisplayString()
        {
            return string.IsNullOrEmpty(description)
                ? $"AI: Place {overlayType.ININame} at ({centerX},{centerY}) r={radius}"
                : $"AI: {description}";
        }

        public override void Perform()
        {
            undoData = new List<OriginalOverlayData>();
            var map = MutationTarget.Map;
            var random = new Random();
            int r2 = radius * radius;

            // Build spawn point exclusion zones (waypoints 0-7 = player spawn points)
            const int spawnExclusionRadius = 8;
            var spawnPoints = new List<Point2D>();
            foreach (var wp in map.Waypoints)
            {
                if (wp.Identifier >= 0 && wp.Identifier <= 7)
                    spawnPoints.Add(wp.Position);
            }

            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    // Circular boundary check
                    int dx = x - centerX;
                    int dy = y - centerY;
                    if (dx * dx + dy * dy > r2)
                        continue;

                    // Density check — edges are sparser for natural look
                    double distRatio = Math.Sqrt(dx * dx + dy * dy) / radius;
                    double effectiveDensity = density * (1.0 - 0.4 * distRatio); // Denser center, sparser edges
                    if (random.NextDouble() > effectiveDensity)
                        continue;

                    var cell = map.GetTile(x, y);
                    if (cell == null)
                        continue;

                    // Skip cells near spawn points
                    bool nearSpawn = false;
                    foreach (var sp in spawnPoints)
                    {
                        if (Math.Abs(x - sp.X) + Math.Abs(y - sp.Y) <= spawnExclusionRadius)
                        {
                            nearSpawn = true;
                            break;
                        }
                    }
                    if (nearSpawn)
                        continue;

                    // Skip cells on invalid terrain (water, rock, ramps)
                    if (!AIPlacementTerrainRules.IsValidGroundCell(map, cell, requireFlat: true))
                        continue;

                    // Skip cells that already have terrain objects (trees, rocks)
                    if (cell.TerrainObject != null)
                        continue;

                    // Save original overlay for undo
                    undoData.Add(new OriginalOverlayData
                    {
                        Position = new Point2D(x, y),
                        OverlayTypeIndex = cell.Overlay?.OverlayType?.Index ?? -1,
                        FrameIndex = cell.Overlay?.FrameIndex ?? 0
                    });

                    // Place overlay
                    cell.Overlay = new Overlay()
                    {
                        Position = new Point2D(x, y),
                        OverlayType = overlayType,
                        FrameIndex = 0
                    };
                }
            }

            // Update frame indices for tiberium smoothing
            if (overlayType.Tiberium)
            {
                for (int y = centerY - radius - 1; y <= centerY + radius + 1; y++)
                {
                    for (int x = centerX - radius - 1; x <= centerX + radius + 1; x++)
                    {
                        var cell = map.GetTile(x, y);
                        if (cell?.Overlay != null)
                        {
                            cell.Overlay.FrameIndex = map.GetOverlayFrameIndex(new Point2D(x, y));
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

            var map = MutationTarget.Map;

            foreach (var data in undoData)
            {
                var cell = map.GetTile(data.Position);
                if (cell == null)
                    continue;

                if (data.OverlayTypeIndex < 0)
                {
                    cell.Overlay = null;
                }
                else
                {
                    cell.Overlay = new Overlay()
                    {
                        Position = data.Position,
                        OverlayType = map.Rules.OverlayTypes[data.OverlayTypeIndex],
                        FrameIndex = data.FrameIndex
                    };
                }
            }

            // Re-smooth tiberium after undo
            if (overlayType.Tiberium)
            {
                for (int y = centerY - radius - 1; y <= centerY + radius + 1; y++)
                {
                    for (int x = centerX - radius - 1; x <= centerX + radius + 1; x++)
                    {
                        var cell = map.GetTile(x, y);
                        if (cell?.Overlay != null)
                        {
                            cell.Overlay.FrameIndex = map.GetOverlayFrameIndex(new Point2D(x, y));
                        }
                    }
                }
            }

            MutationTarget.InvalidateMap();
        }

        private struct OriginalOverlayData
        {
            public Point2D Position;
            public int OverlayTypeIndex;
            public int FrameIndex;
        }
    }
}
