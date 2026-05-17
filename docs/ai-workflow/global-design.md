# SmartAlert AI Workflow Global Design

## 1. 项目背景

SmartAlert 是一个面向红色警戒 2、尤里的复仇以及 Mental Omega 等 Mod 的 AI 地图编辑器。项目基于开源地图编辑器 WorldAlteringEditor fork 构建，希望在成熟的地图数据模型、渲染、文件 I/O 和传统编辑工具之上，引入 AI 自然语言能力。

用户的真实目标不是做一个只能自动生成地图的玩具，而是做一个可和手工编辑并行使用的地图编辑器：

- 用户可以让 AI 从零生成一张完整地图。
- 用户可以打开已有地图，让 AI 对局部或全局做调优。
- 用户可以随时手工干预，也可以随时让 AI 继续接手。
- AI 需要支持标准对战图、生存挑战图、剧情图、塔防图、装饰美化图等不同意图。

Mental Omega 本身包含大量自定义单位、建筑、地形对象和地图风格，因此 SmartAlert 的 AI 系统不能只面向原版 RA2/YR 的简单对象集设计。AI 必须理解当前地图所使用的规则、素材、对象归属和玩家展开需求。

## 2. 当前项目状态

当前仓库已经不是纯 P0，也不是只有 UI 设想。代码中已经存在较多 AI 功能原型：

- AI 聊天面板：`src/TSMapEditor/UI/AIChatPanel.cs`
- AI 服务与 agent loop：`src/TSMapEditor/AI/AIChatService.cs`
- OpenAI-compatible provider：`src/TSMapEditor/AI/OpenAICompatibleProvider.cs`
- 工具定义：`src/TSMapEditor/AI/ToolDefinitions.cs`
- 工具执行器：`src/TSMapEditor/AI/ToolExecutor.cs`
- 坐标解析：`src/TSMapEditor/AI/PositionResolver.cs`
- 选区工具：`src/TSMapEditor/UI/CursorActions/AISelectionCursorAction.cs`
- AI 专用 mutations：`src/TSMapEditor/Mutations/Classes/AI*.cs`
- 地图质量检查：`src/TSMapEditor/AI/MapQualityChecker.cs`

当前可以认为项目处于：

> AI 地图编辑原型已基本落地，但可靠性、可解释性、可回归验证和工作流状态管理不足。

验证现状：

- `dotnet build src\TSMapEditor\TSMapEditor.csproj --configuration Release /p:EnableMGCBItems=false` 可以通过，说明 C# 主体源码能编译。
- 不禁用 MGCB 的正常构建遇到 MonoGame 内容管线访问拒绝问题，主要是 `Content\.mgcontent` 和 shader `.xnb` 文件访问问题。
- `dotnet test src\TSMapEditor.Tests\TSMapEditor.Tests.csproj --no-build --configuration Debug` 能启动测试框架，但当前没有发现可用测试。
- `docs/superpowers/specs/` 和 `docs/superpowers/plans/` 原本不存在，说明设计文档和实现计划没有真正随代码同步维护。

## 3. 当前主要问题

### 3.1 系统缺少意图层

现在 AI 直接从用户自然语言跳到工具调用，例如 `place_trees`、`place_building`、`fill_terrain`。但地图编辑不是单纯的对象放置问题，关键在于用户意图。

同样是“出生点附近有树”，在不同地图类型里含义完全不同：

- 标准对战图：可能是严重问题，因为阻挡基地展开。
- 生存挑战图：可能是刻意设计，因为玩家需要清理森林开局。
- 剧情图：可能是视觉叙事的一部分。
- 美化已有地图：可能只需要警告，而不应该自动删除。

如果系统没有意图层，就只能把规则写死。规则写死后，AI 会变得僵硬；规则不写死，又会小问题很多。

### 3.2 工具边界混乱

当前工具基本是“直接改地图”的施工工具。AI 看到工具后，会直接执行地图修改。这样有几个问题：

- 没有明确的观察阶段。
- 没有明确的计划阶段。
- 没有明确的验收阶段。
- 没有明确的修复阶段。
- 工具语义过粗，例如 `place_unit` 实际偏载具，步兵没有单独工具。

