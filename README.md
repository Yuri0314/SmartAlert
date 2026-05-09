# SmartAlert

一个 AI 驱动的红色警戒系列地图编辑器，基于 [WorldAlteringEditor](https://github.com/CnCNet/WorldAlteringEditor) fork 开发。

An AI-powered map editor for the Red Alert series, forked from [WorldAlteringEditor](https://github.com/CnCNet/WorldAlteringEditor).

## 🎯 核心特性 / Core Features

### 🤖 AI 自然语言编辑 / AI Natural Language Editing
- 用中文/英文描述地图编辑需求，AI 理解并自动执行修改
- Type map editing commands in natural language, AI understands and applies changes
- 支持放置单位、建筑、地形、矿石等多种操作
- 快捷键 `Ctrl+Shift+A` 打开 AI 聊天面板

### 🎯 AI 选区操作 / AI Selection
- `Ctrl+Shift+S` 框选地图区域
- 对选中区域执行自然语言操作（如"种满矿石"、"散布树木"）

### 🎮 Mental Omega 完整支持 / Mental Omega Support
- 自动从 MO 的 MIX 文件中提取 rules/art/AI 控制数据
- 完整加载 MO 的所有单位、建筑和图像资源
- **零游戏目录污染** — 所有提取的文件存放在编辑器目录

### 🌐 多语言支持 / Internationalization
- 中英文自由切换（设置面板选择）
- 1735 个 MO 单位/建筑的中文名称翻译
- AI 界面文本完全国际化

### 📦 Mod 选择器 / Mod Selector
- 主菜单可选加载 **原版 RA2/YR** 或 **Mental Omega**
- 独立配置文件体系，互不干扰
- 选择自动保存，下次启动恢复

## 🚀 快速开始 / Quick Start

### 环境要求
- .NET 8.0 SDK
- Windows 10/11

### 构建运行
```bash
git clone https://github.com/Yuri0314/SmartAlert.git
cd SmartAlert
dotnet build src/TSMapEditor/TSMapEditor.csproj -c Release
```

### 运行
```bash
cd src/TSMapEditor/bin/Release/net8.0-windows
./WorldAlteringEditor.exe
```

### 配置 AI 功能
1. 启动编辑器，打开地图
2. `Ctrl+Shift+A` 打开 AI 面板
3. 点击 "Settings/设置"，配置：
   - **API Endpoint**: LLM API 地址（支持 OpenAI 格式）
   - **API Key**: API 密钥
   - **Model**: 模型名称（如 `gpt-4o`、`deepseek-chat`）

## 💡 AI 使用示例 / AI Usage Examples

| 输入指令 | 效果 |
|---------|------|
| `在地图中间放3辆天启坦克` | 放置 3 个 APOC 单位 |
| `在(50,50)建一个苏联基地` | 放置建造场+电厂+兵营等 |
| `Ctrl+Shift+S` 框选 → `种满矿石` | 选区内填充矿石 |
| `把这个区域的高度设为5` | 修改选区地形高度 |
| `散布一些树木，密度中等` | 按 35% 密度放置树木 |
| `Place 5 Apocalypse tanks at (30,40)` | English commands work too |

## 🏗 架构 / Architecture

```
SmartAlert/
├── src/TSMapEditor/
│   ├── AI/                    # AI 核心模块
│   │   ├── AIChatService.cs   # AI 聊天服务
│   │   ├── AISettingsWindow.cs # AI 设置窗口
│   │   └── Operations/        # 操作解析与执行
│   │       ├── IntentParser.cs # 意图解析（NL→JSON）
│   │       ├── MapOperationExecutor.cs # 地图操作执行器
│   │       └── MapOperation.cs # 操作数据模型
│   ├── Config/
│   │   ├── Default/           # 原版 RA2/YR 配置
│   │   ├── MentalOmega/       # MO 专属配置
│   │   └── Translations/      # 翻译文件
│   │       └── zh-Hans/       # 简体中文
│   └── UI/
│       ├── AIChatPanel.cs     # AI 聊天面板
│       └── CursorActions/
│           └── AISelectionCursorAction.cs # AI 选区工具
```

## 📜 致谢 / Credits

- [WorldAlteringEditor (WAE)](https://github.com/CnCNet/WorldAlteringEditor) — 基础地图编辑器
- [Mental Omega](https://mentalomega.com/) — 红色警戒2最大的社区 Mod
- [Rampastring](https://github.com/Rampastring) — WAE 原作者

## 📄 License

本项目基于 WAE 的开源协议。详见 [LICENSE](LICENSE) 文件。
