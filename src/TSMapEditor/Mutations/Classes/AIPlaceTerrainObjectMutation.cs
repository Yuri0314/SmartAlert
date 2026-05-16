using System;
using System.Collections.Generic;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.UI;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// A mutation that places terrain objects (trees, rocks, etc.) in a circular area.
    /// Supports multiple terrain types (randomly mixed) and density control.
    /// Used by the AI system's "place_trees" operation.
    /// </summary>
    public class AIPlaceTerrainObjectMutation : Mutation
    {
        /// <summary>
        /// Creates a mutation that places terrain objects from a list of types in a circular area.
        /// </summary>
        /// <param name="terrainTypes">List of terrain types to randomly choose from when placing each object.</param>
        /// <param name="centerX">Center X coordinate of the circular area.</param>
        /// <param name="centerY">Center Y coordinate of the circular area.</param>
        /// <param name="radius">Radius of the circular placement area.</param>
        /// <param name="density">Probability of placing an object on each eligible cell (0.0 to 1.0).</param>
        /// <param name="description">Display description for undo history.</param>
        /// <param name="exclusionZones">Optional list of (center, radius) zones where objects should NOT be placed (e.g. spawn points).</param>
        public AIPlaceTerrainObjectMutation(IMutationTarget mutationTarget,
            List<TerrainType> terrainTypes, int centerX, int centerY, int radius,
            float density, string description, List<(Point2D Center, int Radius)> exclusionZones = null)
            : base(mutationTarget)
        {
            this.terrainTypes = terrainTypes ?? throw new ArgumentNullException(nameof(terrainTypes));
            this.centerX = centerX;
            this.centerY = centerY;
            this.radius = Math.Max(1, radius);
            this.density = Math.Max(0.05f, Math.Min(1.0f, density));
            this.description = description;
            this.exclusionZones = exclusionZones ?? new List<(Point2D, int)>();
        }

        // Legacy single-type constructor for backward compatibility
        public AIPlaceTerrainObjectMutation(IMutationTarget mutationTarget,
            TerrainType terrainType, int startX, int startY, int width, int height,
            float density, string description)
            : this(mutationTarget,
                  new List<TerrainType> { terrainType },
                  startX + width / 2, startY + height / 2, Math.Max(width, height) / 2,
                  density, description)
        {
        }

        private readonly List<TerrainType> terrainTypes;
        private readonly int centerX;
        private readonly int centerY;
        private readonly int radius;
        private readonly float density;
        private readonly string description;
        private readonly List<(Point2D Center, int Radius)> exclusionZones;

        // Track placed objects for undo
        private List<PlacedTerrainObj> placedObjects;

        public override string GetDisplayString()
        {
            return string.IsNullOrEmpty(description)
                ? $"AI: Place terrain objects at ({centerX},{centerY}) r={radius} density={density:P0}"
                : $"AI: {description}";
        }

        public override void Perform()
        {
            placedObjects = new List<PlacedTerrainObj>();
            var map = MutationTarget.Map;
            var random = new Random();
            int r2 = radius * radius;

            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    // Circular boundary check
                    int dx = x - centerX;
                    int dy = y - centerY;
                    if (dx * dx + dy * dy > r2)
                        continue;

                    // Spawn exclusion zone check
                    if (IsInExclusionZone(x, y))
                        continue;

                    // Density check: skip cells randomly
                    if (random.NextDouble() > density)
                        continue;

                    var cell = map.GetTile(x, y);
                    if (cell == null)
                        continue;

                    // Skip cells that already have objects, structures, or units
                    if (cell.TerrainObject != null)
                        continue;
                    if (cell.Structures.Count > 0)
                        continue;
                    if (cell.Vehicles.Count > 0)
                        continue;
                    if (cell.HasInfantry())
                        continue;

                    // Randomly pick a terrain type from the list
                    var chosenType = terrainTypes[random.Next(terrainTypes.Count)];

                    // Create and place the terrain object
                    var terrainObj = new TerrainObject(chosenType, new Point2D(x, y));
                    map.AddTerrainObject(terrainObj);
                    placedObjects.Add(new PlacedTerrainObj
                    {
                        Position = new Point2D(x, y),
                        TerrainObject = terrainObj
                    });
                }
            }

            MutationTarget.InvalidateMap();
        }

        /// <summary>
        /// Checks if a position falls within any exclusion zone (e.g. near a spawn point).
        /// </summary>
        private bool IsInExclusionZone(int x, int y)
        {
            foreach (var zone in exclusionZones)
            {
                int dx = x - zone.Center.X;
                int dy = y - zone.Center.Y;
                if (dx * dx + dy * dy <= zone.Radius * zone.Radius)
                    return true;
            }
            return false;
        }

        public override void Undo()
        {
            if (placedObjects == null)
                return;

            var map = MutationTarget.Map;

            foreach (var placed in placedObjects)
            {
                var cell = map.GetTile(placed.Position);
                if (cell?.TerrainObject == placed.TerrainObject)
                    map.RemoveTerrainObject(placed.Position);
            }

            MutationTarget.InvalidateMap();
        }

        private struct PlacedTerrainObj
        {
            public Point2D Position;
            public TerrainObject TerrainObject;
        }
    }
}
