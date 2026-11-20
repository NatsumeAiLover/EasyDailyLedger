# 日常账本 v1.4 · Apple 原生版

SwiftUI 界面、Swift Charts 折线图、AppKit 文件选择与系统窗口、Foundation JSON/文件存储。没有 Electron、Chromium、Node 或 WebKit，也没有额外运行时或外部 Swift 依赖。

目标使用环境：macOS 27、Apple Silicon。Swift Package 的基础 API 部署版本为 macOS 14，便于在 GitHub 的 macOS 26 环境编译和运行非图形检查；最终应用 Info.plist 明确声明最低 macOS 27。这不等于已经在 macOS 27 真机验收。

## 使用发布包

下载 [v1.4 Release](https://github.com/NatsumeAiLover/EasyDailyLedger/releases/tag/v1.4) 中的“DailyLedger-1.4.0-macOS27-arm64.zip”，完整解压后运行安装脚本。构建过程已经完成 ad-hoc 签名验证，但没有 Developer ID 签名或 Apple 公证。详见“使用说明.txt”。

账目位置是 `~/Library/Application Support/DailyLedgerNative`。与之前 Electron 版独立，迁移时通过 JSON 完整备份恢复。兼容 Windows v1.0—v1.4 的 Version=1 数据字段，金额用 Decimal 读写、整数分计算。没有样例账目或云服务。

## 本机编译

在 Apple Silicon Mac 安装 Xcode 命令行工具后：

```sh
cd macos-native
swift test
bash scripts/build-macos.sh
```

发布文件在 `macos-native/release/`。构建脚本原生编译 ARM64、组装 `.app`、临时签名并验证、输出 ZIP 和 SHA256。`linked-frameworks.txt` 可核对系统依赖。没有执行图形界面测试。

## GitHub 自动构建

仓库根目录 `.github/workflows/macos-native.yml` 改为手动运行。进入 Actions，选择“Build native macOS ledger”，填写已有 Release 标签（默认 `v1.4`）。默认只生成构建附件；需要发布时再勾选 publish。标签须与应用版本匹配。

流程：Mac 环境运行 XCTest → Release 原生编译 → codesign 验证 → 打包匹配源码 → 上传构建附件。选择发布时，将 Mac 应用、源码和框架说明更新到已有的同版本 Release，合并 SHA256 校验清单并保留 Windows 等其他附件，不创建独立的 Mac 预发布条目。

Windows 和 Mac v1.4 下载统一放在同一个 v1.4 Release。主分支同时保留两平台源码；历史 v1.0、v1.1、v1.2 Release 继续保留。

## 功能

- 首页：收入/支出、月预算、日均、分类排行、最近账单。
- 每日账单：日期展开折叠、筛选搜索、排序、修改删除、CSV本月与全部账目导出。
- 支出走势：按日/按月、上月/上年虚线、悬停/点击/键盘选择和明细跳转。
- 设置：三种主题、分类、预算、CSV 追加导入与全部导出、完整 JSON 备份与恢复。
- 保护：单账本文件锁、原子写入、每次修改前快照、损坏读取不清空。

## 核心检查与界面验收

`Tests/LedgerCoreTests` 覆盖金额精度、JSON/Windows字段、预算、统计、日/月走势、闰年、未来空值、CSV、保存和备份保护。

界面没有使用 computer use。以下应在目标 Mac 上手动验收：安装和首次启动、原生文件面板、Swift Charts 悬停/选择/键盘/账单跳转、三种主题、屏幕缩放、关闭草稿提示、导入 Windows 备份及升级后持久化。发布包不含 Developer ID 凭证。

参考：[Apple Swift Charts](https://developer.apple.com/documentation/charts)、[GitHub Mac runners](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)。

## v1.4 CSV 导入与导出

“设置与备份”中可导入 CSV、导出全部账目；“每日账单”中可导出所选月份的全部账目，不受筛选影响。

```csv
日期,收支类别,类别,金额,备注
2024年1月2日,支出,餐饮,25.50,午饭
2024年1月3日,收入,工资 / 兼职,100.00,
```

导出日期为 `yyyy年M月d日`，月份和日期不补零。导入也接受补零的中文日期和旧版 `yyyy-MM-dd`。收支类别为“支出”或“收入”，兼容“收支类型”和旧版“类型”表头；类别和金额也兼容旧版“大类”“金额（元）”。

备注选填，可留空或省略整列；列顺序不限，“账户”等额外栏目忽略。导入自动识别逗号或制表符分列，支持 UTF-8、带 BOM 的 UTF-16 和 GBK/GB18030；导出为带 BOM 的 UTF-8 逗号分列文件。含逗号、双引号或换行的字段按标准 CSV 转义。

导入前校验整份文件并显示笔数及类别变化；确认后一次性追加，保留原账目和预算，并先备份当前账本。未知类别按收支分别添加，已停用类别重新启用。取消、解析失败或保存失败不会部分导入。重复导入同一文件会重复记账。

日期范围为1900年1月1日至今天；金额大于零、最多两位小数且不超过1亿元；类别最多30个字符，备注最多200个字符。CSV 不包含预算和未使用的类别，完整迁移仍使用 JSON 备份。

升级时退出旧版应用，再用 v1.4 替换应用；原 `~/Library/Application Support/DailyLedgerNative` 数据目录继续保留。收入和支出的备注均可填写，CSV 中已有备注在编辑保存后保留。
