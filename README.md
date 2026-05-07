# SmartAlert

一个 AI 驱动的红色警戒系列地图编辑器，基于 [WorldAlteringEditor](https://github.com/CnCNet/WorldAlteringEditor) fork 开发。

## 🎯 核心特性

- **AI 自然语言编辑** — 用中文/英文描述地图需求，AI 理解并修改地图
- **人机混合模式** — 传统手工编辑 + AI 辅助，任何节点可互相干预
- **双模 AI 交互** — 聊天面板（全局操作）+ 选区指令（局部精修）
- **多游戏支持** — RA2 / YR / TS 及各种 Mod（心灵终结等）

## 🏗️ 技术栈

| 项目 | 选择 |
|------|------|
| 基座 | WorldAlteringEditor (WAE) fork, yr 分支 |
| 语言 | C# / .NET 8 |
| 渲染 | MonoGame / DirectX11 |
| 平台 | Windows 桌面应用 |
| AI | Anthropic Claude / OpenAI GPT / 自定义 API |
| 许可证 | GPL v3 |

## 📋 开发阶段

| 阶段 | 内容 | 状态 |
|------|------|------|
| P0 | Fork WAE，跑通编译，能打开 MO 地图 | ✅ 完成 |
| P1 | AI 聊天面板 + 基础地形生成/修改 | 未开始 |
| P2 | 选区指令模式 + 单位/建筑/资源放置 | 未开始 |
| P3 | 触发器生成、平衡性分析、模板系统 | 未开始 |

## 🚀 快速开始

### 前置要求

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- DirectX11 兼容 GPU（建议 2GB+ VRAM）
- 红色警戒2 / 心灵终结游戏安装

### 首次设置

```bash
# 1. 安装 MonoGame Content Builder 工具
dotnet tool install --global dotnet-mgcb --version 3.8.3
```

### 编译和运行

```bash
# 编译核心编辑器
dotnet build src/TSMapEditor/TSMapEditor.csproj --configuration Release

# 运行编辑器
dotnet run --project src/TSMapEditor/TSMapEditor.csproj --configuration Release
```

启动后在界面中设置游戏目录路径（如 `D:\Game\MentalOmega\`），然后即可打开地图文件进行编辑。

## 📝 文档

- [设计文档](docs/superpowers/specs/2026-04-21-smartalert-design.md)
- [WAE 用户手册](docs/Manual.md)
- [WAE 贡献指南](docs/Contributing.md)

## 📄 许可证

GPL v3（继承自 WAE）。所有衍生代码必须开源。
