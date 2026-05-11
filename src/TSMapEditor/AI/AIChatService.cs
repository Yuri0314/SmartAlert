using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rampastring.Tools;
using TSMapEditor.Models;
using TSMapEditor.Mutations;
using TSMapEditor.Rendering;
using TSMapEditor.UI;

namespace TSMapEditor.AI
{
    /// <summary>
    /// The main AI chat service with Agent Loop architecture.
    /// 
    /// Flow:
    /// 1. User sends message → background thread calls LLM with tool definitions
    /// 2. If LLM returns tool_calls → queue for main thread execution
    /// 3. Main thread executes tools via ProcessPendingOperations()
    /// 4. Results fed back to LLM → loop until LLM returns text
    /// 
    /// Threading model:
    /// - HTTP calls to AI run on a background thread (Task.Run)
    /// - Map mutations MUST run on the main thread (MonoGame game loop)
    /// - Communication via pendingToolCalls/pendingToolResults queues
    /// </summary>
    public class AIChatService
    {
        private AIConfig config;
        private IAIProvider provider;
        private ToolExecutor toolExecutor;
        private readonly List<ChatMessage> chatHistory = new List<ChatMessage>();

        private Map map;
        private TheaterGraphics theaterGraphics;
        private MutationManager mutationManager;
        private IMutationTarget mutationTarget;

        // Thread-safe communication between background AI thread and main thread
        private readonly object syncLock = new object();
        private AgentState agentState = AgentState.Idle;
        private List<ToolCallInfo> pendingToolCalls;           // Background → Main: tools to execute
        private List<(string id, string result)> pendingToolResults; // Main → Background: tool results
        private string pendingFinalMessage;                    // Background → Main: final text response
        private string pendingError;                           // Background → Main: error message
        private readonly ManualResetEventSlim toolResultsReady = new ManualResetEventSlim(false);

        private const int MaxAgentIterations = 30;

        /// <summary>
        /// Event raised when the AI sends a text response.
        /// </summary>
        public event EventHandler<string> MessageReceived;

        /// <summary>
        /// Event raised when a tool is being executed (for progress display).
        /// </summary>
        public event EventHandler<string> ToolProgressUpdate;

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
        /// The currently selected map region.
        /// </summary>
        public AISelection CurrentSelection { get; private set; }

        public void SetSelection(int x, int y, int width, int height)
        {
            CurrentSelection = new AISelection(x, y, width, height);
        }

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
                toolExecutor = new ToolExecutor(map, theaterGraphics, mutationManager, mutationTarget);
            else
                toolExecutor = null;
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
        /// Processes pending tool executions and feeds results back to the agent.
        /// </summary>
        public void ProcessPendingOperations()
        {
            List<ToolCallInfo> toolCallsToExecute = null;

            lock (syncLock)
            {
                // Check for final message (agent loop completed)
                if (pendingFinalMessage != null)
                {
                    string message = pendingFinalMessage;
                    pendingFinalMessage = null;
                    agentState = AgentState.Idle;

                    chatHistory.Add(ChatMessage.Assistant(message));
                    MessageReceived?.Invoke(this, message);
                    IsBusy = false;
                    BusyStateChanged?.Invoke(this, false);
                    return;
                }

                // Check for errors
                if (pendingError != null)
                {
                    string error = pendingError;
                    pendingError = null;
                    agentState = AgentState.Idle;

                    ErrorOccurred?.Invoke(this, error);
                    IsBusy = false;
                    BusyStateChanged?.Invoke(this, false);
                    return;
                }

                // Check for pending tool calls
                if (agentState == AgentState.WaitingForToolExecution && pendingToolCalls != null)
                {
                    toolCallsToExecute = pendingToolCalls;
                    pendingToolCalls = null;
                }
            }

            // Execute tools OUTSIDE the lock (mutations can be slow)
            if (toolCallsToExecute != null)
            {
                var results = new List<(string id, string result)>();
                foreach (var tc in toolCallsToExecute)
                {
                    ToolProgressUpdate?.Invoke(this, $"🔧 {tc.FunctionName}...");
                    string result = toolExecutor.Execute(tc.FunctionName, tc.Arguments);
                    results.Add((tc.Id, result));
                    ToolProgressUpdate?.Invoke(this, $"  {result}");
                    Logger.Log($"Tool {tc.FunctionName} → {result}");
                }

                // Send results back to background thread
                lock (syncLock)
                {
                    pendingToolResults = results;
                    agentState = AgentState.ToolResultsReady;
                    toolResultsReady.Set();
                }
            }
        }

