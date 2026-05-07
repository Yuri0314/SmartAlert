using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rampastring.Tools;
using TSMapEditor.AI.Operations;
using TSMapEditor.Models;
using TSMapEditor.Mutations;
using TSMapEditor.Rendering;
using TSMapEditor.UI;

namespace TSMapEditor.AI
{
    /// <summary>
    /// The main AI chat service that coordinates the provider, parser, and map operator.
    /// Handles the full flow: user message → AI API → parse response → execute operations.
    /// </summary>
    public class AIChatService
    {
        private AIConfig config;
        private IAIProvider provider;
        private AIMapOperator mapOperator;
        private readonly List<ChatMessage> chatHistory = new List<ChatMessage>();

        private Map map;
        private TheaterGraphics theaterGraphics;
        private MutationManager mutationManager;
        private IMutationTarget mutationTarget;

        /// <summary>
        /// Event raised when the AI sends a response (message text).
        /// </summary>
        public event EventHandler<string> MessageReceived;

        /// <summary>
        /// Event raised when the AI is processing (true = busy, false = idle).
        /// </summary>
        public event EventHandler<bool> BusyStateChanged;

        /// <summary>
        /// Event raised when an error occurs.
        /// </summary>
        public event EventHandler<string> ErrorOccurred;

        /// <summary>
        /// Whether the service is currently processing a request.
        /// </summary>
        public bool IsBusy { get; private set; }

        /// <summary>
        /// Whether the service is configured and ready to use.
        /// </summary>
        public bool IsConfigured => config?.IsConfigured ?? false;

        /// <summary>
        /// The current configuration.
        /// </summary>
        public AIConfig Config => config;

        /// <summary>
        /// The conversation history.
        /// </summary>
        public IReadOnlyList<ChatMessage> ChatHistory => chatHistory.AsReadOnly();

        public AIChatService()
        {
            config = AIConfig.Load();
            if (config.IsConfigured)
                provider = new OpenAICompatibleProvider(config);
        }

        /// <summary>
        /// Updates the map-related dependencies. Called when a new map is loaded.
        /// </summary>
        public void SetMapContext(Map map, TheaterGraphics theaterGraphics,
            MutationManager mutationManager, IMutationTarget mutationTarget)
        {
            this.map = map;
            this.theaterGraphics = theaterGraphics;
            this.mutationManager = mutationManager;
            this.mutationTarget = mutationTarget;

            if (map != null && theaterGraphics != null && mutationManager != null && mutationTarget != null)
                mapOperator = new AIMapOperator(map, theaterGraphics, mutationManager, mutationTarget);
            else
                mapOperator = null;
        }

        /// <summary>
        /// Updates the AI configuration and recreates the provider.
        /// </summary>
        public void UpdateConfig(AIConfig newConfig)
        {
            config = newConfig;
            config.Save();

            if (config.IsConfigured)
                provider = new OpenAICompatibleProvider(config);
            else
                provider = null;
        }

        /// <summary>
        /// Clears the chat history.
        /// </summary>
        public void ClearHistory()
        {
            chatHistory.Clear();
        }

        /// <summary>
        /// Sends a user message to the AI and processes the response.
        /// This runs asynchronously; results are delivered via events.
        /// </summary>
        public void SendMessage(string userMessage)
        {
            if (IsBusy)
                return;

            if (string.IsNullOrWhiteSpace(userMessage))
                return;

            if (!IsConfigured)
            {
                ErrorOccurred?.Invoke(this, "AI 服务未配置。请先在设置中填入 API 端点、Key 和模型名。");
                return;
            }

            // Add user message to history
            chatHistory.Add(ChatMessage.User(userMessage));

            // Start async processing
            IsBusy = true;
            BusyStateChanged?.Invoke(this, true);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

            Task.Run(async () =>
            {
                try
                {
                    // Build system prompt with map context
                    string mapContext = (map != null && theaterGraphics != null)
                        ? MapContextBuilder.BuildContext(map, theaterGraphics)
                        : "（当前没有打开地图）";

                    string systemPrompt = IntentParser.BuildSystemPrompt(mapContext);

                    // Call AI
                    string aiResponse = await provider.ChatAsync(systemPrompt, chatHistory, cts.Token);

                    // Parse response
                    var (message, operations) = IntentParser.Parse(aiResponse);

                    // Execute operations on the map
                    string operationResult = string.Empty;
                    if (operations.Count > 0 && mapOperator != null)
                    {
                        operationResult = mapOperator.ExecuteOperations(operations);
                    }

                    // Combine message with operation results
                    string fullMessage = message;
                    if (!string.IsNullOrEmpty(operationResult))
                        fullMessage += "\n\n" + operationResult;

                    // Add assistant message to history
                    chatHistory.Add(ChatMessage.Assistant(fullMessage));

                    MessageReceived?.Invoke(this, fullMessage);
                }
                catch (OperationCanceledException)
                {
                    ErrorOccurred?.Invoke(this, "AI 请求超时（120秒）。请检查网络连接。");
                }
                catch (Exception ex)
                {
                    Logger.Log($"AIChatService error: {ex}");
                    ErrorOccurred?.Invoke(this, $"AI 错误: {ex.Message}");
                }
                finally
                {
                    IsBusy = false;
                    BusyStateChanged?.Invoke(this, false);
                    cts.Dispose();
                }
            });
        }
    }
}
