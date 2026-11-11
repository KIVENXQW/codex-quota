# Codex 周额度 · 极简版

Windows 上的独立小工具，显示当前 Codex 账户的周额度剩余与北京时间重置时刻。点击头像上方的粉色额度环，白色详情面板会在图标旁展开。

![详情面板预览，68%为示例数值](界面预览.png)

## 下载与启动

从 [1.3.0 发布页](https://github.com/KIVENXQW/codex-quota/releases/tag/weekly-v1.3.0) 下载 CodexWeekly-win-x64.zip，完整解压后双击 CodexWeekly.exe。需要 Windows x64、.NET Framework 4.8、已登录的 Codex 桌面客户端及 Node.js 22+（可以使用本机 Codex 配套的 Node）。

额度每分钟刷新一次。F5 可立即刷新，Esc 收起至托盘。退出可用托盘菜单。1.1.0 及之后的极简版可以从托盘检查更新，在发现新版后打开发行页。发布标签使用 weekly-v 加版本号。

## 1.3.0

- 侧栏使用白底粉色细环，周额度剩余 50% 时右半圈着色。
- 详情面板展开时靠近侧栏徽标。屏幕边缘自动避让，重复打开时按同一规则定位。
- 中心读数使用 Segoe UI Bold，按照每个读数的字形轮廓居中。
- 额度不可用时显示破折号；主面板展示周剩余百分比、进度条及重置时间。

## 构建

在 Windows PowerShell 中执行 Build.ps1。脚本编译 C# 原生窗口，并运行额度、更新、圆环和侧栏检查。项目使用 Node.js 标准库，通过本机 Codex 客户端查询周额度。

本分支为极简周额度版。完整版见[主分支](https://github.com/KIVENXQW/codex-quota/tree/main)。
