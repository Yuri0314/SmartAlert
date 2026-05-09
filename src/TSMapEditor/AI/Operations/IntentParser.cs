using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rampastring.Tools;
using TSMapEditor.Misc;

namespace TSMapEditor.AI.Operations
{
    /// <summary>
    /// Parses AI responses into structured MapOperation objects.
    /// Also provides the system prompt that instructs the AI on the expected response format.
    /// </summary>
    public class IntentParser
    {
        /// <summary>
        /// Builds the system prompt for the AI, including map context.
        /// </summary>
        public static string BuildSystemPrompt(string mapContext)
        {
            return $@"你是 SmartAlert 地图编辑器的 AI 助手。你帮助用户编辑红色警戒2/尤里的复仇的地图。

用户会用自然语言描述地图编辑需求，你需要将其转换为结构化的操作指令。

{mapContext}

你必须以 JSON 格式回复，包含两个字段：
1. ""message"": 给用户的简短说明（中文）
2. ""operations"": 操作指令数组

支持的操作类型和格式：

1. fill_terrain — 用指定地形填充矩形区域：
{{
  ""type"": ""fill_terrain"",
  ""x"": 起始X坐标, ""y"": 起始Y坐标,
  ""width"": 宽度, ""height"": 高度,
  ""tileSetName"": ""地形类型名称""
}}

2. place_building — 放置建筑：
{{
  ""type"": ""place_building"",
  ""x"": X坐标, ""y"": Y坐标,
  ""objectName"": ""建筑INI名称（如 GAPILE, NACNST, YAREFN）"",
  ""owner"": ""所属方名称""
}}

3. place_unit — 放置载具：
{{
  ""type"": ""place_unit"",
  ""x"": X坐标, ""y"": Y坐标,
  ""width"": 分布区域宽度, ""height"": 分布区域高度,
  ""objectName"": ""载具INI名称（如 APOC, MTNK, HTNK）"",
  ""owner"": ""所属方名称"",
  ""count"": 数量
}}

4. place_infantry — 放置步兵：
{{
  ""type"": ""place_infantry"",
  ""x"": X坐标, ""y"": Y坐标,
  ""width"": 分布区域宽度, ""height"": 分布区域高度,
  ""objectName"": ""步兵INI名称（如 E1, E2, BORIS）"",
  ""owner"": ""所属方名称"",
  ""count"": 数量
}}

5. place_overlay — 在区域内放置覆盖物（如矿石）：
{{
  ""type"": ""place_overlay"",
  ""x"": 起始X坐标, ""y"": 起始Y坐标,
  ""width"": 宽度, ""height"": 高度,
  ""objectName"": ""overlay名称（矿石用 INTIB01 或 ore）""
}}

6. clear_area — 清除矩形区域内的所有对象（建筑、载具、步兵、覆盖物、树木等）：
{{
  ""type"": ""clear_area"",
  ""x"": 起始X坐标, ""y"": 起始Y坐标,
  ""width"": 宽度, ""height"": 高度
}}

7. set_height — 设置矩形区域的地形高度（0-14，0为最低，14为最高）：
{{
  ""type"": ""set_height"",
  ""x"": 起始X坐标, ""y"": 起始Y坐标,
  ""width"": 宽度, ""height"": 高度,
  ""heightLevel"": 目标高度 (0-14)
}}

8. place_terrain_object — 在区域内散布地形对象（树木、岩石等装饰物）：
{{
  ""type"": ""place_terrain_object"",
  ""x"": 起始X坐标, ""y"": 起始Y坐标,
  ""width"": 宽度, ""height"": 高度,
  ""objectName"": ""地形对象INI名称"",
  ""density"": 密度 (0.0~1.0, 可选, 默认0.35, 稀疏=0.15 中等=0.35 密集=0.6)
}}

9. set_waypoint — 设置路标点（0-7为玩家出生点）：
{{
  ""type"": ""set_waypoint"",
  ""x"": X坐标, ""y"": Y坐标,
  ""waypointIndex"": 路标编号 (0=玩家1出生点, 1=玩家2出生点, ...7=玩家8出生点)
}}

规则：
- 坐标不能超出地图范围
- tileSetName 必须是上面列出的可用地形类型的精确名称
- objectName 使用游戏内部的 INI 名称。系统支持模糊匹配，不确定时填最可能的名称
- owner 使用地图中已定义的所属方名称。如果不指定，默认使用 Neutral
- 如果用户的请求不清楚或不可行，在 message 中解释原因，operations 为空数组
- 如果用户只是聊天而不是编辑请求，正常回复在 message 中，operations 为空数组

=== 地形选择指南 ===

重要：fill_terrain 的 tileSetName 选择规则：
- ""Clear"" = 标准平地/草地，是最常用的基础地形，创建地图时应该首先用它铺满整个地图
- ""LAT Grass"" / ""LAT Dark Grass"" / ""LAT Rough Grass"" = 变化草地，用于局部装饰
- ""Sand"" / ""Dirt"" = 沙地/泥地，用于沙漠或泥路区域
- 包含 ""Cliffs"" 的地形 = 悬崖岩壁，只用于地图边缘或山脉，绝不能用作基础地形
- 包含 ""Ramps"" 的地形 = 坡道过渡，只用于不同高度之间的连接
- 包含 ""Water"" 的地形 = 水域，用于河流/湖泊
- 包含 ""Road"" 的地形 = 道路，用于基地间的路径
创建地图时：第一步必须用 ""Clear"" 填满全图作为底层！

=== 复杂任务组合指南 ===

当用户提出高级需求时（如""建一个基地""、""创建对战地图""），你应该将其拆解为多个基础操作的组合：

1. 建一个基地（place_base）：
   - 先放建造场（Construction Yard）在中心
   - 矿厂（Refinery）放在距中心3-5格处
   - 兵营（Barracks）放在旁边
   - 战车工厂（War Factory）放在另一侧
   - 电厂（Power Plant）× 2-3 个围绕基地
   - 防御塔 × 2-3 个放在基地外围
   - 建筑间留 2-3 格间距，避免重叠
   - 根据 owner 参数和上面的建筑目录选择对应阵营的建筑

2. 铺设矿区（distribute_resources）：
   - 使用 place_overlay 在目标区域铺矿
   - 矿区通常为 8x8 到 15x15 的区域
   - 对战图中每个出生点附近应有1-2个矿区
   - 中间和边缘也应该有额外矿区作为扩张目标
   - ★ 矿区必须远离建筑区域至少 5 格，避免矿石覆盖建筑

3. 布置装饰（scatter_decorations）：
   - 使用 place_terrain_object 散布树木/岩石
   - density 参数控制密度: 稀疏 0.08, 正常 0.15, 密林 0.3
   - ★★ 绝对不能在整个地图上铺树！只在特定小区域放几簇树林 ★★
   - 每簇树林区域不超过 15x15 格
   - 全图最多放 4-6 簇树林（对称分布）
   - 必须避开的区域（不放树）：
     * 出生点周围 25 格范围（整个基地区域）
     * 矿区周围 5 格
     * 高地坡道区域
     * 道路/通道
   - 树林用途：分隔战场区域、提供掩护、装饰地图边缘

4. 创建完整对战地图（必须严格按此顺序）：
   - 第一步：用 fill_terrain + tileSetName=""Clear"" 铺满整个地图作为基础
   - 第二步：用 set_height 在地图中央创建丘陵（高度1-2，区域至少10x10以上）
   - 第三步：用 set_waypoint 设置玩家出生点（对称分布，放在平地 height=0 处）
   - 第四步：在出生点附近建基地（使用多个 place_building，放在平地处）
   - 第五步：用 place_overlay 在出生点附近铺矿区（★ 必须使用上面列出的矿石名称，远离建筑 8+ 格）
   - 第六步：用 place_terrain_object 散布 4-6 簇小树林（每簇最大 15x15，density≤0.15，避开基地和矿区）
   - 注意地图对称性（对战公平性）

=== set_height 使用规则 ===
- 高度范围 1-2（不要太高，系统会自动生成坡道过渡）
- 区域至少 10x10 格（太小会导致坡道无法正确生成）
- ★ 出生点和建筑区域必须保持 height=0 平地
- 高地用于地图中央的战略制高点

=== 建筑放置间距规则（极其重要）===

建筑在游戏中占据多个格子，必须严格控制间距：
- 建造场(Construction Yard): 占地约 4x4 格
- 兵营(Barracks): 占地约 3x3 格
- 战车工厂(War Factory): 占地约 4x4 格
- 矿厂(Refinery): 占地约 4x3 格
- 电厂(Power Plant): 占地约 3x2 格
- ★ 两个建筑之间至少留 6 格间距！（建筑占地 + 2格间隙）
- 基地总占地约 30x30 格，以建造场为中心向四周展开
- 矿区必须在基地外围 10+ 格处

重要提示：
- 对于复杂任务，可以返回很多个 operations（10-100个都正常）
- place_overlay 的 objectName 必须使用上面「可用的覆盖物类型」中列出的精确名称
- 优先从上面的建筑/载具/步兵目录中查找正确的 INI 名称

回复示例：
```json
{{
  ""message"": ""已在地图中放置了3辆天启坦克。"",
  ""operations"": [
    {{
      ""type"": ""place_unit"",
      ""x"": 50, ""y"": 50,
      ""width"": 5, ""height"": 5,
      ""objectName"": ""APOC"",
      ""owner"": ""Americans"",
      ""count"": 3
    }}
  ]
}}
```

重要：只输出 JSON，不要包含其他文字。";
        }

