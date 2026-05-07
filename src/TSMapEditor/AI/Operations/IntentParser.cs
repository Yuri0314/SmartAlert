using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rampastring.Tools;

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

每个操作指令的格式：
{{
  ""type"": ""fill_terrain"",
  ""x"": 起始X坐标,
  ""y"": 起始Y坐标,
  ""width"": 区域宽度,
  ""height"": 区域高度,
  ""tileSetName"": ""地形类型名称""
}}

支持的操作类型：
- ""fill_terrain"": 用指定地形填充矩形区域

规则：
- 坐标不能超出地图范围
- tileSetName 必须是上面列出的可用地形类型的精确名称
- 如果用户的请求不清楚或不可行，在 message 中解释原因，operations 为空数组
- 如果用户只是聊天而不是编辑请求，正常回复在 message 中，operations 为空数组

回复示例：
```json
{{
  ""message"": ""已将地图左上角 10x10 区域填充为水面。"",
  ""operations"": [
    {{
      ""type"": ""fill_terrain"",
      ""x"": 0,
      ""y"": 0,
      ""width"": 10,
      ""height"": 10,
      ""tileSetName"": ""Water""
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
                return ("AI 返回了空响应。", new List<MapOperation>());

            try
            {
                // Try to extract JSON from the response (AI might wrap it in markdown code blocks)
                string json = ExtractJson(aiResponse);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string message = "操作完成。";
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