一个稳定的 AI 编辑系统应该把工具分成不同职责，而不是把所有能力平铺给同一个 agent。

### 3.3 缺少任务全局状态

当前 agent 主要依赖聊天上下文和 tool result 继续推理。长任务做到第 5 步、第 8 步时，它可能忘记：

- 用户最初到底要的是标准图还是创意图。
- 哪些步骤已经完成。
- 哪些步骤待完成。
- 哪些问题已经发现但尚未修复。
- 哪些风险是用户明确允许的设计选择。
- 最近一次工具调用是否改变了地图对象数量。

这会导致 agent 越做越飘，后续步骤靠猜，而不是靠稳定的任务状态推进。

### 3.4 校验器太窄，且自动修复语义不清

`MapQualityChecker` 已经存在，但它目前更像一个有限补丁：

- 补出生点。
- 补矿。
- 检查简单地形多样性。
- 对出生点距离过近只记录日志。

它还没有覆盖：

- 出生点展开空间是否足够。
- 是否有单位、树、建筑阻挡基地部署。
- 资源是否公平。
- 路径是否可达。
- owner 是否合法。
- 对象类型是否符合用户意图。
- 用户是否明确允许某些风险。

更重要的是，校验器现在缺少 “Error / Warning / Intentional” 的判断模型。很多问题不应该一律自动修复，也不应该一律放行。

### 3.5 选区模式尚未形成真正执行约束

代码中有选区 UI，用户可以框选区域，也能在聊天面板里看到选区信息。但从当前链路看，选区更多是展示状态，还没有成为工具坐标解析和执行范围的硬约束。

理想行为：

- 用户框选区域后，AI 工具的 `x_pct/y_pct` 应该默认映射到选区内部。
- `clear_area`、`place_trees`、`fill_terrain` 等局部操作不能越过选区。
- AI 回复和工作流状态中应明确当前是否有 active selection。

### 3.6 归属和对象类型存在隐性风险

当前 owner 解析存在危险兜底：如果找不到用户指定的 owner，可能返回第一个 house。这会导致单位或建筑归属混乱。

另外，`AIPlaceObjectMutation` 内部支持 Infantry，但工具层没有暴露清晰的 `place_infantry`。用户说“步兵”“士兵”“巡逻队”时，模型容易混用载具工具或搜索错误对象。

### 3.7 缺少可复现测试

现在很多小问题只能通过人工观察地图发现。没有 fixture 测试，就无法把问题沉淀为回归保护。

例如以下问题都应该有测试：

- 标准对战图出生点 10 格内被树堵住，应报告 Error。
- 创意挑战图允许出生点附近有树，但应报告 Intentional 或 Warning。
- 不存在的 owner 应失败，不应默默 fallback。
- 选区操作不应影响选区外对象。
- 步兵、载具、建筑应走不同工具和不同校验。

## 4. 用户提出的关键想法

### 4.1 不希望规则限制过死

用户明确指出：有些情况下，出生点周围有树、建筑或障碍物可能是设计意图。系统不能永远禁止这些行为。

这意味着 SmartAlert 不能只做一个“标准对战地图生成器”，而应该支持不同地图意图和不同安全策略。

### 4.2 希望重分类和重组 agent 工具

当前工具较散，且大部分是直接施工工具。用户提出是否应该重新分类工具，让 agent 不再随意调用一堆低层地图修改工具。

这个方向是正确的。工具需要按工作流职责分层，而不是按代码实现平铺。

### 4.3 希望探索主 agent 与多 agent 系统

用户考虑过主 agent 加多个子 agent，或者类似多角色协作系统。这个方向长期有价值，但应建立在稳定的工具边界和工作流状态之上。

如果底层仍然没有意图层、校验层和任务状态，多 agent 只会更快地产生更多混乱。

### 4.4 希望 agent 随时查看任务全貌

用户提出需要一个工具，让 agent 在第 2 步、第 3 步、第 7 步、第 8 步时都能重新查看完整进度。

这个需求非常关键。它对应架构中的 “任务黑板” 或 “Workflow State”。它不是聊天记录，而是结构化任务账本。

## 5. 推荐总体架构

推荐将 SmartAlert AI 系统从：

