using System.Collections.Generic;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.UI;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// A mutation that clears all objects (buildings, vehicles, infantry, overlays,
    /// terrain objects, smudges) from a rectangular area.
    /// Used by the AI system's "clear_area" operation.
    /// </summary>
    public class AIClearAreaMutation : Mutation
    {
        public AIClearAreaMutation(IMutationTarget mutationTarget,
            int startX, int startY, int width, int height, string description)
            : base(mutationTarget)
        {
            this.startX = startX;
            this.startY = startY;
            this.width = width;
            this.height = height;
            this.description = description;
        }

        private readonly int startX;
        private readonly int startY;
        private readonly int width;
        private readonly int height;
        private readonly string description;

        // Undo data
        private List<RemovedStructure> removedStructures;
        private List<RemovedUnit> removedUnits;
        private List<RemovedInfantryEntry> removedInfantry;
        private List<RemovedOverlay> removedOverlays;
        private List<RemovedTerrainObj> removedTerrainObjects;
        private List<RemovedSmudge> removedSmudges;

        public override string GetDisplayString()
        {
            return string.IsNullOrEmpty(description)
                ? $"AI: Clear area at ({startX},{startY}) {width}x{height}"
                : $"AI: {description}";
        }

        public override void Perform()
        {
            var map = MutationTarget.Map;
            removedStructures = new List<RemovedStructure>();
            removedUnits = new List<RemovedUnit>();
            removedInfantry = new List<RemovedInfantryEntry>();
            removedOverlays = new List<RemovedOverlay>();
            removedTerrainObjects = new List<RemovedTerrainObj>();
            removedSmudges = new List<RemovedSmudge>();

            for (int y = startY; y < startY + height; y++)
            {
                for (int x = startX; x < startX + width; x++)
                {
                    var cell = map.GetTile(x, y);
                    if (cell == null)
                        continue;

                    var pos = new Point2D(x, y);

                    // Remove structures
                    if (cell.Structures.Count > 0)
                    {
                        foreach (var structure in new List<Structure>(cell.Structures))
                        {
                            removedStructures.Add(new RemovedStructure { Structure = structure });
                            map.RemoveBuilding(structure);
                        }
                    }

                    // Remove vehicles
                    if (cell.Vehicles.Count > 0)
                    {
                        foreach (var unit in new List<Unit>(cell.Vehicles))
                        {
                            removedUnits.Add(new RemovedUnit { Unit = unit });
                            map.RemoveUnit(unit);
                        }
                    }

                    // Remove infantry
                    if (cell.HasInfantry())
                    {
                        for (int i = 0; i < cell.Infantry.Length; i++)
                        {
                            if (cell.Infantry[i] != null)
                            {
                                removedInfantry.Add(new RemovedInfantryEntry { Infantry = cell.Infantry[i] });
                                map.RemoveInfantry(cell.Infantry[i]);
                            }
                        }
                    }

                    // Remove overlays
                    if (cell.Overlay != null)
                    {
                        removedOverlays.Add(new RemovedOverlay
                        {
                            Position = pos,
                            Overlay = cell.Overlay
                        });
                        cell.Overlay = null;
                    }

                    // Remove terrain objects
                    if (cell.TerrainObject != null)
                    {
                        removedTerrainObjects.Add(new RemovedTerrainObj
                        {
                            Position = pos,
                            TerrainObject = cell.TerrainObject
                        });
                        map.RemoveTerrainObject(pos);
                    }

                    // Remove smudges
                    if (cell.Smudge != null)
                    {
                        removedSmudges.Add(new RemovedSmudge
                        {
                            Position = pos,
                            Smudge = cell.Smudge
                        });
                        cell.Smudge = null;
                    }
                }
            }

            MutationTarget.InvalidateMap();
        }

        public override void Undo()
        {
            var map = MutationTarget.Map;

            // Restore in reverse order of removal

            foreach (var data in removedSmudges)
            {
                var cell = map.GetTile(data.Position);
                if (cell != null)
                    cell.Smudge = data.Smudge;
            }

            foreach (var data in removedTerrainObjects)
            {
                var cell = map.GetTile(data.Position);
                if (cell != null && cell.TerrainObject == null)
                    map.AddTerrainObject(data.TerrainObject);
            }

            foreach (var data in removedOverlays)
            {
                var cell = map.GetTile(data.Position);
                if (cell != null)
                    cell.Overlay = data.Overlay;
            }

            foreach (var data in removedInfantry)
                map.PlaceInfantry(data.Infantry);

            foreach (var data in removedUnits)
                map.PlaceUnit(data.Unit);

            foreach (var data in removedStructures)
                map.PlaceBuilding(data.Structure);

            MutationTarget.InvalidateMap();
        }

        // --- Undo data structs ---
        private struct RemovedStructure { public Structure Structure; }
        private struct RemovedUnit { public Unit Unit; }
        private struct RemovedInfantryEntry { public Infantry Infantry; }
        private struct RemovedOverlay { public Point2D Position; public Overlay Overlay; }
        private struct RemovedTerrainObj { public Point2D Position; public TerrainObject TerrainObject; }
        private struct RemovedSmudge { public Point2D Position; public Smudge Smudge; }
    }
}
