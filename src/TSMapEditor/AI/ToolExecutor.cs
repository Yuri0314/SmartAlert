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
using TSMapEditor.AI.Workflow;

namespace TSMapEditor.AI
{
    /// <summary>
    /// Executes tool calls from the AI agent.
    /// Each tool call is mapped to a map mutation via PositionResolver.
    /// The AI never sees raw coordinates — it only knows semantic positions and percentages.
    /// </summary>
    /// <summary>
    /// Controls whether position resolution uses the active selection or the full map.
    /// </summary>
    internal enum PositionScope
    {
        /// <summary>Always resolve against the full map diamond.</summary>
        Global,
        /// <summary>Resolve relative to active selection when one exists; fall back to full map otherwise.</summary>
        SelectionWhenActive
    }

    public class ToolExecutor
    {
        private readonly Map map;
        private readonly TheaterGraphics theaterGraphics;
        private readonly MutationManager mutationManager;
        private readonly IMutationTarget mutationTarget;
        private readonly PositionResolver positionResolver;
        private readonly AIWorkflowState workflowState;
        private readonly Func<AISelection> selectionProvider;

        // Unit reference: code -> description, loaded from References/unit_reference.json
        private readonly Dictionary<string, string> unitReference = new(StringComparer.OrdinalIgnoreCase);

        // Chinese unit aliases loaded from References/unit_aliases.zh.json
        private IReadOnlyList<AIUnitAliasMatch> unitAliases = System.Array.Empty<AIUnitAliasMatch>();

        public ToolExecutor(Map map, TheaterGraphics theaterGraphics,
            MutationManager mutationManager, IMutationTarget mutationTarget,
            AIWorkflowState workflowState = null, Func<AISelection> selectionProvider = null)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.theaterGraphics = theaterGraphics ?? throw new ArgumentNullException(nameof(theaterGraphics));
            this.mutationManager = mutationManager ?? throw new ArgumentNullException(nameof(mutationManager));
            this.mutationTarget = mutationTarget ?? throw new ArgumentNullException(nameof(mutationTarget));
            this.workflowState = workflowState;
            this.selectionProvider = selectionProvider;
            this.positionResolver = new PositionResolver(map);
            LoadUnitReference();
            LoadUnitAliases();
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
                    "get_workflow_state" => ExecuteGetWorkflowState(),
                    "set_workflow_state" => ExecuteSetWorkflowState(args),
                    "get_houses" => ExecuteGetHouses(),
                    "fill_terrain" => ExecuteFillTerrain(args),
                    "create_plateau" => ExecuteCreatePlateau(args),
                    "draw_road" => ExecuteDrawRoad(args),
                    "draw_river" => ExecuteDrawRiver(args),
                    "place_building" => ExecutePlaceBuilding(args),
                    "place_buildings" => ExecutePlaceBatch(args, AIPlaceObjectType.Building),
                    "place_unit" => ExecutePlaceUnit(args),
                    "place_units" => ExecutePlaceBatch(args, AIPlaceObjectType.Vehicle),
                    "place_infantry" => ExecutePlaceInfantry(args),
                    "place_infantries" => ExecutePlaceBatch(args, AIPlaceObjectType.Infantry),
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
                bool isQueryOnly = toolName == "get_map_info" || toolName == "search_units" || toolName == "get_workflow_state" || toolName == "set_workflow_state" || toolName == "get_houses";
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

        /// <summary>
        /// Reads the optional "coordinate_scope" parameter from tool args.
        /// If the AI passes coordinate_scope=global, returns Global regardless of the default.
        /// Otherwise returns the default scope (typically SelectionWhenActive for local tools).
        /// </summary>
        private static PositionScope ResolveRequestedPositionScope(JsonElement args, PositionScope defaultScope)
        {
            string coordinateScope = args.TryGetString("coordinate_scope");
            if (string.Equals(coordinateScope, "global", System.StringComparison.OrdinalIgnoreCase))
                return PositionScope.Global;
            return defaultScope;
        }

        /// <summary>
        /// Resolves position with selection-awareness when a selection is active.
        /// Local tools use SelectionWhenActive; global tools use Global.
        /// </summary>
        private Point2D ResolvePosition(JsonElement args, PositionScope scope = PositionScope.Global)
        {
            string semantic = args.TryGetString("position");
            int? xPct = args.TryGetInt("x_pct");
            int? yPct = args.TryGetInt("y_pct");

            if (scope == PositionScope.SelectionWhenActive)
            {
                var selection = selectionProvider?.Invoke();
                if (selection != null)
                    return positionResolver.ResolveWithinSelection(selection, semantic, xPct, yPct);
            }

            return positionResolver.Resolve(semantic, xPct, yPct);
        }

