# GH2 Mac 兼容

`mac` 从 `origin/develop` 的 `d97933a` 开始，接入 Windows GH2 提交 `63d3070`。
需要 Rhino 9 / GH2 `9.0.26300.9401` 或更新版本，以及 .NET 10。
GH1 的 `SuperHelper.gha` 与 GH2 的 `SuperHelper.GH2.rhp` 独立加载，设置目录和画布方向分别保存，Rhino 面板注册共用一套。

## Mac 接入

- 公共工作区使用 `MacWorkspacePlatform`，保留 Rhino 原生命令区、视口标题菜单及内部绘图视图的焦点处理。GH2 使用 Eto，不加载 GH1 的 WinForms 键盘过滤器。
- GH2 的 Mac ScrollGesture 会将普通鼠标滚轮当作平移。Mac 本地滚轮绑定只处理 GH2 窗口内、画布范围中的无 phase / momentum 纵向滚轮，并交给 GH2 自带的缩放处理器；设置可独立反向。事件的坐标、修饰键、自然滚动标志及缩放边界由原生处理器保留。
- GH1 的 Mac WinForms 桥直接使用 NSEvent 的纵向增量，GH2 原生滚轮处理器还会按自然滚动标志补偿一次方向。绑定在自然滚动事件中抵消这次额外补偿，使相同 Rhino 设置和 SuperHelper 设置下，GH1 / GH2 的鼠标滚轮方向一致；自然滚动标志本身保留。
- 高精度鼠标也可能报告 precise deltas，不能以该标志判断触控板。带 phase 的平移、惯性和捏合继续走 GH2 自身的输入路径。访问 NSEvent.Phase 前必须先确认事件类型为 ScrollWheel。
- Rhino 会在 GH2 启动前扫描包顶层的 RHP，此时直接扫描 GH2 扩展会因缺少 Grasshopper2.dll 报初始化失败。现用同一个 `SuperHelperR9` 通用包：顶层提供只引用 RhinoCommon 的 `SuperHelper.GH2.Loader.rhp` 和 GH1 的 `SuperHelper.gha`，用户打开 GH2 后才加载 `gh2/SuperHelper.GH2.rhp`。Windows / macOS 共用此流程，不自动打开 GH2，不随包分发宿主 SDK。
- 隐藏的 GH2 不会因 GH1 或 Rhino 的取点命令自动弹回前台；只有 GH2 自己安排的 getter 能恢复隐藏窗口。
- 示例复制和插入会临时选择整个源文档，并恢复每个对象原有的选择状态。未保存选中状态的示例也能完整插入，GH2 负责重映射标识、连接及撤销记录。
- Eto 在 Mac 上异步完成原生拖放。示例的 token 和 DataObject 保留到 DragEnd；源控件卸载时也清理，取消后不会回退为直接插入。拖动提供缩略图或图标，确保 Eto.Mac 创建实际的 NSDraggingItem。入口采用卡片 MouseDown 的按钮状态，避免全局物理按钮状态与事件不一致时误插入。
- Rhino 面板列表只注册九个共用定义，沿用 GH1 GUID 和名称。各工作区通过面板实例标识绑定自身内容，任一宿主先加载都可复用同一套注册；旧 GH2 布局自动迁移 GUID。GH2 不再新增顶部工具栏，保留 SuperHelper 菜单中的 Open Settings。
- GH1 的 View → Switch to Grasshopper 2 和 GH2 的 SuperHelper → Switch to Grasshopper 1 调用目标宿主原生打开命令，成功后隐藏来源编辑器，保留各自文档和工作区开关设置。
- 九个共用面板通过公共 `Rhino.UI.Panels.RegisterPanel` 注册到真实的通用 Rhino 加载器；退出时先释放两宿主私有容器，再卸载所有者，避免把 GH2 扩展 ID 当成 Rhino 插件 ID 引起清理弹窗。此逻辑没有新增系统分支。

## 构建与安装

在仓库根目录使用 PowerShell 7：

```powershell
dotnet build ./SuperHelper.sln -c Release
./build/Build-GH2.ps1
```

