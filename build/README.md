# Rhino 9 Yak 打包和发布

工作流：`.github/workflows/yak.yml`。

| 触发 | 行为 |
| --- | --- |
| 推送相关代码、PR、手动 Run workflow | 还原、Release 构建、打 Yak 包、保存 Actions artifact，不发布 |
| 发布正式 GitHub Release | 完成上述检查，再将同一份 artifact 发布到 GitHub Release 和 Yak |
| GitHub prerelease | 跳过 |

仅支持 net10.0、版本一致的正式 RhinoCommon/Grasshopper 9.x，生成 `rh9_0-win` 分发包。

## 发布准备

仓库 Actions Secret 名称为 `YAK_TOKEN`，值为有权发布 `SuperHelperR9` 的 Rhino Accounts CI Key。工作流不会打印该值。GitHub 附件上传使用自动提供的 `GITHUB_TOKEN`。

发布标签必须对应待发布的 R9 代码，并与以下两个位置的版本一致：

- `SuperHelper/SuperHelper.csproj` 的 `Version`。
- `SuperHelperInfo.cs` 的 GH AssemblyInfo 版本。

例如项目 `2.1.0.0` 可对应标签 `v2.1.0`；`v2.1.1` 需要先修改产品版本。公共 metadata 在 `YAK/manifest-rh9.yml`，打包时替换其中的版本占位符。历史 YAK 目录不参与发布，也不被脚本改写。

## 本地验证

在仓库目录使用 PowerShell 7 和 .NET 10 SDK：

```powershell
$yakOutputRoot = Join-Path $PWD.Path ('artifacts/yak-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
./build/Build-Yak.ps1 -RepositoryRoot $PWD.Path `
  -YakExecutable 'C:\Program Files\Rhino 9\System\yak.exe' `
  -OutputRoot $yakOutputRoot
./build/Publish-Yak.ps1 -PackageDirectory $yakOutputRoot -ReleaseTag v2.1.0
```

第二条脚本默认只做版本、文件哈希及 GHA 内容校验，不联系 GitHub 或 Yak。实际发布由正式 Release 事件的独立 job 执行。

## 发布和重试行为

- package job 没有发布凭据或仓库写权限；publish job 通过不可变 artifact ID 取得构建结果并复核哈希，不重新构建。
- 缺少 `YAK_TOKEN` 时在上传 Release 附件前停止。Token 的有效性和包所有权只能在实际服务认证时确认。
- 如果 GitHub Release 已有同名包，下载并对比 ZIP 中每个文件的内容；一致时复用原附件，不覆盖。内容不一致则停止，要求新版本。
- 如果 Yak 推送失败，保留 GitHub Release 附件供重试。Yak 已存在版本的冲突会作为失败报告，不被当作发布成功或强制覆盖。
- 普通云端构建不包含 Rhino GUI 回归；窗口、取点和嵌入视口行为仍需实机验证。

当前接入结果和本地验证日志在 `artifacts/yak-ci-20261029/`。工作流进入 GitHub 并成功运行前，不能将本地验证视为云端成功。
