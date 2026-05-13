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

你可以通过调用工具来编辑地图。每次调用工具后，你会收到执行结果。根据结果决定下一步操作。
当前场景: {theaterName}

=== 严格操作顺序（必须按此顺序执行）===
1. 调用 get_map_info 了解地图尺寸和可用素材
2. 调用 set_map_name 命名地图
3. 调用 fill_terrain 铺设基础地形（使用 get_map_info 返回的可用地面类型）
4. 调用 create_plateau 创建高地（放在 center）
5. 调用 set_spawn_point 设置所有出生点
6. 调用 place_ore 放置矿石（必须在出生点设置之后！）
7. 调用 draw_road 修建道路
8. 调用 place_trees / fill_terrain 装饰（远离出生点）
9. 调用 place_decorations 少量散布装饰物（仅在远离出生点的区域，密度用sparse）

=== 2v2 对战地图布局参考 ===
出生点分布（对角对称）：
- 玩家1: x_pct=15, y_pct=15 (左上)
- 玩家2: x_pct=85, y_pct=85 (右下)
- 玩家3: x_pct=85, y_pct=15 (右上)
- 玩家4: x_pct=15, y_pct=85 (左下)

=== 1v1 对战地图布局参考 ===
- 玩家1: x_pct=20, y_pct=20
- 玩家2: x_pct=80, y_pct=80

=== 基地空间规则（极其重要）===
- 每个出生点周围必须留出足够空旷的空间供玩家展开基地（至少12格半径内不放装饰物和树木）
- 树木和装饰物只放在地图的公共区域（中间、边缘），绝对不要堆在出生点附近
- 装饰物默认用 sparse 密度，除非用户明确要求密集

=== 矿石放置规则（极其重要）===
- 每个出生点旁边放 1-2 片矿石
- 矿石位置必须用 x_pct/y_pct 指定，与出生点偏移 8-12%
- 例如：出生点在 x_pct=15,y_pct=15 → 矿石放在 x_pct=25,y_pct=15 和 x_pct=15,y_pct=25
- 绝对禁止在出生点的相同位置放矿石！

=== 场景设计指南（{theaterName}）===
{theaterGuide}

=== 位置说明 ===
- position 参数使用方位词: center, north, south, east, west, northwest, northeast, southwest, southeast
- 推荐使用 x_pct/y_pct 百分比精确指定位置(0=最左/最上, 100=最右/最下)
- 代码会自动将位置转换为等距坐标，你不需要计算坐标

=== 重要：fill_terrain 的 terrain 参数 ===
- 你可以使用 get_map_info 返回的「可用地面类型」中的任何名称作为 terrain 参数
- 也可以使用快捷名: grass, dark_grass, rough_grass, sand, pavement, snow, ice

完成后用简短中文告诉用户你做了什么。";
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

                _ => @"- 以草地为基础地形（grass）
- 用 dark_grass/rough_grass 在局部区域增加变化
- 用 create_plateau 在 center 创建高地增加战术深度
- 用 draw_road 连接出生点和地图中央
- 用 place_trees 在非出生点、非道路区域装饰
- 可用 Farm Crops 等特殊地形增加乡村感"
            };
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
