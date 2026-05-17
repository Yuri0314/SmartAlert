# AGENTS.md

本文件为 AI 编码助手提供本项目的上下文和指导。

## 项目概述

**SmartAlert** 是一个 AI 驱动的红色警戒系列地图编辑器，面向红色警戒2 / 尤里的复仇及心灵终结（Mental Omega）等 Mod。基于 **[WorldAlteringEditor (WAE)](https://github.com/CnCNet/WorldAlteringEditor) fork** 开发，在 WAE 成熟的编辑和渲染引擎之上，加入 AI 自然语言交互能力。

### 核心理念

用户可以**同时**使用传统手工编辑工具（继承自 WAE）**和** AI 自然语言指令来创建和编辑地图。人和 AI 可以在任何节点互相干预——没有固定的"先 AI 后人"或反过来的顺序。

### AI 交互模式

1. **聊天模式** — 持续对话面板，用户用自然语言描述地图编辑需求。支持多轮对话，适合全局性的复杂操作。
2. **选区模式** — 用户用鼠标在地图上框选一个区域，然后输入指令，AI 只修改选中区域。适合精准的局部编辑。

## 架构

```
┌──────────────────────────────────────────────────┐
│                 UI 层 (WAE 界面)                   │
│  ┌─────────────────┐  ┌────────────────────────┐ │
│  │  AI 聊天面板     │  │  选区指令输入框         │ │
│  └────────┬────────┘  └───────────┬────────────┘ │
├───────────┼────────────────────────┼──────────────┤
│           ▼        AI 中间层       ▼              │
│  ┌─────────────────────────────────────────────┐ │
│  │  AI Provider (Anthropic Codex / OpenAI GPT) │ │
│  │  意图解析器 (自然语言 → 结构化操作指令)        │ │
│  │  地图操作器 (操作指令 → WAE 地图数据修改)      │ │
│  └─────────────────────────────────────────────┘ │
├──────────────────────────────────────────────────┤
│              WAE 核心层 (复用)                     │
│  地图数据模型 / 渲染引擎 / 文件 I/O / 编辑工具    │
└──────────────────────────────────────────────────┘
```

## 开发阶段

| 阶段 | 目标 | 状态 |
|------|------|------|
| P0 | Fork WAE，编译通过，能打开心灵终结地图 | ✅ 完成 |
| P1 | 加入 AI 聊天面板 + 基础地形生成/修改 | 未开始 |
| P2 | 加入选区指令模式 + 单位/建筑/资源放置 | 未开始 |
| P3 | 触发器生成、平衡性分析、模板系统等高级功能 | 未开始 |

## 编译和运行命令

```bash
# 前置：安装 MonoGame Content Builder 工具（首次编译前需要）
dotnet tool install --global dotnet-mgcb --version 3.8.3

# 编译核心编辑器（不编译 MapEditorLauncher，它依赖外部更新器）
dotnet build src/TSMapEditor/TSMapEditor.csproj --configuration Release

# 运行编辑器
dotnet run --project src/TSMapEditor/TSMapEditor.csproj --configuration Release
```

**注意：** 不要编译整个解决方案 `TSMapEditor.sln`，因为 `MapEditorLauncher` 项目依赖外部 `Rampastring.Updater.dll`（WAE 的自动更新器），我们不需要它。

## 技术栈

| 项目 | 选择 |
|------|------|
| 基座 | WorldAlteringEditor (WAE) fork，yr 分支 |
| 语言 | C# / .NET 8 |
| 渲染 | MonoGame / DirectX11 |
| 平台 | Windows 桌面应用 |
| AI 接口 | Anthropic Codex、OpenAI GPT、自定义（兼容 OpenAI 格式） |
| 许可证 | GPL v3（继承自 WAE） |

## 关键依赖

- WAE 上游主仓库（官方）：https://github.com/CnCNet/WorldAlteringEditor
- WAE 原作者仓库：https://github.com/Rampastring/WorldAlteringEditor
- .NET 8 SDK（当前使用 8.0.419）
- MonoGame Content Builder (mgcb) 3.8.3
- DirectX11 兼容 GPU

## 配置机制

- **设置文件：** `MapEditorSettings.ini`（运行时从 `Environment.CurrentDirectory` 加载）
- **核心设置类：** `src/TSMapEditor/Settings/UserSettings.cs`
- **游戏路径设置：** `UserSettings.Instance.GameDirectory`（在编辑器启动界面输入）
- **默认设置模板：** `src/TSMapEditor/Config/DefaultSettings.ini`

## 游戏相关路径

- 心灵终结安装目录：`D:\Game\MentalOmega\`
- MO 地图文件：`D:\Game\MentalOmega\MapsMO\Standard\*.map`
- FinalAlert2 MO 编辑器：`D:\Game\MentalOmega\Map Editor\`
- 地图渲染器：`D:\Game\MentalOmega\Map Renderer\`

## 已知问题

- **MO 地图加载警告：** 打开心灵终结地图时会报 "massive number of errors"，因为 MO 有大量自定义单位/建筑/触发器，WAE yr 分支只认识原版 YR 的定义。地图仍可正常渲染和编辑，但自定义对象会缺失。后续需要让 WAE 正确读取 MO 的 `rulesmd.ini` / `artmd.ini`。

## 设计文档

- **完整设计文档**：`docs/superpowers/specs/2026-04-21-smartalert-design.md`
- **实现计划**：`docs/superpowers/plans/`（按阶段创建）

## 代码风格（继承自 WAE）

- 使用空格缩进（不用 Tab）
- 花括号另起新行
- `var` 仅在类型显而易见时使用；原始类型永远不用 `var`
- 局部变量/参数/私有字段用 `camelCase`，类/属性用 `PascalCase`
- 完整风格指南见 WAE 的 `docs/Contributing.md`

## 与 MOMapEditor 旧项目的关系

SmartAlert 是一个**全新项目**，不是旧的 `E:\Code\MOMapEditor` 项目的延续。旧项目是一个控制台原型，有基础的地图解析和 AI 生成功能。SmartAlert 完全替代它，基于 WAE 的全功能编辑器构建。旧项目的 AI Provider 代码（`IAIProvider`、`AnthropicProvider`、`OpenAIProvider`）可能会被迁移和适配。
