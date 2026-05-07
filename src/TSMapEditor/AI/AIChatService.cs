using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rampastring.Tools;
using TSMapEditor.AI.Operations;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.Mutations;
using TSMapEditor.Rendering;
using TSMapEditor.UI;

namespace TSMapEditor.AI
{
    /// <summary>
    /// The main AI chat service that coordinates the provider, parser, and map operator.
    /// Handles the full flow: user message → AI API → parse response → execute operations.
    /// 
    /// Important threading model:
    /// - HTTP calls to AI run on a background thread (Task.Run)
    /// - Map mutations MUST run on the main thread (MonoGame game loop)
    /// - Pending operations are queued and executed via Update() on the main thread
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

        // Thread-safe pending operations queue
        private readonly object pendingLock = new object();
        private PendingAIResult pendingResult;

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
        /// The currently selected map region (set by AISelectionCursorAction).
        /// Null means no selection is active.
        /// </summary>
        public AISelection CurrentSelection { get; private set; }

        /// <summary>
        /// Sets the current selection region.
        /// </summary>
        public void SetSelection(int x, int y, int width, int height)
        {
            CurrentSelection = new AISelection(x, y, width, height);
        }

        /// <summary>
        /// Clears the current selection.
        /// </summary>
        public void ClearSelection()
        {
            CurrentSelection = null;
        }

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
        /// Must be called from the main game thread (e.g., in Update()).
        /// Executes any pending map operations that were parsed from AI responses.
        /// </summary>
        public void ProcessPendingOperations()
        {
            PendingAIResult result = null;

            lock (pendingLock)
            {
                if (pendingResult != null)
                {
                    result = pendingResult;
                    pendingResult = null;
                }
            }

            if (result == null)
                return;

            // Execute operations on the main thread
            string operationResult = string.Empty;
            if (result.Operations.Count > 0 && mapOperator != null)
            {
                try
                {
                    operationResult = mapOperator.ExecuteOperations(result.Operations);
                }
                catch (Exception ex)
                {
                    Logger.Log($"AIChatService: Failed to execute operations: {ex}");
                    operationResult = $"操作执行失败: {ex.Message}";
                }
            }

            // Combine message with operation results
            string fullMessage = result.Message;
            if (!string.IsNullOrEmpty(operationResult))
                fullMessage += "\n\n" + operationResult;

            // Add assistant message to history
            chatHistory.Add(ChatMessage.Assistant(fullMessage));

            MessageReceived?.Invoke(this, fullMessage);

            IsBusy = false;
            BusyStateChanged?.Invoke(this, false);
        }

        /// <summary>
        /// Sends a user message to the AI and processes the response.
        /// HTTP call runs async; map operations are deferred to main thread via ProcessPendingOperations().
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

            // Augment user message with selection context if present
            string augmentedMessage = userMessage;
            if (CurrentSelection != null)
            {
                augmentedMessage += $"\n[当前选区: 起点({CurrentSelection.X},{CurrentSelection.Y}) 大小{CurrentSelection.Width}x{CurrentSelection.Height}，请在此区域内操作]";
            }

            // Add augmented message to history (AI sees selection context)
            chatHistory.Add(ChatMessage.User(augmentedMessage));

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

                    // Call AI (this is the only part that needs to be async)
                    string aiResponse = await provider.ChatAsync(systemPrompt, chatHistory, cts.Token);

                    // Parse response (safe to do on background thread)
                    var (message, operations) = IntentParser.Parse(aiResponse);

                    // Queue the result for main thread execution
                    lock (pendingLock)
                    {
                        pendingResult = new PendingAIResult
                        {
                            Message = message,
                            Operations = operations
                        };
                    }
                }
                catch (OperationCanceledException)
                {
                    ErrorOccurred?.Invoke(this, "AI 请求超时（120秒）。请检查网络连接。");
                    IsBusy = false;
                    BusyStateChanged?.Invoke(this, false);
                }
                catch (Exception ex)
                {
                    Logger.Log($"AIChatService error: {ex}");
                    ErrorOccurred?.Invoke(this, $"AI 错误: {ex.Message}");
                    IsBusy = false;
                    BusyStateChanged?.Invoke(this, false);
                }
                finally
                {
                    cts.Dispose();
                }
            });
        }

        private class PendingAIResult
        {
            public string Message { get; set; }
            public List<MapOperation> Operations { get; set; }
        }
    }
}
