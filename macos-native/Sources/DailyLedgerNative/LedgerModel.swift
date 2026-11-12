import SwiftUI
import AppKit
import UniformTypeIdentifiers
import LedgerCore
import Darwin

enum LedgerPage: String, CaseIterable, Identifiable {
    case home = "首页概览", ledger = "每日账单", trends = "支出走势", settings = "设置与备份"
    var id: String { rawValue }
    var symbol: String {
        switch self { case .home: return "square.grid.2x2"; case .ledger: return "list.bullet.rectangle"; case .trends: return "chart.xyaxis.line"; case .settings: return "gearshape" }
    }
}
enum LedgerTheme: String, CaseIterable, Identifiable {
    case light, dark, green
    var id: String { rawValue }
    var title: String { self == .light ? "浅色" : self == .dark ? "深色 · 黑灰" : "深色 · 墨绿" }
    var accent: Color { self == .dark ? Color(white: 0.78) : self == .green ? Color(red: 0.56, green: 0.82, blue: 0.70) : Color(red: 0.13, green: 0.41, blue: 0.36) }
}
struct EntryDraft: Equatable {
    var id: String? = nil
    var kind = "expense", amount = "", category = "", note = ""
    var date = Date()
    var dirty: Bool { id != nil || !amount.trimmingCharacters(in: .whitespaces).isEmpty || !note.trimmingCharacters(in: .whitespaces).isEmpty }
}

