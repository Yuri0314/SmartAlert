using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rampastring.Tools;
using TSMapEditor.AI.Workflow;
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
        private string pendingFinalReasoning;
        private string pendingError;                           // Background → Main: error message
        private readonly ManualResetEventSlim toolResultsReady = new ManualResetEventSlim(false);
        private CancellationTokenSource currentCts;            // For cancel button support

        private const int MaxAgentIterations = 80;
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
        public AIWorkflowState WorkflowState { get; } = new AIWorkflowState();

        public void SetSelection(int x, int y, int width, int height)
        {
            CurrentSelection = new AISelection(x, y, width, height);
            WorkflowState.ActiveSelection = $"({x},{y}) {width}x{height}";
        }

        public void ClearSelection()
        {
            CurrentSelection = null;
            WorkflowState.ActiveSelection = string.Empty;
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
                toolExecutor = new ToolExecutor(map, theaterGraphics, mutationManager, mutationTarget, WorkflowState, () => CurrentSelection);
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
                    string reasoning = pendingFinalReasoning;
                    pendingFinalMessage = null;
                    pendingFinalReasoning = null;
                    agentState = AgentState.Idle;

                    // Run programmatic quality checks before declaring completion
                    RunQualityChecks();

                    chatHistory.Add(ChatMessage.Assistant(message, reasoning));
                    MessageReceived?.Invoke(this, message);
                    IsBusy = false;
                    if (mutationManager != null) mutationManager.IsLocked = false;
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
                    if (mutationManager != null) mutationManager.IsLocked = false;
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
            Workflow.AIWorkflowIntentResolver.ApplyInitialState(WorkflowState, userMessage);
            chatHistory.Add(ChatMessage.User(userMessage));

            // Trim history to prevent token overflow (sliding window)
            TrimHistory();

            IsBusy = true;
            if (mutationManager != null) mutationManager.IsLocked = true;
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
                        pendingFinalReasoning = null;
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
                    chatHistory.Add(ChatMessage.AssistantWithToolCalls(response.ToolCalls, response.ReasoningContent));

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
                    lock (syncLock)
                    {
                        pendingFinalMessage = finalMessage;
                        pendingFinalReasoning = response.ReasoningContent;
                    }
                    return;
                }
            }

            // Max iterations reached
            lock (syncLock)
            {
                pendingFinalMessage = $"操作已完成（达到最大迭代次数 {MaxAgentIterations}）。";
                pendingFinalReasoning = null;
            }
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
            string codebook = toolExecutor.GetDynamicCodebook();

            return $@"你是 SmartAlert 地图编辑器的 AI 助手，帮助用户编辑红色警戒2/尤里的复仇(Mental Omega mod)的地图。

{BuildWorkflowProtocolPrompt()}

{BuildSelectionGuardrailPrompt()}

当前场景: {theaterName}
每次工具执行后你会收到执行结果和[当前地图状态]，其中包含建筑、树木、载具、步兵的实时数量。
如果发现数量异常变化（比如突然减少），说明可能发生了撤销操作，你需要根据当前实际状态调整后续计划。

=== 工具使用规范 ===
- place_unit / place_units 工具仅用于放置【载具】。
- place_infantry / place_infantries 工具仅用于放置【步兵/士兵】。
- 中立建筑（如 CAOILD/CAHOSP 等）通常应将所属方设为 Neutral。
- 阵营生产建筑（建造厂/兵工厂等）只在用户明确要求预置基地时才放。
- 防御建筑（炮塔/碉堡）需要指定正确的所属方（如 <Player @ A>）。
- 中立建筑分散放置在公共区域，不要堆在一起。

{BuildOwnerGuardrailPrompt()}