```text
用户自然语言 -> LLM -> 直接工具调用 -> 地图修改
```

升级为：

```text
用户自然语言
  -> 意图识别
  -> 生成结构化计划
  -> 写入任务黑板
  -> 执行地图工具
  -> 运行策略化校验
  -> 修复或解释风险
  -> 更新任务黑板
```

核心是四个新概念：

1. `MapEditIntent`
2. `MapEditPlan`
3. `MapValidationPolicy`
4. `AIWorkflowState`

## 6. 地图意图模型

建议先支持以下 intent：

```text
BalancedSkirmish
CreativeSkirmish
SurvivalChallenge
TowerDefense
ScenarioStory
BeautifyExistingMap
LocalEdit
```

每个 intent 对规则的解释不同。

### BalancedSkirmish

标准对战图。默认最严格：

- 出生点周围必须可展开。
- 矿区要公平。
- 路径要可达。
- 建筑和装饰不能堵出生点。
- 随机装饰应远离玩家部署区。

### CreativeSkirmish

创意对战图。允许一定非标准设计：

- 可以有不完全公平的地形。
- 可以有出生点附近装饰。
- 但必须保留基本可玩性。
- 风险应以 Warning 形式说明。

### SurvivalChallenge

生存或挑战图：

- 可以故意压缩出生空间。
- 可以放置敌方单位、障碍、树阵。
- 系统不应硬删这些对象。
- 但应说明玩家展开空间被压缩。

### TowerDefense

塔防或防守图：

- 出生点和防御区域可以相对封闭。
- 进攻路线、敌人刷出点、防守阵地更重要。
- 标准对战公平性不是最高优先级。

### ScenarioStory

剧情图：

- 对象摆放可能服务叙事。
- 地图装饰和触发器比资源公平更重要。
- 校验器应更关注非法对象、越界和明显不可执行的地图结构。

### BeautifyExistingMap

已有地图美化：

- 不应大规模改动玩法结构。
- 应优先保护原有出生点、矿区、触发器和关键建筑。
- 装饰类操作应尽量局部、可回滚。

### LocalEdit

局部编辑：

- 应强绑定当前选区。
- 不应触碰选区外对象。
- 不应运行完整地图生成式的自动补全逻辑。

## 7. 工具重分类方案

### 7.1 观察工具

只读取地图，不修改地图。

建议工具：

```text
get_map_info
inspect_area
inspect_spawn_points
search_assets
get_object_counts
get_workflow_state
```

目的：

- 让 agent 先理解当前地图。
- 让 agent 在长任务中随时恢复全局上下文。
- 让后续 plan 和 validation 有数据依据。

### 7.2 计划工具

生成或更新结构化计划，不直接改地图。

建议工具：

```text
create_map_edit_plan
update_map_edit_plan
record_user_override
set_edit_intent
set_validation_policy
```

计划中应包含：

- 用户目标。
- 地图 intent。
- 玩家数。
- 风格。
- 允许的例外。
- 严格程度。
- 待执行步骤。
- 已知风险。

### 7.3 施工工具

只负责机械执行地图修改。

现有工具可以重组为：

```text
fill_terrain
create_plateau
draw_road
draw_river
place_building
place_vehicle
place_infantry
place_ore
place_trees
place_decorations
set_spawn_point
clear_area
set_map_name
```

关键调整：

- `place_unit` 应改名或语义限定为 `place_vehicle`。
- 新增 `place_infantry`。
- owner 必须从真实 house 列表中选择。
- 局部编辑时施工工具必须受 selection 约束。

### 7.4 验证工具

只检查，不直接修。

建议工具：

```text
validate_map
validate_spawn_safety
validate_resource_balance
validate_path_access
validate_selection_scope
validate_object_ownership
```

输出结构化 issue：

```json
{
  "severity": "Error",
  "code": "SpawnBlocked",
  "message": "Player 1 spawn has insufficient deployable space",
  "area": "spawn_1",
  "allowedByIntent": false,
  "suggestedFix": "clear_obstacles_near_spawn"
}
```

### 7.5 修复工具

根据验证结果做最小修复。

建议工具：

