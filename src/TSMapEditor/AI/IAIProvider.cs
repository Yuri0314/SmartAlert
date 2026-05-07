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
        /// Sends a chat completion request to the AI service.
        /// </summary>
        /// <param name="systemPrompt">The system prompt that guides AI behavior.</param>
        /// <param name="history">The conversation history.</param>
        /// <param name="cancellationToken">Token to cancel the request.</param>
        /// <returns>The AI's response text.</returns>
        Task<string> ChatAsync(string systemPrompt, List<ChatMessage> history, CancellationToken cancellationToken = default);
    }
}
