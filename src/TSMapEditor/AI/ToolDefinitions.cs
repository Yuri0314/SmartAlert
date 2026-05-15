using System.Collections.Generic;

namespace TSMapEditor.AI
{
    /// <summary>
    /// Defines all tools available to the AI agent for map editing.
    /// Each tool has a JSON Schema that constrains the AI's parameters.
    /// </summary>
    public static class ToolDefinitions
    {
        private static readonly string[] PositionEnum = new[]
        {
            "center", "north", "south", "east", "west",
            "northwest", "northeast", "southwest", "southeast"
        };

        public static List<ToolDefinition> GetAllTools() => new List<ToolDefinition>
        {
            GetMapInfo,
            FillTerrain,
            CreatePlateau,
            DrawRoad,
            DrawRiver,
            PlaceBuilding,
            PlaceUnit,
            SetSpawnPoint,
            PlaceOre,
            PlaceTrees,
            PlaceDecorations,
            ClearArea,
            SetMapName,
            SearchUnits,
            PlaceTile,
        };

        // ─── Query Tools ────────────────────────────────────────────

        public static ToolDefinition GetMapInfo => new ToolDefinition
        {
            Name = "get_map_info",
            Description = "获取当前地图的基本信息：尺寸、当前状态、已有出生点等。在创建地图前先调用此工具了解地图情况。",
            Parameters = Schema(new Dictionary<string, object>()) // No params
        };

        // ─── Terrain Tools ──────────────────────────────────────────

