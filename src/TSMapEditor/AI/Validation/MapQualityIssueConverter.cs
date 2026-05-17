using System.Collections.Generic;
using System.Linq;

namespace TSMapEditor.AI.Validation
{
    public static class MapQualityIssueConverter
    {
        public static MapValidationIssue FromQualityFix(QualityFix fix, MapValidationPolicy policy = MapValidationPolicy.Balanced)
        {
            if (fix == null)
            {
                return new MapValidationIssue(
                    MapValidationSeverity.Warning,
                    "UnknownQualityFix",
                    "Received an empty quality fix object.",
                    "",
                    false,
                    "");
            }

            MapValidationSeverity severity;
            string code;
            string area;
            bool allowedByIntent;

            if (fix.ToolName == "set_spawn_point")
            {
                // Spawn points are always errors regardless of policy
                severity = MapValidationSeverity.Error;
                code = "MissingSpawnPoint";
                area = "spawn";
                allowedByIntent = false;
            }
            else if (fix.ToolName == "place_ore")
            {
                code = "MissingSpawnOre";
                area = "resources";
                switch (policy)
                {
                    case MapValidationPolicy.Creative:
                    case MapValidationPolicy.Scenario:
                        severity = MapValidationSeverity.Info;
                        allowedByIntent = true;
                        break;
                    case MapValidationPolicy.LocalOnly:
                        severity = MapValidationSeverity.Intentional;
                        allowedByIntent = true;
                        break;
                    default: // Balanced
                        severity = MapValidationSeverity.Warning;
                        allowedByIntent = false;
                        break;
                }
            }
            else if (fix.ToolName == "fill_terrain")
            {
                code = "LowTerrainDiversity";
                area = "terrain";
                switch (policy)
                {
                    case MapValidationPolicy.Creative:
                    case MapValidationPolicy.Scenario:
                        severity = MapValidationSeverity.Info;
                        allowedByIntent = true;
                        break;
                    case MapValidationPolicy.LocalOnly:
                        severity = MapValidationSeverity.Intentional;
                        allowedByIntent = true;
                        break;
                    default: // Balanced
                        severity = MapValidationSeverity.Info;
                        allowedByIntent = false;
                        break;
                }
            }
            else
            {
                // Unknown tool
                code = "QualityFixRecommended";
                area = "";
                switch (policy)
                {
                    case MapValidationPolicy.Creative:
                    case MapValidationPolicy.Scenario:
                    case MapValidationPolicy.LocalOnly:
                        severity = MapValidationSeverity.Info;
                        allowedByIntent = true;
                        break;
                    default: // Balanced
                        severity = MapValidationSeverity.Warning;
                        allowedByIntent = false;
                        break;
                }
            }

            return new MapValidationIssue(
                severity,
                code,
                fix.Description ?? string.Empty,
                area,
                allowedByIntent,
                fix.ToolName ?? string.Empty
            );
        }

        public static List<MapValidationIssue> FromQualityFixes(IEnumerable<QualityFix> fixes, MapValidationPolicy policy = MapValidationPolicy.Balanced)
        {
            if (fixes == null) return new List<MapValidationIssue>();
            return fixes.Select(f => FromQualityFix(f, policy)).ToList();
        }
    }
}
