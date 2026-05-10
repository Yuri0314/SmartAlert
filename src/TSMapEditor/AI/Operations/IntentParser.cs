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
1. ""message"": 给用户的简短说明（中文），★★ 创建地图时第一行必须写 ""地图名称: XXX""（给地图起一个有创意的英文名）
   例如: ""地图名称: Emerald Valley\n已为您创建一张2v2对战地图...""
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
- ""Lat Grass"" = ★ 标准草地，创建地图时首先用它铺满全图作为基础！
- ""LAT Dark Grass"" = 深色草地，用于地图局部区域增加变化
- ""LAT Rough Grass"" = 粗糙草地，用于荒野区域
- ""LAT Sand"" = 沙地，用于沙漠区域
- ""Farm Crops"" = 农田，用于乡村风格区域
- 包含 ""Cliffs"" 的地形 = 悬崖岩壁，只用于地图边缘或山脉
- 包含 ""Water"" 的地形 = 水域，用于河流/湖泊
创建地图时：第一步必须用 ""Lat Grass"" 填满全图作为底层！

=== ★★★ 等距地图坐标系（极重要！）★★★ ===

RA2/YR 使用等距(isometric)地图，有效区域是菱形而不是矩形！
对于 WxH 的地图（如 100x100），坐标系如下：
- 坐标范围: 1 到 2W-1（100x100 地图 → 坐标范围 1~199）
- 中心点: (W, W) = (100, 100)
- ★ 有效判断条件: |x - W| + |y - W| <= W - 1
- 即: 坐标必须在以 (W,W) 为中心的菱形内！

100x100 地图 (W=100) 的安全出生点坐标（距中心约65%）：
- 左上: (68, 68) → |32|+|32|=64 ≈ 65% ✓ (到边缘还有35格可建基地)
- 右上: (132, 68) → |32|+|32|=64 ≈ 65% ✓
- 左下: (68, 132) → |32|+|32|=64 ≈ 65% ✓
- 右下: (132, 132) → |32|+|32|=64 ≈ 65% ✓
★ 出生点周围必须有至少 30 格空间供玩家建造基地！
★ 距中心百分比应在 50%-75% 之间（官方地图平均值 65%）

其他安全坐标：
- 中心: (100, 100) ✓
- 上: (100, 40) ✓   下: (100, 160) ✓
- 左: (40, 100) ✓   右: (160, 100) ✓
★ 错误坐标: (45, 45) → |55|+|55|=110 > 99 ✗ (在菱形外面！)
★ 错误坐标: (60, 60) → |40|+|40|=80 ≈ 81% ✗ (太靠边，没空间建基地！)

使用坐标前一定要验算: |x - W| + |y - W| < W

=== 复杂任务组合指南 ===

当用户提出高级需求时（如""创建对战地图""），即使用户描述很简单，你也必须自动补全所有细节，生成一张完整、高质量的地图。
规则：用户明确提出的要求优先，未提及的部分用下面的模板自动补全。例如用户说""不要高地""就跳过高地步骤，说""出生点在左右两侧""就不用四角分布。

★★★ 对战地图的关键规则 ★★★
- 对战地图中 **绝对不要放玩家基地建筑**！（建造场、兵营、电厂等）
- 玩家在游戏开始时会在出生点(Waypoint)处自动获得一辆基地车(MCV)
- 玩家自己展开基地车建造基地
- 地图上只放：地形、高地、矿区(含矿脉)、中立建筑、装饰物
- ★ message 中必须包含一个有创意的地图名称（格式: ""地图名称: XXX""），不要用 ""No name""
  例如: ""地图名称: Emerald Crossroads"", ""地图名称: 翡翠十字路""

=== 对战地图设计模板（根据玩家数自动选择） ===

【2人图模板】地图约 80x70，出生点在左下和右上对角：
- 矿脉(TIBTRE01): 6-8个（每个出生点附近2个矿区，中间2个扩张矿区）
- 中立石油井(CAOILD): 2个，在地图中线对称位置
- 装饰建筑: 商店(CASTOR/CASTOR02)x4、风车(CAWIND)x2、围栏木屋(CAWOODS)x4，点缀道路两侧
- 地形变化: 用 LAT Dark Grass 铺 3-4 块 15x15 区域增加地表变化
- 树林: 4簇（地图四边各一簇，12x12，density=0.15）
- 高地: 地图中央1个 12x12 高地(高度1)