输出单个 `artifacts/all-hosts-…/package/superhelperr9-…-rh9_0-any.yak`，同时支持 GH1 / GH2 和 Windows / macOS。脚本核对两个宿主实现与加载器的版本、SDK 和哈希，拒绝捆绑宿主 DLL；不安装或发布。旧 `-Platform mac/win` 参数只保留调用兼容，仍输出同一个通用包。

使用 Rhino 包管理器或自带 `yak install <包路径>` 本地安装后重启 Rhino，再运行 `Grasshopper` 或 `GH2`，相应工作区会自动加载。
此前安装过独立 `SuperHelperGH2` 测试包的机器，应先在包管理器中卸载该旧包，只保留新的 `SuperHelperR9` 统一包，避免重复注册同一个加载器。设置目录不随此步骤清除，旧 GH2 布局会迁移到共用 GUID。

## 已验证与待验证

以下实机记录对应 `mac` 的 `da8decf` 及更早实现。之后在 `win` 的公共代码中调整了面板共用、菜单切换、GH2 工具栏及退出清理，并合成全宿主通用包。统一包已通过 Windows 冷启动及退出测试，详见 [Windows 验证](gh2-windows.md)；新的 Mac 原生回归尚未执行，不能将下面的旧版结果当作统一包验收。

2026-10-30 实机：Apple Silicon、macOS 27.2、Rhino 9.0.26300.9402、Rhino 自带 .NET 10.0.2。
本轮 GH2 实现 SHA256 为 `e7766532f2d8b408d335fcbc3b3b76f237d22e9cc2dc1509149a9ec07ebe0db5`，仅安装到本机，未发布。先前滚轮和取点报告的实现 SHA256 为 `3284d1da5896347b499f4a35eed45832136a6b7e04f473e3eaf763be81200ebc`。

| 检查 | 结果 | 报告 / 脚本 |
| --- | --- | --- |
| 共享接口、资源、GUID 隔离、SDK 签名、设置及滚轮变换 | 97 项通过，含自然滚动 / 反向开关 / Rhino 方向的 32 种组合 | `artifacts/gh2-contract-checks.json`；`dotnet run --project tests/SuperHelper.Compatibility.Tests -c Release -- <仓库绝对路径>` |
| GH1 共存、原生命令和工作区 | 本轮 35 项通过；另有此前 GH1 画布滚轮 24 项通过 | `build/Test-RhinoCompatibility.py`、`build/Test-CanvasWheelZoom.py` |
| Yak 安装后的冷启动和 GH2 自动加载 | 通过，加载位置为安装包的 `gh2` 目录 | `gh2-rhino-compatibility.json` 的 `auto_loaded_before_test=true` |
| 工作区、九个面板、帮助选择、数据树、GH1 示例迁移、插入及撤销重做 | 24 项通过 | `build/Test-GH2Compatibility.py` → `artifacts/gh2-rhino-compatibility.json` |
| 真实滚轮反向，Rhino 镜头、ZoomScale 及 GH1 方向设置保持不变 | 4 项通过 | `build/Test-GH2CanvasWheel.py` → `artifacts/gh2-native-wheel.json` |
| GH2 原生 Point 参数鼠标取点、点数据、编辑器可见性及镜头保持 | 8 项通过 | `build/Test-GH2PointPrompt.py` → `artifacts/gh2-point-prompt.json` |
| GH1 DollyZoom 与 GH2 原生滚轮处理器方向对照 | 17 项通过 | `build/Test-GH2WheelDirection.py` → `artifacts/gh2-wheel-direction.json` |
| GH2 真触控板横纵平移、惯性、双向捏合及镜头保持 | 8 项通过，记录 354 个原生事件 | `build/Test-MacTrackpad.py` → `artifacts/gh2-native-trackpad.json` |
| GH1 真触控板对照及镜头保持 | 8 项通过，记录 393 个原生事件 | `build/Test-MacTrackpad.py` → `artifacts/gh1-native-trackpad.json` |
| 原双套面板标题区分、各重复注册五次 | 旧版 5 项通过；现已改为九个共用定义，需重跑新版脚本 | `build/Test-PanelRegistration.py` → `artifacts/panel-registration.json` |
| 真鼠标示例拖放、连线、源选择及撤销重做；原生 Esc 取消 | 7 项落点检查、5 项独立取消检查通过 | `build/Test-GH2ExampleDrag.py`；`artifacts/gh2-example-drag.json`、`artifacts/gh2-example-drag-cancel.json` |
| GH1 Hops、GH2 互操作包装及 SuperHelper 插入后真实 HTTP 求解 | 12 项通过，两个宿主均得到 43，撤销重做通过 | `build/Test-HopsCompatibility.py` → `artifacts/hops-compatibility.json` |

