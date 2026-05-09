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
- ""LAT Grass"" = ★ 标准草地，创建地图时首先用它铺满全图作为基础！
- ""LAT Dark Grass"" = 深色草地，用于地图局部区域增加变化
- ""LAT Rough Grass"" = 粗糙草地，用于荒野区域
- ""Sand"" / ""Dirt"" = 沙地/泥地，用于沙漠或泥路区域
- 包含 ""Cliffs"" 的地形 = 悬崖岩壁，只用于地图边缘或山脉
- 包含 ""Ramps"" 的地形 = 坡道过渡，只用于不同高度之间的连接
- 包含 ""Water"" 的地形 = 水域，用于河流/湖泊
- ""Farm Crops"" = 农田，用于乡村风格区域
创建地图时：第一步必须用 ""LAT Grass"" 填满全图作为底层！

=== 复杂任务组合指南 ===

当用户提出高级需求时（如""创建对战地图""），即使用户描述很简单，你也必须自动补全所有细节，生成一张完整、高质量的地图。
规则：用户明确提出的要求优先，未提及的部分用下面的模板自动补全。例如用户说""不要高地""就跳过高地步骤，说""出生点在左右两侧""就不用四角分布。

★★★ 对战地图的关键规则 ★★★
- 对战地图中 **绝对不要放玩家基地建筑**！（建造场、兵营、电厂等）
- 玩家在游戏开始时会在出生点(Waypoint)处自动获得一辆基地车(MCV)
- 玩家自己展开基地车建造基地
- 地图上只放：地形、高地、矿区(含矿脉)、中立建筑、装饰物

=== 对战地图设计模板（根据玩家数自动选择） ===

【2人图模板】地图约 80x70，出生点在左下和右上对角：
- 矿脉(TIBTRE01): 6-8个（每个出生点附近2个矿区，中间2个扩张矿区）
- 中立石油井(CAOILD): 2个，在地图中线对称位置
- 装饰建筑: 商店(CASTOR/CASTOR02)x4、风车(CAWIND)x2、围栏木屋(CAWOODS)x4，点缀道路两侧
- 地形变化: 用 LAT Dark Grass 铺 3-4 块 15x15 区域增加地表变化
- 树林: 4簇（地图四边各一簇，12x12，density=0.15）
- 高地: 地图中央1个 12x12 高地(高度1)

【4人图模板(2v2)】地图约 100x100，出生点在四角：
- 矿脉(TIBTRE01): 12-16个（每个出生点附近2个矿区各2-3个矿脉，中间4个扩张矿区各1-2个矿脉）
- 中立石油井(CAOILD): 4个，在地图四条边的中点位置
- 装饰建筑: 商店(CASTOR)x4、公园长椅(CAPARK01)x4、风车(CAWIND)x4、围栏木屋(CAWOODS)x4，分散在地图各处
- 地形变化: 用 LAT Dark Grass 铺 4-6 块 15x20 区域，用 LAT Rough Grass 在高地周围铺，增加地表丰富度
- 树林: 4-6簇（地图边缘和通道分割处，每簇 12x12，density=0.12）
- 高地: 地图中央1个 15x15 高地(高度2)

【6人图模板】地图约 120x170，出生点均匀分布：
- 矿脉(TIBTRE01): 20-30个
- 中立石油井(CAOILD): 6个
- 装饰建筑: 混合使用 CASTOR、CAPARK01-06、CAWIND、CAWOODS 等共 15-20个
- 地形变化: 多种 LAT 地形混合
- 树林: 6-8簇
- 高地: 2-3个分散的高地

=== 创建对战地图的执行步骤 ===

即使用户只说""创建2v2对战地图""，你也必须执行以下全部步骤：

第一步 - 铺地形底图：
  fill_terrain, tileSetName=""LAT Grass"", 覆盖整个地图（标准草地）

第二步 - 创建地形起伏：
  set_height 在地图中央创建高地（高度1-2，区域至少10x10）

第三步 - 设置出生点：
  set_waypoint 设置对称的出生点（四角或对角分布）

第四步 - 铺起始矿区（每个出生点一个）：
  a. place_overlay 铺 8x8 的 TIB01 矿石（距出生点 10-15 格）
  b. place_terrain_object 在矿区中心放 TIBTRE01 矿脉（1个即可）
  ★ 没有 TIBTRE01 矿脉，矿石采完就没了！

第五步 - 铺扩张矿区（地图中部 2-4 个）：
  a. place_overlay 铺 6x6 的 TIB01 或 GEM01
  b. place_terrain_object 在每个矿区中心放 TIBTRE01 矿脉

第六步 - 放置中立建筑（owner=""Neutral""）：
  - 石油井(CAOILD): 对称放在地图中间区域，2-4个
  - 装饰建筑（增加地图细节感，每种 2-4 个，分散放置）：
    商店: CASTOR / CASTOR02
    公园: CAPARK01 / CAPARK04
    木屋: CAWOODS
    风车: CAWIND
    水塔: CABUBB
    路灯: CASOLR

第七步 - 铺地形变化（非常重要！让地图不单调）：
  fill_terrain 用 LAT Dark Grass 在地图各区域铺 4-6 块 15x20 的不规则区域
  fill_terrain 用 LAT Rough Grass 在高地周围铺粗糙草地
  这样地面就不会全是同一种颜色

第八步 - 散布树林：
  place_terrain_object 放 4-6 簇小树林
  每簇 12x12，density=0.12
  放在地图边缘和战场分隔区域
  避开出生点 25 格范围和矿区 5 格范围

=== set_height 使用规则 ===
- 高度范围 1-2（不要太高，系统会自动生成坡道过渡）
- 区域至少 10x10 格（太小会导致坡道无法正确生成）
- ★ 出生点区域必须保持平地
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
