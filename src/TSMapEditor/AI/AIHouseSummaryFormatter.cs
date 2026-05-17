using System.Collections.Generic;
using System.Linq;
using System.Text;
using TSMapEditor.Models;

namespace TSMapEditor.AI
{
    /// <summary>
    /// Formats House information for the get_houses tool output.
    /// Pure logic — no Map mutation or UI dependencies.
    /// </summary>
    public static class AIHouseSummaryFormatter
    {
        /// <summary>
        /// Formats a readable summary of all houses in the map.
        /// Includes ININame, Country, HouseType, Side, Color, PlayerControl, and Allies.
        /// </summary>
        public static string Format(IEnumerable<House> houses)
        {
            if (houses == null || !houses.Any())
                return "(none) — 当前地图没有定义任何 House/所属方。";

            var sb = new StringBuilder();
            sb.AppendLine("=== 当前地图所属方列表 ===");

            foreach (var house in houses)
            {
                sb.Append($"- {house.ININame}");

                var details = new List<string>();

                if (!string.IsNullOrWhiteSpace(house.Country))
                    details.Add($"Country={house.Country}");

                if (house.HouseType != null)
                {
                    details.Add($"HouseType={house.HouseType.ININame}");
                    if (!string.IsNullOrWhiteSpace(house.HouseType.Side))
                        details.Add($"Side={house.HouseType.Side}");
                }

                if (!string.IsNullOrWhiteSpace(house.Color))
                    details.Add($"Color={house.Color}");

                details.Add(house.PlayerControl ? "PlayerControl=Yes" : "PlayerControl=No");

                if (house.Allies != null && house.Allies.Count > 0)
                    details.Add($"Allies={string.Join(",", house.Allies.Select(a => a.ININame))}");

                if (details.Count > 0)
                    sb.Append($" ({string.Join(", ", details)})");

                sb.AppendLine();
            }

            sb.AppendLine();
            sb.Append("⚠️ 使用精确的 ININame 值作为 owner 参数。不要缩写或部分匹配所属方名称。");

            return sb.ToString();
        }
    }
}