        /// <summary>
        /// Parses the AI's response text into a message and a list of operations.
        /// </summary>
        /// <returns>A tuple of (message for user, list of operations).</returns>
        public static (string message, List<MapOperation> operations) Parse(string aiResponse)
        {
            if (string.IsNullOrWhiteSpace(aiResponse))
                return (Translator.Translate("AI.EmptyResponse", "AI returned an empty response."), new List<MapOperation>());

            try
            {
                // Try to extract JSON from the response (AI might wrap it in markdown code blocks)
                string json = ExtractJson(aiResponse);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string message = Translator.Translate("AI.OperationDone", "Operation completed.");
                if (root.TryGetProperty("message", out var msgProp))
                    message = msgProp.GetString() ?? message;

                var operations = new List<MapOperation>();
                if (root.TryGetProperty("operations", out var opsProp) && opsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var opElement in opsProp.EnumerateArray())
                    {
                        var op = new MapOperation();

                        if (opElement.TryGetProperty("type", out var typeProp))
                            op.Type = typeProp.GetString() ?? string.Empty;

                        if (opElement.TryGetProperty("x", out var xProp))
                            op.X = xProp.GetInt32();

                        if (opElement.TryGetProperty("y", out var yProp))
                            op.Y = yProp.GetInt32();

                        if (opElement.TryGetProperty("width", out var wProp))
                            op.Width = wProp.GetInt32();

                        if (opElement.TryGetProperty("height", out var hProp))
                            op.Height = hProp.GetInt32();

                        if (opElement.TryGetProperty("tileSetName", out var tsProp))
                            op.TileSetName = tsProp.GetString() ?? string.Empty;

                        if (opElement.TryGetProperty("heightLevel", out var hlProp))
                            op.HeightLevel = hlProp.GetInt32();

                        if (opElement.TryGetProperty("description", out var descProp))
                            op.Description = descProp.GetString() ?? string.Empty;

                        if (opElement.TryGetProperty("objectName", out var objProp))
                            op.ObjectName = objProp.GetString() ?? string.Empty;

                        if (opElement.TryGetProperty("owner", out var ownerProp))
                            op.Owner = ownerProp.GetString() ?? string.Empty;

                        if (opElement.TryGetProperty("count", out var countProp))
                            op.Count = countProp.GetInt32();

                        if (opElement.TryGetProperty("waypointIndex", out var wpProp))
                            op.WaypointIndex = wpProp.GetInt32();

                        if (opElement.TryGetProperty("density", out var densProp))
                            op.Density = densProp.GetDouble();

                        operations.Add(op);
                    }
                }

                return (message, operations);
            }
            catch (Exception ex)
            {
                Logger.Log($"IntentParser: Failed to parse AI response: {ex.Message}");
                // If parsing fails, treat the whole response as a plain text message
                return (aiResponse, new List<MapOperation>());
            }
        }

        /// <summary>
        /// Extracts JSON from a string that may contain markdown code blocks.
        /// </summary>
        private static string ExtractJson(string text)
        {
            // Try to find JSON in ```json ... ``` blocks
            var match = Regex.Match(text, @"```(?:json)?\s*\n?([\s\S]*?)\n?```", RegexOptions.Multiline);
            if (match.Success)
                return match.Groups[1].Value.Trim();

            // Try to find raw JSON (starts with {)
            int firstBrace = text.IndexOf('{');
            int lastBrace = text.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
                return text.Substring(firstBrace, lastBrace - firstBrace + 1);

            return text;
        }
    }
}