@MainActor final class LedgerModel: ObservableObject {
    @Published var data = LedgerData()
    @Published var ready = false
    @Published var loadError = ""
    @Published var error: String?
    @Published var page: LedgerPage = .home
    @Published var month = LedgerDate.month()
    @Published var theme: LedgerTheme = .light
    @Published var draft = EntryDraft()
    @Published var filterKind = "", filterCategory = "", search = ""
    @Published var newestFirst = true
    @Published var expanded: [String: Bool] = [:]
    @Published var trendPeriod = LedgerDate.month()
    @Published var trendMonthly = false, trendCompare = false
    @Published var trendSelected: Int? = nil
    @Published var status = "本机保存 · 无需联网"
    private var fileStore: LedgerFileStore?
    private var lockFD: Int32 = -1
    var directory: URL? { fileStore?.directory }
    var canRestore: Bool { fileStore != nil && lockFD >= 0 }
    var summary: MonthSummary { LedgerLogic.summary(data, month: month) }
    var monthRows: [LedgerEntry] { data.Entries.filter { $0.Date.hasPrefix(month + "-") } }
    var filteredRows: [LedgerEntry] {
        monthRows.filter { e in
            (filterKind.isEmpty || e.Kind == filterKind) && (filterCategory.isEmpty || e.CategoryId == filterCategory) &&
            (search.isEmpty || (e.Date + " " + e.Note + " " + data.categoryName(e.CategoryId)).localizedCaseInsensitiveContains(search))
        }
    }
    init() {
        do {
            let support = try FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true)
            let dir = support.appendingPathComponent("DailyLedgerNative", isDirectory: true)
            let store = try LedgerFileStore(directory: dir)
            let fd = open(dir.appendingPathComponent("账本.lock").path, O_CREAT | O_RDWR, mode_t(0o600))
            guard fd >= 0 else { throw LedgerError("无法取得账本文件锁。") }
            guard flock(fd, LOCK_EX | LOCK_NB) == 0 else { close(fd); throw LedgerError("日常账本正在另一个窗口或进程中使用，请先关闭它。") }
            lockFD = fd; fileStore = store
            if let bytes = try? Data(contentsOf: dir.appendingPathComponent("外观设置.json")),
               let object = try? JSONSerialization.jsonObject(with: bytes) as? [String: Any],
               let value = object["Theme"] as? String, let selected = LedgerTheme(rawValue: value) { theme = selected }
            data = try store.load(); ready = true; resetDraft()
        } catch { loadError = "账本未能读取，原文件保持原样。\n" + error.localizedDescription }
    }
    deinit { if lockFD >= 0 { flock(lockFD, LOCK_UN); close(lockFD) } }
    func perform(_ work: () throws -> Void) { do { try work() } catch { self.error = error.localizedDescription } }
    @discardableResult func commit(_ change: (inout LedgerData) throws -> Void) -> Bool {
        do {
            guard ready, let store = fileStore else { throw LedgerError("请先恢复有效的账本备份。") }
            var next = data; try change(&next); try store.save(next); data = next
            status = "已保存至本机 · " + Date().formatted(date: .omitted, time: .shortened)
            return true
        } catch { self.error = error.localizedDescription; return false }
    }
    func resetDraft() { draft = EntryDraft(); draft.category = data.Categories.first { $0.Kind == "expense" && $0.Active }?.Id ?? "" }
    func setKind(_ kind: String) {
        draft.kind = kind; draft.category = data.Categories.first { $0.Kind == kind && $0.Active }?.Id ?? ""
    }
    func saveEntry() {
        do {
            let date = LedgerDate.key(draft.date), cents = try Money.cents(draft.amount)
            guard date <= LedgerDate.key(), LedgerDate.parse(date) != nil, draft.note.count <= 200 else { throw LedgerError("请选择今天或过去的日期，备注最多200字。") }
            let saved = draft
            if commit({ next in
                let original = saved.id.flatMap { id in next.Entries.first { $0.Id == id } }
                guard saved.id == nil || original != nil else { throw LedgerError("原账目不存在。") }
                guard let category = next.Categories.first(where: { $0.Id == saved.category && $0.Kind == saved.kind }), category.Active || original?.CategoryId == category.Id else { throw LedgerError("请选择启用的大类。") }
                var entry = original ?? LedgerEntry(date: date, kind: saved.kind, category: saved.category, cents: cents, note: "")
                entry.Date = date; entry.Kind = saved.kind; entry.CategoryId = saved.category; entry.Amount = Decimal(cents) / 100; entry.Note = saved.kind == "expense" ? saved.note : ""
                if let index = next.Entries.firstIndex(where: { $0.Id == entry.Id }) { next.Entries[index] = entry } else { next.Entries.append(entry) }
                LedgerLogic.inheritBudget(&next, month: String(date.prefix(7)))
            }) { month = String(date.prefix(7)); resetDraft() }
        } catch { self.error = error.localizedDescription }
    }
    func confirm(_ title: String, detail: String, action: String) -> Bool {
        let alert = NSAlert(); alert.messageText = title; alert.informativeText = detail; alert.alertStyle = .warning
        alert.addButton(withTitle: "取消"); alert.addButton(withTitle: action)
        return alert.runModal() == .alertSecondButtonReturn
    }
    func discardDraft() -> Bool { !draft.dirty || confirm("还有未保存的账目", detail: "确定放弃记账框中的内容吗？", action: "放弃") }
    func edit(_ entry: LedgerEntry) {
        guard discardDraft() else { return }
        draft = EntryDraft(id: entry.Id, kind: entry.Kind, amount: Money.input(entry.cents), category: entry.CategoryId, note: entry.Note, date: LedgerDate.parse(entry.Date) ?? Date())
        page = .home
    }
    func delete(_ entry: LedgerEntry) {
        guard confirm("删除这笔账目？", detail: "\(entry.Date) · \(data.categoryName(entry.CategoryId)) · \(Money.display(entry.cents))\n删除前账本会保留在自动备份中。", action: "删除") else { return }
        if commit({ $0.Entries.removeAll { $0.Id == entry.Id } }), draft.id == entry.Id { resetDraft() }
    }
    func changeMonth(_ value: String) { if LedgerDate.validMonth(value), value >= "1900-01", value <= LedgerDate.month() { month = value } }
    func saveBudget(_ amount: String) {
        perform { let value = try Money.cents(amount, allowZero: true); _ = commit { $0.Budgets[month] = Decimal(value) / 100 } }
    }
    func saveTheme(_ value: LedgerTheme) {
        perform {
            guard let directory else { throw LedgerError("数据目录不可用。") }
            let bytes = try JSONSerialization.data(withJSONObject: ["Version": 1, "Theme": value.rawValue])
            try bytes.write(to: directory.appendingPathComponent("外观设置.json"), options: .atomic); theme = value
        }
    }
    func category(action: String, id: String, kind: String, name: String) {
        _ = commit { next in
            if action == "add" { next.Categories.append(LedgerCategory(name: name.trimmingCharacters(in: .whitespacesAndNewlines), kind: kind)) }
            else {
                guard let index = next.Categories.firstIndex(where: { $0.Id == id }) else { throw LedgerError("请选择分类。") }
                if action == "rename" { next.Categories[index].Name = name.trimmingCharacters(in: .whitespacesAndNewlines) }
                else { next.Categories[index].Active.toggle() }
            }
        }
    }
    func backup() {
        perform {
            guard ready else { throw LedgerError("账本尚未成功读取。") }
            let panel = NSSavePanel(); panel.allowedContentTypes = [.json]; panel.nameFieldStringValue = "日常账本备份-\(LedgerDate.key()).json"
            if panel.runModal() == .OK, let url = panel.url { try checkExportURL(url); try LedgerLogic.encode(data).write(to: url, options: .atomic); status = "已保存完整备份" }
        }
    }
    func restore() {
        guard canRestore, discardDraft() else { return }
        perform {
            let panel = NSOpenPanel(); panel.allowedContentTypes = [.json]; panel.canChooseDirectories = false; panel.allowsMultipleSelection = false
            guard panel.runModal() == .OK, let url = panel.url, let store = fileStore else { return }
            let restored = try store.read(url)
            guard confirm("用备份替换当前账本？", detail: "备份有 \(restored.Entries.count) 笔账目、\(restored.Budgets.count) 个月预算。当前文件会先保留为自动备份，数据不会合并。", action: "恢复") else { return }
            try store.save(restored); data = restored; ready = true; loadError = ""; resetDraft(); expanded = [:]; filterKind = ""; filterCategory = ""; search = ""; status = "已恢复完整备份"
        }
    }
    private func checkExportURL(_ url: URL) throws {
        guard let store = fileStore, url.standardizedFileURL != store.file.standardizedFileURL else { throw LedgerError("请勿覆盖正在使用的账本文件。") }
    }
    func exportCSV() {
        perform {
            let panel = NSSavePanel(); panel.allowedContentTypes = [.commaSeparatedText]; panel.nameFieldStringValue = "账单-\(month).csv"
            if panel.runModal() == .OK, let url = panel.url { try checkExportURL(url); try Data(LedgerLogic.csv(data, month: month).utf8).write(to: url, options: .atomic); status = "已导出整月账单" }
        }
    }
    func openFolder(backups: Bool = false) {
        perform { guard let store = fileStore else { return }; let url = backups ? store.backups : store.directory; try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true); NSWorkspace.shared.open(url) }
    }
    func drillDown(_ point: ExpensePoint, monthly: Bool) {
        month = String(point.date.prefix(7)); filterKind = "expense"; filterCategory = ""; search = monthly ? "" : point.date
        if !monthly { expanded[point.date] = true }; page = .ledger
    }
}

