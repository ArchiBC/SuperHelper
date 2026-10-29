# Rhino 9 分支、打包和发布

工作流：`.github/workflows/yak.yml`。

## 分支

- `main`：默认分支，保存已合入的 R9 代码。
- `develop`：日常开发分支；需要发版时创建 `develop → main` PR。
- `legacy/rhino8`：原 `master` 的 R8 最后版本（`7e0d9d7`，产品版本 `2.0.3.1`），保留代码和历史安装包，不参与 R9 发版。

本地默认在 `develop` 工作。历史 YAK 目录不参与当前构建；新的包由 Actions artifacts 和 GitHub Release 保存。

## 触发规则

| 触发 | 行为 |
| --- | --- |
| 向 `main`、`develop` 推送相关代码 | 还原、Release 构建、校验 Yak 包、保存 artifact |
| 面向 `main`、`develop` 的 PR 打开、更新或重开 | 构建和校验 |
| 同仓库 `develop → main` PR 成功合并 | 从本次合并提交构建，自动创建版本标签及 GitHub Release，再将同一份包发布到 Release 和 Yak |
| 关闭未合并 PR、其他来源分支合并、R8 归档分支 | 不发布 |
| 手动 Run workflow | 仅构建和校验 |
| 手动创建标签或 GitHub Release | 不触发工作流 |

发版必须通过 `develop → main` PR 合并。直接推送到 `main` 只会构建，不会发版。
构建和发布都固定到该 PR 的合并提交 SHA，后续分支移动不会改变发布内容。

仅支持 net10.0、版本一致的正式 RhinoCommon/Grasshopper 9.x，生成 `rh9_0-win` 分发包。

## 发版步骤

1. 在 `develop` 完成改动和必要的 Rhino 9 实机回归。
2. 同时更新 `SuperHelper/SuperHelper.csproj` 的 `Version` 和 `SuperHelperInfo.cs` 的 GH AssemblyInfo 版本。
3. 推送 `develop`，确认构建通过。
4. 创建并合并 `develop → main` PR。工作流自动读取产品版本，例如 `2.1.1.0` 对应标签 `v2.1.1`。
5. 检查 Actions 的 package / publish 结果、GitHub Release 附件与公共 Yak 源。

如果版本标签已指向其他提交，发版会停止并要求提升版本号；不会移动标签或覆盖旧版本。

仓库 Actions Secret `YAK_TOKEN` 使用有权发布 `SuperHelperR9` 的 Rhino Accounts CI Key，已配置的值无需改变。
GitHub Release 创建和附件上传使用自动提供的 `GITHUB_TOKEN`。公共包 metadata 位于 `YAK/manifest-rh9.yml`。

## 本地验证

在仓库目录使用 PowerShell 7 和 .NET 10 SDK：

```powershell
$yakOutputRoot = Join-Path $PWD.Path ('artifacts/yak-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
./build/Build-Yak.ps1 -RepositoryRoot $PWD.Path `
  -YakExecutable 'C:\Program Files\Rhino 9\System\yak.exe' `
  -OutputRoot $yakOutputRoot
./build/Publish-Yak.ps1 -PackageDirectory $yakOutputRoot -ReleaseTag v2.1.1
```

第二条脚本默认只做版本、文件哈希及 GHA 内容校验，不联系 GitHub 或 Yak。
自动发布任务额外传入 `-Publish -CreateRelease -TargetCommit <合并提交完整 SHA>`；通过全部包检查后才创建 Release。

## 重试行为

- package job 没有发布凭据或仓库写权限；publish job 通过不可变 artifact ID 取得构建结果并复核哈希，不重新构建。
- 缺少 `YAK_TOKEN` 时在创建 Release 前停止。
- 仅复用标签指向同一合并提交的正式 Release；标签指向其他提交、draft 或 prerelease 都会停止。
- 如果 Release 已有同名包，下载并对比 ZIP 中每个文件的内容；一致时复用原附件，不覆盖。内容不一致则要求新版本。
- 如果 Yak 推送失败，保留 Release 附件供重跑失败任务。Yak 已存在版本的冲突会报告失败，不会强制覆盖。
- 仓库保留 `develop`，不启用合并后自动删除来源分支。
- 云端构建不包含 Rhino GUI 回归；窗口、取点和嵌入视口行为仍需实机验证。
