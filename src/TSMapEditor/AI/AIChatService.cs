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

=== 最重要的规则：判断用户意图 ===
- 你只处理地图编辑相关的请求（如 生成地图、放树、铺草地、设置出生点 等）。
- 如果用户发送的内容与地图编辑无关（如闲聊、打招呼、问天气等），不要调用任何工具，直接回复：我是地图编辑助手，只能帮你编辑地图。试试说""生成一张2人对战地图""或""在地图中间放5棵树""吧！
- 如果不确定用户的意图，也不要调用工具，先用文字询问确认。

你可以通过调用工具来编辑地图。每次调用工具后，你会收到执行结果。根据结果决定下一步操作。
当前场景: {theaterName}

=== 严格操作顺序（生成完整地图时必须全部执行）===
1. 调用 get_map_info 了解地图尺寸和可用素材
2. 调用 set_map_name 命名地图（起一个有创意的英文名）
3. 铺设基础地形（必须混合多种地面！）：
   - 先用 fill_terrain scope=full_map 铺满主地形（如 grass）
   - 再用 fill_terrain scope=medium_patch/large_patch 在 3-5 个不同位置铺设变化地形（如 dark_grass, rough_grass, sand）
   - 目标：地面不能是单一颜色，必须有层次感
4. 创建地形高度变化（至少 1-2 个高地）：
   - 用 create_plateau 在地图中央或关键路口创建高地
   - 可在其他位置再创建 1-2 个小高地增加战术多样性
5. 设置所有出生点（必须全部设置！2人=2个，4人=4个）：
   - 逐个调用 set_spawn_point，不能遗漏任何一个
6. 放置矿石（每个出生点旁必须放 1-2 片！）：
   - 逐个为每个出生点旁放矿石，偏移量 5-8%
7. 修建道路（至少 2 条）：
   - 每个出生点到中央各一条路
   - 可选：出生点之间的对角路
8. 装饰树木（至少 4-6 片，分散在不同位置）：
   - 在地图四边、中间空地等位置分散放置
   - 远离出生点（至少 15% 距离）
9. 少量散布装饰物：
   - place_decorations 密度用 sparse，远离出生点

=== 质量检查（完成后自检）===
生成地图完成后，必须确认以下每一项：
- [ ] 所有出生点都已设置（数量正确）
- [ ] 每个出生点旁都有矿石（1-2 片）
- [ ] 地面至少使用了 2 种以上地面类型
- [ ] 至少有 1 个高地
- [ ] 每个出生点到中央都有道路
- [ ] 树木和装饰物远离出生点
如果有遗漏，立即补充！

=== 1v1 对战地图完整模板 ===
出生点分布（对角对称）：
- 玩家1: x_pct=20, y_pct=20 (左上)
- 玩家2: x_pct=80, y_pct=80 (右下)
矿石位置：
- 玩家1矿石A: x_pct=25, y_pct=20  矿石B: x_pct=20, y_pct=25
- 玩家2矿石A: x_pct=75, y_pct=80  矿石B: x_pct=80, y_pct=75
道路：
- 玩家1(20,20) → 中央(50,50)
- 玩家2(80,80) → 中央(50,50)
地形变化示例：
- fill_terrain(dark_grass, medium_patch, 30%, 70%)
- fill_terrain(rough_grass, medium_patch, 70%, 30%)
- fill_terrain(sand, small_patch, 50%, 50%) — 中央高地附近
树木位置（远离出生点的 6 个位置）：
- (10%, 50%) (50%, 10%) (90%, 50%) (50%, 90%) (35%, 65%) (65%, 35%)

=== 2v2 对战地图布局参考 ===
出生点分布（对角对称）：
- 玩家1: x_pct=15, y_pct=15 (左上)
- 玩家2: x_pct=85, y_pct=85 (右下)
- 玩家3: x_pct=85, y_pct=15 (右上)
- 玩家4: x_pct=15, y_pct=85 (左下)

=== 基地空间规则（极其重要）===
- 每个出生点周围必须留出足够空旷的空间供玩家展开基地（至少12格半径内不放装饰物和树木）
- 树木和装饰物只放在地图的公共区域（中间、边缘），绝对不要堆在出生点附近
- 装饰物默认用 sparse 密度，除非用户明确要求密集

=== 矿石放置规则（极其重要）===
- 每个出生点旁边必须放 1-2 片矿石，矿石要紧挨出生点！
- 矿石偏移量只需要 5-8%，不要太远！
- 绝对禁止在出生点的相同位置放矿石！
- 矿石半径用 5-7，不要太大

=== 场景设计指南（{theaterName}）===
{theaterGuide}

=== 位置说明 ===
- position 参数使用方位词: center, north, south, east, west, northwest, northeast, southwest, southeast
- 推荐使用 x_pct/y_pct 百分比精确指定位置(0=最左/最上, 100=最右/最下)
- 代码会自动将位置转换为等距坐标，你不需要计算坐标

=== 重要：fill_terrain 的 terrain 参数 ===
- 仅支持 LAT 地面类型: grass, dark_grass, rough_grass, sand, pavement, snow, ice
- 也可以使用 get_map_info 返回的可用地面类型中的全名

完成地图操作后用简短中文告诉用户你做了什么。非地图操作的请求请直接拒绝。";
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
    }
}
