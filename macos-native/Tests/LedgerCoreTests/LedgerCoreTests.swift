import XCTest
@testable import LedgerCore

final class LedgerCoreTests: XCTestCase {
    let today = "2024-03-12"
    func sample() -> LedgerData {
        var data = LedgerData()
        let expense = data.Categories.first { $0.Kind == "expense" }!.Id, income = data.Categories.first { $0.Kind == "income" }!.Id
        data.Entries = [LedgerEntry(date: "2024-03-01", kind: "expense", category: expense, cents: 10, note: "测试"), LedgerEntry(date: "2024-03-01", kind: "expense", category: expense, cents: 20, note: ""), LedgerEntry(date: "2024-03-12", kind: "expense", category: expense, cents: 2000, note: ""), LedgerEntry(date: "2024-03-12", kind: "income", category: income, cents: 10000, note: ""), LedgerEntry(date: "2024-02-29", kind: "expense", category: expense, cents: 2900, note: "")]
        data.Budgets["2024-02"] = 100; return data
    }
    func testCentsAreExact() throws { XCTAssertEqual(try Money.cents("0.29"), 29); XCTAssertEqual(try Money.cents("0.1"), 10); XCTAssertEqual(try Money.cents("100000000.00"), 10_000_000_000) }
    func testInvalidMoney() { for text in ["-1", "1e3", "1.001", "", "NaN", "100000000.01", "0"] { XCTAssertThrowsError(try Money.cents(text)) } }
    func testZeroBudget() throws { XCTAssertEqual(try Money.cents("0", allowZero: true), 0) }
    func testLargeTotalFormatting() { XCTAssertEqual(Money.display(5_000_000_000_000_001), "¥50,000,000,000,000.01") }
    func testInvalidDates() { XCTAssertNotNil(LedgerDate.parse("2024-02-29")); XCTAssertNil(LedgerDate.parse("2025-02-29")); XCTAssertNil(LedgerDate.parse("2024-04-31")); XCTAssertNil(LedgerDate.parse("1899-12-31")) }
    func testCrossYear() { XCTAssertEqual(LedgerDate.shifted("2024-01", by: -1), "2023-12"); XCTAssertEqual(LedgerDate.shifted("2024-12", by: 1), "2025-01") }
    func testBlankLedger() throws { let data = LedgerData(); XCTAssertTrue(data.Entries.isEmpty); XCTAssertTrue(data.Budgets.isEmpty); try LedgerLogic.validate(data) }
    func testWindowsSchemaAndDecimalJSON() throws { let data = sample(); let bytes = try LedgerLogic.encode(data); XCTAssertEqual(try LedgerLogic.decode(bytes), data); XCTAssertTrue(String(decoding: bytes, as: UTF8.self).contains("\"Version\"")) }
    func testWindowsBOM() throws { var bytes = Data([0xef, 0xbb, 0xbf]); bytes.append(try LedgerLogic.encode(sample())); XCTAssertEqual(try LedgerLogic.decode(bytes).Entries[0].cents, 10) }
    func testUnknownVersion() { var data = sample(); data.Version = 2; XCTAssertThrowsError(try LedgerLogic.validate(data)) }
    func testDuplicateIDs() { var data = sample(); data.Entries[1].Id = data.Entries[0].Id; XCTAssertThrowsError(try LedgerLogic.validate(data)) }
    func testWrongCategory() { var data = sample(); data.Entries[0].CategoryId = data.Categories.first { $0.Kind == "income" }!.Id; XCTAssertThrowsError(try LedgerLogic.validate(data)) }
    func testDuplicateCategoryName() { var data = sample(); data.Categories[1].Name = data.Categories[0].Name; XCTAssertThrowsError(try LedgerLogic.validate(data)) }
    func testActiveCategoryRequired() { var data = sample(); for i in data.Categories.indices where data.Categories[i].Kind == "income" { data.Categories[i].Active = false }; XCTAssertThrowsError(try LedgerLogic.validate(data)) }
    func testFutureEntryRejected() { var data = sample(); data.Entries[0].Date = "2024-03-13"; XCTAssertThrowsError(try LedgerLogic.validate(data, today: today)) }
    func testInvalidBudget() { var data = sample(); data.Budgets["2024-13"] = 3; XCTAssertThrowsError(try LedgerLogic.validate(data)) }
    func testInvalidDecimalPrecision() { var data = sample(); data.Entries[0].Amount = Decimal(string: "0.001")!; XCTAssertThrowsError(try LedgerLogic.validate(data)) }
    func testIncomeSeparated() { let s = LedgerLogic.summary(sample(), month: "2024-03", today: today); XCTAssertEqual(s.spent, 2030); XCTAssertEqual(s.income, 10000); XCTAssertEqual(s.net, 7970) }
    func testNaturalDayAverage() { XCTAssertEqual(LedgerLogic.summary(sample(), month: "2024-03", today: today).average, 169) }
    func testSameDateCutoff() { XCTAssertEqual(LedgerLogic.summary(sample(), month: "2024-03", today: today).previousSpent, 0) }
    func testShortPreviousMonth() { let s = LedgerLogic.summary(sample(), month: "2024-03", today: "2024-03-31"); XCTAssertEqual(s.previousSpent, 2900); XCTAssertEqual(s.remainingDays, 0) }
    func testHistoricalSummary() { XCTAssertEqual(LedgerLogic.summary(sample(), month: "2024-02", today: today).spent, 2900) }
    func testBudgetInheritance() { var d = sample(); XCTAssertNil(LedgerLogic.budget(d, month: "2024-01")); LedgerLogic.inheritBudget(&d, month: "2024-03"); d.Budgets["2024-02"] = 200; LedgerLogic.inheritBudget(&d, month: "2024-03"); XCTAssertEqual(d.Budgets["2024-03"], 100) }
    func testZeroBudgetOverspend() { var d = sample(); d.Budgets["2024-03"] = 0; XCTAssertEqual(LedgerLogic.summary(d, month: "2024-03", today: today).remaining, -2030) }
    func testDailySumAndCount() { let t = LedgerLogic.trends(sample(), month: "2024-03", monthly: false, today: today); XCTAssertEqual(t.current[0]?.cents, 30); XCTAssertEqual(t.current[0]?.count, 2); XCTAssertEqual(t.total, 2030) }
    func testEmptyAndFutureDays() { let t = LedgerLogic.trends(sample(), month: "2024-03", monthly: false, today: today); XCTAssertEqual(t.current[1]?.cents, 0); XCTAssertNil(t.current[12]) }
    func testLeapComparison() { let t = LedgerLogic.trends(sample(), month: "2024-03", monthly: false, today: today); XCTAssertEqual(t.previous[28]?.cents, 2900); XCTAssertNil(t.previous[29]) }
    func testThirtyDayMonth() { let t = LedgerLogic.trends(sample(), month: "2024-04", monthly: false, today: "2024-04-30"); XCTAssertEqual(t.current.count, 31); XCTAssertNil(t.current[30]); XCTAssertNotNil(t.previous[30]) }
    func testMonthlyAggregation() { let t = LedgerLogic.trends(sample(), month: "2024-03", monthly: true, today: today); XCTAssertEqual(t.current[2]?.cents, 2030); XCTAssertNil(t.current[3]) }
    func testPreviousYear() { var d = sample(); d.Entries[0].Date = "2023-03-01"; XCTAssertEqual(LedgerLogic.trends(d, month: "2024-03", monthly: true, today: today).previous[2]?.cents, 10) }
    func testHistoricalYear() { XCTAssertTrue(LedgerLogic.trends(sample(), month: "2023-01", monthly: true, today: today).current.allSatisfy { $0 != nil }) }
    func testTrendDoesNotMutateData() { let d = sample(), before = d; _ = LedgerLogic.trends(d, month: "2024-03", monthly: false, today: today); XCTAssertEqual(d, before) }
    func testEditAndDeleteRecompute() { var d = sample(); d.Entries[0].Amount = Decimal(string: "0.50")!; XCTAssertEqual(LedgerLogic.trends(d, month: "2024-03", monthly: false, today: today).current[0]?.cents, 70); d.Entries.removeFirst(); XCTAssertEqual(LedgerLogic.trends(d, month: "2024-03", monthly: false, today: today).current[0]?.cents, 20) }
    func testCSVFormulaAndQuoting() { var d = sample(); d.Entries[0].Note = "=SUM(1,2)"; let csv = LedgerLogic.csv(d, month: "2024-03"); XCTAssertTrue(csv.hasPrefix("\u{feff}")); XCTAssertTrue(csv.contains("\"'=SUM(1,2)\"")) }
    func testStoreAndBackups() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: dir) }
        let store = try LedgerFileStore(directory: dir); XCTAssertTrue(try store.load().Entries.isEmpty)
        try store.save(sample()); var d = sample(); d.Entries[0].Amount = Decimal(string: "0.29")!; try store.save(d)
        XCTAssertEqual(try store.load().Entries[0].cents, 29)
        let backups = try FileManager.default.contentsOfDirectory(at: store.backups, includingPropertiesForKeys: nil)
        XCTAssertEqual(backups.count, 1); XCTAssertEqual(try store.read(backups[0]).Entries[0].cents, 10)
    }
    func testInvalidSavePreservesOriginal() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: dir) }
        let store = try LedgerFileStore(directory: dir); try store.save(sample()); let before = try Data(contentsOf: store.file)
        var d = sample(); d.Entries[0].Amount = -1; XCTAssertThrowsError(try store.save(d)); XCTAssertEqual(try Data(contentsOf: store.file), before)
    }
    func testCorruptionPreservedOnRecovery() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: dir) }
        let store = try LedgerFileStore(directory: dir); try Data("broken".utf8).write(to: store.file)
        XCTAssertThrowsError(try store.load()); XCTAssertEqual(try String(contentsOf: store.file), "broken")
        try store.save(sample()); let files = try FileManager.default.contentsOfDirectory(at: store.backups, includingPropertiesForKeys: nil)
        XCTAssertEqual(try String(contentsOf: files[0]), "broken")
    }
}
