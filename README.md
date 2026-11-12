# 简易记账本 v1.3
## 说在前面
依旧AI分得全部功劳，本人苦广告久矣，因此做出本记账软件，并且发来github正好学学github的用法，未来还希望学习编程。

## 本软件介绍（下面由AI生成）
此软件Windows中文本机账本，使用C#、WPF、.NET Framework 4.8桌面组件。免安装，无需联网或账户。

## v1.3新增

左边栏“支出走势”：按日或按月查看支出折线，可对比上月或上年；支持时间选择、总额与峰值、悬停详情、键盘选择以及跳转支出账单。兼容旧版本数据。统计口径见“使用说明.txt”。

## 构建和逻辑检查

在Windows PowerShell中从源码根目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
& .\tests\CoreTests.exe
& .\tests\TrendLogicTests.exe
```

构建使用Windows自带.NET Framework 4.x编译器与已安装的4.8 WPF组件，不下载NuGet包。默认输出“发布/日常账本.exe”；可用 -OutputDirectory 指定输出目录。覆盖正在运行的同一份程序前请先关闭程序。构建不自动运行测试。

原有ThemeTests、LedgerTests也会编译，供需要时自行运行；它们会创建WPF界面对象并保存检查截图，本次没有运行。

## 源码

- src/Core.cs：账本、预算、存储、备份、CSV导出。
- src/Trends.cs：日/月支出汇总与折线图绘制。
- src/TrendsPage.cs：走势页面状态、时间选择、对比和账单跳转。
- src/App.cs：程序入口、记账及页面操作。
- src/Theme.cs：主题、外观保存和弹窗。
- src/MainWindow.xaml：界面和控件样式。
- tests/CoreTests.cs：33项原有核心检查。
- tests/TrendLogicTests.cs：22项走势统计检查。
- tests/ThemeTests.cs、LedgerTests.cs：保留的原有界面检查工具。

## 升级与验证

关闭旧程序，用新版exe替换原目录同名文件，保留“数据”文件夹。数据结构版本仍为1；应用版本为1.3.0，两者独立。完整JSON备份可恢复；CSV只供查看。

详见“升级日志-v1.3.txt”“使用说明.txt”“验证记录-v1.3.md”。发布包和源码包均不含用户账目。当前通过编译及55项逻辑检查；本次未进行界面操作和视觉验证。

## macOS 原生版

v1.3 同时提供 Windows 版和 Apple 原生 Mac 版，两者统一在 [v1.3 Release](https://github.com/NatsumeAiLover/EasyDailyLedger/releases/tag/v1.3) 下载；v1.0、v1.1、v1.2 继续保留。

Mac 版使用 SwiftUI、Swift Charts、AppKit 和 Foundation，直接调用系统组件，不附带 Electron、Chromium 或 Node.js。面向 **macOS 27、Apple Silicon**，应用压缩包约 **378 KB**。

### 下载与安装

| 文件 | 用途 |
| --- | --- |
| [EasyDailyLedger-v1.3.exe](https://github.com/NatsumeAiLover/EasyDailyLedger/releases/download/v1.3/EasyDailyLedger-v1.3.exe) | Windows 版程序 |
| [DailyLedger-1.3.0-macOS27-arm64.zip](https://github.com/NatsumeAiLover/EasyDailyLedger/releases/download/v1.3/DailyLedger-1.3.0-macOS27-arm64.zip) | Mac 原生应用和安装说明 |
| [DailyLedger-1.3.0-native-source.zip](https://github.com/NatsumeAiLover/EasyDailyLedger/releases/download/v1.3/DailyLedger-1.3.0-native-source.zip) | Mac 完整源码与构建流程 |
| [SHA256SUMS.txt](https://github.com/NatsumeAiLover/EasyDailyLedger/releases/download/v1.3/SHA256SUMS.txt) | 发布附件的 SHA256 校验值 |

Mac 下载后请完整解压，按包内说明运行“安装到我的应用.command”。程序已完成 ad-hoc 临时签名，**没有 Developer ID 签名或 Apple 公证**；如遇系统拦截，请先确认文件来源，再按说明安装。安装脚本不更改系统安全设置。

### 功能与数据迁移

Mac 版提供首页概览、每日账单、支出走势、设置与备份，支持日／月折线、上月／上年对比、预算、分类管理、三种主题以及 JSON 备份恢复。

数据保存在 `~/Library/Application Support/DailyLedgerNative`，与应用分开存放。迁移时，在 Windows 版导出完整 JSON 备份，再在 Mac 版的“设置与备份”中恢复。兼容 Windows v1.0—v1.3 的 Version=1 账本。恢复会替换当前数据并先保留原文件；CSV 只供查看，不用于恢复。数据没有加密，重要备份请另存到其他磁盘。

### 源码与验证

主分支的 [macos-native](macos-native) 目录包含 Mac 原生源码；[Mac 版说明](macos-native/README.md)提供本机编译步骤和构建流程介绍。原生版已在 GitHub 的 Mac 环境完成编译，37 项核心检查通过，并完成临时签名验证；尚未进行 macOS 27 真机和图形界面操作验收。

GitHub Actions 的“Build native macOS ledger”现在改为手动构建。需要发布时选择已有的同版本 Release（默认 `v1.3`），Mac 附件会更新到该版本下，不再按构建次数新增独立的 Mac 预发布条目。

