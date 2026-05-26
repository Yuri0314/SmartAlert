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

        // Track placed objects for undo and result reporting
        private readonly List<Structure> placedBuildings = new List<Structure>();
        private readonly List<Unit> placedUnits = new List<Unit>();
        private readonly List<Infantry> placedInfantry = new List<Infantry>();

        /// <summary>Number of positions requested for placement.</summary>
        public int RequestedCount => positions?.Count ?? 0;

        /// <summary>Number of objects actually placed after Perform().</summary>
        public int PlacedCount => placedBuildings.Count + placedUnits.Count + placedInfantry.Count;

        /// <summary>Whether any objects were successfully placed.</summary>
        public bool PlacedAny => PlacedCount > 0;

        public override string GetDisplayString()
        {
            return string.IsNullOrEmpty(description)
                ? $"AI: Place {objectININame} x{positions.Count}"
                : $"AI: {description}";
        }

        public override void Perform()
        {
            // Clear previous state to ensure accurate counts if Perform() is called more than once
            placedBuildings.Clear();
            placedUnits.Clear();
            placedInfantry.Clear();

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

            // Check placement validity: WAE same-type check + AI cross-type guard
            var cell = map.GetTile(pos);
            if (!map.CanPlaceObjectAt(unit, pos, false, false) || !IsCellFreeOfTechno(cell))
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

            // AI cross-type guard: infantry should not be placed on cells with
            // buildings, vehicles, or aircraft. Infantry-infantry sharing is allowed
            // as long as a free subcell exists.
            if (!CanPlaceInfantryOnCell(cell))
            {
                // Try nearby cells
                Point2D? nearby = FindCellWithFreeInfantrySlot(map, pos);
                if (nearby == null)
                    return;
                pos = nearby.Value;
                cell = map.GetTile(pos);
                if (!CanPlaceInfantryOnCell(cell))
                    return;
            }

            SubCell freeSpot = cell.GetFreeSubCellSpot();
            if (freeSpot == SubCell.None)
                return;

            var infantry = new Infantry(infantryType);
            infantry.Owner = owner;
            infantry.Position = pos;
            infantry.SubCell = freeSpot;
            map.PlaceInfantry(infantry);
            placedInfantry.Add(infantry);
        }

        // ─── AI Cross-Type Occupancy Guards ─────────────────────────

        /// <summary>
        /// Returns true if the cell has no techno objects (buildings, vehicles,
        /// aircraft, or infantry). Used as an AI safety guard to prevent
        /// cross-type overlap during AI-driven placement.
        /// Does NOT block placement due to overlays, smudges, waypoints,
        /// terrain objects, or cell tags.
        /// </summary>
        internal static bool IsCellFreeOfTechno(MapTile cell)
        {
            if (cell == null)
                return false;

            return !cell.HasTechno();
        }

        /// <summary>
        /// Returns true if an infantry unit can be placed on the cell.
        /// Allows infantry-infantry sharing (multiple infantry per cell via subcells)
        /// but blocks placement on cells with buildings, vehicles, or aircraft.
        /// </summary>
        internal static bool CanPlaceInfantryOnCell(MapTile cell)
        {
            if (cell == null)
                return false;

            // Block if cell has any non-infantry techno
            if (cell.Structures.Count > 0 ||
                cell.Vehicles.Count > 0 ||
                cell.Aircraft.Count > 0)
            {
                return false;
            }

            // Allow if there's a free infantry subcell
            return cell.GetFreeSubCellSpot() != SubCell.None;
        }

        // ─── Position Search Helpers ────────────────────────────────

        /// <summary>
        /// Searches for a valid placement position for a building, spiraling outward from the target.
        /// Checks WAE occupancy, AI cross-type techno guard, AND terrain height uniformity across foundation.
        /// </summary>
        private Point2D? FindValidPlacementPosition(Map map, Structure structure, Point2D target)
        {
            // Try the target position first
            if (IsValidBuildingPosition(map, structure, target))
                return target;

            // Spiral outward up to 10 cells away
            for (int radius = 1; radius <= 10; radius++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                            continue; // Only check the perimeter

                        var candidate = new Point2D(target.X + dx, target.Y + dy);
                        if (IsValidBuildingPosition(map, structure, candidate))
                            return candidate;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Checks if a building can be placed at the given position:
        /// 1. All foundation cells must exist on the map
        /// 2. All foundation cells must be at the same height level (no cliff spanning)
        /// 3. No overlap with existing buildings (CanPlaceObjectAt)
        /// 4. No cross-type techno overlap on any foundation cell (AI guard)
        /// </summary>
        private bool IsValidBuildingPosition(Map map, Structure structure, Point2D pos)
        {
            var cell = map.GetTile(pos);
            if (cell == null)
                return false;

            structure.Position = pos;

            // Check WAE's built-in occupancy validation
            if (!map.CanPlaceObjectAt(structure, pos, false, false))
                return false;

            // Check height uniformity and cross-type techno guard across all foundation cells
            byte baseHeight = cell.Level;
            bool valid = true;

            structure.ObjectType.ArtConfig.DoForFoundationCoordsOrOrigin(offset =>
            {
                var foundationCell = map.GetTile(pos + offset);
                if (foundationCell == null)
                {
                    valid = false;
                    return;
                }

                if (foundationCell.Level != baseHeight)
                {
                    valid = false;
                    return;
                }

                // AI cross-type guard: reject if any foundation cell has vehicles, aircraft, or infantry
                if (foundationCell.Vehicles.Count > 0 ||
                    foundationCell.Aircraft.Count > 0 ||
                    foundationCell.HasInfantry())
                {
                    valid = false;
                }
            });

            return valid;
        }

        /// <summary>
        /// Searches for a valid placement position for a unit/aircraft.
        /// Checks both WAE same-type validation and AI cross-type techno guard.
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
                        var candidateCell = map.GetTile(candidate);
                        if (candidateCell != null &&
                            map.CanPlaceObjectAt(movable, candidate, false, false) &&
                            IsCellFreeOfTechno(candidateCell))
                        {
                            return candidate;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a nearby cell that is free of non-infantry techno and has a free
        /// sub-cell slot for infantry placement.
        /// </summary>
        private Point2D? FindCellWithFreeInfantrySlot(Map map, Point2D target)
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
                        if (cell != null && CanPlaceInfantryOnCell(cell))
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
