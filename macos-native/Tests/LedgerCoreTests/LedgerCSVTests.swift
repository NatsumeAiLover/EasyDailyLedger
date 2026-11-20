import XCTest
import CoreFoundation
@testable import LedgerCore

final class LedgerCSVTests: XCTestCase {
    let today = "2026-11-20"
    let header = "日期,收支类别,类别,金额,备注\r\n"
    func imported(_ text: String, into data: LedgerData = LedgerData()) throws -> LedgerCSVImport {
        try LedgerCSV.prepareImport(Data(text.utf8), into: data, today: today)
    }
    func row(_ note: String = "") -> String { "2024年2月29日,支出,餐饮,12.30," + note + "\r\n" }
    func canonical(_ data: LedgerData) -> [String] {
        data.Entries.map { [$0.Date, $0.Kind, data.categoryName($0.CategoryId), Money.input($0.cents), $0.Note].joined(separator: "\0") }.sorted()
    }
    func testCanonicalColumnsAndExactCents() throws {
        let result = try imported(header + row())
        XCTAssertEqual(result.entryCount, 1); XCTAssertEqual(result.data.Entries[0].Date, "2024-02-29"); XCTAssertEqual(result.data.Entries[0].cents, 1230)
    }
    func testColumnReorderingAndAccountIgnored() throws {
        let result = try imported("账户,金额,收支类别,日期,备注,类别,无关\n任意账户,1.01,收入,2024年3月1日,备注内容,新类别,忽略\n")
        XCTAssertEqual(result.entryCount, 1); XCTAssertEqual(result.data.Entries[0].Note, "备注内容"); XCTAssertEqual(result.data.Entries[0].cents, 101)
    }
    func testUTF16TabSeparatedCSVWithAlias() throws {
        let text = "日期\t收支类型\t类别\t账户\t金额\t备注\n2021年09月02日\t支出\t新类别\t未关联\t30\t\n"
        var bytes = Data([0xff, 0xfe]); bytes.append(text.data(using: .utf16LittleEndian)!)
        let result = try LedgerCSV.prepareImport(bytes, into: LedgerData(), today: today)
        XCTAssertEqual(result.data.Entries[0].cents, 3000); XCTAssertEqual(result.newCategoryCount, 1); XCTAssertEqual(result.data.Entries[0].Note, "")
    }
    func testUTF16BigEndian() throws {
        var bytes = Data([0xfe, 0xff]); bytes.append((header + row("中文")).data(using: .utf16BigEndian)!)
        XCTAssertEqual(try LedgerCSV.prepareImport(bytes, into: LedgerData(), today: today).data.Entries[0].Note, "中文")
    }
    func testGBKEncodingFromIndependentFixture() throws {
        let bytes = Data(base64Encoded: "yNXG2izK1danwODQzSzA4LHwLL3wtu4ssbjXogoyMDI0xOox1MIxyNUs1qez9izQwsDgsfAsMSzW0M7EsbjXogo=")!
        let result = try LedgerCSV.prepareImport(bytes, into: LedgerData(), today: today)
        XCTAssertEqual(result.data.Entries[0].Note, "中文备注"); XCTAssertEqual(result.data.categoryName(result.data.Entries[0].CategoryId), "新类别")
    }
    func testUTF8BOMAndWithoutBOM() throws {
        let text = header + row("中文备注")
        XCTAssertEqual(try imported(text).entryCount, 1)
        XCTAssertEqual(try imported("\u{feff}" + text).data.Entries[0].Note, "中文备注")
    }
    func testDateFormats() throws {
        for date in ["2024年2月9日", "2024年02月09日", "2024年2月09日", "2024年02月9日", "2024-02-09"] {
            XCTAssertEqual(try imported(header + "\(date),支出,餐饮,1,\n").data.Entries[0].Date, "2024-02-09")
        }
    }
    func testOldWindowsHeadersAndDate() throws {
        XCTAssertEqual(try imported("日期,类型,大类,金额（元）,备注\n2024-02-29,支出,餐饮,1,旧版\n").data.Entries[0].Note, "旧版")
    }
    func testSameCategoryNameSeparateKinds() throws {
        let result = try imported(header + "2024年1月1日,支出,新类别,1,\n2024年1月2日,收入,新类别,2,收入备注\n2024年1月3日,支出,新类别,3,\n")
        XCTAssertEqual(result.newCategoryCount, 2); XCTAssertEqual(result.data.Categories.filter { $0.Name == "新类别" }.count, 2)
        XCTAssertEqual(result.data.Entries[0].CategoryId, result.data.Entries[2].CategoryId); XCTAssertEqual(result.data.Entries[1].Note, "收入备注")
    }
    func testReactivationDoesNotDuplicateCategory() throws {
        var data = LedgerData(); let i = data.Categories.firstIndex { $0.Name == "餐饮" }!; data.Categories[i].Active = false
        let result = try imported(header + row() + row(), into: data)
        XCTAssertEqual(result.reactivatedCategoryCount, 1); XCTAssertEqual(result.newCategoryCount, 0); XCTAssertTrue(result.data.Categories[i].Active); XCTAssertFalse(data.Categories[i].Active)
    }
    func testMissingAndEmptyRemark() throws {
        XCTAssertEqual(try imported("日期,收支类型,类别,金额\n2024年1月1日,支出,餐饮,1").data.Entries[0].Note, "")
        XCTAssertEqual(try imported(header + row()).data.Entries[0].Note, "")
    }
    func testQuotedCommaQuoteTabAndMultilineRemark() throws {
        let result = try imported(header + row("\"带,逗号\t制表符\"\"引号\r\n下一行\""))
        XCTAssertEqual(result.data.Entries[0].Note, "带,逗号\t制表符\"引号\r\n下一行")
    }
    func testQuotedTabHeaderAndEmptyRecords() throws {
        let text = "\r\n\t\t\n\"日期\"\t\"收支类型\"\t\"类别\"\t\"金额\"\t\"备注\"\t\"其他,列\"\n2024年1月1日\t收入\t餐饮\t1\t\"逗号,\t制表符\"\t忽略\n"
        XCTAssertEqual(try imported(text).data.Entries[0].Note, "逗号,\t制表符")
    }
    func testMissingDuplicateAndAliasedHeadersRejected() {
        for text in ["日期,类别,金额\n", "日期,收支类别,收支类型,类别,金额\n", "日期,收支类别,类别,金额,金额\n"] { XCTAssertThrowsError(try imported(text)) }
    }
    func testInvalidDatesKindsAndMoneyRejected() {
        for date in ["2025年2月29日", "2024年4月31日", "1899年12月31日", "2026年11月21日"] { XCTAssertThrowsError(try imported(header + "\(date),支出,餐饮,1,\n")) }
        XCTAssertThrowsError(try imported(header + "2024年1月1日,转账,餐饮,1,\n"))
        for money in ["0", "-1", "1.001", "1e3", "NaN", "100000000.01", ""] { XCTAssertThrowsError(try imported(header + "2024年1月1日,支出,餐饮,\(money),\n")) }
        XCTAssertEqual(try? imported(header + "2024年1月1日,支出,餐饮,100000000.00,\n").data.Entries[0].cents, 10_000_000_000)
    }
    func testEmptyAndOverlongFieldsRejected() {
        XCTAssertThrowsError(try imported(header + "2024年1月1日,支出,,1,\n"))
        XCTAssertThrowsError(try imported(header + "2024年1月1日,支出,\(String(repeating: "类", count: 31)),1,\n"))
        XCTAssertThrowsError(try imported(header + row(String(repeating: "备", count: 201))))
        XCTAssertThrowsError(try imported(header + row(String(repeating: "😀", count: 101))))
        XCTAssertThrowsError(try imported(header + "2024年1月1日,支出,餐饮\n"))
    }
    func testMalformedQuotingAndEmptyFileRejected() {
        for text in ["", header, header + row("\"未闭合"), header + row("\"闭合\"x"), header + row("错\"误")] { XCTAssertThrowsError(try imported(text)) }
    }
    func testErrorPhysicalLineAfterMultiline() {
        let text = header + row("\"跨\n行\"") + "错误日期,支出,餐饮,1,\n"
        XCTAssertThrowsError(try imported(text)) { XCTAssertTrue($0.localizedDescription.contains("第 4 行")) }
    }
    func testPreviewAndInvalidFileDoNotMutateExistingLedger() throws {
        var data = LedgerData(); data.Budgets["2024-01"] = 500; let before = data
        _ = try imported(header + "2024年1月1日,支出,新增分类,1,\n", into: data)
        XCTAssertEqual(data, before)
        XCTAssertThrowsError(try imported(header + "2024年1月1日,支出,新增分类,1,\n错误,支出,餐饮,1,\n", into: data))
        XCTAssertEqual(data, before)
    }
    func testAppendKeepsBudgetAndRepeatedImportAppends() throws {
        var data = LedgerData(); data.Budgets["2024-01"] = 500
        let first = try imported(header + row(), into: data), second = try imported(header + row(), into: first.data)
        XCTAssertEqual(second.data.Entries.count, 2); XCTAssertEqual(second.data.Budgets, data.Budgets); XCTAssertEqual(second.newCategoryCount, 0)
    }
    func testExportContractAllMonthsAndMonthScope() throws {
        let data = try imported(header + row() + "2024年3月1日,收入,其他,20,\n").data
        let csv = LedgerLogic.csv(data)
        XCTAssertTrue(csv.hasPrefix("\u{feff}日期,收支类别,类别,金额,备注\r\n")); XCTAssertTrue(csv.contains("2024年2月29日"))
        XCTAssertEqual(try imported(csv).entryCount, 2); XCTAssertEqual(try imported(LedgerLogic.csv(data, month: "2024-03")).entryCount, 1)
        XCTAssertFalse(LedgerLogic.csv(data, month: "2024-03").contains("2024年2月29日"))
    }
    func testFormulaEscapesAndApostrophesRoundTrip() throws {
        var data = LedgerData()
        let category = LedgerCategory(name: "=新分类", kind: "expense"); data.Categories.append(category)
        for note in ["=SUM(1,2)\r\n备注\"", " @SUM(1)", "'原备注", "''两个单引号", "-文字", "+文字", ""] { data.Entries.append(LedgerEntry(date: "2024-02-29", kind: "expense", category: category.Id, cents: 1230, note: note)) }
        let csv = LedgerLogic.csv(data)
        XCTAssertTrue(csv.contains("'=SUM(1,2)")); XCTAssertEqual(canonical(try imported(csv).data), canonical(data))
    }
    func testImportPersistenceAndPreImportBackup() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let store = try LedgerFileStore(directory: directory); let original = LedgerData(); try store.save(original)
        let preview = try imported(header + row(), into: original)
        XCTAssertTrue(try store.load().Entries.isEmpty)
        try store.save(preview.data); XCTAssertEqual(canonical(try store.load()), canonical(preview.data))
        let backups = try FileManager.default.contentsOfDirectory(at: store.backups, includingPropertiesForKeys: nil)
        XCTAssertEqual(backups.count, 1); XCTAssertTrue(try store.read(backups[0]).Entries.isEmpty)
    }
    func testFailedWriteKeepsSavedData() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let store = try LedgerFileStore(directory: directory); try store.save(LedgerData()); let original = try Data(contentsOf: store.file)
        try FileManager.default.removeItem(at: store.backups); try Data("block backup directory".utf8).write(to: store.backups)
        XCTAssertThrowsError(try store.save(imported(header + row()).data)); XCTAssertEqual(try Data(contentsOf: store.file), original)
    }
    func testCategoryLimitIsCheckedBeforeAdding() throws {
        var data = LedgerData()
        while data.Categories.count < 1000 { data.Categories.append(LedgerCategory(name: "类别\(data.Categories.count)", kind: "expense")) }
        XCTAssertThrowsError(try imported(header + "2024年1月1日,支出,超过上限的新类,1,\n", into: data))
    }
}