# Windows GH2 扩展

开发分支：`win`，基础提交 `d97933a`（创建分支时的最新 `origin/develop`）。
`SuperHelper.gha` 与 `SuperHelper.GH2.rhp` 分别接入 GH1 / GH2，由一个 `SuperHelperR9` 通用 Yak 包交付。

需要 Rhino 9、.NET 10，以及版本不低于 `9.0.26300.9401` 的 GH2。
`win` 已同步 `mac` 的 `da8decf`，两平台沿用同一套公共界面和宿主接口，分别使用原生输入适配，见 [GH2 Mac 兼容说明](gh2-mac.md)。

## 功能对应

| 功能 | GH2 接入 |
| --- | --- |
| 打开、关闭、左右停靠 | 原生 Eto 菜单和分隔器，不新增顶部工具栏 |
| Command、Viewport、Help 布局 | 复用 Rhino 原生工作区，GH1 / GH2 共用九个面板 GUID |
| Rhino 命令、反馈、取点视口 | 复用原生命令栏和视口，单独处理 GH2 getter 的隐藏计时器 |
| Origin Help | GH2 本地组件说明，支持说明中的 term/spec/topic 链接 |
| Web Help | GH2 官方组件地址、中文和 100-GH 来源、自定义地址保存 |
| Example | GH2 原生示例、用户映射、HTTP/本地文件、拖放、复制、插入与撤销 |
| Params Help | 输入输出、求解数据或持久数据、分支、叶数据、空值、几何高亮 |
| Development Help | 类型、继承链、接口、组件 GUID、程序集位置 |
| File Manager | `.ghz`、`.gh`、`.ghx` 浏览和打开；旧文件通过 GH2 迁移 |
| Hops | `.gh/.ghx` 通过 GH2 的 GH1 互操作组件，仍需另行安装 Hops |
| Settings | 布局、颜色、线宽、自动目标、独立的画布滚轮方向设置 |

GH2 和 GH1 共用九个 Rhino 面板定义和原 GH1 GUID，面板列表只出现一套名称。各工作区的面板实例按 Rhino 实例标识解析到自身内容，关闭一个工作区只移除自身的内容注册。
两宿主仍使用独立的设置目录、打开状态和示例映射，允许同时加载。旧 GH2 布局会将九个旧 GUID 映射到共用 GUID，保留顺序、选中项及其他 Rhino 面板。
共享界面仅依赖 `IHelperHost`、`HelpObject`、`IHelpExample`；GH1/GH2 文档和编辑器 API 留在各自的接入层。
GH2 项目不引用 GH1 程序集；需要读取旧文件或使用 Hops 时，调用 GH2 自带的互操作接口。
GH2 NuGet 未包含的本地说明渲染程序集，通过独立的运行时适配器访问。

## 构建与安装

在仓库根目录用 PowerShell 7：

```powershell
dotnet build .\SuperHelper.sln -c Release
.\build\Build-GH2.ps1
```

脚本现在统一输出 `superhelperr9-…-rh9_0-any.yak`，同时包含 GH1、GH2 和公共 Rhino 加载器，校验三个实现文件的哈希，不捆绑 Rhino/GH/Eto SDK。`Build-GH2.ps1` 是 `Build-Yak.ps1` 的兼容入口，不再按宿主或系统分包。
构建脚本不安装、不发布。用 Rhino 包管理器或 `yak install <本地包路径>` 安装同一个包，重启 Rhino 后运行 `Grasshopper` 或 `GH2`。
从旧的独立 `SuperHelperGH2` 测试包升级时，先通过包管理器卸载该旧包，只保留新的 `SuperHelperR9` 统一包，避免重复安装同一个加载器；保留设置目录以迁移原布局。

包顶层是 `SuperHelper.gha` 和 `SuperHelper.GH2.Loader.rhp`，GH2 实现位于 `gh2/SuperHelper.GH2.rhp`。加载器只依赖 RhinoCommon，启动 Rhino 时不打开 GH2，待用户打开 GH2 后才通过 GH2 插件接口加载嵌套实现；Windows / macOS 共用此流程。

GH2 编辑器的 **SuperHelper** 菜单提供入口，其中 **Open Settings** 打开设置；不再向编辑器添加工具栏或工具栏按钮。
GH1 的 **View → Switch to Grasshopper 2** 与 GH2 的 **SuperHelper → Switch to Grasshopper 1** 提供往返切换。切换调用目标宿主原生打开命令，成功后隐藏来源编辑器，不关闭文档或改变工作区开关设置。

## 验证范围

