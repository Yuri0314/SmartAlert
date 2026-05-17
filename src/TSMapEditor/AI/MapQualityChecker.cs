using System;
using System.Collections.Generic;
using System.Linq;
using Rampastring.Tools;
using TSMapEditor.AI.Validation;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.Rendering;

namespace TSMapEditor.AI
{
    /// <summary>
    /// Post-generation quality checker that programmatically verifies map completeness
    /// after the AI agent loop finishes. Identifies missing elements (spawn points without ore,
    /// monotonous terrain, etc.) and returns fix actions for the ToolExecutor to apply.
    /// 
    /// This is the engineering safety net — we don't rely on the AI's self-discipline
    /// to guarantee map completeness.
    /// </summary>
    public class MapQualityChecker
    {
        private readonly Map map;
        private readonly PositionResolver positionResolver;
        private readonly TheaterGraphics theaterGraphics;

        public MapQualityChecker(Map map, TheaterGraphics theaterGraphics)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.theaterGraphics = theaterGraphics;
            this.positionResolver = new PositionResolver(map);
        }

        /// <summary>
        /// Runs all quality checks and returns a list of fix actions.
        /// Each fix action is a (toolName, argumentsJson, description) tuple
        /// that can be directly passed to ToolExecutor.Execute().
        /// </summary>
        /// <param name="expectedPlayers">Number of players the user requested (0 = skip spawn checks)</param>
        public List<QualityFix> Check(int expectedPlayers)
        {
            var fixes = new List<QualityFix>();

            if (expectedPlayers > 0)
            {
                CheckSpawnPoints(expectedPlayers, fixes);
                CheckOreNearSpawns(fixes);
                CheckSpawnSpacing(fixes);
            }

            CheckTerrainDiversity(fixes);

            Logger.Log($"MapQualityChecker: {fixes.Count} issue(s) found");
            return fixes;
        }

        /// <summary>
        /// Returns semantic validation issues for the current map state.
        /// Delegates to Check() and converts results to MapValidationIssue.
        /// Does not mutate the map or execute tools.
        /// </summary>
        public List<MapValidationIssue> CheckIssues(int expectedPlayers, MapValidationPolicy policy = MapValidationPolicy.Balanced)
        {
            var fixes = Check(expectedPlayers);
            return MapQualityIssueConverter.FromQualityFixes(fixes, policy);
        }

        /// <summary>
        /// Detects the number of players the AI was asked to create based on the
        /// user's last message. Returns 0 if not a map generation request.
        /// </summary>
        public static int DetectExpectedPlayers(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return 0;

            string msg = userMessage.ToLowerInvariant();

            // Detect "局部编辑" patterns — these don't need quality checks
            string[] localEditPatterns = new[]
            {
                "加树", "加几棵树", "放树", "放几棵树",
                "改成", "换成", "铺上", "铺设",
                "删除", "清除", "移除",
                "在这", "这里", "那里", "右边", "左边",
                "名字", "命名", "改名",
            };

            foreach (var pattern in localEditPatterns)
            {
                if (msg.Contains(pattern))
                    return 0; // Local edit, skip quality checks
            }

            // Detect map generation patterns
            // NvM patterns: "1v3" "1v2" "2v3" etc.
            var nvmMatch = System.Text.RegularExpressions.Regex.Match(msg, @"(\d+)\s*[vV]\s*(\d+)");
            if (nvmMatch.Success)
            {
                int n = int.Parse(nvmMatch.Groups[1].Value);
                int m = int.Parse(nvmMatch.Groups[2].Value);
                return Math.Min(n + m, 8); // Cap at 8 players
            }

            // "2人" "4人" "2v2" "4v4" "1v1" "3v3" "2人对战" etc.
            if (msg.Contains("1人") || msg.Contains("单人"))
                return 2; // 1v1 still needs 2 spawn points

            if (msg.Contains("4人"))
                return 4;

            if (msg.Contains("6人"))
                return 6;

            if (msg.Contains("8人"))
                return 8;

            if (msg.Contains("2人") || msg.Contains("双人"))
                return 2;

            // Generic "generate map" without player count
            if (msg.Contains("生成") || msg.Contains("创建") || msg.Contains("做一张") || msg.Contains("做一个"))
                return 2; // Default to 2 players

            return 0; // Not a map generation request
        }

