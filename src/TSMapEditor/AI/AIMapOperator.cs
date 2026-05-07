using System;
using System.Collections.Generic;
using System.Linq;
using Rampastring.Tools;
using TSMapEditor.AI.Operations;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.Mutations;
using TSMapEditor.Mutations.Classes;
using TSMapEditor.Rendering;
using TSMapEditor.UI;

namespace TSMapEditor.AI
{
    /// <summary>
    /// Translates structured MapOperation objects into WAE Mutation calls.
    /// This is the bridge between AI intent and actual map modifications.
    /// </summary>
    public class AIMapOperator
    {
        private readonly Map map;
        private readonly TheaterGraphics theaterGraphics;
        private readonly MutationManager mutationManager;
        private readonly IMutationTarget mutationTarget;

        public AIMapOperator(Map map, TheaterGraphics theaterGraphics,
            MutationManager mutationManager, IMutationTarget mutationTarget)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.theaterGraphics = theaterGraphics ?? throw new ArgumentNullException(nameof(theaterGraphics));
            this.mutationManager = mutationManager ?? throw new ArgumentNullException(nameof(mutationManager));
            this.mutationTarget = mutationTarget ?? throw new ArgumentNullException(nameof(mutationTarget));
        }

        /// <summary>
        /// Executes a list of map operations, returning a summary of what was done.
        /// </summary>
        public string ExecuteOperations(List<MapOperation> operations)
        {
            if (operations == null || operations.Count == 0)
                return string.Empty;

            var results = new List<string>();

            foreach (var op in operations)
            {
                try
                {
                    string result = ExecuteOperation(op);
                    if (!string.IsNullOrEmpty(result))
                        results.Add(result);
                }
                catch (Exception ex)
                {
                    string error = $"操作失败 ({op.Type}): {ex.Message}";
                    Logger.Log($"AIMapOperator error: {ex}");
                    results.Add(error);
                }
            }

            return string.Join("\n", results);
        }

        private string ExecuteOperation(MapOperation op)
        {
            switch (op.Type?.ToLowerInvariant())
            {
                case "fill_terrain":
                    return ExecuteFillTerrain(op);
                case "place_building":
                    return ExecutePlaceObject(op, AIPlaceObjectType.Building);
                case "place_unit":
                    return ExecutePlaceObject(op, AIPlaceObjectType.Vehicle);
                case "place_infantry":
                    return ExecutePlaceObject(op, AIPlaceObjectType.Infantry);
                case "place_overlay":
                    return ExecutePlaceOverlay(op);
                default:
                    return $"不支持的操作类型: {op.Type}";
            }
        }

        private string ExecuteFillTerrain(MapOperation op)
        {
            // Validate basic parameters
            if (op.Width <= 0 || op.Height <= 0)
                return $"无效的尺寸: {op.Width}x{op.Height}";

            // Use coordinates as-is — the AITerrainMutation uses Map.GetTile()
            // which safely returns null for cells outside the isometric diamond.
            // No need to clamp to map.Size (which is NOT the coord range for isometric maps).
            int startX = Math.Max(1, op.X);
            int startY = Math.Max(1, op.Y);
            int actualWidth = op.Width;
            int actualHeight = op.Height;

            // Sanity check: at least one cell in the area should be valid
            bool anyValid = false;
            for (int dy = 0; dy < actualHeight && !anyValid; dy++)
            {
                for (int dx = 0; dx < actualWidth && !anyValid; dx++)
                {
                    if (map.GetTile(startX + dx, startY + dy) != null)
                        anyValid = true;
                }
            }
            if (!anyValid)
                return $"坐标 ({startX},{startY}) {actualWidth}x{actualHeight} 完全超出地图有效区域";

            // Find the tileset by name
            int tileIndex = FindTileIndexByName(op.TileSetName);
            if (tileIndex < 0)
                return $"找不到地形类型: \"{op.TileSetName}\"";

            // Determine if we should flatten height (water needs to be at level 0)
            bool isWater = op.TileSetName.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0;

            // Create and execute the mutation
            var mutation = new AITerrainMutation(mutationTarget, startX, startY,
                actualWidth, actualHeight, tileIndex,
                op.Description ?? $"填充 {op.TileSetName} 在 ({startX},{startY}) {actualWidth}x{actualHeight}",
                flattenHeight: isWater, targetHeight: 0);

            mutationManager.PerformMutation(mutation);

            return $"✓ 已填充 {op.TileSetName} 在 ({startX},{startY}) 区域 {actualWidth}x{actualHeight}";
        }