=== 出生点与障碍物规则 ===
不要将出生点附近的障碍物一律视为错误，需根据【意图】区分处理：
- BalancedSkirmish: 阻挡基地展开空间的障碍物通常是错误，需保持出生点周围 15+ 格平坦。
- CreativeSkirmish, SurvivalChallenge, TowerDefense, ScenarioStory: 出生点附近的树木、建筑或障碍物可能是设计刻意为之，但你应在回复中解释这是一种风险或设计选择。
- BeautifyExistingMap: 除非用户要求，否则避免破坏核心游戏区域（如出生点周围）。
- LocalEdit: 改动必须在概念上局限于用户选定的区域。

=== 地图生成基本参考 ===
（如果你确定意图需要从零生成平衡地图，请参考以下顺序，否则按需执行）
1. 设置出生点 (set_spawn_point) — 决定空间布局。不同玩家出生点之间至少 25% 地图宽度的距离。
2. 铺设地形 (fill_terrain) — 混合多种地面类型，使交错自然。
3. 创建高地 (create_plateau)
4. 画河流/道路 (draw_river/draw_road)
5. 放置矿石 (place_ore)
6. 放置建筑 (place_buildings)
7. 放置树木/装饰 (place_trees/place_decorations)
8. 设置地图名 (set_map_name)

1vN 塔防图参考：
- 防守方(1方)放角落/边缘，利用角落减少受攻面。进攻方(N方)集中在对侧。

地形美化参考：
- 必须混合 3+ 种地面类型。水岸边用沙地(sand)过渡。悬崖旁用碎石草地(rough_grass)过渡。

效率建议：
- 使用 place_buildings（批量）代替多次 place_building（单个）
- 使用 place_units / place_infantries 批量工具代替单个放置。
- 下方代码表中的代码可以直接使用，不在表中的用 search_units 搜索

{codebook}

=== 出生点参考位置 ===
2人对战: 对角分布 (15%,15%) 和 (85%,85%)
4人对战: 四角 (15%,15%)(85%,85%)(85%,15%)(15%,85%)
1v3塔防: 防守方 (15%,15%)，进攻方 (85%,50%)(70%,85%)(85%,85%)
1v2塔防: 防守方 (15%,15%)，进攻方 (85%,50%)(85%,85%)

=== 场景指南（{theaterName}）===
{theaterGuide}

=== 位置说明 ===
- 推荐使用 x_pct/y_pct 百分比指定位置(0=最左/最上, 100=最右/最下)
- 也可使用方位词: center, north, south, east, west, northwest, northeast, southwest, southeast