        // ─── Check: Spawn Points ────────────────────────────────────

        private void CheckSpawnPoints(int expectedPlayers, List<QualityFix> fixes)
        {
            int actualSpawns = CountSpawnPoints();

            if (actualSpawns >= expectedPlayers)
                return; // Enough spawn points

            Logger.Log($"MapQualityChecker: Expected {expectedPlayers} spawn points, found {actualSpawns}");

            // Get positions of existing spawns to avoid overlap
            var existingPositions = GetSpawnPositions();

            // Standard spawn layouts (percentage coordinates)
            var layouts = GetStandardSpawnLayout(expectedPlayers);

            for (int i = 0; i < expectedPlayers; i++)
            {
                // Check if spawn point i already exists
                bool exists = map.Waypoints.Any(wp => wp.Identifier == i && wp.Position.X >= 0);
                if (exists)
                    continue;

                if (i < layouts.Count)
                {
                    var (xPct, yPct) = layouts[i];
                    fixes.Add(new QualityFix(
                        "set_spawn_point",
                        $"{{\"player_index\": {i}, \"x_pct\": {xPct}, \"y_pct\": {yPct}}}",
                        $"补充玩家{i + 1}出生点 ({xPct}%,{yPct}%)"
                    ));
                }
            }
        }

        // ─── Check: Ore near spawns ─────────────────────────────────

        private void CheckOreNearSpawns(List<QualityFix> fixes)
        {
            var spawnPositions = GetSpawnPositions();

            foreach (var (playerIndex, pos) in spawnPositions)
            {
                // Check if there's any ore overlay within a radius of the spawn point
                bool hasOre = HasOreNearPosition(pos, 15);

                if (!hasOre)
                {
                    // Calculate ore position: offset 5% from spawn
                    // Use PositionResolver to convert back to percentage (approximate)
                    int center = positionResolver.Center;
                    int radius = positionResolver.DiamondRadius;
                    int safeRadius = (int)(radius * 0.70);

                    // Approximate percentage from absolute position
                    int xPctApprox = safeRadius > 0 ? (int)((pos.X - center) * 50.0 / safeRadius + 50) : 50;
                    int yPctApprox = safeRadius > 0 ? (int)((pos.Y - center) * 50.0 / safeRadius + 50) : 50;

                    // Offset ore position by +5% in X
                    int oreXPct = Math.Min(95, xPctApprox + 5);
                    int oreYPct = yPctApprox;

                    fixes.Add(new QualityFix(
                        "place_ore",
                        $"{{\"x_pct\": {oreXPct}, \"y_pct\": {oreYPct}, \"amount\": \"medium\", \"type\": \"ore\"}}",
                        $"为玩家{playerIndex + 1}补充矿石"
                    ));
                }
            }
        }

        // ─── Check: Spawn Spacing ──────────────────────────────────

        private void CheckSpawnSpacing(List<QualityFix> fixes)
        {
            var spawns = GetSpawnPositions();
            if (spawns.Count < 2) return;

            int minDistance = positionResolver.DiamondRadius / 3; // At least 1/3 of map radius apart

            for (int i = 0; i < spawns.Count; i++)
            {
                for (int j = i + 1; j < spawns.Count; j++)
                {
                    int dx = spawns[i].pos.X - spawns[j].pos.X;
                    int dy = spawns[i].pos.Y - spawns[j].pos.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    if (dist < minDistance)
                    {
                        Logger.Log($"MapQualityChecker: Spawn {spawns[i].playerIndex} and {spawns[j].playerIndex} too close ({dist:F0} cells, min={minDistance})");
                        // We can't easily auto-fix spawn positions without knowing the intended layout,
                        // so we log a warning. The improved system prompt should prevent this.
                    }
                }
            }
        }

        // ─── Check: Terrain Diversity ───────────────────────────────

