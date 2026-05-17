using System.Collections.Generic;
using System.Reflection;
using TSMapEditor.AI;
using Xunit;

namespace TSMapEditor.Tests.AI
{
    public class OpenAICompatibleProviderTests
    {
        private ChatResponse InvokeParseResponse(string json)
        {
            var provider = new OpenAICompatibleProvider(new AIConfig { ApiEndpoint = "test", ApiKey = "test", ModelName = "test" });
            var method = typeof(OpenAICompatibleProvider).GetMethod("ParseResponse", BindingFlags.NonPublic | BindingFlags.Instance);
            return (ChatResponse)method.Invoke(provider, new object[] { json });
        }

        [Fact]
        public void ParseResponse_CapturesReasoningContent_WithToolCalls()
        {
            string json = @"
            {
                ""choices"": [
                    {
                        ""message"": {
                            ""role"": ""assistant"",
                            ""reasoning_content"": ""I should use the get_houses tool to find valid house names."",
                            ""tool_calls"": [
                                {
                                    ""id"": ""call_123"",
                                    ""type"": ""function"",
                                    ""function"": {
                                        ""name"": ""get_houses"",
                                        ""arguments"": ""{}""
                                    }
                                }
                            ]
                        }
                    }
                ]
            }";

            var response = InvokeParseResponse(json);

            Assert.True(response.HasToolCalls);
            Assert.Single(response.ToolCalls);
            Assert.Equal("get_houses", response.ToolCalls[0].FunctionName);
            Assert.Equal("I should use the get_houses tool to find valid house names.", response.ReasoningContent);
            Assert.Null(response.TextContent);
        }

        [Fact]
        public void ParseResponse_CapturesReasoningContent_WithTextResponse()
        {
            string json = @"
            {
                ""choices"": [
                    {
                        ""message"": {
                            ""role"": ""assistant"",
                            ""reasoning_content"": ""The user just said hello. I will reply."",
                            ""content"": ""Hello! How can I help you?""
                        }
                    }
                ]
            }";

            var response = InvokeParseResponse(json);

            Assert.False(response.HasToolCalls);
            Assert.Equal("Hello! How can I help you?", response.TextContent);
            Assert.Equal("The user just said hello. I will reply.", response.ReasoningContent);
        }

        [Fact]
        public void ParseResponse_HandlesMissingReasoningContentGracefully()
        {
            string json = @"
            {
                ""choices"": [
                    {
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""This model does not provide reasoning content.""
                        }
                    }
                ]
            }";

            var response = InvokeParseResponse(json);

            Assert.False(response.HasToolCalls);
            Assert.Equal("This model does not provide reasoning content.", response.TextContent);
            Assert.Null(response.ReasoningContent);
        }

        [Fact]
        public void BuildMessagePayloads_IncludesReasoningContentForAssistant_WhenPresent()
        {
            var history = new List<ChatMessage>
            {
                ChatMessage.User("User message"),
                ChatMessage.Assistant("Assistant reply", "Thinking process...")
            };

            var payloads = OpenAICompatibleProvider.BuildMessagePayloads("System prompt", history);

            Assert.Equal(3, payloads.Count); // System + 2 history

            var assistantPayload = payloads[2];
            Assert.Equal("assistant", assistantPayload["role"]);
            Assert.Equal("Assistant reply", assistantPayload["content"]);
            Assert.True(assistantPayload.ContainsKey("reasoning_content"));
            Assert.Equal("Thinking process...", assistantPayload["reasoning_content"]);
        }

        [Fact]
        public void BuildMessagePayloads_OmitsReasoningContent_WhenNullOrEmpty()
        {
            var history = new List<ChatMessage>
            {
                ChatMessage.Assistant("Assistant reply without reasoning")
            };

            var payloads = OpenAICompatibleProvider.BuildMessagePayloads("System prompt", history);

            Assert.Equal(2, payloads.Count);

            var assistantPayload = payloads[1];
            Assert.Equal("assistant", assistantPayload["role"]);
            Assert.Equal("Assistant reply without reasoning", assistantPayload["content"]);
            Assert.False(assistantPayload.ContainsKey("reasoning_content"));
        }

        [Fact]
        public void BuildMessagePayloads_ToolResultMessages_DoNotContainReasoningContent()
        {
            var history = new List<ChatMessage>
            {
                ChatMessage.ToolResult("call_123", "Tool success")
            };

            var payloads = OpenAICompatibleProvider.BuildMessagePayloads("System prompt", history);

            Assert.Equal(2, payloads.Count);

            var toolPayload = payloads[1];
            Assert.Equal("tool", toolPayload["role"]);
            Assert.Equal("Tool success", toolPayload["content"]);
            Assert.Equal("call_123", toolPayload["tool_call_id"]);
            Assert.False(toolPayload.ContainsKey("reasoning_content"));
        }

        [Fact]
        public void BuildMessagePayloads_AssistantWithToolCalls_IncludesReasoningContent()
        {
            var toolCalls = new List<ToolCallInfo> { new ToolCallInfo { Id = "call_123", FunctionName = "test", Arguments = "{}" } };
            var history = new List<ChatMessage>
            {
                ChatMessage.AssistantWithToolCalls(toolCalls, "Deciding to use test tool.")
            };

            var payloads = OpenAICompatibleProvider.BuildMessagePayloads("System prompt", history);

            var assistantPayload = payloads[1];
            Assert.Equal("assistant", assistantPayload["role"]);
            Assert.Null(assistantPayload["content"]);
            Assert.True(assistantPayload.ContainsKey("reasoning_content"));
            Assert.Equal("Deciding to use test tool.", assistantPayload["reasoning_content"]);
            Assert.True(assistantPayload.ContainsKey("tool_calls"));
        }
    }
}
