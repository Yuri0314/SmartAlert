using System.Collections.Generic;

namespace TSMapEditor.AI
{
    /// <summary>
    /// Represents a single message in an AI chat conversation.
    /// Supports text messages (user/assistant/system) and tool-related messages.
    /// </summary>
    public class ChatMessage
    {
        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }

        /// <summary>
        /// The role of the message sender: "user", "assistant", "system", or "tool".
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// The text content of the message. May be null for assistant messages with tool_calls.
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// Tool calls requested by the assistant. Only set for assistant messages.
        /// </summary>
        public List<ToolCallInfo> ToolCalls { get; set; }

        /// <summary>
        /// The tool_call_id this message is responding to. Only set for tool messages.
        /// </summary>
        public string ToolCallId { get; set; }

        public static ChatMessage User(string content) => new ChatMessage("user", content);
        public static ChatMessage Assistant(string content) => new ChatMessage("assistant", content);

        /// <summary>
        /// Creates an assistant message that contains tool calls (no text content).
        /// </summary>
        public static ChatMessage AssistantWithToolCalls(List<ToolCallInfo> toolCalls)
        {
            return new ChatMessage("assistant", null) { ToolCalls = toolCalls };
        }

        /// <summary>
        /// Creates a tool result message.
        /// </summary>
        public static ChatMessage ToolResult(string toolCallId, string result)
        {
            return new ChatMessage("tool", result) { ToolCallId = toolCallId };
        }
    }

    /// <summary>
    /// Represents a tool call requested by the AI.
    /// </summary>
    public class ToolCallInfo
    {
        public string Id { get; set; }
        public string FunctionName { get; set; }
        public string Arguments { get; set; } // JSON string
    }

    /// <summary>
    /// Response from the AI provider, which can be either text or tool calls.
    /// </summary>
    public class ChatResponse
    {
        public string TextContent { get; set; }
        public List<ToolCallInfo> ToolCalls { get; set; }
        public bool HasToolCalls => ToolCalls != null && ToolCalls.Count > 0;
    }
}
