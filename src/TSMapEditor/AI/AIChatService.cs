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
        private CancellationTokenSource currentCts;            // For cancel button support

        private const int MaxAgentIterations = 30;
        private const int MaxHistoryMessages = 40;             // Sliding window to prevent token overflow
        private string lastUserMessage;                        // For quality checker player count detection

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
        /// Cancels the current AI operation if one is in progress.
        /// </summary>
        public void CancelCurrentOperation()
        {
            if (!IsBusy) return;

            try
            {
                currentCts?.Cancel();
                // Also unblock the background thread if it's waiting for tool results
                toolResultsReady.Set();
            }
            catch (ObjectDisposedException) { }

            Logger.Log("AIChatService: Operation cancelled by user.");
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

                    // Run programmatic quality checks before declaring completion
                    RunQualityChecks();

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

            lastUserMessage = userMessage;
            chatHistory.Add(ChatMessage.User(userMessage));

            // Trim history to prevent token overflow (sliding window)
            TrimHistory();

            IsBusy = true;
            BusyStateChanged?.Invoke(this, true);

            currentCts = new CancellationTokenSource(TimeSpan.FromSeconds(300));
            var cts = currentCts;

            Task.Run(async () =>
            {
                try
                {
                    await RunAgentLoop(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    string msg = cts.IsCancellationRequested && !cts.Token.WaitHandle.WaitOne(0)
                        ? "操作已被用户取消。"
                        : "AI 请求超时（300秒）。请检查网络连接。";
                    lock (syncLock)
                    {
                        pendingFinalMessage = msg;
                        agentState = AgentState.Idle;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"AIChatService agent loop error: {ex}");
                    lock (syncLock) { pendingError = $"AI 错误: {ex.Message}"; }
                }
                finally
                {
                    currentCts = null;
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

                    // 使用封装好的方法：把表单交给主线程，并等待主线程返回结果
                    List<(string id, string result)> results = ExecuteOnMainThreadAndWait(response.ToolCalls, ct);

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

        /// <summary>
        /// 把任务移交给主线程执行，并挂起当前后台线程，直到主线程返回结果。
        /// 这是一个纯手工打造的跨线程 Invoke 方法。
        /// </summary>
        private List<(string id, string result)> ExecuteOnMainThreadAndWait(List<ToolCallInfo> toolCalls, CancellationToken ct)
        {
            // 1. 把任务装进共享抽屉，通知主线程
            lock (syncLock)
            {
                pendingToolCalls = toolCalls;
                agentState = AgentState.WaitingForToolExecution;
                toolResultsReady.Reset(); // 挂起红灯
            }

            // 2. 彻底休眠，直到主线程干完活把灯变绿
            toolResultsReady.Wait(ct);

            // 3. 活干完了，从共享抽屉里把结果拿出来
            List<(string id, string result)> results;
            lock (syncLock)
            {
                results = pendingToolResults;
                pendingToolResults = null; // 清空抽屉
                agentState = AgentState.RunningAgentLoop; // 恢复状态
            }

            return results;
        }

        private string BuildSystemPrompt()
        {
            string theaterName = map.LoadedTheaterName ?? map.TheaterName ?? "TEMPERATE";
            string theaterGuide = GetTheaterGuide(theaterName);

            return $@"你是 SmartAlert 地图编辑器的 AI 助手，帮助用户编辑红色警戒2/尤里的复仇(Mental Omega mod)的地图。

=== 意图判断 ===
- 你只处理地图编辑相关的请求。
- 非地图请求（闲聊等），不调用工具，直接回复：我是地图编辑助手，只能帮你编辑地图。试试说""生成一张2人对战地图""或""在地图中间放5棵树""吧！
- 不确定意图时，先用文字询问确认。

当前场景: {theaterName}
每次工具执行后你会收到执行结果和当前地图状态，根据这些信息决定下一步操作。

=== 设计原则（不是步骤！自由发挥，但遵守原则）===

**地形**
- 调用 get_map_info 了解可用素材
- 地面必须混合多种类型（如 grass + dark_grass + rough_grass + sand），不能是单一颜色
- 用 create_plateau 制造高低差增加战术深度
- fill_terrain 只支持 LAT 地面类型: grass, dark_grass, rough_grass, sand, pavement, snow, ice

**玩家**
- 出生点分布要对称公平，对角分布效果最佳
- 每个出生点旁边放 1-2 片矿石（偏移 5-8%，不在同一位置）
- 出生点周围保持空旷（不放树木和装饰物），让玩家能展开基地

**交通与装饰**
- 用道路连接出生点和中央区域
- 树木分散在地图各处（远离出生点）
- 装饰物用 sparse 密度
- 自由安排位置和数量，让地图看起来自然而不是机械排列

**创意**
- 给地图起一个有创意的英文名
- 不要拘泥于模板，根据地图尺寸和用户要求灵活设计
- 可以在任何合理位置创建多个不同大小的高地
- 地形变化要自然过渡（比如高地旁边铺 sand 模拟泥沙）

=== 出生点参考位置 ===
2人: 对角分布，如 (20%,20%) 和 (80%,80%)
4人: 四角分布，如 (15%,15%)(85%,85%)(85%,15%)(15%,85%)

=== 场景指南（{theaterName}）===
{theaterGuide}

=== 位置说明 ===
- 推荐使用 x_pct/y_pct 百分比指定位置(0=最左/最上, 100=最右/最下)
- 也可使用方位词: center, north, south, east, west, northwest, northeast, southwest, southeast

完成后用简短中文告诉用户你做了什么。非地图操作请直接拒绝。";
        }

        private string GetTheaterGuide(string theaterName)
        {
            return theaterName.ToUpperInvariant() switch
            {
                "SNOW" => @"- 基础地形使用雪地（snow）而非草地
- 树木使用针叶林/雪松（FRTREE, SNTREE 等）
- 水面区域可使用冰面（ice）
- 建筑相对稀疏，体现寒冷荒凉感
- 地形装饰使用 get_map_info 返回的可用地面类型",

                "URBAN" or "NEWURBAN" => @"- 大面积使用 pavement 铺设地面
- 密集放置城市建筑（用 place_building）
- 用 draw_road 构建道路网络（多条相交道路）
- 树木稀疏，仅在公园/绿化带区域
- 体现城市密集、工业化的感觉",

                "DESERT" => @"- 基础地形使用沙地（sand）而非草地
- 植被极少，只在绿洲附近放少量树
- 地形相对平坦，高地较矮
- 利用岩石和沙丘增加地形变化
- 体现干燥、广袤的沙漠感",

                "LUNAR" => @"- 月球表面地形，极其荒凉
- 没有任何植被和水面
- 地形以灰色月面为主
- 利用高低差制造陨石坑效果
- 建筑使用科技/太空风格",

                _ => @"- 以草地(grass)为主，但必须混合其他地面：
  * 在低洼/阴暗区域铺 dark_grass
  * 在干燥/踩踏区域铺 rough_grass
  * 在高地顶部或路边铺 sand（模拟泥沙）
- 高地建议：中央1个medium高地 + 侧翼1-2个small高地
- 道路连接所有出生点到中央（路宽4格）
- 树木分散在地图四周和中间空地（至少6片，密度medium）
- 装饰物用sparse密度散布在远离出生点的区域"
            };
        }

        private enum AgentState
        {
            Idle,
            RunningAgentLoop,
            WaitingForToolExecution,
            ToolResultsReady,
        }

        /// <summary>
        /// Runs programmatic quality checks after the AI agent loop completes.
        /// Auto-fixes missing spawn points, ore, and terrain diversity.
        /// </summary>
        private void RunQualityChecks()
        {
            if (map == null || toolExecutor == null)
                return;

            try
            {
                int expectedPlayers = MapQualityChecker.DetectExpectedPlayers(lastUserMessage);
                if (expectedPlayers == 0)
                    return; // Not a map generation request, skip checks

                var checker = new MapQualityChecker(map, theaterGraphics);
                var fixes = checker.Check(expectedPlayers);

                if (fixes.Count == 0)
                {
                    Logger.Log("MapQualityChecker: All checks passed.");
                    return;
                }

                ToolProgressUpdate?.Invoke(this, $"🔍 质量检查发现 {fixes.Count} 个问题，自动修复中...");

                foreach (var fix in fixes)
                {
                    ToolProgressUpdate?.Invoke(this, $"  [自动补充] {fix.Description}");
                    string result = toolExecutor.Execute(fix.ToolName, fix.ArgumentsJson);
                    Logger.Log($"QualityFix: {fix.ToolName} → {result}");
                }

                ToolProgressUpdate?.Invoke(this, $"✅ 质量检查完成，已修复 {fixes.Count} 个问题");
            }
            catch (Exception ex)
            {
                Logger.Log($"MapQualityChecker error: {ex.Message}");
                // Quality checks are best-effort, don't fail the whole operation
            }
        }

        /// <summary>
        /// Trims chat history to prevent token overflow.
        /// Keeps the most recent MaxHistoryMessages messages.
        /// </summary>
        private void TrimHistory()
        {
            if (chatHistory.Count <= MaxHistoryMessages)
                return;

            // Keep only the most recent messages
            int removeCount = chatHistory.Count - MaxHistoryMessages;
            chatHistory.RemoveRange(0, removeCount);
            Logger.Log($"TrimHistory: removed {removeCount} old messages, {chatHistory.Count} remaining");
        }
    }
}