完成后用简短中文向用户总结你做了什么。请勿在回复中声称进行了活体验证或测试，除非你确实调用了相关的校验工具。非地图操作请直接拒绝。";
        }

        private static string BuildWorkflowProtocolPrompt()
        {
            return @"=== 意图判断与工作流协议 ===
在进行大量的地图生成或多步编辑前，必须先推断用户的意图（Intent）。
支持的意图包括：
- BalancedSkirmish (平衡对战)
- CreativeSkirmish (创意对战)
- SurvivalChallenge (生存挑战)
- TowerDefense (塔防)
- ScenarioStory (剧情战役)
- BeautifyExistingMap (现有地图美化)
- LocalEdit (局部编辑)

如果用户意图不明确且后续操作依赖于意图，请主动提问确认，不要自行猜测。
在进行任何建设前，始终调用 `get_map_info` 来检查当前地图资源。

--- 工作流状态管理协议 ---
你拥有两个工作流工具：`set_workflow_state`（写入）和 `get_workflow_state`（读取）。

**`set_workflow_state` 使用规则：**
- `set_workflow_state` 是唯一用于更新工作流状态的工具，不会修改地图数据。
- 在开始**多步骤**地图生成或编辑任务时，应在前期调用 `set_workflow_state` 设置：user_goal、intent、current_phase、pending_steps，以及可选的 known_risks。
- 在执行过程中，当发生**有意义的阶段转换**时更新 `set_workflow_state`（例如从 Terrain 阶段进入 Structures 阶段），而不是每次调用小工具前都更新。
- 不要在每一个工具调用前/后都调用 `set_workflow_state`——这会浪费 Token 且无意义。

**`get_workflow_state` 使用规则：**
- 在恢复中断的任务、连续调用多次工具后进度不明确时，或需要回顾当前任务上下文时，调用 `get_workflow_state`。
- `get_workflow_state` 中的 [Last Validation Findings] 仅代表上一次质量检查的结果，作为风险提示。这些问题在检查后可能已经被自动修复，请勿盲目反复尝试修复它们；你可以利用这些信息向用户解释风险或决定后续检查策略。

**严格禁止：**
- 不要发明或调用工具 schema 中不存在的工作流工具。只使用 `set_workflow_state` 和 `get_workflow_state`。
- 不要在回复中声称进行了验证、测试或质量检查，除非你确实调用了相应的校验工具并获得了结果。";
        }

        private static string BuildSelectionGuardrailPrompt()
        {
            return @"=== 选区与作用域规范 ===
- 当存在活跃选区（Active Selection）时，常规局部编辑工具接收到的坐标和百分比将自动作为【选区相对坐标】来解析。你无需手动换算。
- 当不存在活跃选区时，上述工具的坐标将回退为全地图相对或绝对坐标。
- 针对选区内的编辑需求，优先使用常规编辑工具（如 place_trees/place_ore/clear_area 等），这些工具会自动在选区内工作并避免溢出，无需你自己去计算和模拟选区边界。
- draw_road 和 draw_river 的起点与终点在有选区时同样是相对于选区的，但请注意，渲染出的路径宽度可能会轻微溢出选区边界，这是已知的限制。如果对边缘精度要求极高，请避免在选区边缘画宽路或向用户解释此限制。
- 绝对全局工具：set_spawn_point 和 set_map_name 永远作用于全图，不受选区约束。
- 非修改类工具：get_map_info, get_workflow_state, set_workflow_state, get_houses 等属于查询和控制工具，不会修改任何地形或选区内容。
- 严禁行为：不得在未实际调用任何校验工具的情况下，虚构并声称选区编辑已通过质量检查。

--- coordinate_scope 参数 ---
大多数局部编辑工具支持可选参数 `coordinate_scope`（值为 ""selection"" 或 ""global""）：
- 省略或 ""selection""（默认）：有选区时按选区相对坐标解释。
- ""global""：即使有活跃选区，也按全地图坐标解释百分比/方位。
当用户明确说「地图中心」「全图中心」「whole map center」或要求在全地图范围操作时，设置 coordinate_scope 为 ""global""。
当用户说「选区中心」「在选区里」或给出局部选定区域的指令时，省略 coordinate_scope 或设为 ""selection""。
不要为了模拟全局坐标而去清除选区——使用 coordinate_scope: ""global"" 即可。";
        }

        private static string BuildOwnerGuardrailPrompt()
        {
            return @"=== 所属方(Owner/Faction)规则 ===
- 如果不确定可用的所属方名称，先调用 `get_houses` 获取精确列表。
- owner 参数必须使用 `get_houses` 返回的精确 ININame，不要自行编造阵营名或使用缩写。
- 如果用户指定的所属方在 `get_houses` 结果中不存在，请告知用户并让用户从真实列表中选择。";
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
                    if (WorkflowState != null)
                        WorkflowState.ValidationIssues.Clear();
                    return;
                }

                if (WorkflowState != null)
                {
                    // These are findings from the last quality check pass.
                    var policy = TSMapEditor.AI.Validation.MapValidationPolicyResolver.Resolve(
                        WorkflowState.Intent, WorkflowState.UserGoal ?? lastUserMessage);
                    var issues = TSMapEditor.AI.Validation.MapQualityIssueConverter.FromQualityFixes(fixes, policy);
                    WorkflowState.ValidationIssues.Clear();
                    foreach (var issue in issues)
                    {
                        WorkflowState.ValidationIssues.Add(issue.ToSummary());
                    }
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
