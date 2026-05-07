using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TSMapEditor.Models;
using TSMapEditor.Rendering;

namespace TSMapEditor.AI.Operations
{
    /// <summary>
    /// Builds context information about the current map state for the AI's system prompt.
    /// This helps the AI understand the map dimensions, available tilesets, unit types, and current state.
    /// 
    /// Design: Each section is a separate method for modularity. Future phases can
    /// selectively include/exclude sections based on context (e.g., planning vs execution mode).
    /// </summary>
    public static class MapContextBuilder
    {
        /// <summary>
        /// Builds a complete context string describing the current map for the AI.
        /// </summary>
        public static string BuildContext(Map map, TheaterGraphics theaterGraphics)
        {
            var sb = new StringBuilder();

            AppendMapDimensions(sb, map);
            AppendTileSetCatalog(sb, theaterGraphics);
            AppendHouseList(sb, map);
            AppendUnitCatalog(sb, map);

            return sb.ToString();
        }

        /// <summary>
        /// Appends map coordinate system information.
        /// </summary>
        private static void AppendMapDimensions(StringBuilder sb, Map map)
        {
            // TS/RA2 maps use an isometric (diamond) coordinate system.
            // map.Size is the logical dimension, but actual cell coords range from
            // 1 to approximately Size.X + Size.Y - 1 in both X and Y, forming a diamond.
            int maxCoord = map.Size.X + map.Size.Y - 1;

            sb.AppendLine("=== 当前地图信息 ===");
            sb.AppendLine($"地图逻辑尺寸: {map.Size.X} x {map.Size.Y}");
            sb.AppendLine($"坐标系: 等距菱形坐标系，有效坐标范围约 X = 1 到 {maxCoord}, Y = 1 到 {maxCoord}（菱形区域，非所有组合有效）");
            sb.AppendLine("重要: 如果用户通过选区指定了坐标范围，请严格使用选区内的坐标，不要自行推测坐标。");
            sb.AppendLine();
        }

        /// <summary>
        /// Appends available tileset information for terrain operations.
        /// </summary>
        private static void AppendTileSetCatalog(StringBuilder sb, TheaterGraphics theaterGraphics)
        {
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
            sb.AppendLine();
        }

        /// <summary>
        /// Appends available house (owner/faction) information.
        /// </summary>
        private static void AppendHouseList(StringBuilder sb, Map map)
        {
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
            sb.AppendLine();
        }

        /// <summary>
        /// Appends a compact catalog of all available building, vehicle, and infantry types.
        /// Groups them by the Owner field (faction) for easier AI comprehension.
        /// This enables the AI to recognize Mental Omega mod units, not just vanilla RA2.
        /// </summary>
        private static void AppendUnitCatalog(StringBuilder sb, Map map)
        {
            sb.AppendLine("=== 可用的建筑/载具/步兵类型 ===");
            sb.AppendLine("使用 objectName 字段时请填写 ININame（第一列）。系统支持模糊匹配。");
            sb.AppendLine();

            // Buildings
            AppendTypeCatalog(sb, "建筑 (place_building)", map.Rules.BuildingTypes);

            // Vehicles
            AppendTypeCatalog(sb, "载具 (place_unit)", map.Rules.UnitTypes);

            // Infantry
            AppendTypeCatalog(sb, "步兵 (place_infantry)", map.Rules.InfantryTypes);
        }

        /// <summary>
        /// Appends a grouped catalog for a single object type category.
        /// Groups by Owner field (e.g., "AlliedCountries", "SovietCountries").
        /// Format: compact "ININame: DisplayName" per line, grouped by faction.
        /// </summary>
        private static void AppendTypeCatalog<T>(StringBuilder sb, string categoryName, List<T> types) where T : TechnoType
        {
            if (types == null || types.Count == 0)
                return;

            sb.AppendLine($"--- {categoryName} ---");

            // Group by Owner (faction). Types without Owner go to "通用" group.
            var grouped = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in types)
            {
                // Skip editor-invisible types (internal/system types)
                if (!t.EditorVisible)
                    continue;

                string owner = string.IsNullOrWhiteSpace(t.Owner) ? "通用" : t.Owner;
                if (!grouped.ContainsKey(owner))
                    grouped[owner] = new List<string>();

                string displayName = t.GetEditorDisplayName();
                // Only include display name if it's different from ININame (to save tokens)
                if (displayName != t.ININame && !string.IsNullOrWhiteSpace(displayName))
                    grouped[owner].Add($"{t.ININame}: {displayName}");
                else
                    grouped[owner].Add(t.ININame);
            }

            // Output each group
            foreach (var kvp in grouped.OrderBy(g => g.Key))
            {
                sb.AppendLine($"  [{kvp.Key}] ({kvp.Value.Count}个)");

                // Join entries with comma for compactness (saves tokens vs one-per-line)
                const int maxPerLine = 8;
                for (int i = 0; i < kvp.Value.Count; i += maxPerLine)
                {
                    int take = Math.Min(maxPerLine, kvp.Value.Count - i);
                    sb.AppendLine("    " + string.Join(", ", kvp.Value.Skip(i).Take(take)));
                }
            }

            sb.AppendLine();
        }
    }
}