        private void CheckTerrainDiversity(List<QualityFix> fixes)
        {
            // Sample tiles across the map to check terrain diversity
            var tileIndices = new HashSet<int>();
            int center = positionResolver.Center;
            int radius = positionResolver.DiamondRadius;
            int sampleStep = Math.Max(1, radius / 10);

            int sampleCount = 0;
            for (int dy = -radius; dy <= radius; dy += sampleStep)
            {
                for (int dx = -radius; dx <= radius; dx += sampleStep)
                {
                    if (Math.Abs(dx) + Math.Abs(dy) > radius) continue;
                    var cell = map.GetTile(center + dx, center + dy);
                    if (cell != null)
                    {
                        tileIndices.Add(cell.TileIndex);
                        sampleCount++;
                    }
                }
            }

            // If we have very few distinct tile types (< 2), add terrain patches
            if (tileIndices.Count < 2 && sampleCount > 10)
            {
                Logger.Log($"MapQualityChecker: Only {tileIndices.Count} tile type(s) found across {sampleCount} samples");

                // Add 3 terrain variation patches at different positions
                string[] varTerrains = GetVariationTerrains();
                var varPositions = new[] { (30, 70), (70, 30), (50, 50) };

                for (int i = 0; i < Math.Min(varTerrains.Length, varPositions.Length); i++)
                {
                    var (xPct, yPct) = varPositions[i];
                    fixes.Add(new QualityFix(
                        "fill_terrain",
                        $"{{\"terrain\": \"{varTerrains[i]}\", \"scope\": \"medium_patch\", \"x_pct\": {xPct}, \"y_pct\": {yPct}}}",
                        $"补充地形变化 ({varTerrains[i]})"
                    ));
                }
            }
        }

        // ─── Helpers ────────────────────────────────────────────────

        private int CountSpawnPoints()
        {
            return map.Waypoints.Count(wp => wp.Identifier >= 0 && wp.Identifier <= 7);
        }

        private List<(int playerIndex, Point2D pos)> GetSpawnPositions()
        {
            var result = new List<(int, Point2D)>();
            foreach (var wp in map.Waypoints)
            {
                if (wp.Identifier >= 0 && wp.Identifier <= 7 && wp.Position.X >= 0)
                {
                    result.Add((wp.Identifier, wp.Position));
                }
            }
            return result;
        }

        private bool HasOreNearPosition(Point2D pos, int searchRadius)
        {
            for (int dy = -searchRadius; dy <= searchRadius; dy++)
            {
                for (int dx = -searchRadius; dx <= searchRadius; dx++)
                {
                    if (Math.Abs(dx) + Math.Abs(dy) > searchRadius) continue;
                    var cell = map.GetTile(pos.X + dx, pos.Y + dy);
                    if (cell?.Overlay != null && cell.Overlay.OverlayType?.Tiberium == true)
                        return true;
                }
            }
            return false;
        }

        private List<(int xPct, int yPct)> GetStandardSpawnLayout(int playerCount)
        {
            return playerCount switch
            {
                2 => new List<(int, int)> { (20, 20), (80, 80) },
                4 => new List<(int, int)> { (15, 15), (85, 85), (85, 15), (15, 85) },
                6 => new List<(int, int)> { (15, 15), (85, 85), (85, 15), (15, 85), (50, 15), (50, 85) },
                8 => new List<(int, int)> { (15, 15), (85, 85), (85, 15), (15, 85), (50, 15), (50, 85), (15, 50), (85, 50) },
                _ => new List<(int, int)> { (20, 20), (80, 80) }
            };
        }

        private string[] GetVariationTerrains()
        {
            string theaterName = (map.LoadedTheaterName ?? map.TheaterName ?? "").ToUpperInvariant();
            return theaterName switch
            {
                "SNOW" => new[] { "rough_grass", "ice" },
                "DESERT" => new[] { "rough_grass", "pavement" },
                "URBAN" or "NEWURBAN" => new[] { "rough_grass", "sand" },
                _ => new[] { "dark_grass", "rough_grass", "sand" }
            };
        }
    }

    /// <summary>
    /// Represents a single fix action that the quality checker recommends.
    /// </summary>
    public class QualityFix
    {
        public string ToolName { get; }
        public string ArgumentsJson { get; }
        public string Description { get; }

        public QualityFix(string toolName, string argumentsJson, string description)
        {
            ToolName = toolName;
            ArgumentsJson = argumentsJson;
            Description = description;
        }
    }
}
