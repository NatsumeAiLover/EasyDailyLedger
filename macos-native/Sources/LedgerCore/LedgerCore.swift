import Foundation

public struct LedgerError: LocalizedError {
    public let message: String
    public init(_ message: String) { self.message = message }
    public var errorDescription: String? { message }
}

public struct LedgerCategory: Codable, Identifiable, Hashable {
    public var Id: String
    public var Name: String
    public var Kind: String
    public var Active: Bool
    public var id: String { Id }
    public init(name: String, kind: String) {
        Id = UUID().uuidString.replacingOccurrences(of: "-", with: "")
        Name = name; Kind = kind; Active = true
    }
}

public struct LedgerEntry: Codable, Identifiable, Hashable {
    public var Id: String
    public var Date: String
    public var Kind: String
    public var CategoryId: String
    public var Amount: Decimal
    public var Note: String
    public var CreatedAt: String
    public var id: String { Id }
    public var cents: Int64 { NSDecimalNumber(decimal: Amount * 100).int64Value }
    public init(date: String, kind: String, category: String, cents: Int64, note: String) {
        Id = UUID().uuidString.replacingOccurrences(of: "-", with: "")
        Date = date; Kind = kind; CategoryId = category
        Amount = Decimal(cents) / 100; Note = note
        CreatedAt = ISO8601DateFormatter().string(from: Foundation.Date())
    }
}

public struct LedgerData: Codable, Equatable {
    public var Version: Int = 1
    public var Categories: [LedgerCategory] = []
    public var Entries: [LedgerEntry] = []
    public var Budgets: [String: Decimal] = [:]
    public init() {
        Categories = ["餐饮", "交通", "购物", "日用", "居住", "学习", "医疗", "娱乐", "其他"].map { LedgerCategory(name: $0, kind: "expense") }
        Categories += ["生活费", "工资 / 兼职", "奖助学金", "其他"].map { LedgerCategory(name: $0, kind: "income") }
    }
    public func categoryName(_ id: String) -> String { Categories.first { $0.Id == id }?.Name ?? "未知分类" }
}

public enum LedgerDate {
    private static var calendar: Calendar { Calendar(identifier: .gregorian) }
    public static func key(_ date: Date = Date()) -> String {
        let c = calendar.dateComponents([.year, .month, .day], from: date)
        return String(format: "%04d-%02d-%02d", c.year!, c.month!, c.day!)
    }
    public static func parse(_ value: String) -> Date? {
        let parts = value.split(separator: "-", omittingEmptySubsequences: false)
        guard parts.count == 3, parts[0].count == 4, parts[1].count == 2, parts[2].count == 2,
              let y = Int(parts[0]), let m = Int(parts[1]), let d = Int(parts[2]), y >= 1900, y <= 9999,
              let date = calendar.date(from: DateComponents(year: y, month: m, day: d, hour: 12)), key(date) == value else { return nil }
        return date
    }
    public static func month(_ date: Date = Date()) -> String { String(key(date).prefix(7)) }
    public static func validMonth(_ value: String) -> Bool { parse(value + "-01") != nil }
    public static func shifted(_ month: String, by offset: Int) -> String {
        // 1899 is needed only for comparison with January 1900.
        let parts = month.split(separator: "-").compactMap { Int($0) }
        guard parts.count == 2, let start = calendar.date(from: DateComponents(year: parts[0], month: parts[1], day: 1, hour: 12)),
              let shifted = calendar.date(byAdding: .month, value: offset, to: start) else { return month }
        return self.month(shifted)
    }
    public static func days(_ month: String) -> Int {
        let parts = month.split(separator: "-").compactMap { Int($0) }
        guard parts.count == 2, let date = calendar.date(from: DateComponents(year: parts[0], month: parts[1], day: 1, hour: 12)) else { return 0 }
        return calendar.range(of: .day, in: .month, for: date)?.count ?? 0
    }
    public static func label(_ month: String) -> String {
        let p = month.split(separator: "-")
        return p.count == 2 ? "\(p[0]) 年 \(Int(p[1]) ?? 1) 月" : month
    }
}

