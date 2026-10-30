# GH2 Mac 兼容

`mac` 从 `origin/develop` 的 `d97933a` 开始，接入 Windows GH2 提交 `63d3070`。
需要 Rhino 9 / GH2 `9.0.26300.9401` 或更新版本，以及 .NET 10。
GH1 的 `SuperHelper.gha` 与 GH2 的 `SuperHelper.GH2.rhp` 独立加载，设置目录、画布方向和面板 GUID 分别保存。

## Mac 接入

- 公共工作区使用 `MacWorkspacePlatform`，保留 Rhino 原生命令区、视口标题菜单及内部绘图视图的焦点处理。GH2 使用 Eto，不加载 GH1 的 WinForms 键盘过滤器。
- GH2 的 Mac ScrollGesture 会将普通鼠标滚轮当作平移。Mac 本地滚轮绑定只处理 GH2 窗口内、画布范围中的无 phase / momentum 纵向滚轮，并交给 GH2 自带的缩放处理器；设置可独立反向。事件的坐标、修饰键、自然滚动标志及缩放边界由原生处理器保留。
- 高精度鼠标也可能报告 precise deltas，不能以该标志判断触控板。带 phase 的平移、惯性和捏合继续走 GH2 自身的输入路径。访问 NSEvent.Phase 前必须先确认事件类型为 ScrollWheel。
- RhinoMac 会在 GH2 启动前扫描包顶层的 RHP，此时直接扫描 GH2 扩展会因缺少 Grasshopper2.dll 报初始化失败。Mac 包顶层提供只引用 RhinoCommon 的 `SuperHelper.GH2.MacLoader.rhp`，在用户启动 GH2、编辑器创建后加载 `gh2/SuperHelper.GH2.rhp`。不会在 Rhino 启动时自动打开 GH2，也不随包分发宿主 SDK。Windows 包继续使用原来的顶层 GH2 RHP。
- 隐藏的 GH2 不会因 GH1 或 Rhino 的取点命令自动弹回前台；只有 GH2 自己安排的 getter 能恢复隐藏窗口。
- 示例复制和插入会临时选择整个源文档，并恢复每个对象原有的选择状态。未保存选中状态的示例也能完整插入，GH2 负责重映射标识、连接及撤销记录。

## 构建与安装

在仓库根目录使用 PowerShell 7：

```powershell
dotnet build ./SuperHelper.sln -c Release
./build/Build-GH2.ps1 -Platform mac
```

输出 `artifacts/gh2-mac-…/package/superhelpergh2-…-rh9_0-mac.yak`。脚本核对包内 RHP 与本次构建的哈希，并拒绝捆绑宿主 DLL 或 GH1 扩展；不安装或发布。

使用 Rhino 包管理器或自带 `yak install <包路径>` 本地安装后重启 Rhino，再运行 `GH2`，工作区会自动加载。开发时也可以先启动 GH2，再通过 `GH2Plugins` → Install 选择 `SuperHelper.GH2/bin/Release/net10.0/SuperHelper.GH2.rhp`。

## 已验证与待验证

2026-10-30 实机：Apple Silicon、macOS 27.2、Rhino 9.0.26300.9402、Rhino 自带 .NET 10.0.2。
测试的 GH2 实现 SHA256 为 `3284d1da5896347b499f4a35eed45832136a6b7e04f473e3eaf763be81200ebc`，仅安装到本机，未发布。

| 检查 | 结果 | 报告 / 脚本 |
| --- | --- | --- |
| 共享接口、资源、GUID 隔离、SDK 签名、设置及滚轮变换 | 65 项通过 | `artifacts/gh2-contract-checks.json`；`dotnet run --project tests/SuperHelper.Compatibility.Tests -c Release -- <仓库绝对路径>` |
| GH1 共存、原生命令及隐藏 GH2 不抢焦点 | 36 项通过；另有 GH1 画布滚轮 24 项通过 | `build/Test-RhinoCompatibility.py`、`build/Test-CanvasWheelZoom.py` |
| Yak 安装后的冷启动和 GH2 自动加载 | 通过，加载位置为安装包的 `gh2` 目录 | `gh2-rhino-compatibility.json` 的 `auto_loaded_before_test=true` |
| 工作区、九个面板、帮助选择、数据树、GH1 示例迁移、插入及撤销重做 | 24 项通过 | `build/Test-GH2Compatibility.py` → `artifacts/gh2-rhino-compatibility.json` |
| 真实滚轮反向，Rhino 镜头、ZoomScale 及 GH1 方向设置保持不变 | 4 项通过 | `build/Test-GH2CanvasWheel.py` → `artifacts/gh2-native-wheel.json` |
| GH2 原生 Point 参数鼠标取点、点数据、编辑器可见性及镜头保持 | 8 项通过 | `build/Test-GH2PointPrompt.py` → `artifacts/gh2-point-prompt.json` |

Rhino 脚本使用 `_-RunPythonScript "<脚本绝对路径>"` 执行。滚轮脚本分三次运行：首次设置原生方向；在空画布向上滚动后再次运行；用相同量向上滚动后第三次运行，检查并恢复原设置与投影。取点脚本在临时 GH2 文档中使用 Point 的原生 PickOneItem，提示期间先滚动嵌入视口再点击，结束后恢复原文档、镜头和工作平面；60 秒未完成会取消。

交互示例拖放及取消、物理触控板的平移/惯性/捏合、可选 Hops 与 compute 服务仍需专项验证。集成脚本保留 skipped 和 `complete=false`；这些范围没有计入上述通过项。Windows 原生界面仍需在 Windows 上回归。

参考：[GH2 官方开发入口](https://developer.rhino3d.com/guides/grasshopper2/getting-started/)、[Rhino 包结构](https://developer.rhino3d.com/en/guides/yak/the-anatomy-of-a-package/)。