        /// <summary>
        /// Resolves a percentage position with selection-awareness.
        /// Used by batch placement and other tools that pass coordinates directly.
        /// </summary>
        private Point2D ResolvePercentagePosition(int xPct, int yPct, PositionScope scope = PositionScope.Global)
        {
            if (scope == PositionScope.SelectionWhenActive)
            {
                var selection = selectionProvider?.Invoke();
                if (selection != null)
                    return positionResolver.ResolveWithinSelection(selection, null, xPct, yPct);
            }

            return positionResolver.Resolve(null, xPct, yPct);
        }

        private AISelection GetActiveSelection(PositionScope scope)
        {
            if (scope != PositionScope.SelectionWhenActive)
                return null;

            return selectionProvider?.Invoke();
        }

        private static bool IsWithinSelection(Point2D point, AISelection selection)
        {
            if (selection == null)
                return true;

            return point.X >= selection.X
                && point.Y >= selection.Y
                && point.X < selection.X + selection.Width
                && point.Y < selection.Y + selection.Height;
        }

        /// <summary>
        /// Resolves path endpoints with optional selection-awareness.
        /// draw_road/draw_river now resolve selection-relative when a selection is active.
        /// </summary>
        private Point2D ResolveEndpoint(JsonElement args, string posKey, string xKey, string yKey,
            PositionScope scope = PositionScope.SelectionWhenActive)
        {
            string semantic = args.TryGetString(posKey);
            int? xPct = args.TryGetInt(xKey);
            int? yPct = args.TryGetInt(yKey);

            if (scope == PositionScope.SelectionWhenActive)
            {
                var selection = selectionProvider?.Invoke();
                if (selection != null)
                    return positionResolver.ResolveWithinSelection(selection, semantic, xPct, yPct);
            }

            return positionResolver.Resolve(semantic, xPct, yPct);
        }

        // ─── Selection Containment Helpers ──────────────────────────

        /// <summary>
        /// Clamps a radius so the circular area around center stays within the active selection.
        /// Returns true if the operation can proceed with the (possibly reduced) radius.
        /// Returns false if the selection is too small for even radius=1.
        /// If no selection is active, returns the requested radius unchanged.
        /// </summary>
        internal static bool TryClampRadiusToSelection(Point2D center, int requestedRadius, AISelection selection,
            out int clampedRadius, out string warning)
        {
            warning = null;

            if (selection == null)
            {
                clampedRadius = requestedRadius;
                return true;
            }

            int distLeft = center.X - selection.X;
            int distRight = (selection.X + selection.Width - 1) - center.X;
            int distTop = center.Y - selection.Y;
            int distBottom = (selection.Y + selection.Height - 1) - center.Y;

            int maxRadius = Math.Min(Math.Min(distLeft, distRight), Math.Min(distTop, distBottom));
            maxRadius = Math.Max(0, maxRadius);

            if (maxRadius < 1)
            {
                clampedRadius = 0;
                warning = "选区太小，无法容纳最小半径操作";
                return false;
            }

            clampedRadius = Math.Min(requestedRadius, maxRadius);
            if (clampedRadius < requestedRadius)
                warning = $"半径从{requestedRadius}缩小到{clampedRadius}以适应选区";

            return true;
        }

        /// <summary>
        /// Clamps a rectangle (startX, startY, width, height) to fit within the active selection.
        /// Returns false if the intersection is empty.
        /// If no selection is active, returns the original rectangle unchanged.
        /// </summary>
        internal static bool TryClampRectToSelection(int startX, int startY, int width, int height,
            AISelection selection, out int clampedX, out int clampedY, out int clampedW, out int clampedH,
            out string warning)
        {
            warning = null;

            if (selection == null)
            {
                clampedX = startX;
                clampedY = startY;
                clampedW = width;
                clampedH = height;
                return true;
            }

            int selRight = selection.X + selection.Width;
            int selBottom = selection.Y + selection.Height;
            int rectRight = startX + width;
            int rectBottom = startY + height;

            clampedX = Math.Max(startX, selection.X);
            clampedY = Math.Max(startY, selection.Y);
            int endX = Math.Min(rectRight, selRight);
            int endY = Math.Min(rectBottom, selBottom);

            clampedW = endX - clampedX;
            clampedH = endY - clampedY;

            if (clampedW <= 0 || clampedH <= 0)
            {
                clampedW = 0;
                clampedH = 0;
                warning = "请求的区域完全在选区范围外";
                return false;
            }

            if (clampedW < width || clampedH < height)
                warning = $"区域从 {width}x{height} 裁剪为 {clampedW}x{clampedH} 以适应选区";

            return true;
        }

