using System;
using System.Collections.Generic;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.UI;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// A mutation that places decoration buildings (civilian structures) in a circular area.
    /// Supports undo via tracking all placed structures.
    /// Used by the AI system's "place_decorations" operation.
    /// </summary>
    public class AIPlaceDecorationsMutation : Mutation
    {
        public AIPlaceDecorationsMutation(IMutationTarget mutationTarget,
            List<BuildingType> buildingTypes, House owner,
            int centerX, int centerY, int radius, float density,
            List<Point2D> spawnExclusionPoints, int spawnExclusionRadius,
            string description)
            : base(mutationTarget)
        {
            this.buildingTypes = buildingTypes ?? throw new ArgumentNullException(nameof(buildingTypes));
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.centerX = centerX;
            this.centerY = centerY;
            this.radius = Math.Max(1, radius);
            this.density = Math.Max(0.01f, Math.Min(1.0f, density));
            this.spawnExclusionPoints = spawnExclusionPoints ?? new List<Point2D>();
            this.spawnExclusionRadius = spawnExclusionRadius;
            this.description = description;
        }

        private readonly List<BuildingType> buildingTypes;
        private readonly House owner;
        private readonly int centerX;
        private readonly int centerY;
        private readonly int radius;
        private readonly float density;
        private readonly List<Point2D> spawnExclusionPoints;
        private readonly int spawnExclusionRadius;
        private readonly string description;

        // Track placed structures for undo
        private List<Structure> placedStructures;

        public override string GetDisplayString()
        {
            return string.IsNullOrEmpty(description)
                ? $"AI: Place decorations at ({centerX},{centerY}) r={radius}"
                : $"AI: {description}";
        }

        public override void Perform()
        {
            placedStructures = new List<Structure>();
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

                    // Density check
                    if (random.NextDouble() > density)
                        continue;

                    // Skip cells near spawn points
                    bool nearSpawn = false;
                    foreach (var sp in spawnExclusionPoints)
                    {
                        if (Math.Abs(x - sp.X) + Math.Abs(y - sp.Y) <= spawnExclusionRadius)
                        {
                            nearSpawn = true;
                            break;
                        }
                    }
                    if (nearSpawn)
                        continue;

                    var cell = map.GetTile(x, y);
                    if (cell == null || cell.TerrainObject != null ||
                        cell.Structures.Count > 0 || cell.Vehicles.Count > 0)
                        continue;

                    var chosenType = buildingTypes[random.Next(buildingTypes.Count)];
                    var structure = new Structure(chosenType)
                    {
                        Position = new Point2D(x, y),
                        Owner = owner,
                        Facing = (byte)(random.Next(8) * 32),
                        HP = 256
                    };

                    map.PlaceBuilding(structure);
                    placedStructures.Add(structure);
                }
            }

            MutationTarget.InvalidateMap();
        }

        public override void Undo()
        {
            if (placedStructures == null)
                return;

            var map = MutationTarget.Map;

            foreach (var structure in placedStructures)
            {
                map.RemoveBuilding(structure);
            }

            MutationTarget.InvalidateMap();
        }
    }
}
