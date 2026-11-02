# 内置更新与发布包

当前版本为 **2.1.0**。`update-source.json` 的 `repository` 目前留空；更新功能在配置仓库之前不会连接到发布源，也没有可用的在线更新。

准备好公开 GitHub 仓库后，将 `repository` 填成 `owner/repo`，保留 `assetName` 为 `CodexQuota-win-x64.zip`。默认启动时检查，后台检查间隔为 24 小时。此配置不需要填写访问令牌。

## 生成发布包

先用 `Build-Native.ps1` 编译与 `version.json` 对应的主程序和更新助手，再运行：

```powershell
.\Build-Release.ps1
```

脚本默认读取 `version.json`。显式指定的 `-Version` 必须与它完全一致；缺少任何运行文件都会停止。已有发布包不会被覆盖，需要重新打包时明确使用：

```powershell
.\Build-Release.ps1 -Version 2.1.0 -Force
```

产物位于 `dist/v2.1.0/`：

- `CodexQuota-win-x64.zip`
- `CodexQuota-win-x64.zip.sha256`

ZIP 根目录直接放置运行文件，没有外层文件夹。`update-manifest.json` 记录版本，以及每个运行文件（包括配置）的 SHA-256。包内容采用固定名单，不包含本机账户数据、缓存、插件、源码、截图或交接记录。临时目录仅在本项目内创建并清理。

## GitHub Release

为对应版本创建正式、已发布的 Release，例如标签 `v2.1.0`，并上传上面的 **ZIP 和 SHA-256 两个文件**。草稿或预发布版本不作为稳定更新。只有仓库已配置且发布包已上传后，在线更新才可用。

更新客户端使用 GitHub 提供的 SHA-256 摘要或配套校验文件核验 ZIP，更新助手再按清单核验运行文件。更新保留现有 `update-source.json`，以及 `.data`、`.run` 中的本机数据；仓库选择不会被新包的默认配置覆盖。

本脚本只生成本地发布包，不创建仓库、不上传资产，也不发布 Release。