        // ─── Tool Implementations ───────────────────────────────────

        private string ExecuteGetWorkflowState()
        {
            if (workflowState == null)
            {
                return "❌ WorkflowState is not available in the current context.";
            }
            return workflowState.ToSummary();
        }

        private string ExecuteSetWorkflowState(JsonElement args)
        {
            if (workflowState == null)
            {
                return "❌ WorkflowState is not available in the current context.";
            }

            int updated = Workflow.AIWorkflowStateUpdater.Apply(workflowState, args);
            if (updated == 0)
            {
                return "⚠️ No fields were updated. Provide at least one of: user_goal, intent, current_phase, completed_steps, pending_steps, known_risks.";
            }

            return $"✅ 已更新工作流状态（{updated}个字段）\n" + workflowState.ToSummary();
        }

        private string ExecuteGetHouses()
        {
            var houses = map.GetHouses();
            return AIHouseSummaryFormatter.Format(houses);
        }

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
                // When selection is active, "full_map" fills the selected rectangle
                var selection = selectionProvider?.Invoke();
                if (selection != null)
                {
                    startX = selection.X;
                    startY = selection.Y;
                    width = selection.Width;
                    height = selection.Height;
                }
                else
                {
                    // Fill the entire map area
                    startX = 1;
                    startY = 1;
                    int maxCoord = map.Size.X + map.Size.Y - 1;
                    width = maxCoord;
                    height = maxCoord;
                }
            }
            else
            {
                var pos = ResolvePosition(args, ResolveRequestedPositionScope(args, PositionScope.SelectionWhenActive));
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
            var pos = ResolvePosition(args, ResolveRequestedPositionScope(args, PositionScope.SelectionWhenActive));
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

            // Clamp radius to fit within active selection
            var activeSelection = selectionProvider?.Invoke();
            if (!TryClampRadiusToSelection(pos, radius, activeSelection, out int clampedRadius, out string clampWarning))
                return $"⏭ {clampWarning}";
            if (clampWarning != null)
                Logger.Log($"ToolExecutor create_plateau: {clampWarning}");
            radius = clampedRadius;

            Logger.Log($"ToolExecutor create_plateau: pos=({pos.X},{pos.Y}) radius={radius} height={height}");

            var mutation = new AICreatePlateauMutation(mutationTarget, pos.X, pos.Y, radius, height);
            mutationManager.PerformMutation(mutation);

            // Warn if plateau edge/slope overlaps any spawn point
            string spawnWarning = "";
            foreach (var zone in GetSpawnExclusionZones(6))
            {
                int dx = pos.X - zone.Center.X;
                int dy = pos.Y - zone.Center.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                // Check if spawn is in the slope ring (between radius-3 and radius+3)
                if (dist >= radius - 3 && dist <= radius + 3)
                    spawnWarning = "\n⚠️ 高地斜坡可能覆盖出生点，玩家无法在坡上建造建筑";
            }

            return $"✓ 已在 {GetPositionDescription(args)} 创建{size}高地，半径{radius}，高度{height}{spawnWarning}";
        }

        private string ExecuteDrawRoad(JsonElement args)
        {
            var scope = ResolveRequestedPositionScope(args, PositionScope.SelectionWhenActive);
            var from = ResolveEndpoint(args, "from_position", "from_x_pct", "from_y_pct", scope);
            var to = ResolveEndpoint(args, "to_position", "to_x_pct", "to_y_pct", scope);
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
            var scope = ResolveRequestedPositionScope(args, PositionScope.SelectionWhenActive);
            var from = ResolveEndpoint(args, "from_position", "from_x_pct", "from_y_pct", scope);
            var to = ResolveEndpoint(args, "to_position", "to_x_pct", "to_y_pct", scope);
            int width = args.TryGetInt("width") ?? 10;
            width = Math.Max(8, Math.Min(15, width));

            int tileIndex = FindTileIndexByName("Water");
            if (tileIndex < 0)
                return "❌ 找不到 Water 地形类型";

            // Check if river path would cross near any spawn point — warn but don't block
            var spawnZones = GetSpawnExclusionZones(5); // 5-cell buffer (matches base clear radius)
            var riverWarnings = new List<string>();
            foreach (var zone in spawnZones)
            {
                double distToLine = PointToSegmentDistance(zone.Center, from, to);
                if (distToLine < zone.Radius + width / 2)
                {
                    var pct = CellToPercentage(zone.Center);
                    riverWarnings.Add($"P({pct.Item1}%,{pct.Item2}%)距{distToLine:F0}格");
                }
            }

            Logger.Log($"ToolExecutor draw_river: from=({from.X},{from.Y}) to=({to.X},{to.Y}) width={width}");

            var mutation = new AIDrawPathMutation(mutationTarget, from.X, from.Y, to.X, to.Y,
                width, tileIndex, $"绘制河流", flattenHeight: true, targetHeight: 0);

            mutationManager.PerformMutation(mutation);

            string result = $"✓ 已绘制河流从 {GetEndpointDescription(args, "from")} 到 {GetEndpointDescription(args, "to")}，宽度{width}";
            if (riverWarnings.Count > 0)
                result += $"\n  ⚠️ 河流靠近出生点: {string.Join(", ", riverWarnings)}。请确保这些出生点仍有足够展开空间。";

            return result;
        }

