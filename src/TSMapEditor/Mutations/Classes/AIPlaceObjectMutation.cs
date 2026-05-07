using System;
using System.Collections.Generic;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.UI;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// The type of object being placed by AI.
    /// </summary>
    public enum AIPlaceObjectType
    {
        Building,
        Vehicle,
        Infantry
    }

    /// <summary>
    /// A mutation that places one or more game objects (buildings, vehicles, infantry)
    /// at specified positions. Unlike WAE's built-in PlaceBuildingMutation etc.,
    /// this mutation allows directly specifying the owner house rather than
    /// depending on MutationTarget.ObjectOwner.
    /// </summary>
    public class AIPlaceObjectMutation : Mutation
    {
        public AIPlaceObjectMutation(IMutationTarget mutationTarget,
            AIPlaceObjectType objectType, string objectININame,
            House owner, List<Point2D> positions, string description)
            : base(mutationTarget)
        {
            this.objectType = objectType;
            this.objectININame = objectININame;
            this.owner = owner;
            this.positions = positions;
            this.description = description;
        }

        private readonly AIPlaceObjectType objectType;
        private readonly string objectININame;
        private readonly House owner;
        private readonly List<Point2D> positions;
        private readonly string description;

        // Track placed objects for undo
        private readonly List<Structure> placedBuildings = new List<Structure>();
        private readonly List<Unit> placedUnits = new List<Unit>();
        private readonly List<Infantry> placedInfantry = new List<Infantry>();

        public override string GetDisplayString()
        {
            return string.IsNullOrEmpty(description)
                ? $"AI: Place {objectININame} x{positions.Count}"
                : $"AI: {description}";
        }

        public override void Perform()
        {
            var map = MutationTarget.Map;

            foreach (var pos in positions)
            {
                var cell = map.GetTile(pos);
                if (cell == null)
                    continue;

                try
                {
                    switch (objectType)
                    {
                        case AIPlaceObjectType.Building:
                            PlaceBuilding(map, pos);
                            break;
                        case AIPlaceObjectType.Vehicle:
                            PlaceVehicle(map, pos);
                            break;
                        case AIPlaceObjectType.Infantry:
                            PlaceInfantry(map, pos);
                            break;
                    }
                }
                catch (Exception)
                {
                    // Skip cells where placement fails (e.g., occupied)
                }
            }

            MutationTarget.InvalidateMap();
        }

        private void PlaceBuilding(Map map, Point2D pos)
        {
            var buildingType = map.Rules.BuildingTypes.Find(
                bt => bt.ININame.Equals(objectININame, StringComparison.OrdinalIgnoreCase));
            if (buildingType == null)
                return;

            var structure = new Structure(buildingType);
            structure.Owner = owner;
            structure.Position = pos;

            // Check if the position is valid; if not, search nearby
            Point2D? validPos = FindValidPlacementPosition(map, structure, pos);
            if (validPos == null)
                return;

            structure.Position = validPos.Value;
            map.PlaceBuilding(structure);
            placedBuildings.Add(structure);
        }

        private void PlaceVehicle(Map map, Point2D pos)
        {
            var unitType = map.Rules.UnitTypes.Find(
                ut => ut.ININame.Equals(objectININame, StringComparison.OrdinalIgnoreCase));
            if (unitType == null)
                return;

            var unit = new Unit(unitType);
            unit.Owner = owner;
            unit.Position = pos;

            // Check placement validity
            if (!map.CanPlaceObjectAt(unit, pos, false, false))
            {
                Point2D? validPos = FindValidPlacementPositionSimple(map, unit, pos);
                if (validPos == null)
                    return;
                unit.Position = validPos.Value;
            }

            map.PlaceUnit(unit);
            placedUnits.Add(unit);
        }

        private void PlaceInfantry(Map map, Point2D pos)
        {
            var infantryType = map.Rules.InfantryTypes.Find(
                it => it.ININame.Equals(objectININame, StringComparison.OrdinalIgnoreCase));
            if (infantryType == null)
                return;

            var cell = map.GetTile(pos);
            if (cell == null)
                return;

            SubCell freeSpot = cell.GetFreeSubCellSpot();
            if (freeSpot == SubCell.None)
            {
                // Try nearby cells
                Point2D? nearby = FindCellWithFreeSubCell(map, pos);
                if (nearby == null)
                    return;
                pos = nearby.Value;
                cell = map.GetTile(pos);
                freeSpot = cell.GetFreeSubCellSpot();
                if (freeSpot == SubCell.None)
                    return;
            }

            var infantry = new Infantry(infantryType);
            infantry.Owner = owner;
            infantry.Position = pos;
            infantry.SubCell = freeSpot;
            map.PlaceInfantry(infantry);
            placedInfantry.Add(infantry);
        }

        /// <summary>
        /// Searches for a valid placement position for a building, spiraling outward from the target.
        /// </summary>
        private Point2D? FindValidPlacementPosition(Map map, Structure structure, Point2D target)
        {
            // Try the target position first
            if (map.CanPlaceObjectAt(structure, target, false, false))
                return target;

            // Spiral outward up to 8 cells away
            for (int radius = 1; radius <= 8; radius++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                            continue; // Only check the perimeter

                        var candidate = new Point2D(target.X + dx, target.Y + dy);
                        structure.Position = candidate;
                        if (map.GetTile(candidate) != null &&
                            map.CanPlaceObjectAt(structure, candidate, false, false))
                        {
                            return candidate;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Searches for a valid placement position for a unit/aircraft.
        /// </summary>
        private Point2D? FindValidPlacementPositionSimple(Map map, IMovable movable, Point2D target)
        {
            for (int radius = 1; radius <= 5; radius++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                            continue;

                        var candidate = new Point2D(target.X + dx, target.Y + dy);
                        if (map.GetTile(candidate) != null &&
                            map.CanPlaceObjectAt(movable, candidate, false, false))
                        {
                            return candidate;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a nearby cell that has a free sub-cell slot for infantry.
        /// </summary>
        private Point2D? FindCellWithFreeSubCell(Map map, Point2D target)
        {
            for (int radius = 1; radius <= 5; radius++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                            continue;

                        var candidate = new Point2D(target.X + dx, target.Y + dy);
                        var cell = map.GetTile(candidate);
                        if (cell != null && cell.GetFreeSubCellSpot() != SubCell.None)
                            return candidate;
                    }
                }
            }

            return null;
        }

        public override void Undo()
        {
            var map = MutationTarget.Map;

            foreach (var structure in placedBuildings)
                map.RemoveBuilding(structure);

            foreach (var unit in placedUnits)
                map.RemoveUnit(unit);

            foreach (var infantry in placedInfantry)
                map.RemoveInfantry(infantry);

            MutationTarget.InvalidateMap();
        }
    }
}
