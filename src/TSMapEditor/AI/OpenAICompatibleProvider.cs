using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Rampastring.Tools;

namespace TSMapEditor.AI
{
    /// <summary>
    /// AI provider that works with any OpenAI-compatible API.
    /// Supports function calling (tool use) for the Agent architecture.
    /// Compatible with: Alibaba Bailian (DashScope), DeepSeek, Ollama, OpenAI, etc.
    /// </summary>
    public class OpenAICompatibleProvider : IAIProvider
    {
        private readonly HttpClient httpClient;
        private readonly AIConfig config;

        public OpenAICompatibleProvider(AIConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(300);
        }

        /// <summary>
        /// Simple text-only chat (no tools). Backward compatible.
        /// </summary>
        public async Task<string> ChatAsync(string systemPrompt, List<ChatMessage> history, CancellationToken cancellationToken = default)
        {
            var response = await ChatWithToolsAsync(systemPrompt, history, null, cancellationToken);
            return response.TextContent ?? "";
        }

        /// <summary>
        /// Chat with optional tool definitions. Returns structured response with text or tool calls.
        /// </summary>
        public async Task<ChatResponse> ChatWithToolsAsync(string systemPrompt, List<ChatMessage> history,
            List<ToolDefinition> tools, CancellationToken cancellationToken = default)
        {
            if (!config.IsConfigured)
                throw new InvalidOperationException("AI service is not configured. Please set API endpoint, key, and model name in settings.");

            string endpoint = config.ApiEndpoint.TrimEnd('/');
            if (!endpoint.EndsWith("/chat/completions"))
                endpoint += "/chat/completions";

            // Build messages array
            var messages = new List<object>();
            messages.Add(new { role = "system", content = systemPrompt });

            foreach (var msg in history)
            {
                if (msg.Role == "tool")
                {
                    // Tool result message
                    messages.Add(new { role = "tool", content = msg.Content, tool_call_id = msg.ToolCallId });
                }
                else if (msg.Role == "assistant" && msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                {
                    // Assistant message with tool calls
                    var toolCalls = new List<object>();
                    foreach (var tc in msg.ToolCalls)
                    {
                        toolCalls.Add(new
                        {
                            id = tc.Id,
                            type = "function",
                            function = new { name = tc.FunctionName, arguments = tc.Arguments }
                        });
                    }
                    messages.Add(new { role = "assistant", content = msg.Content, tool_calls = toolCalls });
                }
                else
                {
                    // Regular user/assistant message
                    messages.Add(new { role = msg.Role, content = msg.Content });
                }
            }

            // Build request body
            var requestObj = new Dictionary<string, object>
            {
                ["model"] = config.ModelName,
                ["messages"] = messages,
                ["temperature"] = 0.7,
                ["max_tokens"] = 16384
            };

            // Add tools if provided
            if (tools != null && tools.Count > 0)
            {
                var toolDefs = new List<object>();
                foreach (var tool in tools)
                {
                    toolDefs.Add(new
                    {
                        type = "function",
                        function = new
                        {
                            name = tool.Name,
                            description = tool.Description,
                            parameters = tool.Parameters
                        }
                    });
                }
                requestObj["tools"] = toolDefs;
            }

            string jsonBody = JsonSerializer.Serialize(requestObj);
            Logger.Log($"AI Request to {endpoint}, model={config.ModelName}, messages={messages.Count}, tools={tools?.Count ?? 0}");

            // Retry logic for transient errors (network issues, 429, 500, 502, 503)
            const int maxRetries = 2;
            HttpResponseMessage response = null;
            string responseBody = null;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attempt > 0)
                {
                    int delayMs = 1000 * (int)Math.Pow(2, attempt - 1); // 1s, 2s
                    Logger.Log($"AI API retry {attempt}/{maxRetries} after {delayMs}ms...");
                    await Task.Delay(delayMs, cancellationToken);
                }

                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                    request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");

                    response = await httpClient.SendAsync(request, cancellationToken);
                    responseBody = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                        break; // Success, exit retry loop

                    int statusCode = (int)response.StatusCode;
                    bool isRetryable = statusCode == 429 || statusCode >= 500;

                    if (!isRetryable || attempt == maxRetries)
                    {
                        Logger.Log($"AI API error: {response.StatusCode} - {responseBody}");
                        string friendlyMsg = statusCode switch
                        {
                            401 => "API Key 无效，请在设置中检查。",
                            429 => "API 请求过于频繁，请稍后再试。",
                            >= 500 => $"AI 服务端错误 ({statusCode})，已重试 {attempt} 次。",
                            _ => $"AI API 返回错误 {statusCode}"
                        };
                        throw new HttpRequestException(friendlyMsg);
                    }

                    Logger.Log($"AI API returned {statusCode}, will retry...");
                }
                catch (HttpRequestException) when (attempt < maxRetries)
                {
                    // Network error, will retry
                    Logger.Log($"AI API network error on attempt {attempt + 1}, will retry...");
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < maxRetries)
                {
                    // Individual request timeout (not user cancel), will retry
                    Logger.Log($"AI API request timeout on attempt {attempt + 1}, will retry...");
                }
            }

            return ParseResponse(responseBody);
        }

        private ChatResponse ParseResponse(string responseBody)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;
                var choice = root.GetProperty("choices")[0];
                var message = choice.GetProperty("message");

                var result = new ChatResponse();

                // Check for tool_calls
                if (message.TryGetProperty("tool_calls", out var toolCallsElement) &&
                    toolCallsElement.ValueKind == JsonValueKind.Array &&
                    toolCallsElement.GetArrayLength() > 0)
                {
                    result.ToolCalls = new List<ToolCallInfo>();
                    foreach (var tc in toolCallsElement.EnumerateArray())
                    {
                        var func = tc.GetProperty("function");
                        result.ToolCalls.Add(new ToolCallInfo
                        {
                            Id = tc.GetProperty("id").GetString(),
                            FunctionName = func.GetProperty("name").GetString(),
                            Arguments = func.GetProperty("arguments").GetString()
                        });
                    }
                    Logger.Log($"AI Response: {result.ToolCalls.Count} tool call(s): {string.Join(", ", result.ToolCalls.ConvertAll(t => t.FunctionName))}");
                }

                // Get text content (may be null when tool_calls are present)
                if (message.TryGetProperty("content", out var contentElement) &&
                    contentElement.ValueKind == JsonValueKind.String)
                {
                    result.TextContent = contentElement.GetString();
                }

                if (!result.HasToolCalls && string.IsNullOrEmpty(result.TextContent))
                {
                    throw new InvalidOperationException("AI returned neither text content nor tool calls.");
                }

                Logger.Log($"AI Response received, text={result.TextContent?.Length ?? 0} chars, toolCalls={result.ToolCalls?.Count ?? 0}");
                return result;
            }
            catch (JsonException ex)
            {
                Logger.Log($"Failed to parse AI response: {ex.Message}");
                throw new InvalidOperationException($"Failed to parse AI response: {responseBody}", ex);
            }
        }
    }
}
