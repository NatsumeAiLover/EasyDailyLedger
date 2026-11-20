# 简易记账本 v1.4
## 说在前面
依旧AI分得全部功劳，本人苦广告久矣，因此做出本记账软件，并且发来github正好学学github的用法，未来还希望学习编程。

## 本软件介绍（下面由AI生成）
此软件Windows中文本机账本，使用C#、WPF、.NET Framework 4.8桌面组件。免安装，无需联网或账户。

## v1.4 更新（Windows / macOS）

增加 CSV 文件的导入、导出：在“设置与备份”中导入数据或导出全部账目，在“每日账单”中导出所选月份的全部账目。

### CSV 格式

```csv
日期,收支类别,类别,金额,备注
2026年1月2日,支出,餐饮,25.50,午饭
2026年1月3日,收入,工资 / 兼职,100.00,
```

- 日期格式为 `yyyy年M月d日`，例如 `2026年1月2日`；导入也兼容补零的中文日期和旧版 `yyyy-MM-dd`。
- 收支类别为“支出”或“收入”，也接受“收支类型”表头。
- 类别按收支分别匹配，未知类别自动添加到配置，已停用类别重新启用。
- 金额大于零，最多两位小数；备注对应软件中的记账备注，选填，可留空或省略整列。
- 栏目顺序不限，“账户”等额外栏目忽略。导入兼容逗号或制表符分列，以及 UTF-8、带 BOM 的 UTF-16、GBK/GB18030 编码；导出采用逗号分列的 UTF-8 BOM 文件。
- 导入前检查整份文件并确认后追加，保留原账目和预算；重复导入会重复记账。完整数据迁移仍使用 JSON 备份。

Windows / macOS 下载：[v1.4 Release](https://github.com/NatsumeAiLover/EasyDailyLedger/releases/tag/v1.4)。

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

CsvTests、ThemeTests、LedgerTests也会编译；可运行 test.ps1 执行全部检查。界面检查会创建 WPF 对象并保存截图。

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

关闭旧程序，用新版exe替换原目录同名文件，保留“数据”文件夹。数据结构版本仍为1；应用版本为1.4.0.1，两者独立。完整JSON备份可恢复；CSV支持账目导入导出。

详见“升级日志-v1.3.txt”“使用说明.txt”“验证记录-v1.3.md”。发布包和源码包均不含用户账目。当前修复版已通过编译及169项自动检查，另通过所提供文件的5项专项检查。

## macOS 原生版

Mac 原生版也更新到 v1.4，支持同样的 CSV 导入与导出，与 Windows 版统一在 [v1.4 Release](https://github.com/NatsumeAiLover/EasyDailyLedger/releases/tag/v1.4) 下载。历史发布继续保留。

Mac 版使用 SwiftUI、Swift Charts、AppKit 和 Foundation，直接调用系统组件，不附带 Electron、Chromium 或 Node.js。面向 **macOS 27、Apple Silicon**，应用压缩包以发布附件标示的大小为准。

### 下载与安装

| 文件 | 用途 |
| --- | --- |
| [EasyDailyLedger-v1.4.exe](https://github.com/NatsumeAiLover/EasyDailyLedger/releases/download/v1.4/EasyDailyLedger-v1.4.exe) | Windows v1.4 程序 |
| [DailyLedger-1.4.0-macOS27-arm64.zip](https://github.com/NatsumeAiLover/EasyDailyLedger/releases/download/v1.4/DailyLedger-1.4.0-macOS27-arm64.zip) | Mac 原生应用和安装说明 |
| [DailyLedger-1.4.0-native-source.zip](https://github.com/NatsumeAiLover/EasyDailyLedger/releases/download/v1.4/DailyLedger-1.4.0-native-source.zip) | Mac 完整源码与构建流程 |
| [SHA256SUMS.txt](https://github.com/NatsumeAiLover/EasyDailyLedger/releases/download/v1.4/SHA256SUMS.txt) | 发布附件的 SHA256 校验值 |

Mac 下载后请完整解压，按包内说明运行“安装到我的应用.command”。程序已完成 ad-hoc 临时签名，**没有 Developer ID 签名或 Apple 公证**；如遇系统拦截，请先确认文件来源，再按说明安装。安装脚本不更改系统安全设置。

### 功能与数据迁移

Mac 版提供首页概览、每日账单、支出走势、设置与备份，支持日／月折线、上月／上年对比、预算、分类管理、三种主题以及 JSON 备份恢复。

数据保存在 `~/Library/Application Support/DailyLedgerNative`，与应用分开存放。迁移时，在 Windows 版导出完整 JSON 备份，再在 Mac 版的“设置与备份”中恢复。兼容 Windows v1.0—v1.4 的 Version=1 账本。JSON 恢复会替换当前数据并先保留原文件；CSV 可追加导入账目，但不包含预算等完整配置。数据没有加密，重要备份请另存到其他磁盘。

### 源码与验证

主分支的 [macos-native](macos-native) 目录包含 Mac 原生源码；[Mac 版说明](macos-native/README.md)提供本机编译步骤和构建流程介绍。原生版已在 GitHub 的 Mac 环境完成编译，核心及 CSV 回归检查通过后打包发布，并完成临时签名验证；尚未进行 macOS 27 真机和图形界面操作验收。

GitHub Actions 的“Build native macOS ledger”现在改为手动构建。需要发布时选择已有的同版本 Release（默认 `v1.4`），Mac 附件会更新到该版本下，不再按构建次数新增独立的 Mac 预发布条目。