```text
apply_validation_fix
clear_spawn_obstacles
move_objects_outside_spawn_zone
normalize_owner
add_missing_ore
restore_selection_boundary
```

修复工具不应该自己决定是否修复，而应由 policy 和主控流程决定。

## 8. 任务黑板设计

任务黑板是 agent 随时可读写的结构化全局状态。它应成为长任务和多 agent 协作的核心。

建议数据模型：

```json
{
  "userGoal": "生成一张 4 人标准对战图，偏森林风格",
  "intent": "BalancedSkirmish",
  "policy": {
    "strictness": "Balanced",
    "spawnProtection": "Strict",
    "allowSpawnObstacles": false,
    "creativeOverrides": []
  },
  "currentPhase": "Execution",
  "completedSteps": [
    "读取地图信息",
    "生成地图计划",
    "设置 4 个出生点"
  ],
  "pendingSteps": [
    "铺设基础地形",
    "放置矿区",
    "添加道路",
    "添加树木和装饰",
    "运行质量检查"
  ],
  "knownRisks": [
    "东北出生点附近树木密度偏高"
  ],
  "validationIssues": [],
  "lastActions": [
    "set_spawn_point P1",
    "set_spawn_point P2"
  ],
  "activeSelection": null
}
```

建议工具：

```text
get_workflow_state
update_workflow_state
record_completed_step
record_validation_issue
record_user_override
get_pending_steps
```

任务黑板的收益：

- agent 不必翻聊天记录猜进度。
- 多 agent 可以通过同一个状态协作。
- 出错后可以复盘。
- 用户可以看到 AI 到底做了什么、为什么这么做。
- 后续可以保存为 JSON 日志，形成可复现证据。

## 9. 多 agent 方案评估

### 9.1 不建议立刻做完整多 agent

完整多 agent 会带来额外复杂度：

- 角色之间需要通信协议。
- 多个 agent 可能互相覆盖判断。
- 如果工具边界没整理好，会更快地产生混乱。
- 调试难度上升。

在当前阶段，直接上完整多 agent 风险较高。

### 9.2 推荐先做单 agent 多阶段

第一版用一个 agent，但分阶段执行：

```text
Planner 阶段：理解用户意图，创建 MapEditPlan
Executor 阶段：只执行施工工具
Critic 阶段：运行验证工具，输出 issues
Fixer 阶段：根据 policy 做最小修复
Reporter 阶段：总结结果、风险和保留的创作选择
```

这些阶段可以先由 `AIChatService` 控制，不需要真实启动多个 agent。

### 9.3 后续可演进为多 agent

当单 agent 多阶段稳定后，再拆：

```text
Orchestrator：主控，维护 workflow state
Designer：地图设计和玩法意图
Executor：工具调用和地图修改
Critic：质量检查和风险识别
Balance Analyst：资源、公平性、路径分析
Fixer：最小修复
```

所有 agent 通过 `AIWorkflowState` 共享任务状态，而不是靠互相聊天传递上下文。

## 10. 策略化校验模型

建议引入：

```text
MapValidationPolicy
MapValidationIssue
MapValidationSeverity
MapValidationOverride
```

Severity：

```text
Error
Warning
Info
Intentional
```

Policy 示例：

```text
Strict：标准对战图，自动修复高风险问题。
Balanced：默认模式，Error 自动修，Warning 询问或报告。
Creative：允许创意风险，但必须解释。
Scenario：剧情/挑战图，更多问题降级为 Warning 或 Intentional。
LocalOnly：只检查选区范围和局部副作用。
```

关键原则：

- 不是所有风险都应该禁止。
- 不是所有风险都应该自动修复。
- 用户明确表达的设计意图可以覆盖默认规则。
- 覆盖必须被记录到 workflow state。

例如：

```text
用户：做一张森林包围出生点的生存图。
系统：将 spawn_near_trees 从 Error 降级为 Intentional，但仍检查是否完全无法展开。
```

## 11. 推荐实现路线

### 阶段 A：建立工作流状态与计划模型

目标：

- 新增 `AIWorkflowState`。
- 新增 `MapEditIntent`。
- 新增 `MapEditPlan`。
- AI 每次任务开始先写入计划。
- 暴露 `get_workflow_state` 工具。

