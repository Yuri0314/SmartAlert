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