```powershell
dotnet run --project .\tests\SuperHelper.Compatibility.Tests -c Release -- $PWD.Path
```

这些检查验证宿主依赖、共用 GUID、跨宿主内容路由与释放、旧布局迁移、资源、当前安装 SDK 的接口签名、映射持久化、工作区状态及滚轮消息变换。
报告：`artifacts/gh2-contract-checks.json`。它们不验证 Rhino 原生界面的运行表现。

在已运行的 Rhino 9 内执行：

```text
_-RunPythonScript "<仓库绝对路径>\build\Test-GH2Compatibility.py"
```

报告：`artifacts/gh2-rhino-compatibility.json`。脚本临时切换到独立文档，结束后恢复原画布；不清空用户图。
交互拖放、取消、原生取点、镜头连续性及 Hops 服务须另行实机确认。报告保留 skipped，不将其视为完整通过。

2026-10-30，Windows / Rhino `9.0.26300.9401`：统一包 Release 构建、发布前包校验及 103 项托管检查通过。本机用 Yak 安装同一个 `SuperHelperR9 2.1.2.0` 包后，分别验证 GH1 先打开和 GH2 先打开，每种顺序均通过 27 项 GH2 集成检查、8 项面板注册及归属检查、11 项宿主切换检查，以及冷启动/退出检查。两次测试进程均正常退出；GH2 先打开的完整退出码为 0。额外通过 6 项工作区主动清理与重复清理检查。只开 GH1、只开 GH2 均正常清理并以 0 退出，GH1 不会自行启动 GH2；另行用 `RhinoApp.Exit(true)` 验证普通退出路径，GH2 同样正常清理并以 0 退出。

当前包和冷启动报告位于 `artifacts/all-hosts-candidate-1/`，安装位置为本机 `packages/9.0/SuperHelperR9/2.1.2.0`。包内三个实现文件的哈希与实机加载文件逐一对应，见 `package-result.json`、`cold-*`、`panels-*`、`integration-*`、`switching-*` 和 `exit-GH2-first.json`。早期分包及反射测试的记录仍保留在原 `artifacts/shared-panels-validation*` 目录，不作为统一包的通过证据。

`build/Test-PanelRegistration.py` 验证两宿主只注册九个面板、重复注册不增加定义，以及各工作区实例取得自身内容。GH2 集成脚本还检查菜单设置入口和工具栏按钮移除。
原生鼠标回归在用户按 Esc 停止界面控制后暂停；滚轮、拖放/取消、取点与镜头连续性尚未验收。本轮未配置 Hops 服务，也未验收 Hops HTTP 求解。

统一包已完成本机安装验证。新版 Mac 原生回归尚未执行；Mac 旧版实机结果不能代替本次统一包验收。构建期间有 NuGet 漏洞数据源不可达提示，Yak 对加载器名称和 informational version 给出元数据提示，包内容及哈希校验均通过。

## 退出清理修复

此前 GH2 将其 `Framework.Plugin` ID `01a71230-29b2-4b47-a8f0-b141a36a7f2a` 作为 Rhino dock bar 的所有者，退出时出现 `Tabbed dockbars cleanup error`。现由真实的 Rhino 插件加载器 `89447b4b-3888-4b3f-9c83-4f3a5416cb25` 统一拥有九个面板，使用公共 `Rhino.UI.Panels.RegisterPanel` 注册。Rhino Closing 和加载器 OnShutdown 在释放所有者前清理两宿主私有容器及内容注册，重复调用无副作用；不屏蔽错误弹窗。实机验证包含直接退出时的自动清理。

## 统一集成与发布

当前 `origin/develop` 是 `win` / `origin/mac` 的祖先，`origin/mac` 已包含原 `win` 提交；同步后的 `win` 可作为统一集成来源，无需分别合入两份相同实现。
兼容工作流在 `main` / `develop` / `win` / `mac` 推送时构建并校验同一个全宿主通用包；正式发布流程仍遵守 `build/README.md`。
两种宿主加载顺序的 Windows 报告已通过。2026-10-30 用户确认 Windows 测试无问题，授权按默认通用兼容发布 `2.1.2`。未完成的 Mac 新包实机专项及 Windows 自动鼠标专项继续保留为未完成，不改写为通过；发布继续执行 Windows 打包校验与三个项目的 macOS 编译门禁。

参考：[GH2 官方开发入口](https://developer.rhino3d.com/guides/grasshopper2/getting-started/)、[GH2 官方组件说明](https://www.rhino3d.com/docs/grasshopper2/specs/)。
