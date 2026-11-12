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
