import SwiftUI
import Charts
import LedgerCore

struct LedgerCard<Content: View>: View {
    let content: Content
    init(@ViewBuilder content: () -> Content) { self.content = content() }
    var body: some View { VStack(alignment: .leading, spacing: 14) { content }.padding(20).frame(maxWidth: .infinity, alignment: .leading).background(.background, in: RoundedRectangle(cornerRadius: 12)).overlay(RoundedRectangle(cornerRadius: 12).stroke(Color(nsColor: .separatorColor).opacity(0.25))) }
}
struct StatCard: View {
    var title: String, value: String, hint: String = ""
    var body: some View {
        LedgerCard {
            Text(title).foregroundStyle(.secondary).font(.callout)
            Text(value).font(.system(size: 27, weight: .medium)).monospacedDigit().textSelection(.enabled)
            if !hint.isEmpty { Text(hint).font(.caption).foregroundStyle(.secondary) }
        }
    }
}
struct RootView: View {
    @EnvironmentObject var model: LedgerModel
    var body: some View {
        NavigationSplitView {
            VStack(alignment: .leading) {
                Text("日常账本").font(.title2.bold()).padding(.horizontal, 18).padding(.top, 20)
                Text("把每一天，记清楚。").font(.caption).foregroundStyle(.secondary).padding(.horizontal, 18)
                List(LedgerPage.allCases) { page in
                    Button { model.page = page } label: { Label(page.rawValue, systemImage: page.symbol).frame(maxWidth: .infinity, alignment: .leading).padding(.vertical, 7) }
                        .buttonStyle(.plain).listRowBackground(model.page == page ? model.theme.accent.opacity(0.16) : Color.clear)
                }
                Picker("外观", selection: Binding(get: { model.theme }, set: { model.saveTheme($0) })) { ForEach(LedgerTheme.allCases) { Text($0.title).tag($0) } }.labelsHidden().padding(.horizontal)
                Text("本机保存 · Apple 原生\nDaily Ledger 1.3").font(.caption2).foregroundStyle(.secondary).padding(18)
            }.navigationSplitViewColumnWidth(min: 168, ideal: 180, max: 210)
        } detail: {
            VStack(alignment: .leading, spacing: 18) {
                header
                if !model.ready { recovery }
                else { pageContent }
                Text(model.status).font(.caption2).foregroundStyle(.secondary).textSelection(.enabled)
            }
            .padding(24)
            .background(model.theme == .green ? Color(red: 0.075, green: 0.11, blue: 0.09) : Color(nsColor: .windowBackgroundColor))
            .navigationTitle(model.page.rawValue)
        }
        .alert("操作未完成", isPresented: Binding(get: { model.error != nil }, set: { if !$0 { model.error = nil } })) { Button("好", role: .cancel) { model.error = nil } } message: { Text(model.error ?? "") }
    }
    private var header: some View {
        HStack {
            VStack(alignment: .leading, spacing: 5) {
                Text(model.page == .home ? "本月概览" : model.page.rawValue).font(.largeTitle.weight(.semibold))
                Text(model.page == .trends ? "按日、按月比较开支，找到支出变化。" : model.month == LedgerDate.month() ? "截至今天 · \(LedgerDate.key())" : "历史月份 · 整月汇总").font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            if model.page != .trends { MonthPicker(month: Binding(get: { model.month }, set: { model.changeMonth($0) })) }
        }
    }
    private var recovery: some View {
        VStack(alignment: .leading, spacing: 16) {
            Label("账本未能读取", systemImage: "exclamationmark.triangle").font(.title2)
            Text(model.loadError).textSelection(.enabled)
            HStack { Button("从 JSON 备份恢复") { model.restore() }.disabled(!model.canRestore); Button("打开数据文件夹") { model.openFolder() } }
        }.padding(24).frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }
    @ViewBuilder private var pageContent: some View {
        switch model.page { case .home: HomeView(); case .ledger: LedgerView(); case .trends: TrendView(period: $model.trendPeriod, monthly: $model.trendMonthly, compare: $model.trendCompare, selected: $model.trendSelected); case .settings: LedgerSettingsView() }
    }
}

struct MonthPicker: View {
    @Binding var month: String
    var body: some View {
        HStack(spacing: 8) {
            Button { month = LedgerDate.shifted(month, by: -1) } label: { Image(systemName: "chevron.left") }.disabled(month <= "1900-01").help("上个月")
            DatePicker("查看月份", selection: Binding(get: { LedgerDate.parse(month + "-01") ?? Date() }, set: { month = min(LedgerDate.month($0), LedgerDate.month()) }), in: (LedgerDate.parse("1900-01-01") ?? Date())...Date(), displayedComponents: .date).labelsHidden().datePickerStyle(.field).frame(width: 130)
            Button { month = LedgerDate.shifted(month, by: 1) } label: { Image(systemName: "chevron.right") }.disabled(month >= LedgerDate.month()).help("下个月")
            Button("本月") { month = LedgerDate.month() }
        }
    }
}

struct HomeView: View {
    @EnvironmentObject var model: LedgerModel
    private var s: MonthSummary { model.summary }
    private var comparison: String {
        guard s.hasPrevious else { return "暂无上月支出记录" }
        let delta = s.spent - s.previousSpent, label = model.month == LedgerDate.month() ? "较上月同期" : "较上月整月"
        return label + (delta == 0 ? "持平" : (delta > 0 ? "多 " : "少 ") + Money.display(abs(delta)))
    }
    private var allowance: String {
        guard let remaining = s.remaining else { return "设置预算后计算每天可用金额" }
        if remaining < 0 { return "本月预算已用完" }
        if model.month != LedgerDate.month() { return "该月预算节余" }
        return s.remainingDays > 0 ? "明日起每天可用 " + Money.display(remaining / Int64(s.remainingDays)) : "今天是本月最后一天"
    }
    var body: some View {
        ScrollView {
            VStack(spacing: 18) {
                HStack(alignment: .top, spacing: 14) {
                    StatCard(title: "本月已支出", value: Money.display(s.spent), hint: comparison)
                    StatCard(title: (s.remaining ?? 0) < 0 ? "已超预算" : "预算还剩", value: s.remaining.map { Money.display(abs($0)) } ?? "待设置", hint: allowance)
                    StatCard(title: "本月日均支出", value: Money.display(s.average), hint: "按自然天数计算，共 \(s.elapsed) 天")
                }
                budget
                HStack(alignment: .top, spacing: 18) {
                    VStack(alignment: .leading, spacing: 18) {
                        CategoryBars()
                        LedgerCard { HStack { Text("最近的每日账单").font(.headline); Spacer(); Button("全部账单 →") { model.page = .ledger } }; DayGroups(rows: model.monthRows, recent: true) }
                        Text("本月收入 \(Money.display(s.income))　收支结余 \(Money.display(s.net))").font(.caption).foregroundStyle(.secondary)
                    }.frame(maxWidth: .infinity)
                    EntryForm().frame(width: 270)
                }
            }.padding(.bottom, 8)
        }
    }
    private var budget: some View {
        VStack(spacing: 8) {
            HStack { Text("月预算 " + (s.budget.map(Money.display) ?? "尚未设置")).font(.caption).foregroundStyle(.secondary); Spacer(); Button("设置 / 修改预算") { model.page = .settings } }
            ProgressView(value: s.budget.map { $0 > 0 ? min(1, Double(s.spent) / Double($0)) : s.spent > 0 ? 1 : 0 } ?? 0).tint((s.remaining ?? 0) < 0 ? .red : model.theme.accent)
        }.padding(.horizontal, 2)
    }
}

struct EntryForm: View {
    @EnvironmentObject var model: LedgerModel
    private var categories: [LedgerCategory] { model.data.Categories.filter { $0.Kind == model.draft.kind && ($0.Active || $0.Id == model.draft.category) } }
    var body: some View {
        LedgerCard {
            Text(model.draft.id == nil ? "记一笔" : "修改账目").font(.headline)
            Picker("类型", selection: Binding(get: { model.draft.kind }, set: { model.setKind($0) })) { Text("支出").tag("expense"); Text("收入").tag("income") }.pickerStyle(.segmented)
            VStack(alignment: .leading) { Text("金额（元）").font(.caption).foregroundStyle(.secondary); TextField("0.00", text: $model.draft.amount).font(.title2).textFieldStyle(.roundedBorder) }
            Picker("大类", selection: $model.draft.category) { ForEach(categories) { Text($0.Name + ($0.Active ? "" : " · 已停用")).tag($0.Id) } }
            if model.draft.kind == "expense" { TextField("做了什么 · 选填", text: $model.draft.note).textFieldStyle(.roundedBorder) }
            DatePicker("日期", selection: $model.draft.date, in: (LedgerDate.parse("1900-01-01") ?? Date())...Date(), displayedComponents: .date)
            Button(model.draft.id == nil ? "保存这笔\(model.draft.kind == "expense" ? "支出" : "收入")" : "保存修改") { model.saveEntry() }.buttonStyle(.borderedProminent).frame(maxWidth: .infinity)
            if model.draft.id != nil { Button("取消修改") { if model.discardDraft() { model.resetDraft() } } }
        }
    }
}

struct CategoryBars: View {
    @EnvironmentObject var model: LedgerModel
    private var groups: [(String, Int64)] {
        let grouped = Dictionary(grouping: model.monthRows.filter { $0.Kind == "expense" }, by: { $0.CategoryId })
        return grouped.map { ($0.key, $0.value.reduce(Int64(0)) { $0 + $1.cents }) }.sorted { $0.1 > $1.1 }
    }
    var body: some View {
        LedgerCard {
            Text("钱花在哪里").font(.headline)
            if groups.isEmpty { Text("还没有支出，记下第一笔吧。").foregroundStyle(.secondary) }
            ForEach(groups, id: \.0) { group in
                HStack(spacing: 10) {
                    Text(model.data.categoryName(group.0)).font(.caption).frame(width: 65, alignment: .leading)
                    ProgressView(value: Double(group.1), total: Double(max(1, model.summary.spent)))
                    Text(Money.display(group.1)).font(.caption).monospacedDigit()
                }
            }
        }
    }
}

struct LedgerView: View {
    @EnvironmentObject var model: LedgerModel
    var body: some View {
        VStack(spacing: 16) {
            LedgerCard {
                HStack { Text("支出 \(Money.display(model.summary.spent))　收入 \(Money.display(model.summary.income))　结余 \(Money.display(model.summary.net))"); Spacer(); Button("导出本月表格") { model.exportCSV() } }
            }
            HStack(spacing: 12) {
                Picker("类型", selection: $model.filterKind) { Text("全部收支").tag(""); Text("仅支出").tag("expense"); Text("仅收入").tag("income") }.frame(width: 130).onChange(of: model.filterKind) { _, _ in model.filterCategory = "" }
                Picker("大类", selection: $model.filterCategory) { Text("全部大类").tag(""); ForEach(model.data.Categories.filter { model.filterKind.isEmpty || $0.Kind == model.filterKind }) { Text($0.Name + ($0.Kind == "expense" ? "（支出）" : "（收入）") + ($0.Active ? "" : " · 已停用")).tag($0.Id) } }.frame(width: 200)
                TextField("搜索备注、分类或日期", text: $model.search).textFieldStyle(.roundedBorder)
                Button(model.newestFirst ? "日期 ↓ 近到远" : "日期 ↑ 远到近") { model.newestFirst.toggle() }
            }.labelsHidden()
            ScrollView { LedgerCard { DayGroups(rows: model.filteredRows, recent: false) } }
            Text("当前显示 \(model.filteredRows.count) 笔 · 支出 \(Money.display(model.filteredRows.filter { $0.Kind == "expense" }.reduce(0) { $0 + $1.cents })) · 收入 \(Money.display(model.filteredRows.filter { $0.Kind == "income" }.reduce(0) { $0 + $1.cents }))").font(.caption).foregroundStyle(.secondary).frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}

struct DayGroups: View {
    @EnvironmentObject var model: LedgerModel
    let rows: [LedgerEntry], recent: Bool
    private var dates: [String] {
        let sorted = Array(Set(rows.map { $0.Date })).sorted { recent || model.newestFirst ? $0 > $1 : $0 < $1 }
        return recent ? Array(sorted.prefix(5)) : sorted
    }
    private func isExpanded(_ date: String) -> Binding<Bool> {
        Binding(get: { model.expanded[date] ?? (date == rows.map { $0.Date }.max()) }, set: { model.expanded[date] = $0 })
    }
    var body: some View {
        if dates.isEmpty { Text("没有符合条件的账目。").foregroundStyle(.secondary).frame(maxWidth: .infinity).padding(25) }
        ForEach(dates, id: \.self) { date in
            DisclosureGroup(isExpanded: isExpanded(date)) {
                ForEach(rows.filter { $0.Date == date }) { entry in EntryRowView(entry: entry) }
            } label: {
                HStack(spacing: 14) { Text(date).fontWeight(.medium); Text(dayTotal(date)).font(.caption).foregroundStyle(.secondary) }.padding(.vertical, 7)
            }
            Divider()
        }
    }
    private func dayTotal(_ date: String) -> String {
        let full = model.data.Entries.filter { $0.Date == date }
        let spent = full.filter { $0.Kind == "expense" }.reduce(Int64(0)) { $0 + $1.cents }, income = full.filter { $0.Kind == "income" }.reduce(Int64(0)) { $0 + $1.cents }
        return "全天支出 \(Money.display(spent))　收入 \(Money.display(income))"
    }
}
struct EntryRowView: View {
    @EnvironmentObject var model: LedgerModel
    let entry: LedgerEntry
    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Text(entry.Kind == "expense" ? "支出" : "收入").foregroundStyle(.secondary).frame(width: 32)
            Text(model.data.categoryName(entry.CategoryId)).frame(width: 65, alignment: .leading)
            Text(Money.display(entry.cents)).monospacedDigit().frame(minWidth: 82, alignment: .trailing)
            Text(entry.Note).frame(maxWidth: .infinity, alignment: .leading).textSelection(.enabled)
            Button("修改") { model.edit(entry) }; Button("删除", role: .destructive) { model.delete(entry) }
        }.font(.caption).padding(.vertical, 6)
    }
}

struct TrendView: View {
    @EnvironmentObject var model: LedgerModel
    @Binding var period: String
    @Binding var monthly: Bool
    @Binding var compare: Bool
    @Binding var selected: Int?
    @State private var hovered: Int? = nil
    private var trend: ExpenseTrend { LedgerLogic.trends(model.data, month: period, monthly: monthly) }
    private var activePoint: ExpensePoint? { guard let index = hovered ?? selected, trend.current.indices.contains(index - 1) else { return nil }; return trend.current[index - 1] }
    private var label: String { monthly ? String(period.prefix(4)) + " 年" : LedgerDate.label(period) }
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                controls
                HStack(alignment: .top, spacing: 14) {
                    StatCard(title: "所选期间总支出", value: Money.display(trend.total), hint: label)
                    StatCard(title: monthly ? "最高支出月份" : "最高支出日期", value: trend.peak.map { "\(monthly ? LedgerDate.label(String($0.date.prefix(7))) : $0.date) · \(Money.display($0.cents))" } ?? "暂无支出", hint: "并列时显示最早的日期或月份")
                }
                LedgerCard {
                    legend
                    nativeChart
                    Text(pointDescription(activePoint)).font(.callout).monospacedDigit().textSelection(.enabled)
                    if let index = hovered ?? selected, compare, trend.hasPrevious, trend.previous.indices.contains(index - 1) {
                        Text("对比 " + pointDescription(trend.previous[index - 1])).font(.caption).foregroundStyle(.secondary)
                    }
                    Button("查看对应账单 →") { if let point = activePoint { model.drillDown(point, monthly: monthly) } }.disabled(activePoint == nil)
                }
                Text("只汇总支出，收入不抵扣。过去无支出记录按 0 显示，未来和不存在的日期留空。\n本月只到今天，对比线保留历史完整期间，两期时长可能不同。\n移动鼠标查看详情，点击固定选择；也可聚焦图表后使用左右方向键。").font(.caption).foregroundStyle(.secondary)
            }.padding(.bottom, 8)
        }
    }
    private var controls: some View {
        HStack(spacing: 16) {
            Picker("统计方式", selection: $monthly) { Text("按日").tag(false); Text("按月").tag(true) }.pickerStyle(.segmented).frame(width: 150).onChange(of: monthly) { _, _ in selected = nil; hovered = nil }
            if monthly {
                Picker("年份", selection: Binding(get: { Int(period.prefix(4)) ?? 2026 }, set: { period = String($0) + "-01"; selected = nil })) { ForEach(Array((1900...(Calendar.current.component(.year, from: Date()))).reversed()), id: \.self) { Text(String($0)).tag($0) } }.frame(width: 130)
            } else { MonthPicker(month: Binding(get: { period }, set: { period = $0; selected = nil; hovered = nil })) }
            if monthly {
                Button { period = LedgerDate.shifted(period, by: -12); selected = nil } label: { Image(systemName: "chevron.left") }.disabled(period.prefix(4) <= "1900")
                Button { period = min(LedgerDate.shifted(period, by: 12), LedgerDate.month()); selected = nil } label: { Image(systemName: "chevron.right") }.disabled(period.prefix(4) >= LedgerDate.month().prefix(4))
                Button("今年") { period = LedgerDate.month(); selected = nil }
            }
            Toggle(monthly ? "对比上年" : "对比上月", isOn: $compare).toggleStyle(.checkbox)
            Spacer(minLength: 0)
        }
    }
    private var legend: some View {
        HStack(spacing: 18) {
            Label("实线：" + label, systemImage: "line.diagonal").foregroundStyle(model.theme.accent)
            if compare { Text(trend.hasPrevious ? "虚线：" + (monthly ? String(trend.previousPeriod.prefix(4)) + " 年" : LedgerDate.label(trend.previousPeriod)) : "暂无对比支出记录").foregroundStyle(.secondary) }
            Spacer()
        }.font(.caption)
    }
    private var nativeChart: some View {
        Chart {
            if compare && trend.hasPrevious {
                ForEach(trend.previous.compactMap { $0 }) { point in
                    LineMark(x: .value("序号", point.index), y: .value("金额（元）", Double(point.cents) / 100), series: .value("期间", "对比"))
                        .foregroundStyle(Color.secondary).lineStyle(StrokeStyle(lineWidth: 1.5, dash: [6, 4])).interpolationMethod(.linear)
                }
            }
            ForEach(trend.current.compactMap { $0 }) { point in
                LineMark(x: .value("序号", point.index), y: .value("金额（元）", Double(point.cents) / 100), series: .value("期间", "所选期间"))
                    .foregroundStyle(model.theme.accent).lineStyle(StrokeStyle(lineWidth: 2.5)).interpolationMethod(.linear)
                PointMark(x: .value("序号", point.index), y: .value("金额（元）", Double(point.cents) / 100)).foregroundStyle(model.theme.accent).symbolSize(18)
            }
            if let index = hovered ?? selected { RuleMark(x: .value("选择", index)).foregroundStyle(Color.secondary.opacity(0.4)) }
        }
        .chartXScale(domain: 1...max(2, trend.current.count))
        .chartYScale(domain: .automatic(includesZero: true))
        .chartXAxis { AxisMarks(values: .stride(by: monthly ? 1 : 5)) { value in AxisGridLine(); AxisTick(); AxisValueLabel { if let index = value.as(Int.self) { Text("\(index)\(monthly ? "月" : "日")") } } } }
        .chartYAxisLabel("金额 / 元")
        .frame(height: 330)
        .chartOverlay { proxy in
            GeometryReader { geometry in
                Rectangle().fill(.clear).contentShape(Rectangle())
                    .onContinuousHover { phase in
                        switch phase { case .active(let location): hovered = chartIndex(location.x, proxy: proxy, geometry: geometry); case .ended: hovered = nil }
                    }
                    .onTapGesture { location in selected = chartIndex(location.x, proxy: proxy, geometry: geometry) }
            }
        }
        .focusable()
        .onMoveCommand { direction in
            let end = trend.current.lastIndex(where: { $0 != nil }).map { $0 + 1 } ?? 1
            if direction == .left || direction == .right { hovered = nil; selected = max(1, min(end, (selected ?? 1) + (direction == .left ? -1 : 1))) }
        }
        .accessibilityLabel("支出走势折线图")
    }
    private func chartIndex(_ x: CGFloat, proxy: ChartProxy, geometry: GeometryProxy) -> Int? {
        guard let anchor = proxy.plotFrame else { return nil }
        let frame = geometry[anchor], local = x - frame.minX
        guard local >= 0, local <= frame.width, let value: Double = proxy.value(atX: local) else { return nil }
        return max(1, min(trend.current.count, Int(value.rounded())))
    }
    private func pointDescription(_ point: ExpensePoint?) -> String {
        guard let point else { return "请选择日期或月份；未来或不存在的日期没有数据。" }
        return (monthly ? LedgerDate.label(String(point.date.prefix(7))) : point.date) + " · " + Money.display(point.cents) + " · \(point.count) 笔" + (point.count == 0 ? "（无支出记录）" : "")
    }
}