        /// <summary>
        /// Sends a user message and starts the agent loop.
        /// </summary>
        public void SendMessage(string userMessage)
        {
            if (IsBusy) return;
            if (string.IsNullOrWhiteSpace(userMessage)) return;

            if (!IsConfigured)
            {
                ErrorOccurred?.Invoke(this, "AI 服务未配置。请先在设置中填入 API 端点、Key 和模型名。");
                return;
            }

            chatHistory.Add(ChatMessage.User(userMessage));

            IsBusy = true;
            BusyStateChanged?.Invoke(this, true);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(300));

            Task.Run(async () =>
            {
                try
                {
                    await RunAgentLoop(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    lock (syncLock) { pendingError = "AI 请求超时（300秒）。请检查网络连接。"; }
                }
                catch (Exception ex)
                {
                    Logger.Log($"AIChatService agent loop error: {ex}");
                    lock (syncLock) { pendingError = $"AI 错误: {ex.Message}"; }
                }
                finally
                {
                    cts.Dispose();
                }
            });
        }

        /// <summary>
        /// The agent loop: call LLM → execute tools → feed results → repeat.
        /// Runs on a background thread.
        /// </summary>
        private async Task RunAgentLoop(CancellationToken ct)
        {
            string systemPrompt = BuildSystemPrompt();
            var tools = ToolDefinitions.GetAllTools();

            for (int iteration = 0; iteration < MaxAgentIterations; iteration++)
            {
                ct.ThrowIfCancellationRequested();

                Logger.Log($"Agent iteration {iteration + 1}/{MaxAgentIterations}");

                var response = await provider.ChatWithToolsAsync(systemPrompt, chatHistory, tools, ct);

                if (response.HasToolCalls)
                {
                    // AI wants to call tools
                    // Add assistant message with tool calls to history
                    chatHistory.Add(ChatMessage.AssistantWithToolCalls(response.ToolCalls));

                    // Queue tool calls for main thread execution
                    lock (syncLock)
                    {
                        pendingToolCalls = response.ToolCalls;
                        agentState = AgentState.WaitingForToolExecution;
                        toolResultsReady.Reset();
                    }

                    // Wait for main thread to execute tools and return results
                    toolResultsReady.Wait(ct);

                    // Get results and add to history
                    List<(string id, string result)> results;
                    lock (syncLock)
                    {
                        results = pendingToolResults;
                        pendingToolResults = null;
                        agentState = AgentState.RunningAgentLoop;
                    }

                    if (results != null)
                    {
                        foreach (var (id, result) in results)
                        {
                            chatHistory.Add(ChatMessage.ToolResult(id, result));
                        }
                    }

                    // Continue loop — next iteration will call LLM again with tool results
                }
                else
                {
                    // AI returned text — agent loop complete
                    string finalMessage = response.TextContent ?? "操作已完成。";
                    lock (syncLock) { pendingFinalMessage = finalMessage; }
                    return;
                }
            }

            // Max iterations reached
            lock (syncLock) { pendingFinalMessage = $"操作已完成（达到最大迭代次数 {MaxAgentIterations}）。"; }
        }

        private string BuildSystemPrompt()
        {
            return @"你是 SmartAlert 地图编辑器的 AI 助手，帮助用户编辑红色警戒2/尤里的复仇(Mental Omega mod)的地图。

你可以通过调用工具来编辑地图。每次调用工具后，你会收到执行结果。根据结果决定下一步操作。

工作流程：
1. 先调用 get_map_info 了解地图尺寸和现状
2. 根据用户需求逐步调用工具
3. 完成后用简短中文告诉用户你做了什么

地图设计指南：
- 对战地图出生点应对称分布，位于地图边缘（如 northwest, southeast）
- 用 create_plateau 创建中央高地增加战术深度
- 用 draw_road 连接出生点和地图中央
- 每个出生点附近（偏移5-10%）放 1-2 片矿石（place_ore），不要和出生点重叠
- 用 fill_terrain 的 dark_grass/rough_grass 让地形不单调
- 用 place_trees 装饰空旷区域
- 创建新地图时记得调用 set_map_name 起名

位置说明：
- position 参数使用方位词: center, north, south, east, west, northwest, northeast, southwest, southeast
- 也可以用 x_pct/y_pct 百分比指定精确位置(0=最左/最上, 100=最右/最下)
- 代码会自动将位置转换为等距坐标，你不需要计算坐标";
        }

        private enum AgentState
        {
            Idle,
            RunningAgentLoop,
            WaitingForToolExecution,
            ToolResultsReady,
        }
    }
}