public enum Money {
    public static func cents(_ text: String, allowZero: Bool = false) throws -> Int64 {
        let input = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard input.range(of: #"^[0-9]+(\.[0-9]{1,2})?$"#, options: .regularExpression) != nil,
              let amount = Decimal(string: input, locale: Locale(identifier: "en_US_POSIX")) else { throw LedgerError("请输入最多两位小数的有效金额。") }
        return try cents(amount, allowZero: allowZero)
    }
    public static func cents(_ amount: Decimal, allowZero: Bool = false) throws -> Int64 {
        let scaled = amount * 100
        var source = scaled, rounded = Decimal()
        NSDecimalRound(&rounded, &source, 0, .plain)
        guard !amount.isNaN, scaled == rounded, amount >= (allowZero ? 0 : Decimal(string: "0.01")!), amount <= 100_000_000 else {
            throw LedgerError("金额须在 \(allowZero ? "0" : "0.01") 至 1 亿元之间，最多两位小数。")
        }
        return NSDecimalNumber(decimal: scaled).int64Value
    }
    public static func display(_ cents: Int64) -> String {
        let n = abs(cents), formatter = NumberFormatter()
        formatter.numberStyle = .decimal; formatter.maximumFractionDigits = 0; formatter.locale = Locale(identifier: "zh_CN")
        return (cents < 0 ? "−" : "") + "¥" + (formatter.string(from: NSNumber(value: n / 100)) ?? String(n / 100)) + String(format: ".%02lld", n % 100)
    }
    public static func input(_ cents: Int64) -> String { "\(cents / 100)" + String(format: ".%02lld", abs(cents % 100)) }
}

public enum LedgerLogic {
    public static func validate(_ data: LedgerData, today: String = LedgerDate.key()) throws {
        guard data.Version == 1, data.Categories.count <= 1000, data.Entries.count <= 500_000, data.Budgets.count <= 12_000 else { throw LedgerError("账本版本或规模不支持。") }
        var categories: [String: LedgerCategory] = [:], names = Set<String>(), ids = Set<String>()
        for c in data.Categories {
            guard !c.Id.trimmingCharacters(in: .whitespaces).isEmpty, c.Id.count <= 100, categories[c.Id] == nil,
                  !c.Name.trimmingCharacters(in: .whitespaces).isEmpty, c.Name.count <= 30, ["expense", "income"].contains(c.Kind) else { throw LedgerError("分类数据不正确。") }
            let name = c.Kind + ":" + c.Name.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
            guard names.insert(name).inserted else { throw LedgerError("同一类型的分类名称不能重复。") }
            categories[c.Id] = c
        }
        for kind in ["expense", "income"] {
            guard data.Categories.contains(where: { $0.Kind == kind && $0.Active }) else { throw LedgerError("收入和支出各需至少一个启用的大类。") }
        }
        for e in data.Entries {
            guard !e.Id.isEmpty, e.Id.count <= 100, ids.insert(e.Id).inserted, LedgerDate.parse(e.Date) != nil, e.Date <= today,
                  ["expense", "income"].contains(e.Kind), e.Note.count <= 200, let c = categories[e.CategoryId], c.Kind == e.Kind else { throw LedgerError("账目日期、内容或分类不正确。") }
            _ = try Money.cents(e.Amount)
        }
        for (month, amount) in data.Budgets {
            guard LedgerDate.validMonth(month) else { throw LedgerError("预算月份不正确。") }
            _ = try Money.cents(amount, allowZero: true)
        }
    }
    public static func decode(_ bytes: Data) throws -> LedgerData {
        guard bytes.count <= 64_000_000 else { throw LedgerError("文件过大。") }
        let bom = Data([0xef, 0xbb, 0xbf]), body = bytes.starts(with: bom) ? bytes.dropFirst(3) : bytes[...]
        let data = try JSONDecoder().decode(LedgerData.self, from: Data(body))
        try validate(data); return data
    }
    public static func encode(_ data: LedgerData) throws -> Data {
        try validate(data)
        let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        return try encoder.encode(data)
    }
    public static func budget(_ data: LedgerData, month: String) -> Int64? {
        guard let key = data.Budgets.keys.filter({ $0 <= month }).max(), let amount = data.Budgets[key] else { return nil }
        return try? Money.cents(amount, allowZero: true)
    }
    public static func inheritBudget(_ data: inout LedgerData, month: String) {
        if data.Budgets[month] == nil, let budget = budget(data, month: month) { data.Budgets[month] = Decimal(budget) / 100 }
    }
    public static func summary(_ data: LedgerData, month: String, today: String = LedgerDate.key()) -> MonthSummary {
        let current = String(today.prefix(7)), elapsed = month < current ? LedgerDate.days(month) : month == current ? Int(today.suffix(2))! : 0
        let rows = data.Entries.filter { $0.Date.hasPrefix(month + "-") && (Int($0.Date.suffix(2)) ?? 32) <= elapsed }
        let spent = rows.filter { $0.Kind == "expense" }.reduce(Int64(0)) { $0 + $1.cents }
        let income = rows.filter { $0.Kind == "income" }.reduce(Int64(0)) { $0 + $1.cents }
        let previous = LedgerDate.shifted(month, by: -1)
        let cutoff = month == current ? min(elapsed, LedgerDate.days(previous)) : LedgerDate.days(previous)
        let before = data.Entries.filter { $0.Kind == "expense" && $0.Date.hasPrefix(previous + "-") && (Int($0.Date.suffix(2)) ?? 32) <= cutoff }
        return MonthSummary(spent: spent, income: income, budget: budget(data, month: month), elapsed: elapsed,
                            remainingDays: month == current ? LedgerDate.days(month) - elapsed : 0,
                            previousSpent: before.reduce(0) { $0 + $1.cents }, hasPrevious: !before.isEmpty)
    }
    public static func trends(_ data: LedgerData, month: String, monthly: Bool, today: String = LedgerDate.key()) -> ExpenseTrend {
        let start = monthly ? String(month.prefix(4)) + "-01" : month
        let previous = LedgerDate.shifted(start, by: monthly ? -12 : -1)
        let slots = monthly ? 12 : max(LedgerDate.days(start), LedgerDate.days(previous))
        var totals: [String: (Int64, Int)] = [:]
        for e in data.Entries where e.Kind == "expense" && e.Date <= today {
            let key = monthly ? String(e.Date.prefix(7)) : e.Date
            let old = totals[key] ?? (0, 0); totals[key] = (old.0 + e.cents, old.1 + 1)
        }
        func series(_ period: String) -> [ExpensePoint?] {
            (0..<slots).map { index in
                if !monthly && index >= LedgerDate.days(period) { return nil }
                let key = monthly ? LedgerDate.shifted(period, by: index) : period + String(format: "-%02d", index + 1)
                let date = monthly ? key + "-01" : key
                guard date <= today else { return nil }
                let value = totals[key] ?? (0, 0)
                return ExpensePoint(index: index + 1, date: date, cents: value.0, count: value.1)
            }
        }
        return ExpenseTrend(period: start, previousPeriod: previous, monthly: monthly, current: series(start), previous: series(previous))
    }
    public static func csv(_ data: LedgerData, month: String) -> String {
        func cell(_ input: String) -> String {
            var value = input
            if let first = input.trimmingCharacters(in: .whitespacesAndNewlines).first, "=+-@".contains(first) { value = "'" + value }
            return "\"" + value.replacingOccurrences(of: "\"", with: "\"\"") + "\""
        }
        let lines = data.Entries.filter { $0.Date.hasPrefix(month + "-") }.sorted { $0.Date < $1.Date }.map { e in
            [e.Date, e.Kind == "expense" ? "支出" : "收入", data.categoryName(e.CategoryId), Money.input(e.cents), e.Note].map(cell).joined(separator: ",")
        }
        return "\u{feff}日期,类型,大类,金额（元）,备注\r\n" + lines.joined(separator: "\r\n")
    }
}

public struct MonthSummary {
    public let spent: Int64, income: Int64, budget: Int64?, elapsed: Int, remainingDays: Int, previousSpent: Int64, hasPrevious: Bool
    public var net: Int64 { income - spent }
    public var remaining: Int64? { budget.map { $0 - spent } }
    public var average: Int64 { elapsed > 0 ? (spent + Int64(elapsed / 2)) / Int64(elapsed) : 0 }
}
public struct ExpensePoint: Identifiable, Equatable {
    public let index: Int, date: String, cents: Int64, count: Int
    public var id: Int { index }
}
public struct ExpenseTrend {
    public let period: String, previousPeriod: String, monthly: Bool
    public let current: [ExpensePoint?], previous: [ExpensePoint?]
    public var total: Int64 { current.compactMap { $0 }.reduce(0) { $0 + $1.cents } }
    public var peak: ExpensePoint? { current.compactMap { $0 }.filter { $0.cents > 0 }.sorted { $0.cents == $1.cents ? $0.index < $1.index : $0.cents > $1.cents }.first }
    public var hasPrevious: Bool { previous.contains { ($0?.count ?? 0) > 0 } }
}

public final class LedgerFileStore {
    public let directory: URL
    public var file: URL { directory.appendingPathComponent("账本.json") }
    public var backups: URL { directory.appendingPathComponent("自动备份", isDirectory: true) }
    public init(directory: URL) throws { self.directory = directory; try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true) }
    public func read(_ url: URL) throws -> LedgerData {
        let size = try url.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0
        guard size <= 64_000_000 else { throw LedgerError("文件过大。") }
        return try LedgerLogic.decode(Data(contentsOf: url))
    }
    public func load() throws -> LedgerData {
        if FileManager.default.fileExists(atPath: file.path) { return try read(file) }
        return LedgerData()
    }
    public func save(_ data: LedgerData) throws {
        let bytes = try LedgerLogic.encode(data), fm = FileManager.default
        try fm.createDirectory(at: backups, withIntermediateDirectories: true)
        if fm.fileExists(atPath: file.path) {
            let name = "账本-\(Int64(Date().timeIntervalSince1970 * 1000))-\(UUID().uuidString).json"
            try fm.copyItem(at: file, to: backups.appendingPathComponent(name))
        }
        try bytes.write(to: file, options: .atomic)
    }
}