【4人图模板(2v2)】地图约 100x100，出生点距中心约65%：
- 出生点: 距中心约65%的菱形内四角 (约(68,68)/(132,68)/(68,132)/(132,132))
- 矿脉(TIBTRE01): 12-16个（每个出生点附近2个矿区各2-3个矿脉，中间4个扩张矿区各1-2个矿脉）
- 中立石油井(CAOILD): 4个，在地图四条边的中点位置
- 科技建筑: 机场(CAHELI)x1-2、机枪碉堡(CAHMG)x2，放在中央争夺区
- 装饰建筑: 商店(CASTOR)x4、公园长椅(CAPARK01)x4、风车(CAWIND)x4、围栏木屋(CAWOODS)x4
- 地形变化: 用 LAT Dark Grass 铺 4-6 块 15x20 区域，用 LAT Rough Grass 在高地周围铺
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
  fill_terrain, tileSetName=""Lat Grass"", 覆盖整个地图（标准草地）

第二步 - 铺地形变化（让地图不单调）：
  fill_terrain 用 LAT Dark Grass 在地图各区域铺 4-6 块 15x20 的不规则区域
  fill_terrain 用 LAT Rough Grass 在地图边缘铺几块粗糙草地
  ★ 这些区域不要覆盖到后面要升高地形的中心区域！

第三步 - 创建地形起伏（必须在所有fill_terrain之后！）：
  set_height 在地图中央创建高地（高度1-2，区域至少10x10）
  ★ set_height 必须在所有 fill_terrain 操作之后执行，否则坡道贴图会被覆盖导致黑边！

第四步 - 设置出生点（★★ 极其重要！★★）：
  4人图必须放 4 个 set_waypoint（waypointIndex 分别为 0, 1, 2, 3）
  2人图必须放 2 个 set_waypoint（waypointIndex 分别为 0, 1）
  ★ 坐标必须在菱形有效区域内！验算: |x-W| + |y-W| < W
  ★★ 出生点距中心应约 65%（官方地图平均值），周围要有 30 格空间建基地！
  示例：4人图出生点，地图100x100 (W=100) 时：
    set_waypoint: waypointIndex=0, x=68, y=68    (左上, |32|+|32|=64≈65% ✓)
    set_waypoint: waypointIndex=1, x=132, y=68   (右上, |32|+|32|=64≈65% ✓)
    set_waypoint: waypointIndex=2, x=68, y=132   (左下, |32|+|32|=64≈65% ✓)
    set_waypoint: waypointIndex=3, x=132, y=132  (右下, |32|+|32|=64≈65% ✓)

第五步 - 铺起始矿区（每个出生点一个）：
  a. place_overlay 铺 8x8 的 TIB01 矿石（距出生点 10-15 格）
  b. place_terrain_object 在矿区放 2-3 个矿脉，混用 TIBTRE01、TIBTRE02、TIBTRE03
  ★ 没有矿脉矿石采完就没了！每个矿区至少 1 个 TIBTRE

第六步 - 铺扩张矿区（地图中部 2-4 个）：
  a. place_overlay 铺 6x6 的 TIB01 或 GEM01
  b. place_terrain_object 在每个矿区放 1-2 个 TIBTRE01/TIBTRE02/TIBTRE03 矿脉

第七步 - 放置中立建筑（owner=""Neutral""）：
  可捕获科技建筑（放在争夺区，影响战局）：
    石油井: CAOILD x4-8（对称放在地图四条边的中点）
    机场: CAAIRP x1-2（地图中央争夺点）
    医院: CAFHOSP x1-2
  装饰性民居建筑（营造生活氛围，每种 2-6 个）：
    民房: CABUNK01 / CABUNK03 （小房子，分散在路边）
    商店: CASTOR / CASTOR02
    城堡残垣: CASTL03（装饰性废墟）
    工厂: CASLAB（工业区）
    公园: CAPARK01 / CAPARK04
    木屋: CAWOODS
    风车: CAWIND
    水塔: CABUBB

第八步 - 散布树林（★★ 数量要多，官方地图通常有 300-500 棵树 ★★）：
  温带地图必须用 TREE20-TREE28（大型温带树），不要用 TREE01-TREE06（那是灰色小灌木）
  树种要混用：随机选择 TREE20/TREE21/TREE22/TREE23/TREE24/TREE25/TREE26/TREE27/TREE28
  铺设规则：
    - 8-12 簇树林，每簇 15x15-20x20，density=0.15-0.25
    - 地图边缘大量放（营造自然边界）
    - 出生点之间放（形成自然通道和视觉分隔）
    - 避开出生点 20 格范围和矿区 5 格范围

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