@MainActor final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    weak var model: LedgerModel?
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply { model?.discardDraft() == false ? .terminateCancel : .terminateNow }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
    func windowShouldClose(_ sender: NSWindow) -> Bool {
        guard model?.discardDraft() != false else { return false }
        model?.resetDraft(); return true
    }
}

struct NativeWindowHook: NSViewRepresentable {
    let delegate: AppDelegate
    func makeNSView(context: Context) -> NSView { let view = NSView(); DispatchQueue.main.async { view.window?.delegate = delegate }; return view }
    func updateNSView(_ nsView: NSView, context: Context) { DispatchQueue.main.async { nsView.window?.delegate = delegate } }
}

@main struct DailyLedgerApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @StateObject private var model = LedgerModel()
    var body: some Scene {
        WindowGroup("日常账本") {
            RootView().environmentObject(model)
                .preferredColorScheme(model.theme == .light ? .light : .dark)
                .tint(model.theme.accent)
                .frame(minWidth: 960, minHeight: 680)
                .background(NativeWindowHook(delegate: delegate))
                .onAppear { delegate.model = model }
        }
        .defaultSize(width: 1220, height: 850)
        .commands {
            CommandGroup(replacing: .newItem) {}
            CommandGroup(after: .saveItem) { Button("保存当前账目") { if model.page == .home { model.saveEntry() } }.keyboardShortcut("s", modifiers: .command).disabled(!model.ready || model.page != .home) }
        }
    }
}
