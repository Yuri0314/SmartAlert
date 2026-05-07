using System;
using System.Collections.Generic;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.UI;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// A mutation that places terrain objects (trees, rocks, etc.) in a rectangular area.
    /// Supports density control: objects are scattered randomly rather than filling every cell.
    /// Used by the AI system's "place_terrain_object" operation.
    /// </summary>
    public class AIPlaceTerrainObjectMutation : Mutation
    {
        public AIPlaceTerrainObjectMutation(IMutationTarget mutationTarget,
            TerrainType terrainType, int startX, int startY, int width, int height,
            float density, string description)
            : base(mutationTarget)
        {
            this.terrainType = terrainType;
            this.startX = startX;
            this.startY = startY;
            this.width = width;
            this.height = height;
            this.density = Math.Max(0.05f, Math.Min(1.0f, density)); // Clamp 5%-100%
            this.description = description;
        }

        private readonly TerrainType terrainType;
        private readonly int startX;
        private readonly int startY;
        private readonly int width;
        private readonly int height;
        private readonly float density;
        private readonly string description;

        // Track placed objects for undo
        private List<PlacedTerrainObj> placedObjects;

        public override string GetDisplayString()
        {
            return string.IsNullOrEmpty(description)
                ? $"AI: Place {terrainType.ININame} at ({startX},{startY}) {width}x{height} density={density:P0}"
                : $"AI: {description}";
        }

        public override void Perform()
        {
            placedObjects = new List<PlacedTerrainObj>();
            var map = MutationTarget.Map;
            var random = new Random();

            for (int y = startY; y < startY + height; y++)
            {
                for (int x = startX; x < startX + width; x++)
                {
                    // Density check: skip cells randomly based on density
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

                    // Create and place the terrain object
                    var terrainObj = new TerrainObject(terrainType, new Point2D(x, y));
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
