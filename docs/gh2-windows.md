# Windows GH2 扩展

开发分支：`win`，基础提交 `d97933a`（创建分支时的最新 `origin/develop`）。
`SuperHelper.GH2` 是独立 GH2 插件；GH1 的 `SuperHelper.gha` 继续独立构建。

需要 Rhino 9、.NET 10，以及版本不低于 `9.0.26300.9401` 的 GH2。
Windows 接入位于 `win` 分支。`mac` 分支已接入共享界面和宿主接口并提供 Mac 平台适配，见 [GH2 Mac 兼容说明](gh2-mac.md)。

## 功能对应

| 功能 | GH2 接入 |
| --- | --- |
| 打开、关闭、左右停靠 | 原生 Eto 菜单、工具栏和分隔器 |
| Command、Viewport、Help 布局 | 复用 Rhino 原生工作区、独立的 GH2 面板 GUID |
| Rhino 命令、反馈、取点视口 | 复用原生命令栏和视口，单独处理 GH2 getter 的隐藏计时器 |
| Origin Help | GH2 本地组件说明，支持说明中的 term/spec/topic 链接 |
| Web Help | GH2 官方组件地址、中文和 100-GH 来源、自定义地址保存 |
| Example | GH2 原生示例、用户映射、HTTP/本地文件、拖放、复制、插入与撤销 |
| Params Help | 输入输出、求解数据或持久数据、分支、叶数据、空值、几何高亮 |
| Development Help | 类型、继承链、接口、组件 GUID、程序集位置 |
| File Manager | `.ghz`、`.gh`、`.ghx` 浏览和打开；旧文件通过 GH2 迁移 |
| Hops | `.gh/.ghx` 通过 GH2 的 GH1 互操作组件，仍需另行安装 Hops |
| Settings | 布局、颜色、线宽、自动目标、独立的画布滚轮方向设置 |

GH2 和 GH1 使用独立的设置目录、面板 GUID、打开状态和示例映射，允许同时加载。
共享界面仅依赖 `IHelperHost`、`HelpObject`、`IHelpExample`；GH1/GH2 文档和编辑器 API 留在各自的接入层。
GH2 项目不引用 GH1 程序集；需要读取旧文件或使用 Hops 时，调用 GH2 自带的互操作接口。
GH2 NuGet 未包含的本地说明渲染程序集，通过独立的运行时适配器访问。

## 构建与安装

在仓库根目录用 PowerShell 7：

```powershell
dotnet build .\SuperHelper.sln -c Release
.\build\Build-GH2.ps1
```

构建脚本输出独立的 `superhelpergh2-…-rh9_0-win.yak`，校验包内 RHP 哈希，且不捆绑 Rhino/GH/Eto SDK。
脚本不安装、不发布，也不改变原有 GH1 发版流程。

本地调试时先运行 Rhino 的 `GH2`，再通过 `GH2Plugins`（Grasshopper → Plugins → Install）选择：

```text
SuperHelper.GH2\bin\Release\net10.0\SuperHelper.GH2.rhp
```

GH2 编辑器的 **SuperHelper** 菜单提供入口。

## 验证范围

```powershell
dotnet run --project .\tests\SuperHelper.Compatibility.Tests -c Release -- $PWD.Path
```

这些检查验证宿主依赖、GUID 隔离、资源、当前安装 SDK 的接口签名、映射持久化、工作区状态及滚轮消息变换。
报告：`artifacts/gh2-contract-checks.json`。它们不验证 Rhino 原生界面的运行表现。

在已运行的 Rhino 9 内执行：

```text
_-RunPythonScript "<仓库绝对路径>\build\Test-GH2Compatibility.py"
```

报告：`artifacts/gh2-rhino-compatibility.json`。脚本临时切换到独立文档，结束后恢复原画布；不清空用户图。
交互拖放、取消、原生取点、镜头连续性及 Hops 服务须另行实机确认。报告保留 skipped，不将其视为完整通过。

截至本次交付，Release 构建、托管合同检查及 Yak 内容校验可在 Windows 上执行；Rhino GUI 回归尚未完成。

参考：[GH2 官方开发入口](https://developer.rhino3d.com/guides/grasshopper2/getting-started/)、[GH2 官方组件说明](https://www.rhino3d.com/docs/grasshopper2/specs/)。
