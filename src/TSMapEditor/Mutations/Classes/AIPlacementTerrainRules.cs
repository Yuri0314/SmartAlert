using TSMapEditor.CCEngine;
using TSMapEditor.Models;
using TSMapEditor.Rendering;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// AI-specific terrain legality guard for placement mutations.
    /// Rejects water, land-unit-impassable land (rock), and optionally ramp cells
    /// to prevent AI from placing objects on invalid terrain.
    ///
    /// This is NOT a replacement for WAE's global CanPlaceObjectAt / CanAddObject.
    /// Manual editor placement may intentionally allow unusual placements.
    /// </summary>
    internal static class AIPlacementTerrainRules
    {
        /// <summary>
        /// Checks whether a map cell is valid ground for AI placement.
        /// </summary>
        /// <param name="map">The map instance (provides TheaterInstance for tile lookups).</param>
        /// <param name="cell">The map tile to check.</param>
        /// <param name="requireFlat">If true, rejects any ramp cell (RampType != None).
        /// Use true for buildings, decorations, and terrain objects.
        /// Use false for ore (which may exist on gentle slopes in RA2).</param>
        /// <returns>True if the cell is valid for AI ground placement.</returns>
        public static bool IsValidGroundCell(Map map, MapTile cell, bool requireFlat = true)
        {
            if (cell == null)
                return false;

            // Fast path: use MapTile.MatchesLandType if TileImage is cached
            // But we need the raw terrain type int for the Helpers methods,
            // so go through TheaterInstance.

            var theaterInstance = map.TheaterInstance;
            if (theaterInstance == null)
                return true; // No theater loaded (e.g. unit tests without full map init); allow placement.

            ITileImage tile;
            try
            {
                tile = theaterInstance.GetTile(cell.TileIndex);
            }
            catch
            {
                return false; // Invalid tile index
            }

            if (tile == null)
                return false;

            ISubTileImage subTile;
            try
            {
                subTile = tile.GetSubTile(cell.SubTileIndex);
            }
            catch
            {
                return false; // Invalid sub-tile index
            }

            if (subTile?.TmpImage == null)
                return false;

            int terrainType = subTile.TmpImage.TerrainType;

            // Reject water (land type 0x9)
            if (Helpers.IsLandTypeWater(terrainType))
                return false;

            // Reject land-unit-impassable terrain (rock = 0x7, 0x8, 0xF)
            // Also rejects ice (0x1-0x4), water (0x9), beach (0xA) when considerLandUnitsOnly is true
            if (Helpers.IsLandTypeImpassable(terrainType, true))
                return false;

            // Reject ramp cells if flat terrain is required
            if (requireFlat && subTile.TmpImage.RampType != RampType.None)
                return false;

            return true;
        }

        /// <summary>
        /// Checks whether all foundation cells of a building are on valid ground.
        /// </summary>
        /// <param name="map">The map instance.</param>
        /// <param name="structure">The building to check (must have Position set).</param>
        /// <returns>True if all foundation cells are valid for ground placement.</returns>
        public static bool IsValidBuildingGround(Map map, Structure structure)
        {
            bool valid = true;

            structure.ObjectType.ArtConfig.DoForFoundationCoordsOrOrigin(offset =>
            {
                if (!valid)
                    return; // Short-circuit on first failure

                var foundationCell = map.GetTile(structure.Position + offset);
                if (!IsValidGroundCell(map, foundationCell, requireFlat: true))
                {
                    valid = false;
                }
            });

            return valid;
        }
    }
}
