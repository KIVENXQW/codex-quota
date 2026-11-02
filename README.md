# Codex Quota

Windows 上的 Codex 额度与用量小工具，当前版本 **2.1.0**。在 Codex 左下角头像上方显示额度徽标，同时提供系统托盘入口；点击即可展开原生弹窗。

这是独立社区工具，与 OpenAI 无官方关联。

## 下载与运行

1. 登录有权限访问此私有仓库的 GitHub 账号，从 [下载包目录](downloads/v2.1.0/) 获取 `CodexQuota-win-x64.zip`。打开 ZIP 文件页面后选择下载原始文件。
2. 将 ZIP **完整解压**到一个可写文件夹，保留所有文件与子目录。
3. 打开 Codex 桌面客户端并登录，然后运行 `CodexQuota.exe`。

不要只复制 EXE，也不要直接在压缩包内运行。日常操作通过头像上方的徽标和 Windows 托盘完成，无需打开网页。

运行环境：

- Windows 10 / 11，x64。
- .NET Framework 4.8；系统已安装时无需另装。
- 较新的 Codex 桌面客户端，并已登录账户。
- Node.js 22 或更新版本：可通过 `PATH` 找到，或使用工具能识别的 Codex 配套运行时。

## 功能与统计口径

- **账户额度**：显示账户返回的剩余百分比、额度窗口与重置时间；缺失信息不会显示成零。
- **Token 用量**：显示账户提供的汇总、按日记录及年度活动热力图。
- **AI 工作时长与已完成任务**：依据本机可读取的最近 90 天活动记录统计。时长从提交到完成，排除用户空闲，并合并同时进行的工作区间，避免重复计时。这是部分本机历史，不等同于账户完整历史。
- **日 / 周 / 月柱状图**：切换 Token 与工作时长，悬停查看明细。
- **外观**：中英文切换、固定系统界面字体、预设与自定义强调色。
- **内置更新**：从托盘检查新版，确认安装后下载并校验 SHA-256，再重启更新。关闭更新窗口可取消正在进行的检查或下载。

Token 数量、额度百分比和工作时长是不同指标；本工具不会用 API 单价推算订阅费用。

## 更新来源

更新来源由 `update-source.json` 的 `repository` 指定，格式为 `owner/repo`。仓库字段为空时不会自动联网检查；手动检查会提示尚未配置。

目前先以私有仓库保存，更新来源保持为空。现有更新器匿名读取 GitHub Releases，不能直接读取私有仓库的发布包。将来确定公开发布源、配置仓库并上传正式 Release 的 ZIP 和 `.sha256` 后，在线更新才可用。默认启动时检查，检查间隔为 24 小时。更新保留现有来源配置与本机数据。发布步骤见 [UPDATES.md](UPDATES.md)。

## 隐私与本机数据

登录凭据由 Codex 管理，无需在本工具中填写账户密码或 API Key。工作统计不收集聊天正文；本机活动与运行状态保存在 `.data/`、`.run/` 等本地目录中，这些目录不会进入发布包。

读取账户额度需要连接相应账户服务；检查和下载更新使用配置的 GitHub 发布仓库。

## 从源码构建

在项目目录中运行 PowerShell：

```powershell
.\Build-Native.ps1
.\Build-Release.ps1
```

构建使用 .NET Framework 的 `csc.exe`，无需安装 .NET SDK。版本来自 `version.json`；发布包输出到 `dist/v2.1.0/`。已有输出需显式添加 `-Force` 才能覆盖。

运行 Node 测试：

```powershell
node --test
```

## English quickstart

Codex Quota is an independent Windows companion for Codex Desktop. It adds a quota badge **above the bottom-left profile avatar** and a system tray popup. It is not affiliated with OpenAI.

**Requirements:** Windows 10/11 x64, .NET Framework 4.8, a recent signed-in Codex Desktop, and Node.js 22+ on `PATH` or an accessible Codex-bundled runtime.

Sign in to a GitHub account with access to this private repository. Download `CodexQuota-win-x64.zip` from [downloads/v2.1.0](downloads/v2.1.0/), extract the entire archive into a writable folder, then run `CodexQuota.exe`. Keep the accompanying files and folders together.

The popup shows account quota, Token activity, daily/weekly/monthly charts, and local AI work time. Local work statistics cover available records from the last 90 days: elapsed time runs from submission to completion, excludes user idle time, and merges overlapping intervals. This is partial local history, not complete account history. Language and accent color are configurable; the system-style font is fixed.

Credentials remain managed by Codex. Chat message bodies are not collected, and local `.data/` and `.run/` directories are excluded from release packages. No subscription cost is inferred from API pricing.

This repository is initially private storage. The current updater reads public GitHub Releases without credentials; it cannot fetch private release assets. The empty `repository` in `update-source.json` keeps online checks disabled until a public release source is chosen. Installation requires your confirmation and a verified package. See [UPDATES.md](UPDATES.md) for packaging and release details.

Build with `Build-Native.ps1`, then `Build-Release.ps1`; run Node tests with `node --test`. The native build uses the .NET Framework compiler and does not require the .NET SDK.