        /// <summary>
        /// Calculates the minimum distance from a point to a line segment.
        /// Used to check if rivers/roads would cross spawn points.
        /// </summary>
        private static double PointToSegmentDistance(Point2D point, Point2D segA, Point2D segB)
        {
            double dx = segB.X - segA.X;
            double dy = segB.Y - segA.Y;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < 1) return Math.Sqrt((point.X - segA.X) * (point.X - segA.X) + (point.Y - segA.Y) * (point.Y - segA.Y));

            double t = Math.Max(0, Math.Min(1, ((point.X - segA.X) * dx + (point.Y - segA.Y) * dy) / lenSq));
            double projX = segA.X + t * dx;
            double projY = segA.Y + t * dy;
            double pdx = point.X - projX;
            double pdy = point.Y - projY;
            return Math.Sqrt(pdx * pdx + pdy * pdy);
        }

        private string ExecutePlaceBuilding(JsonElement args)
        {
            return ExecutePlaceObject(args, AIPlaceObjectType.Building);
        }

        private string ExecutePlaceUnit(JsonElement args)
        {
            return ExecutePlaceObject(args, AIPlaceObjectType.Vehicle);
        }

        private string ExecutePlaceInfantry(JsonElement args)
        {
            return ExecutePlaceObject(args, AIPlaceObjectType.Infantry);
        }

        private static string GetObjectTypeDisplayName(AIPlaceObjectType objectType)
        {
            return objectType switch
            {
                AIPlaceObjectType.Building => "建筑",
                AIPlaceObjectType.Vehicle => "载具",
                AIPlaceObjectType.Infantry => "步兵",
                _ => "对象"
            };
        }

        private string ExecutePlaceObject(JsonElement args, AIPlaceObjectType objectType)
        {
            var resolvedScope = ResolveRequestedPositionScope(args, PositionScope.SelectionWhenActive);
            AISelection activeSelection = GetActiveSelection(resolvedScope);
            var pos = ResolvePosition(args, resolvedScope);
            string name = args.GetProperty("name").GetString();
            string ownerName = args.TryGetString("owner") ?? "Neutral";

            // Resolve INI name
            string resolvedName = ResolveObjectININame(name, objectType);
            if (resolvedName == null)
            {
                var suggestions = FindSimilarNames(name, objectType, 5);
                string typeName = GetObjectTypeDisplayName(objectType);
                if (suggestions.Count > 0)
                    return $"❌ 找不到{typeName}: \"{name}\"。你是否要找: {string.Join(", ", suggestions)}";
                return $"❌ 找不到{typeName}: \"{name}\"";
            }

            House owner = ResolveOwner(ownerName);
            if (owner == null)
                return $"❌ 找不到所属方: \"{ownerName}\"。可用所属方: {AIHouseResolver.FormatAvailableOwners(map.GetHouses())}";

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

            // Auto-relocate away from spawn exclusion zones
            string relocateNote = "";
            var finalPos = positions[0];
            foreach (var zone in GetSpawnExclusionZones())
            {
                int sdx = finalPos.X - zone.Center.X;
                int sdy = finalPos.Y - zone.Center.Y;
                int distSq = sdx * sdx + sdy * sdy;
                if (distSq <= zone.Radius * zone.Radius)
                {
                    double dist = Math.Sqrt(distSq);
                    if (dist < 1) dist = 1;
                    double scale = (zone.Radius + 2) / dist;
                    int newX = zone.Center.X + (int)(sdx * scale);
                    int newY = zone.Center.Y + (int)(sdy * scale);
                    var relocated = new Point2D(newX, newY);
                    if (map.GetTile(relocated) != null)
                    {
                        positions = new List<Point2D> { relocated };
                        relocateNote = "（已自动外移避开出生点）";
                    }
                    break;
                }
            }

            // Validate terrain: don't place on water or slopes
            var validatedPos = FindNearestValidCell(positions[0]);
            if (validatedPos.X < 0)
                return $"⏭ 跳过 {resolvedName}：目标位置在水面/斜坡上且附近无可用陆地";
            if (validatedPos != positions[0])
            {
                positions = new List<Point2D> { validatedPos };
                relocateNote += "（已避开水面/斜坡）";
            }

            if (!IsWithinSelection(positions[0], activeSelection))
                return $"⚠️ 跳过 {resolvedName}：未找到选区 {activeSelection} 内的有效放置位置。";

            var mutation = new AIPlaceObjectMutation(mutationTarget, objectType,
                resolvedName, owner, positions, $"放置 {resolvedName}");
            mutationManager.PerformMutation(mutation);

            if (!mutation.PlacedAny)
                return $"❌ 未能放置 {resolvedName}：当前位置不可用或该对象无法在当前规则中创建";

            return $"✓ 已在 {GetPositionDescription(args)} 放置 {resolvedName}（{owner.ININame}）{relocateNote}";
        }

        /// <summary>
        /// Batch placement: handles place_buildings, place_units, and place_infantries.
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
            AISelection activeSelection = GetActiveSelection(PositionScope.SelectionWhenActive);

            foreach (var item in itemsArray.EnumerateArray())
            {
                try
                {
                    string name = item.GetProperty("name").GetString();
                    int xPct = item.TryGetProperty("x_pct", out var xp) ? ParseJsonInt(xp, 50) : 50;
                    int yPct = item.TryGetProperty("y_pct", out var yp) ? ParseJsonInt(yp, 50) : 50;
                    string ownerName = item.TryGetProperty("owner", out var ow) ? ow.GetString() ?? "Neutral" : "Neutral";

                    var itemScope = ResolveRequestedPositionScope(item, PositionScope.SelectionWhenActive);
                    var pos = ResolvePercentagePosition(xPct, yPct, itemScope);

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
                        errors.Add($"{name}(所属方'{ownerName}'无效，可用: {AIHouseResolver.FormatAvailableOwners(map.GetHouses())})");
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

                    // Auto-relocate away from spawn exclusion zones
                    foreach (var zone in spawnZones)
                    {
                        int sdx = pos.X - zone.Center.X;
                        int sdy = pos.Y - zone.Center.Y;
                        int distSq = sdx * sdx + sdy * sdy;
                        if (distSq <= zone.Radius * zone.Radius)
                        {
                            // Push outward from spawn center to just outside the exclusion radius
                            double dist = Math.Sqrt(distSq);
                            if (dist < 1) dist = 1;
                            double scale = (zone.Radius + 2) / dist;
                            int newX = zone.Center.X + (int)(sdx * scale);
                            int newY = zone.Center.Y + (int)(sdy * scale);
                            var relocated = new Point2D(newX, newY);
                            if (map.GetTile(relocated) != null)
                            {
                                pos = relocated;
                                positions = new List<Point2D> { pos };
                                spawnWarnings.Add($"{resolvedName}→已自动外移");
                            }
                            break;
                        }
                    }

                    // Validate terrain: don't place on water or slopes
                    var validatedPos = FindNearestValidCell(pos);
                    if (validatedPos.X < 0)
                    {
                        errors.Add($"{resolvedName}(水面/斜坡无可用陆地)");
                        failCount++;
                        continue;
                    }
                    if (validatedPos != pos)
                    {
                        pos = validatedPos;
                        positions = new List<Point2D> { pos };
                        spawnWarnings.Add($"{resolvedName}→已避开水面/斜坡");
                    }

                    if (!IsWithinSelection(pos, activeSelection))
                    {
                        errors.Add($"{resolvedName}(未找到选区 {activeSelection} 内的有效位置)");
                        failCount++;
                        continue;
                    }

                    var mutation = new AIPlaceObjectMutation(mutationTarget, objectType,
                        resolvedName, owner, positions, $"批量放置 {resolvedName}");
                    mutationManager.PerformMutation(mutation);

                    if (mutation.PlacedAny)
                    {
                        successCount++;
                    }
                    else
                    {
                        errors.Add($"{resolvedName}(放置失败：位置不可用或对象无法创建)");
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"异常: {ex.Message}");
                    failCount++;
                }
            }

            string typeName = GetObjectTypeDisplayName(objectType);
            string result = $"✓ 批量放置{typeName}: {successCount}个成功";
            if (failCount > 0)
                result += $", {failCount}个失败({string.Join(", ", errors)})";

            // Report relocated items so AI knows what happened
            if (spawnWarnings.Count > 0)
                result += $"\n📍 {spawnWarnings.Count}个物品因靠近出生点被自动外移: {string.Join(", ", spawnWarnings)}";

            return result;
        }

        private string ExecuteSetSpawnPoint(JsonElement args)
        {
            var pos = ResolvePosition(args, PositionScope.Global);
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

            // Build distance feedback to existing spawn points
            var distanceInfo = new List<string>();
            bool hasCloseWarning = false;
            for (int i = 0; i <= 7; i++)
            {
                if (i == playerIndex) continue;
                var wp = map.Waypoints.FirstOrDefault(w => w.Identifier == i);
                if (wp == null || wp.Position.X < 0) continue;
                int dx = pos.X - wp.Position.X;
                int dy = pos.Y - wp.Position.Y;
                int dist = (int)Math.Sqrt(dx * dx + dy * dy);
                var otherPct = CellToPercentage(wp.Position);
                string entry = $"P{i + 1}({otherPct.Item1}%,{otherPct.Item2}%)={dist}格";
                if (dist < 40)
                {
                    entry += "⚠️近";
                    hasCloseWarning = true;
                }
                distanceInfo.Add(entry);
            }

            var myPct = CellToPercentage(pos);
            string result = $"✓ 已设置玩家{playerIndex + 1}出生点在 {GetPositionDescription(args)}({myPct.Item1}%,{myPct.Item2}%)（已自动清除周围障碍物）";
            if (distanceInfo.Count > 0)
                result += $"\n  距离: {string.Join(", ", distanceInfo)}";
            if (hasCloseWarning)
                result += "\n  ⚠️ 存在距离过近的出生点(<40格)，建议调整以确保基地展开空间";

            return result;
        }

        private string ExecutePlaceOre(JsonElement args)
        {
            var pos = ResolvePosition(args, ResolveRequestedPositionScope(args, PositionScope.SelectionWhenActive));
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
            // Clamp radius to fit within active selection
            var activeSelection = selectionProvider?.Invoke();
            if (!TryClampRadiusToSelection(pos, radius, activeSelection, out int clampedRadius, out string clampWarning))
                return $"⏭ {clampWarning}";
            if (clampWarning != null)
                Logger.Log($"ToolExecutor place_ore: {clampWarning}");
            radius = clampedRadius;

            var mutation = new AIPlaceOverlayMutation(mutationTarget, overlayType,
                pos.X, pos.Y, radius, 0.7f, $"放置{type}矿");
            mutationManager.PerformMutation(mutation);

            // Read optional include_mine parameter (default: true for backward compatibility)
            bool includeMine = true;
            if (args.TryGetProperty("include_mine", out JsonElement includeMineElement) &&
                (includeMineElement.ValueKind == JsonValueKind.True || includeMineElement.ValueKind == JsonValueKind.False))
            {
                includeMine = includeMineElement.GetBoolean();
            }

            // Auto-place an Ore Mine drill (TIBTRE01) at the center of the ore field
            // TIBTRE01 is a terrain object that continuously regenerates ore for players to harvest
            // Used 6258 times across 720 official MO maps
            string minePlaced = "";
            if (includeMine)
            {
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
            }

            return $"✓ 已在 {GetPositionDescription(args)} 放置{amount}{(type == "gems" ? "宝石" : "矿石")}{minePlaced}";
        }

        private string ExecutePlaceTrees(JsonElement args)
        {
            var pos = ResolvePosition(args, ResolveRequestedPositionScope(args, PositionScope.SelectionWhenActive));
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

            // Clamp radius to fit within active selection
            var activeSelection = selectionProvider?.Invoke();
            if (!TryClampRadiusToSelection(pos, radius, activeSelection, out int clampedRadius, out string clampWarning))
                return $"⏭ {clampWarning}";
            radius = clampedRadius;

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
            var pos = ResolvePosition(args, ResolveRequestedPositionScope(args, PositionScope.SelectionWhenActive));
            int radius = args.GetProperty("radius").GetInt32();
            radius = Math.Max(3, Math.Min(30, radius));
            // Clamp clear rectangle to selection bounds
            int clearStartX = pos.X - radius;
            int clearStartY = pos.Y - radius;
            int clearW = radius * 2;
            int clearH = radius * 2;

            var activeSelection = selectionProvider?.Invoke();
            if (!TryClampRectToSelection(clearStartX, clearStartY, clearW, clearH, activeSelection,
                out int cx, out int cy, out int cw, out int ch, out string clampWarning))
                return $"⏭ {clampWarning}";

            var mutation = new AIClearAreaMutation(mutationTarget,
                cx, cy, cw, ch, $"清除区域");
            mutationManager.PerformMutation(mutation);

            string result = $"✓ 已清除 {GetPositionDescription(args)} 半径{radius}的区域";
            if (clampWarning != null)
                result += $"\n  ⚠️ {clampWarning}";
            return result;
        }

        private string ExecutePlaceDecorations(JsonElement args)
        {
            var pos = ResolvePosition(args, ResolveRequestedPositionScope(args, PositionScope.SelectionWhenActive));
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
            // Clamp radius to fit within active selection
            var activeSelection = selectionProvider?.Invoke();
            if (!TryClampRadiusToSelection(pos, radius, activeSelection, out int clampedRadius, out string clampWarning))
                return $"⏭ {clampWarning}";
            radius = clampedRadius;

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

            // 1. Check Chinese aliases first (high-confidence matches)
            var aliasMatches = AIUnitAliasResolver.FindMatches(unitAliases, keyword);

            // 2. Normal reference search
            var results = new List<string>();
            foreach (var kvp in unitReference)
            {
                bool match = kvp.Key.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                             kvp.Value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
                if (match)
                    results.Add($"  {kvp.Key} = {kvp.Value}");
            }

            // 3. Build combined output
            var output = new List<string>();

            if (aliasMatches.Count > 0)
            {
                output.Add("High-confidence aliases:");
                foreach (var am in aliasMatches)
                    output.Add($"  {am.Code} = {am.Alias} / {am.Type} / {am.Description}");
            }

            if (results.Count > 0)
            {
                if (output.Count > 0) output.Add("");
                // Limit to 20 results to avoid overwhelming
                if (results.Count > 20)
                {
                    output.Add($"找到 {results.Count} 个匹配项（显示前20个）:");
                    output.AddRange(results.Take(20));
                }
                else
                {
                    output.Add($"找到 {results.Count} 个匹配项:");
                    output.AddRange(results);
                }
            }

            if (output.Count == 0)
                return $"未找到匹配 \"{keyword}\" 的单位";

            return string.Join("\n", output);
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
        /// Loads Chinese unit alias codebook from References/unit_aliases.zh.json.
        /// </summary>
        private void LoadUnitAliases()
        {
            try
            {
                string[] searchPaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AI", "References", "unit_aliases.zh.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "AI", "References", "unit_aliases.zh.json"),
                };

                string aliasPath = null;
                foreach (var p in searchPaths)
                {
                    if (File.Exists(p)) { aliasPath = p; break; }
                }

                if (aliasPath == null)
                {
                    Logger.Log("ToolExecutor: unit_aliases.zh.json not found, Chinese alias resolution will be limited");
                    return;
                }

                string json = File.ReadAllText(aliasPath);
                unitAliases = AIUnitAliasResolver.LoadFromJson(json);
                Logger.Log($"ToolExecutor: Loaded {unitAliases.Count} Chinese unit aliases");
            }
            catch (Exception ex)
            {
                Logger.Log($"ToolExecutor: Failed to load unit aliases: {ex.Message}");
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

        /// <summary>
        /// Checks if a cell is valid for placing buildings/units:
        /// - Cell exists on map
        /// - Not a water tile
        /// - Not on a slope (height transition)
        /// </summary>
        private bool IsValidPlacementCell(Point2D pos)
        {
            var cell = map.GetTile(pos);
            if (cell == null) return false;

            // Check for water tiles
            if (cell.TileIndex > 0 && theaterGraphics != null)
            {
                try
                {
                    var tileImage = theaterGraphics.GetTileGraphics(cell.TileIndex);
                    if (tileImage != null)
                    {
                        int tileSetId = tileImage.TileSetId;
                        var theater = theaterGraphics.Theater;
                        if (theater != null && tileSetId >= 0 && tileSetId < theater.TileSets.Count)
                        {
                            var tileSet = theater.TileSets[tileSetId];
                            if (tileSet.SetName != null &&
                                tileSet.SetName.Contains("Water", StringComparison.OrdinalIgnoreCase))
                                return false;
                        }
                    }
                }
                catch { /* Tile index out of range — treat as valid */ }
            }

            // Check for height transitions (slopes) by comparing with neighbors
            int myHeight = cell.Level;
            int[] dxs = { -1, 1, 0, 0 };
            int[] dys = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                var neighbor = map.GetTile(pos.X + dxs[i], pos.Y + dys[i]);
                if (neighbor != null && neighbor.Level != myHeight)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Finds the nearest valid placement cell (not water, not slope).
        /// Returns Point2D(-1,-1) if no valid cell is found, signaling to SKIP placement.
        /// </summary>
        private Point2D FindNearestValidCell(Point2D pos, int maxRadius = 10)
        {
            if (IsValidPlacementCell(pos)) return pos;

            for (int r = 1; r <= maxRadius; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                        var candidate = new Point2D(pos.X + dx, pos.Y + dy);
                        if (IsValidPlacementCell(candidate))
                            return candidate;
                    }
                }
            }
            return new Point2D(-1, -1); // No valid land found — caller should skip placement
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
            // Spawn points with percentage coordinates
            int spawnCount = map.Waypoints.Count(wp => wp.Identifier >= 0 && wp.Identifier <= 7);
            var spawnDetails = new List<string>();
            for (int i = 0; i <= 7; i++)
            {
                var wp = map.Waypoints.FirstOrDefault(w => w.Identifier == i);
                if (wp != null && wp.Position.X >= 0)
                {
                    var pct = CellToPercentage(wp.Position);
                    spawnDetails.Add($"P{i + 1}({pct.Item1}%,{pct.Item2}%)");
                }
            }
            string spawnStatus = spawnCount > 0
                ? $"出生点: {string.Join(" ", spawnDetails)}"
                : "出生点: 无";

            // Object counts from direct map collections
            int buildingCount = map.Structures.Count;
            int treeCount = map.TerrainObjects.Count;
            int vehicleCount = map.Units.Count;
            int infantryCount = map.Infantry.Count;

            return $"[当前地图状态] {spawnStatus} | 建筑:{buildingCount} 树木:{treeCount} 载具:{vehicleCount} 步兵:{infantryCount}";
        }

        /// <summary>
        /// Converts isometric cell coordinates to approximate percentage (0-100) for AI readability.
        /// </summary>
        private (int, int) CellToPercentage(Point2D cellPos)
        {
            int center = positionResolver.Center;
            int radius = positionResolver.DiamondRadius;
            int safeRadius = (int)(radius * 0.85);
            if (safeRadius < 1) safeRadius = 1;

            int xPct = 50 + (int)((cellPos.X - center) * 50.0 / safeRadius);
            int yPct = 50 + (int)((cellPos.Y - center) * 50.0 / safeRadius);
            xPct = Math.Max(0, Math.Min(100, xPct));
            yPct = Math.Max(0, Math.Min(100, yPct));
            return (xPct, yPct);
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
            var pos = ResolvePosition(args, ResolveRequestedPositionScope(args, PositionScope.SelectionWhenActive));

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

        private List<TechnoType> GetObjectTypes(AIPlaceObjectType objectType)
        {
            return objectType switch
            {
                AIPlaceObjectType.Building => map.Rules.BuildingTypes.Cast<TechnoType>().ToList(),
                AIPlaceObjectType.Vehicle => map.Rules.UnitTypes.Cast<TechnoType>().ToList(),
                AIPlaceObjectType.Infantry => map.Rules.InfantryTypes.Cast<TechnoType>().ToList(),
                _ => new List<TechnoType>()
            };
        }

        private string ResolveObjectININame(string name, AIPlaceObjectType objectType)
        {
            List<TechnoType> types = GetObjectTypes(objectType);

            // Try Chinese alias exact resolution first, but validate against loaded rules
            string aliasCode = AIUnitAliasResolver.ResolveExact(unitAliases, name, objectType);
            if (aliasCode != null)
            {
                var aliasMatch = types.Find(t => t.ININame.Equals(aliasCode, StringComparison.OrdinalIgnoreCase));
                if (aliasMatch != null)
                    return aliasMatch.ININame;

                Logger.Log($"ToolExecutor: alias '{name}' resolved to '{aliasCode}' but no matching {objectType} type exists in loaded rules. Falling through to search.");
            }

            // Exact → case-insensitive → partial → display name
            var match = types.Find(t => t.ININame == name)
                ?? types.Find(t => t.ININame.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? types.Find(t => t.ININame.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                ?? types.Find(t => t.GetEditorDisplayName().IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);

            return match?.ININame;
        }

        private List<string> FindSimilarNames(string name, AIPlaceObjectType objectType, int maxResults)
        {
            List<TechnoType> types = GetObjectTypes(objectType);

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
            return AIHouseResolver.ResolveOwner(map.GetHouses(), ownerName);
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