        private string ExecutePlaceObject(MapOperation op, AIPlaceObjectType objectType)
        {
            if (string.IsNullOrWhiteSpace(op.ObjectName))
                return "缺少对象名称 (objectName)";

            // Resolve the ININame
            string resolvedININame = ResolveObjectININame(op.ObjectName, objectType);
            if (resolvedININame == null)
            {
                // Generate "did you mean?" suggestions
                var suggestions = FindSimilarNames(op.ObjectName, objectType, 5);
                string typeName = GetObjectTypeName(objectType);
                if (suggestions.Count > 0)
                    return $"找不到{typeName}: \"{op.ObjectName}\"。你是否要找: {string.Join(", ", suggestions)}";
                else
                    return $"找不到{typeName}: \"{op.ObjectName}\"";
            }

            // Resolve owner house
            House owner = ResolveOwner(op.Owner);
            if (owner == null)
                return $"找不到所属方: \"{op.Owner}\"。可用: {string.Join(", ", map.GetHouses().Select(h => h.ININame))}";

            int count = Math.Max(1, Math.Min(op.Count, 50)); // Cap at 50

            // Calculate placement positions
            var positions = CalculatePlacementPositions(op.X, op.Y, op.Width, op.Height, count);
            if (positions.Count == 0)
                return $"无法在 ({op.X},{op.Y}) 区域内找到可放置的位置";

            var mutation = new AIPlaceObjectMutation(mutationTarget, objectType,
                resolvedININame, owner, positions,
                op.Description ?? $"放置 {count}x {resolvedININame} 归属 {owner.ININame}");

            mutationManager.PerformMutation(mutation);

            return $"✓ 已放置 {positions.Count}x {resolvedININame} 归属 {owner.ININame}";
        }

        private string ExecutePlaceOverlay(MapOperation op)
        {
            if (string.IsNullOrWhiteSpace(op.ObjectName))
                return "缺少 overlay 名称 (objectName)";

            // Find overlay type
            var overlayType = FindOverlayType(op.ObjectName);
            if (overlayType == null)
                return $"找不到 overlay 类型: \"{op.ObjectName}\"";

            // Use coordinates as-is — the mutation uses Map.GetTile()
            // which safely handles the isometric diamond bounds.
            int width = Math.Max(1, op.Width);
            int height = Math.Max(1, op.Height);
            int startX = Math.Max(1, op.X);
            int startY = Math.Max(1, op.Y);

            var mutation = new AIPlaceOverlayMutation(mutationTarget, overlayType,
                startX, startY, width, height,
                op.Description ?? $"放置 {overlayType.ININame} 在 ({startX},{startY}) {width}x{height}");

            mutationManager.PerformMutation(mutation);

            return $"✓ 已放置 {overlayType.ININame} 在 ({startX},{startY}) 区域 {width}x{height}";
        }

        // --- Name resolution helpers ---

        /// <summary>
        /// Resolves an object name (ININame or display name) to its actual ININame.
        /// Tries exact match first, then case-insensitive, then partial display name match.
        /// </summary>
        private string ResolveObjectININame(string name, AIPlaceObjectType objectType)
        {
            switch (objectType)
            {
                case AIPlaceObjectType.Building:
                    return FindInList(map.Rules.BuildingTypes, name);
                case AIPlaceObjectType.Vehicle:
                    return FindInList(map.Rules.UnitTypes, name);
                case AIPlaceObjectType.Infantry:
                    return FindInList(map.Rules.InfantryTypes, name);
                default:
                    return null;
            }
        }

