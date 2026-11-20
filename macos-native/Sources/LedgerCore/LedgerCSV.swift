import Foundation
import CoreFoundation

public struct LedgerCSVImport {
    public let data: LedgerData
    public let entryCount: Int
    public let newCategoryCount: Int
    public let reactivatedCategoryCount: Int
}

public enum LedgerCSV {
    private static func error(_ line: Int, _ message: String) -> LedgerError {
        LedgerError("CSV 第 \(line) 行：\(message)")
    }
    private static func trimmed(_ value: String) -> String { value.trimmingCharacters(in: .whitespacesAndNewlines) }
    private static func header(_ value: String) -> String? {
        switch trimmed(value).replacingOccurrences(of: "\u{feff}", with: "") {
        case "日期": return "日期"
        case "收支类别", "收支类型", "类型": return "收支类别"
        case "类别", "大类": return "类别"
        case "金额", "金额（元）": return "金额"
        case "备注": return "备注"
        default: return nil
        }
    }
    private static func decode(_ bytes: Data) throws -> String {
        guard bytes.count <= 64_000_000 else { throw LedgerError("CSV 文件不能超过 64 MB。") }
        let value: String?
        if bytes.starts(with: [0xef, 0xbb, 0xbf]) { value = String(data: bytes.dropFirst(3), encoding: .utf8) }
        else if bytes.starts(with: [0xff, 0xfe]) { value = String(data: bytes.dropFirst(2), encoding: .utf16LittleEndian) }
        else if bytes.starts(with: [0xfe, 0xff]) { value = String(data: bytes.dropFirst(2), encoding: .utf16BigEndian) }
        else if let utf8 = String(data: bytes, encoding: .utf8) { value = utf8 }
        else {
            let encoding = String.Encoding(rawValue: CFStringConvertEncodingToNSStringEncoding(CFStringEncoding(CFStringEncodings.GB_18030_2000.rawValue)))
            value = String(data: bytes, encoding: encoding)
        }
        guard let value else { throw LedgerError("无法识别 CSV 编码，请另存为 UTF-8 CSV 后导入。") }
        return value
    }
    private static func delimiter(_ bytes: [UInt8]) -> UInt8 {
        var quoted = false, content = false, commas = 0, tabs = 0, i = 0
        while i < bytes.count {
            let c = bytes[i]
            if c == 34 {
                if quoted && i + 1 < bytes.count && bytes[i + 1] == 34 { content = true; i += 2; continue }
                quoted.toggle()
            } else if quoted { if c > 32 { content = true } }
            else if c == 13 || c == 10 {
                if content { break }
                commas = 0; tabs = 0
            } else if c == 44 { commas += 1 }
            else if c == 9 { tabs += 1 }
            else if c > 32 { content = true }
            i += 1
        }
        return tabs > commas ? 9 : 44
    }
    // Work on UTF-8 bytes so CRLF remains two bytes and quoted newlines retain their exact form.
    private static func records(_ bytes: [UInt8], separator: UInt8, receive: (Int, [String]) throws -> Void) throws {
        var cells: [String] = [], cell: [UInt8] = []
        var quoted = false, closed = false, line = 1, start = 1, i = 0
        while i < bytes.count {
            let c = bytes[i]
            if quoted {
                if c == 34 {
                    if i + 1 < bytes.count && bytes[i + 1] == 34 { cell.append(34); i += 1 }
                    else { quoted = false; closed = true }
                } else {
                    cell.append(c)
                    if c == 10 || (c == 13 && (i + 1 == bytes.count || bytes[i + 1] != 10)) { line += 1 }
                }
            } else if c == separator || c == 13 || c == 10 {
                cells.append(String(decoding: cell, as: UTF8.self)); cell.removeAll(keepingCapacity: true); closed = false
                if c != separator {
                    try receive(start, cells); cells.removeAll(keepingCapacity: true)
                    if c == 13 && i + 1 < bytes.count && bytes[i + 1] == 10 { i += 1 }
                    line += 1; start = line
                }
            } else if closed { throw error(line, "结束引号后只能是列分隔符或换行。") }
            else if c == 34 {
                guard cell.isEmpty else { throw error(line, "字段内的引号须用双引号转义，并用引号包围整个字段。") }
                quoted = true
            } else { cell.append(c) }
            i += 1
        }
        guard !quoted else { throw error(start, "引号未闭合。") }
        if !cell.isEmpty || closed || !cells.isEmpty {
            cells.append(String(decoding: cell, as: UTF8.self)); try receive(start, cells)
        }
    }
    private static func dateKey(_ value: String) -> String? {
        let text = trimmed(value)
        if LedgerDate.parse(text) != nil { return text }
        guard text.range(of: #"^[0-9]{4}年[0-9]{1,2}月[0-9]{1,2}日$"#, options: .regularExpression) != nil else { return nil }
        let components = text.replacingOccurrences(of: "年", with: "-").replacingOccurrences(of: "月", with: "-").replacingOccurrences(of: "日", with: "").split(separator: "-").compactMap { Int($0) }
        guard components.count == 3 else { return nil }
        let key = String(format: "%04d-%02d-%02d", components[0], components[1], components[2])
        return LedgerDate.parse(key) == nil ? nil : key
    }
    private static func decodedCell(_ value: String) -> String {
        if value.hasPrefix("''") { return String(value.dropFirst()) }
        if value.hasPrefix("'"), let first = trimmed(String(value.dropFirst())).first, "=+-@".contains(first) { return String(value.dropFirst()) }
        return value
    }
    public static func prepareImport(_ bytes: Data, into current: LedgerData, today: String = LedgerDate.key()) throws -> LedgerCSVImport {
        try LedgerLogic.validate(current, today: today)
        let text = try decode(bytes), utf8 = Array(text.utf8)
        var next = current, columns: [String: Int] = [:], headerFound = false
        var count = 0, added = 0, reactivated = 0, categories: [String: Int] = [:]
        for (i, category) in next.Categories.enumerated() { categories[category.Kind + "\n" + trimmed(category.Name).lowercased()] = i }
        try records(utf8, separator: delimiter(utf8)) { line, cells in
            if cells.allSatisfy({ trimmed($0).isEmpty }) { return }
            if !headerFound {
                for (i, cell) in cells.enumerated() {
                    guard let name = header(cell) else { continue }
                    guard columns[name] == nil else { throw error(line, "“\(name)”栏目重复。") }
                    columns[name] = i
                }
                for name in ["日期", "收支类别", "类别", "金额"] where columns[name] == nil { throw error(line, "表头缺少“\(name)”栏目。") }
                headerFound = true; return
            }
            func value(_ name: String, optional: Bool = false) throws -> String {
                guard let i = columns[name], i < cells.count else {
                    if optional { return "" }
                    throw error(line, "缺少“\(name)”的值。")
                }
                return cells[i]
            }
            guard let date = dateKey(try value("日期")), date <= today else { throw error(line, "日期须为 2026年1月2日，或旧版 yyyy-MM-dd，且在1900年1月1日至今天之间。") }
            let kindText = trimmed(try value("收支类别"))
            guard kindText == "支出" || kindText == "收入" else { throw error(line, "收支类别只能是“支出”或“收入”。") }
            let kind = kindText == "支出" ? "expense" : "income"
            let name = trimmed(decodedCell(try value("类别")))
            guard !name.isEmpty, name.utf16.count <= 30 else { throw error(line, "类别须为1至30个字符。") }
            let cents: Int64
            do { cents = try Money.cents(try value("金额")) } catch { throw self.error(line, "金额须大于零、最多两位小数且不超过100000000。") }
            let note = decodedCell(try value("备注", optional: true))
            guard note.utf16.count <= 200 else { throw error(line, "备注不能超过200个字符。") }
            let key = kind + "\n" + name.lowercased()
            let categoryIndex: Int
            if let index = categories[key] {
                categoryIndex = index
                if !next.Categories[index].Active { next.Categories[index].Active = true; reactivated += 1 }
            } else {
                guard next.Categories.count < 1000 else { throw error(line, "类别总数不能超过1000个。") }
                categoryIndex = next.Categories.count
                next.Categories.append(LedgerCategory(name: name, kind: kind)); categories[key] = categoryIndex; added += 1
            }
            guard next.Entries.count < 500_000 else { throw error(line, "账目总数不能超过500000笔。") }
            next.Entries.append(LedgerEntry(date: date, kind: kind, category: next.Categories[categoryIndex].Id, cents: cents, note: note)); count += 1
        }
        guard headerFound, count > 0 else { throw LedgerError("CSV 中没有可导入的账目。") }
        try LedgerLogic.validate(next, today: today)
        return LedgerCSVImport(data: next, entryCount: count, newCategoryCount: added, reactivatedCategoryCount: reactivated)
    }
    private static func encodedCell(_ input: String) -> String {
        var value = input
        if input.hasPrefix("'") || trimmed(input).first.map({ "=+-@".contains($0) }) == true { value = "'" + value }
        return "\"" + value.replacingOccurrences(of: "\"", with: "\"\"") + "\""
    }
    public static func export(_ data: LedgerData, month: String? = nil) -> String {
        let rows = data.Entries.filter { month == nil || $0.Date.hasPrefix(month! + "-") }.sorted {
            if $0.Date != $1.Date { return $0.Date < $1.Date }
            if $0.CreatedAt != $1.CreatedAt { return $0.CreatedAt < $1.CreatedAt }
            return $0.Id < $1.Id
        }
        var output = "\u{feff}日期,收支类别,类别,金额,备注\r\n"
        for entry in rows {
            let parts = entry.Date.split(separator: "-").compactMap { Int($0) }
            let date = parts.count == 3 ? String(format: "%04d年%d月%d日", parts[0], parts[1], parts[2]) : entry.Date
            output += [date, entry.Kind == "expense" ? "支出" : "收入", data.categoryName(entry.CategoryId), Money.input(entry.cents), entry.Note].map(encodedCell).joined(separator: ",") + "\r\n"
        }
        return output
    }
}