价值：

- 长任务不再丢失全局进度。
- 后续多 agent 有共享状态基础。

### 阶段 B：整理工具语义

目标：

- 将工具分类为观察、计划、施工、验证、修复。
- 新增 `place_infantry`。
- 将 `place_unit` 明确改为 vehicle 语义，或迁移为 `place_vehicle`。
- owner 必须从真实 house 列表中解析，失败就报错。
- 工具描述中减少模糊中文/乱码风险，尽量使用稳定英文 schema 和中文辅助说明。

价值：

- 减少单位混乱、owner 混乱。
- 降低 LLM 错用工具概率。

### 阶段 C：接入选区约束

目标：

- `AIWorkflowState` 记录 active selection。
- system prompt 明确当前选区。
- `PositionResolver` 支持 selection-relative percentage。
- 局部工具默认限制在选区内。
- 增加选区越界校验。

价值：

- 选区模式真正可用。
- 避免“只想改这里，结果改了全图”。

### 阶段 D：重构质量检查为策略化验证器

目标：

- 将 `MapQualityChecker` 从自动补丁扩展为结构化 validator。
- 输出 `MapValidationIssue`。
- 根据 intent 和 policy 判断 severity。
- 对 Error 执行自动修复；对 Warning 记录并报告；对 Intentional 保留并解释。

价值：

- 解决“不能太死，也不能太松”的核心矛盾。

### 阶段 E：建立 fixture 回归测试

目标：

- 不依赖真实 AI。
- 直接构造地图和工具调用。
- 测试 owner、对象类型、出生点阻挡、选区约束、policy override。

价值：

- 每个历史小问题都能变成测试。
- 后续 agent 修改代码时有边界。

### 阶段 F：再考虑真实多 agent

目标：

- 在单 agent 多阶段稳定后，拆出 Critic 或 Balance Analyst。
- 保持所有 agent 通过 workflow state 协作。

价值：

- 多 agent 增益用于复杂分析，而不是替代基础工程边界。

## 12. 第一轮推荐落地范围

建议第一轮不要做太大，聚焦以下最小闭环：

1. 新增 `AIWorkflowState` 内存对象。
2. 新增 `get_workflow_state` 工具。
3. 将当前选区写入 workflow state。
4. 修复 owner fallback：找不到 owner 时返回错误。
5. 新增 `place_infantry` 工具定义和执行分支。
6. 新增最小 `MapValidationIssue` 类型。
7. 给 owner 解析、intent policy、选区状态写最小测试。

这一步完成后，项目会从“AI 直接改图”变成“AI 带着全局状态改图”。它不会一次性解决全部地图质量问题，但会建立后续稳定迭代的地基。

## 13. 非目标

第一轮不建议做：

- 完整多 agent 并行系统。
- 完整地图平衡模拟器。
- 完整路径寻路分析。
- 完整触发器生成系统。
- 大规模 UI 重做。
- 一次性重写所有 AI 工具。

这些都可以后续做，但现在最重要的是建立可控、可验证、可复盘的工作流骨架。

## 14. 成功标准

第一轮成功后，应满足：

- AI 能随时通过工具查看当前任务全貌。
- 用户意图能被结构化记录。
- 选区信息不只是 UI 展示，而是进入执行上下文。
- 不存在的 owner 不再静默变成其他阵营。
- 步兵和载具工具语义分开。
- 至少有一组不依赖真实 AI 的自动测试。
- 后续任何“小问题”都能被写成 fixture 测试，再进入修复。

## 15. 总结判断

SmartAlert 当前的问题不是 AI 能力不够，而是 AI 编辑系统缺少工程化工作流：

- 缺意图层，所以规则不是太死就是太松。
- 缺工具边界，所以 agent 容易直接施工出错。
- 缺任务黑板，所以长任务容易丢全局。
- 缺策略化校验，所以无法区分错误、警告和创作意图。
- 缺测试，所以小问题无法沉淀。

推荐方向是：

> 先做单 agent 多阶段工作流，加上任务黑板和策略化验证；等这个闭环稳定后，再考虑真正的多 agent。

这条路线既保留创作自由，也能逐步压住反复出现的小问题。
