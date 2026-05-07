namespace TSMapEditor.AI.Operations
{
    /// <summary>
    /// A structured map editing operation parsed from AI's response.
    /// </summary>
    public class MapOperation
    {
        /// <summary>
        /// Operation type: "fill_terrain", "place_building", "place_unit", "place_infantry", "place_overlay"
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Top-left X coordinate of the target area.
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Top-left Y coordinate of the target area.
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// Width of the target area in cells.
        /// </summary>
        public int Width { get; set; } = 1;

        /// <summary>
        /// Height of the target area in cells.
        /// </summary>
        public int Height { get; set; } = 1;

        /// <summary>
        /// Name of the target terrain tileset (e.g. "Water", "Grass", "Sand", "Rough").
        /// Used by fill_terrain operations.
        /// </summary>
        public string TileSetName { get; set; } = string.Empty;

        /// <summary>
        /// Height level for set_height operations (0-14).
        /// </summary>
        public int HeightLevel { get; set; }

        /// <summary>
        /// Human-readable description of what this operation does.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// ININame or display name of the object to place (e.g. "APOC", "E1", "GAPILE").
        /// Used by place_building, place_unit, place_infantry operations.
        /// </summary>
        public string ObjectName { get; set; } = string.Empty;

        /// <summary>
        /// Owner house name (e.g. "Americans", "Neutral", "Russians").
        /// Used by place_building, place_unit, place_infantry operations.
        /// </summary>
        public string Owner { get; set; } = string.Empty;

        /// <summary>
        /// Number of objects to place (default 1).
        /// Used by place_unit, place_infantry operations.
        /// </summary>
        public int Count { get; set; } = 1;
    }
}
