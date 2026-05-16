using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    /// Executes tool calls from the AI agent.
    /// Each tool call is mapped to a map mutation via PositionResolver.
    /// The AI never sees raw coordinates — it only knows semantic positions and percentages.
    /// </summary>
    public class ToolExecutor
    {
        private readonly Map map;
        private readonly TheaterGraphics theaterGraphics;
        private readonly MutationManager mutationManager;
        private readonly IMutationTarget mutationTarget;
        private readonly PositionResolver positionResolver;

        // Unit reference: code -> description, loaded from References/unit_reference.json
        private readonly Dictionary<string, string> unitReference = new(StringComparer.OrdinalIgnoreCase);

        public ToolExecutor(Map map, TheaterGraphics theaterGraphics,
            MutationManager mutationManager, IMutationTarget mutationTarget)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.theaterGraphics = theaterGraphics ?? throw new ArgumentNullException(nameof(theaterGraphics));
            this.mutationManager = mutationManager ?? throw new ArgumentNullException(nameof(mutationManager));
            this.mutationTarget = mutationTarget ?? throw new ArgumentNullException(nameof(mutationTarget));
            this.positionResolver = new PositionResolver(map);
            LoadUnitReference();
        }

        /// <summary>
        /// Executes a tool call and returns a result string for the AI to observe.
        /// </summary>
        public string Execute(string toolName, string argumentsJson)
        {
            try
            {
                Logger.Log($"ToolExecutor: {toolName}({argumentsJson})");
                using var doc = JsonDocument.Parse(argumentsJson);
                var args = doc.RootElement;

                string result = toolName switch
                {
                    "get_map_info" => ExecuteGetMapInfo(),
                    "fill_terrain" => ExecuteFillTerrain(args),
                    "create_plateau" => ExecuteCreatePlateau(args),
                    "draw_road" => ExecuteDrawRoad(args),
                    "draw_river" => ExecuteDrawRiver(args),
                    "place_building" => ExecutePlaceBuilding(args),
                    "place_buildings" => ExecutePlaceBatch(args, AIPlaceObjectType.Building),
                    "place_unit" => ExecutePlaceUnit(args),
                    "place_units" => ExecutePlaceBatch(args, AIPlaceObjectType.Vehicle),
                    "set_spawn_point" => ExecuteSetSpawnPoint(args),
                    "place_ore" => ExecutePlaceOre(args),
                    "place_trees" => ExecutePlaceTrees(args),
                    "place_decorations" => ExecutePlaceDecorations(args),
                    "clear_area" => ExecuteClearArea(args),
                    "set_map_name" => ExecuteSetMapName(args),
                    "search_units" => ExecuteSearchUnits(args),
                    "place_tile" => ExecutePlaceTile(args),
                    _ => $"❌ 未知工具: {toolName}"
                };

                // Append map state summary to all mutation results so the AI
                // always sees the current global state after each operation.
                // Skip for query-only tools and errors.
                bool isQueryOnly = toolName == "get_map_info" || toolName == "search_units";
                if (!isQueryOnly && !result.StartsWith("❌"))
                {
                    result += "\n" + GetMapStateSummary();
                }

                return result;
            }
            catch (JsonException ex)
            {
                Logger.Log($"ToolExecutor JSON parse error: {ex.Message}, args={argumentsJson}");
                return $"❌ 参数解析错误: {ex.Message}";
            }
            catch (Exception ex)
            {
                Logger.Log($"ToolExecutor error in {toolName}: {ex}");
                return $"❌ 工具执行失败 ({toolName}): {ex.Message}";
            }
        }

        // ─── Position Helpers ───────────────────────────────────────

        private Point2D ResolvePosition(JsonElement args)
        {
            string semantic = args.TryGetString("position");
            int? xPct = args.TryGetInt("x_pct");
            int? yPct = args.TryGetInt("y_pct");
            return positionResolver.Resolve(semantic, xPct, yPct);
        }

        private Point2D ResolveEndpoint(JsonElement args, string posKey, string xKey, string yKey)
        {
            string semantic = args.TryGetString(posKey);
            int? xPct = args.TryGetInt(xKey);
            int? yPct = args.TryGetInt(yKey);
            return positionResolver.Resolve(semantic, xPct, yPct);
        }

        // ─── Tool Implementations ───────────────────────────────────

        private string ExecuteGetMapInfo()
        {
            int spawnCount = map.Waypoints.Count(wp => wp.Identifier >= 0 && wp.Identifier <= 7);
            var houses = map.GetHouses();
            string theaterName = map.LoadedTheaterName ?? map.TheaterName ?? "UNKNOWN";

            // Collect available LAT ground types (usable for fill_terrain)
            var latGrounds = theaterGraphics.Theater.LATGrounds;
            var latNames = latGrounds.Select(g => g.GroundTileSet?.SetName).Where(n => n != null).ToList();

            // Collect available terrain object types (trees, rocks, etc.) - first 20
            var terrainTypes = map.Rules.TerrainTypes.Take(20)
                .Select(t => t.ININame).ToList();

            // Collect placeable tile sets (cliffs, shores, roads, etc.)
            var tileSets = theaterGraphics.Theater.TileSets;
            var placeableSets = new List<string>();
            foreach (var ts in tileSets)
            {
                if (ts.AllowToPlace && ts.LoadedTileCount > 0 && !string.IsNullOrWhiteSpace(ts.SetName))
                {
                    // Skip LAT grounds (already listed above) and clear/base tiles
                    bool isLat = latNames.Any(n => string.Equals(n, ts.SetName, StringComparison.OrdinalIgnoreCase));
                    if (isLat || ts.SetName == "Clear")
                        continue;

                    // Skip tile sets that need game logic (height changes, adjacent water, etc.)
                    // These should use create_plateau or draw_river instead of place_tile
                    string nameLower = ts.SetName.ToLowerInvariant();
                    if (nameLower.Contains("cliff") || nameLower.Contains("ramp") ||
                        nameLower.Contains("shore") || nameLower.Contains("bridge"))
                        continue;

                    placeableSets.Add($"{ts.SetName}({ts.LoadedTileCount})");
                }
            }

            return $"地图尺寸: {map.Size.X}x{map.Size.Y}\n" +
                   $"场景(Theater): {theaterName}\n" +
                   $"等距中心: ({positionResolver.Center},{positionResolver.Center})\n" +
                   $"菱形半径: {positionResolver.DiamondRadius}\n" +
                   $"当前出生点数量: {spawnCount}\n" +
                   $"可用所属方: {string.Join(", ", houses.Select(h => h.ININame))}\n" +
                   $"可用地面类型: {string.Join(", ", latNames)}\n" +
                   $"可用地块集(地形装饰): {string.Join(", ", placeableSets.Take(25))}\n" +
                   $"可用地物类型(示例): {string.Join(", ", terrainTypes)}\n" +
                   $"地图名称: {map.Basic.Name ?? "(未设置)"}";
        }

        private string ExecuteFillTerrain(JsonElement args)
        {
            string terrain = args.GetProperty("terrain").GetString();
            string scope = args.GetProperty("scope").GetString();

            // Map shorthand names to LAT tileset names (fill_terrain only handles LAT ground types)
            // For non-LAT tile sets (cliffs, shores, etc.), use the place_tile tool instead
            string tileSetName = terrain switch
            {
                "grass" => "Lat Grass",
                "dark_grass" => "LAT Dark Grass",
                "rough_grass" => "LAT Rough Grass",
                "sand" => "LAT Sand",
                "pavement" => "LAT Pavement",
                "snow" => "LAT Snow",
                "ice" => "Ice",
                _ => null  // Unknown terrain type
            };

            if (tileSetName == null)
            {
                // Check if user is trying to use a non-LAT tileset name
                int checkIdx = FindTileIndexByName(terrain);
                if (checkIdx >= 0)
                    return $"X fill_terrain 只支持 LAT 地面类型。要放置 {terrain}，请改用 place_tile 工具。";
                return $"X 未知地形类型: {terrain}。可用类型: grass, dark_grass, rough_grass, sand, pavement, snow, ice";
            }

            int tileIndex = FindTileIndexByName(tileSetName);
            if (tileIndex < 0)
                return $"X 找不到地形类型: {tileSetName}";

            // Determine area based on scope
            int center = positionResolver.Center;
            int radius = positionResolver.DiamondRadius;
            int startX, startY, width, height;

            if (scope == "full_map")
            {
                // Fill the entire map area
                startX = 1;
                startY = 1;
                int maxCoord = map.Size.X + map.Size.Y - 1;
                width = maxCoord;
                height = maxCoord;
            }
            else
            {
                var pos = ResolvePosition(args);
                int patchRadius = scope switch
                {
                    "small_patch" => 5,
                    "medium_patch" => 10,
                    "large_patch" => 18,
                    _ => 10
                };
                startX = pos.X - patchRadius;
                startY = pos.Y - patchRadius;
                width = patchRadius * 2;
                height = patchRadius * 2;
            }

            bool isWater = tileSetName.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0;

            var mutation = new AITerrainMutation(mutationTarget, startX, startY,
                width, height, tileIndex,
                $"填充 {tileSetName}",
                flattenHeight: isWater, targetHeight: 0);

            mutationManager.PerformMutation(mutation);

            return $"✓ 已填充 {tileSetName}，位置 ({startX},{startY})，区域 {width}x{height}";
        }

        private string ExecuteCreatePlateau(JsonElement args)
        {
            var pos = ResolvePosition(args);
            string size = args.GetProperty("size").GetString();
            int height = args.TryGetInt("height") ?? 2;

            int radius = size switch
            {
                "small" => 8,
                "medium" => 12,
                "large" => 18,
                _ => 12
            };

            height = Math.Max(1, Math.Min(4, height));

            Logger.Log($"ToolExecutor create_plateau: pos=({pos.X},{pos.Y}) radius={radius} height={height}");

            var mutation = new AICreatePlateauMutation(mutationTarget, pos.X, pos.Y, radius, height);
            mutationManager.PerformMutation(mutation);

            return $"✓ 已在 {GetPositionDescription(args)} 创建{size}高地，半径{radius}，高度{height}";
        }

        private string ExecuteDrawRoad(JsonElement args)
        {
            var from = ResolveEndpoint(args, "from_position", "from_x_pct", "from_y_pct");
            var to = ResolveEndpoint(args, "to_position", "to_x_pct", "to_y_pct");
            int width = args.TryGetInt("width") ?? 4;
            width = Math.Max(3, Math.Min(6, width));

            // Always use LAT Pavement for roads
            int tileIndex = FindTileIndexByName("LAT Pavement");
            if (tileIndex < 0)
                tileIndex = FindTileIndexByName("Pavement");
            if (tileIndex < 0)
                return "❌ 找不到 LAT Pavement 地形类型";

            Logger.Log($"ToolExecutor draw_road: from=({from.X},{from.Y}) to=({to.X},{to.Y}) width={width}");

            var mutation = new AIDrawPathMutation(mutationTarget, from.X, from.Y, to.X, to.Y,
                width, tileIndex, $"绘制公路", flattenHeight: false, targetHeight: 0);

            mutationManager.PerformMutation(mutation);

            return $"✓ 已绘制公路从 {GetEndpointDescription(args, "from")} 到 {GetEndpointDescription(args, "to")}，宽度{width}";
        }

        private string ExecuteDrawRiver(JsonElement args)
        {
            var from = ResolveEndpoint(args, "from_position", "from_x_pct", "from_y_pct");
            var to = ResolveEndpoint(args, "to_position", "to_x_pct", "to_y_pct");
            int width = args.TryGetInt("width") ?? 10;
            width = Math.Max(8, Math.Min(15, width));

            int tileIndex = FindTileIndexByName("Water");
            if (tileIndex < 0)
                return "❌ 找不到 Water 地形类型";

            Logger.Log($"ToolExecutor draw_river: from=({from.X},{from.Y}) to=({to.X},{to.Y}) width={width}");

            var mutation = new AIDrawPathMutation(mutationTarget, from.X, from.Y, to.X, to.Y,
                width, tileIndex, $"绘制河流", flattenHeight: true, targetHeight: 0);

            mutationManager.PerformMutation(mutation);

            return $"✓ 已绘制河流从 {GetEndpointDescription(args, "from")} 到 {GetEndpointDescription(args, "to")}，宽度{width}";
        }

        private string ExecutePlaceBuilding(JsonElement args)
        {
            return ExecutePlaceObject(args, AIPlaceObjectType.Building);
        }

        private string ExecutePlaceUnit(JsonElement args)
        {
            return ExecutePlaceObject(args, AIPlaceObjectType.Vehicle);
        }

        private string ExecutePlaceObject(JsonElement args, AIPlaceObjectType objectType)
        {
            var pos = ResolvePosition(args);
            string name = args.GetProperty("name").GetString();
            string ownerName = args.TryGetString("owner") ?? "Neutral";

            // Resolve INI name
            string resolvedName = ResolveObjectININame(name, objectType);
            if (resolvedName == null)
            {
                var suggestions = FindSimilarNames(name, objectType, 5);
                string typeName = objectType == AIPlaceObjectType.Building ? "建筑" : "载具";
                if (suggestions.Count > 0)
                    return $"❌ 找不到{typeName}: \"{name}\"。你是否要找: {string.Join(", ", suggestions)}";
                return $"❌ 找不到{typeName}: \"{name}\"";
            }

            House owner = ResolveOwner(ownerName);
            if (owner == null)
                return $"❌ 找不到所属方: \"{ownerName}\"";

            var positions = new List<Point2D> { pos };
            if (map.GetTile(pos) == null)
            {
                // Search nearby for valid cell
                positions.Clear();
                for (int r = 1; r <= 5 && positions.Count == 0; r++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        for (int dx = -r; dx <= r; dx++)
                        {
                            var candidate = new Point2D(pos.X + dx, pos.Y + dy);
                            if (map.GetTile(candidate) != null) { positions.Add(candidate); goto found; }
                        }
                    }
                }
                found:;
            }

            if (positions.Count == 0)
                return $"❌ 位置 ({pos.X},{pos.Y}) 不在地图有效区域内";

            var mutation = new AIPlaceObjectMutation(mutationTarget, objectType,
                resolvedName, owner, positions, $"放置 {resolvedName}");
            mutationManager.PerformMutation(mutation);

            return $"✓ 已在 {GetPositionDescription(args)} 放置 {resolvedName}（{owner.ININame}）";
        }

        /// <summary>
        /// Batch placement: handles place_buildings and place_units.
        /// Accepts {"items": [{name, x_pct, y_pct, owner?}, ...]}
        /// </summary>
        private string ExecutePlaceBatch(JsonElement args, AIPlaceObjectType objectType)
        {
            if (!args.TryGetProperty("items", out var itemsArray) || itemsArray.ValueKind != JsonValueKind.Array)
                return "❌ 缺少 items 数组";

            int successCount = 0;
            int failCount = 0;
            var errors = new List<string>();
            var spawnWarnings = new List<string>();
            var spawnZones = GetSpawnExclusionZones();

            foreach (var item in itemsArray.EnumerateArray())
            {
                try
                {
                    string name = item.GetProperty("name").GetString();
                    int xPct = item.TryGetProperty("x_pct", out var xp) ? ParseJsonInt(xp, 50) : 50;
                    int yPct = item.TryGetProperty("y_pct", out var yp) ? ParseJsonInt(yp, 50) : 50;
                    string ownerName = item.TryGetProperty("owner", out var ow) ? ow.GetString() ?? "Neutral" : "Neutral";

                    var pos = positionResolver.Resolve(null, xPct, yPct);

                    string resolvedName = ResolveObjectININame(name, objectType);
                    if (resolvedName == null)
                    {
                        errors.Add($"{name}(未找到)");
                        failCount++;
                        continue;
                    }

                    House owner = ResolveOwner(ownerName);
                    if (owner == null)
                    {
                        errors.Add($"{name}(所属方'{ownerName}'无效)");
                        failCount++;
                        continue;
                    }

                    // Find valid position
                    if (map.GetTile(pos) == null)
                    {
                        for (int r = 1; r <= 5; r++)
                        {
                            bool found = false;
                            for (int dy = -r; dy <= r && !found; dy++)
                                for (int dx = -r; dx <= r && !found; dx++)
                                {
                                    var candidate = new Point2D(pos.X + dx, pos.Y + dy);
                                    if (map.GetTile(candidate) != null) { pos = candidate; found = true; }
                                }
                            if (found) break;
                        }
                    }

                    var positions = new List<Point2D> { pos };

                    // Check spawn proximity (warn but don't block)
                    foreach (var zone in spawnZones)
                    {
                        int sdx = pos.X - zone.Center.X;
                        int sdy = pos.Y - zone.Center.Y;
                        if (sdx * sdx + sdy * sdy <= zone.Radius * zone.Radius)
                        {
                            spawnWarnings.Add(resolvedName);
                            break;
                        }
                    }

                    var mutation = new AIPlaceObjectMutation(mutationTarget, objectType,
                        resolvedName, owner, positions, $"批量放置 {resolvedName}");
                    mutationManager.PerformMutation(mutation);
                    successCount++;
                }
                catch (Exception ex)
                {
                    errors.Add($"异常: {ex.Message}");
                    failCount++;
                }
            }

            string typeName = objectType == AIPlaceObjectType.Building ? "建筑" : "载具";
            string result = $"✓ 批量放置{typeName}: {successCount}个成功";
            if (failCount > 0)
                result += $", {failCount}个失败({string.Join(", ", errors)})";

            // Add spawn proximity warnings
            if (spawnWarnings.Count > 0)
                result += $"\n⚠️ 注意: {string.Join(", ", spawnWarnings.Distinct())} 距离出生点过近(<8格)，可能影响玩家基地展开";

            return result;
        }

        private string ExecuteSetSpawnPoint(JsonElement args)
        {
            var pos = ResolvePosition(args);
            int playerIndex = args.GetProperty("player_index").GetInt32();
            playerIndex = Math.Max(0, Math.Min(7, playerIndex));

            if (map.GetTile(pos) == null)
                return $"❌ 位置 ({pos.X},{pos.Y}) 不在地图有效区域内";

            // Set the waypoint
            var mutation = new AISetWaypointMutation(mutationTarget, playerIndex, pos,
                $"设置玩家{playerIndex + 1}出生点");
            mutationManager.PerformMutation(mutation);

            // Auto-clear a 5-cell radius around spawn point so the base can deploy
            int clearRadius = 5;
            var clearMutation = new AIClearAreaMutation(mutationTarget,
                pos.X - clearRadius, pos.Y - clearRadius, clearRadius * 2, clearRadius * 2,
                $"清除出生点{playerIndex + 1}周围障碍物");
            mutationManager.PerformMutation(clearMutation);

            return $"✓ 已设置玩家{playerIndex + 1}出生点在 {GetPositionDescription(args)}（已自动清除周围障碍物）";
        }

        private string ExecutePlaceOre(JsonElement args)
        {
            var pos = ResolvePosition(args);
            string amount = args.GetProperty("amount").GetString();
            string type = args.TryGetString("type") ?? "ore";

            // Find overlay type
            OverlayType overlayType;
            if (type == "gems")
            {
                overlayType = map.Rules.OverlayTypes.Find(o =>
                    o.ININame.IndexOf("GEM", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            else
            {
                overlayType = map.Rules.OverlayTypes.Find(o => o.Tiberium);
            }

            if (overlayType == null)
                return "❌ 找不到矿石类型";

            int radius = amount switch
            {
                "small" => 4,
                "medium" => 7,
                "large" => 10,
                _ => 7
            };

            var mutation = new AIPlaceOverlayMutation(mutationTarget, overlayType,
                pos.X - radius, pos.Y - radius, radius * 2, radius * 2,
                $"放置{type}矿");
            mutationManager.PerformMutation(mutation);

            // Auto-place an Ore Mine drill (TIBTRE01) at the center of the ore field
            // TIBTRE01 is a terrain object that continuously regenerates ore for players to harvest
            // Used 6258 times across 720 official MO maps
            string minePlaced = "";
            var oreMineType = map.Rules.TerrainTypes.Find(t =>
                t.ININame.Equals("TIBTRE01", StringComparison.OrdinalIgnoreCase)) ??
                map.Rules.TerrainTypes.Find(t =>
                t.ININame.Equals("TIBTRE02", StringComparison.OrdinalIgnoreCase));
            if (oreMineType != null)
            {
                var cell = map.GetTile(pos.X, pos.Y);
                if (cell != null && cell.TerrainObject == null)
                {
                    var mineMutation = new AIPlaceTerrainObjectMutation(mutationTarget,
                        new List<TerrainType> { oreMineType },
                        pos.X, pos.Y, 0, 1.0f, "放置矿井");
                    mutationManager.PerformMutation(mineMutation);
                    minePlaced = " + 生矿机";
                }
            }

            return $"✓ 已在 {GetPositionDescription(args)} 放置{amount}{(type == "gems" ? "宝石" : "矿石")}{minePlaced}";
        }

        private string ExecutePlaceTrees(JsonElement args)
        {
            var pos = ResolvePosition(args);
            string density = args.TryGetString("density") ?? "medium";

            // Get curated tree types for current theater (visually coherent, 3-5 types)
            var treeTypes = GetTheaterTreeTypes();
            if (treeTypes.Count == 0)
                return "❌ 当前场景没有可用的树木类型";

            int radius = 8;
            float densityValue = density switch
            {
                "sparse" => 0.12f,
                "medium" => 0.25f,
                "dense" => 0.45f,
                _ => 0.25f
            };

            var exclusionZones = GetSpawnExclusionZones();
            var mutation = new AIPlaceTerrainObjectMutation(mutationTarget, treeTypes,
                pos.X, pos.Y, radius, densityValue, $"放置树木", exclusionZones);
            mutationManager.PerformMutation(mutation);

            return $"✓ 已在 {GetPositionDescription(args)} 放置{density}密度的树木（{treeTypes.Count}种混搭）";
        }

        /// <summary>
        /// Returns a curated list of tree types appropriate for the current theater.
        /// Each theater uses 3-5 visually coherent tree types instead of all available trees.
        /// Fallback: if none of the curated names exist, pick the first few TREE types.
        /// </summary>
        private List<TerrainType> GetTheaterTreeTypes()
        {
            string theaterName = (map.LoadedTheaterName ?? map.TheaterName ?? "").ToUpperInvariant();

            // Curated tree lists per theater — derived from analyzing 720 official Mental Omega maps.
            // Each list contains the Top 5 most frequently used tree types for that theater.
            string[] preferredTrees = theaterName switch
            {
                // SNOW (162 maps): TREE25(9561), TREE26(9123), TREE27(8884), TREE24(7636), TREE23(7609)
                "SNOW" => new[] { "TREE25", "TREE26", "TREE27", "TREE24", "TREE23" },
                // URBAN (110 maps): TREE09(3604), TREE01(3330), TREE05(2193), TREE06(2190), TREE03(2099)
                "URBAN" => new[] { "TREE09", "TREE01", "TREE05", "TREE06", "TREE03" },
                // NEWURBAN (85 maps): TREE05(3374), TREE08(3156), TREE12(3069), TREE04(3062), TREE07(3055)
                "NEWURBAN" => new[] { "TREE05", "TREE08", "TREE12", "TREE04", "TREE07" },
                // DESERT (153 maps): TREE21(5967), TREE22(5554), TREE23(5328), TREE20(5144), TREE24(5064)
                "DESERT" => new[] { "TREE21", "TREE22", "TREE23", "TREE20", "TREE24" },
                // LUNAR (2 maps): no vegetation
                "LUNAR" => Array.Empty<string>(),
                // TEMPERATE (208 maps): TREE22(10370), TREE21(9279), TREE23(8471), TREE08(7797), TREE20(7582)
                _ => new[] { "TREE22", "TREE21", "TREE23", "TREE08", "TREE20" }
            };

            var result = new List<TerrainType>();
            foreach (string name in preferredTrees)
            {
                var tt = map.Rules.TerrainTypes.Find(t =>
                    t.ININame.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (tt != null)
                    result.Add(tt);
            }

            // Fallback: if curated list yields nothing, use first 5 TREE types found
            if (result.Count == 0)
            {
                result = map.Rules.TerrainTypes
                    .FindAll(t => t.ININame.StartsWith("TREE", StringComparison.OrdinalIgnoreCase))
                    .GetRange(0, Math.Min(5, map.Rules.TerrainTypes
                        .FindAll(t => t.ININame.StartsWith("TREE", StringComparison.OrdinalIgnoreCase)).Count));
            }

            return result;
        }

        private string ExecuteClearArea(JsonElement args)
        {
            var pos = ResolvePosition(args);
            int radius = args.GetProperty("radius").GetInt32();
            radius = Math.Max(3, Math.Min(30, radius));

            var mutation = new AIClearAreaMutation(mutationTarget,
                pos.X - radius, pos.Y - radius, radius * 2, radius * 2,
                $"清除区域");
            mutationManager.PerformMutation(mutation);

            return $"✓ 已清除 {GetPositionDescription(args)} 半径{radius}的区域";
        }

        private string ExecutePlaceDecorations(JsonElement args)
        {
            var pos = ResolvePosition(args);
            string density = args.TryGetString("density") ?? "medium";
            int radius = args.TryGetInt("radius") ?? 8;
            radius = Math.Max(3, Math.Min(20, radius));

            // Get theater-appropriate decoration buildings
            var decoNames = GetTheaterDecorations();
            if (decoNames.Length == 0)
                return "❌ 当前场景没有可用的装饰物";

            float densityValue = density switch
            {
                "sparse" => 0.02f,
                "medium" => 0.04f,
                "dense" => 0.08f,
                _ => 0.04f
            };

            // Build spawn point exclusion zones
            const int spawnExclusionRadius = 12;
            var spawnPoints = new List<Point2D>();
            foreach (var wp in map.Waypoints)
            {
                if (wp.Identifier >= 0 && wp.Identifier <= 7)
                    spawnPoints.Add(wp.Position);
            }

            // Resolve building types
            var buildingTypes = new List<BuildingType>();
            foreach (var name in decoNames)
            {
                var bt = map.Rules.BuildingTypes.Find(b =>
                    b.ININame.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (bt != null)
                    buildingTypes.Add(bt);
            }

            if (buildingTypes.Count == 0)
                return "❌ 找不到装饰物建筑类型";

            House neutralOwner = ResolveOwner("Neutral");
            if (neutralOwner == null)
                return "❌ 找不到 Neutral 所属方";

            var mutation = new AIPlaceDecorationsMutation(mutationTarget,
                buildingTypes, neutralOwner, pos.X, pos.Y, radius, densityValue,
                spawnPoints, spawnExclusionRadius, $"放置装饰物");
            mutationManager.PerformMutation(mutation);

            return $"✓ 已在 {GetPositionDescription(args)} 放置{density}密度的装饰物";
        }

        /// <summary>
        /// Returns theater-appropriate PURE DECORATION building INI names.
        /// Data derived from analyzing 720 official Mental Omega standard maps.
        /// 
        /// EXCLUDED (functional buildings that must NOT be scattered):
        ///   CAOILD = Oil Derrick (capturable, generates money)
        ///   CABHUT = Bridge Repair Hut (must be near bridges)
        ///   CAHOSP = Hospital (capturable tech building)
        ///   CAAIRP = Airport (capturable tech building)
        ///   CAPOWR = Power Plant (capturable tech building)
        ///   
        /// INCLUDED (safe pure-decoration objects):
        ///   CABARR01/02 = Barrels/crates
        ///   CAWALL = Fence segments
        ///   NEGLAMP/INGALITE/INYELWLAMP/SNODUSLAMP/TEMDUSLAMP = Street lamps
        ///   CAMISC03/04/05 = Misc decorative objects (poles, signs)
        ///   CAMSC06/07/08/09 = Small misc objects
        ///   CAFARM02 = Farm buildings
        /// </summary>
        private string[] GetTheaterDecorations()
        {
            string theaterName = (map.LoadedTheaterName ?? map.TheaterName ?? "").ToUpperInvariant();

            return theaterName switch
            {
                "SNOW" => new[] { "CABARR01", "CABARR02", "CAWALL", "SNODUSLAMP" },
                "URBAN" => new[] { "CABARR01", "CAWALL", "NEGLAMP", "CAPARK01" },
                "NEWURBAN" => new[] { "CABARR01", "CAWALL", "INYELWLAMP" },
                "DESERT" => new[] { "CABARR01", "CABARR02", "CAWALL", "INGALITE" },
                "LUNAR" => new[] { "CABARR01" },
                // TEMPERATE: barrels, fences, lamps
                _ => new[] { "CABARR01", "CABARR02", "CAWALL", "NEGLAMP" }
            };
        }

        private string ExecuteSetMapName(JsonElement args)
        {
            string name = args.GetProperty("name").GetString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                map.Basic.Name = name;
                Logger.Log($"Map name set to: {name}");
                return $"✓ 地图名称已设置为: {name}";
            }
            return "❌ 地图名称不能为空";
        }

        private string ExecuteSearchUnits(JsonElement args)
        {
            string keyword = args.GetProperty("keyword").GetString()?.Trim();
            if (string.IsNullOrEmpty(keyword))
                return "❌ 请提供搜索关键词";

            string category = args.TryGetString("category");

            var results = new List<string>();
            foreach (var kvp in unitReference)
            {
                bool match = kvp.Key.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                             kvp.Value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
                if (match)
                    results.Add($"  {kvp.Key} = {kvp.Value}");
            }

            if (results.Count == 0)
                return $"未找到匹配 \"{keyword}\" 的单位";

            // Limit to 20 results to avoid overwhelming
            if (results.Count > 20)
                return $"找到 {results.Count} 个匹配项（显示前20个）:\n{string.Join("\n", results.Take(20))}";

            return $"找到 {results.Count} 个匹配项:\n{string.Join("\n", results)}";
        }

        /// <summary>
        /// Loads the unit reference JSON file, flattening all categories into a single
        /// code -> description lookup dictionary.
        /// </summary>
        private void LoadUnitReference()
        {
            try
            {
                // Try multiple possible locations for the reference file
                string[] searchPaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AI", "References", "unit_reference.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "AI", "References", "unit_reference.json"),
                };

                string refPath = null;
                foreach (var p in searchPaths)
                {
                    if (File.Exists(p)) { refPath = p; break; }
                }

                if (refPath == null)
                {
                    Logger.Log("ToolExecutor: unit_reference.json not found, search_units will be limited");
                    return;
                }

                string json = File.ReadAllText(refPath);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("categories", out var categories))
                {
                    foreach (var cat in categories.EnumerateObject())
                    {
                        if (cat.Value.TryGetProperty("items", out var items))
                        {
                            foreach (var item in items.EnumerateObject())
                            {
                                unitReference[item.Name] = item.Value.GetString() ?? item.Name;
                            }
                        }
                    }
                }

                Logger.Log($"ToolExecutor: Loaded {unitReference.Count} unit references from JSON");
            }
            catch (Exception ex)
            {
                Logger.Log($"ToolExecutor: Failed to load unit_reference.json: {ex.Message}");
            }

            // Enrich with actual game data — these have real Name= values
            // from the game's rulesmd.ini, which are authoritative
            try
            {
                int enriched = 0;
                foreach (var bt in map.Rules.BuildingTypes)
                {
                    if (!unitReference.ContainsKey(bt.ININame) && !string.IsNullOrEmpty(bt.Name))
                    {
                        unitReference[bt.ININame] = bt.Name;
                        enriched++;
                    }
                }
                foreach (var vt in map.Rules.UnitTypes)
                {
                    if (!unitReference.ContainsKey(vt.ININame) && !string.IsNullOrEmpty(vt.Name))
                    {
                        unitReference[vt.ININame] = vt.Name;
                        enriched++;
                    }
                }
                foreach (var it in map.Rules.InfantryTypes)
                {
                    if (!unitReference.ContainsKey(it.ININame) && !string.IsNullOrEmpty(it.Name))
                    {
                        unitReference[it.ININame] = it.Name;
                        enriched++;
                    }
                }
                Logger.Log($"ToolExecutor: Enriched with {enriched} game data entries, total {unitReference.Count}");
            }
            catch (Exception ex)
            {
                Logger.Log($"ToolExecutor: Failed to enrich from game data: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates a categorized codebook string from the actually-loaded game rules.
        /// This is included in the system prompt so the AI knows valid INI names.
        /// </summary>
        public string GetDynamicCodebook()
        {
            var sb = new System.Text.StringBuilder();

            // --- Neutral/Capturable buildings (CA prefix, tech buildings) ---
            sb.AppendLine("=== 中立/可占领建筑 ===");
            int neutralCount = 0;
            foreach (var bt in map.Rules.BuildingTypes)
            {
                if (bt.ININame.StartsWith("CA", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(bt.Name))
                {
                    // Only include functional/notable ones, skip too many decorations
                    string lower = bt.ININame.ToLowerInvariant();
                    if (lower.Contains("oild") || lower.Contains("hosp") || lower.Contains("lab") ||
                        lower.Contains("mach") || lower.Contains("powr") || lower.Contains("airp") ||
                        lower.Contains("sam") || lower.Contains("hmg") || lower.Contains("fort") ||
                        lower.Contains("hse") || lower.Contains("eur") || lower.Contains("farm") ||
                        lower.Contains("gas") || lower.Contains("park") || lower.Contains("army"))
                    {
                        sb.AppendLine($"  {bt.ININame} = {bt.Name}");
                        neutralCount++;
                        if (neutralCount >= 30) break;
                    }
                }
            }

            // --- Civilian vehicles ---
            sb.AppendLine("\n=== 中立载具（装饰用）===");
            int civVehCount = 0;
            foreach (var vt in map.Rules.UnitTypes)
            {
                string ini = vt.ININame.ToUpperInvariant();
                if ((ini.StartsWith("CIV") || ini.StartsWith("TRUCK") || ini.StartsWith("SUV") ||
                     ini.StartsWith("BUS") || ini.StartsWith("COP") || ini.StartsWith("TAXI") ||
                     ini.StartsWith("AMBU") || ini.StartsWith("LIMO") || ini.StartsWith("PICK")) &&
                    !string.IsNullOrEmpty(vt.Name))
                {
                    sb.AppendLine($"  {vt.ININame} = {vt.Name}");
                    civVehCount++;
                    if (civVehCount >= 15) break;
                }
            }

            // --- Faction defense structures (turrets, walls, etc.) ---
            sb.AppendLine("\n=== 防御建筑（需要指定所属方）===");
            int defCount = 0;
            foreach (var bt in map.Rules.BuildingTypes)
            {
                string ini = bt.ININame.ToUpperInvariant();
                string name = (bt.Name ?? "").ToLowerInvariant();
                if ((name.Contains("turret") || name.Contains("wall") || name.Contains("pillar") ||
                     name.Contains("gate") || name.Contains("bunker") || name.Contains("tower") ||
                     name.Contains("sentry") || name.Contains("sam") || name.Contains("cannon") ||
                     name.Contains("defense") || name.Contains("flak")) &&
                    !ini.StartsWith("CA") && !string.IsNullOrEmpty(bt.Name))
                {
                    sb.AppendLine($"  {bt.ININame} = {bt.Name}");
                    defCount++;
                    if (defCount >= 20) break;
                }
            }

            // --- Note about faction production buildings ---
            sb.AppendLine("\n=== 注意 ===");
            sb.AppendLine("玩家阵营生产建筑（建造厂、兵工厂、矿厂等）属于特定阵营，");
            sb.AppendLine("只在用户明确要求\"给玩家预置基地\"时才应放置。");
            sb.AppendLine("地图装饰应使用中立建筑(CA前缀)和中立载具。");
            sb.AppendLine("用 search_units 搜索你需要的任何特定单位。");

            return sb.ToString();
        }

        // ─── Spatial Helpers ──────────────────────────────────────

        /// <summary>
        /// Returns exclusion zones around all spawn points (waypoints 0-7).
        /// Used by tree/decoration placement to automatically avoid spawn areas.
        /// The 8-cell radius ensures players have room to deploy their base.
        /// </summary>
        private List<(Point2D Center, int Radius)> GetSpawnExclusionZones(int radius = 8)
        {
            var zones = new List<(Point2D, int)>();
            foreach (var wp in map.Waypoints)
            {
                if (wp.Identifier >= 0 && wp.Identifier <= 7 && wp.Position.X >= 0)
                    zones.Add((wp.Position, radius));
            }
            return zones;
        }

        // ─── Description Helpers ────────────────────────────────────

        /// <summary>
        /// Parses a JSON value as int, handling both Number and String types.
        /// AI models sometimes send numbers as strings (e.g. "50" instead of 50).
        /// </summary>
        private static int ParseJsonInt(JsonElement element, int fallback)
        {
            if (element.ValueKind == JsonValueKind.Number)
                return element.GetInt32();
            if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out int result))
                return result;
            return fallback;
        }

        private string GetPositionDescription(JsonElement args)
        {
            string semantic = args.TryGetString("position");
            if (semantic != null) return semantic;
            int? xPct = args.TryGetInt("x_pct");
            int? yPct = args.TryGetInt("y_pct");
            if (xPct.HasValue && yPct.HasValue) return $"({xPct}%,{yPct}%)";
            return "center";
        }

        private string GetEndpointDescription(JsonElement args, string prefix)
        {
            string semantic = args.TryGetString($"{prefix}_position");
            if (semantic != null) return semantic;
            int? xPct = args.TryGetInt($"{prefix}_x_pct");
            int? yPct = args.TryGetInt($"{prefix}_y_pct");
            if (xPct.HasValue && yPct.HasValue) return $"({xPct}%,{yPct}%)";
            return "center";
        }

        /// <summary>
        /// Returns a brief map state summary that is appended to tool results.
        /// This gives the AI real-time awareness of what has been placed on the map.
        /// </summary>
        private string GetMapStateSummary()
        {
            // Spawn points
            int spawnCount = map.Waypoints.Count(wp => wp.Identifier >= 0 && wp.Identifier <= 7);
            var spawnDetails = new List<string>();
            for (int i = 0; i <= 7; i++)
            {
                var wp = map.Waypoints.FirstOrDefault(w => w.Identifier == i);
                if (wp != null && wp.Position.X >= 0)
                    spawnDetails.Add($"P{i + 1}✓");
            }
            string spawnStatus = spawnCount > 0
                ? $"出生点: {string.Join(" ", spawnDetails)} ({spawnCount}个)"
                : "出生点: 无";

            // Object counts from direct map collections
            int buildingCount = map.Structures.Count;
            int treeCount = map.TerrainObjects.Count;
            int vehicleCount = map.Units.Count;
            int infantryCount = map.Infantry.Count;

            return $"[当前地图状态] {spawnStatus} | 建筑:{buildingCount} 树木:{treeCount} 载具:{vehicleCount} 步兵:{infantryCount}";
        }
        // ─── Tile Set Placement ──────────────────────────────────────

        private string ExecutePlaceTile(JsonElement args)
        {
            string tileSetName = args.GetProperty("tileset_name").GetString();
            int? variantIndex = args.TryGetInt("variant_index");

            // Find the tileset
            var tileSets = theaterGraphics.Theater.TileSets;
            CCEngine.TileSet matchedSet = null;
            foreach (var ts in tileSets)
            {
                if (ts.SetName != null && string.Equals(ts.SetName, tileSetName, StringComparison.OrdinalIgnoreCase))
                {
                    matchedSet = ts;
                    break;
                }
            }
            // Fuzzy match if exact not found
            if (matchedSet == null)
            {
                foreach (var ts in tileSets)
                {
                    if (ts.SetName?.IndexOf(tileSetName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matchedSet = ts;
                        break;
                    }
                }
            }

            if (matchedSet == null)
                return $"X 找不到地块集: {tileSetName}。请使用 get_map_info 查看可用地块集。";

            if (matchedSet.LoadedTileCount == 0)
                return $"X 地块集 {matchedSet.SetName} 没有可用地块。";

            // Determine which variant (tile within the set) to use
            int actualVariant;
            if (variantIndex.HasValue && variantIndex.Value >= 0 && variantIndex.Value < matchedSet.LoadedTileCount)
            {
                actualVariant = variantIndex.Value;
            }
            else
            {
                // Random variant
                actualVariant = new Random().Next(matchedSet.LoadedTileCount);
            }

            int tileIndex = matchedSet.StartTileIndex + actualVariant;

            // Get position
            var pos = ResolvePosition(args);

            // Place the tile using a small 1x1 mutation
            var cell = map.GetTile(pos.X, pos.Y);
            if (cell == null)
                return $"X 位置 ({pos.X},{pos.Y}) 超出地图范围。";

            // Use a 1x1 terrain mutation for proper undo support
            var mutation = new Mutations.Classes.AITerrainMutation(
                mutationTarget, pos.X, pos.Y, 1, 1, tileIndex,
                $"放置地块 {matchedSet.SetName} 变体{actualVariant}");
            mutationManager.PerformMutation(mutation);

            return $"> 已在 ({pos.X},{pos.Y}) 放置地块 {matchedSet.SetName} (变体{actualVariant}/{matchedSet.LoadedTileCount})";
        }

        // ─── Internal Helpers ────────────────────────────────────────

        private int FindTileIndexByName(string tileSetName)
        {
            if (string.IsNullOrWhiteSpace(tileSetName)) return -1;
            var tileSets = theaterGraphics.Theater.TileSets;
            for (int i = 0; i < tileSets.Count; i++)
            {
                if (string.Equals(tileSets[i].SetName, tileSetName, StringComparison.OrdinalIgnoreCase))
                    return tileSets[i].StartTileIndex;
            }
            for (int i = 0; i < tileSets.Count; i++)
            {
                if (tileSets[i].SetName?.IndexOf(tileSetName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return tileSets[i].StartTileIndex;
            }
            return -1;
        }

        private string ResolveObjectININame(string name, AIPlaceObjectType objectType)
        {
            List<TechnoType> types = objectType switch
            {
                AIPlaceObjectType.Building => map.Rules.BuildingTypes.Cast<TechnoType>().ToList(),
                AIPlaceObjectType.Vehicle => map.Rules.UnitTypes.Cast<TechnoType>().ToList(),
                AIPlaceObjectType.Infantry => map.Rules.InfantryTypes.Cast<TechnoType>().ToList(),
                _ => new List<TechnoType>()
            };

            // Exact → case-insensitive → partial → display name
            var match = types.Find(t => t.ININame == name)
                ?? types.Find(t => t.ININame.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? types.Find(t => t.ININame.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                ?? types.Find(t => t.GetEditorDisplayName().IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);

            return match?.ININame;
        }

        private List<string> FindSimilarNames(string name, AIPlaceObjectType objectType, int maxResults)
        {
            List<TechnoType> types = objectType switch
            {
                AIPlaceObjectType.Building => map.Rules.BuildingTypes.Cast<TechnoType>().ToList(),
                AIPlaceObjectType.Vehicle => map.Rules.UnitTypes.Cast<TechnoType>().ToList(),
                AIPlaceObjectType.Infantry => map.Rules.InfantryTypes.Cast<TechnoType>().ToList(),
                _ => new List<TechnoType>()
            };

            string nameLower = name.ToLowerInvariant();
            return types
                .Where(t => t.EditorVisible)
                .Select(t => (t.ININame, score: ScoreSimilarity(t.ININame, t.GetEditorDisplayName(), nameLower)))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .Take(maxResults)
                .Select(x => x.ININame)
                .ToList();
        }

        private int ScoreSimilarity(string iniName, string displayName, string query)
        {
            string iniLower = iniName.ToLowerInvariant();
            string displayLower = displayName.ToLowerInvariant();
            int score = 0;
            if (iniLower.Contains(query) || query.Contains(iniLower)) score += 10;
            if (displayLower.Contains(query) || query.Contains(displayLower)) score += 8;
            int prefixLen = 0;
            int minLen = Math.Min(iniLower.Length, query.Length);
            for (int i = 0; i < minLen && iniLower[i] == query[i]; i++) prefixLen++;
            score += prefixLen;
            return score;
        }

        private House ResolveOwner(string ownerName)
        {
            var houses = map.GetHouses();
            if (string.IsNullOrWhiteSpace(ownerName))
                return houses.Find(h => h.ININame == "Neutral") ?? (houses.Count > 0 ? houses[0] : null);

            return houses.Find(h => h.ININame == ownerName)
                ?? houses.Find(h => h.ININame.Equals(ownerName, StringComparison.OrdinalIgnoreCase))
                ?? houses.Find(h => h.ININame.IndexOf(ownerName, StringComparison.OrdinalIgnoreCase) >= 0)
                ?? (houses.Count > 0 ? houses[0] : null);
        }
    }

    // ─── JsonElement Extension Helpers ───────────────────────────

    internal static class JsonElementExtensions
    {
        public static string TryGetString(this JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
            return null;
        }

        public static int? TryGetInt(this JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
                return prop.GetInt32();
            return null;
        }
    }
}
