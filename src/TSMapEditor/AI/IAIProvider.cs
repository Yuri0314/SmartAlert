using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TSMapEditor.AI
{
    /// <summary>
    /// Interface for AI service providers.
    /// Implementations send messages to an AI API and return the response.
    /// </summary>
    public interface IAIProvider
    {
        /// <summary>
        /// Sends a chat completion request to the AI service (text-only, no tools).
        /// </summary>
        Task<string> ChatAsync(string systemPrompt, List<ChatMessage> history, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a chat completion request with tool definitions.
        /// The AI may respond with text or tool calls.
        /// </summary>
        Task<ChatResponse> ChatWithToolsAsync(string systemPrompt, List<ChatMessage> history, List<ToolDefinition> tools, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Defines a tool that can be called by the AI.
    /// Follows the OpenAI function calling format.
    /// </summary>
    public class ToolDefinition
    {
        public string Name { get; set; }
        public string Description { get; set; }
        /// <summary>
        /// JSON Schema for the tool's parameters, as a pre-built dictionary.
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; }
    }
}
