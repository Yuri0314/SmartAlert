using TSMapEditor.AI.Operations;

namespace TSMapEditor.Tests
{
    /// <summary>
    /// Tests for IntentParser — the critical bridge between AI JSON responses and MapOperations.
    /// These tests verify that all 9 operation types parse correctly, including edge cases.
    /// </summary>
    public class IntentParserTests
    {
        // ===== Basic parsing =====

        [Fact]
        public void Parse_ValidFillTerrain_ReturnsCorrectOperation()
        {
            string json = @"{
                ""message"": ""已填充草地"",
                ""operations"": [{
                    ""type"": ""fill_terrain"",
                    ""x"": 10, ""y"": 20,
                    ""width"": 30, ""height"": 40,
                    ""tileSetName"": ""Green""
                }]
            }";

            var (message, ops) = IntentParser.Parse(json);

            Assert.Equal("已填充草地", message);
            Assert.Single(ops);
            Assert.Equal("fill_terrain", ops[0].Type);
            Assert.Equal(10, ops[0].X);
            Assert.Equal(20, ops[0].Y);
            Assert.Equal(30, ops[0].Width);
            Assert.Equal(40, ops[0].Height);
            Assert.Equal("Green", ops[0].TileSetName);
        }

        [Fact]
        public void Parse_ValidPlaceBuilding_ReturnsCorrectOperation()
        {
            string json = @"{
                ""message"": ""已放置建筑"",
                ""operations"": [{
                    ""type"": ""place_building"",
                    ""x"": 50, ""y"": 60,
                    ""objectName"": ""NACNST"",
                    ""owner"": ""Russians""
                }]
            }";

            var (message, ops) = IntentParser.Parse(json);

