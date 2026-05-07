using System.Collections.Generic;
using System.Text;
using TSMapEditor.Models;
using TSMapEditor.Rendering;

namespace TSMapEditor.AI.Operations
{
    /// <summary>
    /// Builds context information about the current map state for the AI's system prompt.
    /// This helps the AI understand the map dimensions, available tilesets, and current state.
    /// </summary>
    public static class MapContextBuilder
    {
        /// <summary>
        /// Builds a context string describing the current map for the AI.
        /// </summary>
        public static string BuildContext(Map map, TheaterGraphics theaterGraphics)
        {
            var sb = new StringBuilder();

            // TS/RA2 maps use an isometric (diamond) coordinate system.
            // map.Size is the logical dimension, but actual cell coords range from
            // 1 to approximately Size.X + Size.Y - 1 in both X and Y, forming a diamond.
            int maxCoord = map.Size.X + map.Size.Y - 1;

            sb.AppendLine("=== 当前地图信息 ===");
            sb.AppendLine($"地图逻辑尺寸: {map.Size.X} x {map.Size.Y}");
            sb.AppendLine($"坐标系: 等距菱形坐标系，有效坐标范围约 X = 1 到 {maxCoord}, Y = 1 到 {maxCoord}（菱形区域，非所有组合有效）");
            sb.AppendLine("重要: 如果用户通过选区指定了坐标范围，请严格使用选区内的坐标，不要自行推测坐标。");
            sb.AppendLine();

            // List available tilesets
            sb.AppendLine("=== 可用的地形类型 (TileSet) ===");
            var tileSets = theaterGraphics.Theater.TileSets;
            var usableTileSets = new List<string>();

            for (int i = 0; i < tileSets.Count; i++)
            {
                var tileSet = tileSets[i];
                if (tileSet.TilesInSet > 0 && !string.IsNullOrWhiteSpace(tileSet.SetName))
                {
                    // Only list 1x1 tilesets (suitable for area fill)
                    if (tileSet.Only1x1 || tileSet.SetName.Contains("LAT") ||
                        tileSet.SetName.Contains("Clear") || tileSet.SetName.Contains("Water") ||
                        tileSet.SetName.Contains("Sand") || tileSet.SetName.Contains("Rough") ||
                        tileSet.SetName.Contains("Green") || tileSet.SetName.Contains("Pave") ||
                        tileSet.SetName.Contains("Dirt"))
                    {
                        usableTileSets.Add($"  - \"{tileSet.SetName}\" (ID={i}, 包含 {tileSet.TilesInSet} 个瓦片)");
                    }
                }
            }

            foreach (string ts in usableTileSets)
                sb.AppendLine(ts);

            sb.AppendLine();
            sb.AppendLine("注意: TileSetName 必须完全匹配上面列出的名称（区分大小写）。");

            // List available houses (owners)
            sb.AppendLine();
            sb.AppendLine("=== 地图中的所属方 (Houses) ===");
            var houses = map.GetHouses();
            if (houses.Count > 0)
            {
                foreach (var house in houses)
                {
                    sb.AppendLine($"  - \"{house.ININame}\"");
                }
            }
            else
            {
                sb.AppendLine("  （无已定义的所属方）");
            }

            sb.AppendLine();
            sb.AppendLine("注意: 放置单位/建筑时 owner 使用上面的所属方名称。不指定时默认 Neutral。");

            return sb.ToString();
        }
    }
}
