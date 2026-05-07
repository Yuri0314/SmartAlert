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
    /// Supports: Alibaba Bailian (DashScope), Xiaomi MIMO, DeepSeek, Ollama, OpenAI, etc.
    /// </summary>
    public class OpenAICompatibleProvider : IAIProvider
    {
        private readonly HttpClient httpClient;
        private readonly AIConfig config;

        public OpenAICompatibleProvider(AIConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(120);
        }

        public async Task<string> ChatAsync(string systemPrompt, List<ChatMessage> history, CancellationToken cancellationToken = default)
        {
            if (!config.IsConfigured)
                throw new InvalidOperationException("AI service is not configured. Please set API endpoint, key, and model name in settings.");

            string endpoint = config.ApiEndpoint.TrimEnd('/');
            if (!endpoint.EndsWith("/chat/completions"))
                endpoint += "/chat/completions";

            // Build messages array
            var messages = new List<RequestMessage>();
            messages.Add(new RequestMessage { Role = "system", Content = systemPrompt });

            foreach (var msg in history)
            {
                messages.Add(new RequestMessage { Role = msg.Role, Content = msg.Content });
            }

            var requestBody = new RequestBody
            {
                Model = config.ModelName,
                Messages = messages,
                Temperature = 0.7
            };

            string jsonBody = JsonSerializer.Serialize(requestBody);
            Logger.Log($"AI Request to {endpoint}, model={config.ModelName}, messages={messages.Count}");

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");

            HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Logger.Log($"AI API error: {response.StatusCode} - {responseBody}");
                throw new HttpRequestException($"AI API returned {(int)response.StatusCode}: {responseBody}");
            }

            try
            {
                var responseObj = JsonSerializer.Deserialize<ResponseBody>(responseBody);
                string content = responseObj?.Choices?[0]?.Message?.Content;

                if (string.IsNullOrEmpty(content))
                    throw new InvalidOperationException("AI returned an empty response.");

                Logger.Log($"AI Response received, length={content.Length}");
                return content;
            }
            catch (JsonException ex)
            {
                Logger.Log($"Failed to parse AI response: {ex.Message}");
                throw new InvalidOperationException($"Failed to parse AI response: {responseBody}", ex);
            }
        }

        // --- Request/Response DTOs ---

        private class RequestBody
        {
            [JsonPropertyName("model")]
            public string Model { get; set; }

            [JsonPropertyName("messages")]
            public List<RequestMessage> Messages { get; set; }

            [JsonPropertyName("temperature")]
            public double Temperature { get; set; }
        }

        private class RequestMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; }

            [JsonPropertyName("content")]
            public string Content { get; set; }
        }

        private class ResponseBody
        {
            [JsonPropertyName("choices")]
            public List<ResponseChoice> Choices { get; set; }
        }

        private class ResponseChoice
        {
            [JsonPropertyName("message")]
            public ResponseMessage Message { get; set; }
        }

        private class ResponseMessage
        {
            [JsonPropertyName("content")]
            public string Content { get; set; }
        }
    }
}