        private string FindInList<T>(List<T> types, string name) where T : TechnoType
        {
            // 1. Exact ININame match
            var exact = types.Find(t => t.ININame == name);
            if (exact != null)
                return exact.ININame;

            // 2. Case-insensitive ININame match
            var caseInsensitive = types.Find(t =>
                t.ININame.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (caseInsensitive != null)
                return caseInsensitive.ININame;

            // 3. Partial match on ININame (contains)
            var partialINI = types.Find(t =>
                t.ININame.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            if (partialINI != null)
                return partialINI.ININame;

            // 4. Display name match (Name property from INI)
            var displayMatch = types.Find(t =>
                t.GetEditorDisplayName().IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            if (displayMatch != null)
                return displayMatch.ININame;

            return null;
        }

        private OverlayType FindOverlayType(string name)
        {
            // Exact match
            var exact = map.Rules.OverlayTypes.Find(o => o.ININame == name);
            if (exact != null) return exact;

            // Case-insensitive
            var ci = map.Rules.OverlayTypes.Find(o =>
                o.ININame.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (ci != null) return ci;

            // Partial match
            var partial = map.Rules.OverlayTypes.Find(o =>
                o.ININame.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            if (partial != null) return partial;

            // Check if user asked for "tiberium" or "ore" generically
            if (name.IndexOf("ore", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("矿", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("tiberium", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Return first tiberium overlay type
                return map.Rules.OverlayTypes.Find(o => o.Tiberium);
            }

            return null;
        }

        private House ResolveOwner(string ownerName)
        {
            var houses = map.GetHouses();

            if (string.IsNullOrWhiteSpace(ownerName))
            {
                // Default to first house or Neutral
                return houses.Find(h => h.ININame == "Neutral") ?? (houses.Count > 0 ? houses[0] : null);
            }

            // Exact match
            var exact = houses.Find(h => h.ININame == ownerName);
            if (exact != null) return exact;

            // Case-insensitive
            var ci = houses.Find(h => h.ININame.Equals(ownerName, StringComparison.OrdinalIgnoreCase));
            if (ci != null) return ci;

            // Partial match
            var partial = houses.Find(h =>
                h.ININame.IndexOf(ownerName, StringComparison.OrdinalIgnoreCase) >= 0);
            if (partial != null) return partial;

            // Fallback: first house
            return houses.Count > 0 ? houses[0] : null;
        }

        /// <summary>
        /// Calculates a list of placement positions within an area.
        /// </summary>
        private List<Point2D> CalculatePlacementPositions(int x, int y, int width, int height, int count)
        {
            var positions = new List<Point2D>();

            if (count == 1)
            {
                // Single object: place at the specified coordinate directly.
                // Use Map.GetTile() to verify the cell exists in the isometric grid.
                var coord = new Point2D(x, y);
                if (map.GetTile(coord) != null)
                {
                    positions.Add(coord);
                }
                else
                {
                    // If exact cell is invalid, search nearby (spiral outward)
                    for (int radius = 1; radius <= 5 && positions.Count == 0; radius++)
                    {
                        for (int dy = -radius; dy <= radius; dy++)
                        {
                            for (int dx = -radius; dx <= radius; dx++)
                            {
                                if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                                    continue;
                                var candidate = new Point2D(x + dx, y + dy);
                                if (map.GetTile(candidate) != null)
                                {
                                    positions.Add(candidate);
                                    break;
                                }
                            }
                            if (positions.Count > 0) break;
                        }
                    }
                }
            }
            else
            {
                // Multiple objects: distribute within the area
                int areaW = Math.Max(1, width);
                int areaH = Math.Max(1, height);
                int totalCells = areaW * areaH;

                // If area is large enough, distribute evenly; otherwise fill sequentially
                int step = Math.Max(1, totalCells / count);
                int placed = 0;

                for (int i = 0; i < totalCells && placed < count; i += step)
                {
                    int cx = x + (i % areaW);
                    int cy = y + (i / areaW);

                    // Use GetTile to check the isometric diamond bounds
                    if (map.GetTile(cx, cy) != null)
                    {
                        positions.Add(new Point2D(cx, cy));
                        placed++;
                    }
                }

                // If step was too large and we didn't place enough, try filling sequentially
                if (placed < count)
                {
                    for (int i = 0; i < totalCells && placed < count; i++)
                    {
                        int cx = x + (i % areaW);
                        int cy = y + (i / areaW);
                        var pt = new Point2D(cx, cy);
                        if (map.GetTile(cx, cy) != null && !positions.Contains(pt))
                        {
                            positions.Add(pt);
                            placed++;
                        }
                    }
                }
            }

            return positions;
        }

        private static string GetObjectTypeName(AIPlaceObjectType type)
        {
            switch (type)
            {
                case AIPlaceObjectType.Building: return "建筑";
                case AIPlaceObjectType.Vehicle: return "载具";
                case AIPlaceObjectType.Infantry: return "步兵";
                default: return "对象";
            }
        }

        /// <summary>
        /// Finds type names similar to the given name, for "did you mean?" suggestions.
        /// Uses substring overlap scoring: types whose ININame or display name contain
        /// parts of the search term (or vice versa) are ranked higher.
        /// </summary>
        private List<string> FindSimilarNames(string name, AIPlaceObjectType objectType, int maxResults)
        {
            List<TechnoType> types;
            switch (objectType)
            {
                case AIPlaceObjectType.Building:
                    types = map.Rules.BuildingTypes.Cast<TechnoType>().ToList();
                    break;
                case AIPlaceObjectType.Vehicle:
                    types = map.Rules.UnitTypes.Cast<TechnoType>().ToList();
                    break;
                case AIPlaceObjectType.Infantry:
                    types = map.Rules.InfantryTypes.Cast<TechnoType>().ToList();
                    break;
                default:
                    return new List<string>();
            }

            string nameLower = name.ToLowerInvariant();

            // Score each type by similarity
            var scored = new List<(string iniName, string displayName, int score)>();
            foreach (var t in types)
            {
                if (!t.EditorVisible)
                    continue;

                string iniLower = t.ININame.ToLowerInvariant();
                string displayLower = t.GetEditorDisplayName().ToLowerInvariant();
                int score = 0;

                // Check substring containment in both directions
                if (iniLower.Contains(nameLower) || nameLower.Contains(iniLower))
                    score += 10;
                if (displayLower.Contains(nameLower) || nameLower.Contains(displayLower))
                    score += 8;

                // Check common prefix length
                int prefixLen = 0;
                int minLen = Math.Min(iniLower.Length, nameLower.Length);
                for (int i = 0; i < minLen && iniLower[i] == nameLower[i]; i++)
                    prefixLen++;
                score += prefixLen;

                if (score > 0)
                {
                    string label = t.GetEditorDisplayName() != t.ININame
                        ? $"{t.ININame} ({t.GetEditorDisplayName()})"
                        : t.ININame;
                    scored.Add((t.ININame, label, score));
                }
            }

            return scored
                .OrderByDescending(s => s.score)
                .Take(maxResults)
                .Select(s => s.displayName)
                .ToList();
        }

        /// <summary>
        /// Finds the start tile index for a tileset by name (case-insensitive).
        /// </summary>
        private int FindTileIndexByName(string tileSetName)
        {
            if (string.IsNullOrWhiteSpace(tileSetName))
                return -1;

            var tileSets = theaterGraphics.Theater.TileSets;

            for (int i = 0; i < tileSets.Count; i++)
            {
                if (string.Equals(tileSets[i].SetName, tileSetName, StringComparison.OrdinalIgnoreCase))
                {
                    return tileSets[i].StartTileIndex;
                }
            }

            // Try partial match as fallback
            for (int i = 0; i < tileSets.Count; i++)
            {
                if (tileSets[i].SetName != null &&
                    tileSets[i].SetName.IndexOf(tileSetName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return tileSets[i].StartTileIndex;
                }
            }

            return -1;
        }
    }
}