            Assert.Single(ops);
            Assert.Equal("place_building", ops[0].Type);
            Assert.Equal(50, ops[0].X);
            Assert.Equal(60, ops[0].Y);
            Assert.Equal("NACNST", ops[0].ObjectName);
            Assert.Equal("Russians", ops[0].Owner);
        }

        [Fact]
        public void Parse_ValidPlaceUnit_WithCount_ReturnsCorrectOperation()
        {
            string json = @"{
                ""message"": ""已放置坦克"",
                ""operations"": [{
                    ""type"": ""place_unit"",
                    ""x"": 50, ""y"": 50,
                    ""width"": 5, ""height"": 5,
                    ""objectName"": ""APOC"",
                    ""owner"": ""Americans"",
                    ""count"": 3
                }]
            }";

            var (message, ops) = IntentParser.Parse(json);

            Assert.Single(ops);
            Assert.Equal("place_unit", ops[0].Type);
            Assert.Equal("APOC", ops[0].ObjectName);
            Assert.Equal("Americans", ops[0].Owner);
            Assert.Equal(3, ops[0].Count);
            Assert.Equal(5, ops[0].Width);
        }

        [Fact]
        public void Parse_ValidPlaceInfantry_ReturnsCorrectOperation()
        {
            string json = @"{
                ""message"": ""已放置步兵"",
                ""operations"": [{
                    ""type"": ""place_infantry"",
                    ""x"": 30, ""y"": 40,
                    ""width"": 3, ""height"": 3,
                    ""objectName"": ""E1"",
                    ""owner"": ""Neutral"",
                    ""count"": 5
                }]
            }";

            var (message, ops) = IntentParser.Parse(json);

            Assert.Single(ops);
            Assert.Equal("place_infantry", ops[0].Type);
            Assert.Equal("E1", ops[0].ObjectName);
            Assert.Equal(5, ops[0].Count);
        }

        [Fact]
        public void Parse_ValidPlaceOverlay_ReturnsCorrectOperation()
        {
            string json = @"{
                ""message"": ""已放置矿石"",
                ""operations"": [{
                    ""type"": ""place_overlay"",
                    ""x"": 20, ""y"": 30,
                    ""width"": 10, ""height"": 10,
                    ""objectName"": ""INTIB01""
                }]
            }";

            var (message, ops) = IntentParser.Parse(json);

            Assert.Single(ops);
            Assert.Equal("place_overlay", ops[0].Type);
            Assert.Equal("INTIB01", ops[0].ObjectName);
        }

        // ===== Phase 4B new operations =====

        [Fact]
        public void Parse_ValidClearArea_ReturnsCorrectOperation()
        {
            string json = @"{
                ""message"": ""已清除区域"",
                ""operations"": [{
                    ""type"": ""clear_area"",
                    ""x"": 10, ""y"": 20,
                    ""width"": 15, ""height"": 15
                }]
            }";

            var (message, ops) = IntentParser.Parse(json);

            Assert.Single(ops);
            Assert.Equal("clear_area", ops[0].Type);
            Assert.Equal(10, ops[0].X);
            Assert.Equal(20, ops[0].Y);
            Assert.Equal(15, ops[0].Width);
            Assert.Equal(15, ops[0].Height);
        }

        [Fact]
        public void Parse_ValidSetHeight_ReturnsCorrectOperation()
        {
            string json = @"{
                ""message"": ""已设置高度"",
                ""operations"": [{
                    ""type"": ""set_height"",
                    ""x"": 10, ""y"": 20,
                    ""width"": 10, ""height"": 10,
                    ""heightLevel"": 4
                }]
            }";

            var (message, ops) = IntentParser.Parse(json);

            Assert.Single(ops);
            Assert.Equal("set_height", ops[0].Type);
            Assert.Equal(4, ops[0].HeightLevel);
        }

        [Fact]
        public void Parse_ValidPlaceTerrainObject_WithDensity_ReturnsCorrectOperation()
        {
            string json = @"{
                ""message"": ""已散布树木"",
                ""operations"": [{
                    ""type"": ""place_terrain_object"",
                    ""x"": 10, ""y"": 20,
                    ""width"": 20, ""height"": 20,
                    ""objectName"": ""TREE01"",
                    ""density"": 0.6
                }]
            }";

            var (message, ops) = IntentParser.Parse(json);

            Assert.Single(ops);
            Assert.Equal("place_terrain_object", ops[0].Type);
            Assert.Equal("TREE01", ops[0].ObjectName);
            Assert.Equal(0.6, ops[0].Density, 2);
        }

        [Fact]
        public void Parse_ValidSetWaypoint_ReturnsCorrectOperation()
        {
            string json = @"{
                ""message"": ""已设置出生点"",
                ""operations"": [{
                    ""type"": ""set_waypoint"",
                    ""x"": 30, ""y"": 40,
                    ""waypointIndex"": 0
                }]
            }";

            var (message, ops) = IntentParser.Parse(json);

            Assert.Single(ops);
            Assert.Equal("set_waypoint", ops[0].Type);
            Assert.Equal(30, ops[0].X);
            Assert.Equal(40, ops[0].Y);
            Assert.Equal(0, ops[0].WaypointIndex);
        }

        // ===== Multiple operations =====

        [Fact]
        public void Parse_MultipleOperations_ReturnsAll()
        {
            string json = @"{
                ""message"": ""已完成多个操作"",
                ""operations"": [
                    { ""type"": ""fill_terrain"", ""x"": 1, ""y"": 1, ""width"": 50, ""height"": 50, ""tileSetName"": ""Green"" },
                    { ""type"": ""place_building"", ""x"": 25, ""y"": 25, ""objectName"": ""GACNST"", ""owner"": ""Americans"" },
                    { ""type"": ""set_waypoint"", ""x"": 25, ""y"": 25, ""waypointIndex"": 0 }
                ]
            }";

            var (message, ops) = IntentParser.Parse(json);

            Assert.Equal(3, ops.Count);
            Assert.Equal("fill_terrain", ops[0].Type);
            Assert.Equal("place_building", ops[1].Type);
            Assert.Equal("set_waypoint", ops[2].Type);
        }

        // ===== Edge cases =====

        [Fact]
        public void Parse_EmptyString_ReturnsEmptyMessage()
        {
            var (message, ops) = IntentParser.Parse("");

            Assert.Equal("AI 返回了空响应。", message);
            Assert.Empty(ops);
        }

        [Fact]
        public void Parse_NullString_ReturnsEmptyMessage()
        {
            var (message, ops) = IntentParser.Parse(null!);

            Assert.Equal("AI 返回了空响应。", message);
            Assert.Empty(ops);
        }

        [Fact]
        public void Parse_PlainTextResponse_ReturnsTextAsMessage()
        {
            string response = "我不太理解你的意思，能否更详细描述一下？";

            var (message, ops) = IntentParser.Parse(response);

            // Should return the plain text as the message
            Assert.Equal(response, message);
            Assert.Empty(ops);
        }

        [Fact]
        public void Parse_JsonInMarkdownCodeBlock_ExtractsCorrectly()
        {
            string response = @"```json
{
    ""message"": ""从代码块中提取"",
    ""operations"": [{
        ""type"": ""clear_area"",
        ""x"": 5, ""y"": 5,
        ""width"": 10, ""height"": 10
    }]
}
```";

            var (message, ops) = IntentParser.Parse(response);

            Assert.Equal("从代码块中提取", message);
            Assert.Single(ops);
            Assert.Equal("clear_area", ops[0].Type);
        }

        [Fact]
        public void Parse_EmptyOperations_ReturnsMessageOnly()
        {
            string json = @"{
                ""message"": ""这只是一条聊天消息"",
                ""operations"": []
            }";

            var (message, ops) = IntentParser.Parse(json);

            Assert.Equal("这只是一条聊天消息", message);
            Assert.Empty(ops);
        }

        [Fact]
        public void Parse_MissingOptionalFields_UsesDefaults()
        {
            string json = @"{
                ""message"": ""test"",
                ""operations"": [{
                    ""type"": ""place_unit"",
                    ""x"": 10, ""y"": 20,
                    ""objectName"": ""APOC""
                }]
            }";

            var (message, ops) = IntentParser.Parse(json);

            Assert.Single(ops);
            // Defaults
            Assert.Equal(1, ops[0].Width);
            Assert.Equal(1, ops[0].Height);
            Assert.Equal(1, ops[0].Count);
            Assert.Equal(string.Empty, ops[0].Owner);
            Assert.Equal(0.35, ops[0].Density, 2);
        }

        // ===== System Prompt =====

        [Fact]
        public void BuildSystemPrompt_ContainsAllOperationTypes()
        {
            string prompt = IntentParser.BuildSystemPrompt("test context");

            Assert.Contains("fill_terrain", prompt);
            Assert.Contains("place_building", prompt);
            Assert.Contains("place_unit", prompt);
            Assert.Contains("place_infantry", prompt);
            Assert.Contains("place_overlay", prompt);
            Assert.Contains("clear_area", prompt);
            Assert.Contains("set_height", prompt);
            Assert.Contains("place_terrain_object", prompt);
            Assert.Contains("set_waypoint", prompt);
        }

        [Fact]
        public void BuildSystemPrompt_ContainsMapContext()
        {
            string prompt = IntentParser.BuildSystemPrompt("=== 测试地图上下文 ===");

            Assert.Contains("测试地图上下文", prompt);
        }

        [Fact]
        public void BuildSystemPrompt_ContainsJsonFormat()
        {
            string prompt = IntentParser.BuildSystemPrompt("");

            Assert.Contains("JSON", prompt);
            Assert.Contains("message", prompt);
            Assert.Contains("operations", prompt);
        }

        [Fact]
        public void BuildSystemPrompt_ContainsCompositionGuidelines()
        {
            string prompt = IntentParser.BuildSystemPrompt("");

            // Should contain high-level task composition guidelines
            Assert.Contains("复杂任务组合指南", prompt);
            Assert.Contains("建一个基地", prompt);
            Assert.Contains("铺设矿区", prompt);
            Assert.Contains("布置装饰", prompt);
            Assert.Contains("创建完整对战地图", prompt);
        }

        [Fact]
        public void Parse_ComplexBaseLayout_ParsesMultipleOperations()
        {
            // Simulate what AI would return for "建一个盟军基地"
            string json = @"{
                ""message"": ""已为盟军建立基地"",
                ""operations"": [
                    { ""type"": ""place_building"", ""x"": 50, ""y"": 50, ""objectName"": ""GACNST"", ""owner"": ""Americans"" },
                    { ""type"": ""place_building"", ""x"": 53, ""y"": 50, ""objectName"": ""GAREFN"", ""owner"": ""Americans"" },
                    { ""type"": ""place_building"", ""x"": 47, ""y"": 50, ""objectName"": ""GAWEAP"", ""owner"": ""Americans"" },
                    { ""type"": ""place_building"", ""x"": 50, ""y"": 53, ""objectName"": ""GAPOWR"", ""owner"": ""Americans"" },
                    { ""type"": ""place_building"", ""x"": 50, ""y"": 47, ""objectName"": ""GAPOWR"", ""owner"": ""Americans"" },
                    { ""type"": ""set_waypoint"", ""x"": 50, ""y"": 50, ""waypointIndex"": 0 },
                    { ""type"": ""place_overlay"", ""x"": 55, ""y"": 55, ""width"": 10, ""height"": 10, ""objectName"": ""INTIB01"" }
                ]
            }";

            var (message, ops) = IntentParser.Parse(json);

            Assert.Equal(7, ops.Count);
            Assert.Equal(5, ops.Count(o => o.Type == "place_building"));
            Assert.Equal(1, ops.Count(o => o.Type == "set_waypoint"));
            Assert.Equal(1, ops.Count(o => o.Type == "place_overlay"));
        }
    }
}