struct LedgerSettingsView: View {
    @EnvironmentObject var model: LedgerModel
    @State private var budget = ""
    @State private var kind = "expense"
    @State private var categoryID = ""
    @State private var categoryName = ""
    private var selectedCategory: LedgerCategory? { model.data.Categories.first { $0.Id == categoryID } }
    var body: some View {
        ScrollView {
            VStack(spacing: 18) {
                LedgerCard {
                    Text("外观").font(.headline)
                    Picker("主题", selection: Binding(get: { model.theme }, set: { model.saveTheme($0) })) { ForEach(LedgerTheme.allCases) { Text($0.title).tag($0) } }.pickerStyle(.segmented)
                    Text("使用 SwiftUI 原生控件、SF Symbols 与 Swift Charts。").font(.caption).foregroundStyle(.secondary)
                }
                LedgerCard {
                    Text(LedgerDate.label(model.month) + "的预算").font(.headline)
                    Text("收入不自动增加预算；新月份沿用最近一个月预算，剩余不累加。").font(.caption).foregroundStyle(.secondary)
                    HStack { TextField("预算金额（元）", text: $budget).textFieldStyle(.roundedBorder).frame(maxWidth: 240); Button("保存预算") { model.saveBudget(budget) }.buttonStyle(.borderedProminent) }
                }
                LedgerCard {
                    Text("管理大类").font(.headline)
                    HStack {
                        Picker("类型", selection: $kind) { Text("支出").tag("expense"); Text("收入").tag("income") }.frame(width: 130).onChange(of: kind) { _, _ in selectFirstCategory() }
                        Picker("分类", selection: $categoryID) { ForEach(model.data.Categories.filter { $0.Kind == kind }) { Text($0.Name + ($0.Active ? "" : " · 已停用")).tag($0.Id) } }.frame(width: 200).onChange(of: categoryID) { _, _ in categoryName = selectedCategory?.Name ?? "" }
                    }
                    HStack {
                        TextField("分类名称", text: $categoryName).textFieldStyle(.roundedBorder)
                        Button("新增") { model.category(action: "add", id: "", kind: kind, name: categoryName) }
                        Button("改名") { model.category(action: "rename", id: categoryID, kind: kind, name: categoryName) }
                        Button(selectedCategory?.Active == true ? "停用" : "启用") { model.category(action: "toggle", id: categoryID, kind: kind, name: "") }
                    }
                    Text("停用保留历史账目，收入和支出各需至少一个启用大类。").font(.caption).foregroundStyle(.secondary)
                }
                LedgerCard {
                    Text("备份与恢复").font(.headline)
                    Text("可导入 Windows v1.0—v1.3 或 Electron 版完整 JSON 备份。恢复会替换当前账本，并先保留原文件。").font(.caption).foregroundStyle(.secondary)
                    HStack { Button("备份全部数据") { model.backup() }.buttonStyle(.borderedProminent); Button("从 JSON 备份恢复") { model.restore() }; Button("打开数据文件夹") { model.openFolder() }; Button("查看自动备份") { model.openFolder(backups: true) } }
                    Text(model.directory?.path ?? "").font(.caption).foregroundStyle(.secondary).textSelection(.enabled)
                    Text("应用与数据分开保存。账目没有加密，重要备份应另存到其他磁盘。").font(.caption).foregroundStyle(.secondary)
                }
                LedgerCard {
                    Text("统计口径").font(.headline)
                    Text("预算剩余 = 月预算 − 支出；收支结余 = 收入 − 支出，不代表银行余额。\n日均支出按月初至今天的自然天数计算；历史月份按整月计算。\n剩余每天可用按明天至月底计算。本月首页对比上月同期，走势虚线保留历史完整期间。").font(.caption).foregroundStyle(.secondary)
                    Text("日常账本 1.3.0 · Apple 原生版").font(.caption2).foregroundStyle(.secondary)
                }
            }.padding(.bottom, 8)
        }
        .onAppear { refreshBudget(); selectFirstCategory() }
        .onChange(of: model.month) { _, _ in refreshBudget() }
    }
    private func refreshBudget() { budget = model.summary.budget.map(Money.input) ?? "" }
    private func selectFirstCategory() { categoryID = model.data.Categories.first { $0.Kind == kind }?.Id ?? ""; categoryName = selectedCategory?.Name ?? "" }
}