        public static ToolDefinition FillTerrain => new ToolDefinition
        {
            Name = "fill_terrain",
            Description = "用指定地形类型填充一个区域。常用于铺设基础草地或局部地形变化。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["position"] = PropEnum("填充区域的中心位置", PositionEnum),
                ["x_pct"] = PropInt("中心X百分比(0=最左,100=最右)", 0, 100),
                ["y_pct"] = PropInt("中心Y百分比(0=最上,100=最下)", 0, 100),
                ["terrain"] = PropEnum("地形类型（也可使用 get_map_info 返回的可用地面类型名称）", new[]
                {
                    "grass", "dark_grass", "rough_grass", "sand", "pavement", "snow", "ice"
                }),
                ["scope"] = PropEnum("填充范围", new[]
                {
                    "full_map", "small_patch", "medium_patch", "large_patch"
                }),
            }, required: new[] { "terrain", "scope" })
        };

        public static ToolDefinition CreatePlateau => new ToolDefinition
        {
            Name = "create_plateau",
            Description = "创建带悬崖边缘的高地平台，自动生成斜坡。用于增加地形高差和战术深度。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["position"] = PropEnum("高地中心位置", PositionEnum),
                ["x_pct"] = PropInt("中心X百分比(0=最左,100=最右)", 0, 100),
                ["y_pct"] = PropInt("中心Y百分比(0=最上,100=最下)", 0, 100),
                ["size"] = PropEnum("高地大小", new[] { "small", "medium", "large" }),
                ["height"] = PropInt("高度级数(1-4)", 1, 4),
            }, required: new[] { "size" })
        };

        public static ToolDefinition DrawRoad => new ToolDefinition
        {
            Name = "draw_road",
            Description = "在两个位置之间画一条柏油公路(LAT Pavement)，用于连接基地和地图中央。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["from_position"] = PropEnum("起点位置", PositionEnum),
                ["to_position"] = PropEnum("终点位置", PositionEnum),
                ["from_x_pct"] = PropInt("起点X百分比", 0, 100),
                ["from_y_pct"] = PropInt("起点Y百分比", 0, 100),
                ["to_x_pct"] = PropInt("终点X百分比", 0, 100),
                ["to_y_pct"] = PropInt("终点Y百分比", 0, 100),
                ["width"] = PropInt("路宽(格数)", 3, 6),
            })
        };

        public static ToolDefinition DrawRiver => new ToolDefinition
        {
            Name = "draw_river",
            Description = "在两个位置之间画一条河流(Water)，宽度至少8格才能看到水面。用于分割战场。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["from_position"] = PropEnum("起点位置", PositionEnum),
                ["to_position"] = PropEnum("终点位置", PositionEnum),
                ["from_x_pct"] = PropInt("起点X百分比", 0, 100),
                ["from_y_pct"] = PropInt("起点Y百分比", 0, 100),
                ["to_x_pct"] = PropInt("终点X百分比", 0, 100),
                ["to_y_pct"] = PropInt("终点Y百分比", 0, 100),
                ["width"] = PropInt("河宽(格数，至少8)", 8, 15),
            })
        };

        // ─── Object Tools ───────────────────────────────────────────

        public static ToolDefinition PlaceBuilding => new ToolDefinition
        {
            Name = "place_building",
            Description = "放置一个建筑。常用建筑: GAWEAP(盟军兵工厂), NACNST(苏军建造厂), YAREFN(矿厂)",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["position"] = PropEnum("放置位置", PositionEnum),
                ["x_pct"] = PropInt("X百分比", 0, 100),
                ["y_pct"] = PropInt("Y百分比", 0, 100),
                ["name"] = PropString("建筑INI名称(如GAWEAP,NACNST)"),
                ["owner"] = PropEnum("所属方", new[] { "Neutral", "Special", "GDI", "Nod" }),
            }, required: new[] { "name" })
        };

        public static ToolDefinition PlaceUnit => new ToolDefinition
        {
            Name = "place_unit",
            Description = "放置一个载具单位。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["position"] = PropEnum("放置位置", PositionEnum),
                ["x_pct"] = PropInt("X百分比", 0, 100),
                ["y_pct"] = PropInt("Y百分比", 0, 100),
                ["name"] = PropString("单位INI名称"),
                ["owner"] = PropEnum("所属方", new[] { "Neutral", "Special", "GDI", "Nod" }),
            }, required: new[] { "name" })
        };

        public static ToolDefinition SetSpawnPoint => new ToolDefinition
        {
            Name = "set_spawn_point",
            Description = "设置玩家出生点(Waypoint)。2v2地图需要4个出生点(index 0-3)，1v1需要2个(index 0-1)。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["position"] = PropEnum("出生点位置", PositionEnum),
                ["x_pct"] = PropInt("X百分比", 0, 100),
                ["y_pct"] = PropInt("Y百分比", 0, 100),
                ["player_index"] = PropInt("玩家编号(0=P1, 1=P2, ...)", 0, 7),
            }, required: new[] { "player_index" })
        };

        public static ToolDefinition PlaceOre => new ToolDefinition
        {
            Name = "place_ore",
            Description = "在指定位置放置矿石资源(Tiberium/Ore)。每个出生点附近应放1-2片矿。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["position"] = PropEnum("矿石中心位置", PositionEnum),
                ["x_pct"] = PropInt("X百分比", 0, 100),
                ["y_pct"] = PropInt("Y百分比", 0, 100),
                ["amount"] = PropEnum("矿石数量", new[] { "small", "medium", "large" }),
                ["type"] = PropEnum("矿石类型", new[] { "ore", "gems" }),
            }, required: new[] { "amount" })
        };

        public static ToolDefinition PlaceTrees => new ToolDefinition
        {
            Name = "place_trees",
            Description = "在指定位置放置一片树木/装饰物。用于填充空旷区域。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["position"] = PropEnum("树木中心位置", PositionEnum),
                ["x_pct"] = PropInt("X百分比", 0, 100),
                ["y_pct"] = PropInt("Y百分比", 0, 100),
                ["density"] = PropEnum("树木密度", new[] { "sparse", "medium", "dense" }),
            })
        };

        public static ToolDefinition PlaceDecorations => new ToolDefinition
        {
            Name = "place_decorations",
            Description = "在指定区域自动散布装饰物（油桶、小屋、围墙、路灯等民用建筑）。装饰物类型自动匹配当前场景。用于让地图更有生活感。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["position"] = PropEnum("装饰区域中心位置", PositionEnum),
                ["x_pct"] = PropInt("X百分比", 0, 100),
                ["y_pct"] = PropInt("Y百分比", 0, 100),
                ["density"] = PropEnum("装饰密度", new[] { "sparse", "medium", "dense" }),
                ["radius"] = PropInt("散布半径(格数)", 3, 20),
            }, required: new[] { "density" })
        };

        public static ToolDefinition ClearArea => new ToolDefinition
        {
            Name = "clear_area",
            Description = "清除指定区域的所有物体(建筑、单位、树木等)。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["position"] = PropEnum("清除区域的中心", PositionEnum),
                ["x_pct"] = PropInt("X百分比", 0, 100),
                ["y_pct"] = PropInt("Y百分比", 0, 100),
                ["radius"] = PropInt("清除半径(格数)", 3, 30),
            }, required: new[] { "radius" })
        };

        // ─── Utility Tools ──────────────────────────────────────────

        public static ToolDefinition SetMapName => new ToolDefinition
        {
            Name = "set_map_name",
            Description = "设置地图名称。创建新地图时请调用此工具给地图起一个有创意的英文名。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["name"] = PropString("地图名称(英文)"),
            }, required: new[] { "name" })
        };

        public static ToolDefinition SearchUnits => new ToolDefinition
        {
            Name = "search_units",
            Description = "搜索单位编码及其含义。当用户提到某种建筑/载具/动物但你不确定INI代码时，用关键词搜索。支持中英文搜索。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["keyword"] = PropString("搜索关键词，如 'truck' '卡车' 'European' '路灯' 'farm' '医院' 等"),
            }, required: new[] { "keyword" })
        };

        public static ToolDefinition PlaceTile => new ToolDefinition
        {
            Name = "place_tile",
            Description = "在指定位置放置一个地块集中的地块。用于放置悬崖、海岸线、泥路等装饰性地形。先调用 get_map_info 查看可用地块集列表。",
            Parameters = Schema(new Dictionary<string, object>
            {
                ["tileset_name"] = PropString("地块集名称（从 get_map_info 返回的可用地块集中选择）"),
                ["position"] = PropEnum("放置位置", PositionEnum),
                ["x_pct"] = PropInt("X百分比(0=最左,100=最右)", 0, 100),
                ["y_pct"] = PropInt("Y百分比(0=最上,100=最下)", 0, 100),
                ["variant_index"] = PropInt("地块变体编号(0开始)，不指定则随机选择", 0, 100),
            }, required: new[] { "tileset_name" })
        };

        // ─── Schema Helpers ─────────────────────────────────────────

        private static Dictionary<string, object> Schema(
            Dictionary<string, object> properties,
            string[] required = null)
        {
            var schema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties,
            };
            if (required != null && required.Length > 0)
                schema["required"] = required;
            return schema;
        }

        private static Dictionary<string, object> PropEnum(string description, string[] values)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = description,
                ["enum"] = values
            };
        }

        private static Dictionary<string, object> PropInt(string description, int min, int max)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "integer",
                ["description"] = description,
                ["minimum"] = min,
                ["maximum"] = max
            };
        }

        private static Dictionary<string, object> PropString(string description)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = description
            };
        }
    }
}