Rhino 脚本使用 `_-RunPythonScript "<脚本绝对路径>"` 执行。滚轮脚本分三次运行：首次设置原生方向；在空画布向上滚动后再次运行；用相同量向上滚动后第三次运行，检查并恢复原设置与投影。取点脚本在临时 GH2 文档中使用 Point 的原生 PickOneItem，提示期间先滚动嵌入视口再点击，结束后恢复原文档、镜头和工作平面；60 秒未完成会取消。

示例拖放脚本完整模式运行三次：建立临时卡片后真正拖到 GH2 画布；第二次检查并撤销插入，再拖动并按 Esc 取消；第三次验证取消并清理。可先在 IronPython 中设置 `scriptcontext.sticky['SuperHelper.GH2.ExampleDragMode']='cancel'`，单独运行两次取消用例。只有实际收到 DragEnd (None)，才会判定取消。首轮自动化取消没有收到原生结束事件，报告保留该失败；随后用独立真鼠标取消用例验证资源释放，不能把自动输入尚未结束当作产品失败或成功。

物理触控板使用 `build/Test-MacTrackpad.py` 专项记录，不能用普通滚轮代替。默认 GH2；测试 GH1 前设置 `scriptcontext.sticky['SuperHelper.TrackpadHost']='GH1'`。首次运行后在空画布做双指横移、纵移、快速滑动后松手、张开和捏合；再次运行检查原生 phase / momentum、投影变化和镜头，并恢复原投影与窗口可见性。

本轮 GH1 实现 SHA256 为 `9184e19ded788f09e4a124f226dad7941befb138c2c69843340027df26a12829`。两边均收到真实带 phase 的横纵平移、momentum 事件及正负 magnification；平移不改变缩放，惯性继续移动画布，张开与捏合方向正确，Rhino 嵌入镜头与全局 ZoomScale 不变。用户分别确认 GH1 / GH2 手势操作正常。

集成脚本仍将交互拖放、取点和 Hops 列为单独验证范围，保留 `complete=false`；总体验收应合并各专项报告。Windows 原生界面仍需在 Windows 上回归。

## Hops 专项复现

使用官方 Yak 的 Hops 9.0.0。建立 Python 3.13 虚拟环境后安装 `tests/hops/requirements.txt`，运行 `python tests/hops/server.py`，再在 Rhino 执行 `build/Test-HopsCompatibility.py`。服务器只监听 `127.0.0.1:18731`，每次实际求解写入 `artifacts/hops-server-calls.jsonl`，结束测试后停止服务器。

固定用例使用输入 A=6、B=7，服务器真实计算 A×B+1；脚本分别通过 GH1 原生 Hops 和 GH2 的 GH1InteropComponent 得到 43，并覆盖 SuperHelper 示例插入及撤销重做。Hops 9 的 IO 描述读取 InputNames / OutputNames；ghhops_server 1.5.5 的描述缺少这两项，因此测试服务器补齐名称元数据，计算仍由 ghhops_server 执行。

此用例验证 Mac 支持的本地 Python Hops 服务。任意 `.gh` 文件作为 Hops 定义的求解还需要可用的 Rhino.Compute 服务；本轮没有配置远程 Compute，未验收该服务端部署路径。参见 [Hops 官方说明](https://developer.rhino3d.com/guides/compute/hops-component/)。

参考：[GH2 官方开发入口](https://developer.rhino3d.com/guides/grasshopper2/getting-started/)、[Rhino 包结构](https://developer.rhino3d.com/en/guides/yak/the-anatomy-of-a-package/)。
