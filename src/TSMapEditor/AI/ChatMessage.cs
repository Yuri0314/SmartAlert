namespace TSMapEditor.AI
{
    /// <summary>
    /// Represents a single message in an AI chat conversation.
    /// </summary>
    public class ChatMessage
    {
        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }

        /// <summary>
        /// The role of the message sender: "user" or "assistant".
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// The text content of the message.
        /// </summary>
        public string Content { get; set; }

        public static ChatMessage User(string content) => new ChatMessage("user", content);
        public static ChatMessage Assistant(string content) => new ChatMessage("assistant", content);
    }
}
