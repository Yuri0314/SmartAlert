using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TSMapEditor.Models;

namespace TSMapEditor.AI
{
    /// <summary>
    /// Formats House information for the get_houses tool output.
    /// Groups houses by MO faction (HouseType.Side) for easier scanning.
    /// Pure logic — no Map mutation or UI dependencies.
    /// </summary>
    public static class AIHouseSummaryFormatter
    {
        // Side sort order: Allied, Soviet, Epsilon, Foehn, Civilian, Other, Multiplayer slots
        private static readonly string[] SideOrder = { "GDI", "Nod", "ThirdSide", "FourthSide", "Civilian" };

        /// <summary>
        /// Formats a readable summary of all houses in the map,
        /// grouped by MO faction side for easier discovery.
        /// Includes ININame, Country, HouseType, Side, Color, PlayerControl, and Allies.
        /// </summary>
        public static string Format(IEnumerable<House> houses)
        {
            if (houses == null || !houses.Any())
                return "(none) — 当前地图没有定义任何 House/所属方。";

            var houseList = houses.ToList();
            var sb = new StringBuilder();
            sb.AppendLine("=== 当前地图所属方列表 ===");

            // Separate multiplayer template slots from normal houses
            var multiplayerSlots = houseList.Where(IsMultiplayerSlot).ToList();
            var normalHouses = houseList.Where(h => !IsMultiplayerSlot(h)).ToList();

            // Group normal houses by Side
            var groups = normalHouses
                .GroupBy(h => h.HouseType?.Side ?? "")
                .OrderBy(g => GetSortKeyForSide(g.Key))
                .ToList();

            foreach (var group in groups)
            {
                string title = GetFactionGroupTitle(group.Key);
                sb.AppendLine();
                sb.AppendLine($"[{title}]");

                foreach (var house in group)
                {
                    sb.AppendLine(FormatHouseLine(house));
                }
            }

            // Multiplayer template slots
            if (multiplayerSlots.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[Multiplayer Slots / 多人模板槽位 (not recommended for AI placement)]");

                foreach (var house in multiplayerSlots)
                {
                    sb.AppendLine(FormatHouseLine(house));
                }
            }

            sb.AppendLine();
            sb.Append("⚠️ 使用精确的 ININame 值作为 owner 参数。不要缩写或部分匹配所属方名称。");

            return sb.ToString();
        }

        /// <summary>
        /// Formats a single house line with detail fields.
        /// </summary>
        private static string FormatHouseLine(House house)
        {
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
                return $"  - {house.ININame} ({string.Join(", ", details)})";

            return $"  - {house.ININame}";
        }

        /// <summary>
        /// Maps WAE Side values to MO-friendly faction group titles.
        /// </summary>
        internal static string GetFactionGroupTitle(string side)
        {
            if (string.IsNullOrEmpty(side))
                return "Other / 未分组";

            return side switch
            {
                "GDI" => "Allied / 盟军 (Side=GDI)",
                "Nod" => "Soviet / 苏军 (Side=Nod)",
                "ThirdSide" => "Epsilon / 厄普西隆 (Side=ThirdSide)",
                "FourthSide" => "Foehn / 焚风 (Side=FourthSide)",
                "Civilian" => "Neutral / Special / 中立与特殊 (Side=Civilian)",
                _ => $"Other / 未分组 (Side={side})",
            };
        }

        /// <summary>
        /// Returns a numeric sort key for side ordering.
        /// </summary>
        private static int GetSortKeyForSide(string side)
        {
            if (string.IsNullOrEmpty(side))
                return SideOrder.Length;

            int index = Array.IndexOf(SideOrder, side);
            return index >= 0 ? index : SideOrder.Length;
        }

        /// <summary>
        /// Detects multiplayer template slot houses by ININame prefix.
        /// </summary>
        internal static bool IsMultiplayerSlot(House house)
        {
            return house?.ININame?.StartsWith("<Player @", StringComparison.OrdinalIgnoreCase) == true;
        }
    }
}
